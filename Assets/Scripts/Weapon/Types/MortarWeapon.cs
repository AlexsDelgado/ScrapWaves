using UnityEngine;

public sealed class MortarWeapon : BasicProjectileWeapon, IMortarReticleStatus
{
    private bool _presentationPoolPrepared;
    private readonly RaycastHit[] _barragePredictionHits = new RaycastHit[32];
    private readonly RaycastHit[] _presentationSupportHits = new RaycastHit[16];

    public float ManualExplosionRadius => Runtime?.Data == null
        ? 0f
        : Runtime.Data.Mortar.MortarManualExplosionRadius * GetAreaSizeMultiplier();
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

        if (!EnemyRegistry.TryGetRandomOnPlane(Owner.position, Runtime.Data.BaseRange, out Transform target))
            return;

        FireTimer = GetFireInterval();
        Vector3 impact = target.position + RandomPlanarOffset(tuning.MortarAutoAccuracyRadius);
        Vector3 launch = Spawn != null ? Spawn.position : Owner.position;
        EmitLaunchFeedback(
            WeaponFeedbackMode.Automatic,
            launch,
            impact,
            tuning.MortarAutoExplosionRadius * GetAreaSizeMultiplier(),
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
        Vector3 impact = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        impact += RandomPlanarOffset(tuning.MortarManualAccuracyRadius);
        EmitLaunchFeedback(
            WeaponFeedbackMode.Manual,
            Spawn.position,
            impact,
            tuning.MortarManualExplosionRadius * GetAreaSizeMultiplier(),
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

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        Vector3 center = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        int shellCount = IsGrapeshotPath() ? GetGrapeshotRainShellCount(tuning) : GetActiveShellCount(tuning);
        float barrageRadius = tuning.MortarBarrageRadius * GetAreaSizeMultiplier();
        if (TryGetActiveBarrageCollision(center, tuning, out RaycastHit barrageHit))
        {
            MortarPresentationSurface.Resolve(
                barrageHit,
                tuning.MortarActiveExplosionRadius * GetAreaSizeMultiplier(),
                Owner,
                _presentationSupportHits,
                out Vector3 presentationPoint,
                out Vector3 presentationNormal);
            EmitLaunchFeedback(
                WeaponFeedbackMode.Active,
                Spawn.position,
                presentationPoint,
                barrageRadius,
                true,
                presentationNormal);
        }
        for (int i = 0; i < shellCount; i++)
        {
            Vector3 impact = center + RandomPlanarOffset(barrageRadius);
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
                shellCount: shellCount);
        }

        CompleteActiveAbility();
    }

    public override bool CanCrit() => false;

    protected override float GetHeatFireRateMultiplier()
    {
        if (Heat == null || Heat.NormalizedHeat <= 0.5f)
            return 1f;

        float heatOverHalf = Mathf.InverseLerp(0.5f, 1f, Heat.NormalizedHeat);
        return 1f + heatOverHalf * Mathf.Max(0f, Runtime.Data.Mortar.MortarHeatFireRateBonusAbove50);
    }

    private float GetManualFireInterval()
    {
        return GetFireIntervalWithoutHeat();
    }

    private float GetManualTravelTime(MortarTuning tuning)
    {
        float speedBonus = Heat != null ? 1f + Heat.NormalizedHeat * Mathf.Max(0f, tuning.MortarHeatManualSpeedBonus) : 1f;
        return Mathf.Max(0.05f, tuning.MortarManualTravelTime / speedBonus);
    }

    private int GetActiveShellCount(MortarTuning tuning)
    {
        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        return Mathf.Max(1, 5 + heatBonus);
    }

    private MortarUpgradePayload GetUpgradePayload(bool activeAbility)
    {
        if (Runtime == null || !Runtime.HasAdvancedPath)
            return MortarUpgradePayload.None;

        if (Runtime.SelectedPath == WeaponUpgradePath.PathA)
        {
            if (activeAbility)
                return MortarUpgradePayload.None;

            return new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f);
        }

        if (Runtime.SelectedPath == WeaponUpgradePath.PathB)
            return new MortarUpgradePayload(false, 0, 0f, 0f, 3, 2f);

        return MortarUpgradePayload.None;
    }

    private bool IsGrapeshotPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsMultiChargedPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    private int GetGrapeshotRainShellCount(MortarTuning tuning)
    {
        const float rainDurationSeconds = 5f;
        int heatBonusPerSecond = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        int projectilesPerSecond = 10 + heatBonusPerSecond;
        return Mathf.Max(1, Mathf.RoundToInt(projectilesPerSecond * rainDurationSeconds));
    }

    private float GetActiveShellTravelTime(MortarTuning tuning, int shellIndex)
    {
        float baseTravelTime = Mathf.Max(0.05f, tuning.MortarActiveTravelTime);
        if (IsMultiChargedPath())
            return baseTravelTime;

        if (IsGrapeshotPath())
        {
            const float rainDurationSeconds = 5f;
            int shellCount = Mathf.Max(1, GetGrapeshotRainShellCount(tuning));
            return baseTravelTime + Mathf.Max(0, shellIndex) * (rainDurationSeconds / shellCount);
        }

        return baseTravelTime + Mathf.Max(0, shellIndex) * 0.08f;
    }

    private void FireShell(
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
        int shellCount = 1)
    {
        MortarTuning tuning = Runtime.Data.Mortar;
        float area = GetAreaSizeMultiplier();
        MortarUpgradePayload payload = GetUpgradePayload(activeAbility);
        WeaponDamageContext damageContext = CreateDamageContext(damageScale, isAbilityDamage);
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
            eventIntensity: activeAbility ? 0.7f : 1f);
        MortarShellImpact.LaunchAuthored(
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
            feedback);
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

    private static Vector3 RandomPlanarOffset(float radius)
    {
        if (radius <= 0f)
            return Vector3.zero;

        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }
}
