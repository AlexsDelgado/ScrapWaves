using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class EnemyDeathReactionVfx : MonoBehaviour
{
    private sealed class SnapshotPiece
    {
        public string Name;
        public Mesh Mesh;
        public bool OwnsMesh;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Material[] Materials;
        public int Layer;
    }

    private sealed class DeathSnapshot
    {
        public readonly List<SnapshotPiece> Pieces = new();

        public void Dispose()
        {
            for (int i = 0; i < Pieces.Count; i++)
            {
                SnapshotPiece piece = Pieces[i];
                if (piece != null && piece.OwnsMesh && piece.Mesh != null)
                    DestroySafely(piece.Mesh);
            }
            Pieces.Clear();
        }
    }

    private struct PendingDeath
    {
        public int Id;
        public Vector3 Position;
        public Vector3 Direction;
        public float Radius;
        public Color Color;
        public WeaponStatusMask Statuses;
        public float Intensity;
        public EnemyReactionProfile Profile;
        public bool Critical;
        public WeaponType WeaponType;
        public int Frame;
        public DeathSnapshot Snapshot;
    }

    private const int ShardCount = 14;
    private const int RingSegments = 36;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int AshColorId = Shader.PropertyToID("_AshColor");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int EffectCenterId = Shader.PropertyToID("_EffectCenter");
    private static readonly int EffectHeightId = Shader.PropertyToID("_EffectHeight");
    private static readonly int EffectRadiusId = Shader.PropertyToID("_EffectRadius");
    private static readonly int EffectDirectionId = Shader.PropertyToID("_EffectDirection");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int LuminescenceId = Shader.PropertyToID("_Luminescence");
    private static readonly Dictionary<int, PendingDeath> s_pending = new();
    private static readonly Stack<EnemyDeathReactionVfx> s_pool = new();
    private static readonly HashSet<EnemyDeathReactionVfx> s_active = new();
    private static Transform s_root;
    private static Material s_material;
    private static Shader s_disintegrationShader;
    private static EnemyDeathReactionRunner s_runner;

    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly Vector3[] _spiralSeeds = new Vector3[ShardCount];
    private readonly List<Renderer> _snapshotRenderers = new();
    private readonly List<GameObject> _snapshotObjects = new();
    private readonly List<Material> _snapshotMaterials = new();
    private readonly List<Mesh> _ownedMeshes = new();
    private LineRenderer _ring;
    private LineRenderer _core;
    private MaterialPropertyBlock _snapshotBlock;
    private float _duration;
    private float _age;
    private float _radius;
    private float _intensity;
    private Color _color;
    private Vector3 _direction;
    private bool _initialized;

    public static int ActiveCount => s_active.Count;

    public static void Schedule(int id, Vector3 position, Vector3 direction, float radius, Color color,
        WeaponStatusMask statuses, float intensity, EnemyReactionProfile profile, Transform source)
    {
        EnsureRunner();
        if (s_pending.TryGetValue(id, out PendingDeath previous))
            previous.Snapshot?.Dispose();
        s_pending[id] = new PendingDeath
        {
            Id = id,
            Position = position,
            Direction = direction,
            Radius = radius,
            Color = color,
            Statuses = statuses,
            Intensity = intensity,
            Profile = EnemyReactionProfile.Resolve(profile),
            WeaponType = WeaponType.AutomaticCannon,
            Frame = Time.frameCount,
            Snapshot = CaptureSnapshot(source)
        };
    }

    public static void EnrichPending(int id, Vector3 direction, bool critical, WeaponType weaponType)
    {
        if (!s_pending.TryGetValue(id, out PendingDeath pending))
            return;
        pending.Direction = direction;
        pending.Critical = critical;
        pending.WeaponType = weaponType;
        s_pending[id] = pending;
    }

    internal static void FlushPending()
    {
        if (s_pending.Count == 0)
            return;
        List<int> completed = new();
        foreach (KeyValuePair<int, PendingDeath> pair in s_pending)
        {
            if (pair.Value.Frame >= Time.frameCount)
                continue;
            Spawn(pair.Value);
            completed.Add(pair.Key);
        }
        for (int i = 0; i < completed.Count; i++)
            s_pending.Remove(completed[i]);
    }

    private static void Spawn(PendingDeath pending)
    {
        if (!EnemyReactionRuntime.Enabled)
        {
            pending.Snapshot?.Dispose();
            return;
        }
        EnemyReactionProfile profile = EnemyReactionProfile.Resolve(pending.Profile);
        if (s_active.Count >= profile.DeathPoolCapacity)
        {
            pending.Snapshot?.Dispose();
            return;
        }
        EnsureRoot();
        EnemyDeathReactionVfx vfx = s_pool.Count > 0 ? s_pool.Pop() : CreateInstance();
        vfx.transform.SetParent(s_root, false);
        vfx.transform.position = pending.Position;
        vfx.gameObject.SetActive(true);
        vfx.Configure(pending, profile);
        s_active.Add(vfx);
    }

    private static EnemyDeathReactionVfx CreateInstance()
    {
        GameObject go = new("[Enemy Death Reaction]");
        go.hideFlags = HideFlags.DontSave;
        EnemyDeathReactionVfx vfx = go.AddComponent<EnemyDeathReactionVfx>();
        vfx.Initialize();
        return vfx;
    }

    private void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _ring = CreateLine("Collapsing Death Ring", true, RingSegments, 0.045f);
        _core = CreateLine("Death Lift Core", false, 3, 0.075f);
        for (int i = 0; i < ShardCount; i++)
            _shards[i] = CreateLine("Inward Death Fragment " + i, false, 3, 0.052f);
    }

    private void Configure(PendingDeath pending, EnemyReactionProfile profile)
    {
        Initialize();
        CleanupSnapshot();
        _age = 0f;
        _duration = profile.DeathDuration;
        _radius = pending.Radius;
        _intensity = pending.Intensity * (pending.Critical ? 1.2f : 1f);
        _color = ResolveDeathColor(pending.Color, pending.Statuses);
        _direction = pending.Direction.sqrMagnitude > 0.0001f ? pending.Direction.normalized : Vector3.forward;
        BuildSnapshot(pending.Snapshot);
        for (int i = 0; i < ShardCount; i++)
        {
            float angle = i / (float)ShardCount * Mathf.PI * 2f + (i % 2) * 0.21f;
            float height = -0.48f + (i % 5) * 0.26f;
            _spiralSeeds[i] = new Vector3(Mathf.Cos(angle), height, Mathf.Sin(angle));
        }
        ApplyFrame();
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        ApplyFrame();
        if (_age >= _duration)
            Release();
    }

    private void ApplyFrame()
    {
        float t = Mathf.Clamp01(_age / Mathf.Max(0.05f, _duration));
        float appear = Mathf.Clamp01(t / 0.07f);
        float disappear = 1f - Mathf.SmoothStep(0.66f, 1f, t);
        float alpha = appear * disappear;
        Color color = _color;
        float transientAlpha = EnemyReactionRuntime.ScreenFlashEnabled
            ? (EnemyReactionRuntime.ReducedFlash ? 0.28f : 0.64f)
            : 0f;
        color.a = alpha * transientAlpha;
        float dissolve = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.045f, 0.94f, t));
        ApplySnapshot(dissolve, t);

        float groundY = -_radius * 0.62f;
        float ringRadius = _radius * Mathf.Lerp(0.88f, 1.18f, t);
        Color ringColor = color;
        ringColor.a *= 0.42f * (1f - t);
        _ring.widthMultiplier = _radius * Mathf.Lerp(0.026f, 0.008f, t) * _intensity;
        _ring.startColor = ringColor;
        _ring.endColor = ringColor;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f + t * 0.28f;
            float notch = 1f + Mathf.Sin(angle * 7f + t * 5f) * 0.055f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * ringRadius * notch, groundY + t * _radius * 0.045f, Mathf.Sin(angle) * ringRadius * notch));
        }

        Color coreColor = Color.Lerp(color, new Color(0.86f, 0.72f, 0.54f, color.a), 0.45f);
        coreColor.a = color.a * 0.55f;
        float wakeHeight = _radius * Mathf.Lerp(0.7f, 1.85f, t);
        _core.widthMultiplier = _radius * Mathf.Lerp(0.034f, 0.009f, t) * _intensity;
        _core.startColor = new Color(coreColor.r, coreColor.g, coreColor.b, 0f);
        _core.endColor = coreColor;
        _core.SetPosition(0, new Vector3(0f, groundY + _radius * 0.36f, 0f));
        _core.SetPosition(1, new Vector3(Mathf.Sin(t * 7f) * _radius * 0.11f, groundY + wakeHeight * 0.62f, Mathf.Cos(t * 5.4f) * _radius * 0.08f));
        _core.SetPosition(2, new Vector3(Mathf.Sin(t * 5f + 0.8f) * _radius * 0.18f, groundY + wakeHeight, Mathf.Cos(t * 4.2f) * _radius * 0.14f));

        for (int i = 0; i < ShardCount; i++)
        {
            Vector3 seed = _spiralSeeds[i];
            float baseAngle = Mathf.Atan2(seed.z, seed.x);
            float staggered = Mathf.Clamp01((t - (i % 5) * 0.025f) / 0.88f);
            float angle = baseAngle + staggered * (0.58f + (i % 3) * 0.16f);
            float radial = _radius * Mathf.Lerp(0.24f + (i % 3) * 0.18f, 1.12f + (i % 2) * 0.22f, staggered);
            float y = _radius * Mathf.Lerp(seed.y, 1.1f + (i % 4) * 0.18f, Mathf.SmoothStep(0f, 1f, staggered));
            Vector3 position = new(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0.34f, Mathf.Cos(angle)).normalized * _radius * Mathf.Lerp(0.17f, 0.065f, staggered);
            LineRenderer shard = _shards[i];
            Color shardColor = color;
            shardColor.a *= Mathf.Sin(staggered * Mathf.PI);
            shard.widthMultiplier = _radius * Mathf.Lerp(0.032f, 0.008f, staggered) * _intensity;
            shard.startColor = shardColor;
            shard.endColor = coreColor;
            shard.SetPosition(0, position);
            shard.SetPosition(1, position - tangent * 0.48f + Vector3.down * _radius * 0.018f);
            shard.SetPosition(2, position - tangent);
        }
    }

    private static DeathSnapshot CaptureSnapshot(Transform source)
    {
        if (source == null)
            return null;
        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        DeathSnapshot snapshot = new();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsSnapshotSource(renderer))
                continue;
            Mesh mesh = null;
            bool ownsMesh = false;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh
                {
                    name = "[Enemy Death Pose] " + skinned.sharedMesh.name,
                    hideFlags = HideFlags.DontSave
                };
                skinned.BakeMesh(mesh);
                ownsMesh = true;
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter))
            {
                mesh = filter.sharedMesh;
            }
            if (mesh == null || mesh.vertexCount == 0)
            {
                if (ownsMesh)
                    DestroySafely(mesh);
                continue;
            }
            snapshot.Pieces.Add(new SnapshotPiece
            {
                Name = renderer.gameObject.name,
                Mesh = mesh,
                OwnsMesh = ownsMesh,
                Position = renderer.transform.position,
                Rotation = renderer.transform.rotation,
                Scale = renderer.transform.lossyScale,
                Materials = renderer.sharedMaterials,
                Layer = renderer.gameObject.layer
            });
        }
        if (snapshot.Pieces.Count > 0)
            return snapshot;
        snapshot.Dispose();
        return null;
    }

    private static bool IsSnapshotSource(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || renderer is LineRenderer ||
            renderer.GetComponentInParent<EnemyStatusVisual>() != null ||
            renderer.GetComponent<TMPro.TMP_Text>() != null)
            return false;
        string objectName = renderer.gameObject.name;
        return !objectName.StartsWith("[Enemy Hit Flash]") &&
               !objectName.StartsWith("[Enemy Freeze Shell]") &&
               !objectName.StartsWith("[Enemy Status]");
    }

    private void BuildSnapshot(DeathSnapshot snapshot)
    {
        if (snapshot == null)
            return;
        Shader shader = GetDisintegrationShader();
        if (shader == null)
        {
            snapshot.Dispose();
            return;
        }
        for (int i = 0; i < snapshot.Pieces.Count; i++)
        {
            SnapshotPiece piece = snapshot.Pieces[i];
            if (piece == null || piece.Mesh == null)
                continue;
            GameObject go = new("[Disintegrating Enemy] " + piece.Name);
            go.hideFlags = HideFlags.DontSave;
            go.layer = piece.Layer;
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(piece.Position, piece.Rotation);
            go.transform.localScale = piece.Scale;
            go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            Material[] sourceMaterials = piece.Materials;
            int materialCount = Mathf.Max(1, sourceMaterials != null ? sourceMaterials.Length : 0);
            Material[] materials = new Material[materialCount];
            for (int m = 0; m < materialCount; m++)
            {
                Material sourceMaterial = sourceMaterials != null && m < sourceMaterials.Length ? sourceMaterials[m] : null;
                Material material = CreateDisintegrationMaterial(shader, sourceMaterial);
                materials[m] = material;
                _snapshotMaterials.Add(material);
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _snapshotObjects.Add(go);
            _snapshotRenderers.Add(renderer);
            if (piece.OwnsMesh)
            {
                _ownedMeshes.Add(piece.Mesh);
                piece.OwnsMesh = false;
            }
        }
        snapshot.Dispose();
    }

    private static Material CreateDisintegrationMaterial(Shader shader, Material source)
    {
        Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
        Texture texture = Texture2D.whiteTexture;
        Color color = Color.white;
        Vector2 scale = Vector2.one;
        Vector2 offset = Vector2.zero;
        if (source != null)
        {
            if (source.HasProperty("_BaseMap"))
            {
                texture = source.GetTexture("_BaseMap") ?? Texture2D.whiteTexture;
                scale = source.GetTextureScale("_BaseMap");
                offset = source.GetTextureOffset("_BaseMap");
            }
            else if (source.mainTexture != null)
            {
                texture = source.mainTexture;
                scale = source.mainTextureScale;
                offset = source.mainTextureOffset;
            }
            if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color")) color = source.color;
        }
        material.SetTexture(BaseMapId, texture);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor(BaseColorId, color);
        material.SetFloat("_NoiseScale", 6.5f);
        material.SetFloat(LuminescenceId, 0.4f);
        return material;
    }

    private void ApplySnapshot(float dissolve, float time)
    {
        if (_snapshotRenderers.Count == 0)
            return;
        _snapshotBlock ??= new MaterialPropertyBlock();
        Color edge = Color.Lerp(_color, new Color(1f, 0.48f, 0.08f, 1f), 0.62f);
        Color ash = Color.Lerp(_color, new Color(0.12f, 0.105f, 0.09f, 1f), 0.8f);
        float glow = EnemyReactionRuntime.ScreenFlashEnabled
            ? (EnemyReactionRuntime.ReducedFlash ? 0.16f : 0.4f)
            : 0f;
        float opacity = 1f - Mathf.SmoothStep(0.84f, 1f, time) * 0.48f;
        for (int i = 0; i < _snapshotRenderers.Count; i++)
        {
            Renderer renderer = _snapshotRenderers[i];
            if (renderer == null)
                continue;
            _snapshotBlock.Clear();
            _snapshotBlock.SetFloat(DissolveId, dissolve);
            _snapshotBlock.SetColor(EdgeColorId, edge);
            _snapshotBlock.SetColor(AshColorId, ash);
            _snapshotBlock.SetVector(EffectCenterId, transform.position);
            _snapshotBlock.SetFloat(EffectHeightId, Mathf.Max(0.2f, _radius * 2.55f));
            _snapshotBlock.SetFloat(EffectRadiusId, Mathf.Max(0.1f, _radius));
            _snapshotBlock.SetVector(EffectDirectionId, _direction);
            _snapshotBlock.SetFloat(OpacityId, opacity);
            _snapshotBlock.SetFloat(LuminescenceId, glow);
            renderer.SetPropertyBlock(_snapshotBlock);
        }
    }

    private static Shader GetDisintegrationShader()
    {
        if (s_disintegrationShader == null)
            s_disintegrationShader = Shader.Find("ScrapWaves/GameFeel/Enemy Disintegration");
        return s_disintegrationShader;
    }

    private void CleanupSnapshot()
    {
        for (int i = 0; i < _snapshotObjects.Count; i++)
            DestroySafely(_snapshotObjects[i]);
        for (int i = 0; i < _snapshotMaterials.Count; i++)
            DestroySafely(_snapshotMaterials[i]);
        for (int i = 0; i < _ownedMeshes.Count; i++)
            DestroySafely(_ownedMeshes[i]);
        _snapshotObjects.Clear();
        _snapshotMaterials.Clear();
        _ownedMeshes.Clear();
        _snapshotRenderers.Clear();
    }

    private void Release()
    {
        if (!s_active.Remove(this))
            return;
        CleanupSnapshot();
        gameObject.SetActive(false);
        s_pool.Push(this);
    }

    private void OnDestroy() => CleanupSnapshot();

    private LineRenderer CreateLine(string childName, bool loop, int positions, float width)
    {
        GameObject child = new(childName);
        child.transform.SetParent(transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = positions;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.sharedMaterial = GetMaterial();
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private static Color ResolveDeathColor(Color baseColor, WeaponStatusMask statuses)
    {
        Color ash = Color.Lerp(baseColor, new Color(0.72f, 0.58f, 0.42f, 1f), 0.62f);
        if ((statuses & WeaponStatusMask.Freeze) != 0) return Color.Lerp(ash, new Color(0.34f, 0.82f, 1f, 1f), 0.52f);
        if ((statuses & WeaponStatusMask.JellifiedBurn) != 0) return Color.Lerp(ash, new Color(0.48f, 0.94f, 0.08f, 1f), 0.48f);
        if ((statuses & WeaponStatusMask.Burn) != 0) return Color.Lerp(ash, new Color(1f, 0.24f, 0.025f, 1f), 0.48f);
        if ((statuses & WeaponStatusMask.Vulnerable) != 0) return Color.Lerp(ash, new Color(1f, 0.16f, 0.72f, 1f), 0.4f);
        if ((statuses & WeaponStatusMask.Slow) != 0) return Color.Lerp(ash, new Color(0.28f, 0.66f, 1f, 1f), 0.42f);
        return ash;
    }

    private static Material GetMaterial()
    {
        if (s_material != null)
            return s_material;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        s_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_material;
    }

    private static void DestroySafely(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(value);
        else
            Object.DestroyImmediate(value);
    }

    private static void EnsureRoot()
    {
        if (s_root != null)
            return;
        GameObject root = new("[Enemy Death Reactions]");
        root.hideFlags = HideFlags.DontSave;
        s_root = root.transform;
        if (Application.isPlaying)
            DontDestroyOnLoad(root);
    }

    private static void EnsureRunner()
    {
        if (s_runner != null)
            return;
        EnsureRoot();
        s_runner = s_root.gameObject.AddComponent<EnemyDeathReactionRunner>();
    }
}

public sealed class EnemyDeathReactionRunner : MonoBehaviour
{
    private void LateUpdate() => EnemyDeathReactionVfx.FlushPending();
}
