using UnityEngine;

public enum MortarCueStyle
{
    Launch,
    BarrageWarning,
    Impact,
    GrapeshotAirburst,
    GrapeshotImpact,
    MultiChargedImpact,
    MultiChargedRepeat
}

[DisallowMultipleComponent]
public sealed class MortarCueVfx : MonoBehaviour, IWeaponVfxPrewarm, IWeaponVfxContextReceiver
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [SerializeField] private MortarCueStyle _style;
    [SerializeField] private Color _primaryColor = new(1f, 0.42f, 0.06f, 0.95f);
    [SerializeField] private Color _coreColor = new(1f, 0.92f, 0.62f, 1f);
    [SerializeField] private Renderer[] _meshLayers;
    [SerializeField] private ParticleSystem[] _particleLayers;
    [SerializeField] private Transform[] _animatedRoots;
    [SerializeField, Min(0.05f)] private float _lifetime = 0.72f;
    [SerializeField, Min(0f)] private float _baseEmission = 3.4f;
    [SerializeField, Min(0f)] private float _rotationDegreesPerSecond;
    [SerializeField] private AnimationCurve _scaleOverLife = new(
        new Keyframe(0f, 0.2f),
        new Keyframe(0.16f, 1f),
        new Keyframe(1f, 1.3f));

    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _baseScales;
    private int[] _baseMaxParticles;
    private float _elapsed;
    private float _intensity = 1f;
    private float _heat;
    private float _radiusScale = 1f;
    private bool _cached;

    public MortarCueStyle Style => _style;
    public int RuntimeMeshLayerCount => _meshLayers?.Length ?? 0;
    public int RuntimeParticleSystemCount => _particleLayers?.Length ?? 0;

    public void Prewarm()
    {
        CacheLayers();
        ApplyFrame(1f);
    }

    public void ApplyContext(in WeaponPresentationContext context)
    {
        CacheLayers();
        if (_style != MortarCueStyle.Launch)
        {
            Vector3 normal = _style == MortarCueStyle.GrapeshotAirburst
                ? Vector3.up
                : context.ImpactNormal;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        }
        _intensity = Mathf.Max(0.25f, context.Intensity);
        _heat = context.NormalizedHeat;
        _radiusScale = ResolveRadiusScale(context.ExplosionRadius);
        ApplyParticleBudget(context.HeatSmokeMultiplier, context.HeatSparkMultiplier, context.Quality);
        ApplyFrame(0f);
    }

    private void Awake() => CacheLayers();

    private void OnEnable()
    {
        CacheLayers();
        _elapsed = 0f;
        ApplyFrame(0f);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float normalized = Mathf.Clamp01(_elapsed / Mathf.Max(0.05f, _lifetime));
        ApplyFrame(normalized);
        float rotation = _rotationDegreesPerSecond * Time.unscaledDeltaTime;
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].Rotate(0f, rotation, 0f, Space.Self);
        }
    }

    private void OnValidate()
    {
        _lifetime = Mathf.Max(0.05f, _lifetime);
        _baseEmission = Mathf.Max(0f, _baseEmission);
        _rotationDegreesPerSecond = Mathf.Max(0f, _rotationDegreesPerSecond);
        _scaleOverLife ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        _cached = false;
    }

    private void CacheLayers()
    {
        if (_cached)
            return;
        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        _meshLayers ??= GetComponentsInChildren<Renderer>(true);
        _particleLayers ??= GetComponentsInChildren<ParticleSystem>(true);
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

    private void ApplyFrame(float normalized)
    {
        bool barrage = _style == MortarCueStyle.BarrageWarning;
        float envelope = barrage
            ? Mathf.Lerp(0.72f, 0.18f, Mathf.SmoothStep(0f, 1f, normalized))
            : 1f - Mathf.SmoothStep(0.05f, 1f, normalized);
        float rhythmicPulse = barrage
            ? 0.68f + 0.32f * Mathf.Sin((_elapsed * 3.8f + normalized * 4f) * Mathf.PI * 2f)
            : 1f;
        Color color = Color.Lerp(_coreColor, _primaryColor, Mathf.Clamp01(normalized * 1.65f));
        color.a *= envelope;
        float emission = _baseEmission * _intensity * Mathf.Lerp(1f + _heat * 0.35f, 0.12f, normalized) * rhythmicPulse;
        float dissolve = barrage ? Mathf.Clamp01(normalized * 0.7f) : Mathf.Clamp01(normalized * 0.92f);

        for (int i = 0; i < _meshLayers.Length; i++)
        {
            Renderer renderer = _meshLayers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, _heat);
            _propertyBlock.SetFloat(PulseId, barrage ? rhythmicPulse : Mathf.Lerp(1f, 0.1f, normalized));
            _propertyBlock.SetFloat(DissolveId, dissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float lifeScale = Mathf.Max(0f, _scaleOverLife.Evaluate(normalized));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] == null)
                continue;
            float radius = _style == MortarCueStyle.Launch ? 1f : _radiusScale;
            _animatedRoots[i].localScale = _baseScales[i] * lifeScale * radius;
        }
    }

    private float ResolveRadiusScale(float explosionRadius)
    {
        if (_style == MortarCueStyle.Launch)
            return 1f;
        if (_style == MortarCueStyle.GrapeshotAirburst)
            return Mathf.Max(0.8f, explosionRadius * 0.55f);
        if (_style == MortarCueStyle.GrapeshotImpact)
            return Mathf.Clamp(explosionRadius * 2.1f, 0.42f, 1.15f);
        return Mathf.Clamp(explosionRadius * 2f, 1.6f, 13f);
    }

    private void ApplyParticleBudget(float smokeMultiplier, float sparkMultiplier, GameFeelQualityLevel quality)
    {
        float qualityScale = quality switch
        {
            GameFeelQualityLevel.Low => 0.45f,
            GameFeelQualityLevel.Medium => 0.72f,
            _ => 1f
        };
        for (int i = 0; i < _particleLayers.Length; i++)
        {
            ParticleSystem particles = _particleLayers[i];
            if (particles == null)
                continue;
            float response = particles.gameObject.name.Contains("Smoke") || particles.gameObject.name.Contains("Dirt")
                ? smokeMultiplier
                : sparkMultiplier;
            ParticleSystem.MainModule main = particles.main;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(_baseMaxParticles[i] * qualityScale * Mathf.Max(0.25f, response)));
        }
    }
}
