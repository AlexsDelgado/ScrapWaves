using System.Collections.Generic;
using UnityEngine;

public sealed class MortarWeapon : BasicProjectileWeapon
{
    private readonly List<Transform> _targets = new();

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
        if (!EnemyRegistry.TryGetRandomOnPlane(Owner.position, Runtime.Data.BaseRange, out Transform target))
            return;

        FireTimer = GetFireInterval();
        Vector3 impact = target.position + RandomPlanarOffset(tuning.MortarAutoAccuracyRadius);
        FireShell(
            impact,
            1f,
            tuning.MortarAutoExplosionRadius,
            tuning.MortarExplosionFalloff,
            tuning.MortarShellTravelTime,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

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

        MortarTuning tuning = Runtime.Data.Mortar;
        FireTimer = GetManualFireInterval();
        Vector3 impact = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        impact += RandomPlanarOffset(tuning.MortarManualAccuracyRadius);
        FireShell(
            impact,
            1f,
            tuning.MortarManualExplosionRadius,
            tuning.MortarExplosionFalloff,
            GetManualTravelTime(tuning),
            false);
    }

    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        MortarTuning tuning = Runtime.Data.Mortar;
        Vector3 center = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
        int shellCount = GetActiveShellCount(tuning);
        for (int i = 0; i < shellCount; i++)
        {
            Vector3 impact = center + RandomPlanarOffset(tuning.MortarBarrageRadius);
            FireShell(
                impact,
                tuning.MortarActiveDamageScale,
                tuning.MortarActiveExplosionRadius,
                tuning.MortarExplosionFalloff,
                tuning.MortarActiveTravelTime + i * 0.08f,
                false);
        }
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
        int count = Mathf.Max(1, tuning.MortarActiveShellCount);
        WeaponUpgradePath path = Runtime.SelectedPath;
        if (Runtime.HasAdvancedPath && path == WeaponUpgradePath.PathA)
            count += 4;
        if (Runtime.HasAdvancedPath && path == WeaponUpgradePath.PathB)
            count += 2;
        if (Runtime.Level >= 10)
            count += 3;
        return count;
    }

    private void FireShell(Vector3 impactPosition, float damageScale, float explosionRadius, float falloff, float travelTime, bool eliteOrBoss)
    {
        if (Spawn == null)
            return;

        float area = GetAreaSizeMultiplier();
        int damage = Mathf.RoundToInt(WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit()) * Mathf.Max(0f, damageScale));
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, damage, damageScale);
        MortarShellImpact.Launch(
            Spawn.position,
            impactPosition,
            travelTime,
            Runtime.Data.Mortar.MortarArcHeight,
            Mathf.Max(1, damage),
            explosionRadius * area,
            falloff,
            knockback);
    }

    private static Vector3 RandomPlanarOffset(float radius)
    {
        if (radius <= 0f)
            return Vector3.zero;

        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }
}
