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
    private static readonly int HotColorId = Shader.PropertyToID("_HotColor");
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
    private bool _activeRadialPlumeBuilt;
    private Material _activePlumeMaterial;
    private static Mesh _activeBillowMesh;
    private Transform[] _activeBillows = System.Array.Empty<Transform>();
    private Vector3[] _activeBillowTargetPositions = System.Array.Empty<Vector3>();
    private Vector3[] _activeBillowTargetScales = System.Array.Empty<Vector3>();
    private float[] _activeBillowDelays = System.Array.Empty<float>();

    public FlamethrowerCueStyle Style => _style;
    public int RuntimeMeshLayerCount => _meshLayers?.Length ?? 0;
    public int RuntimeParticleSystemCount => _particleLayers?.Length ?? 0;
    public bool UsesActiveRadialPlume => _activeRadialPlumeBuilt;

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
        float rotationRate = IsActiveBurst() ? 0f : _rotationDegreesPerSecond;
        float rotation = rotationRate * Time.unscaledDeltaTime;
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].Rotate(0f, rotation, 0f, Space.Self);
        }
    }

    private void OnDisable() => SetLightEnabled(false);

    private void OnDestroy()
    {
        if (_activePlumeMaterial == null)
            return;
        if (Application.isPlaying)
            Destroy(_activePlumeMaterial);
        else
            DestroyImmediate(_activePlumeMaterial);
    }

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
        if (IsActiveBurst())
            BuildActiveRadialPlume();
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

    private bool IsActiveBurst() =>
        _style == FlamethrowerCueStyle.FlameActiveBurst ||
        _style == FlamethrowerCueStyle.JellifiedActiveBurst ||
        _style == FlamethrowerCueStyle.NitrogenActiveBurst;

    private void BuildActiveRadialPlume()
    {
        if (_activeRadialPlumeBuilt)
            return;

        Transform radiusRoot = transform.Find("Animated Visual/Damage Radius");
        if (radiusRoot == null)
            return;

        Renderer[] authoredRenderers = _meshLayers != null && _meshLayers.Length > 0
            ? _meshLayers
            : GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < authoredRenderers.Length; i++)
        {
            if (authoredRenderers[i] != null && authoredRenderers[i] is not ParticleSystemRenderer)
                authoredRenderers[i].enabled = false;
        }

        Shader shader = Shader.Find("ScrapWaves/GameFeel/Flamethrower Plume");
        if (shader == null)
            shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        if (shader == null)
            return;

        _activePlumeMaterial = new Material(shader)
        {
            name = "Flamethrower Active Radial Plume (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (_activePlumeMaterial.HasProperty(BaseColorId))
            _activePlumeMaterial.SetColor(BaseColorId, _primaryColor);
        if (_activePlumeMaterial.HasProperty(HotColorId))
            _activePlumeMaterial.SetColor(HotColorId, _coreColor);

        Mesh billowMesh = GetActiveBillowMesh();
        System.Collections.Generic.List<Renderer> plumeRenderers = new(24);
        System.Collections.Generic.List<Transform> billows = new(24);
        System.Collections.Generic.List<Vector3> targetPositions = new(24);
        System.Collections.Generic.List<Vector3> targetScales = new(24);
        System.Collections.Generic.List<float> delays = new(24);
        CreateBillowRing(radiusRoot, billowMesh, plumeRenderers, billows, targetPositions, targetScales, delays, 12, 0.34f, 0.135f, 0.24f, 0.17f, 0f, 0.018f);
        CreateBillowRing(radiusRoot, billowMesh, plumeRenderers, billows, targetPositions, targetScales, delays, 8, 0.15f, 0.13f, 0.21f, 0.2f, 15f, 0.032f);
        CreateFireballCore(radiusRoot, billowMesh, plumeRenderers, billows, targetPositions, targetScales, delays);

        _meshLayers = plumeRenderers.ToArray();
        _activeBillows = billows.ToArray();
        _activeBillowTargetPositions = targetPositions.ToArray();
        _activeBillowTargetScales = targetScales.ToArray();
        _activeBillowDelays = delays.ToArray();
        _activeRadialPlumeBuilt = true;
    }

    private void CreateFireballCore(
        Transform parent,
        Mesh mesh,
        System.Collections.Generic.List<Renderer> renderers,
        System.Collections.Generic.List<Transform> billows,
        System.Collections.Generic.List<Vector3> targetPositions,
        System.Collections.Generic.List<Vector3> targetScales,
        System.Collections.Generic.List<float> delays)
    {
        Vector3[] positions =
        {
            new(0f, 0f, -0.075f),
            new(-0.045f, 0.025f, -0.125f),
            new(0.05f, -0.035f, -0.15f),
            new(0.012f, 0.02f, -0.235f)
        };
        Vector3[] scales =
        {
            new(0.25f, 0.25f, 0.2f),
            new(0.2f, 0.22f, 0.27f),
            new(0.21f, 0.19f, 0.3f),
            new(0.16f, 0.18f, 0.34f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            Renderer renderer = CreateActiveBillow(
                parent,
                mesh,
                $"Radial Fireball Core {i + 1:00}",
                positions[i],
                Quaternion.Euler(0f, 0f, i * 47f),
                scales[i]);
            renderers.Add(renderer);
            billows.Add(renderer.transform);
            targetPositions.Add(positions[i]);
            targetScales.Add(scales[i]);
            delays.Add(i * 0.014f);
        }
    }

    private void CreateBillowRing(
        Transform parent,
        Mesh mesh,
        System.Collections.Generic.List<Renderer> renderers,
        System.Collections.Generic.List<Transform> billows,
        System.Collections.Generic.List<Vector3> targetPositions,
        System.Collections.Generic.List<Vector3> targetScales,
        System.Collections.Generic.List<float> delays,
        int count,
        float radius,
        float tangentialScale,
        float radialScale,
        float verticalScale,
        float angleOffset,
        float delayBase)
    {
        for (int i = 0; i < count; i++)
        {
            float angleDegrees = angleOffset + i / (float)count * 360f + Mathf.Sin(i * 5.17f + count) * 4.5f;
            float angle = angleDegrees * Mathf.Deg2Rad;
            float variation = 0.9f + Mathf.Sin(i * 4.73f + count) * 0.1f;
            float variedRadius = radius * Mathf.Lerp(0.9f, 1.04f, Mathf.Sin(i * 7.31f + 0.7f) * 0.5f + 0.5f);
            Vector3 position = new(
                Mathf.Cos(angle) * variedRadius,
                Mathf.Sin(angle) * variedRadius,
                -0.02f - (i % 3) * 0.008f);
            Vector3 scale = new(
                tangentialScale * Mathf.Lerp(0.9f, 1.1f, i % 2),
                radialScale * variation,
                verticalScale * Mathf.Lerp(0.86f, 1.14f, (i % 4) / 3f));
            Renderer renderer = CreateActiveBillow(
                parent,
                mesh,
                $"Radial Flame Billow {parent.childCount + 1:00}",
                position,
                Quaternion.Euler(0f, 0f, angleDegrees - 90f),
                scale);
            renderers.Add(renderer);
            billows.Add(renderer.transform);
            targetPositions.Add(position);
            targetScales.Add(scale);
            delays.Add(delayBase + (i % 4) * 0.012f);
        }
    }

    private Renderer CreateActiveBillow(
        Transform parent,
        Mesh mesh,
        string objectName,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale)
    {
        GameObject billow = new(objectName);
        billow.transform.SetParent(parent, false);
        billow.transform.localPosition = localPosition;
        billow.transform.localRotation = localRotation;
        billow.transform.localScale = localScale;
        MeshFilter filter = billow.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = billow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _activePlumeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return renderer;
    }

    private static Mesh GetActiveBillowMesh()
    {
        if (_activeBillowMesh != null)
            return _activeBillowMesh;

        const int longitudeCount = 12;
        const int latitudeCount = 8;
        int vertexCount = (latitudeCount + 1) * longitudeCount;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Vector2[] uv2 = new Vector2[vertexCount];
        Color[] colors = new Color[vertexCount];
        int[] triangles = new int[latitudeCount * longitudeCount * 6];

        for (int latitude = 0; latitude <= latitudeCount; latitude++)
        {
            float v = latitude / (float)latitudeCount;
            float polar = v * Mathf.PI;
            float axial = Mathf.Cos(polar);
            float ring = Mathf.Sin(polar);
            for (int longitude = 0; longitude < longitudeCount; longitude++)
            {
                float u = longitude / (float)longitudeCount;
                float angle = u * Mathf.PI * 2f;
                Vector3 normal = new(Mathf.Cos(angle) * ring, Mathf.Sin(angle) * ring, axial);
                int vertex = latitude * longitudeCount + longitude;
                vertices[vertex] = normal * 0.5f;
                normals[vertex] = normal;
                uvs[vertex] = new Vector2(u, v);
                // A broad heat range lets one billow carry the complete fire ramp:
                // red perimeter, orange body, and yellow-hot turbulent pockets.
                uv2[vertex] = new Vector2(Mathf.Lerp(0.2f, 0.88f, ring), 0.35f);
                colors[vertex] = new Color(1f, 1f, 1f, Mathf.Lerp(0.78f, 1f, ring));

                if (latitude >= latitudeCount)
                    continue;
                int nextLongitude = (longitude + 1) % longitudeCount;
                int next = vertex + longitudeCount;
                int triangle = (latitude * longitudeCount + longitude) * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = next;
                triangles[triangle + 2] = latitude * longitudeCount + nextLongitude;
                triangles[triangle + 3] = latitude * longitudeCount + nextLongitude;
                triangles[triangle + 4] = next;
                triangles[triangle + 5] = (latitude + 1) * longitudeCount + nextLongitude;
            }
        }

        _activeBillowMesh = new Mesh
        {
            name = "Flamethrower Active Radial Billow",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            normals = normals,
            uv = uvs,
            uv2 = uv2,
            colors = colors,
            triangles = triangles
        };
        _activeBillowMesh.RecalculateBounds();
        return _activeBillowMesh;
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
            Color layerColor = color;
            Color layerHotColor = _coreColor;
            float layerEmission = emission;
            float layerDissolve = dissolve;
            if (IsActiveBurst())
            {
                float targetRadius = i < _activeBillowTargetPositions.Length
                    ? new Vector2(_activeBillowTargetPositions[i].x, _activeBillowTargetPositions[i].y).magnitude
                    : 0f;
                float radialPosition = Mathf.Clamp01(targetRadius / 0.34f);
                bool compactCore = targetRadius < 0.085f;
                // Every layer now follows the outer shell timing: it drops to a low
                // opacity quickly, reaches the gameplay radius, and clears as one burst.
                const float fadeStart = 0.26f;
                const float fadeEnd = 0.46f;
                float fadeProgress = Mathf.Clamp01(Mathf.InverseLerp(fadeStart, fadeEnd, normalizedLife));
                float rapidOuterFade = 1f - Mathf.Pow(1f - fadeProgress, 2.15f);
                float shellFade = rapidOuterFade;
                float visibility = 1f - shellFade;
                float ignitionFlash = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedLife / 0.075f));
                bool fieryBurst = _style != FlamethrowerCueStyle.NitrogenActiveBurst;
                if (fieryBurst)
                {
                    Color yellowFire = new(1f, 0.9f, 0.2f, _coreColor.a);
                    Color orangeFire = new(1f, 0.32f, 0.012f, _primaryColor.a);
                    Color redFire = new(0.94f, 0.055f, 0.004f, _primaryColor.a);
                    Color brightFire = Color.Lerp(yellowFire, new Color(1f, 0.98f, 0.64f, _coreColor.a), 0.38f);
                    Color saturatedFire = Color.Lerp(orangeFire, redFire, Mathf.Lerp(0.18f, 0.58f, radialPosition));
                    if (compactCore)
                        saturatedFire = Color.Lerp(orangeFire, yellowFire, 0.38f);
                    layerColor = Color.Lerp(saturatedFire, brightFire, ignitionFlash * 0.9f);
                    layerHotColor = Color.Lerp(orangeFire, yellowFire, compactCore ? 0.88f : 0.68f);

                    float cooling = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.11f, 0.34f, normalizedLife));
                    float sootVariation = 0.68f +
                        (Mathf.Sin(i * 8.73f + targetRadius * 31f) * 0.5f + 0.5f) * 0.32f;
                    float sootStrength = cooling * sootVariation * (compactCore ? 0.24f : 0.52f);
                    Color burntAir = new(0.018f, 0.012f, 0.009f, layerColor.a);
                    Color dyingEmber = new(0.34f, 0.055f, 0.004f, layerHotColor.a);
                    layerColor = Color.Lerp(layerColor, burntAir, sootStrength);
                    layerHotColor = Color.Lerp(layerHotColor, dyingEmber, sootStrength * 0.78f);
                    layerEmission *= Mathf.Lerp(1f, 0.48f, sootStrength);
                }
                else
                {
                    layerColor = Color.Lerp(_primaryColor, _coreColor, ignitionFlash * 0.72f);
                    layerHotColor = _coreColor;
                }
                float shellOpacity = Mathf.Lerp(0.7f, 0.22f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedLife / 0.085f)));
                layerColor.a = color.a * visibility * shellOpacity;
                layerEmission *= compactCore
                    ? Mathf.Lerp(1.08f, 1.85f, ignitionFlash)
                    : Mathf.Lerp(1.18f, 2f, ignitionFlash);
                // Alpha-only fading keeps every layer fiery instead of turning dusty.
                layerDissolve = 0f;
            }
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, layerColor);
            _propertyBlock.SetColor(EmissionColorId, layerColor * layerEmission);
            _propertyBlock.SetColor(HotColorId, layerHotColor);
            _propertyBlock.SetFloat(EmissionIntensityId, layerEmission);
            _propertyBlock.SetFloat(HeatId, _heat);
            _propertyBlock.SetFloat(
                PulseId,
                Mathf.Clamp01(_intensity * (1f - normalizedLife * 0.35f)) * flashScale);
            _propertyBlock.SetFloat(DissolveId, layerDissolve);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        float lifeScale = IsActiveBurst()
            ? 1f
            : Mathf.Max(0f, _scaleOverLife.Evaluate(normalizedLife));
        float scale = _size * _contextScale * lifeScale;
        for (int i = 0; i < _animatedRoots.Length; i++)
        {
            if (_animatedRoots[i] != null)
                _animatedRoots[i].localScale = _baseScales[i] * scale;
        }
        if (IsActiveBurst())
            AnimateActiveExplosion(normalizedLife);
        if (_lightPulse != null)
        {
            _lightPulse.color = color;
            _lightPulse.intensity = emission;
        }
    }

    private void AnimateActiveExplosion(float normalizedLife)
    {
        int count = Mathf.Min(
            _activeBillows.Length,
            Mathf.Min(_activeBillowTargetPositions.Length, _activeBillowTargetScales.Length));
        for (int i = 0; i < count; i++)
        {
            Transform billow = _activeBillows[i];
            if (billow == null)
                continue;

            float delay = i < _activeBillowDelays.Length ? _activeBillowDelays[i] : 0f;
            float life = Mathf.Clamp01((normalizedLife - delay) / Mathf.Max(0.01f, 1f - delay));
            Vector3 targetPosition = _activeBillowTargetPositions[i];
            Vector3 targetScale = _activeBillowTargetScales[i];
            float targetRadius = new Vector2(targetPosition.x, targetPosition.y).magnitude;
            bool center = targetRadius < 0.085f;

            if (center)
            {
                float ignition = Mathf.Lerp(0.62f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.055f)));
                float burst = Mathf.Lerp(0.55f, 1.58f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.16f)));
                float settle = Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.34f, life)));
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.94f, life));
                Vector3 corePosition = targetPosition;
                corePosition.x *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.18f));
                corePosition.y *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.18f));
                corePosition.z *= Mathf.Lerp(0.18f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.26f)));
                corePosition.z -= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 1f, life)) * 0.055f;
                billow.localPosition = corePosition;
                billow.localScale = targetScale * ignition * burst * settle * Mathf.Lerp(0.18f, 1f, collapse);
                continue;
            }

            float travel = 1f - Mathf.Pow(1f - Mathf.Clamp01(life / 0.27f), 3f);
            float overshootPhase = Mathf.Clamp01(life / 0.36f);
            float overshoot = Mathf.Sin(overshootPhase * Mathf.PI) * 0.12f;
            float distanceScale = Mathf.Lerp(0.045f, 1f, travel) * (1f + overshoot);
            Vector3 position = targetPosition;
            position.x *= distanceScale;
            position.y *= distanceScale;
            position.z *= Mathf.Lerp(0.18f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.3f)));
            position.z -= Mathf.Sin(Mathf.Clamp01(life / 0.78f) * Mathf.PI) * (0.045f + targetRadius * 0.06f);
            billow.localPosition = position;

            float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.045f));
            float swell = Mathf.Lerp(0.12f, 1.34f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(life / 0.19f)));
            float breakup = Mathf.Lerp(1f, 0.22f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.56f, 1f, life)));
            billow.localScale = targetScale * appear * swell * breakup;
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
