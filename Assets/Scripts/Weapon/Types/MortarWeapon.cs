using UnityEngine;

public sealed class MortarWeapon : BasicProjectileWeapon, IMortarReticleStatus
{
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
        FireShell(
            Spawn != null ? Spawn.position : Owner.position,
            impact,
            1f,
            tuning.MortarAutoExplosionRadius,
            tuning.MortarExplosionFalloff,
            tuning.MortarShellTravelTime,
            tuning.MortarArcHeight,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

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
        FireShell(
            Spawn.position,
            impact,
            1f,
            tuning.MortarManualExplosionRadius,
            tuning.MortarExplosionFalloff,
            GetManualTravelTime(tuning),
            tuning.MortarArcHeight,
            false);
    }

    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

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
                activeAbility: true,
                isAbilityDamage: true);
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
        bool activeAbility = false,
        bool isAbilityDamage = false)
    {
        MortarTuning tuning = Runtime.Data.Mortar;
        float area = GetAreaSizeMultiplier();
        MortarUpgradePayload payload = GetUpgradePayload(activeAbility);
        WeaponDamageContext damageContext = CreateDamageContext(damageScale, isAbilityDamage);
        int damage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(damage);
        MortarShellImpact.Launch(
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
            damageContext);
    }

    private static Vector3 RandomPlanarOffset(float radius)
    {
        if (radius <= 0f)
            return Vector3.zero;

        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }
}
