using UnityEngine;

public sealed class MortarWeapon : BasicProjectileWeapon, IMortarReticleStatus
{
    public override float? GetManualAimConvergenceDistance()
    {
        if (Runtime?.Data == null) return null;
        WeaponResolvedStat range = WeaponStatAuthoringResolver.ResolveModeValue(
            Runtime, WeaponStatMode.Manual, WeaponStatField.RangeMeters, Runtime.Data.BaseRange);
        return range.IsAuthored ? range.Value : null;
    }

    public override WeaponGeometrySnapshot CaptureGeometry()
    {
        if (Runtime?.Data == null) return null;
        MortarTuning tuning = Runtime.Data.Mortar;
        float automaticRange = GetModeRange(WeaponStatMode.Automatic);
        float manualRange = ManualRange;
        bool grapeshot = IsGrapeshotPath();
        WeaponGeometryPurpose ordinaryPurpose = grapeshot ? WeaponGeometryPurpose.Targeting : WeaponGeometryPurpose.Explosion;
        WeaponGeometryShape ordinaryShape = grapeshot ? WeaponGeometryShape.Circle : WeaponGeometryShape.Sphere;
        return new WeaponGeometrySnapshot(
            new(WeaponStatMode.Automatic, WeaponGeometryPurpose.Targeting, WeaponGeometryShape.Circle,
                WeaponGeometryAnchor.Owner, radiusMeters: automaticRange),
            new(WeaponStatMode.Automatic, ordinaryPurpose, ordinaryShape, WeaponGeometryAnchor.Muzzle,
                radiusMeters: GetModeFootprintRadius(WeaponStatMode.Automatic, tuning.MortarAutoExplosionRadius),
                forwardOffsetMeters: automaticRange),
            new(WeaponStatMode.Manual, WeaponGeometryPurpose.ProjectilePath, WeaponGeometryShape.Arc,
                WeaponGeometryAnchor.Muzzle, rangeMeters: manualRange, arcHeightMeters: tuning.MortarArcHeight),
            new(WeaponStatMode.Manual, ordinaryPurpose, ordinaryShape, WeaponGeometryAnchor.Muzzle,
                radiusMeters: ManualFootprintRadius, forwardOffsetMeters: manualRange),
            new(WeaponStatMode.Ability, WeaponGeometryPurpose.Targeting, WeaponGeometryShape.Circle,
                WeaponGeometryAnchor.Muzzle, radiusMeters: GetBarrageRadius(tuning) * GetAreaSizeMultiplier(),
                forwardOffsetMeters: Runtime.Data.BaseRange),
            new(WeaponStatMode.Ability, WeaponGeometryPurpose.Explosion, WeaponGeometryShape.Sphere,
                WeaponGeometryAnchor.Muzzle,
                radiusMeters: GetExplosionRadius(WeaponStatMode.Ability, tuning.MortarActiveExplosionRadius) * GetAreaSizeMultiplier(),
                forwardOffsetMeters: Runtime.Data.BaseRange));
    }

    protected override void CollectDiagnostics(System.Collections.Generic.List<WeaponDiagnosticSection> sections)
    {
        MortarTuning t = Runtime.Data.Mortar;
        bool grapeshot = IsGrapeshotPath();
        float pelletScale = grapeshot ? GetUpgradePayloadForMode(false, WeaponStatMode.Automatic).GrapeshotDamageScale : 1f;
        var automatic = DiagnosticMode("Automatic", pelletScale, GetAutomaticFireInterval(), GetModeRange(WeaponStatMode.Automatic), 1, 0f,
            knockbackScale: grapeshot ? 0.2f : -1f);
        var manual = DiagnosticMode("Manual", pelletScale, GetManualFireInterval(), GetModeRange(WeaponStatMode.Manual), 1, 1f,
            knockbackScale: grapeshot ? 0.2f : -1f);
        int count = IsGrapeshotPath() ? GetGrapeshotRainShellCount(t) : GetActiveShellCount(t);
        var active = DiagnosticMode("Active Ability", 1f, 0f, Runtime.Data.BaseRange,
            count, WeaponMath.GetActiveAbilityAmmoCost(Runtime), true, weaponDamageScale: t.MortarActiveDamageScale);
        automatic.Add(IsGrapeshotPath() ? "Grapeshot target spread" : "Explosion radius",
                GetModeFootprintRadius(WeaponStatMode.Automatic, t.MortarAutoExplosionRadius), " m")
            .Add("Travel time", t.MortarShellTravelTime, " s").Add("Accuracy radius", t.MortarAutoAccuracyRadius, " m");
        manual.Add(IsGrapeshotPath() ? "Grapeshot target spread" : "Explosion radius", ManualFootprintRadius, " m")
            .Add("Travel time", GetManualTravelTime(t), " s")
            .Add("Accuracy radius", t.MortarManualAccuracyRadius, " m");
        active.Add("Explosion radius", GetExplosionRadius(WeaponStatMode.Ability, t.MortarActiveExplosionRadius) * GetAreaSizeMultiplier(), " m")
            .Add("Barrage radius", GetBarrageRadius(t) * GetAreaSizeMultiplier(), " m")
            .Add("First shell travel time", GetActiveShellTravelTime(t, 0), " s")
            .Add("Last shell travel time", GetActiveShellTravelTime(t, count - 1), " s");
        foreach (var section in new[] { automatic, manual, active })
        {
            section.Add("Explosion falloff", t.MortarExplosionFalloff);
            WeaponStatMode mode = section == automatic ? WeaponStatMode.Automatic
                : section == manual ? WeaponStatMode.Manual : WeaponStatMode.Ability;
            MortarUpgradePayload payload = GetUpgradePayloadForMode(section == active, mode);
            section.Add("Explosion repetitions", payload.RepeatExplosionCount)
                .Add("Repeat delay", payload.RepeatExplosionDelay, " s");
            if (payload.UseGrapeshot)
            {
                section.Add("Grapeshot count", payload.GrapeshotCount)
                    .Add("Grapeshot damage scale", payload.GrapeshotDamageScale, "x")
                    .Add("Damage unit", "Per pellet; parent shell deals no damage");
                if (Stats != null && Stats.GetDefinition(StatType.DamageMultiplier) != null
                    && Stats.GetDefinition(StatType.EliteDamageMultiplier) != null)
                {
                    var source = new WeaponDamageContext(Stats, Runtime, false, 1f, 1f, false, 1f, mode: mode);
                    section.Add("Source damage before split (non-critical)", source.EstimateDamage(false));
                }
            }
        }
        sections.Add(automatic); sections.Add(manual); sections.Add(active);
    }

    private const int ActiveShellPlacementAttempts = 4;
    private bool _presentationPoolPrepared;
    private readonly RaycastHit[] _barragePredictionHits = new RaycastHit[32];
    private readonly RaycastHit[] _presentationSupportHits = new RaycastHit[16];

    public float ManualRange => Runtime?.Data == null ? 0f : GetModeRange(WeaponStatMode.Manual);
    public float ManualExplosionRadius => Runtime?.Data == null
        ? 0f
        : GetExplosionRadius(WeaponStatMode.Manual, Runtime.Data.Mortar.MortarManualExplosionRadius) * GetAreaSizeMultiplier();
    public float ManualFootprintRadius => Runtime?.Data == null
        ? 0f
        : GetModeFootprintRadius(WeaponStatMode.Manual, Runtime.Data.Mortar.MortarManualExplosionRadius);
    public float ShellCollisionRadius => Runtime?.Data == null
        ? 0.01f
        : Runtime.Data.Mortar.MortarShellCollisionRadius * GetAreaSizeMultiplier();
    public float ManualTravelTime => Runtime?.Data == null
        ? 0.05f
        : GetManualTravelTime(Runtime.Data.Mortar);
    public float ArcHeight => Runtime?.Data == null
        ? 0f
        : Runtime.Data.Mortar.MortarArcHeight;

    public MortarWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        EnsurePresentationPool();

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        if (Owner == null)
            return;

        if (!EnemyRegistry.TryGetRandomOnPlaneWithinVerticalDelta(
                Owner.position,
                GetModeRange(WeaponStatMode.Automatic),
                ConfiguredEnemyTargeting.DefaultMaxAimVerticalDelta,
                out Transform target))
        {
            ClearFireOriginAim();
            return;
        }

        FireTimer = GetAutomaticFireInterval();
        Vector3 impact = EnemyRegistry.GetAimPoint(target) + RandomPlanarOffset(tuning.MortarAutoAccuracyRadius);
        AimFireOriginAt(null, impact);
        Vector3 launch = Spawn != null ? Spawn.position : Owner.position;
        EmitLaunchFeedback(
            WeaponFeedbackMode.Automatic,
            launch,
            impact,
            GetExplosionRadius(WeaponStatMode.Automatic, tuning.MortarAutoExplosionRadius) * GetAreaSizeMultiplier(),
            false);
        FireShell(
            launch,
            impact,
            1f,
            tuning.MortarAutoExplosionRadius,
            tuning.MortarExplosionFalloff,
            tuning.MortarShellTravelTime,
            tuning.MortarArcHeight,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target),
            WeaponFeedbackMode.Automatic);
    }

    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        EnsurePresentationPool();

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        FireTimer = GetManualFireInterval();
        Vector3 impact = Spawn.position + aimDirection.normalized * GetModeRange(WeaponStatMode.Manual);
        impact += RandomPlanarOffset(tuning.MortarManualAccuracyRadius);
        EmitLaunchFeedback(
            WeaponFeedbackMode.Manual,
            Spawn.position,
            impact,
            GetExplosionRadius(WeaponStatMode.Manual, tuning.MortarManualExplosionRadius) * GetAreaSizeMultiplier(),
            false);
        FireShell(
            Spawn.position,
            impact,
            1f,
            tuning.MortarManualExplosionRadius,
            tuning.MortarExplosionFalloff,
            GetManualTravelTime(tuning),
            tuning.MortarArcHeight,
            false,
            WeaponFeedbackMode.Manual);
    }

    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        EnsurePresentationPool();

        if (Spawn == null)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        Vector3 aimedCenter = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        if (!TryResolveActiveBarrageSurface(
                aimedCenter,
                tuning,
                out Vector3 center,
                out Vector3 presentationNormal))
        {
            return;
        }

        if (!TrySpendManualAmmo(WeaponMath.GetActiveAbilityAmmoCost(Runtime), requireFullAmount: false))
            return;

        int shellCount = IsGrapeshotPath() ? GetGrapeshotRainShellCount(tuning) : GetActiveShellCount(tuning);
        float barrageRadius = GetBarrageRadius(tuning) * GetAreaSizeMultiplier();
        EmitLaunchFeedback(
            WeaponFeedbackMode.Active,
            Spawn.position,
            center,
            barrageRadius,
            true,
            presentationNormal);
        int shellsPerSubVolley = GetActiveDamageFeedbackSubVolleyShellCount();
        int subVolleySequenceId = 0;
        MortarUpgradePayload activePayload = GetUpgradePayload(activeAbility: true);
        int contributorsPerShell = GetDamageFeedbackContributorCount(activePayload);
        for (int i = 0; i < shellCount; i++)
        {
            if (i % shellsPerSubVolley == 0)
            {
                int subVolleyShellCount = Mathf.Min(shellsPerSubVolley, shellCount - i);
                subVolleySequenceId = DamageFeedbackSequenceRuntime.BeginSequence(
                    DamageFeedbackKind.Explosion,
                    subVolleyShellCount * contributorsPerShell);
            }

            Vector3 impact = ResolveActiveShellImpact(center, barrageRadius, tuning);
            Vector3 dropStart = impact + Vector3.up * Mathf.Max(0f, tuning.MortarActiveDropHeight);
            FireShell(
                dropStart,
                impact,
                tuning.MortarActiveDamageScale,
                tuning.MortarActiveExplosionRadius,
                tuning.MortarExplosionFalloff,
                GetActiveShellTravelTime(tuning, i),
                0f,
                false,
                WeaponFeedbackMode.Active,
                activeAbility: true,
                isAbilityDamage: true,
                shellIndex: i,
                shellCount: shellCount,
                actionSequenceId: subVolleySequenceId,
                manageSequenceLifecycle: false);

            bool subVolleyComplete = i + 1 >= shellCount || (i + 1) % shellsPerSubVolley == 0;
            if (subVolleyComplete)
                DamageFeedbackSequenceRuntime.CompleteSequence(subVolleySequenceId);
        }

        CompleteActiveAbility();
    }

    public override bool CanCrit() => WeaponStatAuthoringResolver.SupportsCriticalHits(Runtime);

    protected override float GetHeatFireRateMultiplier()
    {
        if (Heat == null || Heat.NormalizedHeat <= 0.5f)
            return 1f;

        float heatOverHalf = Mathf.InverseLerp(0.5f, 1f, Heat.NormalizedHeat);
        return 1f + heatOverHalf * Mathf.Max(0f, Runtime.Data.Mortar.MortarHeatFireRateBonusAbove50);
    }

    private WeaponMortarStatOverrides Authored => WeaponStatAuthoringResolver.GetLevelRow(Runtime)?.Mortar;

    private float GetModeRange(WeaponStatMode mode) => WeaponStatAuthoringResolver.ResolveModeValue(
        Runtime, mode, WeaponStatField.RangeMeters, Runtime.Data.BaseRange).Value;

    private float GetExplosionRadius(WeaponStatMode mode, float legacyRadius) => WeaponStatAuthoringResolver.ResolveModeValue(
        Runtime, mode, WeaponStatField.ExplosionRadiusMeters, legacyRadius).Value;

    private float GetBarrageRadius(MortarTuning tuning) => WeaponStatAuthoringResolver.ResolveOptional(
        Runtime, Authored?.BarrageRadiusMeters ?? default, tuning.MortarBarrageRadius).Value;

    private float? GetAuthoredGrapeshotLandingSpread(WeaponStatMode mode)
    {
        WeaponOptionalFloat field = mode == WeaponStatMode.Automatic
            ? Authored?.GrapeshotAutomaticLandingSpreadMeters ?? default
            : Authored?.GrapeshotManualLandingSpreadMeters ?? default;
        WeaponResolvedStat spread = WeaponStatAuthoringResolver.ResolveOptional(Runtime, field, 0f);
        return spread.IsAuthored ? spread.Value * GetAreaSizeMultiplier() : null;
    }

    private float GetModeFootprintRadius(WeaponStatMode mode, float legacyRadius)
    {
        float radius = GetExplosionRadius(mode, legacyRadius) * GetAreaSizeMultiplier();
        return IsGrapeshotPath() && mode != WeaponStatMode.Ability
            ? GetAuthoredGrapeshotLandingSpread(mode) ?? Mathf.Max(0.75f, radius * 1.5f)
            : radius;
    }

    private float GetAutomaticFireInterval()
    {
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Automatic,
            WeaponStatField.AttackCyclesPerSecond, Runtime.Data.BaseAttackRate * WeaponMath.GetAttackRateMultiplier(Runtime));
        return rate.IsAuthored
            ? 1f / (rate.Value * WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier) * GetHeatFireRateMultiplier())
            : GetFireIntervalWithoutHeat(WeaponStatMode.Automatic) / Mathf.Max(0.01f, GetHeatFireRateMultiplier());
    }

    private float GetManualFireInterval()
    {
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.AttackCyclesPerSecond, Runtime.Data.BaseAttackRate * WeaponMath.GetAttackRateMultiplier(Runtime));
        return rate.IsAuthored
            ? 1f / (rate.Value * WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier))
            : GetFireIntervalWithoutHeat(WeaponStatMode.Manual);
    }

    private float GetManualTravelTime(MortarTuning tuning)
    {
        float speedBonus = Heat != null ? 1f + Heat.NormalizedHeat * Mathf.Max(0f, tuning.MortarHeatManualSpeedBonus) : 1f;
        return Mathf.Max(0.05f, tuning.MortarManualTravelTime / speedBonus);
    }

    private int GetActiveShellCount(MortarTuning tuning)
    {
        float heatShells = WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.AbilityHeatShellBonus ?? default, 10f).Value;
        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * heatShells) : 0;
        int count = WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.ActiveShellCount ?? default, 5).Value;
        return Mathf.Max(0, count + heatBonus);
    }

    private MortarUpgradePayload GetUpgradePayload(bool activeAbility) => GetUpgradePayloadForMode(
        activeAbility, WeaponStatAuthoringResolver.GetImmediateMode(Runtime, activeAbility));

    private MortarUpgradePayload GetUpgradePayloadForMode(bool activeAbility, WeaponStatMode mode)
    {
        if (Runtime == null || !Runtime.HasAdvancedPath)
            return MortarUpgradePayload.None;

        if (Runtime.SelectedPath == WeaponUpgradePath.PathA)
        {
            if (activeAbility)
                return MortarUpgradePayload.None;

            return new MortarUpgradePayload(true,
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.GrapeshotPelletCount ?? default, 15).Value,
                70f, // Legacy unused cone payload is retained only for compatibility.
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.GrapeshotDamageMultiplier ?? default, 0.5f).Value,
                1, 0f, GetAuthoredGrapeshotLandingSpread(mode));
        }

        if (Runtime.SelectedPath == WeaponUpgradePath.PathB)
            return new MortarUpgradePayload(false, 0, 0f, 0f,
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.MultiChargedTotalExplosions ?? default, 3).Value,
                WeaponStatAuthoringResolver.ResolveOptional(Runtime, Authored?.MultiChargedRepeatDelaySeconds ?? default, 2f).Value);

        return MortarUpgradePayload.None;
    }

    private bool IsGrapeshotPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsMultiChargedPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    private int GetGrapeshotRainShellCount(MortarTuning tuning)
    {
        float rainDurationSeconds = GetGrapeshotRainDuration();
        float maximumHeatRate = WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            Authored?.GrapeshotAbilityHeatProjectilesPerSecond ?? default, 10f).Value;
        int heatBonusPerSecond = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * maximumHeatRate) : 0;
        float projectilesPerSecond = WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            Authored?.GrapeshotAbilityProjectilesPerSecond ?? default, 10f).Value + heatBonusPerSecond;
        return Mathf.Max(0, Mathf.RoundToInt(projectilesPerSecond * rainDurationSeconds));
    }

    private float GetGrapeshotRainDuration() => WeaponStatAuthoringResolver.ResolveOptional(Runtime,
        Authored?.GrapeshotAbilityDurationSeconds ?? default, 5f).Value;

    private float GetActiveShellTravelTime(MortarTuning tuning, int shellIndex)
    {
        float baseTravelTime = Mathf.Max(0.05f, tuning.MortarActiveTravelTime);
        if (IsMultiChargedPath())
            return baseTravelTime;

        if (IsGrapeshotPath())
        {
            float rainDurationSeconds = GetGrapeshotRainDuration();
            int shellCount = Mathf.Max(1, GetGrapeshotRainShellCount(tuning));
            return baseTravelTime + Mathf.Max(0, shellIndex) * (rainDurationSeconds / shellCount);
        }

        return baseTravelTime + Mathf.Max(0, shellIndex) * WeaponStatAuthoringResolver.ResolveOptional(Runtime,
            Authored?.BasicAbilityShellSpacingSeconds ?? default, 0.08f).Value;
    }

    private bool FireShell(
        Vector3 launchPosition,
        Vector3 impactPosition,
        float damageScale,
        float explosionRadius,
        float falloff,
        float travelTime,
        float arcHeight,
        bool eliteOrBoss,
        WeaponFeedbackMode mode,
        bool activeAbility = false,
        bool isAbilityDamage = false,
        int shellIndex = 0,
        int shellCount = 1,
        int actionSequenceId = 0,
        bool manageSequenceLifecycle = true)
    {
        MortarTuning tuning = Runtime.Data.Mortar;
        float area = GetAreaSizeMultiplier();
        WeaponStatMode statMode = mode == WeaponFeedbackMode.Active ? WeaponStatMode.Ability
            : mode == WeaponFeedbackMode.Manual ? WeaponStatMode.Manual : WeaponStatMode.Automatic;
        MortarUpgradePayload payload = GetUpgradePayloadForMode(activeAbility, statMode);
        int contributorCount = GetDamageFeedbackContributorCount(payload);
        int sequenceId = manageSequenceLifecycle
            ? DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Explosion, contributorCount)
            : actionSequenceId;
        explosionRadius = GetExplosionRadius(statMode, explosionRadius);
        WeaponDamageContext damageContext = CreateDamageContext(1f, isAbilityDamage, mode: statMode, weaponDamageScale: damageScale)
            .WithFeedbackMetadata(
                sequenceId,
                DamageFeedbackKind.Explosion);
        int damage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(damage);
        MortarPresentationSettings presentation = Runtime.Data.PresentationProfile?.Mortar;
        bool detailed = ShouldUseDetailedPresentation(presentation, shellIndex, shellCount);
        bool showLanding = !activeAbility || detailed;
        Vector3 direction = impactPosition - launchPosition;
        WeaponFeedbackContext feedback = new(
            Runtime,
            mode,
            Heat != null ? Heat.NormalizedHeat : 0f,
            launchPosition,
            direction,
            impactPosition: impactPosition,
            isAbilityDamage: isAbilityDamage,
            explosionRadius: explosionRadius * area,
            eventIntensity: activeAbility ? 0.7f : 1f,
            referenceDamage: damageContext.ReferenceDamage,
            actionSequenceId: sequenceId,
            damageKind: damageContext.DamageKind);
        MortarShellImpact shell = MortarShellImpact.LaunchAuthored(
            launchPosition,
            impactPosition,
            travelTime,
            arcHeight,
            Mathf.Max(1, damage),
            explosionRadius * area,
            falloff,
            knockback,
            tuning.MortarShellCollisionRadius * area,
            Owner,
            payload,
            IsGrapeshotPath(),
            damageContext,
            presentation?.ShellPrefab,
            presentation?.ShellPoolCapacity ?? 1,
            detailed,
            showLanding,
            Presentation,
            feedback,
            completesSequenceContributor: sequenceId != 0);

        if (shell == null && sequenceId != 0)
        {
            for (int i = 0; i < contributorCount; i++)
                DamageFeedbackSequenceRuntime.CompleteContributor(sequenceId);
        }

        if (manageSequenceLifecycle)
            DamageFeedbackSequenceRuntime.CompleteSequence(sequenceId);
        return shell != null;
    }

    private int GetActiveDamageFeedbackSubVolleyShellCount()
    {
        MortarPresentationSettings settings = Runtime?.Data?.PresentationProfile?.Mortar;
        return Mathf.Max(1, settings?.DamageFeedbackSubVolleyShellCount ?? 5);
    }

    private static int GetDamageFeedbackContributorCount(MortarUpgradePayload payload)
    {
        return 1 + (payload.UseGrapeshot ? Mathf.Max(0, payload.GrapeshotCount) : 0);
    }

    private void EnsurePresentationPool()
    {
        if (_presentationPoolPrepared)
            return;
        MortarPresentationSettings settings = Runtime?.Data?.PresentationProfile?.Mortar;
        if (settings?.ShellPrefab == null)
            return;
        settings.Sanitize();
        MortarShellImpact.Prewarm(settings.ShellPrefab, settings.ShellPrewarmCount, settings.ShellPoolCapacity);
        _presentationPoolPrepared = true;
    }

    private static bool ShouldUseDetailedPresentation(
        MortarPresentationSettings settings,
        int shellIndex,
        int shellCount)
    {
        if (settings == null || shellCount <= 1)
            return true;
        int maximum = Mathf.Max(1, settings.MaximumDetailedRainShells);
        if (shellCount <= maximum)
            return true;
        int currentBucket = Mathf.FloorToInt(Mathf.Max(0, shellIndex) * maximum / (float)shellCount);
        int previousBucket = Mathf.FloorToInt(Mathf.Max(0, shellIndex - 1) * maximum / (float)shellCount);
        return shellIndex == 0 || currentBucket != previousBucket;
    }

    private void EmitLaunchFeedback(
        WeaponFeedbackMode mode,
        Vector3 origin,
        Vector3 target,
        float radius,
        bool ability,
        Vector3 impactNormal = default)
    {
        Vector3 direction = target - origin;
        WeaponFeedbackContext feedback = new(
            Runtime,
            mode,
            Heat != null ? Heat.NormalizedHeat : 0f,
            origin,
            direction,
            impactPosition: ability ? target : default,
            impactNormal: impactNormal,
            isAbilityDamage: ability,
            explosionRadius: radius,
            eventIntensity: ability ? 1.25f : 1f,
            anchor: ability ? null : Spawn);
        Feedback.OnShotFired(in feedback);
    }

    private bool TryGetActiveBarrageCollision(
        Vector3 center,
        MortarTuning tuning,
        out RaycastHit terrainHit)
    {
        float area = GetAreaSizeMultiplier();
        Vector3 dropStart = center + Vector3.up * Mathf.Max(0f, tuning.MortarActiveDropHeight);
        return MortarTrajectory.TryPredictTerrainCollision(
            dropStart,
            center,
            0f,
            Mathf.Max(0.05f, tuning.MortarActiveTravelTime),
            Mathf.Max(0.01f, tuning.MortarShellCollisionRadius * area),
            Owner,
            _barragePredictionHits,
            out terrainHit);
    }

    private bool TryResolveActiveBarrageSurface(
        Vector3 aimedCenter,
        MortarTuning tuning,
        out Vector3 surfacePoint,
        out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = Vector3.up;
        if (!TryGetActiveBarrageCollision(aimedCenter, tuning, out RaycastHit terrainHit))
            return false;

        MortarPresentationSurface.Resolve(
            terrainHit,
            GetExplosionRadius(WeaponStatMode.Ability, tuning.MortarActiveExplosionRadius) * GetAreaSizeMultiplier(),
            Owner,
            _presentationSupportHits,
            out surfacePoint,
            out surfaceNormal);
        return true;
    }

    private Vector3 ResolveActiveShellImpact(
        Vector3 resolvedCenter,
        float barrageRadius,
        MortarTuning tuning)
    {
        for (int attempt = 0; attempt < ActiveShellPlacementAttempts; attempt++)
        {
            Vector3 candidate = resolvedCenter + RandomPlanarOffset(barrageRadius);
            if (TryGetActiveBarrageCollision(candidate, tuning, out RaycastHit terrainHit))
                return terrainHit.point;
        }

        // The center was already validated against terrain. Falling back to it
        // keeps the promised shell count without ever placing a shell underground.
        return resolvedCenter;
    }

    private static Vector3 RandomPlanarOffset(float radius)
    {
        if (radius <= 0f)
            return Vector3.zero;

        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }
}
