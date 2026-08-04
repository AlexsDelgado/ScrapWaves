using UnityEngine;

public enum RotatingBladeCueStyle
{
    ContactSparks,
    MultiBladeFinalImpact,
    AtomicSliceImpact
}

[DisallowMultipleComponent]
public sealed class RotatingBladeCueVfx : MonoBehaviour, IWeaponVfxPrewarm, IWeaponVfxContextReceiver
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    [SerializeField] private RotatingBladeCueStyle _style;
    [SerializeField] private Color _primaryColor = new(0.65f, 0.95f, 1f, 0.95f);
    [SerializeField] private Color _coreColor = Color.white;
    [SerializeField] private Renderer[] _meshLayers;
    [SerializeField] private ParticleSystem[] _particleLayers;
    [SerializeField] private Transform[] _animatedRoots;
    [SerializeField, Min(0.05f)] private float _lifetime = 0.42f;
    [SerializeField, Min(0f)] private float _baseEmission = 3.2f;
    [SerializeField, Min(0f)] private float _rotationDegreesPerSecond = 180f;
    [SerializeField] private AnimationCurve _scaleOverLife = new(
        new Keyframe(0f, 0.32f),
        new Keyframe(0.18f, 1f),
        new Keyframe(1f, 1.28f));

    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _baseScales;
    private int[] _baseMaxParticles;
    private float _elapsed;
    private float _intensity = 1f;
    private float _heat;
    private bool _cached;

    public RotatingBladeCueStyle Style => _style;
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
        _intensity = Mathf.Max(0.25f, context.Intensity);
        _heat = context.NormalizedHeat;
        ApplyParticleBudget(context.HeatSparkMultiplier, context.Quality);
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
                _animatedRoots[i].Rotate(0f, 0f, rotation, Space.Self);
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
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalized);
        Color color = Color.Lerp(_coreColor, _primaryColor, Mathf.Clamp01(normalized * 1.4f));
        color.a *= alpha;
        float emission = _baseEmission * _intensity * Mathf.Lerp(1f + _heat * 0.4f, 0.12f, normalized);
        float dissolve = Mathf.Clamp01(normalized * (_style == RotatingBladeCueStyle.AtomicSliceImpact ? 1f : 0.82f));

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
            _propertyBlock.SetFloat(PulseId, Mathf.Lerp(1f, 0.25f, normalized));
            _propertyBlock.SetFloat(DissolveId, dissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float scale = Mathf.Max(0f, _scaleOverLife.Evaluate(normalized));
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }
    }

    private void ApplyParticleBudget(float sparkMultiplier, GameFeelQualityLevel quality)
    {
        float qualityScale = quality switch
        {
            GameFeelQualityLevel.Low => 0.48f,
            GameFeelQualityLevel.Medium => 0.72f,
            _ => 1f
        };
        for (int i = 0; i < _particleLayers.Length; i++)
        {
            ParticleSystem particles = _particleLayers[i];
            if (particles == null)
                continue;
            ParticleSystem.MainModule main = particles.main;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(_baseMaxParticles[i] * qualityScale * Mathf.Max(0.25f, sparkMultiplier)));
        }
    }
}
