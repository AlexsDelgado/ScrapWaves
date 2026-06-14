using UnityEngine;

public sealed class MortarWeapon : BasicProjectileWeapon
{
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
        if (Runtime.State != WeaponState.Manual || !isFiring)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
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

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        Vector3 center = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        int shellCount = GetActiveShellCount(tuning);
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
                tuning.MortarActiveTravelTime + i * 0.08f,
                0f,
                false);
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
        float baseInterval = GetFireInterval();
        if (Heat == null || Heat.NormalizedHeat <= 0.5f)
            return baseInterval;

        float heatOverHalf = Mathf.InverseLerp(0.5f, 1f, Heat.NormalizedHeat);
        float speedBonus = 1f + heatOverHalf * Mathf.Max(0f, Runtime.Data.Mortar.MortarHeatManualSpeedBonus);
        return baseInterval / Mathf.Max(0.1f, speedBonus);
    }

    private float GetManualTravelTime(MortarTuning tuning)
    {
        float speedBonus = Heat != null ? 1f + Heat.NormalizedHeat * Mathf.Max(0f, tuning.MortarHeatManualSpeedBonus) : 1f;
        return Mathf.Max(0.05f, tuning.MortarManualTravelTime / speedBonus);
    }

    private int GetActiveShellCount(MortarTuning tuning)
    {
        return Mathf.Max(1, tuning.MortarActiveShellCount);
    }

    private void FireShell(
        Vector3 launchPosition,
        Vector3 impactPosition,
        float damageScale,
        float explosionRadius,
        float falloff,
        float travelTime,
        float arcHeight,
        bool eliteOrBoss)
    {
        MortarTuning tuning = Runtime.Data.Mortar;
        float area = GetAreaSizeMultiplier();
        int damage = Mathf.RoundToInt(WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit()) * Mathf.Max(0f, damageScale));
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, damage, damageScale);
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
            Owner);
    }

    private static Vector3 RandomPlanarOffset(float radius)
    {
        if (radius <= 0f)
            return Vector3.zero;

        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }
}
