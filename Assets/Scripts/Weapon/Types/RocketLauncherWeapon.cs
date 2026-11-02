using System.Collections.Generic;
using UnityEngine;

public sealed class RocketLauncherWeapon : BasicProjectileWeapon, IHoldActiveAbilityBehaviour, IRocketReticleStatus
{
    public override float? GetManualAimConvergenceDistance() =>
        Runtime?.Data != null && WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.RangeMeters, Runtime.Data.BaseRange).IsAuthored
            ? GetModeRange(WeaponStatMode.Manual) : (float?)null;

    public override WeaponGeometrySnapshot CaptureGeometry()
    {
        if (Runtime?.Data == null) return null;
        var guides = new List<WeaponGeometryGuide>
        {
            CreateAutomaticTargetingGuide(GetModeRange(WeaponStatMode.Automatic))
        };
        float manualRange = GetProjectileTravelRange(WeaponStatMode.Manual);
        float manualRadius = GetExplosionRadius(WeaponStatMode.Manual) * GetAreaSizeMultiplier();
        guides.Add(new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.ProjectilePath,
            WeaponGeometryShape.Line, WeaponGeometryAnchor.Muzzle, rangeMeters: manualRange));
        guides.Add(new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.Explosion,
            WeaponGeometryShape.Sphere, WeaponGeometryAnchor.Muzzle, radiusMeters: manualRadius,
            forwardOffsetMeters: manualRange));
        if (GetFragmentDamageScale(false) > 0f && GetFragmentConeAngle() > 0f && GetFragmentConeRange(manualRadius, false) > 0f)
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.Hitbox,
                WeaponGeometryShape.PlanarCone, WeaponGeometryAnchor.Muzzle,
                rangeMeters: GetFragmentConeRange(manualRadius, false), fullAngleDegrees: GetFragmentConeAngle(),
                forwardOffsetMeters: manualRange));
        if (IsFragmentationCapPath())
        {
            float range = GetProjectileTravelRange(WeaponStatMode.Ability);
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Ability, WeaponGeometryPurpose.ProjectilePath,
                WeaponGeometryShape.Line, WeaponGeometryAnchor.Muzzle, rangeMeters: range));
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Ability, WeaponGeometryPurpose.Explosion,
                WeaponGeometryShape.Sphere, WeaponGeometryAnchor.Muzzle,
                radiusMeters: GetExplosionRadius(WeaponStatMode.Ability) * GetAreaSizeMultiplier(), forwardOffsetMeters: range));
        }
        else
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Ability, WeaponGeometryPurpose.Targeting,
                WeaponGeometryShape.PlanarCone, WeaponGeometryAnchor.Muzzle,
                rangeMeters: GetModeRange(WeaponStatMode.Ability), fullAngleDegrees: Runtime.Data.RocketLauncher.RocketActiveConeAngle));
        // Automatic lofts and Q child impacts depend on acquired targets/child
        // directions. A static snapshot must not invent those future locations.
        return new WeaponGeometrySnapshot(guides.ToArray());
    }

    protected override void CollectDiagnostics(List<WeaponDiagnosticSection> sections)
    {
        RocketLauncherTuning t = Runtime.Data.RocketLauncher;
        var automatic = DiagnosticMode("Automatic", 1f, GetAutomaticFireInterval(), GetModeRange(WeaponStatMode.Automatic),
            Mathf.Max(1, t.RocketAutoBaseRocketCount + GetThresholdRocketBonus() + GetFragmentationRocketBonus()), 0f,
            knockbackScale: GetPathAdjustedKnockbackScale(false));
        var manual = DiagnosticMode("Manual", 1f, GetManualFireInterval(), GetModeRange(WeaponStatMode.Manual), 1, 1f,
            knockbackScale: GetPathAdjustedKnockbackScale(false));
        var active = DiagnosticMode("Active Ability", 1f, 0f, IsFragmentationCapPath()
            ? GetProjectileTravelRange(WeaponStatMode.Ability) : GetModeRange(WeaponStatMode.Ability),
            IsFragmentationCapPath() ? 1 : GetMaximumActiveRocketCount(t), WeaponMath.GetActiveAbilityAmmoCost(Runtime), true,
            t.RocketActiveDamageScale * GetPathAdjustedKnockbackScale(true),
            mode: WeaponStatMode.Ability, weaponDamageScale: t.RocketActiveDamageScale);
        automatic.Add("Volley shot interval", GetAutomaticVolleyShotInterval(), " s")
            .Add("Heat bonus rockets", GetThresholdRocketBonus())
            .Add("Projectile travel range", GetProjectileTravelRange(WeaponStatMode.Automatic), " m")
            .Add("Explosion radius", GetExplosionRadius(WeaponStatMode.Automatic) * GetAreaSizeMultiplier(), " m")
            .Add("Explosion falloff", GetPathAdjustedFalloff(t.RocketAutoExplosionFalloff));
        manual.Add("Explosion radius", GetExplosionRadius(WeaponStatMode.Manual) * GetAreaSizeMultiplier(), " m")
            .Add("Explosion falloff", GetPathAdjustedFalloff(t.RocketManualExplosionFalloff));
        active.Add("Projectile travel range", GetProjectileTravelRange(WeaponStatMode.Ability), " m")
            .Add("Explosion radius", GetExplosionRadius(WeaponStatMode.Ability) * GetAreaSizeMultiplier(), " m")
            .Add("Explosion falloff", GetPathAdjustedFalloff(t.RocketActiveExplosionFalloff));
        if (!IsFragmentationCapPath())
            active.Add("Maximum locked targets", GetActiveTargetLimit(t))
                .Add("Lock interval", t.RocketActiveTargetLockInterval, " s")
                .Add("Count assumption", "Maximum volley; actual count depends on acquired targets");
        if (IsFragmentationCapPath())
        {
            automatic.Add("Fragment damage scale", GetFragmentDamageScale(false), "x");
            manual.Add("Fragment damage scale", GetFragmentDamageScale(false), "x");
            active.Add("Cluster rockets", GetFragmentClusterRocketCount()).Add("Cluster damage scale", GetFragmentClusterDamageScale(), "x");
            foreach (var section in new[] { automatic, manual, active })
                section.Add("Fragment cone range / explosion radius", GetFragmentConeRangeMultiplier(), "x")
                    .Add("Fragment cone full angle", GetFragmentConeAngle(), " degrees");
            active.Add("Cluster explosion radius", GetClusterExplosionRadius() * GetAreaSizeMultiplier(), " m")
                .Add("Cluster travel range", GetClusterTravelRange(), " m");
        }
        if (IsKineticExplosionPath())
            foreach (var section in new[] { automatic, manual, active })
                section.Add("Vulnerability incoming damage multiplier", GetKineticDamageTakenMultiplier(), "x")
                    .Add("Vulnerability duration", GetKineticDuration(), " s");
        AddFlightDiagnostics(automatic, WeaponStatMode.Automatic);
        AddFlightDiagnostics(manual, WeaponStatMode.Manual);
        AddFlightDiagnostics(active, WeaponStatMode.Ability);
        if (IsFragmentationCapPath())
        {
            WeaponResolvedStat childLifetime = WeaponStatAuthoringResolver.ResolveOptional(Runtime,
                RocketOverrides?.AbilityClusterLifetimeSeconds ?? default, Pool != null ? Pool.ProjectileLifetime : 3f);
            if (childLifetime.IsAuthored || Pool != null) active.Add("Cluster lifetime cap", childLifetime.Value, " s");
            else active.Add("Cluster lifetime cap", "Inherited from projectile pool");
        }
        sections.Add(automatic); sections.Add(manual); sections.Add(active);
    }

    private readonly List<Transform> _abilityTargets = new();
    private readonly List<Transform> _abilityCandidates = new();
    private readonly List<Transform> _markedTargets = new();
    private readonly List<RocketTargetMarkerVfx> _targetMarkers = new();

    private bool _isActiveAbilityCharging;
    private int _requestedActiveTargetCount;
    private float _activeTargetLockTimer;
    private int _lastPresentedLockCount;
    private readonly List<Collider> _launchOwnerColliders = new();
    private readonly List<Renderer> _launchOwnerRenderers = new();
    private AutomaticVolley _automaticVolley;

    private struct AutomaticVolley
    {
        public int Remaining;
        public float Timer;
        public float ShotInterval;
        public Vector3 TargetPosition;
        public float DamageScale;
        public float ExplosionRadius;
        public float Falloff;
        public float SpeedMultiplier;
        public bool EliteOrBoss;
        public bool EmittedLaunchFeedback;
    }

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

    public override void SetFireOrigin(WeaponFireOriginBinding fireOrigin)
    {
        if (fireOrigin.IsValid && fireOrigin.Muzzle != Spawn)
            CancelAutomaticVolley();
        base.SetFireOrigin(fireOrigin);
    }

    // Fires automatic rocket bursts with heat-scaled extra rockets.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
        {
            CancelAutomaticVolley();
            return;
        }

        FireTimer -= deltaTime;
        if (TickAutomaticVolley(deltaTime))
            return;
        if (FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        if (!Targeting.TryGetTarget(Runtime, Owner, GetModeRange(WeaponStatMode.Automatic), aimDirection, out Transform target))
        {
            ClearFireOriginAim();
            return;
        }

        FireTimer = GetAutomaticFireInterval();
        int extra = GetThresholdRocketBonus() + GetFragmentationRocketBonus();
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        AimFireOriginAlong(Vector3.up);
        FireBurstAt(
            EnemyRegistry.GetAimPoint(target),
            tuning.RocketAutoBaseRocketCount + extra,
            1f,
            GetExplosionRadius(WeaponStatMode.Automatic),
            GetPathAdjustedFalloff(tuning.RocketAutoExplosionFalloff),
            tuning.RocketAutoSpeedMultiplier,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires one fast manual rocket and consumes one ammo unit.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        CancelAutomaticVolley();
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
        bool authoredRange = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.RangeMeters, Runtime.Data.BaseRange).IsAuthored;
        FireRocketAt(
            Spawn.position + aimDirection.normalized * GetModeRange(WeaponStatMode.Manual),
            1f,
            GetExplosionRadius(WeaponStatMode.Manual),
            GetPathAdjustedFalloff(tuning.RocketManualExplosionFalloff),
            tuning.RocketManualSpeedMultiplier,
            false,
            launchDirectionOverride: authoredRange ? aimDirection.normalized : (Vector3?)null);
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

        bool authoredFreeAbility = WeaponStatAuthoringResolver.GetLevelRow(Runtime)?.AbilityAmmoCost.Enabled == true
            && WeaponMath.GetActiveAbilityAmmoCost(Runtime) == 0f;
        if (Runtime.CurrentAmmo <= 0f && !authoredFreeAbility)
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

            if (!TrySpendManualAmmo(WeaponMath.GetActiveAbilityAmmoCost(Runtime), requireFullAmount: false))
                return;

            Vector3 direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
            FireRocketAt(
                Spawn.position + direction * Mathf.Max(0.01f, GetProjectileTravelRange(WeaponStatMode.Ability)),
                tuning.RocketActiveDamageScale,
                GetExplosionRadius(WeaponStatMode.Ability),
                GetPathAdjustedFalloff(tuning.RocketActiveExplosionFalloff),
                tuning.RocketActiveSpeedMultiplier,
                false,
                isAbilityDamage: true,
                launchDirectionOverride: RocketOverrides?.AbilityProjectileTravelRangeMeters.Enabled == true ? direction : (Vector3?)null);
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

        if (!TrySpendManualAmmo(WeaponMath.GetActiveAbilityAmmoCost(Runtime), requireFullAmount: false))
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
                GetExplosionRadius(WeaponStatMode.Ability),
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
        float legacyRate = Runtime.Data.BaseAttackRate * WeaponMath.GetAttackRateMultiplier(Runtime);
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.AttackCyclesPerSecond, legacyRate);
        float playerSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float baseInterval = rate.IsAuthored ? 1f / (rate.Value * playerSpeed)
            : 1f / Mathf.Max(0.05f, Mathf.Max(0.01f, rate.Value) * playerSpeed);
        float heatFactor = Heat != null ? 1f + Heat.NormalizedHeat : 1f;
        return baseInterval / Mathf.Max(0.2f, heatFactor);
    }

    private float GetAutomaticFireInterval()
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float legacyRate = Mathf.Max(0.01f, Runtime.Data.RocketLauncher.RocketAutoBurstsPerSecond)
            * WeaponMath.GetAttackRateMultiplier(Runtime);
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime,
            WeaponStatMode.Automatic, WeaponStatField.AttackCyclesPerSecond, legacyRate);
        float interval = rate.IsAuthored ? 1f / (rate.Value * attackSpeed)
            : 1f / Mathf.Max(0.05f, Mathf.Max(0.01f, rate.Value) * attackSpeed);
        return interval / Mathf.Max(0.01f, GetHeatFireRateMultiplier());
    }

    private float GetAutomaticVolleyShotInterval() =>
        Mathf.Max(0.01f, Runtime.Data.RocketLauncher.RocketAutoVolleyShotInterval);

    // Selects a total bonus, without accumulating earlier heat tiers.
    private int GetThresholdRocketBonus()
    {
        if (Heat == null)
            return 0;

        List<RocketAutoHeatBonus> entries = Runtime.Data.RocketLauncher.RocketAutoHeatBonuses;
        if (entries == null)
            return 0;

        float percent = Mathf.Clamp01(Heat.NormalizedHeat) * 100f;
        float highestThreshold = -1f;
        int bonus = 0;
        foreach (RocketAutoHeatBonus entry in entries)
        {
            float threshold = Mathf.Clamp(entry.HeatThresholdPercent, 0f, 100f);
            if (percent < threshold || threshold < highestThreshold)
                continue;
            int additional = Mathf.Max(0, entry.AdditionalRockets);
            bonus = threshold == highestThreshold ? Mathf.Max(bonus, additional) : additional;
            highestThreshold = threshold;
        }
        return bonus;
    }

    private int GetFragmentationRocketBonus()
    {
        return IsFragmentationCapPath() ? WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            RocketOverrides?.FragmentAdditionalAutomaticRockets ?? default, 1).Value : 0;
    }

    private float GetPathAdjustedExplosionRadius(float radius)
    {
        return GetPathAdjustedExplosionRadius(radius, activeAbility: false);
    }

    private float GetPathAdjustedExplosionRadius(float radius, bool activeAbility)
    {
        if (IsKineticExplosionPath())
            radius *= WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.KineticExplosionRadiusMultiplier ?? default, 2f).Value;
        if (IsFragmentationCapPath() && !activeAbility)
            radius *= WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.FragmentOrdinaryExplosionRadiusMultiplier ?? default, .5f).Value;
        return radius;
    }

    private float GetPathAdjustedFalloff(float falloff)
    {
        if (IsKineticExplosionPath())
            return Mathf.Clamp01(falloff * WeaponStatAuthoringResolver.ResolveOptional(Runtime,
                RocketOverrides?.KineticExplosionFalloffMultiplier ?? default, .65f).Value);
        return falloff;
    }

    private float GetPathAdjustedKnockbackScale(bool activeAbility)
    {
        if (IsKineticExplosionPath())
            return activeAbility
                ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.KineticAbilityKnockbackMultiplier ?? default, .5f).Value
                : WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.KineticOrdinaryKnockbackMultiplier ?? default, 3f).Value;
        if (IsFragmentationCapPath())
            return WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.FragmentKnockbackMultiplier ?? default, .75f).Value;
        return 1f;
    }

    private float GetFragmentDamageScale(bool activeAbility)
    {
        return IsFragmentationCapPath() && !activeAbility
            ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.FragmentDamageMultiplier ?? default, 1f).Value : 0f;
    }

    private int GetFragmentClusterRocketCount()
    {
        return IsFragmentationCapPath() ? WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            RocketOverrides?.AbilityClusterRocketCount ?? default, 20).Value : 0;
    }

    private float GetFragmentClusterDamageScale()
    {
        return IsFragmentationCapPath() ? WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            RocketOverrides?.AbilityClusterDamageMultiplier ?? default, .5f).Value : 0f;
    }

    private float GetFragmentConeRange(float scaledExplosionRadius, bool activeAbility)
    {
        if (!IsFragmentationCapPath() || activeAbility)
            return 0f;

        return scaledExplosionRadius * GetFragmentConeRangeMultiplier();
    }

    private WeaponRocketStatOverrides RocketOverrides => WeaponStatAuthoringResolver.GetLevelRow(Runtime)?.Rocket;

    private float GetModeRange(WeaponStatMode mode) => Mathf.Max(0f,
        WeaponStatAuthoringResolver.ResolveModeValue(Runtime, mode, WeaponStatField.RangeMeters,
            Runtime.Data.BaseRange).Value);

    // Acquisition and flight were linked by BaseRange. An explicit acquisition edit
    // must not also shorten an already distinct projectile or Q child flight.
    private float GetProjectileTravelRange(WeaponStatMode mode)
    {
        if (mode == WeaponStatMode.Manual) return GetModeRange(mode);
        WeaponRocketStatOverrides values = RocketOverrides;
        WeaponOptionalFloat value = mode == WeaponStatMode.Automatic
            ? values?.AutomaticProjectileTravelRangeMeters ?? default
            : values?.AbilityProjectileTravelRangeMeters ?? default;
        return WeaponStatAuthoringResolver.ResolveOptional(Runtime, value, Mathf.Max(0f, Runtime.Data.BaseRange)).Value;
    }

    private float GetExplosionRadius(WeaponStatMode mode)
    {
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        float legacy = mode == WeaponStatMode.Automatic ? tuning.RocketAutoExplosionRadius
            : mode == WeaponStatMode.Manual ? tuning.RocketManualExplosionRadius : tuning.RocketActiveExplosionRadius;
        legacy = GetPathAdjustedExplosionRadius(legacy, mode == WeaponStatMode.Ability);
        return WeaponStatAuthoringResolver.ResolveModeValue(Runtime, mode, WeaponStatField.ExplosionRadiusMeters, legacy).Value;
    }

    private float GetClusterExplosionRadius() => WeaponStatAuthoringResolver.ResolveOptional(Runtime,
        RocketOverrides?.AbilityClusterExplosionRadiusMeters ?? default,
        GetPathAdjustedExplosionRadius(Runtime.Data.RocketLauncher.RocketManualExplosionRadius)).Value;

    private float GetClusterTravelRange() => WeaponStatAuthoringResolver.ResolveOptional(Runtime,
        RocketOverrides?.AbilityClusterTravelRangeMeters ?? default, Mathf.Max(0f, Runtime.Data.BaseRange * 0.45f)).Value;

    private WeaponResolvedStat GetProjectileLifetime(WeaponStatMode mode)
    {
        WeaponRocketStatOverrides values = RocketOverrides;
        WeaponOptionalFloat authored = mode == WeaponStatMode.Automatic ? values?.AutomaticProjectileLifetimeSeconds ?? default
            : mode == WeaponStatMode.Manual ? values?.ManualProjectileLifetimeSeconds ?? default
            : values?.AbilityProjectileLifetimeSeconds ?? default;
        return WeaponStatAuthoringResolver.ResolveOptional(Runtime, authored, Pool != null ? Pool.ProjectileLifetime : 3f);
    }

    private void ConfigureAuthoredFlightLimits(Projectile projectile, WeaponStatMode mode, float travelRange)
    {
        bool authoredTravel = mode == WeaponStatMode.Manual
            ? WeaponStatAuthoringResolver.ResolveModeValue(Runtime, mode, WeaponStatField.RangeMeters, Runtime.Data.BaseRange).IsAuthored
            : mode == WeaponStatMode.Automatic ? RocketOverrides?.AutomaticProjectileTravelRangeMeters.Enabled == true
            : RocketOverrides?.AbilityProjectileTravelRangeMeters.Enabled == true;
        if (authoredTravel) projectile.ConfigureMaxTravel(travelRange, true, clampEndpoint: true);
        WeaponResolvedStat lifetime = GetProjectileLifetime(mode);
        if (lifetime.IsAuthored) projectile.ConfigureLifetime(lifetime.Value);
    }

    private void AddFlightDiagnostics(WeaponDiagnosticSection section, WeaponStatMode mode)
    {
        WeaponResolvedStat lifetime = GetProjectileLifetime(mode);
        if (lifetime.IsAuthored || Pool != null) section.Add("Projectile lifetime cap", lifetime.Value, " s");
        else section.Add("Projectile lifetime cap", "Inherited from projectile pool");
        if (mode == WeaponStatMode.Automatic && !lifetime.IsAuthored)
            section.Add("Inherited automatic lifetime", "Pool cap extends to account for lofted flight");
        section.Add("Flight termination", "First collision, maximum travel, or lifetime expiry");
    }

    private float GetKineticDuration() => IsKineticExplosionPath()
        ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.KineticDebuffDurationSeconds ?? default, 5f).Value : 0f;

    private float GetKineticDamageTakenMultiplier() => IsKineticExplosionPath()
        ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.KineticDamageTakenMultiplier ?? default, 1.2f).Value : 1f;

    private float GetFragmentConeRangeMultiplier() => IsFragmentationCapPath()
        ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.FragmentConeRangePerExplosionRadius ?? default, 4f).Value : 0f;

    private float GetFragmentConeAngle() => IsFragmentationCapPath()
        ? WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.FragmentConeAngleDegrees ?? default, 45f).Value : 0f;

    private bool IsKineticExplosionPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsFragmentationCapPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    // The first rocket leaves immediately; subsequent rounds use the current pipe position.
    private void FireBurstAt(Vector3 targetPosition, int count, float damageScale, float explosionRadius, float falloff, float speedMultiplier, bool eliteOrBoss)
    {
        _automaticVolley = new AutomaticVolley
        {
            Remaining = Mathf.Max(0, count),
            ShotInterval = GetAutomaticVolleyShotInterval(),
            TargetPosition = targetPosition,
            DamageScale = damageScale,
            ExplosionRadius = explosionRadius,
            Falloff = falloff,
            SpeedMultiplier = speedMultiplier,
            EliteOrBoss = eliteOrBoss
        };
        FireNextAutomaticRocket();
        _automaticVolley.Timer = _automaticVolley.ShotInterval;
    }

    private bool TickAutomaticVolley(float deltaTime)
    {
        if (_automaticVolley.Remaining <= 0)
            return false;
        if (Spawn == null)
        {
            CancelAutomaticVolley();
            return true;
        }
        _automaticVolley.Timer -= Mathf.Max(0f, deltaTime);
        while (_automaticVolley.Remaining > 0 && _automaticVolley.Timer <= 0f)
        {
            FireNextAutomaticRocket();
            _automaticVolley.Timer += _automaticVolley.ShotInterval;
        }
        return true;
    }

    private void FireNextAutomaticRocket()
    {
        if (_automaticVolley.Remaining <= 0)
            return;
        bool fired = FireRocketAt(
            _automaticVolley.TargetPosition,
            _automaticVolley.DamageScale,
            _automaticVolley.ExplosionRadius,
            _automaticVolley.Falloff,
            _automaticVolley.SpeedMultiplier,
            _automaticVolley.EliteOrBoss,
            isAbilityDamage: false,
            emitShotFeedback: !_automaticVolley.EmittedLaunchFeedback,
            automaticPipeLaunch: true);
        _automaticVolley.EmittedLaunchFeedback |= fired;
        _automaticVolley.Remaining--;
    }

    private void CancelAutomaticVolley()
    {
        _automaticVolley = default;
    }

    private float GetAutomaticLaunchClearanceHeight()
    {
        float top = Spawn.position.y;
        if (Owner != null)
        {
            Owner.GetComponentsInChildren(false, _launchOwnerColliders);
            for (int i = 0; i < _launchOwnerColliders.Count; i++)
            {
                Collider collider = _launchOwnerColliders[i];
                if (collider.enabled && !collider.isTrigger)
                    top = Mathf.Max(top, collider.bounds.max.y);
            }
            Owner.GetComponentsInChildren(false, _launchOwnerRenderers);
            for (int i = 0; i < _launchOwnerRenderers.Count; i++)
            {
                Renderer renderer = _launchOwnerRenderers[i];
                if (renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    top = Mathf.Max(top, renderer.bounds.max.y);
            }
        }
        return top + 0.6f;
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
            GetModeRange(WeaponStatMode.Ability),
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
                DestroyTargetMarker(marker);
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
                DestroyTargetMarker(_targetMarkers[i]);
        }

        _targetMarkers.Clear();
        _markedTargets.Clear();
    }

    private static void DestroyTargetMarker(RocketTargetMarkerVfx marker)
    {
        if (Application.isPlaying) Object.Destroy(marker.gameObject);
        else Object.DestroyImmediate(marker.gameObject);
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
        bool automaticPipeLaunch = false,
        Vector3? launchDirectionOverride = null)
    {
        if (Pool == null || Spawn == null)
            return false;

        Vector3 launchPosition = Spawn.position;
        Vector3 direction = launchDirectionOverride ?? (targetPosition - launchPosition);
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        if (automaticPipeLaunch)
            direction = Vector3.up;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        float scaledExplosionRadius = explosionRadius * GetAreaSizeMultiplier();
        WeaponStatMode mode = isAbilityDamage ? WeaponStatMode.Ability
            : automaticPipeLaunch ? WeaponStatMode.Automatic : WeaponStatMode.Manual;
        float travelRange = GetProjectileTravelRange(mode);
        float pathKnockback = GetPathAdjustedKnockbackScale(isAbilityDamage);
        bool usesCluster = IsFragmentationCapPath() && isAbilityDamage;
        int clusterContributorCount = usesCluster ? GetFragmentClusterRocketCount() : 0;
        int actionSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(
            DamageFeedbackKind.Explosion,
            1 + clusterContributorCount);
        WeaponDamageContext damageContext = CreateDamageContext(
                1f,
                isAbilityDamage,
                damageScale * pathKnockback,
                mode: mode,
                weaponDamageScale: damageScale)
            .WithFeedbackMetadata(actionSequenceId, DamageFeedbackKind.Explosion);
        int finalDamage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(finalDamage);
        float amplifier = GetKineticDamageTakenMultiplier();
        float amplifierDuration = GetKineticDuration();
        float fragmentConeRange = GetFragmentConeRange(scaledExplosionRadius, isAbilityDamage);
        float fragmentDamageScale = GetFragmentDamageScale(isAbilityDamage);
        float fragmentConeAngle = fragmentDamageScale > 0f ? GetFragmentConeAngle() : 0f;

        if (usesCluster)
        {
            RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
            float clusterRadius = GetClusterExplosionRadius() * GetAreaSizeMultiplier();
            float clusterDamageScale = GetFragmentClusterDamageScale();
            WeaponDamageContext clusterDamageContext = CreateDamageContext(
                    clusterDamageScale,
                    isAbilityDamage,
                    damageScale * GetPathAdjustedKnockbackScale(false),
                    mode: WeaponStatMode.Ability,
                    weaponDamageScale: damageScale)
                .WithFeedbackMetadata(actionSequenceId, DamageFeedbackKind.Fragment);
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
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.AbilityClusterFalloff ?? default,
                    GetPathAdjustedFalloff(tuning.RocketManualExplosionFalloff)).Value,
                clusterKnockback,
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, RocketOverrides?.AbilityClusterSpeedMultiplier ?? default,
                    tuning.RocketManualSpeedMultiplier).Value,
                GetClusterTravelRange(),
                GetFragmentDamageScale(activeAbility: false) > 0f ? GetFragmentConeAngle() : 0f,
                GetFragmentConeRange(clusterRadius, activeAbility: false),
                GetFragmentDamageScale(activeAbility: false),
                5f,
                damageContext,
                clusterDamageContext,
                out Projectile clusterRocket);
            if (spawned)
            {
                ConfigureAuthoredFlightLimits(clusterRocket, mode, travelRange);
                if (RocketOverrides?.AbilityClusterLifetimeSeconds.Enabled == true)
                    clusterRocket.ConfigureClusterLifetime(RocketOverrides.AbilityClusterLifetimeSeconds.Value);
                if (RocketOverrides?.AbilityClusterTravelRangeMeters.Enabled == true)
                    clusterRocket.ConfigureClusterTravelEndpointClamp();
                if (RocketOverrides?.AbilityClusterExplosionRadiusMeters.Enabled == true
                    || RocketOverrides?.FragmentOrdinaryExplosionRadiusMultiplier.Enabled == true)
                    clusterRocket.ConfigureExactClusterRadius(clusterRadius);
                clusterRocket.ConfigureDamageFeedback(
                    actionSequenceId,
                    DamageFeedbackKind.Explosion,
                    completesSequenceContributor: actionSequenceId != 0);
                ConfigureRocketPresentation(clusterRocket, direction, scaledExplosionRadius, true, emitShotFeedback, true);
            }
            else
            {
                CompleteFailedSequenceContributors(actionSequenceId, 1 + clusterContributorCount);
            }
            DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
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
        {
            if (automaticPipeLaunch)
            {
                rocket.ConfigureAutomaticRocketLaunch(targetPosition, GetAutomaticLaunchClearanceHeight(), Owner);
                // Inherited reach includes the historical loft compensation. An
                // explicit travel override is literal distance along that arc.
                if (RocketOverrides?.AutomaticProjectileTravelRangeMeters.Enabled == true)
                    rocket.ConfigureMaxTravel(travelRange, true);
            }
            ConfigureAuthoredFlightLimits(rocket, mode, travelRange);
            rocket.ConfigureDamageFeedback(
                actionSequenceId,
                DamageFeedbackKind.Explosion,
                completesSequenceContributor: actionSequenceId != 0);
            ConfigureRocketPresentation(rocket, direction, scaledExplosionRadius, isAbilityDamage, emitShotFeedback, false);
        }
        else
        {
            CompleteFailedSequenceContributors(actionSequenceId, 1);
        }
        DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
        return rocketSpawned;
    }

    private static void CompleteFailedSequenceContributors(int sequenceId, int count)
    {
        if (sequenceId == 0)
            return;
        for (int i = 0; i < Mathf.Max(0, count); i++)
            DamageFeedbackSequenceRuntime.CompleteContributor(sequenceId);
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
