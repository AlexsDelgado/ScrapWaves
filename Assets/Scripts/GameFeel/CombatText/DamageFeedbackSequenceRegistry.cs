using UnityEngine;

public interface IDamageFeedbackSequenceRegistry
{
    int BeginSequence(DamageFeedbackKind kind, int expectedContributors = 1);
    void AddContributor(int sequenceId);
    void TouchSequence(int sequenceId);
    void CompleteContributor(int sequenceId);
    void CompleteSequence(int sequenceId);
    bool IsComplete(int sequenceId);
}

/// <summary>
/// Fixed-capacity action sequence registry. Slots are reusable value records and no
/// sequence creates a managed object or grows a collection.
/// </summary>
public sealed class DamageFeedbackSequenceRegistry : IDamageFeedbackSequenceRegistry
{
    private struct SequenceRecord
    {
        public bool Active;
        public bool RegistrationClosed;
        public bool Complete;
        public int Id;
        public DamageFeedbackKind Kind;
        public int RemainingContributors;
        public float LastActivityTime;
        public float CompletedTime;
    }

    private readonly SequenceRecord[] _records;
    private readonly float _orphanTimeout;
    private readonly float _completedRetention;
    private int _nextId = 1;
    private int _activeCount;
    private int _timeoutCount;
    private int _overflowCount;

    public DamageFeedbackSequenceRegistry(
        int capacity = 64,
        float orphanTimeout = 1.25f,
        float completedRetention = 1.5f)
    {
        _records = new SequenceRecord[Mathf.Max(1, capacity)];
        _orphanTimeout = Mathf.Max(0.05f, orphanTimeout);
        _completedRetention = Mathf.Max(0.05f, completedRetention);
    }

    public int Capacity => _records.Length;
    public int ActiveCount => _activeCount;
    public int TimeoutCount => _timeoutCount;
    public int OverflowCount => _overflowCount;

    public int BeginSequence(DamageFeedbackKind kind, int expectedContributors = 1)
    {
        return BeginSequence(kind, expectedContributors, Time.unscaledTime);
    }

    public int BeginSequence(DamageFeedbackKind kind, int expectedContributors, float now)
    {
        int slotIndex = FindFreeSlot();
        if (slotIndex < 0)
        {
            _overflowCount++;
            return 0;
        }

        int id = GetNextAvailableId();
        if (id == 0)
        {
            _overflowCount++;
            return 0;
        }

        _records[slotIndex] = new SequenceRecord
        {
            Active = true,
            RegistrationClosed = false,
            Complete = false,
            Id = id,
            Kind = kind,
            RemainingContributors = Mathf.Max(0, expectedContributors),
            LastActivityTime = now,
            CompletedTime = 0f
        };
        _activeCount++;
        return id;
    }

    public void AddContributor(int sequenceId)
    {
        AddContributor(sequenceId, Time.unscaledTime);
    }

    public void AddContributor(int sequenceId, float now)
    {
        int index = Find(sequenceId);
        if (index < 0 || _records[index].Complete || _records[index].RegistrationClosed)
            return;

        ref SequenceRecord record = ref _records[index];
        if (record.RemainingContributors < int.MaxValue)
            record.RemainingContributors++;
        record.LastActivityTime = now;
    }

    /// <summary>
    /// Records authoritative producer activity without changing contributor counts.
    /// Long-flight and delayed-repeat producers use this to keep the orphan timeout
    /// as a safety fallback rather than their normal completion mechanism.
    /// </summary>
    public void TouchSequence(int sequenceId)
    {
        TouchSequence(sequenceId, Time.unscaledTime);
    }

    public void TouchSequence(int sequenceId, float now)
    {
        int index = Find(sequenceId);
        if (index < 0 || _records[index].Complete)
            return;

        _records[index].LastActivityTime = now;
    }

    public void CompleteContributor(int sequenceId)
    {
        CompleteContributor(sequenceId, Time.unscaledTime);
    }

    public void CompleteContributor(int sequenceId, float now)
    {
        int index = Find(sequenceId);
        if (index < 0 || _records[index].Complete)
            return;

        ref SequenceRecord record = ref _records[index];
        if (record.RemainingContributors > 0)
            record.RemainingContributors--;
        record.LastActivityTime = now;
        TryFinish(ref record, now);
    }

    /// <summary>
    /// Closes contributor registration. Already registered contributors may continue
    /// completing; the sequence becomes complete only when all have completed.
    /// </summary>
    public void CompleteSequence(int sequenceId)
    {
        CompleteSequence(sequenceId, Time.unscaledTime);
    }

    public void CompleteSequence(int sequenceId, float now)
    {
        int index = Find(sequenceId);
        if (index < 0 || _records[index].Complete)
            return;

        ref SequenceRecord record = ref _records[index];
        record.RegistrationClosed = true;
        record.LastActivityTime = now;
        TryFinish(ref record, now);
    }

    public bool IsComplete(int sequenceId)
    {
        int index = Find(sequenceId);
        return index >= 0 && _records[index].Complete;
    }

    public bool TryGetState(
        int sequenceId,
        out DamageFeedbackKind kind,
        out int remainingContributors,
        out bool registrationClosed,
        out bool complete)
    {
        int index = Find(sequenceId);
        if (index < 0)
        {
            kind = default;
            remainingContributors = 0;
            registrationClosed = false;
            complete = false;
            return false;
        }

        SequenceRecord record = _records[index];
        kind = record.Kind;
        remainingContributors = record.RemainingContributors;
        registrationClosed = record.RegistrationClosed;
        complete = record.Complete;
        return true;
    }

    public void Tick(float now)
    {
        for (int i = 0; i < _records.Length; i++)
        {
            ref SequenceRecord record = ref _records[i];
            if (!record.Active)
                continue;

            if (!record.Complete && now - record.LastActivityTime >= _orphanTimeout)
            {
                record.RegistrationClosed = true;
                record.RemainingContributors = 0;
                record.Complete = true;
                record.CompletedTime = now;
                _timeoutCount++;
            }

            if (record.Complete && now - record.CompletedTime >= _completedRetention)
                Release(i);
        }
    }

    public void Reset()
    {
        for (int i = 0; i < _records.Length; i++)
            _records[i] = default;
        _activeCount = 0;
        _timeoutCount = 0;
        _overflowCount = 0;
        _nextId = 1;
    }

    private static void TryFinish(ref SequenceRecord record, float now)
    {
        if (!record.RegistrationClosed || record.RemainingContributors > 0)
            return;
        record.Complete = true;
        record.CompletedTime = now;
    }

    private int Find(int sequenceId)
    {
        if (sequenceId <= 0)
            return -1;
        for (int i = 0; i < _records.Length; i++)
        {
            if (_records[i].Active && _records[i].Id == sequenceId)
                return i;
        }
        return -1;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _records.Length; i++)
        {
            if (!_records[i].Active)
                return i;
        }
        return -1;
    }

    private int GetNextAvailableId()
    {
        for (int attempt = 0; attempt <= _records.Length; attempt++)
        {
            int candidate = _nextId;
            _nextId = _nextId == int.MaxValue ? 1 : _nextId + 1;
            if (candidate > 0 && Find(candidate) < 0)
                return candidate;
        }
        return 0;
    }

    private void Release(int index)
    {
        if (!_records[index].Active)
            return;
        _records[index] = default;
        _activeCount = Mathf.Max(0, _activeCount - 1);
    }
}

/// <summary>
/// Process-wide facade used by weapons and projectile descendants. Storage remains
/// bounded and may be reconfigured during initialization or tests only.
/// </summary>
public static class DamageFeedbackSequenceRuntime
{
    private const int DefaultCapacity = 64;
    private const float DefaultTimeout = 1.25f;
    private static DamageFeedbackSequenceRegistry s_registry =
        new(DefaultCapacity, DefaultTimeout);

    // Presentation owners may be rebuilt while projectiles are still in flight, so
    // their teardown must not reset this producer-owned registry. Subsystem
    // registration is the safe boundary that also runs when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBeforeRuntimeLoad()
    {
        s_registry = new DamageFeedbackSequenceRegistry(DefaultCapacity, DefaultTimeout);
    }

    public static int ActiveCount => s_registry.ActiveCount;
    public static int Capacity => s_registry.Capacity;
    public static int TimeoutCount => s_registry.TimeoutCount;
    public static int OverflowCount => s_registry.OverflowCount;

    public static int BeginSequence(DamageFeedbackKind kind, int expectedContributors = 1)
    {
        return s_registry.BeginSequence(kind, expectedContributors);
    }

    public static void AddContributor(int sequenceId) => s_registry.AddContributor(sequenceId);
    public static void TouchSequence(int sequenceId) => s_registry.TouchSequence(sequenceId);
    public static void CompleteContributor(int sequenceId) => s_registry.CompleteContributor(sequenceId);
    public static void CompleteSequence(int sequenceId) => s_registry.CompleteSequence(sequenceId);
    public static bool IsComplete(int sequenceId) => s_registry.IsComplete(sequenceId);
    public static void Tick(float now) => s_registry.Tick(now);
    public static void Reset() => s_registry.Reset();

    public static void Configure(int capacity, float orphanTimeout)
    {
        s_registry = new DamageFeedbackSequenceRegistry(capacity, orphanTimeout);
    }

    public static void EnsureCapacity(int capacity, float orphanTimeout)
    {
        int sanitizedCapacity = Mathf.Max(1, capacity);
        if (s_registry.ActiveCount == 0 && s_registry.Capacity != sanitizedCapacity)
            s_registry = new DamageFeedbackSequenceRegistry(sanitizedCapacity, orphanTimeout);
    }

    internal static DamageFeedbackSequenceRegistry Registry => s_registry;
}
