using UnityEngine;
using UnityEngine.Rendering;

public enum FlamethrowerStreamStyle
{
    Flame,
    JellifiedFuel,
    LiquidNitrogen
}

[DisallowMultipleComponent]
public sealed class FlamethrowerStreamVfx : MonoBehaviour
{
    private const int BodyRadialSideCount = 8;
    private const int CoreRadialSideCount = 6;
    private const int BillowLongitudeCount = 12;
    private const int BillowLatitudeCount = 8;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int HotColorId = Shader.PropertyToID("_HotColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedId = Shader.PropertyToID("_NoiseSpeed");

    private static readonly Color FlameCore = new(1f, 0.82f, 0.24f, 0.98f);
    private static readonly Color FlameBody = new(1f, 0.16f, 0.015f, 0.78f);
    private static readonly Color FuelCore = new(0.74f, 0.92f, 0.14f, 0.94f);
    private static readonly Color FuelBody = new(0.025f, 0.22f, 0.055f, 0.86f);
    private static readonly Color NitrogenCore = new(0.96f, 1f, 1f, 0.96f);
    private static readonly Color NitrogenBody = new(0.43f, 0.68f, 0.84f, 0.82f);

    [Header("Procedural ribbons")]
    [SerializeField] private MeshFilter _bodyFilter;
    [SerializeField] private MeshRenderer _bodyRenderer;
    [SerializeField] private MeshFilter _coreFilter;
    [SerializeField] private MeshRenderer _coreRenderer;
    [SerializeField] private MeshFilter _billowFilter;
    [SerializeField] private MeshRenderer _billowRenderer;
    [SerializeField, Range(2, 48)] private int _maximumSegments = 48;
    [SerializeField] private AnimationCurve _ribbonWidth = new(
        new Keyframe(0f, 0.08f),
        new Keyframe(0.12f, 0.17f),
        new Keyframe(0.25f, 0.38f),
        new Keyframe(0.38f, 0.62f),
        new Keyframe(0.5f, 0.82f),
        new Keyframe(0.6f, 0.96f),
        new Keyframe(0.72f, 0.9f),
        new Keyframe(0.84f, 1f),
        new Keyframe(0.93f, 0.88f),
        new Keyframe(1f, 0.45f));
    [SerializeField, Range(0.05f, 1f)] private float _coreWidthMultiplier = 0.34f;
    [SerializeField, Range(0.1f, 1f)] private float _automaticWidthMultiplier = 0.52f;
    [SerializeField, Range(0.1f, 1f)] private float _automaticHeightMultiplier = 0.22f;

    [Header("Automatic turbulent cone")]
    [SerializeField, Range(3, 12)] private int _automaticBillowCount = 11;
    [SerializeField, Range(0.2f, 0.75f)] private float _automaticBillowRadiusMultiplier = 0.48f;
    [SerializeField, Range(0.1f, 3f)] private float _automaticBillowTravelSpeed = 1.65f;
    [SerializeField, Min(0.1f)] private float _automaticReleaseDuration = 0.42f;

    [Header("Manual rolling plume")]
    [SerializeField, Range(3, 12)] private int _manualBillowCount = 12;
    [SerializeField, Range(0.75f, 1.75f)] private float _manualBillowRadiusMultiplier = 1.32f;
    [SerializeField, Range(0.1f, 3f)] private float _manualBillowTravelSpeed = 1.35f;
    [SerializeField, Range(0.2f, 1f)] private float _manualTubeWidthMultiplier = 0.58f;
    [SerializeField, Range(0.05f, 0.8f)] private float _manualBodyOpacity = 0.28f;
    [SerializeField, Min(0.1f)] private float _manualReleaseDuration = 0.55f;

    [Header("Surface motion")]
    [SerializeField, Min(0.1f)] private float _noiseScale = 6.5f;
    [SerializeField, Min(0f)] private float _noiseSpeed = 3.8f;
    [SerializeField, Min(0f)] private float _erosionSpeed = 1.7f;
    [SerializeField, Min(0f)] private float _baseEmission = 2.6f;
    [SerializeField, Min(0f)] private float _heatEmissionMultiplier = 1.25f;

    [Header("Secondary layers")]
    [SerializeField] private ParticleSystem _embers;
    [SerializeField] private ParticleSystem _smoke;
    [SerializeField] private Renderer _nozzleGlow;
    [SerializeField] private Light _nozzleLight;
    [SerializeField, Min(0f)] private float _emberRate = 26f;
    [SerializeField, Min(0f)] private float _smokeRate = 13f;

    private Mesh _bodyMesh;
    private Mesh _coreMesh;
    private Mesh _billowMesh;
    private Material _plumeMaterial;
    private MaterialPropertyBlock _propertyBlock;
    private FlamethrowerStreamStyle _style;
    private Color _coreColor = FlameCore;
    private Color _bodyColor = FlameBody;
    private float _heat;
    private float _visibleTimer;
    private float _visibleDuration = 0.18f;
    private bool _ringMode;
    private float _ringRadius;
    private float _ringElapsed;
    private bool _manualMode;
    private bool _manualReleasing;
    private bool _automaticReleasing;
    private bool _initialized;

    public int MaximumSegments => _maximumSegments;
    public int BodyRadialSides => BodyRadialSideCount;
    public int CoreRadialSides => CoreRadialSideCount;
    public int BodyVertexCount => _bodyMesh != null ? _bodyMesh.vertexCount : 0;
    public int CoreVertexCount => _coreMesh != null ? _coreMesh.vertexCount : 0;
    public Mesh BodyMesh => _bodyMesh;
    public Mesh CoreMesh => _coreMesh;
    public Mesh BillowMesh => _billowMesh;
    public int BillowVertexCount => _billowMesh != null ? _billowMesh.vertexCount : 0;
    public int ManualBillowCount => _manualBillowCount;
    public int AutomaticBillowCount => _automaticBillowCount;
    public float ManualTubeWidthMultiplier => _manualTubeWidthMultiplier;
    public float ManualBodyOpacity => _manualBodyOpacity;
    public float ManualReleaseDuration => _manualReleaseDuration;
    public float AutomaticReleaseDuration => _automaticReleaseDuration;
    public bool IsManualReleasing => _manualReleasing;
    public bool IsAutomaticReleasing => _automaticReleasing;
    public bool BodyVisible => _bodyRenderer != null && _bodyRenderer.enabled;
    public bool CoreVisible => _coreRenderer != null && _coreRenderer.enabled;
    public bool NozzleGlowVisible => _nozzleGlow != null && _nozzleGlow.enabled;
    public string BodyShaderName => _bodyRenderer != null && _bodyRenderer.sharedMaterial != null
        ? _bodyRenderer.sharedMaterial.shader.name
        : string.Empty;
    public int ParticleLayerCount => (_embers != null ? 1 : 0) + (_smoke != null ? 1 : 0);
    public float AutomaticWidthMultiplier => _automaticWidthMultiplier;
    public float AutomaticHeightMultiplier => _automaticHeightMultiplier;

    public static FlamethrowerStreamVfx Create(GameObject authoredPrefab = null, int maximumSegments = 48)
    {
        FlamethrowerStreamVfx vfx = null;
        if (authoredPrefab != null)
        {
            GameObject instance = Instantiate(authoredPrefab);
            instance.name = "[Flamethrower Stream]";
            vfx = instance.GetComponent<FlamethrowerStreamVfx>();
        }

        if (vfx == null)
        {
            GameObject fallback = new("[Flamethrower Stream - Fallback]");
            vfx = fallback.AddComponent<FlamethrowerStreamVfx>();
        }

        vfx._maximumSegments = Mathf.Clamp(maximumSegments, 2, 48);
        vfx.EnsureInitialized();
        return vfx;
    }

    public static void SpawnRing(Vector3 center, float radius, float duration) =>
        SpawnRing(center, radius, duration, FlameCore, FlameBody);

    public static void SpawnRing(Vector3 center, float radius, float duration, Color coreColor, Color bodyColor)
    {
        if (radius <= 0f)
            return;

        FlamethrowerStreamVfx vfx = Create();
        vfx.name = "[Flamethrower Active Ring - Fallback]";
        vfx.transform.position = center + Vector3.up * 0.055f;
        vfx.SetPalette(coreColor, bodyColor);
        vfx._ringMode = true;
        vfx._ringRadius = radius;
        vfx._visibleDuration = Mathf.Max(0.05f, duration);
        vfx._visibleTimer = vfx._visibleDuration;
        vfx._ringElapsed = 0f;
        vfx.BuildRing(0f);
        vfx.SetSecondaryLayers(false);
    }

    public void SetStyle(FlamethrowerStreamStyle style)
    {
        _style = style;
        switch (style)
        {
            case FlamethrowerStreamStyle.JellifiedFuel:
                SetPalette(FuelCore, FuelBody);
                SetParticlePalette(FuelCore, FuelBody, new Color(0.08f, 0.12f, 0.035f, 0.45f));
                break;
            case FlamethrowerStreamStyle.LiquidNitrogen:
                SetPalette(NitrogenCore, NitrogenBody);
                SetParticlePalette(NitrogenCore, NitrogenBody, new Color(0.74f, 0.86f, 0.9f, 0.38f));
                break;
            default:
                SetPalette(FlameCore, FlameBody);
                SetParticlePalette(FlameCore, FlameBody, new Color(0.18f, 0.12f, 0.095f, 0.4f));
                break;
        }
    }

    public void SetPalette(Color coreColor, Color bodyColor)
    {
        _coreColor = coreColor;
        _bodyColor = bodyColor;
        ApplyMaterialProperties(1f);
    }

    public void SetHeat(float normalizedHeat)
    {
        _heat = Mathf.Clamp01(normalizedHeat);
        ApplyMaterialProperties(1f);
    }

    public void ShowCone(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration)
    {
        EnsureInitialized();
        _manualMode = false;
        _manualReleasing = false;
        _automaticReleasing = false;
        UsePlumeBodyMaterial();
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;
        direction.Normalize();

        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
        int pointCount = Mathf.Clamp(Mathf.CeilToInt(range * 1.4f) + 2, 3, _maximumSegments);
        Vector3[] points = new Vector3[pointCount];
        float[] halfWidths = new float[pointCount];
        float tangent = Mathf.Tan(Mathf.Clamp(coneAngle, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float distance = Mathf.Max(0.01f, range) * t;
            points[i] = Vector3.forward * distance;
            float angularWidth = distance * tangent;
            float radialWidth = Mathf.Sqrt(Mathf.Max(0f, range * range - distance * distance));
            halfWidths[i] = i == 0 || i == pointCount - 1
                ? 0f
                : Mathf.Min(angularWidth, radialWidth);
        }

        BuildVolume(
            points,
            halfWidths,
            pointCount,
            taper: false,
            crossSectionWidth: _automaticWidthMultiplier,
            crossSectionHeight: _automaticHeightMultiplier);
        BuildFlameBillows(
            points,
            halfWidths,
            pointCount,
            _automaticBillowCount,
            _automaticBillowRadiusMultiplier,
            _automaticBillowTravelSpeed,
            coneMode: true);
        ShowFor(duration);
    }

    public void ShowHose(Vector3[] worldPoints, int pointCount, float radius, float duration)
    {
        if (worldPoints == null || pointCount <= 1)
            return;

        EnsureInitialized();
        _manualMode = true;
        _manualReleasing = false;
        _automaticReleasing = false;
        UsePlumeBodyMaterial();
        pointCount = Mathf.Clamp(Mathf.Min(pointCount, worldPoints.Length), 2, _maximumSegments);
        Vector3 firstDirection = worldPoints[1] - worldPoints[0];
        if (firstDirection.sqrMagnitude <= 0.0001f)
            firstDirection = Vector3.forward;
        transform.SetPositionAndRotation(worldPoints[0], Quaternion.LookRotation(firstDirection.normalized, GetStableUp(firstDirection)));

        Vector3[] points = new Vector3[pointCount];
        float[] halfWidths = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            points[i] = transform.InverseTransformPoint(worldPoints[i]);
            halfWidths[i] = Mathf.Max(0.025f, radius * Mathf.Max(0f, _ribbonWidth.Evaluate(t)));
        }

        BuildVolume(
            points,
            halfWidths,
            pointCount,
            taper: true,
            crossSectionWidth: 1f,
            crossSectionHeight: 1f,
            bodyRadiusMultiplier: _manualTubeWidthMultiplier,
            coreRadiusMultiplier: _coreWidthMultiplier * 0.34f,
            bodyIrregularity: 0.22f,
            coreIrregularity: 0.3f,
            coreVisibleEnd: 0.18f);
        BuildFlameBillows(
            points,
            halfWidths,
            pointCount,
            _manualBillowCount,
            _manualBillowRadiusMultiplier,
            _manualBillowTravelSpeed,
            coneMode: false);
        ShowFor(duration);
    }

    public void ReleaseManual()
    {
        EnsureInitialized();
        if (!_manualMode || _manualReleasing || _visibleTimer <= 0f)
            return;

        _manualReleasing = true;
        _visibleDuration = Mathf.Max(0.1f, _manualReleaseDuration);
        _visibleTimer = _visibleDuration;
        SetSecondaryLayers(false);
    }

    public void ReleaseAutomatic()
    {
        EnsureInitialized();
        if (_manualMode || _automaticReleasing || _visibleTimer <= 0f)
            return;

        _automaticReleasing = true;
        _visibleDuration = Mathf.Max(0.1f, _automaticReleaseDuration);
        _visibleTimer = _visibleDuration;
        SetSecondaryLayers(false);
    }

    private void Awake() => EnsureInitialized();

    private void OnValidate()
    {
        _maximumSegments = Mathf.Clamp(_maximumSegments, 2, 48);
        _coreWidthMultiplier = Mathf.Clamp(_coreWidthMultiplier, 0.05f, 1f);
        _automaticWidthMultiplier = Mathf.Clamp(_automaticWidthMultiplier, 0.1f, 1f);
        _automaticHeightMultiplier = Mathf.Clamp(_automaticHeightMultiplier, 0.1f, 1f);
        _automaticBillowCount = Mathf.Clamp(_automaticBillowCount, 3, 12);
        _automaticBillowRadiusMultiplier = Mathf.Clamp(_automaticBillowRadiusMultiplier, 0.2f, 0.75f);
        _automaticBillowTravelSpeed = Mathf.Clamp(_automaticBillowTravelSpeed, 0.1f, 3f);
        _automaticReleaseDuration = Mathf.Max(0.1f, _automaticReleaseDuration);
        _manualBillowCount = Mathf.Clamp(_manualBillowCount, 3, 12);
        _manualBillowRadiusMultiplier = Mathf.Clamp(_manualBillowRadiusMultiplier, 0.75f, 1.75f);
        _manualBillowTravelSpeed = Mathf.Clamp(_manualBillowTravelSpeed, 0.1f, 3f);
        _manualTubeWidthMultiplier = Mathf.Clamp(_manualTubeWidthMultiplier, 0.2f, 1f);
        _manualBodyOpacity = Mathf.Clamp(_manualBodyOpacity, 0.05f, 0.8f);
        _manualReleaseDuration = Mathf.Max(0.1f, _manualReleaseDuration);
        _noiseScale = Mathf.Max(0.1f, _noiseScale);
        _noiseSpeed = Mathf.Max(0f, _noiseSpeed);
        _erosionSpeed = Mathf.Max(0f, _erosionSpeed);
        _ribbonWidth ??= AnimationCurve.Linear(0f, 1f, 1f, 0.1f);
    }

    private void Update()
    {
        if (_visibleTimer <= 0f)
        {
            SetVisible(false);
            if (_ringMode)
                Destroy(gameObject);
            return;
        }

        _visibleTimer -= Time.deltaTime;
        float normalizedLife = 1f - Mathf.Clamp01(_visibleTimer / Mathf.Max(0.01f, _visibleDuration));
        float alpha;
        if (_ringMode)
        {
            alpha = 1f - normalizedLife;
        }
        else if (_manualMode && _manualReleasing)
        {
            float release = Mathf.Clamp01(_visibleTimer / Mathf.Max(0.1f, _manualReleaseDuration));
            alpha = release * release * (3f - 2f * release);
        }
        else if (!_manualMode && _automaticReleasing)
        {
            float release = Mathf.Clamp01(_visibleTimer / Mathf.Max(0.1f, _automaticReleaseDuration));
            alpha = release * release * (3f - 2f * release);
        }
        else
        {
            alpha = Mathf.Clamp01(_visibleTimer / 0.06f);
        }
        if (_ringMode)
        {
            _ringElapsed += Time.deltaTime;
            BuildRing(normalizedLife);
        }
        ApplyMaterialProperties(alpha);
    }

    private void OnDestroy()
    {
        if (_bodyMesh != null)
            DestroyRuntimeObject(_bodyMesh);
        if (_coreMesh != null)
            DestroyRuntimeObject(_coreMesh);
        if (_billowMesh != null)
            DestroyRuntimeObject(_billowMesh);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;
        _propertyBlock = new MaterialPropertyBlock();
        EnsureRibbonLayer(ref _bodyFilter, ref _bodyRenderer, "Flame Body", false);
        EnsureRibbonLayer(ref _coreFilter, ref _coreRenderer, "Flame Core", true);
        EnsureRibbonLayer(ref _billowFilter, ref _billowRenderer, "Rolling Flame Billows", false);
        _bodyMesh = new Mesh { name = "Flamethrower Volumetric Body", hideFlags = HideFlags.DontSave };
        _coreMesh = new Mesh { name = "Flamethrower Volumetric Core", hideFlags = HideFlags.DontSave };
        _billowMesh = new Mesh { name = "Flamethrower Rolling Billows", hideFlags = HideFlags.DontSave };
        _bodyMesh.MarkDynamic();
        _coreMesh.MarkDynamic();
        _billowMesh.MarkDynamic();
        _bodyFilter.sharedMesh = _bodyMesh;
        _coreFilter.sharedMesh = _coreMesh;
        _billowFilter.sharedMesh = _billowMesh;
        _plumeMaterial = _billowRenderer != null ? _billowRenderer.sharedMaterial : null;
        SetVisible(false);
    }

    private void UsePlumeBodyMaterial()
    {
        if (_bodyRenderer != null && _plumeMaterial != null)
            _bodyRenderer.sharedMaterial = _plumeMaterial;
    }

    private void EnsureRibbonLayer(ref MeshFilter filter, ref MeshRenderer renderer, string layerName, bool core)
    {
        if (filter == null)
        {
            GameObject layer = new(layerName);
            layer.transform.SetParent(transform, false);
            filter = layer.AddComponent<MeshFilter>();
            renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateFallbackMaterial(core ? FlameCore : FlameBody);
        }
        else if (renderer == null)
            renderer = filter.GetComponent<MeshRenderer>();

        if (!core && layerName == "Rolling Flame Billows" && _bodyRenderer != null && renderer != null && renderer.sharedMaterial == null)
            renderer.sharedMaterial = _bodyRenderer.sharedMaterial;

        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private void BuildVolume(
        Vector3[] points,
        float[] radii,
        int pointCount,
        bool taper,
        float crossSectionWidth,
        float crossSectionHeight,
        float bodyRadiusMultiplier = 1f,
        float coreRadiusMultiplier = -1f,
        float bodyIrregularity = 0.1f,
        float coreIrregularity = 0.045f,
        float coreVisibleEnd = 1f)
    {
        if (coreRadiusMultiplier < 0f)
            coreRadiusMultiplier = _coreWidthMultiplier;

        BuildTubeMesh(_bodyMesh, points, radii, pointCount, BodyRadialSideCount, bodyRadiusMultiplier, bodyIrregularity, taper, crossSectionWidth, crossSectionHeight, 1f);
        BuildTubeMesh(_coreMesh, points, radii, pointCount, CoreRadialSideCount, coreRadiusMultiplier, coreIrregularity, taper, crossSectionWidth, crossSectionHeight, coreVisibleEnd);
    }

    private void BuildFlameBillows(
        Vector3[] points,
        float[] radii,
        int pointCount,
        int requestedBillowCount,
        float radiusMultiplier,
        float travelSpeed,
        bool coneMode)
    {
        int billowCount = Mathf.Clamp(requestedBillowCount, 3, 12);
        int verticesPerBillow = (BillowLatitudeCount + 1) * BillowLongitudeCount;
        int trianglesPerBillow = BillowLatitudeCount * BillowLongitudeCount * 6;
        Vector3[] vertices = new Vector3[billowCount * verticesPerBillow];
        Vector2[] uvs = new Vector2[vertices.Length];
        Vector2[] uv2 = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[billowCount * trianglesPerBillow];
        float travel = Mathf.Repeat(Time.time * travelSpeed * 0.11f, 1f / billowCount);

        for (int billow = 0; billow < billowCount; billow++)
        {
            float t = 0.07f + Mathf.Repeat((billow + 0.35f) / billowCount + travel, 0.88f);
            t = Mathf.Clamp(t, 0.07f, 0.95f);
            SamplePath(points, radii, pointCount, t, out Vector3 center, out Vector3 tangent, out float pathRadius);
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            if (side.sqrMagnitude <= 0.0001f)
                side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(tangent, side).normalized;
            float flamePhase = billow * 2.39996f + Time.time * travelSpeed * 1.6f;
            float variation = 0.9f + Mathf.Sin(billow * 12.9898f + 4.31f) * 0.1f;
            float rootExpansion = coneMode
                ? Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.3f))
                : Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.45f));
            float plumeEnvelope = rootExpansion * Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0.82f, 1f, t));
            float billowRadius = Mathf.Max(0.035f, pathRadius * radiusMultiplier * variation * plumeEnvelope);
            float lateralOffset = Mathf.Sin(flamePhase) * billowRadius * (coneMode ? 0.045f : 0.08f);
            float liftOffset = (0.04f + Mathf.Abs(Mathf.Sin(flamePhase * 0.73f)) * (coneMode ? 0.07f : 0.12f)) * billowRadius;
            center += side * lateralOffset + up * liftOffset;
            float axialStretch = coneMode
                ? Mathf.Lerp(0.85f, 1.15f, Mathf.Sin(flamePhase * 0.61f) * 0.5f + 0.5f)
                : Mathf.Lerp(1.5f, 1.9f, Mathf.Sin(flamePhase * 0.61f) * 0.5f + 0.5f);
            float sideStretch = coneMode
                ? Mathf.Lerp(0.82f, 1f, Mathf.Sin(flamePhase + 1.7f) * 0.5f + 0.5f)
                : Mathf.Lerp(0.9f, 1.12f, Mathf.Sin(flamePhase + 1.7f) * 0.5f + 0.5f);
            float upStretch = coneMode
                ? Mathf.Lerp(0.78f, 0.98f, Mathf.Sin(flamePhase * 1.21f + 0.4f) * 0.5f + 0.5f)
                : Mathf.Lerp(0.94f, 1.18f, Mathf.Sin(flamePhase * 1.21f + 0.4f) * 0.5f + 0.5f);
            int vertexOffset = billow * verticesPerBillow;
            int triangleOffset = billow * trianglesPerBillow;

            for (int latitude = 0; latitude <= BillowLatitudeCount; latitude++)
            {
                float v = latitude / (float)BillowLatitudeCount;
                float polar = v * Mathf.PI;
                float axial = Mathf.Cos(polar);
                float ring = Mathf.Sin(polar);
                for (int longitude = 0; longitude < BillowLongitudeCount; longitude++)
                {
                    float u = longitude / (float)BillowLongitudeCount;
                    float angle = u * Mathf.PI * 2f;
                    float surfaceNoise = 1f + Mathf.Sin(Time.time * 5.2f + billow * 1.73f + latitude * 2.11f + longitude) * 0.12f;
                    float front = axial * 0.5f + 0.5f;
                    float tongueTaper = Mathf.Lerp(1.16f, 0.56f, Mathf.SmoothStep(0.48f, 1f, front));
                    Vector3 radial =
                        side * (Mathf.Cos(angle) * sideStretch) +
                        up * (Mathf.Sin(angle) * upStretch);
                    float curlStrength = coneMode ? 0.1f : 0.18f;
                    Vector3 curl = up * Mathf.Pow(Mathf.Max(0f, axial), 2f) * billowRadius * (curlStrength + 0.1f * upStretch);
                    int vertex = vertexOffset + latitude * BillowLongitudeCount + longitude;
                    Vector3 vertexPosition = center +
                        tangent * axial * axialStretch * billowRadius * surfaceNoise +
                        radial * ring * tongueTaper * billowRadius * surfaceNoise +
                        curl;
                    vertices[vertex] = coneMode
                        ? ClampToConeEnvelope(vertexPosition, points, radii, pointCount)
                        : vertexPosition;
                    uvs[vertex] = new Vector2(u, v + billow * 0.13f);
                    float turbulenceSeed = Mathf.Sin(flamePhase + longitude * 1.37f + latitude * 0.73f) * 0.5f + 0.5f;
                    float heat = Mathf.Clamp01(0.96f - t * 0.5f + turbulenceSeed * 0.16f);
                    uv2[vertex] = new Vector2(heat, t);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 7f, (1f - t) * 8f));
                    float tipFade = Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0.75f, 1f, front));
                    colors[vertex] = new Color(1f, 1f, 1f, alpha * tipFade * 0.95f);
                }
            }

            for (int latitude = 0; latitude < BillowLatitudeCount; latitude++)
            {
                for (int longitude = 0; longitude < BillowLongitudeCount; longitude++)
                {
                    int nextLongitude = (longitude + 1) % BillowLongitudeCount;
                    int current = vertexOffset + latitude * BillowLongitudeCount + longitude;
                    int next = vertexOffset + (latitude + 1) * BillowLongitudeCount + longitude;
                    int triangle = triangleOffset + (latitude * BillowLongitudeCount + longitude) * 6;
                    triangles[triangle] = current;
                    triangles[triangle + 1] = next;
                    triangles[triangle + 2] = vertexOffset + latitude * BillowLongitudeCount + nextLongitude;
                    triangles[triangle + 3] = vertexOffset + latitude * BillowLongitudeCount + nextLongitude;
                    triangles[triangle + 4] = next;
                    triangles[triangle + 5] = vertexOffset + (latitude + 1) * BillowLongitudeCount + nextLongitude;
                }
            }
        }

        _billowMesh.Clear();
        _billowMesh.vertices = vertices;
        _billowMesh.uv = uvs;
        _billowMesh.uv2 = uv2;
        _billowMesh.colors = colors;
        _billowMesh.triangles = triangles;
        _billowMesh.RecalculateNormals();
        _billowMesh.RecalculateBounds();
    }

    private static Vector3 ClampToConeEnvelope(
        Vector3 vertex,
        Vector3[] points,
        float[] radii,
        int pointCount)
    {
        Vector3 start = points[0];
        Vector3 axis = points[pointCount - 1] - start;
        float length = axis.magnitude;
        if (length <= 0.0001f)
            return start;

        axis /= length;
        Vector3 fromStart = vertex - start;
        float rawProjection = Vector3.Dot(fromStart, axis);
        float projection = Mathf.Clamp(rawProjection, 0f, length);
        float pathT = projection / length;
        SamplePath(points, radii, pointCount, pathT, out Vector3 pathCenter, out _, out float allowedRadius);
        Vector3 radial = fromStart - axis * rawProjection;
        float radialLength = radial.magnitude;
        float maximumRadius = Mathf.Max(0f, allowedRadius * 0.94f);
        if (radialLength > maximumRadius && radialLength > 0.0001f)
            radial *= maximumRadius / radialLength;
        return pathCenter + radial;
    }

    private static void SamplePath(
        Vector3[] points,
        float[] radii,
        int pointCount,
        float t,
        out Vector3 center,
        out Vector3 tangent,
        out float radius)
    {
        float scaled = Mathf.Clamp01(t) * (pointCount - 1);
        int start = Mathf.Min(Mathf.FloorToInt(scaled), pointCount - 2);
        int end = start + 1;
        float blend = scaled - start;
        center = Vector3.Lerp(points[start], points[end], blend);
        tangent = points[end] - points[start];
        if (tangent.sqrMagnitude <= 0.0001f)
            tangent = Vector3.forward;
        tangent.Normalize();
        radius = Mathf.Lerp(radii[start], radii[end], blend);
    }

    private static void BuildTubeMesh(
        Mesh mesh,
        Vector3[] points,
        float[] radii,
        int pointCount,
        int radialSides,
        float radiusMultiplier,
        float irregularity,
        bool taper,
        float crossSectionWidth,
        float crossSectionHeight,
        float visibleEnd)
    {
        radialSides = Mathf.Max(3, radialSides);
        Vector3[] vertices = new Vector3[pointCount * radialSides];
        Vector2[] uvs = new Vector2[vertices.Length];
        Vector2[] uv2 = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[(pointCount - 1) * radialSides * 6];
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 tangent = i == 0 ? points[1] - points[0] :
                i == pointCount - 1 ? points[i] - points[i - 1] : points[i + 1] - points[i - 1];
            if (tangent.sqrMagnitude <= 0.0001f)
                tangent = Vector3.forward;
            tangent.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            if (side.sqrMagnitude <= 0.0001f)
                side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(tangent, side).normalized;
            float t = i / (float)(pointCount - 1);
            float vertexAlpha = taper ? Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 5f, (1f - t) * 4f)) : Mathf.SmoothStep(0f, 1f, Mathf.Min(t * 5f, 1f));
            if (visibleEnd < 0.999f)
                vertexAlpha *= 1f - Mathf.SmoothStep(visibleEnd * 0.55f, visibleEnd, t);
            for (int sideIndex = 0; sideIndex < radialSides; sideIndex++)
            {
                float around = sideIndex / (float)radialSides;
                float angle = around * Mathf.PI * 2f;
                // Opposite vertices share a radius, keeping every animated ring centered
                // exactly on the authoritative damage path while the silhouette breathes.
                float livingNoise = Mathf.Sin(Time.time * 8.5f + i * 1.71f + angle * 2f) * 0.5f + 0.5f;
                float radius = radii[i] * radiusMultiplier * Mathf.Lerp(1f - irregularity, 1f, livingNoise);
                Vector3 radial =
                    side * (Mathf.Cos(angle) * crossSectionWidth) +
                    up * (Mathf.Sin(angle) * crossSectionHeight);
                int vertex = i * radialSides + sideIndex;
                vertices[vertex] = points[i] + radial * radius;
                uvs[vertex] = new Vector2(around, t);
                uv2[vertex] = new Vector2(Mathf.Clamp01(0.98f - t * 0.5f), t);
                colors[vertex] = new Color(1f, 1f, 1f, vertexAlpha);

                if (i >= pointCount - 1)
                    continue;
                int nextSide = (sideIndex + 1) % radialSides;
                int nextRing = (i + 1) * radialSides;
                int triangle = (i * radialSides + sideIndex) * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = nextRing + sideIndex;
                triangles[triangle + 2] = i * radialSides + nextSide;
                triangles[triangle + 3] = i * radialSides + nextSide;
                triangles[triangle + 4] = nextRing + sideIndex;
                triangles[triangle + 5] = nextRing + nextSide;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = uv2;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void BuildRing(float normalizedLife)
    {
        const int segments = 36;
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[segments * 6];
        float radius = Mathf.Lerp(_ringRadius * 0.12f, _ringRadius, Mathf.SmoothStep(0f, 1f, normalizedLife));
        float thickness = Mathf.Lerp(_ringRadius * 0.16f, _ringRadius * 0.025f, normalizedLife);
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = radial * Mathf.Max(0f, radius - thickness);
            vertices[i * 2 + 1] = radial * (radius + thickness);
            uvs[i * 2] = new Vector2(0f, t);
            uvs[i * 2 + 1] = new Vector2(1f, t);
            colors[i * 2] = colors[i * 2 + 1] = Color.white;
            if (i == segments)
                continue;
            int vertex = i * 2;
            int triangle = i * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }
        _bodyMesh.Clear();
        _bodyMesh.vertices = vertices;
        _bodyMesh.uv = uvs;
        _bodyMesh.colors = colors;
        _bodyMesh.triangles = triangles;
        _bodyMesh.RecalculateNormals();
        _bodyMesh.RecalculateBounds();
        _coreMesh.Clear();
        _billowMesh.Clear();
        SetVisible(true);
    }

    private void ShowFor(float duration)
    {
        _ringMode = false;
        _visibleDuration = Mathf.Max(0.02f, duration);
        _visibleTimer = _visibleDuration;
        SetVisible(true);
        SetSecondaryLayers(true);
        ApplyMaterialProperties(1f);
    }

    private void SetVisible(bool visible)
    {
        if (_bodyRenderer != null)
            _bodyRenderer.enabled = visible;
        if (_coreRenderer != null)
            _coreRenderer.enabled = false;
        if (_billowRenderer != null)
            _billowRenderer.enabled = visible && !_ringMode && _billowMesh != null && _billowMesh.vertexCount > 0;
        if (_nozzleGlow != null)
            _nozzleGlow.enabled = false;
        if (_nozzleLight != null)
            _nozzleLight.enabled = false;
        if (!visible)
            SetSecondaryLayers(false);
    }

    private void SetSecondaryLayers(bool active)
    {
        ConfigureSecondaryParticles(_manualMode);
        SetParticles(_embers, active, _emberRate * Mathf.Lerp(0.8f, 1.5f, _heat));
        float smokeModeScale = _manualMode ? 1.65f : 1f;
        SetParticles(_smoke, active, _smokeRate * smokeModeScale * Mathf.Lerp(0.65f, 1.6f, _heat));
    }

    private void ConfigureSecondaryParticles(bool manual)
    {
        ConfigureParticle(_embers,
            manual ? new ParticleSystem.MinMaxCurve(0.07f, 0.24f) : new ParticleSystem.MinMaxCurve(0.055f, 0.18f),
            manual ? 19f : 15f,
            manual ? 0.18f : 0.1f);
        ConfigureParticle(_smoke,
            manual ? new ParticleSystem.MinMaxCurve(0.26f, 0.72f) : new ParticleSystem.MinMaxCurve(0.055f, 0.18f),
            manual ? 24f : 19f,
            manual ? 0.22f : 0.13f);
    }

    private static void ConfigureParticle(
        ParticleSystem particles,
        ParticleSystem.MinMaxCurve size,
        float coneAngle,
        float shapeRadius)
    {
        if (particles == null)
            return;
        ParticleSystem.MainModule main = particles.main;
        main.startSize = size;
        ParticleSystem.ShapeModule shape = particles.shape;
        if (shape.shapeType == ParticleSystemShapeType.Cone)
        {
            shape.angle = coneAngle;
            shape.radius = shapeRadius;
        }
    }

    private static void SetParticles(ParticleSystem particles, bool active, float rate)
    {
        if (particles == null)
            return;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = Mathf.Max(0f, rate);
        if (active && !particles.isPlaying)
            particles.Play();
        else if (!active && particles.isPlaying)
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void SetParticlePalette(Color emberStart, Color emberEnd, Color smokeEnd)
    {
        SetParticleGradient(_embers, emberStart, emberEnd, 0.95f);
        SetParticleGradient(_smoke, Color.Lerp(_bodyColor, Color.white, _style == FlamethrowerStreamStyle.LiquidNitrogen ? 0.55f : 0.08f), smokeEnd, 0.5f);
    }

    private static void SetParticleGradient(ParticleSystem particles, Color start, Color end, float startAlpha)
    {
        if (particles == null)
            return;
        ParticleSystem.MainModule main = particles.main;
        main.startColor = Color.white;
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    private void ApplyMaterialProperties(float alpha)
    {
        float bodyAlpha = _manualMode ? alpha * _manualBodyOpacity : alpha;
        float billowAlpha = _manualMode ? alpha * 0.98f : alpha * 0.88f;
        ApplyPlumeRenderer(
            _bodyRenderer,
            _bodyColor,
            _coreColor,
            bodyAlpha,
            _manualMode ? _baseEmission * 0.92f : _baseEmission);
        ApplyRenderer(_coreRenderer, _coreColor, alpha, _baseEmission * 1.55f);
        ApplyPlumeRenderer(_billowRenderer, Color.Lerp(_bodyColor, _coreColor, 0.14f), _coreColor, billowAlpha, _baseEmission * 1.05f);
        ApplyRenderer(_nozzleGlow, _coreColor, alpha, _baseEmission * 1.35f);
        if (_nozzleLight != null)
        {
            _nozzleLight.color = _coreColor;
            _nozzleLight.intensity = alpha * Mathf.Lerp(0.7f, 1.45f, _heat);
        }
    }

    private void ApplyPlumeRenderer(Renderer renderer, Color bodyColor, Color hotColor, float alpha, float emission)
    {
        ApplyRenderer(renderer, bodyColor, alpha, emission);
        if (renderer == null)
            return;
        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(HotColorId, hotColor);
        renderer.SetPropertyBlock(_propertyBlock);
    }

    private void ApplyRenderer(Renderer renderer, Color color, float alpha, float emission)
    {
        if (renderer == null)
            return;
        renderer.GetPropertyBlock(_propertyBlock);
        Color faded = color;
        faded.a *= Mathf.Clamp01(alpha);
        float heatEmission = emission * Mathf.Lerp(1f, _heatEmissionMultiplier, _heat);
        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, faded);
        _propertyBlock.SetColor(EmissionColorId, color * heatEmission);
        _propertyBlock.SetFloat(EmissionIntensityId, heatEmission);
        _propertyBlock.SetFloat(HeatId, _heat);
        _propertyBlock.SetFloat(PulseId, Mathf.Clamp01(alpha));
        _propertyBlock.SetFloat(DissolveId, Mathf.Clamp01(1f - alpha) * Mathf.Clamp01(_erosionSpeed * 0.25f));
        _propertyBlock.SetFloat(NoiseScaleId, _noiseScale);
        _propertyBlock.SetFloat(NoiseSpeedId, _noiseSpeed);
        renderer.SetPropertyBlock(_propertyBlock);
    }

    private static Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);
        return material;
    }

    private static void DestroyRuntimeObject(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    private static Vector3 GetStableUp(Vector3 direction) =>
        Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
}
