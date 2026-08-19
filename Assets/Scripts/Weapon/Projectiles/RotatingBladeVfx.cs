using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class RotatingBladeVfx : MonoBehaviour
{
    private const int OrbitSegmentCount = 72;
    private const int SlashSegmentCount = 96;
    private const int SlashHeadSegmentCount = 24;
    private const float SlashSweepWindow = 0.32f;
    private const int ThrustSegmentCount = 8;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    private static readonly Color OrbitColor = new(0.45f, 0.95f, 1f, 0.22f);
    private static readonly Color BladeColor = new(0.7f, 1f, 1f, 0.95f);
    private static readonly Color SlashColor = new(0.55f, 1f, 0.75f, 0.9f);
    private static readonly Color ThrustColor = new(0.25f, 0.95f, 1f, 0.85f);
    private static Material s_lineMaterial;

    private sealed class BladeVisual
    {
        public GameObject Root;
        public Renderer[] Renderers;
        public TrailRenderer Trail;
    }

    private sealed class MeshPulse
    {
        public GameObject Root;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public float Timer;
        public float Duration;
        public Color Color;
        public Vector3 BaseScale = Vector3.one;
        public bool OwnsMesh;
        public bool IsSlashSweep;
        public float SlashRange;
        public float SlashConeAngle;
    }

    [Header("Authored production layers")]
    [SerializeField] private GameObject _bladePrototype;
    [SerializeField] private Material _trailMaterial;
    [SerializeField] private Material _slashMaterial;
    [SerializeField] private Material _thrustMaterial;
    [SerializeField] private Material _atomicMaterial;

    [Header("Readability")]
    [SerializeField, Range(0f, 1f)] private float _orbitGuideAlpha = 0.22f;
    [SerializeField, Min(0.02f)] private float _minimumBladeLength = 0.7f;
    [SerializeField, Min(0.01f)] private float _baseTrailWidth = 0.16f;
    [SerializeField, Min(0.01f)] private float _baseTrailTime = 0.12f;

    private readonly List<LineRenderer> _bladeLines = new();
    private readonly List<LineRenderer> _slashLines = new();
    private readonly List<float> _slashTimers = new();
    private readonly List<float> _slashDurations = new();
    private readonly List<Color> _slashColors = new();
    private readonly List<Vector3> _slashOrigins = new();
    private readonly List<Vector3> _slashDirections = new();
    private readonly List<float> _slashRanges = new();
    private readonly List<float> _slashHalfAngles = new();
    private readonly List<BladeVisual> _bladeVisuals = new();
    private readonly List<MeshPulse> _slashSurfaces = new();
    private readonly List<MeshPulse> _thrustSurfaces = new();
    private readonly List<MeshPulse> _dashAfterimages = new();
    private LineRenderer _orbitLine;
    private LineRenderer _thrustLine;
    private MaterialPropertyBlock _propertyBlock;

    private float _orbitTimer;
    private float _orbitDuration;
    private float _thrustTimer;
    private float _thrustDuration;
    private int _lastOrbitFrame = -1;
    private int _visibleBladeLineCount;
    private int _visiblePhysicalBladeCount;
    private Color _orbitColor = OrbitColor;
    private Color _bladeColor = BladeColor;
    private Color _thrustColor = ThrustColor;
    private bool _initialized;

    public int AuthoredBladeMeshCount => _bladePrototype != null ? _bladePrototype.GetComponentsInChildren<MeshFilter>(true).Length : 0;
    public int VisiblePhysicalBladeCount => _visiblePhysicalBladeCount;
    public int ActiveSlashSurfaceCount => CountActive(_slashSurfaces);
    public int ActiveThrustSurfaceCount => CountActive(_thrustSurfaces);
    public int ActiveDashAfterimageCount => CountActive(_dashAfterimages);

    public static RotatingBladeVfx Create(GameObject authoredPrefab = null)
    {
        RotatingBladeVfx vfx = null;
        if (authoredPrefab != null)
        {
            GameObject instance = Instantiate(authoredPrefab);
            instance.name = "[Rotating Blade VFX]";
            vfx = instance.GetComponent<RotatingBladeVfx>();
        }

        if (vfx == null)
        {
            GameObject fallback = new("[RotatingBladeVfx - Fallback]");
            vfx = fallback.AddComponent<RotatingBladeVfx>();
        }

        vfx.Initialize();
        return vfx;
    }

    public void ShowOrbit(Vector3 ownerOrigin, Vector3 bladeCenter, float hitRadius, float duration)
    {
        ShowOrbit(ownerOrigin, bladeCenter, hitRadius, duration, BladeColor, 0f);
    }

    public void ShowOrbit(Vector3 ownerOrigin, Vector3 bladeCenter, float hitRadius, float duration, Color bladeColor)
    {
        ShowOrbit(ownerOrigin, bladeCenter, hitRadius, duration, bladeColor, 0f);
    }

    public void ShowOrbit(
        Vector3 ownerOrigin,
        Vector3 bladeCenter,
        float hitRadius,
        float duration,
        Color bladeColor,
        float normalizedHeat)
    {
        Initialize();
        BeginOrbitFrame();

        ownerOrigin += Vector3.up * 0.14f;
        bladeCenter.y = ownerOrigin.y;
        float orbitRadius = Vector3.Distance(ownerOrigin, bladeCenter);
        if (orbitRadius <= 0.01f)
            return;

        float heat = Mathf.Clamp01(normalizedHeat);
        _orbitTimer = _orbitDuration = Mathf.Max(0.01f, duration);
        _orbitColor = WithAlpha(bladeColor, _orbitGuideAlpha);
        _bladeColor = WithAlpha(bladeColor, BladeColor.a);
        _orbitLine.enabled = true;
        _orbitLine.loop = true;
        _orbitLine.positionCount = OrbitSegmentCount;
        _orbitLine.widthMultiplier = Mathf.Lerp(0.025f, 0.055f, heat);

        for (int i = 0; i < OrbitSegmentCount; i++)
        {
            float angle = i / (float)OrbitSegmentCount * Mathf.PI * 2f;
            Vector3 point = ownerOrigin + new Vector3(Mathf.Cos(angle) * orbitRadius, 0f, Mathf.Sin(angle) * orbitRadius);
            _orbitLine.SetPosition(i, point);
        }

        Vector3 radial = bladeCenter - ownerOrigin;
        radial.y = 0f;
        if (radial.sqrMagnitude <= 0.0001f)
            radial = Vector3.forward;

        Vector3 radialDirection = radial.normalized;
        float halfLength = Mathf.Max(0.2f, hitRadius);
        LineRenderer bladeLine = GetNextBladeLine();
        bladeLine.enabled = true;
        bladeLine.loop = false;
        bladeLine.positionCount = 2;
        bladeLine.widthMultiplier = Mathf.Clamp(hitRadius * 0.11f, 0.035f, 0.24f);
        bladeLine.SetPosition(0, bladeCenter - radialDirection * halfLength);
        bladeLine.SetPosition(1, bladeCenter + radialDirection * halfLength);

        BladeVisual physicalBlade = GetNextPhysicalBlade();
        physicalBlade.Root.transform.SetPositionAndRotation(
            bladeCenter,
            Quaternion.LookRotation(radialDirection, Vector3.up));
        float bladeLength = Mathf.Max(_minimumBladeLength, hitRadius * 2f);
        physicalBlade.Root.transform.localScale = new Vector3(
            Mathf.Clamp(hitRadius * 0.85f, 0.28f, 1.6f),
            Mathf.Clamp(hitRadius * 0.38f, 0.14f, 0.7f),
            bladeLength * 0.5f);
        SetBladeVisualEnabled(physicalBlade, true);
        ApplyBladeProperties(physicalBlade, bladeColor, heat);
        ConfigureTrail(physicalBlade.Trail, bladeColor, hitRadius, heat);
        DisableUnusedPhysicalBlades();

        SetLineColor(_orbitLine, _orbitColor, 1f);
        SetLineColor(bladeLine, _bladeColor, 1f);
    }

    public void ShowSlash(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration)
    {
        ShowSlash(origin, direction, range, coneAngle, duration, SlashColor);
    }

    public void ShowSlash(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration, Color color)
    {
        Initialize();
        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 liftedOrigin = origin + Vector3.up * 0.18f;
        float safeDuration = Mathf.Max(0.05f, duration);
        LineRenderer slashLine = GetNextSlashLine(out int index);
        _slashTimers[index] = safeDuration;
        _slashDurations[index] = safeDuration;
        _slashColors[index] = color;
        _slashOrigins[index] = liftedOrigin;
        _slashDirections[index] = direction;
        _slashRanges[index] = range;
        _slashHalfAngles[index] = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
        slashLine.enabled = true;
        slashLine.loop = false;
        slashLine.positionCount = SlashHeadSegmentCount;
        slashLine.widthMultiplier = Mathf.Clamp(range * 0.035f, 0.075f, 0.16f);
        slashLine.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.22f, 0.48f),
            new Keyframe(0.78f, 1f),
            new Keyframe(1f, 0.08f));
        UpdateSlashHead(slashLine, index, 0f);
        SetSlashLineGradient(slashLine, color, 1f);

        MeshPulse surface = GetMeshPulse(_slashSurfaces, "Blade Slash Surface", _slashMaterial, ownsMesh: true);
        surface.Root.transform.SetPositionAndRotation(liftedOrigin, Quaternion.LookRotation(direction, Vector3.up));
        surface.Root.transform.localScale = Vector3.one;
        surface.Timer = surface.Duration = safeDuration;
        surface.Color = color;
        surface.IsSlashSweep = true;
        surface.SlashRange = range;
        surface.SlashConeAngle = coneAngle;
        BuildSlashSurface(surface.Mesh, range, coneAngle, 0f);
        surface.Renderer.enabled = true;
        ApplyPulseProperties(surface, 0f);
    }

    public void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration)
    {
        ShowThrust(origin, direction, range, lineWidth, duration, ThrustColor);
    }

    public void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration, Color color)
    {
        Initialize();
        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 liftedOrigin = origin + Vector3.up * 0.2f;
        float safeDuration = Mathf.Max(0.05f, duration);
        _thrustTimer = _thrustDuration = safeDuration;
        _thrustColor = color;
        _thrustLine.enabled = true;
        _thrustLine.loop = false;
        _thrustLine.positionCount = 2;
        _thrustLine.widthMultiplier = Mathf.Max(0.035f, lineWidth * 0.16f);
        _thrustLine.SetPosition(0, liftedOrigin);
        _thrustLine.SetPosition(1, liftedOrigin + direction * range);
        SetLineColor(_thrustLine, _thrustColor, 1f);

        MeshPulse surface = GetMeshPulse(_thrustSurfaces, "Blade Thrust Ribbon", _thrustMaterial, ownsMesh: true);
        surface.Root.transform.SetPositionAndRotation(liftedOrigin, Quaternion.LookRotation(direction, Vector3.up));
        surface.Root.transform.localScale = Vector3.one;
        surface.Timer = surface.Duration = safeDuration;
        surface.Color = color;
        BuildThrustSurface(surface.Mesh, range, lineWidth);
        surface.Renderer.enabled = true;
        ApplyPulseProperties(surface, 0f);
    }

    public void ShowDash(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration, Color color)
    {
        ShowThrust(origin, direction, range, Mathf.Max(0.08f, lineWidth * 0.62f), duration, color);
        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Mesh bladeMesh = _bladePrototype != null
            ? _bladePrototype.GetComponentInChildren<MeshFilter>(true)?.sharedMesh
            : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        for (int i = 0; i < 6; i++)
        {
            float t = (i + 0.5f) / 6f;
            MeshPulse ghost = GetMeshPulse(_dashAfterimages, "Atomic Dash Blade Afterimage", _atomicMaterial, ownsMesh: false, sharedMesh: bladeMesh);
            ghost.Root.transform.SetPositionAndRotation(
                origin + direction * (range * t) + Vector3.up * 0.22f,
                Quaternion.LookRotation(direction, Vector3.up));
            ghost.BaseScale = new Vector3(
                Mathf.Max(0.18f, lineWidth * 0.45f),
                Mathf.Max(0.08f, lineWidth * 0.16f),
                Mathf.Max(0.45f, lineWidth * 0.9f));
            ghost.Root.transform.localScale = ghost.BaseScale;
            ghost.Timer = ghost.Duration = Mathf.Max(0.08f, duration * Mathf.Lerp(0.45f, 0.85f, t));
            ghost.Color = color;
            ghost.Renderer.enabled = true;
            ApplyPulseProperties(ghost, t * 0.18f);
        }
    }

    private void Awake() => Initialize();

    private void OnValidate()
    {
        _orbitGuideAlpha = Mathf.Clamp01(_orbitGuideAlpha);
        _minimumBladeLength = Mathf.Max(0.02f, _minimumBladeLength);
        _baseTrailWidth = Mathf.Max(0.01f, _baseTrailWidth);
        _baseTrailTime = Mathf.Max(0.01f, _baseTrailTime);
    }

    private void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _propertyBlock = new MaterialPropertyBlock();
        _orbitLine = CreateLine("Blade Orbit", 0.035f);
        _thrustLine = CreateLine("Blade Thrust", 0.12f);
        if (_bladePrototype != null)
            _bladePrototype.SetActive(false);
    }

    private void BeginOrbitFrame()
    {
        int frame = Time.frameCount;
        if (_lastOrbitFrame == frame)
            return;

        _lastOrbitFrame = frame;
        _visibleBladeLineCount = 0;
        _visiblePhysicalBladeCount = 0;
        SetBladeLinesEnabled(false);
        SetPhysicalBladesEnabled(false);
    }

    private LineRenderer GetNextBladeLine()
    {
        if (_visibleBladeLineCount >= _bladeLines.Count)
            _bladeLines.Add(CreateLine($"Blade Contact {_bladeLines.Count + 1}", 0.12f));
        return _bladeLines[_visibleBladeLineCount++];
    }

    private BladeVisual GetNextPhysicalBlade()
    {
        if (_visiblePhysicalBladeCount >= _bladeVisuals.Count)
            _bladeVisuals.Add(CreatePhysicalBlade(_bladeVisuals.Count + 1));
        return _bladeVisuals[_visiblePhysicalBladeCount++];
    }

    private BladeVisual CreatePhysicalBlade(int index)
    {
        GameObject root;
        if (_bladePrototype != null)
        {
            root = Instantiate(_bladePrototype, transform);
            root.name = $"Physical Orbiting Blade {index}";
            root.SetActive(true);
        }
        else
        {
            root = new GameObject($"Physical Orbiting Blade {index}");
            root.transform.SetParent(transform, false);
            MeshFilter filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetLineMaterial();
            ConfigureRenderer(renderer);
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = GetLineMaterial();
        }

        TrailRenderer bladeTrail = root.GetComponentInChildren<TrailRenderer>(true);
        if (bladeTrail == null)
        {
            bladeTrail = root.AddComponent<TrailRenderer>();
            bladeTrail.sharedMaterial = _trailMaterial != null ? _trailMaterial : GetLineMaterial();
        }
        bladeTrail.autodestruct = false;
        bladeTrail.emitting = false;
        bladeTrail.minVertexDistance = 0.04f;
        bladeTrail.numCornerVertices = 2;
        bladeTrail.numCapVertices = 2;
        bladeTrail.shadowCastingMode = ShadowCastingMode.Off;
        bladeTrail.receiveShadows = false;

        return new BladeVisual
        {
            Root = root,
            Renderers = root.GetComponentsInChildren<Renderer>(true),
            Trail = bladeTrail
        };
    }

    private void DisableUnusedPhysicalBlades()
    {
        for (int i = _visiblePhysicalBladeCount; i < _bladeVisuals.Count; i++)
            SetBladeVisualEnabled(_bladeVisuals[i], false);
    }

    private void SetPhysicalBladesEnabled(bool enabled)
    {
        for (int i = 0; i < _bladeVisuals.Count; i++)
            SetBladeVisualEnabled(_bladeVisuals[i], enabled);
    }

    private static void SetBladeVisualEnabled(BladeVisual blade, bool enabled)
    {
        if (blade == null)
            return;
        for (int i = 0; i < blade.Renderers.Length; i++)
        {
            if (blade.Renderers[i] != null)
                blade.Renderers[i].enabled = enabled;
        }
        if (blade.Trail != null)
            blade.Trail.emitting = enabled;
    }

    private void ApplyBladeProperties(BladeVisual blade, Color color, float heat)
    {
        float emission = Mathf.Lerp(1.4f, 4.8f, heat);
        for (int i = 0; i < blade.Renderers.Length; i++)
        {
            Renderer renderer = blade.Renderers[i];
            if (renderer == null || renderer is TrailRenderer)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * emission);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetFloat(HeatId, heat);
            _propertyBlock.SetFloat(PulseId, 0.72f + heat * 0.28f);
            _propertyBlock.SetFloat(DissolveId, 0f);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void ConfigureTrail(TrailRenderer trail, Color color, float hitRadius, float heat)
    {
        if (trail == null)
            return;
        trail.sharedMaterial = _trailMaterial != null ? _trailMaterial : GetLineMaterial();
        trail.time = _baseTrailTime * Mathf.Lerp(0.85f, 1.75f, heat);
        trail.widthMultiplier = _baseTrailWidth * Mathf.Lerp(0.8f, 1.45f, heat) * Mathf.Clamp(hitRadius, 0.55f, 1.8f);
        trail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.55f, 0.62f),
            new Keyframe(1f, 0f));
        Gradient gradient = new();
        Color hot = Color.Lerp(color, Color.white, 0.28f + heat * 0.32f);
        gradient.SetKeys(
            new[] { new GradientColorKey(hot, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.82f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
    }

    private LineRenderer GetNextSlashLine(out int index)
    {
        for (int i = 0; i < _slashLines.Count; i++)
        {
            if (!_slashLines[i].enabled)
            {
                index = i;
                return _slashLines[i];
            }
        }

        LineRenderer line = CreateLine($"Blade Slash {_slashLines.Count + 1}", 0.16f);
        _slashLines.Add(line);
        _slashTimers.Add(0f);
        _slashDurations.Add(0f);
        _slashColors.Add(SlashColor);
        _slashOrigins.Add(Vector3.zero);
        _slashDirections.Add(Vector3.forward);
        _slashRanges.Add(1f);
        _slashHalfAngles.Add(45f);
        index = _slashLines.Count - 1;
        return line;
    }

    private MeshPulse GetMeshPulse(
        List<MeshPulse> pulses,
        string name,
        Material material,
        bool ownsMesh,
        Mesh sharedMesh = null)
    {
        for (int i = 0; i < pulses.Count; i++)
        {
            if (pulses[i].Timer <= 0f)
            {
                if (!ownsMesh && sharedMesh != null)
                    pulses[i].Renderer.GetComponent<MeshFilter>().sharedMesh = sharedMesh;
                return pulses[i];
            }
        }

        GameObject root = new($"{name} {pulses.Count + 1}");
        root.transform.SetParent(transform, false);
        MeshFilter filter = root.AddComponent<MeshFilter>();
        Mesh mesh = ownsMesh ? new Mesh { name = name, hideFlags = HideFlags.DontSave } : sharedMesh;
        if (ownsMesh)
            mesh.MarkDynamic();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material != null ? material : GetLineMaterial();
        ConfigureRenderer(renderer);
        MeshPulse pulse = new()
        {
            Root = root,
            Mesh = mesh,
            Renderer = renderer,
            OwnsMesh = ownsMesh
        };
        pulses.Add(pulse);
        return pulse;
    }

    private LineRenderer CreateLine(string childName, float width)
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = GetLineMaterial();
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        TickOrbit(deltaTime);
        TickOneShot(_thrustLine, _thrustColor, ref _thrustTimer, _thrustDuration, deltaTime);
        TickSlashLines(deltaTime);
        TickMeshPulses(_slashSurfaces, deltaTime, 0f);
        TickMeshPulses(_thrustSurfaces, deltaTime, 0.18f);
        TickMeshPulses(_dashAfterimages, deltaTime, 0.28f);
    }

    private void TickOrbit(float deltaTime)
    {
        if (_orbitTimer <= 0f)
        {
            SetEnabled(_orbitLine, false);
            SetBladeLinesEnabled(false);
            SetPhysicalBladesEnabled(false);
            return;
        }

        _orbitTimer -= deltaTime;
        float alpha = Mathf.Clamp01(_orbitTimer / Mathf.Max(0.01f, _orbitDuration));
        SetLineColor(_orbitLine, _orbitColor, alpha);
        SetBladeLineColors(alpha);
    }

    private void TickOneShot(LineRenderer line, Color color, ref float timer, float duration, float deltaTime)
    {
        if (timer <= 0f)
        {
            SetEnabled(line, false);
            return;
        }

        timer -= deltaTime;
        float alpha = Mathf.Clamp01(timer / Mathf.Max(0.01f, duration));
        SetLineColor(line, color, alpha);
    }

    private void TickMeshPulses(List<MeshPulse> pulses, float deltaTime, float expansion)
    {
        for (int i = 0; i < pulses.Count; i++)
        {
            MeshPulse pulse = pulses[i];
            if (pulse.Timer <= 0f)
                continue;
            pulse.Timer -= deltaTime;
            if (pulse.Timer <= 0f)
            {
                pulse.Renderer.enabled = false;
                continue;
            }

            float normalized = 1f - Mathf.Clamp01(pulse.Timer / Mathf.Max(0.01f, pulse.Duration));
            if (pulse.IsSlashSweep)
            {
                float reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.68f, normalized));
                BuildSlashSurface(pulse.Mesh, pulse.SlashRange, pulse.SlashConeAngle, reveal);
            }
            pulse.Root.transform.localScale = pulse.BaseScale * Mathf.Lerp(1f, 1f + expansion, normalized);
            ApplyPulseProperties(pulse, normalized);
        }
    }

    private void ApplyPulseProperties(MeshPulse pulse, float normalized)
    {
        if (pulse?.Renderer == null)
            return;
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalized);
        Color faded = pulse.Color;
        faded.a *= alpha;
        pulse.Renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, faded);
        _propertyBlock.SetColor(EmissionColorId, pulse.Color * Mathf.Lerp(3.8f, 0.4f, normalized));
        _propertyBlock.SetFloat(EmissionIntensityId, Mathf.Lerp(3.8f, 0.4f, normalized));
        _propertyBlock.SetFloat(PulseId, Mathf.Lerp(1f, 0.35f, normalized));
        _propertyBlock.SetFloat(DissolveId, Mathf.Clamp01(normalized * 0.92f));
        pulse.Renderer.SetPropertyBlock(_propertyBlock);
    }

    private void TickSlashLines(float deltaTime)
    {
        for (int i = 0; i < _slashLines.Count; i++)
        {
            LineRenderer line = _slashLines[i];
            if (!line.enabled)
                continue;
            _slashTimers[i] -= deltaTime;
            if (_slashTimers[i] <= 0f)
            {
                SetEnabled(line, false);
                continue;
            }
            float normalized = 1f - Mathf.Clamp01(_slashTimers[i] / Mathf.Max(0.01f, _slashDurations[i]));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, normalized));
            UpdateSlashHead(line, i, normalized);
            SetSlashLineGradient(line, _slashColors[i], fade);
        }
    }

    private void UpdateSlashHead(LineRenderer line, int index, float normalized)
    {
        float sweepProgress = Mathf.Clamp01(normalized / 0.82f);
        float easedSweep = 1f - Mathf.Pow(1f - sweepProgress, 3f);
        float head = Mathf.Lerp(-0.04f, 1.08f, easedSweep);
        float tail = head - SlashSweepWindow;
        float halfAngle = _slashHalfAngles[index];
        Vector3 origin = _slashOrigins[index];
        Vector3 direction = _slashDirections[index];
        float radius = Mathf.Max(0.05f, _slashRanges[index] * 0.985f);

        for (int i = 0; i < SlashHeadSegmentCount; i++)
        {
            float u = i / (float)(SlashHeadSegmentCount - 1);
            float arcProgress = Mathf.Clamp01(Mathf.Lerp(tail, head, u));
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, arcProgress);
            Vector3 pointDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            line.SetPosition(i, origin + pointDirection * radius);
        }
    }

    private static void SetSlashLineGradient(LineRenderer line, Color color, float alphaMultiplier)
    {
        Color tail = Color.Lerp(color, Color.white, 0.12f);
        Color hot = Color.Lerp(color, Color.white, 0.62f);
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(tail, 0f),
                new GradientColorKey(color, 0.55f),
                new GradientColorKey(hot, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.5f * alphaMultiplier, 0.32f),
                new GradientAlphaKey(alphaMultiplier, 0.82f),
                new GradientAlphaKey(0.12f * alphaMultiplier, 1f)
            });
        line.colorGradient = gradient;
    }

    private void OnDestroy()
    {
        DestroyPulseMeshes(_slashSurfaces);
        DestroyPulseMeshes(_thrustSurfaces);
    }

    private static void DestroyPulseMeshes(List<MeshPulse> pulses)
    {
        for (int i = 0; i < pulses.Count; i++)
        {
            if (!pulses[i].OwnsMesh || pulses[i].Mesh == null)
                continue;
            if (Application.isPlaying)
                Destroy(pulses[i].Mesh);
            else
                DestroyImmediate(pulses[i].Mesh);
        }
    }

    private static void BuildSlashSurface(Mesh mesh, float range, float coneAngle, float revealProgress)
    {
        int pointCount = SlashSegmentCount + 1;
        List<Vector3> vertices = new(pointCount * 2 + 160);
        List<Vector2> uvs = new(pointCount * 2 + 160);
        List<Color> colors = new(pointCount * 2 + 160);
        List<int> triangles = new(SlashSegmentCount * 6 + 360);
        float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
        float visibleEnd = Mathf.Lerp(0.012f, 1f, Mathf.Clamp01(revealProgress));
        float headTaperDistance = Mathf.Max(0.025f, Mathf.Min(0.14f, visibleEnd * 0.45f));

        // Only the already-swept portion exists. The live end is tapered as it
        // advances, so the effect materializes from left to right instead of
        // flashing a complete area-of-effect cone into existence.
        for (int i = 0; i < pointCount; i++)
        {
            float t = visibleEnd * (i / (float)(pointCount - 1));
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t) * Mathf.Deg2Rad;
            Vector3 radial = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float arcEnvelope = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.64f);
            float headEnvelope = visibleEnd >= 0.995f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, (visibleEnd - t) / headTaperDistance);
            float envelope = arcEnvelope * headEnvelope;
            float irregular = Mathf.Sin(t * Mathf.PI * 7f) * range * 0.008f * envelope;
            float outerRadius = Mathf.Max(0.05f, range * 0.985f + irregular);
            float thickness = range * Mathf.Lerp(0.012f, 0.205f, envelope);
            float innerRadius = Mathf.Max(range * 0.76f, outerRadius - thickness);
            float alpha = Mathf.Lerp(0.16f, 0.98f, envelope);

            vertices.Add(radial * innerRadius);
            vertices.Add(radial * outerRadius);
            uvs.Add(new Vector2(0f, t));
            uvs.Add(new Vector2(1f, t));
            colors.Add(new Color(1f, 1f, 1f, alpha * 0.78f));
            colors.Add(new Color(1f, 1f, 1f, alpha));
            if (i >= pointCount - 1)
                continue;
            int vertex = i * 2;
            AddQuadTriangles(triangles, vertex, vertex + 2, vertex + 1, vertex + 3);
        }

        AssignMesh(mesh, vertices.ToArray(), uvs.ToArray(), colors.ToArray(), triangles.ToArray());
    }

    private static void AddQuadTriangles(List<int> triangles, int innerStart, int innerEnd, int outerStart, int outerEnd)
    {
        triangles.Add(innerStart);
        triangles.Add(innerEnd);
        triangles.Add(outerStart);
        triangles.Add(outerStart);
        triangles.Add(innerEnd);
        triangles.Add(outerEnd);
    }

    private static void BuildThrustSurface(Mesh mesh, float range, float width)
    {
        int pointCount = ThrustSegmentCount + 1;
        Vector3[] vertices = new Vector3[pointCount * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color[] colors = new Color[vertices.Length];
        int[] triangles = new int[ThrustSegmentCount * 6];
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float halfWidth = Mathf.Max(0.025f, width * 0.5f * Mathf.Lerp(0.22f, 0.72f, Mathf.Sin(t * Mathf.PI)));
            float lateralNoise = Mathf.Sin(t * Mathf.PI * 7f) * width * 0.035f;
            vertices[i * 2] = new Vector3(-halfWidth + lateralNoise, 0f, range * t);
            vertices[i * 2 + 1] = new Vector3(halfWidth + lateralNoise, 0f, range * t);
            uvs[i * 2] = new Vector2(0f, t);
            uvs[i * 2 + 1] = new Vector2(1f, t);
            colors[i * 2] = colors[i * 2 + 1] = new Color(1f, 1f, 1f, Mathf.Sin(t * Mathf.PI));
            if (i >= pointCount - 1)
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
        AssignMesh(mesh, vertices, uvs, colors, triangles);
    }

    private static void AssignMesh(Mesh mesh, Vector3[] vertices, Vector2[] uvs, Color[] colors, int[] triangles)
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private static void ConfigureRenderer(MeshRenderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static int CountActive(List<MeshPulse> pulses)
    {
        int count = 0;
        for (int i = 0; i < pulses.Count; i++)
        {
            if (pulses[i].Timer > 0f && pulses[i].Renderer != null && pulses[i].Renderer.enabled)
                count++;
        }
        return count;
    }

    private static void SetEnabled(LineRenderer line, bool enabled)
    {
        if (line != null)
            line.enabled = enabled;
    }

    private void SetBladeLinesEnabled(bool enabled)
    {
        for (int i = 0; i < _bladeLines.Count; i++)
            SetEnabled(_bladeLines[i], enabled);
    }

    private void SetBladeLineColors(float alphaMultiplier)
    {
        for (int i = 0; i < _bladeLines.Count; i++)
            SetLineColor(_bladeLines[i], _bladeColor, alphaMultiplier);
    }

    private static void SetLineColor(LineRenderer line, Color color, float alphaMultiplier)
    {
        if (line == null)
            return;
        Color visible = color;
        visible.a *= alphaMultiplier;
        line.startColor = visible;
        line.endColor = visible;
    }

    private static Vector3 GetHorizontalDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static Material GetLineMaterial()
    {
        if (s_lineMaterial != null)
            return s_lineMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        s_lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_lineMaterial;
    }
}
