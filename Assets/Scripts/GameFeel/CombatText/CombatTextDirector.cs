using UnityEngine;

/// <summary>
/// One scene-level owner for combat-text aggregation, visibility, pooling and motion.
/// Gameplay emits immutable feedback; this class never queries or changes health.
/// </summary>
public sealed class CombatTextDirector
{
    private struct AggregateSlot
    {
        public bool Active;
        public bool WasHidden;
        public CombatTextAggregate Aggregate;
        public CombatTextView View;
        public CombatTextPriority Priority;
        public float DistanceScale;
        public int Lane;
    }

    private readonly CombatTextProfile _profile;
    private readonly GameFeelRuntimeOptions _options;
    private readonly AggregateSlot[] _slots;
    private readonly bool[] _laneOccupied = new bool[CombatTextProfile.LaneCount];
    private readonly GameObject _worldRootObject;
    private readonly Transform _viewRoot;
    private readonly CombatTextPool _pool;
    private PresentationAccessibilityState _accessibility;
    private Camera _camera;
    private int _activeAggregateCount;
    private int _visibleBurnCount;
    private int _startsThisFrame;
    private bool _mirrorRuntimeOptions;
    private bool? _compactLargeNumbersOverride;
    private bool _disposed;

    public CombatTextDirector(
        Transform runtimeRoot,
        Camera camera,
        CombatTextProfile profile,
        GameFeelRuntimeOptions options)
        : this(
            runtimeRoot,
            camera,
            profile,
            options,
            options != null
                ? new PresentationAccessibilityState(
                    options.ReducedMotion,
                    options.ReducedShake,
                    options.ReducedFlash,
                    options.CombatText,
                    options.CombatTextScale)
                : PresentationAccessibilityRuntime.Current)
    {
        _mirrorRuntimeOptions = options != null;
    }

    public CombatTextDirector(
        Transform runtimeRoot,
        Camera camera,
        CombatTextProfile profile,
        GameFeelRuntimeOptions options,
        PresentationAccessibilityState accessibility)
    {
        _profile = CombatTextProfile.Resolve(profile);
        _options = options;
        _camera = camera;
        _accessibility = accessibility;
        _slots = new AggregateSlot[_profile.AggregateCapacity];

        _worldRootObject = new GameObject("CombatText World");
        if (runtimeRoot != null && runtimeRoot.gameObject.scene.IsValid() &&
            _worldRootObject.scene != runtimeRoot.gameObject.scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                _worldRootObject,
                runtimeRoot.gameObject.scene);
        }

        GameObject viewRootObject = new("Views");
        _viewRoot = viewRootObject.transform;
        _viewRoot.SetParent(_worldRootObject.transform, false);
        _worldRootObject.AddComponent<CombatTextWorldRenderDriver>().Initialize(this);

        GameFeelQualityLevel quality = CurrentQuality;
        _pool = new CombatTextPool(_viewRoot, _profile, _profile.GetPrewarmCount(quality));
        Metrics = new CombatTextMetrics { PoolCapacity = _pool.Capacity };
        UpdateLiveMetrics();
        DamageFeedbackSequenceRuntime.EnsureCapacity(_profile.SequenceCapacity, _profile.SequenceOrphanTimeout);
    }

    public Transform WorldRoot => _worldRootObject != null ? _worldRootObject.transform : null;
    public CombatTextMetrics Metrics { get; }
    public int ActiveAggregateCount => _activeAggregateCount;
    public int ActiveViewCount => _pool.ActiveCount;
    public bool CompactLargeNumbers => _compactLargeNumbersOverride ?? _profile.CompactLargeNumbers;

    public bool TryEmit(in WeaponFeedbackContext context, float now)
    {
        if (_disposed)
            return false;

        SyncAccessibilityFromOptions();
        if (context.DamageAmount > 0)
        {
            Metrics.DamageEventsReceived++;
            Metrics.SumAppliedDamageReceived = SaturatingAdd(
                Metrics.SumAppliedDamageReceived,
                context.DamageAmount);
        }

        if (!CombatTextEvent.TryFromFeedback(in context, out CombatTextEvent combatTextEvent))
        {
            RecordSuppression(CombatTextSuppressionReason.Invalid, -1);
            return false;
        }

        int slotIndex = FindMergeCandidate(in combatTextEvent, now);
        bool merged = slotIndex >= 0;
        if (!merged)
        {
            slotIndex = FindFreeSlot();
            if (slotIndex < 0)
            {
                Metrics.RecordCapacityOverflows++;
                RecordSuppression(CombatTextSuppressionReason.RecordCapacity, -1);
                return false;
            }

            ref AggregateSlot newSlot = ref _slots[slotIndex];
            newSlot = new AggregateSlot
            {
                Active = true,
                Aggregate = new CombatTextAggregate(in combatTextEvent, now),
                DistanceScale = 1f,
                Lane = -1
            };
            _activeAggregateCount++;
            Metrics.AggregatesCreated++;
            if (combatTextEvent.ActionSequenceId == 0)
                Metrics.SequenceFallbacks++;
        }
        else
        {
            ref AggregateSlot mergeSlot = ref _slots[slotIndex];
            if (!mergeSlot.Aggregate.TryMerge(in combatTextEvent, now))
                return false;
            Metrics.MergedEvents++;
        }

        ref AggregateSlot slot = ref _slots[slotIndex];
        slot.Priority = CombatTextStyleResolver.ResolvePriority(in slot.Aggregate, _profile);
        if (slot.Aggregate.IsKill)
            slot.Aggregate.MarkClosed(now);

        if (slot.View != null)
        {
            MergeVisibleView(ref slot);
            UpdateLiveMetrics();
            return true;
        }

        bool shown = TryStartView(slotIndex, now);
        UpdateLiveMetrics();
        return shown;
    }

    public void NotifySequenceCompleted(int actionSequenceId, float now)
    {
        if (actionSequenceId <= 0)
            return;
        DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            if (slot.Active && slot.Aggregate.Key.ActionSequenceId == actionSequenceId)
                slot.Aggregate.MarkClosed(now);
        }
    }

    public void NotifyStatusSegmentClosed(
        Transform target,
        WeaponStatusKind statusKind,
        int statusInstanceId,
        int segmentIndex,
        float now)
    {
        if (statusInstanceId <= 0)
            return;

        int targetInstanceId = target != null ? target.GetInstanceID() : 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            CombatTextAggregationKey key = slot.Aggregate.Key;
            if (!slot.Active || !slot.Aggregate.IsBurnFamily ||
                (targetInstanceId != 0 && key.TargetInstanceId != targetInstanceId) ||
                key.StatusInstanceId != statusInstanceId ||
                key.StatusKind != statusKind ||
                key.SegmentIndex != segmentIndex)
            {
                continue;
            }

            slot.Aggregate.MarkClosed(now);
            slot.View?.BeginRelease();
        }
    }

    public void Tick(float now, float unscaledDeltaTime)
    {
        if (_disposed)
            return;
        long allocationBefore = System.GC.GetAllocatedBytesForCurrentThread();
        long timestampBefore = System.Diagnostics.Stopwatch.GetTimestamp();
        SyncAccessibilityFromOptions();
        DamageFeedbackSequenceRuntime.Tick(now);

        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            if (!slot.Active)
                continue;

            UpdateClosure(ref slot, now);
            if (slot.View != null)
            {
                if (slot.Aggregate.IsBurnFamily && slot.Aggregate.IsClosed)
                    slot.View.BeginRelease();
                else
                    UpdateViewAnchor(ref slot, now);
                if (slot.View.Tick(unscaledDeltaTime, _camera))
                    ReleaseView(ref slot);
            }

            if (slot.View == null && ShouldReleaseRecord(in slot, now))
                ClearSlot(i);
        }

        _startsThisFrame = 0;
        UpdateLiveMetrics();
        long timestampAfter = System.Diagnostics.Stopwatch.GetTimestamp();
        long allocationAfter = System.GC.GetAllocatedBytesForCurrentThread();
        long allocationDelta = System.Math.Max(0L, allocationAfter - allocationBefore);
        float elapsedMilliseconds = (float)((timestampAfter - timestampBefore) * 1000d /
            System.Diagnostics.Stopwatch.Frequency);
        Metrics.LastUpdateManagedAllocationBytes = allocationDelta;
        Metrics.MaximumUpdateManagedAllocationBytesObserved = System.Math.Max(
            Metrics.MaximumUpdateManagedAllocationBytesObserved,
            allocationDelta);
        Metrics.LastUpdateMilliseconds = elapsedMilliseconds;
        Metrics.MaximumUpdateMillisecondsObserved = Mathf.Max(
            Metrics.MaximumUpdateMillisecondsObserved,
            elapsedMilliseconds);
    }

    public void StopAll()
    {
        if (_disposed)
            return;
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = default;
        _pool.ReleaseAll();
        _activeAggregateCount = 0;
        _visibleBurnCount = 0;
        _startsThisFrame = 0;
        Metrics.Reset();
        UpdateLiveMetrics();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAll();
        _disposed = true;
        if (_worldRootObject == null)
            return;

        _worldRootObject.SetActive(false);
        if (Application.isPlaying)
            Object.Destroy(_worldRootObject);
        else
            Object.DestroyImmediate(_worldRootObject);
    }

    public void SetCamera(Camera camera) => _camera = camera;

    public void RefreshRenderPoses()
    {
        if (_disposed)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            CombatTextView view = _slots[i].View;
            if (view != null)
                view.ApplyRenderPose(_camera);
        }
    }

    public void ResetMetrics()
    {
        Metrics.Reset();
        UpdateLiveMetrics();
    }

    public void SetCompactLargeNumbersOverride(bool? compact)
    {
        _compactLargeNumbersOverride = compact;
        UpdateLiveMetrics();
    }

    public void ApplyAccessibility(PresentationAccessibilityState accessibility)
    {
        _mirrorRuntimeOptions = false;
        ApplyAccessibilityInternal(accessibility);
    }

    private int FindMergeCandidate(in CombatTextEvent combatTextEvent, float now)
    {
        CombatTextAggregationKey key = CombatTextAggregationKey.FromEvent(in combatTextEvent);
        float fallbackWindow = _profile.GetFallbackWindow(
            combatTextEvent.DamageKind,
            combatTextEvent.Mode,
            combatTextEvent.WeaponType);
        float maximumLifetime = _profile.GetMaximumSegmentLifetime(combatTextEvent.DamageKind);
        int best = -1;
        float latest = float.MinValue;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            if (!slot.Active || slot.Aggregate.Key != key ||
                !slot.Aggregate.CanMerge(in combatTextEvent, now, fallbackWindow, maximumLifetime))
            {
                continue;
            }
            if (slot.Aggregate.LastEventTime >= latest)
            {
                best = i;
                latest = slot.Aggregate.LastEventTime;
            }
        }
        return best;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_slots[i].Active)
                return i;
        }
        return -1;
    }

    private bool TryStartView(int slotIndex, float now)
    {
        ref AggregateSlot slot = ref _slots[slotIndex];
        if (!_profile.Enabled || _accessibility.CombatText == CombatTextMode.Off)
        {
            RecordSuppression(_profile.Enabled ? CombatTextSuppressionReason.Mode : CombatTextSuppressionReason.Disabled, slotIndex);
            return false;
        }
        if (!CombatTextVisibilityPolicy.AllowsMode(_accessibility.CombatText, slot.Priority))
        {
            RecordSuppression(CombatTextSuppressionReason.Mode, slotIndex);
            return false;
        }

        Vector3 worldPosition = ResolveWorldPosition(in slot.Aggregate);
        if (!CombatTextVisibilityPolicy.TryEvaluateWorld(
                _camera,
                worldPosition,
                slot.Priority,
                _profile,
                out float distanceScale,
                out CombatTextSuppressionReason projectionReason))
        {
            RecordSuppression(projectionReason, slotIndex);
            return false;
        }

        GameFeelQualityLevel quality = CurrentQuality;
        int startLimit = _profile.GetStartLimit(quality);
        if (slot.Priority < CombatTextPriority.EliteBoss && startLimit > 1 &&
            _startsThisFrame >= startLimit - 1)
        {
            // Keep one bounded start available for critical/weak-point/kill events.
            RecordSuppression(CombatTextSuppressionReason.FrameStartBudget, slotIndex);
            return false;
        }
        CombatTextVisibilityDecision density = CombatTextVisibilityPolicy.EvaluateDensity(
            slot.Aggregate.IsBurnFamily,
            _pool.ActiveCount,
            _visibleBurnCount,
            _startsThisFrame,
            quality,
            _profile);
        if (!density.Visible &&
            (density.Reason == CombatTextSuppressionReason.Density ||
             density.Reason == CombatTextSuppressionReason.BurnDensity) &&
            TryReclaimLowerPriority(slot.Priority))
        {
            density = CombatTextVisibilityPolicy.EvaluateDensity(
                slot.Aggregate.IsBurnFamily,
                _pool.ActiveCount,
                _visibleBurnCount,
                _startsThisFrame,
                quality,
                _profile);
        }
        if (!density.Visible)
        {
            RecordSuppression(density.Reason, slotIndex);
            return false;
        }
        if (!_pool.TryAcquire(out CombatTextView view) &&
            (!TryReclaimLowerPriority(slot.Priority) || !_pool.TryAcquire(out view)))
        {
            RecordSuppression(CombatTextSuppressionReason.PoolExhausted, slotIndex);
            return false;
        }

        int lane = AssignLane(slot.Aggregate.Key.GetHashCode(), slot.Priority);
        worldPosition = ApplySpatialOffset(worldPosition, lane);
        slot.Lane = lane;
        slot.DistanceScale = distanceScale;
        slot.View = view;

        CombatTextStyleId style = CombatTextStyleResolver.ResolveStyle(in slot.Aggregate);
        CombatTextMotionSettings motion = _profile.GetMotion(style, slot.Aggregate.IsBurnFamily, _accessibility.ReducedMotion);
        float scale = CombatTextStyleResolver.ResolveScale(
            in slot.Aggregate,
            _profile,
            _accessibility.CombatTextScale,
            distanceScale);
        CombatTextPresentation presentation = new(
            slot.Aggregate.TotalAppliedDamage,
            style,
            slot.Priority,
            slot.Aggregate.Key.DamageKind,
            slot.Aggregate.IsCritical,
            slot.Aggregate.IsWeakPoint,
            slot.Aggregate.IsKill,
            slot.Aggregate.IsBurnFamily,
            CompactLargeNumbers,
            _accessibility.ReducedFlash,
            !_accessibility.ReducedMotion && !_accessibility.ReducedShake &&
                (slot.Aggregate.IsCritical || slot.Aggregate.IsWeakPoint),
            worldPosition,
            scale,
            slot.Aggregate.Key.GetHashCode(),
            motion);
        view.Play(in presentation, _camera);
        if (!view.IsActive)
        {
            _pool.Release(view);
            slot.View = null;
            RecordSuppression(CombatTextSuppressionReason.Invalid, slotIndex);
            return false;
        }

        _startsThisFrame++;
        if (slot.Aggregate.IsBurnFamily)
            _visibleBurnCount++;
        Metrics.ViewsSpawned++;
        return true;
    }

    private void MergeVisibleView(ref AggregateSlot slot)
    {
        CombatTextStyleId style = CombatTextStyleResolver.ResolveStyle(in slot.Aggregate);
        float scale = CombatTextStyleResolver.ResolveScale(
            in slot.Aggregate,
            _profile,
            _accessibility.CombatTextScale,
            slot.DistanceScale);
        bool burn = slot.Aggregate.IsBurnFamily;
        CombatTextMergePresentation merge = new(
            slot.Aggregate.TotalAppliedDamage,
            style,
            slot.Priority,
            slot.Aggregate.Key.DamageKind,
            slot.Aggregate.IsCritical,
            slot.Aggregate.IsWeakPoint,
            slot.Aggregate.IsKill,
            CompactLargeNumbers,
            _accessibility.ReducedFlash,
            scale,
            burn ? _profile.BurnRePunchScale : _profile.DirectRePunchScale,
            _profile.RePunchDuration,
            burn ? _profile.BurnRePunchNudge : _profile.DirectRePunchNudge);
        slot.View.Merge(in merge);
        if (burn && slot.Aggregate.IsKill)
            slot.View.BeginRelease();
    }

    private void UpdateClosure(ref AggregateSlot slot, float now)
    {
        if (slot.Aggregate.IsClosed)
            return;

        int sequenceId = slot.Aggregate.Key.ActionSequenceId;
        bool hardLimit = now - slot.Aggregate.FirstEventTime >=
                         _profile.GetMaximumSegmentLifetime(slot.Aggregate.Key.DamageKind);
        bool fallbackExpired = sequenceId == 0 && now - slot.Aggregate.LastEventTime >=
            _profile.GetFallbackWindow(
                slot.Aggregate.Key.DamageKind,
                slot.Aggregate.Mode,
                slot.Aggregate.WeaponType);
        bool sequenceComplete = sequenceId != 0 && DamageFeedbackSequenceRuntime.IsComplete(sequenceId);
        bool targetGone = slot.Aggregate.IsBurnFamily && slot.Aggregate.Target == null;
        if (hardLimit || fallbackExpired || sequenceComplete || targetGone || slot.Aggregate.IsKill)
            slot.Aggregate.MarkClosed(now);
    }

    private bool ShouldReleaseRecord(in AggregateSlot slot, float now)
    {
        if (slot.Aggregate.IsClosed)
            return now - slot.Aggregate.ClosedTime >= _profile.SequenceCompletionGrace;
        if (slot.Aggregate.Key.ActionSequenceId == 0)
        {
            float fallback = _profile.GetFallbackWindow(
                slot.Aggregate.Key.DamageKind,
                slot.Aggregate.Mode,
                slot.Aggregate.WeaponType);
            return now - slot.Aggregate.LastEventTime >= fallback;
        }
        return now - slot.Aggregate.FirstEventTime >=
               _profile.GetMaximumSegmentLifetime(slot.Aggregate.Key.DamageKind);
    }

    private void UpdateViewAnchor(ref AggregateSlot slot, float now)
    {
        if (slot.View == null || !slot.View.IsAnchored)
            return;
        Vector3 worldPosition = ResolveWorldPosition(in slot.Aggregate);
        if (!CombatTextVisibilityPolicy.TryEvaluateWorld(
                _camera,
                worldPosition,
                slot.Priority,
                _profile,
                out _,
                out _))
        {
            if (slot.Aggregate.IsBurnFamily)
            {
                slot.Aggregate.MarkClosed(now);
                slot.View.BeginRelease();
            }
            return;
        }
        worldPosition = ApplySpatialOffset(worldPosition, slot.Lane);
        slot.View.SetAnchorPosition(worldPosition, false);
    }

    private int AssignLane(int hash, CombatTextPriority priority)
    {
        for (int i = 0; i < _laneOccupied.Length; i++)
            _laneOccupied[i] = false;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            if (slot.Active && slot.View != null && slot.View.IsAnchored &&
                slot.Lane >= 0 && slot.Lane < _laneOccupied.Length)
            {
                _laneOccupied[slot.Lane] = true;
            }
        }

        int start = priority >= CombatTextPriority.EliteBoss ? 0 : (hash & int.MaxValue) % CombatTextProfile.LaneCount;
        for (int offset = 0; offset < CombatTextProfile.LaneCount; offset++)
        {
            int lane = (start + offset) % CombatTextProfile.LaneCount;
            if (!_laneOccupied[lane])
                return lane;
        }
        return start;
    }

    private float GetLaneOffset(int lane)
    {
        return lane switch
        {
            1 => _profile.LaneSpacing,
            2 => -_profile.LaneSpacing,
            3 => _profile.LaneSpacing * 2f,
            _ => 0f
        };
    }

    private Vector3 ApplySpatialOffset(Vector3 worldPosition, int lane)
    {
        worldPosition += Vector3.up * GetLaneOffset(lane) * _profile.WorldUnitsPerMotionUnit;
        if (_camera != null && _profile.CameraSurfaceBias > 0f)
        {
            Vector3 towardCamera = _camera.transform.position - worldPosition;
            if (towardCamera.sqrMagnitude > 0.0001f)
                worldPosition += towardCamera.normalized * _profile.CameraSurfaceBias;
        }
        return worldPosition;
    }

    private bool TryReclaimLowerPriority(CombatTextPriority incoming)
    {
        int candidate = -1;
        CombatTextPriority lowest = incoming;
        float oldest = float.MaxValue;
        bool mayInterruptRoutine = incoming >= CombatTextPriority.EliteBoss;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref AggregateSlot slot = ref _slots[i];
            if (!slot.Active || slot.View == null || (!mayInterruptRoutine && !slot.View.IsFading) ||
                slot.Priority >= incoming || slot.Priority >= CombatTextPriority.WeakPoint)
            {
                continue;
            }
            if (slot.Priority < lowest ||
                (slot.Priority == lowest && slot.Aggregate.FirstEventTime < oldest))
            {
                candidate = i;
                lowest = slot.Priority;
                oldest = slot.Aggregate.FirstEventTime;
            }
        }
        if (candidate < 0)
            return false;

        ref AggregateSlot reclaimed = ref _slots[candidate];
        ReleaseView(ref reclaimed);
        reclaimed.WasHidden = true;
        Metrics.ReplacedByHigherPriority++;
        return true;
    }

    private void ReleaseView(ref AggregateSlot slot)
    {
        if (slot.View == null)
            return;
        if (slot.Aggregate.IsBurnFamily)
            _visibleBurnCount = Mathf.Max(0, _visibleBurnCount - 1);
        _pool.Release(slot.View);
        slot.View = null;
        slot.Lane = -1;
    }

    private void ClearSlot(int index)
    {
        ref AggregateSlot slot = ref _slots[index];
        if (!slot.Active)
            return;
        ReleaseView(ref slot);
        slot = default;
        _activeAggregateCount = Mathf.Max(0, _activeAggregateCount - 1);
    }

    private void RecordSuppression(CombatTextSuppressionReason reason, int slotIndex)
    {
        Metrics.LastSuppressionReason = reason;
        switch (reason)
        {
            case CombatTextSuppressionReason.Mode:
            case CombatTextSuppressionReason.Disabled:
                Metrics.SuppressedByMode++;
                break;
            case CombatTextSuppressionReason.Distance:
            case CombatTextSuppressionReason.Offscreen:
                Metrics.SuppressedByDistance++;
                break;
            case CombatTextSuppressionReason.BehindCamera:
                Metrics.SuppressedBehindCamera++;
                break;
            case CombatTextSuppressionReason.Density:
            case CombatTextSuppressionReason.BurnDensity:
            case CombatTextSuppressionReason.FrameStartBudget:
            case CombatTextSuppressionReason.PoolExhausted:
                Metrics.SuppressedByDensity++;
                break;
        }
        if (slotIndex >= 0 && !_slots[slotIndex].WasHidden)
        {
            _slots[slotIndex].WasHidden = true;
            Metrics.HiddenAggregatesWithoutViews++;
        }
    }

    private void SyncAccessibilityFromOptions()
    {
        if (!_mirrorRuntimeOptions || _options == null)
            return;
        PresentationAccessibilityState state = new(
            _options.ReducedMotion,
            _options.ReducedShake,
            _options.ReducedFlash,
            _options.CombatText,
            _options.CombatTextScale);
        if (state != _accessibility)
            ApplyAccessibilityInternal(state);
    }

    private void ApplyAccessibilityInternal(PresentationAccessibilityState accessibility)
    {
        _accessibility = accessibility;
        if (_accessibility.CombatText != CombatTextMode.Off)
            return;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Active)
                ReleaseView(ref _slots[i]);
        }
    }

    private void UpdateLiveMetrics()
    {
        GameFeelQualityLevel quality = CurrentQuality;
        Metrics.ActiveAggregates = _activeAggregateCount;
        Metrics.ActiveViews = _pool.ActiveCount;
        Metrics.ActiveViewLimit = _profile.GetActiveLimit(quality);
        Metrics.VisibleBurnTallies = _visibleBurnCount;
        Metrics.VisibleBurnTallyLimit = _profile.GetBurnLimit(quality);
        Metrics.ViewStartsPerFrameLimit = _profile.GetStartLimit(quality);
        Metrics.ActiveSequences = DamageFeedbackSequenceRuntime.ActiveCount;
        Metrics.SequenceTimeouts = DamageFeedbackSequenceRuntime.TimeoutCount;
        Metrics.SequenceOverflows = DamageFeedbackSequenceRuntime.OverflowCount;
        Metrics.PoolAvailable = _pool.AvailableCount;
        Metrics.PoolCapacity = _pool.Capacity;
        Metrics.MaximumActiveViewsObserved = Mathf.Max(Metrics.MaximumActiveViewsObserved, Metrics.ActiveViews);
        Metrics.AppliedQuality = quality;
        Metrics.AppliedReducedMotion = _accessibility.ReducedMotion;
        Metrics.AppliedReducedShake = _accessibility.ReducedShake;
        Metrics.AppliedReducedFlash = _accessibility.ReducedFlash;
        Metrics.AppliedCombatTextMode = _accessibility.CombatText;
        Metrics.AppliedCombatTextScale = _accessibility.CombatTextScale;
        Metrics.AppliedCompactFormatting = CompactLargeNumbers;
    }

    private GameFeelQualityLevel CurrentQuality => _options != null
        ? _options.Quality
        : GameFeelQualityLevel.High;

    private Vector3 ResolveWorldPosition(in CombatTextAggregate aggregate)
    {
        if (aggregate.IsBurnFamily && aggregate.Target != null)
            return ResolveBurnAnchor(aggregate.Target);
        return aggregate.WorldPosition;
    }

    private Vector3 ResolveBurnAnchor(Transform target)
    {
        Vector3 anchor = target.position + Vector3.up * _profile.WorldAnchorHeight;
        Bounds bounds;
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider != null && targetCollider.enabled)
            bounds = targetCollider.bounds;
        else
        {
            Renderer targetRenderer = target.GetComponentInChildren<Renderer>();
            if (targetRenderer == null || !targetRenderer.enabled)
                return anchor;
            bounds = targetRenderer.bounds;
        }

        anchor.x = bounds.center.x;
        anchor.z = bounds.center.z;
        anchor.y = Mathf.Max(anchor.y, bounds.max.y + _profile.WorldAnchorClearance);
        return anchor;
    }

    private static long SaturatingAdd(long current, int value)
    {
        return current > long.MaxValue - value ? long.MaxValue : current + value;
    }
}
