using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class EnemyDeathReactionVfx : MonoBehaviour
{
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
    }

    private const int ShardCount = 9;
    private const int RingSegments = 36;
    private static readonly Dictionary<int, PendingDeath> s_pending = new();
    private static readonly Stack<EnemyDeathReactionVfx> s_pool = new();
    private static readonly HashSet<EnemyDeathReactionVfx> s_active = new();
    private static Transform s_root;
    private static Material s_material;
    private static EnemyDeathReactionRunner s_runner;

    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly Vector3[] _velocities = new Vector3[ShardCount];
    private LineRenderer _ring;
    private float _duration;
    private float _age;
    private float _radius;
    private float _intensity;
    private Color _color;
    private WeaponStatusMask _statuses;
    private bool _initialized;

    public static int ActiveCount => s_active.Count;

    public static void Schedule(int id, Vector3 position, Vector3 direction, float radius, Color color,
        WeaponStatusMask statuses, float intensity, EnemyReactionProfile profile)
    {
        EnsureRunner();
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
            Frame = Time.frameCount
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
            return;
        EnemyReactionProfile profile = EnemyReactionProfile.Resolve(pending.Profile);
        if (s_active.Count >= profile.DeathPoolCapacity)
            return;
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
        _ring = CreateLine("Death Ring", true, RingSegments, 0.045f);
        for (int i = 0; i < ShardCount; i++)
            _shards[i] = CreateLine("Death Shard " + i, false, 2, 0.055f);
    }

    private void Configure(PendingDeath pending, EnemyReactionProfile profile)
    {
        Initialize();
        _age = 0f;
        _duration = profile.DeathDuration;
        _radius = pending.Radius;
        _intensity = pending.Intensity * (pending.Critical ? 1.2f : 1f);
        _statuses = pending.Statuses;
        _color = ResolveDeathColor(pending.Color, pending.Statuses);
        Vector3 bias = pending.Direction.sqrMagnitude > 0.001f ? pending.Direction.normalized : Vector3.up;
        float weaponSpread = pending.WeaponType == WeaponType.RocketLauncher || pending.WeaponType == WeaponType.Mortar ? 1.25f : 1f;
        for (int i = 0; i < ShardCount; i++)
        {
            float angle = i / (float)ShardCount * Mathf.PI * 2f;
            Vector3 radial = new(Mathf.Cos(angle), 0.35f + (i % 3) * 0.22f, Mathf.Sin(angle));
            _velocities[i] = (radial.normalized + bias * 0.28f).normalized * _radius * weaponSpread * (1.4f + (i % 4) * 0.2f);
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
        float alpha = 1f - t;
        Color color = _color;
        color.a = alpha * (EnemyReactionRuntime.ReducedFlash ? 0.35f : 0.72f);
        float ringRadius = _radius * Mathf.Lerp(0.2f, 1.45f, Mathf.Sqrt(t));
        _ring.startColor = color;
        _ring.endColor = color;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * ringRadius, 0.05f + t * 0.12f, Mathf.Sin(angle) * ringRadius));
        }
        for (int i = 0; i < ShardCount; i++)
        {
            Vector3 position = _velocities[i] * _age + Vector3.down * (2.5f * _age * _age);
            Vector3 tangent = _velocities[i].normalized * _radius * 0.22f * (1f - t * 0.65f);
            LineRenderer shard = _shards[i];
            shard.startColor = color;
            shard.endColor = new Color(color.r, color.g, color.b, 0f);
            shard.SetPosition(0, position);
            shard.SetPosition(1, position - tangent);
        }
    }

    private void Release()
    {
        if (!s_active.Remove(this))
            return;
        gameObject.SetActive(false);
        s_pool.Push(this);
    }

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
        if ((statuses & WeaponStatusMask.Freeze) != 0) return new Color(0.56f, 0.9f, 1f, 1f);
        if ((statuses & WeaponStatusMask.JellifiedBurn) != 0) return new Color(0.52f, 0.9f, 0.08f, 1f);
        if ((statuses & WeaponStatusMask.Burn) != 0) return new Color(1f, 0.25f, 0.02f, 1f);
        if ((statuses & WeaponStatusMask.Vulnerable) != 0) return new Color(1f, 0.2f, 0.72f, 1f);
        if ((statuses & WeaponStatusMask.Slow) != 0) return new Color(0.4f, 0.76f, 1f, 1f);
        return Color.Lerp(baseColor, new Color(1f, 0.44f, 0.08f, 1f), 0.45f);
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
