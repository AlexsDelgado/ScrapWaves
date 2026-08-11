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
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
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
    [SerializeField, Range(2, 48)] private int _maximumSegments = 48;
    [SerializeField] private AnimationCurve _ribbonWidth = new(
        new Keyframe(0f, 0.42f),
        new Keyframe(0.22f, 1f),
        new Keyframe(0.78f, 0.72f),
        new Keyframe(1f, 0.08f));
    [SerializeField, Range(0.05f, 1f)] private float _coreWidthMultiplier = 0.34f;
    [SerializeField, Range(0.1f, 1f)] private float _automaticWidthMultiplier = 0.52f;
    [SerializeField, Range(0.1f, 1f)] private float _automaticHeightMultiplier = 0.22f;

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
    private bool _initialized;

    public int MaximumSegments => _maximumSegments;
    public int BodyRadialSides => BodyRadialSideCount;
    public int CoreRadialSides => CoreRadialSideCount;
    public int BodyVertexCount => _bodyMesh != null ? _bodyMesh.vertexCount : 0;
    public int CoreVertexCount => _coreMesh != null ? _coreMesh.vertexCount : 0;
    public Mesh BodyMesh => _bodyMesh;
    public Mesh CoreMesh => _coreMesh;
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
        ShowFor(duration);
    }

    public void ShowHose(Vector3[] worldPoints, int pointCount, float radius, float duration)
    {
        if (worldPoints == null || pointCount <= 1)
            return;

        EnsureInitialized();
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

        BuildVolume(points, halfWidths, pointCount, taper: true, crossSectionWidth: 1f, crossSectionHeight: 1f);
        ShowFor(duration);
    }

    private void Awake() => EnsureInitialized();

    private void OnValidate()
    {
        _maximumSegments = Mathf.Clamp(_maximumSegments, 2, 48);
        _coreWidthMultiplier = Mathf.Clamp(_coreWidthMultiplier, 0.05f, 1f);
        _automaticWidthMultiplier = Mathf.Clamp(_automaticWidthMultiplier, 0.1f, 1f);
        _automaticHeightMultiplier = Mathf.Clamp(_automaticHeightMultiplier, 0.1f, 1f);
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
        float alpha = _ringMode ? 1f - normalizedLife : Mathf.Clamp01(_visibleTimer / 0.06f);
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
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;
        _propertyBlock = new MaterialPropertyBlock();
        EnsureRibbonLayer(ref _bodyFilter, ref _bodyRenderer, "Flame Body", false);
        EnsureRibbonLayer(ref _coreFilter, ref _coreRenderer, "Flame Core", true);
        _bodyMesh = new Mesh { name = "Flamethrower Volumetric Body", hideFlags = HideFlags.DontSave };
        _coreMesh = new Mesh { name = "Flamethrower Volumetric Core", hideFlags = HideFlags.DontSave };
        _bodyMesh.MarkDynamic();
        _coreMesh.MarkDynamic();
        _bodyFilter.sharedMesh = _bodyMesh;
        _coreFilter.sharedMesh = _coreMesh;
        SetVisible(false);
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
        float crossSectionHeight)
    {
        BuildTubeMesh(_bodyMesh, points, radii, pointCount, BodyRadialSideCount, 1f, 0.1f, taper, crossSectionWidth, crossSectionHeight);
        BuildTubeMesh(_coreMesh, points, radii, pointCount, CoreRadialSideCount, _coreWidthMultiplier, 0.045f, taper, crossSectionWidth, crossSectionHeight);
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
        float crossSectionHeight)
    {
        radialSides = Mathf.Max(3, radialSides);
        Vector3[] vertices = new Vector3[pointCount * radialSides];
        Vector2[] uvs = new Vector2[vertices.Length];
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
            _coreRenderer.enabled = visible && !_ringMode;
        if (_nozzleGlow != null)
            _nozzleGlow.enabled = visible;
        if (_nozzleLight != null)
            _nozzleLight.enabled = false;
        if (!visible)
            SetSecondaryLayers(false);
    }

    private void SetSecondaryLayers(bool active)
    {
        SetParticles(_embers, active, _emberRate * Mathf.Lerp(0.8f, 1.5f, _heat));
        SetParticles(_smoke, active, _smokeRate * Mathf.Lerp(0.65f, 1.6f, _heat));
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
        ApplyRenderer(_bodyRenderer, _bodyColor, alpha, _baseEmission);
        ApplyRenderer(_coreRenderer, _coreColor, alpha, _baseEmission * 1.55f);
        ApplyRenderer(_nozzleGlow, _coreColor, alpha, _baseEmission * 1.35f);
        if (_nozzleLight != null)
        {
            _nozzleLight.color = _coreColor;
            _nozzleLight.intensity = alpha * Mathf.Lerp(0.7f, 1.45f, _heat);
        }
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
