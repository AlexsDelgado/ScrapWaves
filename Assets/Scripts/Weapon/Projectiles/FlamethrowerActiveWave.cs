using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One world-fixed Q front. Presentation retains the plain clock after the gameplay object is released.</summary>
[DefaultExecutionOrder(-500)]
public sealed class FlamethrowerActiveWave : MonoBehaviour
{
    public sealed class WaveClock
    {
        public Vector3 Origin { get; internal set; }
        public float FinalRadius { get; internal set; }
        public float ExpansionDuration { get; internal set; }
        public float VisualDuration { get; internal set; }
        public float Elapsed { get; internal set; }
        public bool Cancelled { get; internal set; }
        public float Radius => FinalRadius * Mathf.Clamp01(Elapsed / ExpansionDuration);
        public bool Complete => Cancelled || Elapsed >= VisualDuration;
    }

    static readonly Dictionary<WeaponInstance, WaveClock> Clocks = new();
    readonly List<Transform> _roots = new(256);
    readonly List<Transform> _staleRoots = new(32);
    readonly Dictionary<Transform, Vector2> _previous = new(256);
    readonly HashSet<IDamageable> _hit = new();
    readonly List<Candidate> _candidates = new(64);
    Transform _owner;
    WeaponInstance _weapon;
    Action<Transform> _applyHit;
    Action _activateFootprint;
    float _footprintRadius;
    int _maximumTargets;
    bool _tracking;

    struct Candidate
    {
        public Transform Root;
        public IDamageable Receiver;
        public float Arrival;
    }

    public WaveClock Clock { get; private set; }
    public int HitCount { get; private set; }
    public bool FootprintActivated { get; private set; }
    public static WaveClock FindClock(WeaponInstance weapon) => weapon != null && Clocks.TryGetValue(weapon, out var clock) ? clock : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetClocks() => Clocks.Clear();

    public void Initialize(Transform owner, WeaponInstance weapon, Vector3 origin, float radius,
        float expansionDuration, float visualDuration, int maximumTargets, Action<Transform> applyHit,
        float footprintRadius = 0f, Action activateFootprint = null)
    {
        _owner = owner;
        _weapon = weapon;
        _applyHit = applyHit;
        _maximumTargets = Mathf.Max(1, maximumTargets);
        _footprintRadius = Mathf.Clamp(footprintRadius, 0f, Mathf.Max(0f, radius));
        _activateFootprint = activateFootprint;
        Clock = new WaveClock { Origin = origin, FinalRadius = Mathf.Max(0f, radius),
            ExpansionDuration = Mathf.Max(.01f, expansionDuration),
            VisualDuration = Mathf.Max(expansionDuration + .2f, visualDuration) };
        transform.SetPositionAndRotation(origin, Quaternion.identity);
        if (weapon != null) Clocks[weapon] = Clock;
        EnemyRegistry.CollectActive(_roots);
        foreach (var root in _roots) if (IsEligible(root)) _previous[root] = Planar(root.position - origin);
        EnemyRegistry.Unregistered += OnUnregistered;
        _tracking = true;
    }

    void Update()
    {
        Simulate(Time.deltaTime);
        if (Clock != null && Clock.Complete) Destroy(gameObject);
    }

    public void Simulate(float deltaTime)
    {
        if (Clock == null || Clock.Complete) return;
        // Lifecycle cleanup must also work during hitstop or a paused end-of-run screen.
        if (_owner == null || !_owner.gameObject.activeInHierarchy || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
        {
            Cancel(); return;
        }
        if (deltaTime <= 0f || GameplayPause.IsUiPaused || Time.timeScale <= 0f) return;
        float previousTime = Clock.Elapsed;
        float previousRadius = Clock.Radius;
        Clock.Elapsed = Mathf.Min(Clock.VisualDuration, Clock.Elapsed + deltaTime);
        if (previousTime < Clock.ExpansionDuration && Clock.FinalRadius > 0f)
        {
            float radius = Clock.Radius;
            // Only the fraction before the end of expansion belongs to the damaging sweep.
            float movementFraction = Mathf.Clamp01((Clock.ExpansionDuration - previousTime) / deltaTime);
            EnemyRegistry.CollectActive(_roots);
            _staleRoots.Clear();
            foreach (var root in _previous.Keys) if (!IsEligible(root)) _staleRoots.Add(root);
            foreach (var root in _staleRoots) _previous.Remove(root);
            _candidates.Clear();
            foreach (var root in _roots)
            {
                if (!IsEligible(root)) continue;
                Vector2 current = Planar(root.position - Clock.Origin);
                bool tracked = _previous.TryGetValue(root, out Vector2 previous);
                if (!tracked) previous = current; // New/pooled roots cannot sweep from a stale position.
                _previous[root] = current;
                IDamageable receiver = root.GetComponentInParent<IDamageable>();
                if (receiver == null || receiver is PlayerHealth || _hit.Contains(receiver)) continue;
                Vector2 end = tracked ? Vector2.Lerp(previous, current, movementFraction) : current;
                if (TryCrossing(previous, end, previousRadius, radius, out float arrival))
                    _candidates.Add(new Candidate { Root = root, Receiver = receiver, Arrival = arrival });
            }
            _candidates.Sort((a,b) => a.Arrival.CompareTo(b.Arrival));
            foreach (var candidate in _candidates)
            {
                if (HitCount >= _maximumTargets) break;
                if (!IsEligible(candidate.Root) || !_hit.Add(candidate.Receiver)) continue;
                HitCount++;
                _applyHit?.Invoke(candidate.Root);
            }
            if (!FootprintActivated && _activateFootprint != null && radius >= _footprintRadius)
            {
                FootprintActivated = true;
                _activateFootprint();
            }
            if (Clock.Elapsed >= Clock.ExpansionDuration) StopTracking();
        }
    }

    static bool IsEligible(Transform root) => root != null && root.gameObject.activeInHierarchy && !EnemyVerticalEngagement.IsDisengaged(root);
    static Vector2 Planar(Vector3 point) => new(point.x, point.z);

    // Solve |p0 + displacement*s| = r0 + radiusChange*s, including fast targets that cross and exit in one frame.
    static bool TryCrossing(Vector2 from, Vector2 to, float r0, float r1, out float arrival)
    {
        Vector2 delta = to - from;
        double dr = r1 - r0;
        double a = Vector2.Dot(delta, delta) - dr * dr;
        double b = 2.0 * (Vector2.Dot(from, delta) - r0 * dr);
        double c = Vector2.Dot(from, from) - (double)r0 * r0;
        arrival = 0f;
        if (Math.Abs(c) <= .000001) return true;
        if (Math.Abs(a) <= .00000001)
        {
            if (Math.Abs(b) <= .00000001) return false;
            double root = -c / b;
            arrival = (float)root;
            return root >= 0.0 && root <= 1.000001;
        }
        double discriminant = b * b - 4.0 * a * c;
        if (discriminant < 0.0) return false;
        double squareRoot = Math.Sqrt(discriminant);
        double first = (-b - squareRoot) / (2.0 * a), second = (-b + squareRoot) / (2.0 * a);
        double closest = double.PositiveInfinity;
        if (first >= 0.0 && first <= 1.000001) closest = first;
        if (second >= 0.0 && second <= 1.000001) closest = Math.Min(closest, second);
        if (double.IsPositiveInfinity(closest)) return false;
        arrival = (float)closest;
        return true;
    }

    void OnUnregistered(Transform root) => _previous.Remove(root);
    void StopTracking()
    {
        if (_tracking) EnemyRegistry.Unregistered -= OnUnregistered;
        _tracking = false;
        _roots.Clear(); _staleRoots.Clear(); _previous.Clear(); _candidates.Clear();
    }
    public void Cancel()
    {
        if (Clock != null) { Clock.Cancelled = true; Clock.Elapsed = Clock.VisualDuration; }
        StopTracking(); _applyHit = null; _activateFootprint = null;
    }
    void OnDisable() => Cancel();
    void OnDestroy()
    {
        Cancel();
        if (_weapon != null && Clocks.TryGetValue(_weapon, out var clock) && ReferenceEquals(clock, Clock)) Clocks.Remove(_weapon);
    }
}
