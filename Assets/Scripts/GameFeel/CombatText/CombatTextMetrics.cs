public sealed class CombatTextMetrics
{
    public long DamageEventsReceived { get; internal set; }
    public long SumAppliedDamageReceived { get; internal set; }
    public int AggregatesCreated { get; internal set; }
    public int MergedEvents { get; internal set; }
    public int ViewsSpawned { get; internal set; }
    public int SuppressedByMode { get; internal set; }
    public int SuppressedByDistance { get; internal set; }
    public int SuppressedByDensity { get; internal set; }
    public int SuppressedBehindCamera { get; internal set; }
    public int HiddenAggregatesWithoutViews { get; internal set; }
    public int SequenceFallbacks { get; internal set; }
    public int SequenceTimeouts { get; internal set; }
    public int SequenceOverflows { get; internal set; }
    public int RecordCapacityOverflows { get; internal set; }
    public int ReplacedByHigherPriority { get; internal set; }
    public int ActiveAggregates { get; internal set; }
    public int ActiveViews { get; internal set; }
    public int ActiveViewLimit { get; internal set; }
    public int ActiveSequences { get; internal set; }
    public int VisibleBurnTallies { get; internal set; }
    public int VisibleBurnTallyLimit { get; internal set; }
    public int ViewStartsPerFrameLimit { get; internal set; }
    public int MaximumActiveViewsObserved { get; internal set; }
    public int PoolAvailable { get; internal set; }
    public int PoolCapacity { get; internal set; }
    public long LastUpdateManagedAllocationBytes { get; internal set; }
    public long MaximumUpdateManagedAllocationBytesObserved { get; internal set; }
    public float LastUpdateMilliseconds { get; internal set; }
    public float MaximumUpdateMillisecondsObserved { get; internal set; }
    public GameFeelQualityLevel AppliedQuality { get; internal set; }
    public bool AppliedReducedMotion { get; internal set; }
    public bool AppliedReducedShake { get; internal set; }
    public bool AppliedReducedFlash { get; internal set; }
    public CombatTextMode AppliedCombatTextMode { get; internal set; }
    public float AppliedCombatTextScale { get; internal set; } = 1f;
    public bool AppliedCompactFormatting { get; internal set; }
    public CombatTextSuppressionReason LastSuppressionReason { get; internal set; }

    public void Reset()
    {
        DamageEventsReceived = 0;
        SumAppliedDamageReceived = 0;
        AggregatesCreated = 0;
        MergedEvents = 0;
        ViewsSpawned = 0;
        SuppressedByMode = 0;
        SuppressedByDistance = 0;
        SuppressedByDensity = 0;
        SuppressedBehindCamera = 0;
        HiddenAggregatesWithoutViews = 0;
        SequenceFallbacks = 0;
        SequenceTimeouts = 0;
        SequenceOverflows = 0;
        RecordCapacityOverflows = 0;
        ReplacedByHigherPriority = 0;
        ActiveAggregates = 0;
        ActiveViews = 0;
        ActiveViewLimit = 0;
        ActiveSequences = 0;
        VisibleBurnTallies = 0;
        VisibleBurnTallyLimit = 0;
        ViewStartsPerFrameLimit = 0;
        MaximumActiveViewsObserved = 0;
        PoolAvailable = PoolCapacity;
        LastUpdateManagedAllocationBytes = 0;
        MaximumUpdateManagedAllocationBytesObserved = 0;
        LastUpdateMilliseconds = 0f;
        MaximumUpdateMillisecondsObserved = 0f;
        AppliedQuality = GameFeelQualityLevel.High;
        AppliedReducedMotion = false;
        AppliedReducedShake = false;
        AppliedReducedFlash = false;
        AppliedCombatTextMode = CombatTextMode.Full;
        AppliedCombatTextScale = 1f;
        AppliedCompactFormatting = false;
        LastSuppressionReason = CombatTextSuppressionReason.None;
    }
}
