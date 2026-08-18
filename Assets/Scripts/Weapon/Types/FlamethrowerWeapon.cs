using System.Collections.Generic;
using UnityEngine;

public sealed class FlamethrowerWeapon : BasicProjectileWeapon
{
    private static readonly Color BaseFlameCoreColor = new(1f, 0.75f, 0.15f, 0.95f);
    private static readonly Color BaseFlameEdgeColor = new(1f, 0.18f, 0.02f, 0.75f);
    private static readonly Color JellifiedFuelCoreColor = new(0.08f, 0.32f, 0.09f, 0.92f);
    private static readonly Color JellifiedFuelVfxColor = new(0.02f, 0.16f, 0.04f, 0.9f);
    private static readonly Color LiquidNitrogenCoreColor = new(0.78f, 0.97f, 1f, 0.95f);
    private static readonly Color LiquidNitrogenVfxColor = new(0.38f, 0.78f, 1f, 0.88f);

    private readonly PlayerMovement _movement;
    private readonly List<Transform> _targets = new();
    private readonly List<Vector3> _hitOrigins = new();
    private readonly FlamethrowerHoseStream _hoseStream = new();

    private FlamethrowerStreamVfx _streamVfx;
    private float _autoTickTimer;
    private float _manualTickTimer;
    private bool _sustainedFeedbackActive;
    private WeaponFeedbackMode _sustainedFeedbackMode;
    private WeaponUpgradePath _sustainedFeedbackPath;

    public string LastManualDebugSummary { get; private set; } = "No manual tick yet";
    public int LastManualHitCount { get; private set; }
    public int LastManualDamageApplications { get; private set; }
    public int LastManualPointCount { get; private set; }
    public int LastManualRegistryCount { get; private set; }
    public float LastManualHoseRadius { get; private set; }
    public float LastManualRange { get; private set; }
    public float LastManualAmmoBefore { get; private set; }
    public float LastManualAmmoAfter { get; private set; }
    public bool LastManualFireHeld { get; private set; }
    public Vector3 LastManualAimDirection { get; private set; }

    public FlamethrowerWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn, PlayerMovement movement)
        : base(targeting, pool, spawn)
    {
        _movement = movement;
    }

    // Emits an automatic cone in the player's movement direction.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
        {
            StopSustainedFeedback(GetAutomaticFlameDirection());
            return;
        }

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        Vector3 flameDirection = GetAutomaticFlameDirection();
        float range = GetScaledRange(Runtime.Data.BaseRange);
        ShowAutomaticCone(flameDirection, range, tuning);
        UpdateSustainedFeedback(WeaponFeedbackMode.Automatic, flameDirection, range);

        _autoTickTimer -= deltaTime;
        if (_autoTickTimer > 0f)
            return;

        ApplyAutomaticConeDamage(flameDirection, range, tuning);
        _autoTickTimer = GetAutomaticTickInterval(tuning);
    }

    // Holds a continuous stream in the manual aim direction and spends ammo over time.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        ResetManualDebug(aimDirection, isFiring);

        if (Runtime.State != WeaponState.Manual)
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = $"Skip: state {Runtime.State}";
            return;
        }

        if (!isFiring)
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = "Skip: fire not held";
            return;
        }

        if (aimDirection.sqrMagnitude <= 0.0001f)
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = "Skip: no aim direction";
            return;
        }

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        float ammoCost = tuning.FlameManualAmmoPerSecond * deltaTime;
        LastManualAmmoBefore = Runtime.CurrentAmmo;
        if (!TrySpendManualAmmo(ammoCost, requireFullAmount: false))
        {
            StopSustainedFeedback(aimDirection);
            LastManualAmmoAfter = Runtime.CurrentAmmo;
            LastManualDebugSummary = $"Skip: no ammo ({LastManualAmmoAfter:0.#})";
            return;
        }

        float range = GetManualRange(tuning);
        LastManualAmmoAfter = Runtime.CurrentAmmo;
        LastManualRange = range;
        LastManualHoseRadius = GetScaledHoseRadius(tuning);
        if (!ShowStream(aimDirection, range, tuning, deltaTime))
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = "Skip: no projectile spawn";
            return;
        }
        UpdateSustainedFeedback(WeaponFeedbackMode.Manual, aimDirection, range);

        LastManualPointCount = _hoseStream.PointCount;

        _manualTickTimer -= deltaTime;
        if (_manualTickTimer > 0f)
        {
            LastManualDebugSummary = $"Waiting tick: {_manualTickTimer:0.00}s";
            return;
        }

        LastManualHitCount = ApplyHoseDamage(
            1f,
            applyBurn: true,
            knockbackScale: tuning.FlameManualKnockbackScale,
            tuning: tuning);
        _manualTickTimer = Mathf.Max(0.01f, tuning.FlameManualTickInterval);
        LastManualDebugSummary = $"Hits {LastManualHitCount} | Applied {LastManualDamageApplications} | Registry {LastManualRegistryCount} | Points {LastManualPointCount}";
    }

    // Emits a circular flame burst around the player.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
            return;

        StopSustainedFeedback(aimDirection);

        float activeRadius = GetScaledRange(GetPathAdjustedActiveRadius(tuning));
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            Owner.position,
            Owner.forward,
            activeRadius,
            360f,
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets);

        for (int i = 0; i < hitCount; i++)
        {
            int damage = CalculateDirectDamage(tuning.FlameActiveDamageScale, _targets[i], isAbilityDamage: true);
            int burnDamage = CalculateBurnDamage(tuning, _targets[i], isAbilityDamage: true);
            ApplyDamageToTarget(_targets[i], damage, Owner.position, tuning.FlameActiveKnockbackScale, activeAbility: true);
            ApplyBurnToTarget(_targets[i], burnDamage, tuning, activeAbility: true);
        }

        if (IsJellifiedFuelPath())
        {
            Vector2 puddleSettings = GetJellifiedActivePuddleSettings(tuning, activeRadius);
            int puddleDamage = CalculateBurnDamage(tuning, null, isAbilityDamage: true);
            SpawnFuelPuddle(
                Owner.position,
                puddleSettings.x,
                puddleDamage,
                puddleSettings.y,
                tuning.FlameBurnTickInterval,
                CreateBurnDamageContext(tuning, isAbilityDamage: true));
        }

        Vector3 activeDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Owner.forward;
        WeaponFeedbackContext activeFeedback = CreateFeedbackContext(
            WeaponFeedbackMode.Active,
            Owner.position,
            activeDirection,
            explosionRadius: activeRadius,
            isAbilityDamage: true);
        Feedback.OnShotFired(in activeFeedback);
        if (!HasProductionPresentation())
            FlamethrowerStreamVfx.SpawnRing(Owner.position, activeRadius, tuning.FlameActiveVisualDuration, GetStreamCoreColor(), GetStreamEdgeColor());
        CompleteActiveAbility();
    }

    // Flamethrower direct ticks can crit; burn ticks do not.
    public override bool CanCrit() => true;

    // Uses current movement as the automatic aim, falling back to the player's facing while idle.
    private Vector3 GetAutomaticFlameDirection()
    {
        Vector3 direction = _movement != null ? _movement.CurrentMoveDirectionWorld : Vector3.zero;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f && Owner != null)
        {
            direction = Owner.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

        return direction.normalized;
    }

    // Applies faster automatic ticks above the configured heat threshold.
    private float GetAutomaticTickInterval(FlamethrowerTuning tuning)
    {
        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        if (heatPercent >= tuning.FlameOverheatTickThresholdPercent)
            return Mathf.Max(0.01f, tuning.FlameOverheatAutoTickInterval);

        return Mathf.Max(0.01f, tuning.FlameAutoTickInterval);
    }

    // Manual stream reaches farther as heat rises.
    private float GetManualRange(FlamethrowerTuning tuning)
    {
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        return GetScaledRange(Runtime.Data.BaseRange) * (1f + Mathf.Max(0f, tuning.FlameManualRangeHeatMultiplier) * heat);
    }

    // Damages every enemy inside the horizontal automatic flame cone.
    private int ApplyAutomaticConeDamage(Vector3 direction, float range, FlamethrowerTuning tuning)
    {
        if (Owner == null)
            return 0;

        Vector3 origin = Spawn != null ? Spawn.position : Owner.position;
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            origin,
            direction,
            range,
            tuning.FlameAutoConeAngle,
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets);

        for (int i = 0; i < hitCount; i++)
        {
            int damage = CalculateDirectDamage(1f, _targets[i]);
            if (ApplyDamageToTarget(_targets[i], damage, origin, knockbackScale: 0f)
                && (IsJellifiedFuelPath() || IsLiquidNitrogenPath()))
            {
                int burnDamage = CalculateBurnDamage(tuning, _targets[i]);
                ApplyBurnToTarget(_targets[i], burnDamage, tuning, activeAbility: false);
            }
        }

        return hitCount;
    }

    // Damages enemies near the simulated hose path and optionally refreshes burn on them.
    private int ApplyHoseDamage(float damageScale, bool applyBurn, float knockbackScale, FlamethrowerTuning tuning)
    {
        LastManualDamageApplications = 0;
        LastManualRegistryCount = EnemyRegistry.ActiveCount;
        if (Owner == null || _hoseStream.Points == null || _hoseStream.PointCount <= 0)
            return 0;

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _hoseStream.Points,
            _hoseStream.PointCount,
            LastManualHoseRadius > 0f ? LastManualHoseRadius : GetScaledHoseRadius(tuning),
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets,
            _hitOrigins);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : (Spawn != null ? Spawn.position : Owner.position);
            int damage = CalculateDirectDamage(damageScale, _targets[i]);
            if (ApplyDamageToTarget(_targets[i], damage, impactOrigin, knockbackScale))
                LastManualDamageApplications++;
            if (applyBurn)
            {
                int burnDamage = CalculateBurnDamage(tuning, _targets[i]);
                ApplyBurnToTarget(_targets[i], burnDamage, tuning, activeAbility: false);
            }
        }

        return hitCount;
    }

    // Calculates one direct flamethrower damage tick from shared weapon rules.
    private int CalculateDirectDamage(float damageScale, Transform target, bool isAbilityDamage = false)
    {
        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), isAbilityDamage: isAbilityDamage, targetPosition: target != null ? target.position : (Vector3?)null) * Mathf.Max(0f, damageScale);
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    // Calculates burn damage separately so damage-over-time does not roll critical hits.
    private int CalculateBurnDamage(FlamethrowerTuning tuning, Transform target, bool isAbilityDamage = false)
    {
        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float pathScale = IsJellifiedFuelPath() ? 1.35f : 1f;
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, canCrit: false, isAbilityDamage: isAbilityDamage, targetPosition: target != null ? target.position : (Vector3?)null) * Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale;
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    private WeaponDamageContext CreateBurnDamageContext(FlamethrowerTuning tuning, bool isAbilityDamage)
    {
        float pathScale = IsJellifiedFuelPath() ? 1.35f : 1f;
        return new WeaponDamageContext(
            Stats,
            Runtime,
            canCrit: false,
            critMultiplierOverride: 1f,
            damageScale: Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale,
            isAbilityDamage: isAbilityDamage,
            knockbackScale: 0f);
    }

    private bool IsJellifiedFuelPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsLiquidNitrogenPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    // Applies immediate damage to one enemy transform if it has a damage receiver.
    private bool ApplyDamageToTarget(Transform target, int damage, Vector3 impactOrigin, float knockbackScale, bool activeAbility = false)
    {
        if (target == null)
            return false;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable != null && WeaponDamageApplier.TryApplyDamage(damageable, damage))
        {
            ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);
            Vector3 direction = target.position - impactOrigin;
            WeaponFeedbackContext feedback = CreateFeedbackContext(
                activeAbility ? WeaponFeedbackMode.Active : GetCurrentFeedbackMode(),
                impactOrigin,
                direction,
                impactPosition: target.position,
                damageAmount: damage,
                target: target,
                anchor: target,
                isAbilityDamage: activeAbility);
            Feedback.OnDamageConfirmed(in feedback);
            return true;
        }

        return false;
    }

    // Refreshes a simple burn component on the target's damage receiver.
    private void ApplyBurnToTarget(Transform target, int damagePerTick, FlamethrowerTuning tuning, bool activeAbility)
    {
        if (target == null || tuning.FlameBurnDuration <= 0f)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable is not Component damageComponent)
            return;

        if (IsLiquidNitrogenPath())
        {
            ApplyLiquidNitrogenStatus(target, activeAbility);
            return;
        }

        FlamethrowerBurnStatus burn = damageComponent.GetComponent<FlamethrowerBurnStatus>();
        if (burn == null)
            burn = damageComponent.gameObject.AddComponent<FlamethrowerBurnStatus>();

        float duration = GetPathAdjustedBurnDuration(tuning);
        burn.Refresh(damageable, damagePerTick, duration, tuning.FlameBurnTickInterval);

        if (IsJellifiedFuelPath())
        {
            float levelScale = Runtime != null ? Mathf.Max(1f, Runtime.Level / 6f) : 1f;
            float radius = GetScaledHoseRadius(tuning) * levelScale;
            SpawnFuelPuddle(
                target.position,
                radius,
                damagePerTick,
                duration,
                tuning.FlameBurnTickInterval,
                CreateBurnDamageContext(tuning, activeAbility));
        }

        WeaponDummyEnemy dummy = damageComponent.GetComponent<WeaponDummyEnemy>();
        if (dummy != null && IsJellifiedFuelPath())
        {
            dummy.ApplyStatus("Jellified Fuel", duration);
        }

        EmitStatusFeedback(target, damagePerTick, activeAbility);
    }

    private void ApplyLiquidNitrogenStatus(Transform target, bool activeAbility)
    {
        if (activeAbility)
        {
            const float freezeDuration = 2f;
            if (!HasProductionPresentation())
                WeaponStatusShardVfx.SpawnIceShards(target, LiquidNitrogenCoreColor, LiquidNitrogenVfxColor, 1.2f, frozen: true);
            WeaponMovementSlowStatus.Apply(target, 0.1f, freezeDuration * 2f, "Deep Freeze");
            WeaponMovementFreezeStatus.Apply(target, freezeDuration);
            EmitStatusFeedback(target, 0, activeAbility: true);
            return;
        }

        if (!HasProductionPresentation())
            WeaponStatusShardVfx.SpawnIceShards(target, LiquidNitrogenCoreColor, LiquidNitrogenVfxColor, 0.55f, frozen: false);
        WeaponMovementSlowStatus.ApplyRamp(target, 0.5f, 0.1f, 6, 3f, "Liquid Nitrogen");
        EmitStatusFeedback(target, 0, activeAbility: false);
    }

    // Keeps one reusable stream simulation and visual alive while the weapon fires.
    private bool ShowStream(Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime)
    {
        if (Spawn == null)
            return false;

        if (_streamVfx == null)
        {
            FlamethrowerPresentationSettings settings = GetPresentationSettings();
            _streamVfx = FlamethrowerStreamVfx.Create(settings?.StreamPrefab, settings?.MaximumStreamSegments ?? 48);
        }

        _streamVfx.SetStyle(GetStreamStyle());
        _streamVfx.SetHeat(Heat != null ? Heat.NormalizedHeat : 0f);
        _hoseStream.Update(Spawn.position, direction, range, tuning, deltaTime);
        _streamVfx.ShowHose(_hoseStream.Points, _hoseStream.PointCount, LastManualHoseRadius > 0f ? LastManualHoseRadius : GetScaledHoseRadius(tuning), tuning.FlameVisualDuration);
        return true;
    }

    // Keeps the automatic visual aligned with the same cone used for damage.
    private void ShowAutomaticCone(Vector3 direction, float range, FlamethrowerTuning tuning)
    {
        if (Spawn == null)
            return;

        if (_streamVfx == null)
        {
            FlamethrowerPresentationSettings settings = GetPresentationSettings();
            _streamVfx = FlamethrowerStreamVfx.Create(settings?.StreamPrefab, settings?.MaximumStreamSegments ?? 48);
        }

        _streamVfx.SetStyle(GetStreamStyle());
        _streamVfx.SetHeat(Heat != null ? Heat.NormalizedHeat : 0f);
        _streamVfx.ShowCone(Spawn.position, direction, range, tuning.FlameAutoConeAngle, tuning.FlameVisualDuration);
    }

    private void SpawnFuelPuddle(
        Vector3 position,
        float radius,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponDamageContext damageContext)
    {
        FlamethrowerPresentationSettings settings = GetPresentationSettings();
        if (settings?.FuelPuddlePrefab != null)
        {
            FlamethrowerFuelPuddle.SpawnAuthored(
                settings.FuelPuddlePrefab,
                settings.FuelPuddlePrewarmCount,
                settings.FuelPuddlePoolCapacity,
                position,
                radius,
                damagePerTick,
                duration,
                tickInterval,
                damageContext);
            return;
        }

        FlamethrowerFuelPuddle.SpawnWithContext(position, radius, damagePerTick, duration, tickInterval, damageContext);
    }

    private FlamethrowerPresentationSettings GetPresentationSettings() =>
        Runtime?.Data?.PresentationProfile != null ? Runtime.Data.PresentationProfile.Flamethrower : null;

    private bool HasProductionPresentation() => Runtime?.Data?.PresentationProfile != null;

    private FlamethrowerStreamStyle GetStreamStyle()
    {
        if (IsJellifiedFuelPath())
            return FlamethrowerStreamStyle.JellifiedFuel;
        return IsLiquidNitrogenPath()
            ? FlamethrowerStreamStyle.LiquidNitrogen
            : FlamethrowerStreamStyle.Flame;
    }

    private void UpdateSustainedFeedback(WeaponFeedbackMode mode, Vector3 direction, float range)
    {
        WeaponUpgradePath path = Runtime != null && Runtime.HasAdvancedPath ? Runtime.SelectedPath : WeaponUpgradePath.None;
        if (_sustainedFeedbackActive && (_sustainedFeedbackMode != mode || _sustainedFeedbackPath != path))
            StopSustainedFeedback(direction);

        Vector3 origin = Spawn != null ? Spawn.position : Owner.position;
        WeaponFeedbackContext context = CreateFeedbackContext(mode, origin, direction, explosionRadius: range, anchor: Spawn);
        Feedback.OnSustainedFireStarted(in context);
        _sustainedFeedbackActive = true;
        _sustainedFeedbackMode = mode;
        _sustainedFeedbackPath = path;
    }

    private void StopSustainedFeedback(Vector3 direction)
    {
        if (!_sustainedFeedbackActive || Runtime == null)
            return;
        Vector3 origin = Spawn != null ? Spawn.position : Owner != null ? Owner.position : Vector3.zero;
        WeaponFeedbackContext context = CreateFeedbackContext(_sustainedFeedbackMode, origin, direction, anchor: Spawn);
        Feedback.OnSustainedFireStopped(in context);
        _sustainedFeedbackActive = false;
    }

    private void EmitStatusFeedback(Transform target, int damageAmount, bool activeAbility)
    {
        if (target == null)
            return;
        Vector3 origin = Spawn != null ? Spawn.position : Owner != null ? Owner.position : target.position;
        WeaponFeedbackContext context = CreateFeedbackContext(
            activeAbility ? WeaponFeedbackMode.Active : GetCurrentFeedbackMode(),
            origin,
            target.position - origin,
            impactPosition: target.position,
            damageAmount: damageAmount,
            target: target,
            anchor: target,
            isAbilityDamage: activeAbility);
        Feedback.OnStatusApplied(in context);
    }

    private WeaponFeedbackMode GetCurrentFeedbackMode() =>
        Runtime != null && Runtime.State == WeaponState.Manual
            ? WeaponFeedbackMode.Manual
            : WeaponFeedbackMode.Automatic;

    private WeaponFeedbackContext CreateFeedbackContext(
        WeaponFeedbackMode mode,
        Vector3 origin,
        Vector3 direction,
        Vector3 impactPosition = default,
        int damageAmount = 0,
        Transform target = null,
        Transform anchor = null,
        float explosionRadius = 0f,
        bool isAbilityDamage = false)
    {
        return new WeaponFeedbackContext(
            Runtime,
            mode,
            Heat != null ? Heat.NormalizedHeat : 0f,
            origin,
            direction,
            impactPosition: impactPosition,
            impactNormal: direction.sqrMagnitude > 0.0001f ? -direction.normalized : Vector3.back,
            damageAmount: damageAmount,
            isAbilityDamage: isAbilityDamage,
            targetClass: WeaponEnemyClassifier.GetKind(target),
            surfaceType: target != null ? ImpactSurfaceType.EnemyOrganic : ImpactSurfaceType.Default,
            explosionRadius: explosionRadius,
            eventIntensity: 1f,
            target: target,
            anchor: anchor);
    }

    private float GetScaledRange(float range)
    {
        return Mathf.Max(0f, range) * GetAreaSizeMultiplier();
    }

    private float GetScaledHoseRadius(FlamethrowerTuning tuning)
    {
        return Mathf.Max(0.05f, tuning.FlameHoseRadius) * GetAreaSizeMultiplier();
    }

    private float GetPathAdjustedBurnDuration(FlamethrowerTuning tuning)
    {
        float duration = tuning.FlameBurnDuration;
        if (IsJellifiedFuelPath())
            duration *= 2f;
        return duration;
    }

    private Vector2 GetJellifiedActivePuddleSettings(FlamethrowerTuning tuning, float activeRadius)
    {
        float levelScale = GetJellifiedFuelLevelScale();
        float radius = Mathf.Max(0.1f, activeRadius * 0.5f) * levelScale;
        float duration = Mathf.Max(0.1f, tuning.FlameBurnDuration * 2f) * levelScale;
        return new Vector2(radius, duration);
    }

    private float GetJellifiedFuelLevelScale()
    {
        return Runtime != null ? Mathf.Max(1f, Runtime.Level / 6f) : 1f;
    }

    private float GetPathAdjustedActiveRadius(FlamethrowerTuning tuning)
    {
        float radius = tuning.FlameActiveRadius;
        if (IsJellifiedFuelPath())
            radius *= 1.2f;
        if (IsLiquidNitrogenPath())
            radius *= 0.9f;
        return radius;
    }

    private Color GetStreamCoreColor()
    {
        if (IsJellifiedFuelPath())
            return JellifiedFuelCoreColor;
        if (IsLiquidNitrogenPath())
            return LiquidNitrogenCoreColor;
        return BaseFlameCoreColor;
    }

    private Color GetStreamEdgeColor()
    {
        if (IsJellifiedFuelPath())
            return JellifiedFuelVfxColor;
        if (IsLiquidNitrogenPath())
            return LiquidNitrogenVfxColor;
        return BaseFlameEdgeColor;
    }

    private void ResetManualDebug(Vector3 aimDirection, bool isFiring)
    {
        LastManualDebugSummary = "Tick start";
        LastManualHitCount = 0;
        LastManualDamageApplications = 0;
        LastManualPointCount = 0;
        LastManualRegistryCount = EnemyRegistry.ActiveCount;
        LastManualHoseRadius = 0f;
        LastManualRange = 0f;
        LastManualAmmoBefore = Runtime != null ? Runtime.CurrentAmmo : 0f;
        LastManualAmmoAfter = LastManualAmmoBefore;
        LastManualFireHeld = isFiring;
        LastManualAimDirection = aimDirection;
    }
}

internal sealed class FlamethrowerHoseStream
{
    private const int MaxSegmentCount = 48;

    private Vector3[] _points;
    private bool _initialized;
    private float _lastUpdateTime;
    private int _lastPointCount;

    public Vector3[] Points => _points;
    public int PointCount { get; private set; }

    public void Update(Vector3 origin, Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime)
    {
        int pointCount = Mathf.Clamp(tuning.FlameHoseSegmentCount, 2, MaxSegmentCount);
        EnsureCapacity(pointCount);

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

        direction.Normalize();
        range = Mathf.Max(0.01f, range);
        deltaTime = Mathf.Clamp(deltaTime, 0.001f, 0.05f);

        bool shouldReset = !_initialized
            || _lastPointCount != pointCount
            || Time.time - _lastUpdateTime > Mathf.Max(0.05f, tuning.FlameVisualDuration * 1.25f);

        if (shouldReset)
            Initialize(origin, direction, range, pointCount);
        else
            Simulate(origin, direction, range, tuning, deltaTime, pointCount);

        PointCount = pointCount;
        _lastPointCount = pointCount;
        _lastUpdateTime = Time.time;
        _initialized = true;
    }

    private void EnsureCapacity(int pointCount)
    {
        if (_points == null || _points.Length < pointCount)
            _points = new Vector3[pointCount];
    }

    private void Initialize(Vector3 origin, Vector3 direction, float range, int pointCount)
    {
        for (int i = 0; i < pointCount; i++)
        {
            float t = pointCount == 1 ? 0f : i / (float)(pointCount - 1);
            _points[i] = origin + direction * (range * t);
        }
    }

    private void Simulate(Vector3 origin, Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime, int pointCount)
    {
        _points[0] = origin;

        float nearFollow = Mathf.Max(0.01f, tuning.FlameHoseNearFollow);
        float farFollow = Mathf.Max(0.01f, tuning.FlameHoseFarFollow);
        float turbulence = Mathf.Max(0f, tuning.FlameHoseTurbulence);
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(Vector3.forward, direction);
        side.Normalize();

        Vector3 vertical = Vector3.Cross(direction, side).normalized;

        for (int i = 1; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            Vector3 desired = origin + direction * (range * t);
            float response = Mathf.Lerp(nearFollow, farFollow, Mathf.Pow(t, 1.35f));
            float follow = 1f - Mathf.Exp(-response * deltaTime);
            _points[i] = Vector3.Lerp(_points[i], desired, follow);

            if (turbulence <= 0f)
                continue;

            float wave = Mathf.Sin(Time.time * 9.5f + i * 1.73f);
            float ripple = Mathf.Sin(Time.time * 6.2f + i * 2.19f);
            float weight = Mathf.Sin(t * Mathf.PI);
            _points[i] += (side * wave + vertical * ripple) * (turbulence * weight * deltaTime);
        }
    }
}
