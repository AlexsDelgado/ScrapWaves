using System.Collections.Generic;
using UnityEngine;

public sealed class FlamethrowerWeapon : BasicProjectileWeapon
{
    protected override void CollectDiagnostics(List<WeaponDiagnosticSection> sections)
    {
        FlamethrowerTuning t = Runtime.Data.Flamethrower;
        var automatic = DiagnosticMode("Automatic", 1f, GetAutomaticTickInterval(t), GetScaledRange(Runtime.Data.BaseRange), 1, 0f, knockbackScale: 0f);
        var manual = DiagnosticMode("Manual", 1f, Mathf.Max(0.01f, t.FlameManualTickInterval), GetManualRange(t), 1,
            t.FlameManualAmmoPerSecond * Mathf.Max(0.01f, t.FlameManualTickInterval), knockbackScale: t.FlameManualKnockbackScale);
        float radius = GetScaledRange(GetPathAdjustedActiveRadius(t));
        var active = DiagnosticMode("Active Ability", t.FlameActiveDamageScale, 0f, radius, 1,
            Runtime.Data.ActiveAbilityAmmoCost, true, t.FlameActiveKnockbackScale);
        automatic.Add("Cone angle", t.FlameAutoConeAngle, " degrees");
        manual.Add("Active areas", ActiveManualAreaCount).Add("Emission interval", Mathf.Max(0.01f, t.FlameAreaEmissionInterval), " s")
            .Add("Area lifetime", t.FlameAreaLifetime, " s").Add("Time to stop", t.FlameAreaTimeToStop, " s")
            .Add("Initial radius", t.FlameAreaInitialRadius * GetAreaSizeMultiplier(), " m")
            .Add("Final radius", Mathf.Max(t.FlameAreaInitialRadius, t.FlameAreaFinalRadius) * GetAreaSizeMultiplier(), " m")
            .Add("Damage timing", "One shared tick; one contact hit per enemy across all areas")
            .Add("Ammo / second", t.FlameManualAmmoPerSecond)
            .Add("Ammo timing", "Continuous drain while held; ammo/action is per tick duration");
        active.Add("Explosion radius", radius, " m");
        foreach (var section in new[] { automatic, manual, active })
        {
            section.Add("Maximum targets / tick", Mathf.Max(1, t.FlameMaxTargetsPerTick));
            if (section == automatic && !ShouldApplyAutomaticStatus()) section.Add("Status", "No burn in base automatic mode");
            else if (IsLiquidNitrogenPath()) section.Add("Status", section == active
                ? "Freeze 2 s, then slowed movement (0.1x for 4 s total); replaces burn"
                : "Movement ramps from 0.5x to 0.1x over 6 hits, lasts 3 s; replaces burn");
            else
            {
                if (Stats != null && Stats.GetDefinition(StatType.DamageMultiplier) != null
                    && Stats.GetDefinition(StatType.EliteDamageMultiplier) != null)
                    section.Add("Burn damage / tick (non-critical)", CreateBurnDamageContext(t, section == active).EstimateDamage(false));
                section.Add("Burn duration", GetPathAdjustedBurnDuration(t), " s").Add("Burn interval", t.FlameBurnTickInterval, " s");
            }
        }
        if (IsJellifiedFuelPath())
        {
            Vector2 puddle = GetJellifiedActivePuddleSettings(t, radius);
            active.Add("Fuel puddle radius", puddle.x, " m").Add("Fuel puddle duration", puddle.y, " s");
        }
        sections.Add(automatic); sections.Add(manual); sections.Add(active);
    }

    private static readonly Color BaseFlameCoreColor = new(1f, 0.75f, 0.15f, 0.95f);
    private static readonly Color BaseFlameEdgeColor = new(1f, 0.18f, 0.02f, 0.75f);
    private static readonly Color JellifiedFuelCoreColor = new(0.08f, 0.32f, 0.09f, 0.92f);
    private static readonly Color JellifiedFuelVfxColor = new(0.02f, 0.16f, 0.04f, 0.9f);
    private static readonly Color LiquidNitrogenCoreColor = new(0.78f, 0.97f, 1f, 0.95f);
    private static readonly Color LiquidNitrogenVfxColor = new(0.38f, 0.78f, 1f, 0.88f);

    private readonly List<Transform> _targets = new();

    private FlamethrowerManualAreas _manualAreas;

    private FlamethrowerStreamVfx _streamVfx;
    private float _autoTickTimer;
    private bool _applyingManualAreaDamage;
    private bool _sustainedFeedbackActive;
    private WeaponFeedbackMode _sustainedFeedbackMode;
    private WeaponUpgradePath _sustainedFeedbackPath;

    public string LastManualDebugSummary { get; private set; } = "No manual tick yet";
    public int LastManualHitCount => _manualAreas != null ? _manualAreas.LastTargetCount : 0;
    public int LastManualDamageApplications => _manualAreas != null ? _manualAreas.LastAppliedCount : 0;
    public int ActiveManualAreaCount => _manualAreas != null ? _manualAreas.ActiveCount : 0;
    public int LastManualRegistryCount { get; private set; }
    public float LastManualAreaRadius { get; private set; }
    public float LastManualRange { get; private set; }
    public float LastManualAmmoBefore { get; private set; }
    public float LastManualAmmoAfter { get; private set; }
    public bool LastManualFireHeld { get; private set; }
    public Vector3 LastManualAimDirection { get; private set; }

    public FlamethrowerWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn, PlayerMovement movement)
        : base(targeting, pool, spawn)
    {
    }

    // Emits an automatic cone from its assigned arm nozzle in player-body forward.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
        {
            StopSustainedFeedback(GetAutomaticFlameDirection());
            return;
        }

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        Vector3 flameDirection = GetAutomaticFlameDirection();
        AimFireOriginAlong(flameDirection);
        float range = GetScaledRange(Runtime.Data.BaseRange);
        ShowAutomaticCone(flameDirection, range, tuning);
        UpdateSustainedFeedback(WeaponFeedbackMode.Automatic, flameDirection, range);

        _autoTickTimer -= deltaTime;
        if (_autoTickTimer > 0f)
            return;

        ApplyAutomaticConeDamage(flameDirection, range, tuning);
        _autoTickTimer = GetAutomaticTickInterval(tuning);
    }

    // Only emission follows the muzzle. Existing areas update in their world-space controller.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        ResetManualDebug(aimDirection, isFiring);
        if (deltaTime <= 0f || Time.timeScale <= 0f || GameplayPause.IsUiPaused) return;
        if (Runtime.State != WeaponState.Manual || !isFiring || aimDirection.sqrMagnitude < 0.0001f || Spawn == null)
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = "Emission stopped; existing areas keep their lifetime";
            return;
        }
        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        float consumption = Mathf.Max(0f, tuning.FlameManualAmmoPerSecond);
        float ammoCost = Mathf.Min(Runtime.CurrentAmmo, consumption * deltaTime);
        float firingTime = consumption > 0f ? ammoCost / consumption : deltaTime;
        if (firingTime <= 0f || !TrySpendManualAmmo(ammoCost, requireFullAmount: false))
        {
            StopSustainedFeedback(aimDirection);
            LastManualDebugSummary = "No ammo; existing areas keep their lifetime";
            return;
        }
        if (_manualAreas == null)
        {
            GameObject root = new("[Flamethrower Manual Areas]");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, Owner.gameObject.scene);
            _manualAreas = root.AddComponent<FlamethrowerManualAreas>();
            _manualAreas.Initialize(Owner, ApplyManualAreaDamage, GetPresentationSettings()?.ManualAreaMaterial);
        }
        LastManualAmmoAfter = Runtime.CurrentAmmo;
        LastManualRange = GetManualRange(tuning);
        LastManualAreaRadius = Mathf.Max(tuning.FlameAreaInitialRadius, tuning.FlameAreaFinalRadius) * GetAreaSizeMultiplier();
        UpdateSustainedFeedback(WeaponFeedbackMode.Manual, aimDirection, LastManualRange);
        _manualAreas.Emit(firingTime, Spawn.position, aimDirection, LastManualRange, GetAreaSizeMultiplier(),
            tuning, GetStreamStyle(), Heat != null ? Heat.NormalizedHeat : 0f);
        if (firingTime < deltaTime) StopSustainedFeedback(aimDirection);
        LastManualDebugSummary = $"Areas {ActiveManualAreaCount} | Hits {LastManualHitCount} | Applied {LastManualDamageApplications}";
    }

    public FlamethrowerManualAreas ManualAreas => _manualAreas;

    public void ClearManualAreas()
    {
        if (_manualAreas == null) return;
        _manualAreas.Clear();
        if (Application.isPlaying) Object.Destroy(_manualAreas.gameObject);
        else Object.DestroyImmediate(_manualAreas.gameObject);
        _manualAreas = null;
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
            int damage = CalculateDirectDamage(
                tuning.FlameActiveDamageScale,
                _targets[i],
                true,
                out WeaponDamageContext directContext);
            int burnDamage = CalculateBurnDamage(
                tuning,
                _targets[i],
                true,
                out WeaponDamageContext burnContext);
            ApplyDamageToTarget(
                _targets[i],
                damage,
                Owner.position,
                tuning.FlameActiveKnockbackScale,
                true,
                in directContext);
            ApplyBurnToTargetWithContext(_targets[i], burnDamage, tuning, true, in burnContext);
        }

        if (IsJellifiedFuelPath())
        {
            Vector2 puddleSettings = GetJellifiedActivePuddleSettings(tuning, activeRadius);
            int puddleDamage = CalculateBurnDamage(tuning, null, true, out _);
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

    // Automatic flame is body-forward. Camera-relative movement must not redirect the shoulder nozzle.
    private Vector3 GetAutomaticFlameDirection()
    {
        Vector3 direction = Owner != null ? Owner.forward : Vector3.forward;
        direction.y = 0f;

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
            int damage = CalculateDirectDamage(1f, _targets[i], false, out WeaponDamageContext directContext);
            if (ApplyDamageToTarget(_targets[i], damage, origin, 0f, false, in directContext)
                && ShouldApplyAutomaticStatus())
            {
                int burnDamage = CalculateBurnDamage(tuning, _targets[i], false, out WeaponDamageContext burnContext);
                ApplyBurnToTargetWithContext(_targets[i], burnDamage, tuning, false, in burnContext);
            }
        }

        return hitCount;
    }

    private bool ApplyManualAreaDamage(Transform target, Vector3 origin)
    {
        _applyingManualAreaDamage = true;
        try
        {
            FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
            int damage = CalculateDirectDamage(1f, target, false, out WeaponDamageContext directContext);
            bool applied = ApplyDamageToTarget(target, damage, origin, tuning.FlameManualKnockbackScale, false, in directContext);
            if (applied && target != null && target.gameObject.activeInHierarchy)
            {
                int burn = CalculateBurnDamage(tuning, target, false, out WeaponDamageContext burnContext);
                ApplyBurnToTargetWithContext(target, burn, tuning, false, in burnContext);
            }
            return applied;
        }
        finally { _applyingManualAreaDamage = false; }
    }
    // Calculates one direct flamethrower damage tick from shared weapon rules.
    private int CalculateDirectDamage(
        float damageScale,
        Transform target,
        bool isAbilityDamage,
        out WeaponDamageContext damageContext)
    {
        damageContext = CreateDamageContext(damageScale, isAbilityDamage)
            .WithFeedbackMetadata(
                0,
                isAbilityDamage ? DamageFeedbackKind.Ability : DamageFeedbackKind.SustainedContact);
        return target != null
            ? damageContext.CalculateDamage(target)
            : damageContext.EstimateDamage(false);
    }

    // Calculates burn damage separately so damage-over-time does not roll critical hits.
    private int CalculateBurnDamage(
        FlamethrowerTuning tuning,
        Transform target,
        bool isAbilityDamage,
        out WeaponDamageContext damageContext)
    {
        float pathScale = IsJellifiedFuelPath() ? 1.35f : 1f;
        WeaponStatusKind statusKind = IsJellifiedFuelPath()
            ? WeaponStatusKind.JellifiedBurn
            : WeaponStatusKind.Burn;
        DamageFeedbackKind damageKind = statusKind == WeaponStatusKind.JellifiedBurn
            ? DamageFeedbackKind.JellifiedBurn
            : DamageFeedbackKind.Burn;
        damageContext = new WeaponDamageContext(
                Stats,
                Runtime,
                canCrit: false,
                critMultiplierOverride: 1f,
                damageScale: Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale,
                isAbilityDamage: isAbilityDamage,
                knockbackScale: 0f)
            .WithFeedbackMetadata(0, damageKind, statusKind: statusKind);
        return target != null
            ? damageContext.CalculateDamage(target)
            : damageContext.EstimateDamage(false);
    }

    private WeaponDamageContext CreateBurnDamageContext(FlamethrowerTuning tuning, bool isAbilityDamage)
    {
        float pathScale = IsJellifiedFuelPath() ? 1.35f : 1f;
        WeaponStatusKind statusKind = IsJellifiedFuelPath()
            ? WeaponStatusKind.JellifiedBurn
            : WeaponStatusKind.Burn;
        DamageFeedbackKind damageKind = statusKind == WeaponStatusKind.JellifiedBurn
            ? DamageFeedbackKind.JellifiedBurn
            : DamageFeedbackKind.Burn;
        return new WeaponDamageContext(
            Stats,
            Runtime,
            canCrit: false,
            critMultiplierOverride: 1f,
            damageScale: Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale,
            isAbilityDamage: isAbilityDamage,
            knockbackScale: 0f).WithFeedbackMetadata(0, damageKind, statusKind: statusKind);
    }

    private bool ShouldApplyAutomaticStatus() => IsJellifiedFuelPath() || IsLiquidNitrogenPath();

    private bool IsJellifiedFuelPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsLiquidNitrogenPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    // Applies immediate damage to one enemy transform if it has a damage receiver.
    private bool ApplyDamageToTarget(
        Transform target,
        int damage,
        Vector3 impactOrigin,
        float knockbackScale,
        bool activeAbility,
        in WeaponDamageContext damageContext)
    {
        if (target == null)
            return false;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        string weaponId = Runtime?.Data != null ? Runtime.Data.WeaponId : null;
        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            damageable,
            damage,
            sourceWeaponId: weaponId);
        if (damageable != null && result.Applied)
        {
            ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);
            Vector3 direction = target.position - impactOrigin;
            WeaponFeedbackContext feedback = CreateFeedbackContext(
                activeAbility ? WeaponFeedbackMode.Active : GetCurrentFeedbackMode(),
                impactOrigin,
                direction,
                impactPosition: target.position,
                damageAmount: result.AppliedDamage,
                target: target,
                anchor: target,
                isAbilityDamage: activeAbility);
            feedback = feedback.WithImpact(
                target.position,
                direction.sqrMagnitude > 0.0001f ? -direction.normalized : Vector3.back,
                result.AppliedDamage,
                damageContext.IsCritical,
                false,
                result.Killed,
                target,
                WeaponEnemyClassifier.GetKind(target),
                ImpactSurfaceType.EnemyOrganic);
            feedback = feedback.WithDamageMetadata(
                damageContext.ReferenceDamage,
                damageContext.ActionSequenceId,
                damageContext.DamageKind);
            if (result.IsAuthoritative && result.AppliedDamage > 0)
                Feedback.OnDamageConfirmed(in feedback);
            return true;
        }

        return false;
    }

    // Refreshes a simple burn component on the target's damage receiver.
    private void ApplyBurnToTarget(
        Transform target,
        int damagePerTick,
        FlamethrowerTuning tuning,
        bool activeAbility)
    {
        WeaponDamageContext burnContext = CreateBurnDamageContext(tuning, activeAbility);
        ApplyBurnToTargetWithContext(target, damagePerTick, tuning, activeAbility, in burnContext);
    }

    private void ApplyBurnToTargetWithContext(
        Transform target,
        int damagePerTick,
        FlamethrowerTuning tuning,
        bool activeAbility,
        in WeaponDamageContext burnContext)
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
        WeaponStatusKind statusKind = IsJellifiedFuelPath()
            ? WeaponStatusKind.JellifiedBurn
            : WeaponStatusKind.Burn;
        StatusDamageSource source = new(
            Runtime,
            Feedback,
            activeAbility ? WeaponFeedbackMode.Active : GetCurrentFeedbackMode(),
            Runtime != null && Runtime.HasAdvancedPath
                ? Runtime.SelectedPath
                : WeaponUpgradePath.None,
            burnContext.ReferenceDamage,
            statusInstanceId: 0,
            statusKind: statusKind,
            isAbilityDamage: activeAbility);
        burn.Refresh(damageable, damagePerTick, duration, tuning.FlameBurnTickInterval,
            statusKind, in source);

        if (IsJellifiedFuelPath())
        {
            float levelScale = Runtime != null ? Mathf.Max(1f, Runtime.Level / 6f) : 1f;
            float radius = GetScaledFuelPuddleRadius(tuning) * levelScale;
            SpawnFuelPuddle(
                target.position,
                radius,
                damagePerTick,
                duration,
                tuning.FlameBurnTickInterval,
                CreateBurnDamageContext(tuning, activeAbility));
        }

        WeaponDummyEnemy dummy = damageComponent.GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus("Burn", duration);

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
        WeaponFeedbackMode feedbackMode = damageContext.IsAbilityDamage
            ? WeaponFeedbackMode.Active
            : GetCurrentFeedbackMode();
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
                damageContext,
                Feedback,
                feedbackMode);
            return;
        }

        FlamethrowerFuelPuddle.SpawnWithContext(
            position,
            radius,
            damagePerTick,
            duration,
            tickInterval,
            damageContext,
            Feedback,
            feedbackMode);
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
        _manualAreas?.StopEmission();
        if (_sustainedFeedbackMode == WeaponFeedbackMode.Manual)
            _streamVfx?.ReleaseManual();
        else if (_sustainedFeedbackMode == WeaponFeedbackMode.Automatic)
            _streamVfx?.ReleaseAutomatic();

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
        _applyingManualAreaDamage || (Runtime != null && Runtime.State == WeaponState.Manual)
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

    private float GetScaledFuelPuddleRadius(FlamethrowerTuning tuning)
    {
        return Mathf.Max(0.05f, tuning.FlameFuelPuddleRadius) * GetAreaSizeMultiplier();
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
        LastManualRegistryCount = EnemyRegistry.ActiveCount;
        LastManualAreaRadius = 0f;
        LastManualRange = 0f;
        LastManualAmmoBefore = Runtime != null ? Runtime.CurrentAmmo : 0f;
        LastManualAmmoAfter = LastManualAmmoBefore;
        LastManualFireHeld = isFiring;
        LastManualAimDirection = aimDirection;
    }
}
