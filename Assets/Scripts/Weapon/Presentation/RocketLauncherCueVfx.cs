using System.Collections.Generic;
using UnityEngine;

public enum RocketLauncherVfxStyle
{
    Launch,
    Impact,
    KineticImpact,
    FragmentImpact,
    ClusterLaunch,
    TargetingLoop,
    LockAcquired,
    TargetingCancelled,
    KineticStatus,
    KillImpact
}

[DisallowMultipleComponent]
public sealed class RocketLauncherCueVfx : MonoBehaviour, IWeaponVfxPrewarm, IWeaponVfxContextReceiver
{
    private const float DamageFootprintHeightOffset = 0.02f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int VertexJitterId = Shader.PropertyToID("_VertexJitter");

    [SerializeField] private RocketLauncherVfxStyle _style;
    [SerializeField] private Color _primaryColor = new(1f, 0.3f, 0.025f, 1f);
    [SerializeField] private Color _coreColor = new(1f, 0.95f, 0.62f, 1f);
    [SerializeField] private Renderer[] _meshLayers;
    [SerializeField] private ParticleSystem[] _particleLayers;
    [SerializeField] private Transform[] _animatedRoots;
    [SerializeField] private Light _lightPulse;

    [SerializeField, Min(0.02f)] private float _lifetime = 0.45f;
    [SerializeField, Min(0.05f)] private float _size = 1f;
    [SerializeField] private AnimationCurve _scaleOverLife = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
    [SerializeField] private AnimationCurve _emissionOverLife = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private AnimationCurve _dissolveOverLife = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Min(0f)] private float _rotationDegreesPerSecond = 160f;
    [SerializeField, Min(0f)] private float _baseEmission = 3f;
    [SerializeField] private bool _scaleFromExplosionRadius;
    [SerializeField, Min(0f)] private float _explosionRadiusMultiplier = 0.55f;

    [Header("Forward mini explosions")]
    [SerializeField] private Transform _forwardMiniExplosionRoot;
    [SerializeField] private Transform[] _forwardMiniExplosions;
    [SerializeField, Min(0f)] private float _forwardConeRangeMultiplier = 4f;
    [SerializeField, Range(0f, 1f)] private float _miniExplosionStart = 0.12f;
    [SerializeField, Range(0f, 0.25f)] private float _miniExplosionStagger = 0.065f;
    [SerializeField, Range(0.05f, 1f)] private float _miniExplosionDuration = 0.3f;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _baseScales;
    private Vector3[] _miniExplosionBaseScales;
    private float _nearestMiniExplosionDepth;
    private float _furthestMiniExplosionDepth;
    private int[] _baseMaxParticles;
    private float _elapsed;
    private float _intensity = 1f;
    private float _contextScale = 1f;
    private float _emissionMultiplier = 1f;
    private float _smokeMultiplier = 1f;
    private float _sparkMultiplier = 1f;
    private Color _reducedFlashColor;
    private float _reducedFlashIntensity = 0.35f;
    private GameFeelQualityLevel _quality = GameFeelQualityLevel.High;
    private bool _reducedFlash;
    private bool _cached;
    private MeshFilter[] _fragmentMeshes;
    private Mesh[] _fragmentSourceMeshes;
    private Mesh[] _fragmentRuntimeMeshes;
    private Vector3[][] _fragmentSourceVertices;
    private Matrix4x4[] _fragmentMeshToRoot;
    private Vector3[] _fragmentMiniLocalPositions;
    private Vector3[] _fragmentMiniRootPositions;
    private int[] _fragmentMeshMiniIndex;
    private readonly Dictionary<Transform, Vector3> _authoredScales = new();
    private readonly Dictionary<ParticleSystem, ParticleBaseline> _particleBaselines = new();
    private MeshRenderer _damageFootprint;
    private Mesh _damageFootprintMesh;
    private bool _showDamageFootprint;
    private MeshRenderer _mainBlastRenderer;
    private Vector3 _mainBlastScalePerMetre;
    private float _mainBlastAuthoredJitter;
    private bool _mainBlastAuthoredEnabled;
    private bool _usesResolvedBlastVolume;
    private float _resolvedBlastRadius;

    private readonly struct ParticleBaseline
    {
        public readonly Vector3 Scale;
        public readonly Vector3 Position;
        public readonly int Maximum;

        public ParticleBaseline(ParticleSystem particles)
        {
            Scale = particles.transform.localScale;
            Position = particles.transform.localPosition;
            Maximum = particles.main.maxParticles;
        }
    }

    public RocketLauncherVfxStyle Style => _style;
    public int RuntimeParticleSystemCount => _particleLayers?.Length ?? 0;
    public int RuntimeMeshLayerCount => _meshLayers?.Length ?? 0;

    public void Prewarm()
    {
        CacheLayers();
        SetLightEnabled(false);
    }

    public void ApplyContext(in WeaponPresentationContext context)
    {
        CacheLayers();
        _intensity = Mathf.Max(0f, context.Intensity);
        _contextScale = _scaleFromExplosionRadius
            ? context.ExplosionRadius * _explosionRadiusMultiplier
            : 1f;
        _usesResolvedBlastVolume = _scaleFromExplosionRadius && IsExplosionCue(context.Cue);
        _resolvedBlastRadius = context.ExplosionRadius;
        _showDamageFootprint = IsExplosionCue(context.Cue) && context.ExplosionRadius > 0f;
        if (_damageFootprint != null)
        {
            // Explosion feedback's normal is the reverse flight direction, not
            // a ground contact. Keep this radial guide in the ground plane;
            // the forward fragmentation sector retains its separate rotation.
            _damageFootprint.transform.rotation = Quaternion.identity;
            // A fixed world offset avoids coplanar floor flicker at every radius.
            _damageFootprint.transform.position = transform.position + Vector3.up * DamageFootprintHeightOffset;
            _damageFootprint.transform.localScale = Vector3.one * context.ExplosionRadius;
        }
        ApplyParticleScale(_scaleFromExplosionRadius && IsExplosionCue(context.Cue) ? context.ExplosionRadius : 1f);
        if (_forwardMiniExplosionRoot != null)
        {
            float coneRange = context.HasFragmentGeometry ? context.FragmentConeRange
                : Mathf.Max(0.05f, context.ExplosionRadius * _forwardConeRangeMultiplier);
            float coneAngle = context.HasFragmentGeometry ? context.FragmentConeAngle : 45f;
            _forwardMiniExplosionRoot.gameObject.SetActive(coneRange > 0f && coneAngle > 0f);
            _forwardMiniExplosionRoot.localScale = Vector3.one * coneRange;
            bool preserveRestMesh = !context.HasFragmentGeometry || (Mathf.Approximately(coneAngle, 45f)
                && Mathf.Approximately(coneRange, context.ExplosionRadius * 4f));
            ApplyFragmentAngle(coneAngle, preserveRestMesh);
        }
        _emissionMultiplier = context.HeatEmissionMultiplier;
        _smokeMultiplier = context.HeatSmokeMultiplier;
        _sparkMultiplier = context.HeatSparkMultiplier;
        _reducedFlash = context.ReducedFlash;
        _reducedFlashColor = context.ReducedFlashColor;
        _reducedFlashIntensity = context.ReducedFlashIntensity;
        _quality = context.Quality;
        ApplyParticleBudget();
        ApplyFrame(0f);
    }

    private void Awake() => CacheLayers();

    private void OnEnable()
    {
        CacheLayers();
        _elapsed = 0f;
        SetLightEnabled(true);
        ApplyFrame(0f);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float normalized = _style == RocketLauncherVfxStyle.TargetingLoop
            ? Mathf.Repeat(_elapsed / Mathf.Max(0.02f, _lifetime), 1f)
            : Mathf.Clamp01(_elapsed / Mathf.Max(0.02f, _lifetime));
        ApplyFrame(normalized);

        if (_animatedRoots == null || _rotationDegreesPerSecond <= 0f)
            return;
        float rotation = _rotationDegreesPerSecond * Time.unscaledDeltaTime;
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].Rotate(0f, 0f, rotation, Space.Self);
        }
    }

    private void OnDisable() => SetLightEnabled(false);

    // The authored mini-explosions are a 45 degree sector. Re-map their planar
    // vertex angle while preserving radial distance, so changing the cone angle
    // cannot extend the visible mesh past the projectile's captured range.
    private void CacheFragmentGeometry()
    {
        if (_forwardMiniExplosionRoot == null || _fragmentMeshes != null) return;
        _fragmentMiniLocalPositions = new Vector3[_forwardMiniExplosions.Length];
        _fragmentMiniRootPositions = new Vector3[_forwardMiniExplosions.Length];
        for (int i = 0; i < _forwardMiniExplosions.Length; i++)
        {
            if (_forwardMiniExplosions[i] == null) continue;
            _fragmentMiniLocalPositions[i] = _forwardMiniExplosions[i].localPosition;
            _fragmentMiniRootPositions[i] = _forwardMiniExplosionRoot.InverseTransformPoint(_forwardMiniExplosions[i].position);
        }
        _fragmentMeshes = _forwardMiniExplosionRoot.GetComponentsInChildren<MeshFilter>(true);
        _fragmentSourceMeshes = new Mesh[_fragmentMeshes.Length];
        _fragmentRuntimeMeshes = new Mesh[_fragmentMeshes.Length];
        _fragmentSourceVertices = new Vector3[_fragmentMeshes.Length][];
        _fragmentMeshToRoot = new Matrix4x4[_fragmentMeshes.Length];
        _fragmentMeshMiniIndex = new int[_fragmentMeshes.Length];
        for (int i = 0; i < _fragmentMeshes.Length; i++)
        {
            _fragmentSourceMeshes[i] = _fragmentMeshes[i].sharedMesh;
            _fragmentMeshToRoot[i] = _forwardMiniExplosionRoot.worldToLocalMatrix * _fragmentMeshes[i].transform.localToWorldMatrix;
            _fragmentMeshMiniIndex[i] = -1;
            for (int j = 0; j < _forwardMiniExplosions.Length; j++)
            {
                if (_forwardMiniExplosions[j] != null && _fragmentMeshes[i].transform.IsChildOf(_forwardMiniExplosions[j]))
                { _fragmentMeshMiniIndex[i] = j; break; }
            }
            if (_fragmentSourceMeshes[i] != null) _fragmentSourceVertices[i] = _fragmentSourceMeshes[i].vertices;
        }
    }

    private static Vector3 RemapFragmentPoint(Vector3 point, float fullAngle)
    {
        float radius = Mathf.Min(1f, Mathf.Sqrt(point.x * point.x + point.z * point.z));
        float halfRestAngle = 22.5f * Mathf.Deg2Rad;
        float angle = Mathf.Clamp(Mathf.Atan2(point.x, point.z), -halfRestAngle, halfRestAngle) * fullAngle / 45f;
        return new Vector3(Mathf.Sin(angle) * radius, point.y, Mathf.Cos(angle) * radius);
    }

    private void ApplyFragmentAngle(float fullAngle, bool preserveRestMesh)
    {
        if (_forwardMiniExplosionRoot == null || fullAngle <= 0f || _fragmentMeshes == null) return;
        for (int i = 0; i < _forwardMiniExplosions.Length; i++)
        {
            Transform mini = _forwardMiniExplosions[i];
            if (mini == null) continue;
            mini.localPosition = preserveRestMesh ? _fragmentMiniLocalPositions[i]
                : mini.parent.InverseTransformPoint(_forwardMiniExplosionRoot.TransformPoint(
                    RemapFragmentPoint(_fragmentMiniRootPositions[i], fullAngle)));
        }
        for (int i = 0; i < _fragmentMeshes.Length; i++)
        {
            if (_fragmentSourceMeshes[i] == null) continue;
            if (preserveRestMesh)
            {
                _fragmentMeshes[i].sharedMesh = _fragmentSourceMeshes[i];
                continue;
            }
            if (_fragmentRuntimeMeshes[i] == null)
            {
                _fragmentRuntimeMeshes[i] = Instantiate(_fragmentSourceMeshes[i]);
                _fragmentRuntimeMeshes[i].name = _fragmentSourceMeshes[i].name + " (resolved fragment cone)";
            }
            // Exclude this root's scale: it already owns the resolved range.
            Matrix4x4 toRoot = _fragmentMeshToRoot[i];
            Matrix4x4 fromRoot = toRoot.inverse;
            int miniIndex = _fragmentMeshMiniIndex[i];
            Vector3 centerDelta = miniIndex >= 0
                ? RemapFragmentPoint(_fragmentMiniRootPositions[miniIndex], fullAngle) - _fragmentMiniRootPositions[miniIndex]
                : Vector3.zero;
            Vector3[] vertices = new Vector3[_fragmentSourceVertices[i].Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(_fragmentSourceVertices[i][v]);
                p = RemapFragmentPoint(p, fullAngle);
                // Animate each remapped mini explosion around its remapped center.
                // Every intermediate pop remains inside the same convex sector.
                vertices[v] = fromRoot.MultiplyPoint3x4(p - centerDelta);
            }
            _fragmentRuntimeMeshes[i].vertices = vertices;
            _fragmentRuntimeMeshes[i].RecalculateBounds();
            _fragmentRuntimeMeshes[i].RecalculateNormals();
            _fragmentMeshes[i].sharedMesh = _fragmentRuntimeMeshes[i];
        }
    }

    private void OnDestroy()
    {
        if (_damageFootprintMesh != null)
        {
            if (Application.isPlaying) Destroy(_damageFootprintMesh);
            else DestroyImmediate(_damageFootprintMesh);
        }
        if (_fragmentRuntimeMeshes == null) return;
        foreach (Mesh mesh in _fragmentRuntimeMeshes)
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
    }

    private void OnValidate()
    {
        _lifetime = Mathf.Max(0.02f, _lifetime);
        _size = Mathf.Max(0.05f, _size);
        _baseEmission = Mathf.Max(0f, _baseEmission);
        _rotationDegreesPerSecond = Mathf.Max(0f, _rotationDegreesPerSecond);
        _explosionRadiusMultiplier = Mathf.Max(0f, _explosionRadiusMultiplier);
        _forwardConeRangeMultiplier = Mathf.Max(0f, _forwardConeRangeMultiplier);
        _miniExplosionStart = Mathf.Clamp01(_miniExplosionStart);
        _miniExplosionStagger = Mathf.Clamp(_miniExplosionStagger, 0f, 0.25f);
        _miniExplosionDuration = Mathf.Clamp(_miniExplosionDuration, 0.05f, 1f);
        _scaleOverLife ??= AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
        _emissionOverLife ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        _dissolveOverLife ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        _cached = false;
    }

    private void CacheLayers()
    {
        if (_cached)
            return;
        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_meshLayers == null || _meshLayers.Length == 0)
            _meshLayers = GetComponentsInChildren<Renderer>(true);
        if (_particleLayers == null || _particleLayers.Length == 0)
            _particleLayers = GetComponentsInChildren<ParticleSystem>(true);
        _animatedRoots ??= System.Array.Empty<Transform>();
        _forwardMiniExplosions ??= System.Array.Empty<Transform>();

        _baseScales = new Vector3[_animatedRoots.Length];
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _baseScales[i] = AuthoredScale(_animatedRoots[i]);
        }

        _miniExplosionBaseScales = new Vector3[_forwardMiniExplosions.Length];
        _nearestMiniExplosionDepth = float.PositiveInfinity;
        _furthestMiniExplosionDepth = float.NegativeInfinity;
        for (int i = 0; i < _forwardMiniExplosions.Length; i++)
        {
            if (_forwardMiniExplosions[i] != null)
            {
                _miniExplosionBaseScales[i] = AuthoredScale(_forwardMiniExplosions[i]);
                float depth = _fragmentMiniLocalPositions != null && i < _fragmentMiniLocalPositions.Length
                    ? _fragmentMiniLocalPositions[i].z : _forwardMiniExplosions[i].localPosition.z;
                _nearestMiniExplosionDepth = Mathf.Min(_nearestMiniExplosionDepth, depth);
                _furthestMiniExplosionDepth = Mathf.Max(_furthestMiniExplosionDepth, depth);
            }
        }
        if (float.IsInfinity(_nearestMiniExplosionDepth))
            _nearestMiniExplosionDepth = _furthestMiniExplosionDepth = 0f;

        // Capture rest transforms before OnEnable/ApplyFrame can scale a mini to zero.
        CacheFragmentGeometry();

        _baseMaxParticles = new int[_particleLayers.Length];
        for (int i = 0; i < _particleLayers.Length; i++)
        {
            if (_particleLayers[i] != null)
            {
                ParticleSystem particles = _particleLayers[i];
                if (!_particleBaselines.TryGetValue(particles, out ParticleBaseline baseline))
                {
                    baseline = new ParticleBaseline(particles);
                    _particleBaselines.Add(particles, baseline);
                }
                _baseMaxParticles[i] = baseline.Maximum;
            }
        }
        CacheMainBlastVolume();
        CacheDamageFootprint();
    }

    private void CacheMainBlastVolume()
    {
        if (_mainBlastRenderer != null) return;
        // Every authored rocket blast has this centered sphere. Forward mini blasts
        // and pressure fronts keep their own geometry and animation.
        Transform core = transform.Find("Explosion Core");
        if (core == null || !core.TryGetComponent(out MeshFilter filter)
            || filter.sharedMesh == null || !core.TryGetComponent(out MeshRenderer renderer)) return;

        Vector3 authoredScale = AuthoredScale(core);
        Mesh mesh = filter.sharedMesh;
        float radius = 0f;
        if (mesh.isReadable)
        {
            foreach (Vector3 vertex in mesh.vertices)
                radius = Mathf.Max(radius, Vector3.Scale(vertex, authoredScale).magnitude);
        }
        else
        {
            Vector3 extent = Vector3.Scale(mesh.bounds.extents, authoredScale);
            radius = Mathf.Max(Mathf.Abs(extent.x), Mathf.Abs(extent.y), Mathf.Abs(extent.z));
        }
        if (radius <= 0f) return;
        _mainBlastRenderer = renderer;
        _mainBlastScalePerMetre = authoredScale / radius;
        _mainBlastAuthoredEnabled = renderer.enabled;
        Material material = renderer.sharedMaterial;
        _mainBlastAuthoredJitter = material != null && material.HasProperty(VertexJitterId)
            ? material.GetFloat(VertexJitterId) : 0f;
    }

    private void ApplyMainBlastVolume(float normalizedLife)
    {
        if (_mainBlastRenderer == null) return;
        _mainBlastRenderer.enabled = _mainBlastAuthoredEnabled && (!_usesResolvedBlastVolume
            || (_resolvedBlastRadius > 0f && normalizedLife < 1f));
        if (!_usesResolvedBlastVolume) return;

        // Damage is instantaneous. Show its full volume from the first visible frame;
        // brightness/dissolve provide the animation instead of delayed, undersized growth.
        Vector3 scale = _mainBlastScalePerMetre * _resolvedBlastRadius;
        Vector3 parentScale = _mainBlastRenderer.transform.parent.lossyScale;
        _mainBlastRenderer.transform.localScale = new Vector3(
            scale.x / Mathf.Max(0.000001f, Mathf.Abs(parentScale.x)),
            scale.y / Mathf.Max(0.000001f, Mathf.Abs(parentScale.y)),
            scale.z / Mathf.Max(0.000001f, Mathf.Abs(parentScale.z)));
    }

    private Vector3 AuthoredScale(Transform target)
    {
        if (!_authoredScales.TryGetValue(target, out Vector3 scale))
        {
            scale = target.localScale;
            _authoredScales.Add(target, scale);
        }
        return scale;
    }

    private static bool IsExplosionCue(WeaponPresentationCue cue) => cue is
        WeaponPresentationCue.RocketImpact or WeaponPresentationCue.RocketKineticImpact or
        WeaponPresentationCue.RocketFragmentImpact or WeaponPresentationCue.RocketFragmentChildImpact or
        WeaponPresentationCue.RocketKillImpact or WeaponPresentationCue.RocketClusterDetonation;

    private void ApplyParticleScale(float radiusScale)
    {
        foreach (ParticleSystem particles in _particleLayers)
        {
            if (particles == null) continue;
            ParticleBaseline baseline = _particleBaselines[particles];
            // These authored impact emitters are siblings of the animated mesh
            // roots. Local scaling handles their authored shape, launch motion
            // and particle size without scaling the blast twice. Their curves
            // and quality budgets remain authored; one metre is the reference.
            ParticleSystem.MainModule main = particles.main;
            main.scalingMode = ParticleSystemScalingMode.Local;
            particles.transform.localScale = baseline.Scale * radiusScale;
            particles.transform.localPosition = baseline.Position * radiusScale;
        }
    }

    private void CacheDamageFootprint()
    {
        if (_damageFootprint != null || !_scaleFromExplosionRadius) return;
        Material material = null;
        foreach (Renderer layer in _meshLayers)
        {
            if (layer == null || layer is ParticleSystemRenderer || layer.sharedMaterial == null) continue;
            material = layer.sharedMaterial;
            break;
        }
        if (material == null) return;

        const int segments = 72;
        Vector3[] vertices = new Vector3[segments * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 edge = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = edge;
            vertices[i * 2 + 1] = edge * 0.975f;
            normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            colors[i * 2] = colors[i * 2 + 1] = Color.white;
            uv[i * 2] = new Vector2(i / (float)segments, 1f);
            uv[i * 2 + 1] = new Vector2(i / (float)segments, 0f);
            int next = (i + 1) % segments * 2;
            int triangle = i * 6;
            triangles[triangle] = i * 2;
            triangles[triangle + 1] = i * 2 + 1;
            triangles[triangle + 2] = next;
            triangles[triangle + 3] = next;
            triangles[triangle + 4] = i * 2 + 1;
            triangles[triangle + 5] = next + 1;
        }
        _damageFootprintMesh = new Mesh { name = "Resolved rocket blast footprint", hideFlags = HideFlags.DontSave };
        _damageFootprintMesh.vertices = vertices;
        _damageFootprintMesh.normals = normals;
        _damageFootprintMesh.colors = colors;
        _damageFootprintMesh.uv = uv;
        _damageFootprintMesh.triangles = triangles;
        _damageFootprintMesh.RecalculateBounds();
        GameObject footprint = new("Resolved Damage Footprint");
        footprint.transform.SetParent(transform, false);
        footprint.AddComponent<MeshFilter>().sharedMesh = _damageFootprintMesh;
        _damageFootprint = footprint.AddComponent<MeshRenderer>();
        _damageFootprint.sharedMaterial = material;
        _damageFootprint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _damageFootprint.receiveShadows = false;
        _damageFootprint.enabled = false;
    }

    private void ApplyFrame(float normalizedLife)
    {
        float flashScale = _reducedFlash ? _reducedFlashIntensity : 1f;
        Color color = _reducedFlash
            ? Color.Lerp(_primaryColor, _reducedFlashColor, 0.7f)
            : Color.Lerp(_primaryColor, _coreColor, Mathf.Clamp01(1f - normalizedLife) * 0.45f);
        float emission = _baseEmission * Mathf.Max(0f, _emissionOverLife.Evaluate(normalizedLife)) *
                         _emissionMultiplier * _intensity * flashScale;
        if (_style == RocketLauncherVfxStyle.KineticImpact)
            emission = Mathf.Min(emission, 2.5f);
        float dissolve = Mathf.Clamp01(_dissolveOverLife.Evaluate(normalizedLife));

        for (int i = 0; i < _meshLayers.Length; i++)
        {
            Renderer renderer = _meshLayers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(EmissionColorId,
                _style == RocketLauncherVfxStyle.KineticImpact ? color : color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(PulseId, Mathf.Clamp01(_intensity * (1f - normalizedLife)) * flashScale);
            _propertyBlock.SetFloat(DissolveId, dissolve);
            if (renderer == _mainBlastRenderer)
                _propertyBlock.SetFloat(VertexJitterId, _usesResolvedBlastVolume ? 0f : _mainBlastAuthoredJitter);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float scale = _size * _contextScale * Mathf.Max(0f, _scaleOverLife.Evaluate(normalizedLife));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }
        ApplyMainBlastVolume(normalizedLife);
        ApplyForwardMiniExplosions(normalizedLife);
        if (_damageFootprint != null)
        {
            _damageFootprint.enabled = _showDamageFootprint && normalizedLife < 1f;
            Color footprintColor = color;
            footprintColor.a *= 1f - Mathf.SmoothStep(0f, 1f, normalizedLife);
            _damageFootprint.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, footprintColor);
            _propertyBlock.SetColor(EmissionColorId, footprintColor * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(PulseId, flashScale);
            _propertyBlock.SetFloat(DissolveId, 0f);
            _propertyBlock.SetFloat(VertexJitterId, 0f);
            _damageFootprint.SetPropertyBlock(_propertyBlock);
        }

        if (_lightPulse != null)
        {
            _lightPulse.color = color;
            _lightPulse.intensity = emission;
        }
    }

    private void ApplyForwardMiniExplosions(float normalizedLife)
    {
        if (_forwardMiniExplosions == null)
            return;

        for (int i = 0; i < _forwardMiniExplosions.Length; i++)
        {
            Transform miniExplosion = _forwardMiniExplosions[i];
            if (miniExplosion == null)
                continue;

            float travelProgress = Mathf.InverseLerp(
                _nearestMiniExplosionDepth,
                _furthestMiniExplosionDepth,
                miniExplosion.localPosition.z);
            float start = _miniExplosionStart +
                          travelProgress * Mathf.Max(0, _forwardMiniExplosions.Length - 1) * _miniExplosionStagger;
            float progress = Mathf.Clamp01((normalizedLife - start) / Mathf.Max(0.05f, _miniExplosionDuration));
            float pop = progress > 0f && progress < 1f
                ? Mathf.Sin(progress * Mathf.PI)
                : 0f;
            Vector3 baseScale = i < _miniExplosionBaseScales.Length
                ? _miniExplosionBaseScales[i]
                : Vector3.one;
            miniExplosion.localScale = baseScale * pop;
        }
    }

    private void ApplyParticleBudget()
    {
        float qualityMultiplier = _quality switch
        {
            GameFeelQualityLevel.Low => 0.35f,
            GameFeelQualityLevel.Medium => 0.7f,
            _ => 1f
        };
        for (int i = 0; i < _particleLayers.Length; i++)
        {
            ParticleSystem particles = _particleLayers[i];
            if (particles == null)
                continue;
            ParticleSystem.MainModule main = particles.main;
            int authoredMaximum = i < _baseMaxParticles.Length ? _baseMaxParticles[i] : main.maxParticles;
            float layerMultiplier = i == 0 ? _sparkMultiplier : _smokeMultiplier;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(authoredMaximum * qualityMultiplier * layerMultiplier));
        }
    }

    private void SetLightEnabled(bool value)
    {
        if (_lightPulse != null)
            _lightPulse.enabled = value && _quality == GameFeelQualityLevel.High;
    }
}
