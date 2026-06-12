using System.Collections.Generic;
using UnityEngine;

public sealed class RocketLauncherWeapon : BasicProjectileWeapon
{
    private readonly List<Transform> _abilityTargets = new();
    private readonly List<Transform> _abilityCandidates = new();

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

    // Fires overloaded multi-target volley scaled by current heat amount.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        if (Spawn == null || aimDirection.sqrMagnitude <= 0.0001f)
            return;

        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
        int rocketCount = tuning.RocketActiveBaseRocketCount + heatBonus + GetFragmentationActiveBonus();
        int targetsFound = BuildActiveRocketTargets(aimDirection, rocketCount, tuning);

        if (targetsFound <= 0)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        for (int i = 0; i < targetsFound; i++)
        {
            FireRocketAt(
                _abilityTargets[i].position,
                tuning.RocketActiveDamageScale,
                GetPathAdjustedExplosionRadius(tuning.RocketActiveExplosionRadius),
                GetPathAdjustedFalloff(tuning.RocketActiveExplosionFalloff),
                tuning.RocketActiveSpeedMultiplier,
                WeaponEnemyClassifier.CountsAsEliteOrBoss(_abilityTargets[i]));
        }

        CompleteActiveAbility();
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

    private int GetFragmentationActiveBonus()
    {
        int bonus = Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB ? 4 : 0;
        if (Runtime.Level >= 10)
            bonus += 2;
        return bonus;
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

    // Builds active volley targets in proximity order, allowing extra rockets for elites/bosses.
    private int BuildActiveRocketTargets(Vector3 aimDirection, int rocketCount, RocketLauncherTuning tuning)
    {
        _abilityTargets.Clear();
        if (rocketCount <= 0)
            return 0;

        EnemyRegistry.CollectClosestOnPlaneInCone(
            Spawn.position,
            aimDirection,
            Runtime.Data.BaseRange,
            tuning.RocketActiveConeAngle,
            Mathf.Max(rocketCount, 64),
            _abilityCandidates);

        for (int i = 0; i < _abilityCandidates.Count && _abilityTargets.Count < rocketCount; i++)
        {
            Transform candidate = _abilityCandidates[i];
            int rocketsForTarget = GetMaxActiveRocketsForTarget(candidate);
            for (int j = 0; j < rocketsForTarget && _abilityTargets.Count < rocketCount; j++)
                _abilityTargets.Add(candidate);
        }

        return _abilityTargets.Count;
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
