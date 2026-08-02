using UnityEngine;

public enum FlamethrowerCueStyle
{
    FlameNozzleLoop,
    JellifiedNozzleLoop,
    NitrogenNozzleLoop,
    FlameActiveBurst,
    JellifiedActiveBurst,
    NitrogenActiveBurst,
    BurnCoating,
    JellifiedCoating,
    NitrogenSlow,
    NitrogenFreeze,
    SustainedStop
}

[DisallowMultipleComponent]
public sealed class FlamethrowerCueVfx : MonoBehaviour, IWeaponVfxPrewarm, IWeaponVfxContextReceiver
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [SerializeField] private FlamethrowerCueStyle _style;
    [SerializeField] private Color _primaryColor = new(1f, 0.2f, 0.02f, 0.9f);
    [SerializeField] private Color _coreColor = new(1f, 0.92f, 0.45f, 1f);
    [SerializeField] private Renderer[] _meshLayers;
    [SerializeField] private ParticleSystem[] _particleLayers;
    [SerializeField] private Transform[] _animatedRoots;
    [SerializeField] private Light _lightPulse;
    [SerializeField, Min(0.02f)] private float _lifetime = 0.6f;
    [SerializeField, Min(0.02f)] private float _size = 1f;
    [SerializeField, Min(0f)] private float _baseEmission = 2.4f;
    [SerializeField, Min(0f)] private float _rotationDegreesPerSecond = 90f;
    [SerializeField] private bool _scaleFromExplosionRadius;
    [SerializeField, Min(0f)] private float _explosionRadiusMultiplier = 1f;
    [SerializeField] private AnimationCurve _scaleOverLife = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
    [SerializeField] private AnimationCurve _emissionOverLife = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private AnimationCurve _dissolveOverLife = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _baseScales;
    private int[] _baseMaxParticles;
    private float _elapsed;
    private float _intensity = 1f;
    private float _contextScale = 1f;
    private float _heat;
    private float _emissionMultiplier = 1f;
    private float _smokeMultiplier = 1f;
    private float _sparkMultiplier = 1f;
    private bool _reducedFlash;
    private Color _reducedFlashColor;
    private float _reducedFlashIntensity = 0.35f;
    private GameFeelQualityLevel _quality = GameFeelQualityLevel.High;
    private bool _cached;

    public FlamethrowerCueStyle Style => _style;
    public int RuntimeMeshLayerCount => _meshLayers?.Length ?? 0;
    public int RuntimeParticleSystemCount => _particleLayers?.Length ?? 0;

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
            ? Mathf.Max(0.05f, context.ExplosionRadius * _explosionRadiusMultiplier)
            : 1f;
        _heat = context.NormalizedHeat;
        _emissionMultiplier = context.HeatEmissionMultiplier;
        _smokeMultiplier = context.HeatSmokeMultiplier;
        _sparkMultiplier = context.HeatSparkMultiplier;
        _quality = context.Quality;
        _reducedFlash = context.ReducedFlash;
        _reducedFlashColor = context.ReducedFlashColor;
        _reducedFlashIntensity = context.ReducedFlashIntensity;
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
        bool looping = _style == FlamethrowerCueStyle.FlameNozzleLoop ||
                       _style == FlamethrowerCueStyle.JellifiedNozzleLoop ||
                       _style == FlamethrowerCueStyle.NitrogenNozzleLoop ||
                       _style == FlamethrowerCueStyle.BurnCoating ||
                       _style == FlamethrowerCueStyle.JellifiedCoating ||
                       _style == FlamethrowerCueStyle.NitrogenSlow;
        float normalized = looping
            ? Mathf.Repeat(_elapsed / Mathf.Max(0.02f, _lifetime), 1f)
            : Mathf.Clamp01(_elapsed / Mathf.Max(0.02f, _lifetime));
        ApplyFrame(normalized);
        if (_animatedRoots == null || _rotationDegreesPerSecond <= 0f)
            return;
        float rotation = _rotationDegreesPerSecond * Time.unscaledDeltaTime;
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].Rotate(0f, rotation, 0f, Space.Self);
        }
    }

    private void OnDisable() => SetLightEnabled(false);

    private void OnValidate()
    {
        _lifetime = Mathf.Max(0.02f, _lifetime);
        _size = Mathf.Max(0.02f, _size);
        _baseEmission = Mathf.Max(0f, _baseEmission);
        _explosionRadiusMultiplier = Mathf.Max(0f, _explosionRadiusMultiplier);
        _scaleOverLife ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        _emissionOverLife ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
        _dissolveOverLife ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
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
        float flashScale = _reducedFlash ? _reducedFlashIntensity : 1f;
        Color color = _reducedFlash
            ? Color.Lerp(_primaryColor, _reducedFlashColor, 0.7f)
            : Color.Lerp(_primaryColor, _coreColor, (1f - normalizedLife) * 0.35f);
        float emission = _baseEmission * Mathf.Max(0f, _emissionOverLife.Evaluate(normalizedLife)) *
                         _emissionMultiplier * _intensity * flashScale;
        float dissolve = Mathf.Clamp01(_dissolveOverLife.Evaluate(normalizedLife));

        for (int i = 0; i < _meshLayers.Length; i++)
        {
            Renderer renderer = _meshLayers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, _heat);
            _propertyBlock.SetFloat(PulseId, Mathf.Clamp01(_intensity * (1f - normalizedLife * 0.35f)));
            _propertyBlock.SetFloat(DissolveId, dissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float scale = _size * _contextScale * Mathf.Max(0f, _scaleOverLife.Evaluate(normalizedLife));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }
        if (_lightPulse != null)
        {
            _lightPulse.color = color;
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
            int authored = i < _baseMaxParticles.Length ? _baseMaxParticles[i] : main.maxParticles;
            float layerMultiplier = i == 0 ? _sparkMultiplier : _smokeMultiplier;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(authored * qualityMultiplier * layerMultiplier));
        }
    }

    private void SetLightEnabled(bool value)
    {
        if (_lightPulse != null)
            _lightPulse.enabled = value && _quality == GameFeelQualityLevel.High;
    }
}
