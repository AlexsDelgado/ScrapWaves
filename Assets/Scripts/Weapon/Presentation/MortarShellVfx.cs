using UnityEngine;

public enum MortarShellVisualStyle
{
    Base,
    Grapeshot,
    MultiCharged
}

[DisallowMultipleComponent]
public sealed class MortarShellVfx : MonoBehaviour, IWeaponVfxPrewarm
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [Header("Authored layers")]
    [SerializeField] private GameObject _flightRoot;
    [SerializeField] private Transform _shellRoot;
    [SerializeField] private Renderer[] _shellRenderers;
    [SerializeField] private TrailRenderer _trail;
    [SerializeField] private ParticleSystem _flightSmoke;

    [Header("World landing prediction")]
    [SerializeField] private GameObject _landingRoot;
    [SerializeField] private Transform _blastRadiusRing;
    [SerializeField] private Transform _countdownRing;
    [SerializeField] private Transform _landingCore;
    [SerializeField] private Renderer[] _indicatorRenderers;
    [SerializeField, Min(0.001f)] private float _surfaceOffset = 0.035f;

    private readonly Color _baseColor = new(1f, 0.42f, 0.06f, 1f);
    private readonly Color _grapeshotColor = new(1f, 0.84f, 0.08f, 1f);
    private readonly Color _chargedColor = new(0.7f, 0.28f, 1f, 1f);
    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _target;
    private Vector3 _impactNormal = Vector3.up;
    private Vector3 _blastBaseScale = Vector3.one;
    private Vector3 _countdownBaseScale = Vector3.one;
    private Vector3 _coreBaseScale = Vector3.one;
    private float _travelTime = 0.5f;
    private float _remainingRepeatTime;
    private float _repeatDelay = 1f;
    private float _spin;
    private float _heat;
    private bool _showLanding;
    private bool _repeatCountdown;
    private bool _cached;
    private Color _styleColor;

    public int ShellRendererCount => _shellRenderers?.Length ?? 0;
    public bool HasLandingIndicator => _landingRoot != null;
    public bool LandingIndicatorVisible => _landingRoot != null && _landingRoot.activeSelf;
    public Vector3 LandingIndicatorPosition => _landingRoot != null ? _landingRoot.transform.position : Vector3.zero;

    public void Prewarm()
    {
        CacheLayers();
        ResetVisuals();
    }

    public void Configure(
        MortarShellVisualStyle style,
        Vector3 target,
        Vector3 impactNormal,
        float explosionRadius,
        float travelTime,
        float normalizedHeat,
        bool detailed,
        bool showLanding)
    {
        CacheLayers();
        _target = target;
        _impactNormal = impactNormal.sqrMagnitude > 0.0001f ? impactNormal.normalized : Vector3.up;
        _travelTime = Mathf.Max(0.05f, travelTime);
        _heat = Mathf.Clamp01(normalizedHeat);
        _showLanding = showLanding;
        _repeatCountdown = false;
        _spin = 0f;
        _styleColor = style switch
        {
            MortarShellVisualStyle.Grapeshot => _grapeshotColor,
            MortarShellVisualStyle.MultiCharged => _chargedColor,
            _ => _baseColor
        };

        if (_flightRoot != null)
            _flightRoot.SetActive(true);
        if (_shellRoot != null)
            _shellRoot.gameObject.SetActive(true);
        if (_landingRoot != null)
            _landingRoot.SetActive(showLanding);
        if (_blastRadiusRing != null)
        {
            float diameter = Mathf.Max(0.12f, explosionRadius * 2f);
            _blastRadiusRing.localScale = new Vector3(
                _blastBaseScale.x * diameter,
                _blastBaseScale.y,
                _blastBaseScale.z * diameter);
        }
        if (_countdownRing != null)
            _countdownRing.localScale = _countdownBaseScale;
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale;

        ApplyColors(_styleColor, 1f);
        ConfigureTrail(detailed);
        ConfigureSmoke(detailed);
        KeepIndicatorWorldAnchored();
    }

    public void UpdateFlight(Vector3 velocity, float normalizedTime)
    {
        CacheLayers();
        float safeTime = Mathf.Clamp01(normalizedTime);
        _spin = Mathf.Repeat(_spin + Time.deltaTime * Mathf.Lerp(420f, 760f, _heat), 360f);
        if (_shellRoot != null && velocity.sqrMagnitude > 0.0001f)
        {
            Quaternion forward = Quaternion.LookRotation(velocity.normalized, GetStableUp(velocity));
            _shellRoot.rotation = forward * Quaternion.AngleAxis(_spin, Vector3.forward);
        }

        float pulse = 0.58f + 0.42f * Mathf.Sin((safeTime * safeTime * 13f + Time.unscaledTime * 2.2f) * Mathf.PI);
        ApplyColors(_styleColor, Mathf.Lerp(0.72f, 1.35f, pulse));
        if (_countdownRing != null)
        {
            float contraction = Mathf.Lerp(1.28f, 0.64f, safeTime);
            _countdownRing.localScale = _countdownBaseScale * contraction;
            _countdownRing.Rotate(0f, 95f * Time.unscaledDeltaTime, 0f, Space.Self);
        }
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale * Mathf.Lerp(0.72f, 1.18f, pulse);
        KeepIndicatorWorldAnchored();
    }

    public void ShowImpact(bool keepChargedMarker)
    {
        CacheLayers();
        if (_flightRoot != null)
            _flightRoot.SetActive(false);
        if (_shellRoot != null)
            _shellRoot.gameObject.SetActive(keepChargedMarker);
        if (_trail != null)
        {
            _trail.emitting = false;
            _trail.Clear();
        }
        if (_flightSmoke != null)
            _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_landingRoot != null)
            _landingRoot.SetActive(keepChargedMarker);
        _showLanding = keepChargedMarker;
        KeepIndicatorWorldAnchored();
    }

    public void BeginRepeatCountdown(float delay, int remainingExplosions)
    {
        _repeatCountdown = remainingExplosions > 0;
        _repeatDelay = Mathf.Max(0.01f, delay);
        _remainingRepeatTime = _repeatDelay;
        _showLanding = _repeatCountdown;
        if (_landingRoot != null)
            _landingRoot.SetActive(_repeatCountdown);
        ApplyColors(_chargedColor, 1.15f);
        KeepIndicatorWorldAnchored();
    }

    public void UpdateRepeatCountdown(float remainingTime)
    {
        if (!_repeatCountdown)
            return;
        _remainingRepeatTime = Mathf.Max(0f, remainingTime);
        float progress = 1f - Mathf.Clamp01(_remainingRepeatTime / _repeatDelay);
        float beat = 0.65f + 0.35f * Mathf.Sin((Time.unscaledTime * Mathf.Lerp(4f, 11f, progress)) * Mathf.PI * 2f);
        if (_countdownRing != null)
            _countdownRing.localScale = _countdownBaseScale * Mathf.Lerp(1.18f, 0.48f, progress);
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale * Mathf.Lerp(0.8f, 1.35f, beat);
        ApplyColors(_chargedColor, Mathf.Lerp(1.1f, 2.1f, progress) * beat);
        KeepIndicatorWorldAnchored();
    }

    public void PulseRepeat(int remainingExplosions)
    {
        _repeatCountdown = remainingExplosions > 0;
        _remainingRepeatTime = _repeatDelay;
        if (_landingCore != null)
            _landingCore.localScale = _coreBaseScale * 1.5f;
        if (_landingRoot != null)
            _landingRoot.SetActive(_repeatCountdown);
    }

    public void SetImpactPoint(Vector3 position, Vector3 normal)
    {
        _target = position;
        _impactNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        KeepIndicatorWorldAnchored();
    }

    public void ResetVisuals()
    {
        CacheLayers();
        _showLanding = false;
        _repeatCountdown = false;
        if (_trail != null)
        {
            _trail.emitting = false;
            _trail.Clear();
        }
        if (_flightSmoke != null)
            _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_landingRoot != null)
            _landingRoot.SetActive(false);
        if (_flightRoot != null)
            _flightRoot.SetActive(false);
        if (_shellRoot != null)
            _shellRoot.gameObject.SetActive(false);
    }

    private void Awake() => CacheLayers();

    private void LateUpdate()
    {
        if (_showLanding || _repeatCountdown)
            KeepIndicatorWorldAnchored();
    }

    private void OnValidate()
    {
        _surfaceOffset = Mathf.Max(0.001f, _surfaceOffset);
        _cached = false;
    }

    private void CacheLayers()
    {
        if (_cached)
            return;
        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        _shellRenderers ??= System.Array.Empty<Renderer>();
        _indicatorRenderers ??= System.Array.Empty<Renderer>();
        if (_blastRadiusRing != null)
            _blastBaseScale = _blastRadiusRing.localScale;
        if (_countdownRing != null)
            _countdownBaseScale = _countdownRing.localScale;
        if (_landingCore != null)
            _coreBaseScale = _landingCore.localScale;
    }

    private void ConfigureTrail(bool detailed)
    {
        if (_trail == null)
            return;
        _trail.Clear();
        _trail.time = detailed ? 0.2f : 0.08f;
        _trail.widthMultiplier = detailed ? 0.11f : 0.045f;
        _trail.emitting = true;
    }

    private void ConfigureSmoke(bool detailed)
    {
        if (_flightSmoke == null)
            return;
        ParticleSystem.EmissionModule emission = _flightSmoke.emission;
        emission.rateOverTime = detailed ? 14f : 0f;
        _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (detailed)
            _flightSmoke.Play(true);
    }

    private void ApplyColors(Color color, float intensity)
    {
        float emission = Mathf.Max(0.2f, intensity) * Mathf.Lerp(2.2f, 3.5f, _heat);
        ApplyColorTo(_shellRenderers, color, emission);
        Color indicator = color;
        indicator.a = 0.72f;
        ApplyColorTo(_indicatorRenderers, indicator, emission * 0.82f);
    }

    private void ApplyColorTo(Renderer[] renderers, Color color, float emission)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, _heat);
            _propertyBlock.SetFloat(PulseId, Mathf.Clamp01(emission / 3f));
            _propertyBlock.SetFloat(DissolveId, 0f);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void KeepIndicatorWorldAnchored()
    {
        if (_landingRoot == null || !_landingRoot.activeSelf)
            return;
        _landingRoot.transform.position = _target + _impactNormal * _surfaceOffset;
        _landingRoot.transform.rotation = Quaternion.FromToRotation(Vector3.up, _impactNormal);
    }

    private static Vector3 GetStableUp(Vector3 direction)
    {
        Vector3 normalized = direction.normalized;
        return Mathf.Abs(Vector3.Dot(normalized, Vector3.up)) > 0.96f ? Vector3.forward : Vector3.up;
    }
}
