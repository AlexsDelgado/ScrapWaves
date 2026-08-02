using System.Collections.Generic;
using UnityEngine;

public sealed class RocketLauncherWeapon : BasicProjectileWeapon, IHoldActiveAbilityBehaviour, IRocketReticleStatus
{
    private readonly List<Transform> _abilityTargets = new();
    private readonly List<Transform> _abilityCandidates = new();
    private readonly List<Transform> _markedTargets = new();
    private readonly List<RocketTargetMarkerVfx> _targetMarkers = new();

    private bool _isActiveAbilityCharging;
    private int _requestedActiveTargetCount;
    private float _activeTargetLockTimer;
    private int _lastPresentedLockCount;

    public bool IsActiveAbilityCharging => _isActiveAbilityCharging;
    public bool IsTargetingActive => _isActiveAbilityCharging;
    public int CurrentRocketLocks => _abilityTargets.Count;
    public int InitialRocketLocks
    {
        get
        {
            if (Runtime?.Data == null)
                return 0;

            int maximum = GetActiveTargetLimit(Runtime.Data.RocketLauncher);
            return Mathf.Min(
                maximum,
                Mathf.Max(1, Runtime.Data.RocketLauncher.RocketActiveInitialTargetCount));
        }
    }
    public int MaximumRocketLocks => Runtime?.Data == null
        ? 0
        : GetActiveTargetLimit(Runtime.Data.RocketLauncher);

    public RocketLauncherWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Fires automatic rocket bursts with heat-scaled extra rockets.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        if (!Targeting.TryGetTarget(Runtime, Owner, Mathf.Max(0f, Runtime.Data.BaseRange), aimDirection, out Transform target))
            return;

        FireTimer = GetFireInterval();
        int extra = GetThresholdRocketBonus() + GetFragmentationRocketBonus();
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        FireBurstAt(
            EnemyRegistry.GetAimPoint(target),
            tuning.RocketAutoBaseRocketCount + extra,
            1f,
            GetPathAdjustedExplosionRadius(tuning.RocketAutoExplosionRadius),
            GetPathAdjustedFalloff(tuning.RocketAutoExplosionFalloff),
            tuning.RocketAutoSpeedMultiplier,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires one fast manual rocket and consumes one ammo unit.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        FireTimer = GetManualFireInterval();
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        FireRocketAt(
            Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange,
            1f,
            GetPathAdjustedExplosionRadius(tuning.RocketManualExplosionRadius),
            GetPathAdjustedFalloff(tuning.RocketManualExplosionFalloff),
            tuning.RocketManualSpeedMultiplier);
    }

    // Provides immediate behavior for debug/UI callers that do not use the hold lifecycle.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        BeginActiveAbility(aimDirection);
        ReleaseActiveAbility(aimDirection);
    }

    // Starts target acquisition, except Fragmentation Cap which fires directly on release.
    public void BeginActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility() || Spawn == null)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (Runtime.CurrentAmmo <= 0f)
            return;

        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        _isActiveAbilityCharging = true;
        _lastPresentedLockCount = 0;
        if (IsFragmentationCapPath())
        {
            _requestedActiveTargetCount = 0;
            _activeTargetLockTimer = Mathf.Max(0.01f, tuning.RocketActiveTargetLockInterval);
            _abilityTargets.Clear();
            _abilityCandidates.Clear();
            ClearTargetMarkers();
            return;
        }

        WeaponFeedbackContext feedback = CreateRocketFeedback(
            WeaponFeedbackMode.Active,
            Spawn.position,
            aimDirection,
            tuning.RocketActiveExplosionRadius,
            true,
            anchor: Spawn);
        Feedback.OnChargeStarted(in feedback);
        _requestedActiveTargetCount = GetInitialActiveTargetCount(tuning);
        _activeTargetLockTimer = Mathf.Max(0.01f, tuning.RocketActiveTargetLockInterval);
        RefreshActiveTargets(aimDirection, tuning);
        UpdateTargetingFeedback(aimDirection, tuning);
    }

    // Adds one target slot at each interval while Q remains held and refreshes locks with current aim.
    public void TickActiveAbility(float deltaTime, Vector3 aimDirection)
    {
        if (!_isActiveAbilityCharging)
            return;

        if (Runtime.State != WeaponState.Manual || Spawn == null)
        {
            CancelActiveAbility();
            return;
        }

        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        if (IsFragmentationCapPath())
        {
            _requestedActiveTargetCount = 0;
            _abilityTargets.Clear();
            ClearTargetMarkers();
            return;
        }

        int maximum = GetActiveTargetLimit(tuning);
        _requestedActiveTargetCount = Mathf.Min(_requestedActiveTargetCount, maximum);
        _activeTargetLockTimer -= deltaTime;

        float interval = Mathf.Max(0.01f, tuning.RocketActiveTargetLockInterval);
        while (_activeTargetLockTimer <= 0f && _requestedActiveTargetCount < maximum)
        {
            _requestedActiveTargetCount++;
            _activeTargetLockTimer += interval;
        }

        RefreshActiveTargets(aimDirection, tuning);
        UpdateTargetingFeedback(aimDirection, tuning);
    }

    // Fires the currently marked volley when Q is released.
    public void ReleaseActiveAbility(Vector3 aimDirection)
    {
        if (!_isActiveAbilityCharging)
            return;

        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        if (IsFragmentationCapPath())
        {
            _isActiveAbilityCharging = false;
            _requestedActiveTargetCount = 0;
            _abilityTargets.Clear();
            ClearTargetMarkers();

            if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
                return;

            Vector3 direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
            FireRocketAt(
                Spawn.position + direction * Mathf.Max(0.01f, Runtime.Data.BaseRange),
                tuning.RocketActiveDamageScale,
                GetPathAdjustedExplosionRadius(tuning.RocketActiveExplosionRadius, activeAbility: true),
                GetPathAdjustedFalloff(tuning.RocketActiveExplosionFalloff),
                tuning.RocketActiveSpeedMultiplier,
                false,
                isAbilityDamage: true);
            CompleteActiveAbility();
            return;
        }

        RefreshActiveTargets(aimDirection, tuning);
        _isActiveAbilityCharging = false;

        if (_abilityTargets.Count <= 0)
        {
            CancelTargetingFeedback(aimDirection, tuning);
            ClearTargetMarkers();
            return;
        }

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
        {
            CancelTargetingFeedback(aimDirection, tuning);
            ClearTargetMarkers();
            return;
        }

        bool emittedLaunchFeedback = false;
        for (int i = 0; i < _abilityTargets.Count; i++)
        {
            Transform target = _abilityTargets[i];
            if (target == null)
                continue;

            bool fired = FireRocketAt(
                target.position,
                tuning.RocketActiveDamageScale,
                GetPathAdjustedExplosionRadius(tuning.RocketActiveExplosionRadius, activeAbility: true),
                GetPathAdjustedFalloff(tuning.RocketActiveExplosionFalloff),
                tuning.RocketActiveSpeedMultiplier,
                WeaponEnemyClassifier.CountsAsEliteOrBoss(target),
                isAbilityDamage: true,
                emitShotFeedback: !emittedLaunchFeedback);
            emittedLaunchFeedback |= fired;
        }

        ClearTargetMarkers();
        _abilityTargets.Clear();
        CompleteActiveAbility();
    }

    public void CancelActiveAbility()
    {
        if (_isActiveAbilityCharging && !IsFragmentationCapPath() && Runtime?.Data != null && Spawn != null)
            CancelTargetingFeedback(Spawn.forward, Runtime.Data.RocketLauncher);
        _isActiveAbilityCharging = false;
        _requestedActiveTargetCount = 0;
        _abilityTargets.Clear();
        _abilityCandidates.Clear();
        ClearTargetMarkers();
    }

    // Rocket launcher heat adds rockets or manual fire rate, not passive automatic fire rate.
    protected override float GetHeatFireRateMultiplier() => 1f;

    // Returns manual fire interval boosted by heat percentage.
    private float GetManualFireInterval()
    {
        float baseInterval = GetFireInterval();
        float heatFactor = Heat != null ? 1f + Heat.NormalizedHeat : 1f;
        return baseInterval / Mathf.Max(0.2f, heatFactor);
    }

    // Converts 25/50/75 heat thresholds into bonus automatic rockets.
    private int GetThresholdRocketBonus()
    {
        if (Heat == null)
            return 0;

        float percent = Heat.NormalizedHeat * 100f;
        int bonus = 0;
        if (percent >= 25f) bonus++;
        if (percent >= 50f) bonus++;
        if (percent >= 75f) bonus++;
        return bonus;
    }

    private int GetFragmentationRocketBonus()
    {
        return IsFragmentationCapPath() ? 1 : 0;
    }

    private float GetPathAdjustedExplosionRadius(float radius)
    {
        return GetPathAdjustedExplosionRadius(radius, activeAbility: false);
    }

    private float GetPathAdjustedExplosionRadius(float radius, bool activeAbility)
    {
        if (IsKineticExplosionPath())
            radius *= 2f;
        if (IsFragmentationCapPath() && !activeAbility)
            radius *= 0.5f;
        return radius;
    }

    private float GetPathAdjustedFalloff(float falloff)
    {
        if (IsKineticExplosionPath())
            return Mathf.Clamp01(falloff * 0.65f);
        return falloff;
    }

    private float GetPathAdjustedKnockbackScale(bool activeAbility)
    {
        if (IsKineticExplosionPath())
            return activeAbility ? 0.5f : 3f;
        if (IsFragmentationCapPath())
            return 0.75f;
        return 1f;
    }

    private float GetFragmentDamageScale(bool activeAbility)
    {
        return IsFragmentationCapPath() && !activeAbility ? 1f : 0f;
    }

    private int GetFragmentClusterRocketCount()
    {
        return IsFragmentationCapPath() ? 20 : 0;
    }

    private float GetFragmentClusterDamageScale()
    {
        return IsFragmentationCapPath() ? 0.5f : 0f;
    }

    private float GetFragmentConeRange(float scaledExplosionRadius, bool activeAbility)
    {
        if (!IsFragmentationCapPath() || activeAbility)
            return 0f;

        return scaledExplosionRadius * 4f;
    }

    private bool IsKineticExplosionPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsFragmentationCapPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    // Spawns explosive rocket volley at the same target point.
    private void FireBurstAt(Vector3 targetPosition, int count, float damageScale, float explosionRadius, float falloff, float speedMultiplier, bool eliteOrBoss)
    {
        bool emittedLaunchFeedback = false;
        for (int i = 0; i < count; i++)
        {
            Vector3 launchOffset = GetVolleyLaunchOffset(i, count, targetPosition - Spawn.position);
            bool fired = FireRocketAt(
                targetPosition,
                damageScale,
                explosionRadius,
                falloff,
                speedMultiplier,
                eliteOrBoss,
                isAbilityDamage: false,
                emitShotFeedback: !emittedLaunchFeedback,
                launchOffset: launchOffset);
            emittedLaunchFeedback |= fired;
        }
    }

    private static Vector3 GetVolleyLaunchOffset(int index, int count, Vector3 direction)
    {
        if (count <= 1)
            return Vector3.zero;

        Vector3 forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();
        Vector3 up = Vector3.Cross(forward, right).normalized;
        float angle = index / (float)count * Mathf.PI * 2f;
        float radius = count <= 3 ? 0.13f : 0.2f;
        return right * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius);
    }

    private int GetMaximumActiveRocketCount(RocketLauncherTuning tuning)
    {
        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        return Mathf.Max(1, tuning.RocketActiveBaseRocketCount + heatBonus);
    }

    private int GetActiveTargetLimit(RocketLauncherTuning tuning)
    {
        if (IsFragmentationCapPath())
            return 0;

        return GetMaximumActiveRocketCount(tuning);
    }

    private int GetInitialActiveTargetCount(RocketLauncherTuning tuning)
    {
        return Mathf.Min(
            GetActiveTargetLimit(tuning),
            Mathf.Max(1, tuning.RocketActiveInitialTargetCount));
    }

    // Builds the current lock plan, assigning one rocket per enemy before elite/boss repeats.
    private void RefreshActiveTargets(Vector3 aimDirection, RocketLauncherTuning tuning)
    {
        _abilityTargets.Clear();
        if (_requestedActiveTargetCount <= 0 || Spawn == null)
        {
            SyncTargetMarkers(tuning);
            return;
        }

        int targetLimit = GetActiveTargetLimit(tuning);
        int requestedTargetCount = Mathf.Min(_requestedActiveTargetCount, targetLimit);

        EnemyRegistry.CollectClosestOnPlaneInCone(
            Spawn.position,
            aimDirection,
            Mathf.Max(0f, Runtime.Data.BaseRange),
            tuning.RocketActiveConeAngle,
            IsFragmentationCapPath() ? 1 : Mathf.Max(requestedTargetCount, 64),
            _abilityCandidates);

        for (int i = 0; i < _abilityCandidates.Count && _abilityTargets.Count < requestedTargetCount; i++)
            _abilityTargets.Add(_abilityCandidates[i]);

        if (IsFragmentationCapPath())
        {
            SyncTargetMarkers(tuning);
            return;
        }

        for (int repeatIndex = 1; repeatIndex < 5 && _abilityTargets.Count < requestedTargetCount; repeatIndex++)
        {
            for (int i = 0; i < _abilityCandidates.Count && _abilityTargets.Count < requestedTargetCount; i++)
            {
                Transform candidate = _abilityCandidates[i];
                if (GetMaxActiveRocketsForTarget(candidate) > repeatIndex)
                    _abilityTargets.Add(candidate);
            }
        }

        SyncTargetMarkers(tuning);
        EmitNewLockFeedback(aimDirection);
    }

    private void SyncTargetMarkers(RocketLauncherTuning tuning)
    {
        _markedTargets.Clear();
        for (int i = 0; i < _abilityTargets.Count; i++)
        {
            Transform target = _abilityTargets[i];
            if (target != null && !_markedTargets.Contains(target))
                _markedTargets.Add(target);
        }

        for (int i = _targetMarkers.Count - 1; i >= 0; i--)
        {
            RocketTargetMarkerVfx marker = _targetMarkers[i];
            if (marker != null && marker.Target != null && _markedTargets.Contains(marker.Target))
                continue;

            if (marker != null)
                Object.Destroy(marker.gameObject);
            _targetMarkers.RemoveAt(i);
        }

        for (int i = 0; i < _markedTargets.Count; i++)
        {
            Transform target = _markedTargets[i];
            bool alreadyMarked = false;
            for (int j = 0; j < _targetMarkers.Count; j++)
            {
                if (_targetMarkers[j] != null && _targetMarkers[j].Target == target)
                {
                    alreadyMarked = true;
                    break;
                }
            }

            if (alreadyMarked)
            {
                for (int j = 0; j < _targetMarkers.Count; j++)
                {
                    if (_targetMarkers[j] != null && _targetMarkers[j].Target == target)
                        _targetMarkers[j].SetLockCount(GetTargetLockCount(target));
                }
                continue;
            }

            RocketTargetMarkerVfx marker = RocketTargetMarkerVfx.Create(
                target,
                tuning.RocketActiveTargetMarkerRadius,
                GetTargetLockCount(target));
            if (marker != null)
                _targetMarkers.Add(marker);
        }
    }

    private void ClearTargetMarkers()
    {
        for (int i = 0; i < _targetMarkers.Count; i++)
        {
            if (_targetMarkers[i] != null)
                Object.Destroy(_targetMarkers[i].gameObject);
        }

        _targetMarkers.Clear();
        _markedTargets.Clear();
    }

    private Transform GetFirstActiveTarget()
    {
        for (int i = 0; i < _abilityTargets.Count; i++)
        {
            if (_abilityTargets[i] != null)
                return _abilityTargets[i];
        }

        return null;
    }

    private int GetTargetLockCount(Transform target)
    {
        int count = 0;
        for (int i = 0; i < _abilityTargets.Count; i++)
        {
            if (_abilityTargets[i] == target)
                count++;
        }
        return Mathf.Max(1, count);
    }

    // Uses current prefab naming until a dedicated elite/boss metadata component exists.
    private int GetMaxActiveRocketsForTarget(Transform target)
    {
        if (target == null)
            return 1;

        return WeaponEnemyClassifier.GetKind(target) switch
        {
            WeaponEnemyKind.Boss => 5,
            WeaponEnemyKind.Elite => 2,
            _ => 1
        };
    }

    // Fires a rocket that detonates on enemy hit or when it reaches weapon range.
    private void FireRocketAt(Vector3 targetPosition, float damageScale, float explosionRadius, float falloff, float speedMultiplier)
    {
        FireRocketAt(targetPosition, damageScale, explosionRadius, falloff, speedMultiplier, false);
    }

    private bool FireRocketAt(
        Vector3 targetPosition,
        float damageScale,
        float explosionRadius,
        float falloff,
        float speedMultiplier,
        bool eliteOrBoss,
        bool isAbilityDamage = false,
        bool emitShotFeedback = true,
        Vector3 launchOffset = default)
    {
        if (Pool == null || Spawn == null)
            return false;

        Vector3 launchPosition = Spawn.position + launchOffset;
        Vector3 direction = targetPosition - launchPosition;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        float scaledExplosionRadius = explosionRadius * GetAreaSizeMultiplier();
        float travelRange = Mathf.Max(0f, Runtime.Data.BaseRange);
        float pathKnockback = GetPathAdjustedKnockbackScale(isAbilityDamage);
        WeaponDamageContext damageContext = CreateDamageContext(damageScale, isAbilityDamage, damageScale * pathKnockback);
        int finalDamage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(finalDamage);
        float amplifier = IsKineticExplosionPath() ? 1.2f : 1f;
        float amplifierDuration = IsKineticExplosionPath() ? 5f : 0f;
        float fragmentConeRange = GetFragmentConeRange(scaledExplosionRadius, isAbilityDamage);
        float fragmentDamageScale = GetFragmentDamageScale(isAbilityDamage);
        float fragmentConeAngle = fragmentDamageScale > 0f ? 45f : 0f;

        if (IsFragmentationCapPath() && isAbilityDamage)
        {
            RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
            float clusterRadius = GetPathAdjustedExplosionRadius(tuning.RocketManualExplosionRadius) * GetAreaSizeMultiplier();
            float clusterDamageScale = GetFragmentClusterDamageScale();
            WeaponDamageContext clusterDamageContext = CreateDamageContext(
                damageScale * clusterDamageScale,
                isAbilityDamage,
                damageScale * GetPathAdjustedKnockbackScale(false));
            int clusterDamage = clusterDamageContext.EstimateDamage(eliteOrBoss);
            float clusterKnockback = clusterDamageContext.CalculateKnockback(clusterDamage);
            bool spawned = Pool.TrySpawnExplosiveProjectileWithAmplifierAndCluster(
                launchPosition,
                rotation,
                direction,
                finalDamage,
                scaledExplosionRadius,
                falloff,
                knockback,
                speedMultiplier,
                travelRange,
                true,
                amplifier,
                amplifierDuration,
                fragmentConeAngle,
                fragmentConeRange,
                fragmentDamageScale,
                GetFragmentClusterRocketCount(),
                clusterDamage,
                clusterRadius,
                GetPathAdjustedFalloff(tuning.RocketManualExplosionFalloff),
                clusterKnockback,
                tuning.RocketManualSpeedMultiplier,
                Mathf.Max(0f, Runtime.Data.BaseRange * 0.45f),
                GetFragmentDamageScale(activeAbility: false) > 0f ? 45f : 0f,
                GetFragmentConeRange(clusterRadius, activeAbility: false),
                GetFragmentDamageScale(activeAbility: false),
                5f,
                damageContext,
                clusterDamageContext,
                out Projectile clusterRocket);
            if (spawned)
                ConfigureRocketPresentation(clusterRocket, direction, scaledExplosionRadius, true, emitShotFeedback, true);
            return spawned;
        }

        bool rocketSpawned = Pool.TrySpawnExplosiveProjectileWithAmplifier(
            launchPosition,
            rotation,
            direction,
            finalDamage,
            scaledExplosionRadius,
            falloff,
            knockback,
            speedMultiplier,
            travelRange,
            true,
            amplifier,
            amplifierDuration,
            fragmentConeAngle,
            fragmentConeRange,
            fragmentDamageScale,
            damageContext,
            out Projectile rocket);
        if (rocketSpawned)
            ConfigureRocketPresentation(rocket, direction, scaledExplosionRadius, isAbilityDamage, emitShotFeedback, false);
        return rocketSpawned;
    }

    private void ConfigureRocketPresentation(
        Projectile projectile,
        Vector3 direction,
        float explosionRadius,
        bool isAbilityDamage,
        bool emitShotFeedback,
        bool clusterRocket)
    {
        if (projectile == null)
            return;

        WeaponFeedbackContext feedback = CreateRocketFeedback(
            GetFeedbackMode(isAbilityDamage),
            projectile.transform.position,
            direction,
            explosionRadius,
            isAbilityDamage);
        IWeaponFeedbackSink semantic = Feedback;
        projectile.ConfigureFeedback(
            semantic,
            in feedback,
            allowWeakPoint: false,
            replaceExplosionVfx: Runtime?.Data?.PresentationProfile != null,
            impactCueOverride: clusterRocket
                ? WeaponPresentationCue.RocketClusterDetonation
                : WeaponPresentationCue.None);
        semantic.ConfigureProjectile(
            projectile,
            clusterRocket ? ProjectilePresentationArchetypeId.ClusterRocket : ProjectilePresentationArchetypeId.Rocket,
            in feedback);
        if (emitShotFeedback)
            semantic.OnShotFired(in feedback);
    }

    private WeaponFeedbackMode GetFeedbackMode(bool isAbilityDamage)
    {
        if (isAbilityDamage)
            return WeaponFeedbackMode.Active;
        return Runtime != null && Runtime.State == WeaponState.Manual
            ? WeaponFeedbackMode.Manual
            : WeaponFeedbackMode.Automatic;
    }

    private WeaponFeedbackContext CreateRocketFeedback(
        WeaponFeedbackMode mode,
        Vector3 origin,
        Vector3 direction,
        float explosionRadius,
        bool isAbilityDamage,
        float intensity = 1f,
        Transform target = null,
        Transform anchor = null)
    {
        return new WeaponFeedbackContext(
            Runtime,
            mode,
            Heat != null ? Heat.NormalizedHeat : 0f,
            origin,
            direction,
            isAbilityDamage: isAbilityDamage,
            explosionRadius: explosionRadius,
            eventIntensity: intensity,
            target: target,
            anchor: anchor);
    }

    private void UpdateTargetingFeedback(Vector3 aimDirection, RocketLauncherTuning tuning)
    {
        float progress = MaximumRocketLocks <= 0 ? 0f : CurrentRocketLocks / (float)MaximumRocketLocks;
        WeaponFeedbackContext feedback = CreateRocketFeedback(
            WeaponFeedbackMode.Active,
            Spawn.position,
            aimDirection,
            tuning.RocketActiveExplosionRadius,
            true,
            intensity: Mathf.Lerp(0.65f, 1.25f, progress),
            anchor: Spawn);
        Feedback.OnChargeUpdated(in feedback, progress);
    }

    private void EmitNewLockFeedback(Vector3 aimDirection)
    {
        int current = _abilityTargets.Count;
        if (current <= _lastPresentedLockCount || Spawn == null)
        {
            _lastPresentedLockCount = current;
            return;
        }

        Transform newestTarget = _abilityTargets[current - 1];
        float progress = MaximumRocketLocks <= 0 ? 1f : current / (float)MaximumRocketLocks;
        WeaponPresentationContext cue = new(
            WeaponPresentationCue.RocketLockAcquired,
            Runtime,
            newestTarget != null ? newestTarget.position : Spawn.position,
            aimDirection,
            Mathf.Lerp(0.75f, 1.3f, progress),
            newestTarget,
            isAbility: true,
            anchor: newestTarget,
            mode: WeaponFeedbackMode.Active,
            upgradePath: Runtime != null && Runtime.HasAdvancedPath ? Runtime.SelectedPath : WeaponUpgradePath.None,
            weaponLevel: Runtime?.Level ?? 1,
            normalizedHeat: Heat != null ? Heat.NormalizedHeat : 0f);
        Presentation.Emit(in cue);
        _lastPresentedLockCount = current;
    }

    private void CancelTargetingFeedback(Vector3 direction, RocketLauncherTuning tuning)
    {
        WeaponFeedbackContext feedback = CreateRocketFeedback(
            WeaponFeedbackMode.Active,
            Spawn.position,
            direction,
            tuning.RocketActiveExplosionRadius,
            true,
            anchor: Spawn);
        Feedback.OnChargeCancelled(in feedback);
    }
}
