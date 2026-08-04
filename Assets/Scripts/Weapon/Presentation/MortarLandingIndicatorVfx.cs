using UnityEngine;

[DisallowMultipleComponent]
public sealed class MortarLandingIndicatorVfx : MonoBehaviour, IWeaponVfxPrewarm
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

    [SerializeField] private Transform _blastRadiusRing;
    [SerializeField] private Transform _timeToImpactRing;
    [SerializeField] private Transform _landingCore;
    [SerializeField] private Renderer[] _renderers;
    [SerializeField, Min(0.01f)] private float _minimumPulsePeriod = 0.12f;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _blastBaseScale = Vector3.one;
    private Vector3 _timeBaseScale = Vector3.one;
    private Vector3 _coreBaseScale = Vector3.one;
    private float _phase;
    private float _blastRadius;
    private float _travelTime = 0.5f;
    private WeaponUpgradePath _path;
    private bool _cached;

    public float BlastRadius => _blastRadius;
    public float TravelTime => _travelTime;
    public WeaponUpgradePath CurrentPath => _path;

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
        CacheLayers();
        _blastRadius = Mathf.Max(0.01f, blastRadius);
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
        ApplyFrame(_phase);
    }

    private void OnValidate()
    {
        _minimumPulsePeriod = Mathf.Max(0.01f, _minimumPulsePeriod);
        _cached = false;
    }

    private void CacheLayers()
    {
        if (_cached)
            return;
        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        _renderers ??= GetComponentsInChildren<Renderer>(true);
        if (_blastRadiusRing != null)
            _blastBaseScale = _blastRadiusRing.localScale;
        if (_timeToImpactRing != null)
            _timeBaseScale = _timeToImpactRing.localScale;
        if (_landingCore != null)
            _coreBaseScale = _landingCore.localScale;
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
            renderer.SetPropertyBlock(_propertyBlock);
        }
        if (_timeToImpactRing != null)
            _timeToImpactRing.localScale = _timeBaseScale * Mathf.Lerp(1.18f, 0.42f, phase);
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale * Mathf.Lerp(0.82f, 1.24f, pulse);
    }
}
