using System.Collections.Generic;
using UnityEngine;

public sealed class RocketLauncherWeapon : BasicProjectileWeapon
{
    private readonly List<Transform> _abilityTargets = new();

    public RocketLauncherWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Fires automatic rocket bursts with heat-scaled extra rockets.
    public override void TickAutomatic(float deltaTime)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        if (!Targeting.TryGetTarget(Runtime, Owner, Runtime.Data.BaseRange, out Transform target))
            return;

        FireTimer = GetFireInterval();
        int extra = GetThresholdRocketBonus();
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        FireBurstAt(
            target.position,
            tuning.RocketAutoBaseRocketCount + extra,
            1f,
            tuning.RocketAutoExplosionRadius,
            tuning.RocketAutoExplosionFalloff,
            tuning.RocketAutoSpeedMultiplier);
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
            tuning.RocketManualExplosionRadius,
            tuning.RocketManualExplosionFalloff,
            tuning.RocketManualSpeedMultiplier);
    }

    // Fires overloaded multi-target volley scaled by current heat amount.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (Spawn == null || aimDirection.sqrMagnitude <= 0.0001f)
            return;

        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        int rocketCount = tuning.RocketActiveBaseRocketCount + heatBonus;
        int targetsFound = EnemyRegistry.CollectClosestOnPlaneInCone(
            Spawn.position,
            aimDirection,
            Runtime.Data.BaseRange,
            tuning.RocketActiveConeAngle,
            rocketCount,
            _abilityTargets);

        if (targetsFound <= 0)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        for (int i = 0; i < targetsFound; i++)
        {
            FireRocketAt(
                _abilityTargets[i].position,
                tuning.RocketActiveDamageScale,
                tuning.RocketActiveExplosionRadius,
                tuning.RocketActiveExplosionFalloff,
                tuning.RocketActiveSpeedMultiplier);
        }
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

    // Spawns explosive rocket volley at the same target point.
    private void FireBurstAt(Vector3 targetPosition, int count, float damageScale, float explosionRadius, float falloff, float speedMultiplier)
    {
        for (int i = 0; i < count; i++)
            FireRocketAt(targetPosition, damageScale, explosionRadius, falloff, speedMultiplier);
    }

    // Fires a rocket that detonates on enemy hit or when it reaches weapon range.
    private void FireRocketAt(Vector3 targetPosition, float damageScale, float explosionRadius, float falloff, float speedMultiplier)
    {
        FireExplosiveAt(targetPosition, damageScale, false, explosionRadius, falloff, speedMultiplier, Runtime.Data.BaseRange, true);
    }
}
