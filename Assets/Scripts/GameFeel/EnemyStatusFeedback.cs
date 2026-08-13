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
            if (renderer == null || renderer is LineRenderer || renderer.GetComponentInParent<EnemyStatusVisual>() != null)
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
        Vector3 size = found ? bounds.size : new Vector3(1f, 1.5f, 1f);
        radius = Mathf.Clamp(Mathf.Max(size.x, size.z) * 0.55f, 0.35f, 3.5f);
        height = Mathf.Clamp(size.y, 0.6f, 7f);
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
    private static Material s_material;

    private readonly LineRenderer[] _accents = new LineRenderer[AccentCount];
    private LineRenderer _lowerRing;
    private LineRenderer _upperRing;
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

        bool fullBody = _kind == WeaponStatusKind.Freeze || _kind == WeaponStatusKind.Vulnerable;
        float lowerY = _center.y - _height * 0.48f + 0.05f;
        float upperY = _kind == WeaponStatusKind.Slow ? lowerY + 0.08f : _center.y + _height * (fullBody ? 0.18f : 0.02f);
        DrawRing(_lowerRing, lowerY, _radius * pulseScale, core);
        DrawRing(_upperRing, upperY, _radius * (_kind == WeaponStatusKind.Vulnerable ? 0.78f : 0.62f) * pulseScale, edge);

        for (int i = 0; i < _accents.Length; i++)
        {
            float t = i / (float)_accents.Length;
            float angle = t * Mathf.PI * 2f + time * GetOrbitSpeed(_kind) + _seed;
            float radial = _radius * (0.72f + 0.2f * Mathf.Sin(time * 1.7f + i));
            float y01 = Mathf.Repeat(t + time * GetRiseSpeed(_kind), 1f);
            float y = _kind == WeaponStatusKind.Slow
                ? lowerY + 0.04f + Mathf.Sin(time * 2f + i) * 0.06f
                : _center.y - _height * 0.42f + y01 * _height * 0.84f;
            Vector3 center = new(_center.x + Mathf.Cos(angle) * radial, y, _center.z + Mathf.Sin(angle) * radial);
            float length = Mathf.Lerp(0.12f, 0.32f, _strength) * (_kind == WeaponStatusKind.Freeze ? 1.5f : 1f);
            Vector3 axis = GetAccentAxis(_kind, angle) * length;
            LineRenderer line = _accents[i];
            line.startColor = core;
            line.endColor = edge;
            line.SetPosition(0, center - axis * 0.5f);
            line.SetPosition(1, center + axis * 0.5f);
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

    private static void DrawRing(LineRenderer line, float y, float radius, Color color)
    {
        line.startColor = color;
        line.endColor = color;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
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
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        s_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_material;
    }

    private static float GetOrbitSpeed(WeaponStatusKind kind) => kind == WeaponStatusKind.Vulnerable ? 1.8f : kind == WeaponStatusKind.Freeze ? 0.25f : 0.75f;
    private static float GetRiseSpeed(WeaponStatusKind kind) => kind == WeaponStatusKind.Burn ? 0.65f : kind == WeaponStatusKind.JellifiedBurn ? -0.25f : 0.18f;
    private static Vector3 GetAccentAxis(WeaponStatusKind kind, float angle)
    {
        if (kind == WeaponStatusKind.Vulnerable)
            return new Vector3(-Mathf.Sin(angle), 0.2f, Mathf.Cos(angle)).normalized;
        if (kind == WeaponStatusKind.Freeze || kind == WeaponStatusKind.Slow)
            return new Vector3(Mathf.Cos(angle) * 0.35f, 1f, Mathf.Sin(angle) * 0.35f).normalized;
        return kind == WeaponStatusKind.JellifiedBurn ? Vector3.down : Vector3.up;
    }

    private static Color GetCoreColor(WeaponStatusKind kind)
    {
        return kind switch
        {
            WeaponStatusKind.JellifiedBurn => new Color(0.25f, 0.78f, 0.08f, 0.62f),
            WeaponStatusKind.Slow => new Color(0.42f, 0.78f, 1f, 0.55f),
            WeaponStatusKind.Freeze => new Color(0.8f, 0.97f, 1f, 0.82f),
            WeaponStatusKind.Vulnerable => new Color(1f, 0.18f, 0.72f, 0.68f),
            _ => new Color(1f, 0.28f, 0.03f, 0.58f)
        };
    }

    private static Color GetEdgeColor(WeaponStatusKind kind)
    {
        return kind switch
        {
            WeaponStatusKind.JellifiedBurn => new Color(0.72f, 0.95f, 0.12f, 0.38f),
            WeaponStatusKind.Slow => new Color(0.85f, 0.97f, 1f, 0.34f),
            WeaponStatusKind.Freeze => new Color(0.36f, 0.64f, 0.86f, 0.58f),
            WeaponStatusKind.Vulnerable => new Color(1f, 0.58f, 0.9f, 0.4f),
            _ => new Color(1f, 0.78f, 0.18f, 0.34f)
        };
    }
}
