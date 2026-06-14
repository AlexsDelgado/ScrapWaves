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

    public bool IsActiveAbilityCharging => _isActiveAbilityCharging;
    public bool IsTargetingActive => _isActiveAbilityCharging;
    public int CurrentRocketLocks => _abilityTargets.Count;
    public int InitialRocketLocks
    {
        get
        {
            if (Runtime?.Data == null)
                return 0;

            int maximum = GetMaximumActiveRocketCount(Runtime.Data.RocketLauncher);
            return Mathf.Min(
                maximum,
                Mathf.Max(1, Runtime.Data.RocketLauncher.RocketActiveInitialTargetCount));
        }
    }
    public int MaximumRocketLocks => Runtime?.Data == null
        ? 0
        : GetMaximumActiveRocketCount(Runtime.Data.RocketLauncher);

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

        if (!Targeting.TryGetTarget(Runtime, Owner, Runtime.Data.BaseRange, aimDirection, out Transform target))
            return;

        FireTimer = GetFireInterval();
        int extra = GetThresholdRocketBonus() + GetFragmentationRocketBonus();
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        FireBurstAt(
            target.position,
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
        if (Runtime.State != WeaponState.Manual || !isFiring)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
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

    // Starts target acquisition with five locks immediately, capped by the current heat-scaled maximum.
    public void BeginActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility() || Spawn == null)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (Runtime.CurrentAmmo < Mathf.Max(0f, Runtime.Data.ActiveAbilityAmmoCost))
            return;

        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        _isActiveAbilityCharging = true;
        _requestedActiveTargetCount = Mathf.Min(
            GetMaximumActiveRocketCount(tuning),
            Mathf.Max(1, tuning.RocketActiveInitialTargetCount));
        _activeTargetLockTimer = Mathf.Max(0.01f, tuning.RocketActiveTargetLockInterval);
        RefreshActiveTargets(aimDirection, tuning);
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
        int maximum = GetMaximumActiveRocketCount(tuning);
        _requestedActiveTargetCount = Mathf.Min(_requestedActiveTargetCount, maximum);
        _activeTargetLockTimer -= deltaTime;

        float interval = Mathf.Max(0.01f, tuning.RocketActiveTargetLockInterval);
        while (_activeTargetLockTimer <= 0f && _requestedActiveTargetCount < maximum)
        {
            _requestedActiveTargetCount++;
            _activeTargetLockTimer += interval;
        }

        RefreshActiveTargets(aimDirection, tuning);
    }

    // Fires the currently marked volley when Q is released.
    public void ReleaseActiveAbility(Vector3 aimDirection)
    {
        if (!_isActiveAbilityCharging)
            return;

        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        RefreshActiveTargets(aimDirection, tuning);
        _isActiveAbilityCharging = false;

        if (_abilityTargets.Count <= 0)
        {
            ClearTargetMarkers();
            return;
        }

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
        {
            ClearTargetMarkers();
            return;
        }

        for (int i = 0; i < _abilityTargets.Count; i++)
        {
            Transform target = _abilityTargets[i];
            if (target == null)
                continue;

            FireRocketAt(
                target.position,
                tuning.RocketActiveDamageScale,
                GetPathAdjustedExplosionRadius(tuning.RocketActiveExplosionRadius),
                GetPathAdjustedFalloff(tuning.RocketActiveExplosionFalloff),
                tuning.RocketActiveSpeedMultiplier,
                WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
        }

        ClearTargetMarkers();
        _abilityTargets.Clear();
        CompleteActiveAbility();
    }

    public void CancelActiveAbility()
    {
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
        return Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB ? 1 : 0;
    }

    private float GetPathAdjustedExplosionRadius(float radius)
    {
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
            radius *= 1.3f;
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB)
            radius *= 0.8f;
        return radius;
    }

    private float GetPathAdjustedFalloff(float falloff)
    {
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
            return Mathf.Clamp01(falloff * 0.65f);
        return falloff;
    }

    // Spawns explosive rocket volley at the same target point.
    private void FireBurstAt(Vector3 targetPosition, int count, float damageScale, float explosionRadius, float falloff, float speedMultiplier, bool eliteOrBoss)
    {
        for (int i = 0; i < count; i++)
            FireRocketAt(targetPosition, damageScale, explosionRadius, falloff, speedMultiplier, eliteOrBoss);
    }

    private int GetMaximumActiveRocketCount(RocketLauncherTuning tuning)
    {
        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        return Mathf.Max(1, tuning.RocketActiveBaseRocketCount + heatBonus);
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

        EnemyRegistry.CollectClosestOnPlaneInCone(
            Spawn.position,
            aimDirection,
            Runtime.Data.BaseRange,
            tuning.RocketActiveConeAngle,
            Mathf.Max(_requestedActiveTargetCount, 64),
            _abilityCandidates);

        for (int i = 0; i < _abilityCandidates.Count && _abilityTargets.Count < _requestedActiveTargetCount; i++)
            _abilityTargets.Add(_abilityCandidates[i]);

        for (int repeatIndex = 1; repeatIndex < 5 && _abilityTargets.Count < _requestedActiveTargetCount; repeatIndex++)
        {
            for (int i = 0; i < _abilityCandidates.Count && _abilityTargets.Count < _requestedActiveTargetCount; i++)
            {
                Transform candidate = _abilityCandidates[i];
                if (GetMaxActiveRocketsForTarget(candidate) > repeatIndex)
                    _abilityTargets.Add(candidate);
            }
        }

        SyncTargetMarkers(tuning);
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
                continue;

            RocketTargetMarkerVfx marker = RocketTargetMarkerVfx.Create(target, tuning.RocketActiveTargetMarkerRadius);
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

    private void FireRocketAt(Vector3 targetPosition, float damageScale, float explosionRadius, float falloff, float speedMultiplier, bool eliteOrBoss)
    {
        FireExplosiveAt(targetPosition, damageScale, eliteOrBoss, explosionRadius, falloff, speedMultiplier, Runtime.Data.BaseRange, true);
    }
}
