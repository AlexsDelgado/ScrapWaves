using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class MortarLandingIndicatorVfx : MonoBehaviour, IWeaponVfxPrewarm
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int VertexJitterId = Shader.PropertyToID("_VertexJitter");

    [SerializeField] private Transform _blastRadiusRing;
    [SerializeField] private Transform _timeToImpactRing;
    [SerializeField] private Transform _landingCore;
    [SerializeField] private Renderer[] _renderers;
    [SerializeField, Min(0.01f)] private float _minimumPulsePeriod = 0.12f;
    [SerializeField, Min(0.001f)] private float _groundSurfaceOffset = 0.015f;
    [SerializeField, Min(0.05f)] private float _groundProjectionDistance = 3f;
    [SerializeField, Min(0.001f)] private float _maximumSurfaceDeviation = 0.06f;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _blastBaseScale = Vector3.one;
    private Vector3 _timeBaseScale = Vector3.one;
    private Vector3 _coreBaseScale = Vector3.one;
    private Transform _cachedBlastLayer, _cachedTimeLayer, _cachedCoreLayer;
    private bool _baseScalesCached;
    private float _phase;
    private float _blastRadius;
    private float _travelTime = 0.5f;
    private WeaponUpgradePath _path;
    private bool _cached;
    private MortarGroundConformer _groundConformer;
    private Vector3 _contactPoint, _contactNormal = Vector3.up;
    private Transform _ignoredRoot;
    private bool _groundConforming;
    private int _lastGroundRefreshFrame = -1;

    public float BlastRadius => _blastRadius;
    public float TravelTime => _travelTime;
    public WeaponUpgradePath CurrentPath => _path;
    public Vector3 ContactPoint => _contactPoint;
    public float SurfaceOffset => _groundSurfaceOffset;
    public bool IsGroundConforming => _groundConforming;
#if UNITY_EDITOR
    public long GroundConformCount { get; private set; }
    public long GroundRaycastCount => _groundConformer?.RaycastCount ?? 0;
    public long GroundRaycastFallbackCount => _groundConformer?.RaycastFallbackCount ?? 0;
#endif

    public void Prewarm()
    {
        CacheLayers();
        ApplyFrame(0f);
    }

    public void Configure(
        Vector3 position,
        Vector3 normal,
        float blastRadius,
        float travelTime,
        WeaponUpgradePath path)
    {
        _groundConforming = false;
        _groundConformer?.Restore();
        _contactPoint = position;
        ConfigureLayers(position, normal, blastRadius, travelTime, path);
    }

    public void ConfigureGroundContact(
        Vector3 contactPoint,
        Vector3 normal,
        float blastRadius,
        float travelTime,
        WeaponUpgradePath path,
        Transform ignoredRoot = null)
    {
        _contactPoint = contactPoint;
        _contactNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        _ignoredRoot = ignoredRoot;
        _groundConforming = _contactNormal.y >= 0.2f;
        if (!_groundConforming) _groundConformer?.Restore();
        ConfigureLayers(contactPoint + _contactNormal * _groundSurfaceOffset,
            _contactNormal, blastRadius, travelTime, path);
        RefreshGroundGeometry();
    }

    private void ConfigureLayers(Vector3 position, Vector3 normal, float blastRadius,
        float travelTime, WeaponUpgradePath path)
    {
        CacheLayers();
        _blastRadius = Mathf.Max(0f, blastRadius);
        _travelTime = Mathf.Max(_minimumPulsePeriod, travelTime);
        _path = path;
        Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, safeNormal));
        float diameter = _blastRadius * 2f;
        if (_blastRadiusRing != null)
            _blastRadiusRing.localScale = new Vector3(_blastBaseScale.x * diameter, _blastBaseScale.y, _blastBaseScale.z * diameter);
        ApplyFrame(_phase);
    }

    private void Awake() => CacheLayers();

    private void OnEnable()
    {
        CacheLayers();
        _phase = 0f;
        ApplyFrame(0f);
    }

    private void Update()
    {
        _phase = Mathf.Repeat(_phase + Time.unscaledDeltaTime / Mathf.Max(_minimumPulsePeriod, _travelTime), 1f);
        if (!_groundConforming) ApplyFrame(_phase);
    }

    private void LateUpdate()
    {
        // ReticleHud (order 100) configures once after its current-frame aim.
        // Standalone authored markers still animate/track moving terrain; do not
        // repeat those physics queries when the HUD already refreshed this frame.
        if (_groundConforming && _lastGroundRefreshFrame != Time.frameCount)
        {
            ApplyFrame(_phase);
            RefreshGroundGeometry();
        }
    }

    public void RefreshGroundGeometry()
    {
        if (!_groundConforming) return;
        CacheLayers();
        _groundConformer ??= new MortarGroundConformer(_renderers, _blastRadiusRing);
        _groundConformer.Conform(_contactPoint, _contactNormal, _ignoredRoot,
            _groundSurfaceOffset, _groundProjectionDistance, _maximumSurfaceDeviation);
#if UNITY_EDITOR
        GroundConformCount++;
#endif
        _lastGroundRefreshFrame = Time.frameCount;
    }

    private void OnDisable()
    {
        _groundConformer?.Restore();
        _lastGroundRefreshFrame = -1;
    }

    private void OnDestroy()
    {
        _groundConformer?.Dispose();
        _groundConformer = null;
    }

    private void OnValidate()
    {
        // Legacy prewarmed markers may have no configured radius yet. Preserve
        // their prior cache evidence before validation invalidates the cache.
        AdoptLegacyBaseScales();
        _minimumPulsePeriod = Mathf.Max(0.01f, _minimumPulsePeriod);
        _groundSurfaceOffset = Mathf.Max(0.001f, _groundSurfaceOffset);
        _groundProjectionDistance = Mathf.Max(0.05f, _groundProjectionDistance);
        _maximumSurfaceDeviation = Mathf.Max(0.001f, _maximumSurfaceDeviation);
        _groundConformer?.Dispose();
        _groundConformer = null;
        _cached = false;
    }

    private void CacheLayers()
    {
        AdoptLegacyBaseScales();
        if (_cached)
            return;
        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        _renderers ??= GetComponentsInChildren<Renderer>(true);
        // Validation can run while these transforms carry resolved radius/pulse
        // scales. Retain their authored baselines unless the layer was replaced.
        if (_blastRadiusRing != null && (!_baseScalesCached || _cachedBlastLayer != _blastRadiusRing))
            _blastBaseScale = _blastRadiusRing.localScale;
        if (_timeToImpactRing != null && (!_baseScalesCached || _cachedTimeLayer != _timeToImpactRing))
            _timeBaseScale = _timeToImpactRing.localScale;
        if (_landingCore != null && (!_baseScalesCached || _cachedCoreLayer != _landingCore))
            _coreBaseScale = _landingCore.localScale;
        _cachedBlastLayer = _blastRadiusRing;
        _cachedTimeLayer = _timeToImpactRing;
        _cachedCoreLayer = _landingCore;
        _baseScalesCached = true;
    }

    private void AdoptLegacyBaseScales()
    {
        // A live component upgraded from the earlier prototype retains its
        // authored bases and old cache/radius fields, while new bookkeeping
        // starts empty. Fresh instances have neither prior-cache evidence nor
        // a configured radius and must still capture their authored transforms.
        if (_baseScalesCached || (!_cached && _blastRadius <= 0f)) return;
        _cachedBlastLayer = _blastRadiusRing;
        _cachedTimeLayer = _timeToImpactRing;
        _cachedCoreLayer = _landingCore;
        _baseScalesCached = true;
    }

    private void ApplyFrame(float phase)
    {
        Color color = _path switch
        {
            WeaponUpgradePath.PathA => new Color(1f, 0.84f, 0.08f, 0.78f),
            WeaponUpgradePath.PathB => new Color(0.7f, 0.28f, 1f, 0.78f),
            _ => new Color(1f, 0.38f, 0.045f, 0.78f)
        };
        float pulse = 0.62f + 0.38f * Mathf.Sin(phase * Mathf.PI * 2f);
        float emission = Mathf.Lerp(1.7f, 3.3f, pulse);
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(PulseId, pulse);
            // The shared VFX shader's vertex wobble makes a surface marker float
            // and clip. Override only this marker; shell/cue materials stay authored.
            _propertyBlock.SetFloat(VertexJitterId, 0f);
            renderer.SetPropertyBlock(_propertyBlock);
        }
        if (_timeToImpactRing != null)
            _timeToImpactRing.localScale = _timeBaseScale * Mathf.Lerp(1.18f, 0.42f, phase);
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale * Mathf.Lerp(0.82f, 1.24f, pulse);
    }
}
