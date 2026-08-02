using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectileVisualController : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");

    [SerializeField] private Transform _visualRoot;
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private TrailRenderer _trail;
    [SerializeField] private ParticleSystem _flightSmoke;
    [SerializeField] private Light _light;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _defaultScale = Vector3.one;
    private Quaternion _defaultRotation = Quaternion.identity;
    private bool _cached;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        _trail?.Clear();
        _flightSmoke?.Clear(true);
    }

    private void OnDisable()
    {
        _trail?.Clear();
        if (_flightSmoke != null)
        {
            _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _flightSmoke.Clear(true);
        }
        if (_light != null)
            _light.enabled = false;
    }

    public void Apply(
        ProjectileArchetypePresentation archetype,
        WeaponHeatPresentationSettings heat,
        float normalizedHeat,
        bool heatEnabled,
        GameFeelQualityLevel quality,
        GameFeelQualitySettings qualitySettings)
    {
        CacheReferences();
        if (archetype == null || _visualRoot == null)
            return;

        archetype.Sanitize();
        if (_meshFilter != null && archetype.Mesh != null)
            _meshFilter.sharedMesh = archetype.Mesh;
        if (_meshRenderer != null && archetype.Material != null)
            _meshRenderer.sharedMaterial = archetype.Material;

        _visualRoot.localScale = Vector3.Scale(_defaultScale, archetype.LocalScale);
        _visualRoot.localRotation = _defaultRotation * Quaternion.Euler(archetype.LocalEulerAngles);

        float heatValue = heatEnabled ? Mathf.Clamp01(normalizedHeat) : 0f;
        float emission = archetype.BaseEmission;
        Color color = Color.white;
        float trailWidthScale = 1f;
        if (heat != null)
        {
            emission *= Mathf.Max(0f, heat.Emission.Evaluate(heatValue));
            color = heat.Color.Evaluate(heatValue);
            trailWidthScale = Mathf.Max(0f, heat.TrailWidth.Evaluate(heatValue));
        }

        if (_meshRenderer != null)
        {
            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, heatValue);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        if (_trail != null)
        {
            _trail.Clear();
            _trail.enabled = archetype.TrailLifetime > 0f && archetype.TrailStartWidth > 0f;
            _trail.time = archetype.TrailLifetime;
            _trail.startWidth = archetype.TrailStartWidth * trailWidthScale;
            _trail.endWidth = archetype.TrailEndWidth * trailWidthScale;
            _trail.colorGradient = archetype.TrailColor;
            if (archetype.TrailMaterial != null)
                _trail.sharedMaterial = archetype.TrailMaterial;
        }

        if (_flightSmoke != null)
        {
            bool smokeEnabled = archetype.FlightSmokeRate > 0f &&
                                archetype.FlightSmokeSize > 0f &&
                                archetype.FlightSmokeLifetime > 0f;
            ParticleSystem.MainModule main = _flightSmoke.main;
            main.startLifetime = archetype.FlightSmokeLifetime;
            main.startSize = archetype.FlightSmokeSize;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule smokeEmission = _flightSmoke.emission;
            smokeEmission.rateOverTime = smokeEnabled ? archetype.FlightSmokeRate : 0f;
            ParticleSystemRenderer smokeRenderer = _flightSmoke.GetComponent<ParticleSystemRenderer>();
            if (smokeRenderer != null && archetype.FlightSmokeMaterial != null)
                smokeRenderer.sharedMaterial = archetype.FlightSmokeMaterial;
            _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (smokeEnabled)
                _flightSmoke.Play(true);
        }

        if (_light != null)
        {
            bool allowLights = qualitySettings == null || qualitySettings.AllowLights(quality);
            _light.enabled = allowLights && archetype.LightIntensity > 0f;
            _light.intensity = archetype.LightIntensity * emission;
            _light.range = archetype.LightRange;
            _light.color = color;
        }
    }

    public void ResetVisual()
    {
        if (_visualRoot != null)
        {
            _visualRoot.localScale = _defaultScale;
            _visualRoot.localRotation = _defaultRotation;
        }
        _trail?.Clear();
        if (_flightSmoke != null)
            _flightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_light != null)
            _light.enabled = false;
    }

    private void CacheReferences()
    {
        if (_cached)
            return;

        _cached = true;
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_visualRoot == null)
            _visualRoot = transform.Find("Visual");
        if (_visualRoot == null)
            _visualRoot = transform;
        if (_meshFilter == null)
            _meshFilter = _visualRoot.GetComponentInChildren<MeshFilter>(true);
        if (_meshRenderer == null)
            _meshRenderer = _visualRoot.GetComponentInChildren<MeshRenderer>(true);
        if (_trail == null)
            _trail = _visualRoot.GetComponentInChildren<TrailRenderer>(true);
        if (_flightSmoke == null)
            _flightSmoke = _visualRoot.Find("Rocket Flight Smoke")?.GetComponent<ParticleSystem>();
        if (_light == null)
            _light = _visualRoot.GetComponentInChildren<Light>(true);
        _defaultScale = _visualRoot.localScale;
        _defaultRotation = _visualRoot.localRotation;
    }
}
