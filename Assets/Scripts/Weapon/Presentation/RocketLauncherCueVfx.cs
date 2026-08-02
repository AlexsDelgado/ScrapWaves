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
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

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
            ? Mathf.Max(0.05f, context.ExplosionRadius * _explosionRadiusMultiplier)
            : 1f;
        if (_forwardMiniExplosionRoot != null)
        {
            float coneRange = Mathf.Max(0.05f, context.ExplosionRadius * _forwardConeRangeMultiplier);
            _forwardMiniExplosionRoot.localScale = Vector3.one * coneRange;
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
                _baseScales[i] = _animatedRoots[i].localScale;
        }

        _miniExplosionBaseScales = new Vector3[_forwardMiniExplosions.Length];
        _nearestMiniExplosionDepth = float.PositiveInfinity;
        _furthestMiniExplosionDepth = float.NegativeInfinity;
        for (int i = 0; i < _forwardMiniExplosions.Length; i++)
        {
            if (_forwardMiniExplosions[i] != null)
            {
                _miniExplosionBaseScales[i] = _forwardMiniExplosions[i].localScale;
                float depth = _forwardMiniExplosions[i].localPosition.z;
                _nearestMiniExplosionDepth = Mathf.Min(_nearestMiniExplosionDepth, depth);
                _furthestMiniExplosionDepth = Mathf.Max(_furthestMiniExplosionDepth, depth);
            }
        }
        if (float.IsInfinity(_nearestMiniExplosionDepth))
            _nearestMiniExplosionDepth = _furthestMiniExplosionDepth = 0f;

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
            : Color.Lerp(_primaryColor, _coreColor, Mathf.Clamp01(1f - normalizedLife) * 0.45f);
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
            _propertyBlock.SetFloat(PulseId, Mathf.Clamp01(_intensity * (1f - normalizedLife)));
            _propertyBlock.SetFloat(DissolveId, dissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float scale = _size * _contextScale * Mathf.Max(0f, _scaleOverLife.Evaluate(normalizedLife));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }
        ApplyForwardMiniExplosions(normalizedLife);

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
