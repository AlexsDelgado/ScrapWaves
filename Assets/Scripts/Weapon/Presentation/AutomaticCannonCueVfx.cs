using UnityEngine;

public enum AutomaticCannonVfxStyle
{
    AutomaticShot,
    ManualShot,
    Impact,
    CriticalImpact,
    WeakPointImpact,
    BaseActive,
    ContinuousLoop,
    ContinuousStop,
    HeadHunterShot,
    HeadHunterCharge,
    HeadHunterRelease,
    KillImpact,
    HeatPulse
}

[DisallowMultipleComponent]
public sealed class AutomaticCannonCueVfx : MonoBehaviour, IWeaponVfxPrewarm, IWeaponVfxContextReceiver
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [Header("Authored identity")]
    [SerializeField] private AutomaticCannonVfxStyle _style;
    [SerializeField] private Color _primaryColor = new(1f, 0.42f, 0.04f, 1f);
    [SerializeField] private Color _coreColor = new(1f, 0.94f, 0.62f, 1f);
    [SerializeField, Tooltip("Legacy texture reference retained for source compatibility; geometry and shader layers are authoritative.")]
    private Texture2D _muzzleFlashTexture;
    [SerializeField] private Texture2D _sparkTexture;

    [Header("Authored layers")]
    [SerializeField] private Renderer[] _meshLayers;
    [SerializeField] private ParticleSystem[] _particleLayers;
    [SerializeField] private Transform[] _animatedRoots;
    [SerializeField] private Light _lightPulse;

    [Header("Timing")]
    [SerializeField, Min(0.02f)] private float _lifetime = 0.16f;
    [SerializeField, Min(0.05f)] private float _size = 1f;
    [SerializeField] private AnimationCurve _scaleOverLife = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f);
    [SerializeField] private AnimationCurve _emissionOverLife = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private AnimationCurve _dissolveOverLife = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Min(0f)] private float _rotationDegreesPerSecond = 180f;
    [SerializeField, Min(0f)] private float _baseEmission = 2f;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _baseScales;
    private int[] _baseMaxParticles;
    private float _elapsed;
    private float _normalizedHeat;
    private float _intensity = 1f;
    private float _heatEmissionMultiplier = 1f;
    private float _heatSmokeMultiplier = 1f;
    private float _heatSparkMultiplier = 1f;
    private Color _reducedFlashColor;
    private float _reducedFlashIntensity = 0.35f;
    private GameFeelQualityLevel _quality = GameFeelQualityLevel.High;
    private bool _reducedFlash;
    private bool _cached;

    public AutomaticCannonVfxStyle Style => _style;
    public int RuntimeLineCount => 0;
    public int RuntimeParticleSystemCount => _particleLayers?.Length ?? 0;
    public bool HasAuthoredTextures =>
        (_muzzleFlashTexture != null && _sparkTexture != null) ||
        (_meshLayers != null && _meshLayers.Length > 0 && _particleLayers != null && _particleLayers.Length > 0);

    public void Prewarm()
    {
        CacheAuthoredLayers();
        SetLightEnabled(false);
    }

    public void ApplyContext(in WeaponPresentationContext context)
    {
        CacheAuthoredLayers();
        _normalizedHeat = context.NormalizedHeat;
        _intensity = Mathf.Max(0f, context.Intensity);
        _heatEmissionMultiplier = context.HeatEmissionMultiplier;
        _heatSmokeMultiplier = context.HeatSmokeMultiplier;
        _heatSparkMultiplier = context.HeatSparkMultiplier;
        _reducedFlashColor = context.ReducedFlashColor;
        _reducedFlashIntensity = context.ReducedFlashIntensity;
        _quality = context.Quality;
        _reducedFlash = context.ReducedFlash;
        ApplyParticleBudget();
        ApplyFrame(0f);
    }

    private void Awake()
    {
        CacheAuthoredLayers();
    }

    private void OnEnable()
    {
        CacheAuthoredLayers();
        _elapsed = 0f;
        SetLightEnabled(true);
        ApplyFrame(0f);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float normalized = Mathf.Clamp01(_elapsed / Mathf.Max(0.02f, _lifetime));
        ApplyFrame(normalized);

        if (_animatedRoots != null && _rotationDegreesPerSecond > 0f)
        {
            float rotation = _rotationDegreesPerSecond * Time.unscaledDeltaTime;
            for (int i = 0; i < _animatedRoots.Length; i++)
            {
                if (_animatedRoots[i] != null)
                    _animatedRoots[i].Rotate(0f, 0f, rotation, Space.Self);
            }
        }
    }

    private void OnDisable()
    {
        SetLightEnabled(false);
    }

    private void OnValidate()
    {
        _lifetime = Mathf.Max(0.02f, _lifetime);
        _size = Mathf.Max(0.05f, _size);
        _rotationDegreesPerSecond = Mathf.Max(0f, _rotationDegreesPerSecond);
        _baseEmission = Mathf.Max(0f, _baseEmission);
        _scaleOverLife ??= AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f);
        _emissionOverLife ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        _dissolveOverLife ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        _cached = false;
    }

    private void CacheAuthoredLayers()
    {
        if (_cached)
            return;

        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_meshLayers == null || _meshLayers.Length == 0)
            _meshLayers = GetComponentsInChildren<Renderer>(true);
        if (_particleLayers == null || _particleLayers.Length == 0)
            _particleLayers = GetComponentsInChildren<ParticleSystem>(true);
        if (_animatedRoots == null)
            _animatedRoots = System.Array.Empty<Transform>();

        _baseScales = new Vector3[_animatedRoots.Length];
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _baseScales[i] = _animatedRoots[i].localScale;
        }

        _baseMaxParticles = new int[_particleLayers.Length];
        for (int i = 0; i < _particleLayers.Length; i++)
        {
            if (_particleLayers[i] != null)
                _baseMaxParticles[i] = _particleLayers[i].main.maxParticles;
        }
    }

    private void ApplyFrame(float normalizedLife)
    {
        float styleHeat = UsesColdHeadHunterPalette() ? 0f : _normalizedHeat;
        Color heatColor = Color.Lerp(_primaryColor, _coreColor, styleHeat);
        if (UsesColdHeadHunterPalette())
            heatColor = Color.Lerp(new Color(0.25f, 0.72f, 1f), Color.white, _normalizedHeat);
        if (_reducedFlash)
            heatColor = Color.Lerp(heatColor, _reducedFlashColor, 0.65f);

        float flashScale = _reducedFlash ? _reducedFlashIntensity : 1f;
        float emission = _baseEmission * _emissionOverLife.Evaluate(normalizedLife) *
                         _heatEmissionMultiplier * _intensity * flashScale;
        float pulse = Mathf.Clamp01(_intensity * (1f - normalizedLife));
        float dissolve = Mathf.Clamp01(_dissolveOverLife.Evaluate(normalizedLife));

        for (int i = 0; i < _meshLayers.Length; i++)
        {
            Renderer renderer = _meshLayers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, heatColor);
            _propertyBlock.SetColor(EmissionColorId, heatColor * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, _normalizedHeat);
            _propertyBlock.SetFloat(PulseId, pulse * flashScale);
            _propertyBlock.SetFloat(DissolveId, dissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float scale = _size * Mathf.Max(0f, _scaleOverLife.Evaluate(normalizedLife));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }

        if (_lightPulse != null)
        {
            _lightPulse.color = heatColor;
            _lightPulse.intensity = emission;
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
            float heatMultiplier = i == 0 ? _heatSparkMultiplier : _heatSmokeMultiplier;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(authoredMaximum * qualityMultiplier * heatMultiplier));
        }
    }

    private bool UsesColdHeadHunterPalette()
    {
        return _style == AutomaticCannonVfxStyle.HeadHunterShot ||
               _style == AutomaticCannonVfxStyle.HeadHunterCharge ||
               _style == AutomaticCannonVfxStyle.HeadHunterRelease ||
               _style == AutomaticCannonVfxStyle.WeakPointImpact;
    }

    private void SetLightEnabled(bool value)
    {
        if (_lightPulse != null)
            _lightPulse.enabled = value && _quality == GameFeelQualityLevel.High;
    }
}
