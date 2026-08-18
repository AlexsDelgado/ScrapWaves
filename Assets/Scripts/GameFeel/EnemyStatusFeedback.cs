using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyStatusFeedback : MonoBehaviour
{
    private sealed class StatusState
    {
        public WeaponStatusKind Kind;
        public float Strength;
        public EnemyStatusVisual Visual;
    }

    [SerializeField] private EnemyReactionProfile _profile;

    private static readonly HashSet<EnemyStatusFeedback> s_active = new();
    private readonly Dictionary<WeaponStatusKind, StatusState> _states = new();
    private Renderer[] _renderers;

    public int ActiveStatusCount => _states.Count;
    public WeaponStatusMask ActiveMask
    {
        get
        {
            WeaponStatusMask mask = WeaponStatusMask.None;
            foreach (WeaponStatusKind kind in _states.Keys)
                mask |= ToMask(kind);
            return mask;
        }
    }

    private void Awake()
    {
        _profile = EnemyReactionProfile.Resolve(_profile);
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable() => s_active.Add(this);

    private void OnDisable()
    {
        ClearImmediate();
        s_active.Remove(this);
    }

    private void OnDestroy()
    {
        ClearImmediate();
        s_active.Remove(this);
    }

    public void ApplyOrRefresh(WeaponStatusKind kind, float duration, float strength = 1f)
    {
        if (!EnemyReactionRuntime.Enabled || duration <= 0f)
            return;

        s_active.Add(this);
        _profile = EnemyReactionProfile.Resolve(_profile);
        if (!ResolveExclusiveStatus(kind))
            return;
        if (_states.TryGetValue(kind, out StatusState existing))
        {
            existing.Strength = Mathf.Max(existing.Strength, Mathf.Clamp01(strength));
            existing.Visual.Refresh(duration, existing.Strength);
            RefreshSuppression();
            return;
        }

        if (_states.Count >= _profile.MaximumStatusVisualsPerEnemy || EnemyStatusVisual.ActiveCount >= _profile.MaximumGlobalStatusVisuals)
            RemoveLowestPriorityFor(kind);
        if (_states.Count >= _profile.MaximumStatusVisualsPerEnemy || EnemyStatusVisual.ActiveCount >= _profile.MaximumGlobalStatusVisuals)
            return;

        ResolveBounds(out Vector3 localCenter, out float radius, out float height);
        EnemyStatusVisual visual = EnemyStatusVisual.Create(this, transform, kind, localCenter, radius, height, duration, strength);
        if (visual == null)
            return;
        _states[kind] = new StatusState { Kind = kind, Strength = Mathf.Clamp01(strength), Visual = visual };
        RefreshSuppression();
    }

    public void Remove(WeaponStatusKind kind)
    {
        if (!_states.Remove(kind, out StatusState state))
            return;
        state.Visual?.Dismiss(_profile != null ? _profile.StatusFadeDuration : 0.24f);
        RefreshSuppression();
    }

    public void Pulse(WeaponStatusKind kind, float amount = 1f)
    {
        if (_states.TryGetValue(kind, out StatusState state))
            state.Visual?.Pulse(amount);
    }

    internal void Expire(WeaponStatusKind kind, EnemyStatusVisual visual)
    {
        if (!_states.TryGetValue(kind, out StatusState state) || state.Visual != visual)
            return;
        Remove(kind);
    }

    public static void ApplyOrRefresh(Transform target, WeaponStatusKind kind, float duration, float strength = 1f)
    {
        EnemyStatusFeedback feedback = GetOrCreate(target);
        feedback?.ApplyOrRefresh(kind, duration, strength);
    }

    public static void Remove(Transform target, WeaponStatusKind kind)
    {
        Find(target)?.Remove(kind);
    }

    public static void Pulse(Transform target, WeaponStatusKind kind, float amount = 1f)
    {
        Find(target)?.Pulse(kind, amount);
    }

    public static WeaponStatusMask ResolveMask(Transform target)
    {
        EnemyStatusFeedback feedback = Find(target);
        return feedback != null ? feedback.ActiveMask : WeaponStatusMask.None;
    }

    public static void ClearAllActive()
    {
        EnemyStatusFeedback[] snapshot = new EnemyStatusFeedback[s_active.Count];
        s_active.CopyTo(snapshot);
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i]?.ClearImmediate();
    }

    private bool ResolveExclusiveStatus(WeaponStatusKind kind)
    {
        if (kind == WeaponStatusKind.JellifiedBurn)
            Remove(WeaponStatusKind.Burn);
        else if (kind == WeaponStatusKind.Burn && _states.ContainsKey(WeaponStatusKind.JellifiedBurn))
            return false;
        return true;
    }

    private void RefreshSuppression()
    {
        bool freeze = _states.ContainsKey(WeaponStatusKind.Freeze);
        if (_states.TryGetValue(WeaponStatusKind.Slow, out StatusState slow))
            slow.Visual.SetSuppressed(freeze);
    }

    private void RemoveLowestPriorityFor(WeaponStatusKind incoming)
    {
        WeaponStatusKind candidate = incoming;
        int candidatePriority = GetPriority(incoming);
        bool found = false;
        foreach (WeaponStatusKind kind in _states.Keys)
        {
            int priority = GetPriority(kind);
            if (!found || priority < candidatePriority)
            {
                candidate = kind;
                candidatePriority = priority;
                found = true;
            }
        }
        if (found && GetPriority(incoming) > candidatePriority)
            Remove(candidate);
    }

    private void ClearImmediate()
    {
        foreach (StatusState state in _states.Values)
            state.Visual?.Dismiss(0f);
        _states.Clear();
    }

    private void ResolveBounds(out Vector3 localCenter, out float radius, out float height)
    {
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new(transform.position, Vector3.one);
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (!IsBodyRenderer(renderer))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        localCenter = transform.InverseTransformPoint(found ? bounds.center : transform.position + Vector3.up * 0.75f);
        Vector3 worldSize = found ? bounds.size : new Vector3(1f, 1.5f, 1f);
        Vector3 rootScale = transform.lossyScale;
        float scaleX = Mathf.Max(0.001f, Mathf.Abs(rootScale.x));
        float scaleY = Mathf.Max(0.001f, Mathf.Abs(rootScale.y));
        float scaleZ = Mathf.Max(0.001f, Mathf.Abs(rootScale.z));
        float localHalfX = worldSize.x * 0.5f / scaleX;
        float localHalfZ = worldSize.z * 0.5f / scaleZ;
        float localHeight = worldSize.y / scaleY;
        float clearance = Mathf.Clamp(localHeight * 0.055f, 0.08f, 0.35f);
        radius = Mathf.Clamp(Mathf.Max(localHalfX, localHalfZ) * 1.18f + clearance, 0.42f, 12f);
        height = Mathf.Clamp(localHeight, 0.6f, 20f);
    }

    internal static bool IsBodyRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || renderer is LineRenderer || renderer is ParticleSystemRenderer ||
            renderer.GetComponentInParent<EnemyStatusVisual>() != null || renderer.GetComponent<TMPro.TMP_Text>() != null)
            return false;
        string objectName = renderer.gameObject.name;
        if (objectName.StartsWith("[Enemy Hit Flash]") || objectName.StartsWith("[Enemy Freeze Shell]") ||
            objectName.StartsWith("[Enemy Status]"))
            return false;
        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private static EnemyStatusFeedback GetOrCreate(Transform target)
    {
        if (target == null)
            return null;
        EnemyStatusFeedback existing = Find(target);
        if (existing != null)
            return existing;
        Transform root = ResolveEnemyRoot(target);
        return root != null ? root.gameObject.AddComponent<EnemyStatusFeedback>() : null;
    }

    private static EnemyStatusFeedback Find(Transform target)
    {
        if (target == null)
            return null;
        EnemyStatusFeedback feedback = target.GetComponentInParent<EnemyStatusFeedback>();
        if (feedback == null)
            feedback = target.GetComponentInChildren<EnemyStatusFeedback>(true);
        return feedback;
    }

    private static Transform ResolveEnemyRoot(Transform target)
    {
        EnemyHealth health = target.GetComponentInParent<EnemyHealth>();
        if (health != null)
            return health.transform;
        WeaponDummyEnemy dummy = target.GetComponentInParent<WeaponDummyEnemy>();
        if (dummy != null)
            return dummy.transform;
        return target.root != null ? target.root : target;
    }

    private static int GetPriority(WeaponStatusKind kind)
    {
        return kind switch
        {
            WeaponStatusKind.Freeze => 5,
            WeaponStatusKind.JellifiedBurn => 4,
            WeaponStatusKind.Vulnerable => 3,
            WeaponStatusKind.Burn => 2,
            _ => 1
        };
    }

    private static WeaponStatusMask ToMask(WeaponStatusKind kind) => (WeaponStatusMask)(1 << (int)kind);
}

[DisallowMultipleComponent]
public sealed class EnemyStatusVisual : MonoBehaviour
{
    private const int RingSegments = 32;
    private const int AccentCount = 6;
    private static readonly int IceColorId = Shader.PropertyToID("_IceColor");
    private static readonly int IceEdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int IceOpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int IceFrostId = Shader.PropertyToID("_Frost");
    private static readonly int IceGlintId = Shader.PropertyToID("_Glint");
    private static readonly int IceLuminescenceId = Shader.PropertyToID("_Luminescence");
    private static readonly AnimationCurve s_taperCurve = new(
        new Keyframe(0f, 0.72f),
        new Keyframe(0.18f, 1f),
        new Keyframe(0.7f, 0.48f),
        new Keyframe(1f, 0f));
    private static readonly AnimationCurve s_angularCurve = new(
        new Keyframe(0f, 0.72f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0.72f));
    private static Material s_material;
    private static Material s_iceMaterial;

    private readonly LineRenderer[] _accents = new LineRenderer[AccentCount];
    private readonly List<Renderer> _freezeShells = new();
    private LineRenderer _lowerRing;
    private LineRenderer _upperRing;
    private MaterialPropertyBlock _iceBlock;
    private WeaponStatusKind _kind;
    private Vector3 _center;
    private float _radius;
    private float _height;
    private float _remaining;
    private float _strength;
    private float _pulse;
    private float _fadeDuration;
    private bool _dismissing;
    private bool _suppressed;
    private float _seed;
    private EnemyStatusFeedback _owner;
    private bool _counted;

    public static int ActiveCount { get; private set; }

    public static EnemyStatusVisual Create(
        EnemyStatusFeedback owner,
        Transform target,
        WeaponStatusKind kind,
        Vector3 localCenter,
        float radius,
        float height,
        float duration,
        float strength)
    {
        if (target == null)
            return null;
        GameObject go = new($"[Enemy Status] {kind}");
        go.transform.SetParent(target, false);
        EnemyStatusVisual visual = go.AddComponent<EnemyStatusVisual>();
        visual.Configure(owner, kind, localCenter, radius, height, duration, strength);
        return visual;
    }

    private void Configure(EnemyStatusFeedback owner, WeaponStatusKind kind, Vector3 center, float radius, float height, float duration, float strength)
    {
        _owner = owner;
        _kind = kind;
        _center = center;
        _radius = radius;
        _height = height;
        _remaining = Mathf.Max(0.05f, duration);
        _strength = Mathf.Clamp01(strength);
        _seed = Mathf.Abs(transform.position.x * 17.3f + transform.position.z * 29.7f + (int)kind * 11.9f);
        _lowerRing = CreateLine("Lower Status Ring", true, 0.025f);
        _upperRing = CreateLine("Upper Status Ring", true, 0.018f);
        for (int i = 0; i < AccentCount; i++)
            _accents[i] = CreateLine($"Status Accent {i}", false, 0.018f + (i % 3) * 0.006f);
        if (_kind == WeaponStatusKind.Freeze)
            CreateFreezeShells(target: transform.parent);
        ActiveCount++;
        _counted = true;
        ApplyFrame();
    }

    public void Refresh(float duration, float strength)
    {
        _remaining = Mathf.Max(_remaining, duration);
        _strength = Mathf.Max(_strength, Mathf.Clamp01(strength));
        _dismissing = false;
        _pulse = Mathf.Max(_pulse, 0.45f);
    }

    public void Pulse(float amount)
    {
        _pulse = Mathf.Max(_pulse, Mathf.Clamp01(amount));
    }

    public void SetSuppressed(bool value) => _suppressed = value;

    public void Dismiss(float duration)
    {
        _dismissing = true;
        _fadeDuration = Mathf.Max(0f, duration);
        _remaining = _fadeDuration;
        if (_fadeDuration <= 0f)
            DestroySelf();
    }

    private void Update()
    {
        _remaining -= _dismissing ? Time.unscaledDeltaTime : Time.deltaTime;
        _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime * 4.5f);
        ApplyFrame();
        if (_remaining <= 0f)
        {
            if (_dismissing)
                DestroySelf();
            else if (_owner != null)
                _owner.Expire(_kind, this);
            else
                Dismiss(0f);
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _freezeShells.Count; i++)
        {
            if (_freezeShells[i] != null)
                DestroySafely(_freezeShells[i].gameObject);
        }
        _freezeShells.Clear();
        if (_counted)
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }

    private void ApplyFrame()
    {
        float fade = _dismissing && _fadeDuration > 0f ? Mathf.Clamp01(_remaining / _fadeDuration) : 1f;
        float quality = EnemyReactionRuntime.Quality switch
        {
            GameFeelQualityLevel.Low => 0.55f,
            GameFeelQualityLevel.Medium => 0.78f,
            _ => 1f
        };
        float visibility = (_suppressed ? 0f : 1f) * fade * quality;
        float flashScale = EnemyReactionRuntime.ReducedFlash ? 0.45f : 1f;
        Color core = GetCoreColor(_kind);
        Color edge = GetEdgeColor(_kind);
        core.a *= visibility * flashScale;
        edge.a *= visibility * flashScale;
        float time = Time.unscaledTime;
        float pulseScale = 1f + _pulse * 0.16f + Mathf.Sin(time * 3.5f + _seed) * 0.025f;
        float lowerY = _center.y - _height * 0.48f + 0.05f;
        if (_kind == WeaponStatusKind.Freeze)
            ApplyFreezeShell(visibility * flashScale, time);
        switch (_kind)
        {
            case WeaponStatusKind.Burn:
                DrawBurn(time, lowerY, pulseScale, core, edge);
                break;
            case WeaponStatusKind.JellifiedBurn:
                DrawJellified(time, lowerY, pulseScale, core, edge);
                break;
            case WeaponStatusKind.Slow:
                DrawSlow(time, lowerY, pulseScale, core, edge);
                break;
            case WeaponStatusKind.Freeze:
                DrawFreeze(lowerY, pulseScale, core, edge);
                break;
            case WeaponStatusKind.Vulnerable:
                DrawVulnerable(time, lowerY, pulseScale, core, edge);
                break;
        }
    }

    private void DrawBurn(float time, float lowerY, float pulseScale, Color core, Color edge)
    {
        SetRingEnabled(_lowerRing, true);
        SetRingEnabled(_upperRing, false);
        _lowerRing.widthMultiplier = Mathf.Clamp(_radius * 0.085f, 0.04f, 0.12f);
        DrawWobblyRing(_lowerRing, lowerY, _radius * (1.04f + _pulse * 0.08f), edge, time * 1.35f, 0.08f);
        for (int i = 0; i < _accents.Length; i++)
        {
            float phase = time * (4.2f + i * 0.19f) + i * 1.73f + _seed;
            float angle = i / (float)_accents.Length * Mathf.PI * 2f + Mathf.Sin(phase * 0.43f) * 0.13f;
            float radial = _radius * (0.98f + (i % 3) * 0.1f);
            float baseY = lowerY + _height * ((i % 3) * 0.145f);
            float flicker = 0.78f + Mathf.Sin(phase) * 0.14f + Mathf.Sin(phase * 2.37f) * 0.08f;
            float flameHeight = _height * Mathf.Lerp(0.28f, 0.48f, _strength) * flicker * pulseScale;
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 tangent = new(-outward.z, 0f, outward.x);
            Vector3 basePoint = new Vector3(_center.x, baseY, _center.z) + outward * radial;
            float lean = Mathf.Sin(phase * 0.72f) * _radius * 0.16f;
            LineRenderer line = _accents[i];
            ConfigureLine(line, 5, false, Mathf.Clamp(_radius * 0.098f + _pulse * 0.016f, 0.04f, 0.14f), core, edge);
            line.widthCurve = s_taperCurve;
            line.SetPosition(0, basePoint);
            line.SetPosition(1, basePoint - outward * _radius * 0.055f + tangent * lean * 0.25f + Vector3.up * flameHeight * 0.2f);
            line.SetPosition(2, basePoint + tangent * (lean + _radius * 0.07f) + Vector3.up * flameHeight * 0.48f);
            line.SetPosition(3, basePoint - tangent * (_radius * 0.08f - lean * 0.35f) + Vector3.up * flameHeight * 0.77f);
            line.SetPosition(4, basePoint + outward * _radius * 0.025f + tangent * lean * 0.55f + Vector3.up * flameHeight);
        }
    }

    private void DrawJellified(float time, float lowerY, float pulseScale, Color core, Color edge)
    {
        SetRingEnabled(_lowerRing, true);
        SetRingEnabled(_upperRing, true);
        _lowerRing.widthMultiplier = Mathf.Clamp(_radius * 0.105f, 0.045f, 0.145f);
        _upperRing.widthMultiplier = Mathf.Clamp(_radius * 0.06f, 0.026f, 0.085f);
        DrawWobblyRing(_lowerRing, lowerY, _radius * (1.02f + _pulse * 0.08f), core, time, 0.075f);
        DrawSplashCrown(_upperRing, lowerY + 0.035f, _radius * 1.08f * pulseScale, edge, time);
        for (int i = 0; i < _accents.Length; i++)
        {
            float angle = i / (float)_accents.Length * Mathf.PI * 2f + _seed * 0.19f;
            float rise = Mathf.Repeat(time * (0.3f + (i % 3) * 0.035f) + i * 0.19f, 1f);
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 tangent = new(-outward.z, 0f, outward.x);
            float y = Mathf.Lerp(lowerY + 0.08f, _center.y + _height * 0.44f, rise);
            Vector3 bubbleCenter = new Vector3(_center.x, y, _center.z) + outward * _radius * (0.94f + (i % 2) * 0.14f);
            float lifeScale = Mathf.Sin(Mathf.Clamp01(rise) * Mathf.PI);
            float bubbleRadius = _radius * (0.075f + (i % 3) * 0.025f) * Mathf.Lerp(0.35f, 1f, lifeScale) * pulseScale;
            Color bubbleCore = core;
            Color bubbleEdge = edge;
            bubbleCore.a *= 1f - Mathf.SmoothStep(0.74f, 1f, rise);
            bubbleEdge.a *= 1f - Mathf.SmoothStep(0.68f, 1f, rise);
            LineRenderer line = _accents[i];
            ConfigureLine(line, 10, true, Mathf.Clamp(_radius * 0.052f, 0.022f, 0.075f), bubbleCore, bubbleEdge);
            line.widthCurve = s_angularCurve;
            DrawVerticalLoop(line, bubbleCenter, tangent, bubbleRadius, 10, 0.82f + Mathf.Sin(time * 2.6f + i) * 0.1f);
        }
    }

    private void DrawSlow(float time, float lowerY, float pulseScale, Color core, Color edge)
    {
        SetRingEnabled(_lowerRing, true);
        SetRingEnabled(_upperRing, true);
        _lowerRing.widthMultiplier = Mathf.Clamp(_radius * 0.062f, 0.028f, 0.088f);
        _upperRing.widthMultiplier = Mathf.Clamp(_radius * 0.068f, 0.03f, 0.095f);
        DrawChainBand(_lowerRing, lowerY + _height * 0.18f, _radius * 1.08f * pulseScale, core, time * 0.18f, 3);
        DrawChainBand(_upperRing, lowerY + _height * 0.49f, _radius * 1.04f * pulseScale, edge, -time * 0.13f, 2);
        for (int i = 0; i < _accents.Length; i++)
        {
            float angle = i / (float)_accents.Length * Mathf.PI * 2f + time * 0.13f + _seed;
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 tangent = new(-outward.z, 0f, outward.x);
            float linkY = lowerY + _height * (0.17f + (i % 3) * 0.17f);
            Vector3 center = new Vector3(_center.x, linkY, _center.z) + outward * _radius * (1.22f + (i % 2) * 0.08f);
            float linkRadius = _radius * Mathf.Lerp(0.12f, 0.18f, _strength) * pulseScale;
            LineRenderer line = _accents[i];
            ConfigureLine(line, 8, true, Mathf.Clamp(_radius * 0.066f, 0.028f, 0.095f), core, edge);
            line.widthCurve = s_angularCurve;
            Vector3 across = (i & 1) == 0 ? tangent : Vector3.Lerp(tangent, outward, 0.72f).normalized;
            Vector3 tall = (i & 1) == 0 ? Vector3.up : Vector3.Lerp(Vector3.up, outward, 0.58f).normalized;
            DrawChainLink(line, center, across, tall, linkRadius, 8);
        }
    }

    private void DrawFreeze(float lowerY, float pulseScale, Color core, Color edge)
    {
        SetRingEnabled(_lowerRing, true);
        SetRingEnabled(_upperRing, true);
        _lowerRing.widthMultiplier = 0.038f;
        _upperRing.widthMultiplier = 0.024f;
        DrawPolygon(_lowerRing, lowerY, _radius * pulseScale, 6, core, 0f);
        DrawPolygon(_upperRing, _center.y + _height * 0.16f, _radius * 1.06f * pulseScale, 6, edge, Mathf.PI / 6f);

        // The full-body ice shell and horizontal facets carry the freeze read.
        // Tall accent spikes looked like unrelated vertical guide lines.
        for (int i = 0; i < _accents.Length; i++)
            _accents[i].enabled = false;
    }

    private void DrawVulnerable(float time, float lowerY, float pulseScale, Color core, Color edge)
    {
        SetRingEnabled(_lowerRing, false);
        SetRingEnabled(_upperRing, true);
        _upperRing.widthMultiplier = 0.032f;
        float orbit = time * 1.35f + _seed;
        DrawPolygon(_upperRing, _center.y + _height * 0.05f, _radius * 1.08f * pulseScale, 4, edge, orbit);
        for (int i = 0; i < _accents.Length; i++)
        {
            float angle = i / (float)_accents.Length * Mathf.PI * 2f - orbit * 0.72f;
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 tangent = new(-outward.z, 0f, outward.x);
            float y = Mathf.Lerp(lowerY + _height * 0.2f, lowerY + _height * 0.78f, (i % 3) / 2f);
            Vector3 tip = new Vector3(_center.x, y, _center.z) + outward * _radius * 1.02f;
            float bracket = _radius * Mathf.Lerp(0.16f, 0.28f, _strength) * pulseScale;
            LineRenderer line = _accents[i];
            ConfigureLine(line, 3, false, Mathf.Clamp(_radius * 0.06f, 0.03f, 0.085f), core, edge);
            line.widthCurve = s_taperCurve;
            line.SetPosition(0, tip + outward * bracket + tangent * bracket * 0.65f);
            line.SetPosition(1, tip);
            line.SetPosition(2, tip + outward * bracket - tangent * bracket * 0.65f);
        }
    }

    private LineRenderer CreateLine(string childName, bool loop, float width)
    {
        GameObject child = new(childName);
        child.transform.SetParent(transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = loop ? RingSegments : 2;
        line.sharedMaterial = GetMaterial();
        line.widthMultiplier = width;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void DrawRing(LineRenderer line, float y, float radius, Color color)
    {
        if (line.positionCount != RingSegments)
            line.positionCount = RingSegments;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(_center.x + Mathf.Cos(angle) * radius, y, _center.z + Mathf.Sin(angle) * radius));
        }
    }

    private void DrawWobblyRing(LineRenderer line, float y, float radius, Color color, float time, float wobble)
    {
        if (line.positionCount != RingSegments)
            line.positionCount = RingSegments;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f;
            float localRadius = radius * (1f + Mathf.Sin(angle * 5f + time * 2.2f) * wobble);
            line.SetPosition(i, new Vector3(_center.x + Mathf.Cos(angle) * localRadius, y, _center.z + Mathf.Sin(angle) * localRadius));
        }
    }

    private void DrawSplashCrown(LineRenderer line, float y, float radius, Color color, float time)
    {
        const int points = 12;
        if (line.positionCount != points)
            line.positionCount = points;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f;
            float alternating = (i & 1) == 0 ? 1f : 0.9f;
            float wobble = 1f + Mathf.Sin(time * 2.8f + angle * 3f + _seed) * 0.075f;
            float localRadius = radius * alternating * wobble;
            float lift = (i & 1) == 0 ? _height * 0.055f : 0f;
            line.SetPosition(i, new Vector3(
                _center.x + Mathf.Cos(angle) * localRadius,
                y + lift,
                _center.z + Mathf.Sin(angle) * localRadius));
        }
    }

    private void DrawChainBand(LineRenderer line, float y, float radius, Color color, float rotation, int sags)
    {
        const int points = 24;
        if (line.positionCount != points)
            line.positionCount = points;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        line.widthCurve = s_angularCurve;
        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f + rotation;
            float facetedRadius = radius * (1f + ((i & 1) == 0 ? 0.035f : -0.035f));
            float sag = (0.5f + 0.5f * Mathf.Cos(angle * sags)) * _height * 0.035f;
            line.SetPosition(i, new Vector3(
                _center.x + Mathf.Cos(angle) * facetedRadius,
                y - sag,
                _center.z + Mathf.Sin(angle) * facetedRadius));
        }
    }

    private static void DrawVerticalLoop(LineRenderer line, Vector3 center, Vector3 horizontal, float radius, int points, float squash)
    {
        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f;
            line.SetPosition(i, center + horizontal * (Mathf.Cos(angle) * radius) + Vector3.up * (Mathf.Sin(angle) * radius * squash));
        }
    }

    private static void DrawChainLink(LineRenderer line, Vector3 center, Vector3 across, Vector3 tall, float radius, int points)
    {
        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius * 0.72f;
            float y = Mathf.Sin(angle) * radius * 1.22f;
            line.SetPosition(i, center + across * x + tall * y);
        }
    }

    private void DrawPolygon(LineRenderer line, float y, float radius, int sides, Color color, float rotation)
    {
        if (line.positionCount != sides)
            line.positionCount = sides;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f + rotation;
            line.SetPosition(i, new Vector3(_center.x + Mathf.Cos(angle) * radius, y, _center.z + Mathf.Sin(angle) * radius));
        }
    }

    private static void SetRingEnabled(LineRenderer line, bool enabled)
    {
        if (line != null)
            line.enabled = enabled;
    }

    private static void ConfigureLine(LineRenderer line, int positions, bool loop, float width, Color start, Color end)
    {
        line.enabled = true;
        if (line.positionCount != positions)
            line.positionCount = positions;
        line.loop = loop;
        line.widthMultiplier = width;
        line.startColor = start;
        line.endColor = end;
    }

    private void CreateFreezeShells(Transform target)
    {
        Material material = GetIceMaterial();
        if (target == null || material == null)
            return;
        Renderer[] sources = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            Renderer source = sources[i];
            if (!EnemyStatusFeedback.IsBodyRenderer(source))
                continue;
            Renderer shell = CreateFreezeShell(source, material);
            if (shell != null)
                _freezeShells.Add(shell);
        }
    }

    private static Renderer CreateFreezeShell(Renderer source, Material material)
    {
        GameObject go = new("[Enemy Freeze Shell] " + source.gameObject.name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(source.transform, false);
        go.transform.localScale = Vector3.one * 1.045f;
        Renderer shell = null;
        if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
        {
            SkinnedMeshRenderer copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = skinned.sharedMesh;
            copy.rootBone = skinned.rootBone;
            copy.bones = skinned.bones;
            copy.localBounds = skinned.localBounds;
            shell = copy;
        }
        else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            shell = go.AddComponent<MeshRenderer>();
        }
        if (shell == null)
        {
            DestroySafely(go);
            return null;
        }
        int materialCount = Mathf.Max(1, source.sharedMaterials.Length);
        Material[] materials = new Material[materialCount];
        for (int i = 0; i < materialCount; i++)
            materials[i] = material;
        shell.sharedMaterials = materials;
        shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shell.receiveShadows = false;
        shell.enabled = false;
        return shell;
    }

    private void ApplyFreezeShell(float visibility, float time)
    {
        if (_freezeShells.Count == 0)
            return;
        _iceBlock ??= new MaterialPropertyBlock();
        float glint = EnemyReactionRuntime.ReducedFlash ? 0.24f : 0.62f + Mathf.Sin(time * 1.9f + _seed) * 0.1f;
        for (int i = 0; i < _freezeShells.Count; i++)
        {
            Renderer shell = _freezeShells[i];
            if (shell == null)
                continue;
            shell.enabled = visibility > 0.001f;
            _iceBlock.Clear();
            _iceBlock.SetColor(IceColorId, new Color(0.16f, 0.62f, 1f, 1f));
            _iceBlock.SetColor(IceEdgeColorId, new Color(0.86f, 0.98f, 1f, 1f));
            _iceBlock.SetFloat(IceOpacityId, visibility * Mathf.Lerp(0.64f, 0.84f, _strength));
            _iceBlock.SetFloat(IceFrostId, Mathf.Lerp(0.72f, 0.96f, _strength));
            _iceBlock.SetFloat(IceGlintId, glint);
            _iceBlock.SetFloat(IceLuminescenceId, 0.4f);
            shell.SetPropertyBlock(_iceBlock);
        }
    }

    private void DestroySelf()
    {
        if (this == null)
            return;
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    private static Material GetMaterial()
    {
        if (s_material != null)
            return s_material;
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Enemy Status Line");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        s_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (s_material.HasProperty("_Brightness")) s_material.SetFloat("_Brightness", 1.08f);
        if (s_material.HasProperty("_Luminescence")) s_material.SetFloat("_Luminescence", 0.4f);
        if (s_material.HasProperty("_Pulse")) s_material.SetFloat("_Pulse", 0.35f);
        return s_material;
    }

    private static Material GetIceMaterial()
    {
        if (s_iceMaterial != null)
            return s_iceMaterial;
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Enemy Ice Shell");
        if (shader == null)
            return null;
        s_iceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_iceMaterial;
    }

    private static void DestroySafely(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(value);
        else
            Object.DestroyImmediate(value);
    }

    private static Color GetCoreColor(WeaponStatusKind kind)
    {
        return kind switch
        {
            WeaponStatusKind.JellifiedBurn => new Color(0.12f, 0.92f, 0.025f, 0.92f),
            WeaponStatusKind.Slow => new Color(0.48f, 0.16f, 1f, 0.92f),
            WeaponStatusKind.Freeze => new Color(0.76f, 0.98f, 1f, 0.98f),
            WeaponStatusKind.Vulnerable => new Color(1f, 0.06f, 0.68f, 0.92f),
            _ => new Color(1f, 0.9f, 0.2f, 0.96f)
        };
    }

    private static Color GetEdgeColor(WeaponStatusKind kind)
    {
        return kind switch
        {
            WeaponStatusKind.JellifiedBurn => new Color(0.82f, 1f, 0.08f, 0.84f),
            WeaponStatusKind.Slow => new Color(0.88f, 0.68f, 1f, 0.82f),
            WeaponStatusKind.Freeze => new Color(0.08f, 0.55f, 1f, 0.86f),
            WeaponStatusKind.Vulnerable => new Color(1f, 0.6f, 0.96f, 0.72f),
            _ => new Color(1f, 0.16f, 0.01f, 0.86f)
        };
    }
}
