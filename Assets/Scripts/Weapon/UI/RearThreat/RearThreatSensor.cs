using System.Collections.Generic;
using UnityEngine;

public enum RearThreatReferenceFrame
{
    PlayerFacing,
    CameraFacing
}

/// <summary>Read-only enemy sampling. The presenter supplies the update; this component creates no UI.</summary>
[DisallowMultipleComponent]
public sealed class RearThreatSensor : MonoBehaviour
{
    private const int InitialCapacity = 256;
    private const float DirectionEpsilon = 0.000001f;

    [Header("References")]
    [SerializeField] private Transform _player;
    [SerializeField] private RearThreatReferenceFrame _referenceFrame = RearThreatReferenceFrame.PlayerFacing;
    [SerializeField] private Camera _cameraReference;
    [SerializeField] private ThirdPersonCamera _gameplayCamera;

    [Header("Detection")]
    [SerializeField, Min(0.01f)] private float _detectionRadius = 10f;
    [SerializeField, Min(0f)] private float _fullStrengthDistance = 2f;
    [SerializeField, Range(0f, 360f)] private float _rearArcDegrees = 180f;
    [SerializeField, Min(0f)] private float _maxVerticalDifference = 3f;
    [SerializeField, Min(0.01f)] private float _sampleInterval = 0.1f;

    [Header("Stability")]
    [SerializeField, Min(0f)] private float _distanceExitMargin = 0.5f;
    [SerializeField, Range(0f, 45f)] private float _arcExitMargin = 5f;
    [SerializeField, Range(0f, 180f)] private float _clusterAngle = 15f;
    [SerializeField, Range(1, RearThreatSnapshot.MaximumSpikes)] private int _maxSpikes = 5;
    [SerializeField, Min(0f)] private float _replacementDistanceAdvantage = 0.5f;

    private struct EnemyCache
    {
        public EnemyHealth Health;
        public WeaponDummyEnemy Dummy;
        public EnemyRegistryMember Member;
        public bool WasEligible;
        public int PreviousTrackId;
        public uint SeenAtScan;
    }

    private struct Candidate
    {
        public Transform Root;
        public EnemyCache Cache;
        public int InstanceId;
        public Vector3 Direction;
        public float Distance;
        public int GroupIndex;
    }

    private struct Group
    {
        public Vector3 Direction;
        public float Distance;
        public int EnemyCount;
        public int TrackId;
        public bool Selected;
    }

    private readonly List<Transform> _registryScratch = new(InitialCapacity);
    private readonly Dictionary<Transform, EnemyCache> _cache = new(InitialCapacity);
    private readonly List<Transform> _expiredCacheKeys = new(InitialCapacity);
    private readonly List<Candidate> _candidates = new(InitialCapacity);
    private readonly List<Group> _groups = new(InitialCapacity);
    private readonly RearThreatSpike[] _spikes = new RearThreatSpike[RearThreatSnapshot.MaximumSpikes];
    private readonly RearThreatSpike[] _previousSpikes = new RearThreatSpike[RearThreatSnapshot.MaximumSpikes];
    private readonly int[] _selectedGroups = new int[RearThreatSnapshot.MaximumSpikes];

    private RearThreatSnapshot _snapshot;
    private Transform _boundPlayer;
    private PlayerHealth _playerHealth;
    private bool _hadPlayerBinding;
    private Camera _resolvedCamera;
    private ThirdPersonCamera _cameraGameplayPose;
    private float _nextSampleTime = float.NegativeInfinity;
    private float _lastPresentationTime = float.NegativeInfinity;
    private uint _scanNumber;
    private int _nextTrackId = 1;
    private bool _subscribed;

    public Transform Player { get => _player; set => BindPlayer(value); }
    public Camera CameraReference
    {
        get => _cameraReference;
        set { _cameraReference = value; RequestResample(); }
    }
    public ThirdPersonCamera GameplayCamera
    {
        get => _gameplayCamera;
        set { _gameplayCamera = value; RequestResample(); }
    }
    public RearThreatReferenceFrame ReferenceFrame
    {
        get => _referenceFrame;
        set
        {
            if (_referenceFrame == value) return;
            _referenceFrame = value;
            ClearTracking();
        }
    }
    public RearThreatSnapshot Snapshot => _snapshot;
    public float RearArcDegrees => _rearArcDegrees;
    public float ArcExitMargin => _arcExitMargin;

    /// <summary>Reads the orientation without scanning or requiring an initialized runtime snapshot.</summary>
    public Vector3 GetReferenceForward()
    {
        if (_player == null) return Vector3.forward;
        if (!Application.IsPlaying(gameObject) && _referenceFrame == RearThreatReferenceFrame.CameraFacing)
        {
            Transform cameraTransform = _cameraReference != null ? _cameraReference.transform
                : _gameplayCamera != null ? _gameplayCamera.transform : null;
            if (cameraTransform != null) return FlattenForward(cameraTransform.forward);
        }
        return ResolveForward();
    }

    public void BindPlayer(Transform player)
    {
        if (ReferenceEquals(_player, player) && ReferenceEquals(_boundPlayer, player)) return;
        _player = player;
        _boundPlayer = player;
        _hadPlayerBinding = player != null;
        CachePlayerHealth();
        ClearTracking();
    }

    private void OnEnable()
    {
        Subscribe();
        _boundPlayer = _player;
        _hadPlayerBinding |= _player != null;
        CachePlayerHealth();
        ClearTracking();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearTracking();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        ClearTracking();
    }

    private void OnValidate()
    {
        _detectionRadius = Mathf.Max(0.01f, _detectionRadius);
        _fullStrengthDistance = Mathf.Clamp(_fullStrengthDistance, 0f, _detectionRadius - 0.001f);
        _rearArcDegrees = Mathf.Clamp(_rearArcDegrees, 0f, 360f);
        _maxVerticalDifference = Mathf.Max(0f, _maxVerticalDifference);
        _sampleInterval = Mathf.Max(0.01f, _sampleInterval);
        _distanceExitMargin = Mathf.Max(0f, _distanceExitMargin);
        _arcExitMargin = Mathf.Clamp(_arcExitMargin, 0f, 45f);
        _clusterAngle = Mathf.Clamp(_clusterAngle, 0f, 180f);
        _maxSpikes = Mathf.Clamp(_maxSpikes, 1, RearThreatSnapshot.MaximumSpikes);
        _replacementDistanceAdvantage = Mathf.Max(0f, _replacementDistanceAdvantage);
        ClearTracking();
    }

    public void RefreshForPresentation() => RefreshForPresentation(Time.time);

    /// <summary>The explicit clock overload also permits deterministic sampling tests.</summary>
    public void RefreshForPresentation(float currentTime)
    {
        if (!isActiveAndEnabled)
        {
            if (_snapshot.IsValidPlayer || _cache.Count != 0) ClearTracking();
            return;
        }

        Subscribe();
        if (_player == null && _hadPlayerBinding)
        {
            Transform replacement = PlayerMovement.PlayerTransform;
            if (replacement != null && replacement.gameObject.scene == gameObject.scene)
                BindPlayer(replacement);
        }
        if (_boundPlayer != _player) BindPlayer(_player);
        if (!HasValidPlayer())
        {
            ClearTracking();
            return;
        }

        Vector3 forward = ResolveForward();
        if (currentTime < _lastPresentationTime) RequestResample();
        _lastPresentationTime = currentTime;
        if (currentTime >= _nextSampleTime)
        {
            Sample(forward);
            _nextSampleTime = currentTime + Mathf.Max(0.01f, _sampleInterval);
        }
        else if (RemoveInvalidCandidates())
        {
            // Health is checked every presentation frame, including deferred Destroy and pool reuse.
            BuildGroups(forward);
        }
        else
        {
            _snapshot = new RearThreatSnapshot(_spikes, _snapshot.Count, true, forward, _snapshot.OverallUrgency);
        }
    }

    private bool HasValidPlayer()
    {
        return _player != null && _player.gameObject.activeInHierarchy && IsFinite(_player.position)
            && (_playerHealth == null || _playerHealth.IsAlive);
    }

    private void CachePlayerHealth()
    {
        _playerHealth = null;
        if (_player != null) _player.TryGetComponent(out _playerHealth);
    }

    private Vector3 ResolveForward()
    {
        Vector3 forward = _player.forward;
        if (_referenceFrame == RearThreatReferenceFrame.CameraFacing)
        {
            if (_resolvedCamera != _cameraReference)
            {
                _resolvedCamera = _cameraReference;
                _cameraGameplayPose = null;
                if (_resolvedCamera != null)
                    _resolvedCamera.TryGetComponent(out _cameraGameplayPose);
            }
            ThirdPersonCamera gameplayPose = _gameplayCamera != null ? _gameplayCamera : _cameraGameplayPose;
            if (gameplayPose != null) forward = gameplayPose.GameplayForward;
            else if (_cameraReference != null) forward = _cameraReference.transform.forward;
        }
        return FlattenForward(forward);
    }

    private Vector3 FlattenForward(Vector3 forward)
    {
        forward.y = 0f;
        if (!IsFinite(forward) || forward.sqrMagnitude < DirectionEpsilon)
        {
            forward = _player.forward;
            forward.y = 0f;
        }
        return forward.sqrMagnitude >= DirectionEpsilon ? forward.normalized : Vector3.forward;
    }

    private void Sample(Vector3 forward)
    {
        _scanNumber++;
        _candidates.Clear();
        EnemyRegistry.CollectActive(_registryScratch);
        Vector3 origin = _player.position;
        float entryCos = Mathf.Cos(Mathf.Clamp(_rearArcDegrees * 0.5f, 0f, 180f) * Mathf.Deg2Rad);
        float exitCos = Mathf.Cos(Mathf.Clamp(_rearArcDegrees * 0.5f + _arcExitMargin, 0f, 180f) * Mathf.Deg2Rad);

        for (int i = 0; i < _registryScratch.Count; i++)
        {
            Transform root = _registryScratch[i];
            if (root == null || root == _player || root.IsChildOf(_player)) continue;
            _cache.TryGetValue(root, out EnemyCache cache);
            if (cache.Health == null && cache.Dummy == null)
            {
                root.TryGetComponent(out cache.Health);
                if (cache.Health == null) root.TryGetComponent(out cache.Dummy);
            }
            if (cache.Member == null) root.TryGetComponent(out cache.Member);
            cache.SeenAtScan = _scanNumber;

            Vector3 delta = root.position - origin;
            bool wasEligible = cache.WasEligible;
            float radius = _detectionRadius + (wasEligible ? _distanceExitMargin : 0f);
            bool eligible = IsAliveAndActive(root, cache) && IsFinite(delta)
                && Mathf.Abs(delta.y) <= _maxVerticalDifference;
            delta.y = 0f;
            float distanceSqr = delta.sqrMagnitude;
            eligible &= distanceSqr <= radius * radius;
            float distance = Mathf.Sqrt(distanceSqr);
            // At an overlapping root there is no bearing; use the center of the warning arc.
            Vector3 direction = distanceSqr > DirectionEpsilon ? delta / distance : -forward;
            eligible &= Vector3.Dot(-forward, direction) + 0.000001f >= (wasEligible ? exitCos : entryCos);
            cache.WasEligible = eligible;
            _cache[root] = cache;
            if (!eligible) continue;
            _candidates.Add(new Candidate
            {
                Root = root,
                Cache = cache,
                InstanceId = root.GetInstanceID(),
                Direction = direction,
                Distance = distance,
                GroupIndex = -1
            });
        }

        _expiredCacheKeys.Clear();
        foreach (KeyValuePair<Transform, EnemyCache> entry in _cache)
            if (entry.Key == null || entry.Value.SeenAtScan != _scanNumber) _expiredCacheKeys.Add(entry.Key);
        for (int i = 0; i < _expiredCacheKeys.Count; i++) _cache.Remove(_expiredCacheKeys[i]);
        _expiredCacheKeys.Clear();
        _registryScratch.Clear();

        // Insertion sort avoids comparer/delegate allocations and is small for the expected enemy counts.
        for (int i = 1; i < _candidates.Count; i++)
        {
            Candidate candidate = _candidates[i];
            int j = i - 1;
            while (j >= 0 && ComesBefore(candidate, _candidates[j]))
            {
                _candidates[j + 1] = _candidates[j];
                j--;
            }
            _candidates[j + 1] = candidate;
        }
        BuildGroups(forward);
    }

    private static bool ComesBefore(Candidate a, Candidate b)
    {
        return a.Distance < b.Distance || (a.Distance == b.Distance && a.InstanceId < b.InstanceId);
    }

    private static bool IsAliveAndActive(Transform root, EnemyCache cache)
    {
        if (root == null || !root.gameObject.activeInHierarchy || (cache.Member != null && !cache.Member.isActiveAndEnabled))
            return false;
        return cache.Health != null ? cache.Health.CurrentHealth > 0 : cache.Dummy != null && cache.Dummy.CurrentHealth > 0;
    }

    private bool RemoveInvalidCandidates()
    {
        bool changed = false;
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            Candidate candidate = _candidates[i];
            if (IsAliveAndActive(candidate.Root, candidate.Cache)) continue;
            if (candidate.Root != null && _cache.TryGetValue(candidate.Root, out EnemyCache cache))
            {
                cache.WasEligible = false;
                cache.PreviousTrackId = 0;
                _cache[candidate.Root] = cache;
            }
            _candidates.RemoveAt(i);
            changed = true;
        }
        return changed;
    }

    private void BuildGroups(Vector3 forward)
    {
        int previousCount = _snapshot.Count;
        for (int i = 0; i < previousCount; i++) _previousSpikes[i] = _spikes[i];
        _groups.Clear();
        float clusterCos = Mathf.Cos(Mathf.Clamp(_clusterAngle, 0f, 180f) * Mathf.Deg2Rad);
        for (int i = 0; i < _candidates.Count; i++)
        {
            Candidate candidate = _candidates[i];
            candidate.GroupIndex = -1;
            _candidates[i] = candidate;
        }

        for (int i = 0; i < _candidates.Count; i++)
        {
            Candidate seed = _candidates[i];
            if (seed.GroupIndex >= 0) continue;
            int groupIndex = _groups.Count;
            Group group = new() { Direction = seed.Direction, Distance = seed.Distance };
            for (int j = i; j < _candidates.Count; j++)
            {
                Candidate candidate = _candidates[j];
                if (candidate.GroupIndex >= 0 || Vector3.Dot(seed.Direction, candidate.Direction) + 0.000001f < clusterCos)
                    continue;
                candidate.GroupIndex = groupIndex;
                _candidates[j] = candidate;
                group.EnemyCount++;
            }
            _groups.Add(group);
        }

        // Match by surviving members so changing the nearest enemy does not create a new visual track.
        for (int p = 0; p < previousCount; p++)
        {
            int bestGroup = -1;
            int bestOverlap = 0;
            float bestAlignment = -2f;
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups[g].TrackId != 0) continue;
                int overlap = 0;
                for (int c = 0; c < _candidates.Count; c++)
                    if (_candidates[c].GroupIndex == g && _candidates[c].Cache.PreviousTrackId == _previousSpikes[p].TrackId)
                        overlap++;
                float alignment = Vector3.Dot(_groups[g].Direction, _previousSpikes[p].WorldDirection);
                if (overlap > bestOverlap || (overlap > 0 && overlap == bestOverlap && alignment > bestAlignment))
                {
                    bestGroup = g;
                    bestOverlap = overlap;
                    bestAlignment = alignment;
                }
            }
            if (bestGroup < 0) continue;
            Group match = _groups[bestGroup];
            match.TrackId = _previousSpikes[p].TrackId;
            _groups[bestGroup] = match;
        }

        int limit = Mathf.Clamp(_maxSpikes, 1, RearThreatSnapshot.MaximumSpikes);
        int selectedCount = 0;
        // Retain existing groups before considering challengers, ordered deterministically by distance.
        for (int g = 0; g < _groups.Count && selectedCount < limit; g++)
            if (_groups[g].TrackId != 0) SelectGroup(g, ref selectedCount);
        for (int g = 0; g < _groups.Count; g++)
        {
            if (_groups[g].Selected) continue;
            if (selectedCount < limit)
            {
                SelectGroup(g, ref selectedCount);
                continue;
            }
            int farthestSlot = 0;
            for (int s = 1; s < selectedCount; s++)
                if (_groups[_selectedGroups[s]].Distance > _groups[_selectedGroups[farthestSlot]].Distance)
                    farthestSlot = s;
            int displacedIndex = _selectedGroups[farthestSlot];
            if (_groups[g].Distance + _replacementDistanceAdvantage > _groups[displacedIndex].Distance) continue;
            Group displaced = _groups[displacedIndex];
            displaced.Selected = false;
            _groups[displacedIndex] = displaced;
            Group replacement = _groups[g];
            replacement.Selected = true;
            _groups[g] = replacement;
            _selectedGroups[farthestSlot] = g;
        }

        for (int s = 0; s < selectedCount; s++)
        {
            int groupIndex = _selectedGroups[s];
            Group group = _groups[groupIndex];
            if (group.TrackId == 0) group.TrackId = _nextTrackId++;
            _groups[groupIndex] = group;
            float urgency = UrgencyAtDistance(group.Distance);
            _spikes[s] = new RearThreatSpike(group.TrackId, group.Direction, group.Distance, urgency, group.EnemyCount);
        }
        for (int s = selectedCount; s < _spikes.Length; s++) _spikes[s] = default;
        for (int c = 0; c < _candidates.Count; c++)
        {
            Candidate candidate = _candidates[c];
            Group group = _groups[candidate.GroupIndex];
            candidate.Cache.PreviousTrackId = group.Selected ? group.TrackId : 0;
            _candidates[c] = candidate;
            _cache[candidate.Root] = candidate.Cache;
        }
        float overallUrgency = _candidates.Count > 0 ? UrgencyAtDistance(_candidates[0].Distance) : 0f;
        _snapshot = new RearThreatSnapshot(_spikes, selectedCount, true, forward, overallUrgency);
    }

    private float UrgencyAtDistance(float distance)
    {
        float radius = Mathf.Max(0.01f, _detectionRadius);
        float fullStrengthDistance = Mathf.Clamp(_fullStrengthDistance, 0f, radius - 0.001f);
        return Mathf.InverseLerp(radius, fullStrengthDistance, distance);
    }

    private void SelectGroup(int groupIndex, ref int selectedCount)
    {
        Group group = _groups[groupIndex];
        group.Selected = true;
        _groups[groupIndex] = group;
        _selectedGroups[selectedCount++] = groupIndex;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        EnemyRegistry.Unregistered += HandleUnregistered;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        EnemyRegistry.Unregistered -= HandleUnregistered;
        _subscribed = false;
    }

    private void HandleUnregistered(Transform root)
    {
        _cache.Remove(root);
        bool changed = false;
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            if (_candidates[i].Root != root) continue;
            _candidates.RemoveAt(i);
            changed = true;
        }
        if (!changed) return;
        if (isActiveAndEnabled && HasValidPlayer())
        {
            RemoveInvalidCandidates();
            BuildGroups(ResolveForward());
        }
        else ClearTracking();
    }

    private void RequestResample() => _nextSampleTime = float.NegativeInfinity;

    private void ClearTracking()
    {
        _snapshot = default;
        _cache.Clear();
        _registryScratch.Clear();
        _expiredCacheKeys.Clear();
        _candidates.Clear();
        _groups.Clear();
        System.Array.Clear(_spikes, 0, _spikes.Length);
        System.Array.Clear(_previousSpikes, 0, _previousSpikes.Length);
        _lastPresentationTime = float.NegativeInfinity;
        RequestResample();
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
