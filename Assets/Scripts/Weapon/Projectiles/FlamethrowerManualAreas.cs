using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>World-space manual flames. One clock and target set serve every live area.</summary>
[DefaultExecutionOrder(1000)]
public sealed class FlamethrowerManualAreas : MonoBehaviour
{
    private const float Skin = 0.002f;
    // Fixed terrain core, independent of the growing damage trigger and area-size upgrades.
    public const float TerrainCoreRadius = 0.025f;
    private readonly List<Area> _active = new();
    private readonly Stack<Area> _available = new();
    private readonly HashSet<IDamageable> _seen = new();
    private readonly List<Hit> _targets = new();
    private Collider[] _overlaps = new Collider[64];
    private RaycastHit[] _worldHits = new RaycastHit[32];
    private Transform _owner;
    private Func<Transform, Vector3, bool> _applyHit;
    private Material _material;
    private bool _ownsMaterial;
    private FlamethrowerManualPlume _plume;
    private float _visualTime;
    private float _emissionRemaining;
    private float _tickRemaining;
    private float _tickInterval;
    private int _maximumTargets;
    private int _created;
    private int _emitted;

    public int ActiveCount => _active.Count;
    public int VisualParticleCount => _plume != null ? _plume.ParticleCount : 0;
    public float VisualTime => _visualTime;
    public int CreatedCount => _created;
    public int LastTargetCount { get; private set; }
    public int LastAppliedCount { get; private set; }
    public Vector3 GetCenter(int index) => _active[index].Root.transform.position;
    public float GetRadius(int index) => _active[index].Radius;
    public Vector3 GetVelocity(int index) => _active[index].Velocity;

    private sealed class Area
    {
        public GameObject Root;
        public SphereCollider Trigger;
        public readonly Vector3[] PathPoints = new Vector3[16];
        public readonly float[] PathAges = new float[16];
        public int PathCount;
        public int Seed;
        public FlamethrowerStreamStyle Style;
        public float Heat;
        public Vector3 Velocity, VisualDirection;
        public float Deceleration, Age, Lifetime, StartRadius, EndRadius, Radius;
    }

    private struct Hit
    {
        public Transform Target;
        public Vector3 Origin;
        public float Distance;
    }

    public void Initialize(Transform owner, Func<Transform, Vector3, bool> applyHit, Material material)
    {
        _owner = owner;
        _applyHit = applyHit;
        _material = material != null ? material : Resources.Load<Material>("GameFeel/ManualFlame");
        if (_material == null)
        {
            _material = new Material(Shader.Find("ScrapWaves/GameFeel/Manual Flame Sprite")) { hideFlags = HideFlags.DontSave };
            _ownsMaterial = true;
        }
        _plume = new FlamethrowerManualPlume(transform, _material);
    }

    // Delay newly emitted areas within this frame; LateUpdate advances only their actual age.
    public void Emit(float duration, Vector3 origin, Vector3 direction, float range, float size,
        FlamethrowerTuning tuning, FlamethrowerStreamStyle style, float heat)
    {
        if (duration <= 0f || direction.sqrMagnitude < 0.0001f) return;
        _tickInterval = Mathf.Max(0.01f, tuning.FlameManualTickInterval);
        _maximumTargets = Mathf.Max(1, tuning.FlameMaxTargetsPerTick);
        float interval = Mathf.Max(0.01f, tuning.FlameAreaEmissionInterval);
        while (_emissionRemaining < duration)
        {
            Area area = _available.Count > 0 ? _available.Pop() : CreateArea();
            float stopTime = Mathf.Max(0.01f, tuning.FlameAreaTimeToStop);
            area.Root.transform.position = origin;
            area.Velocity = direction.normalized * (2f * Mathf.Max(0f, range) / stopTime);
            area.VisualDirection = direction.normalized;
            area.Deceleration = area.Velocity.magnitude / stopTime;
            area.Age = -_emissionRemaining;
            area.PathCount = 1;
            area.PathPoints[0] = origin;
            area.PathAges[0] = 0f;
            area.Lifetime = Mathf.Max(0.01f, tuning.FlameAreaLifetime);
            area.StartRadius = Mathf.Max(0.01f, tuning.FlameAreaInitialRadius) * Mathf.Max(0.01f, size);
            area.EndRadius = Mathf.Max(0.01f, Mathf.Max(tuning.FlameAreaInitialRadius, tuning.FlameAreaFinalRadius)) * Mathf.Max(0.01f, size);
            area.Radius = area.StartRadius;
            SetPalette(area, style, heat);
            area.Seed = ++_emitted;
            area.Root.SetActive(true);
            UpdateVisual(area);
            _active.Add(area);
            _emissionRemaining += interval;
        }
        _emissionRemaining -= duration;
        RefreshPlume();
    }

    public void StopEmission() => _emissionRemaining = 0f;

    private void LateUpdate()
    {
        if (_owner == null)
        {
            Clear();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return;
        }
        if (!_owner.gameObject.activeInHierarchy || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
        {
            Clear();
            return;
        }
        if (GameplayPause.IsUiPaused || Time.timeScale <= 0f) return;
        Simulate(Time.deltaTime);
    }

    /// <summary>Same deterministic simulation entry used by LateUpdate and regression tests.</summary>
    public void Simulate(float deltaTime)
    {
        if (deltaTime <= 0f || GameplayPause.IsUiPaused || Time.timeScale <= 0f) return;
        _visualTime += deltaTime;
        float remaining = deltaTime;
        while (remaining > 0.000001f && _active.Count > 0)
        {
            if (_tickRemaining <= 0.000001f)
            {
                DamageTick();
                _tickRemaining = Mathf.Max(0.01f, _tickInterval);
            }
            float step = Mathf.Min(remaining, Mathf.Min(_tickRemaining, 0.02f));
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Area area = _active[i];
                float elapsed = Mathf.Min(step + Mathf.Min(0f, area.Age), area.Lifetime - Mathf.Max(0f, area.Age));
                area.Age += step;
                if (elapsed > 0f) Move(area, elapsed);
                if (area.Age >= area.Lifetime)
                {
                    area.Root.SetActive(false);
                    _available.Push(area);
                    _active.RemoveAt(i);
                }
                else UpdateVisual(area);
            }
            remaining -= step;
            _tickRemaining -= step;
        }
        if (_active.Count == 0)
        {
            // Empty gaps must not let a short-lived cloud restart the damage cooldown.
            _tickRemaining = Mathf.Max(0f, _tickRemaining - remaining);
            ClearHitRecords();
        }
        else if (_tickRemaining <= 0.000001f)
        {
            DamageTick();
            _tickRemaining = Mathf.Max(0.01f, _tickInterval);
        }
        RefreshPlume();
    }

    private void Move(Area area, float deltaTime)
    {
        float speed = area.Velocity.magnitude;
        float movingTime = area.Deceleration > 0f ? Mathf.Min(deltaTime, speed / area.Deceleration) : deltaTime;
        Vector3 direction = speed > 0f ? area.Velocity / speed : Vector3.zero;
        Vector3 displacement = direction * (speed * movingTime - 0.5f * area.Deceleration * movingTime * movingTime);
        area.Velocity = direction * Mathf.Max(0f, speed - area.Deceleration * deltaTime);
        Vector3 position = area.Root.transform.position;
        // Sweep a small fixed core. The growing damage radius must never catch the edge of a tree.
        for (int contact = 0; contact < 4 && displacement.sqrMagnitude > 0.0000001f; contact++)
        {
            if (!TryWorldHit(position, displacement, out RaycastHit hit, TerrainCoreRadius))
            {
                position += displacement;
                displacement = Vector3.zero;
                break;
            }
            Vector3 normal = hit.normal.normalized;
            Vector3 leftover = displacement.normalized * Mathf.Max(0f, displacement.magnitude - hit.distance);
            // SphereCast hit.point lies on the wall, not on the core trajectory. Preserve the tangent position.
            position += displacement.normalized * hit.distance + normal * Skin;
            displacement = Vector3.ProjectOnPlane(leftover, normal);
            area.Velocity = Vector3.ProjectOnPlane(area.Velocity, normal);
        }
        area.Root.transform.position = position;
        RecordTrail(area, position);
        area.Radius = Mathf.Lerp(area.StartRadius, area.EndRadius, Mathf.Clamp01(area.Age / area.Lifetime));
    }

    // Retain only recent world positions: old fire never follows the player's muzzle.
    private static void RecordTrail(Area area, Vector3 position)
    {
        while (area.PathCount > 1 && area.PathAges[0] < area.Age - 0.24f)
        {
            Array.Copy(area.PathPoints, 1, area.PathPoints, 0, --area.PathCount);
            Array.Copy(area.PathAges, 1, area.PathAges, 0, area.PathCount);
        }
        if ((position - area.PathPoints[area.PathCount - 1]).sqrMagnitude < 0.000001f) return;
        if (area.PathCount == area.PathPoints.Length)
        {
            Array.Copy(area.PathPoints, 1, area.PathPoints, 0, --area.PathCount);
            Array.Copy(area.PathAges, 1, area.PathAges, 0, area.PathCount);
        }
        area.PathPoints[area.PathCount] = position;
        area.PathAges[area.PathCount++] = area.Age;
    }

    private bool TryWorldHit(Vector3 origin, Vector3 delta, out RaycastHit closest, float coreRadius = 0f)
    {
        closest = default;
        float distance = delta.magnitude;
        if (distance < 0.00001f) return false;
        int count;
        while (true)
        {
            count = coreRadius > 0f
                ? Physics.SphereCastNonAlloc(origin, coreRadius, delta / distance, _worldHits, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, delta / distance, _worldHits, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < _worldHits.Length) break;
            Array.Resize(ref _worldHits, _worldHits.Length * 2);
        }
        bool found = false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = _worldHits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform)
                || (_owner != null && collider.transform.IsChildOf(_owner))
                || collider.GetComponentInParent<IDamageable>() != null) continue;
            if (_worldHits[i].distance >= nearest) continue;
            found = true;
            nearest = _worldHits[i].distance;
            closest = _worldHits[i];
        }
        return found;
    }

    private void DamageTick()
    {
        _seen.Clear();
        _targets.Clear();
        foreach (Area area in _active)
        {
            if (area.Age < 0f) continue;
            Vector3 center = area.Root.transform.position;
            int count;
            while (true)
            {
                count = Physics.OverlapSphereNonAlloc(center, area.Radius, _overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide);
                if (count < _overlaps.Length) break;
                Array.Resize(ref _overlaps, _overlaps.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlaps[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy
                    || (_owner != null && collider.transform.IsChildOf(_owner))) continue;
                IDamageable receiver = collider.GetComponentInParent<IDamageable>();
                if (receiver is not Component target || receiver is PlayerHealth || _seen.Contains(receiver)) continue;
                Vector3 point = collider.ClosestPoint(center);
                // Reverse visibility also catches a center embedded by a moving obstacle.
                if (TryWorldHit(center, point - center, out _) || TryWorldHit(point, center - point, out _)) continue;
                _seen.Add(receiver);
                _targets.Add(new Hit { Target = target.transform, Origin = center,
                    Distance = _owner != null ? (point - _owner.position).sqrMagnitude : 0f });
            }
        }
        _targets.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        LastTargetCount = Mathf.Min(_maximumTargets, _targets.Count);
        LastAppliedCount = 0;
        for (int i = 0; i < LastTargetCount; i++)
        {
            Hit hit = _targets[i];
            if (hit.Target != null && hit.Target.gameObject.activeInHierarchy && _applyHit != null && _applyHit(hit.Target, hit.Origin))
                LastAppliedCount++;
        }
    }

    private Area CreateArea()
    {
        var root = new GameObject("Manual flame area");
        root.layer = 2; // Ignore Raycast; enemy overlaps are explicitly queried with all layers.
        root.transform.SetParent(transform, false);
        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        _created++;
        return new Area { Root = root, Trigger = trigger };
    }

    private static void SetPalette(Area area, FlamethrowerStreamStyle style, float heat)
    {
        area.Style = style;
        area.Heat = Mathf.Clamp01(heat);
    }

    private static void UpdateVisual(Area area)
    {
        area.Trigger.enabled = area.Age >= 0f;
        area.Trigger.radius = 1f;
        area.Root.transform.localScale = Vector3.one * area.Radius;
        if (area.Velocity.sqrMagnitude > 0.0001f) area.VisualDirection = area.Velocity.normalized;
    }

    private void RefreshPlume()
    {
        if (_plume == null) return;
        _plume.Begin(_visualTime);
        foreach (Area area in _active)
        {
            if (area.Age < 0f) continue;
            _plume.Add(area.Root.transform.position, area.VisualDirection, area.PathPoints, area.PathCount,
                area.Radius, Mathf.Clamp01(area.Age / area.Lifetime), area.Seed, area.Style, area.Heat);
        }
        _plume.End();
    }

    public void Clear()
    {
        foreach (Area area in _active) { area.Root.SetActive(false); _available.Push(area); }
        _active.Clear();
        _plume?.Clear();
        ClearHitRecords();
        _tickRemaining = 0f; _emissionRemaining = 0f;
        LastTargetCount = 0; LastAppliedCount = 0;
    }

    private void ClearHitRecords()
    {
        _seen.Clear(); _targets.Clear();
        Array.Clear(_overlaps, 0, _overlaps.Length);
        Array.Clear(_worldHits, 0, _worldHits.Length);
    }

    private void OnDisable() => Clear();
    private void OnDestroy()
    {
        if (_ownsMaterial && _material != null) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
    }
}
