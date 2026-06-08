using UnityEngine;

public interface IWeaponBehaviour
{
    WeaponInstance Runtime { get; }
    void Setup(WeaponInstance instance, Transform owner, PlayerStats stats, HeatManager heat);
    void TickAutomatic(float deltaTime, Vector3 aimDirection);
    void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring);
    void UseActiveAbility(Vector3 aimDirection);
    bool CanCrit();
}

public class BasicProjectileWeapon : IWeaponBehaviour
{
    protected readonly IWeaponTargeting Targeting;
    protected readonly ProjectilePool Pool;
    protected readonly Transform Spawn;

    protected Transform Owner;
    protected PlayerStats Stats;
    protected HeatManager Heat;
    protected float FireTimer;

    public WeaponInstance Runtime { get; protected set; }

    public BasicProjectileWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
    {
        Targeting = targeting;
        Pool = pool;
        Spawn = spawn;
    }

    // Stores runtime dependencies required by weapon behavior.
    public void Setup(WeaponInstance instance, Transform owner, PlayerStats stats, HeatManager heat)
    {
        Runtime = instance;
        Owner = owner;
        Stats = stats;
        Heat = heat;
    }

    // Handles automatic fire attempts using selected targeting logic.
    public virtual void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        FireTimer = GetFireInterval();
        if (!Targeting.TryGetTarget(Runtime, Owner, Runtime.Data.BaseRange, aimDirection, out Transform target))
            return;

        FireAt(target.position, 1f, WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires manually toward aim direction and consumes one ammo.
    public virtual void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual || !isFiring)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        FireTimer = GetFireInterval();
        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        FireAt(Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange, 1f, false);
    }

    // Executes baseline active ability projectile and ammo spending.
    public virtual void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        FireAt(Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange, 1.75f, false);
    }

    // Enables critical hits by default for generic projectile weapons.
    public virtual bool CanCrit() => true;

    // Computes interval from base rate, stats, level/path, and heat.
    protected virtual float GetFireInterval()
    {
        float attackSpeed = Mathf.Max(0.01f, Stats.GetStat(StatType.AttackSpeedMultiplier));
        float weaponRate = Mathf.Max(0.01f, Runtime.Data.BaseAttackRate * WeaponMath.GetAttackRateMultiplier(Runtime));
        float heatBonus = GetHeatFireRateMultiplier();
        return 1f / Mathf.Max(0.05f, weaponRate * attackSpeed * heatBonus);
    }

    // Lets individual weapons decide whether heat affects fire rate.
    protected virtual float GetHeatFireRateMultiplier()
    {
        return Heat != null ? 1f + Heat.NormalizedHeat * 0.25f : 1f;
    }

    // Lets individual weapons override critical damage behavior.
    protected virtual float GetCritMultiplierOverride() => 1f;

    // Spends manual ammo, optionally requiring the full amount before firing.
    protected bool TrySpendManualAmmo(float amount, bool requireFullAmount)
    {
        if (Runtime == null || Runtime.State != WeaponState.Manual || Runtime.CurrentAmmo <= 0f)
            return false;

        float cost = Mathf.Max(0f, amount);
        if (requireFullAmount && Runtime.CurrentAmmo < cost)
            return false;

        Runtime.CurrentAmmo = Mathf.Max(0f, Runtime.CurrentAmmo - cost);
        return true;
    }

    // Spawns explosive projectile with configurable radius and falloff behavior.
    protected void FireExplosiveAt(Vector3 targetPosition, float damageScale, bool eliteOrBoss, float explosionRadius, float falloff)
    {
        FireExplosiveAt(targetPosition, damageScale, eliteOrBoss, explosionRadius, falloff, 1f, 0f, false);
    }

    // Spawns explosive projectile with speed and max-range detonation options.
    protected void FireExplosiveAt(
        Vector3 targetPosition,
        float damageScale,
        bool eliteOrBoss,
        float explosionRadius,
        float falloff,
        float speedMultiplier,
        float maxTravelDistance,
        bool explodeOnMaxTravel)
    {
        if (Pool == null || Spawn == null)
            return;

        Vector3 direction = (targetPosition - Spawn.position).normalized;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        float scaledExplosionRadius = explosionRadius * GetAreaSizeMultiplier();
        int finalDamage = Mathf.RoundToInt(WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), GetCritMultiplierOverride()) * damageScale);
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, finalDamage, damageScale);
        Pool.TrySpawnExplosiveProjectile(
            Spawn.position,
            rotation,
            direction,
            finalDamage,
            scaledExplosionRadius,
            falloff,
            knockback,
            speedMultiplier,
            maxTravelDistance,
            explodeOnMaxTravel);
    }

    // Spawns one projectile toward position and resolves final scaled damage.
    protected void FireAt(Vector3 targetPosition, float damageScale, bool eliteOrBoss)
    {
        if (Spawn == null)
            return;

        FireInDirection(targetPosition - Spawn.position, damageScale, eliteOrBoss);
    }

    // Spawns one projectile in a known direction and resolves final scaled damage.
    protected void FireInDirection(Vector3 direction, float damageScale, bool eliteOrBoss)
    {
        if (Spawn == null)
            return;

        FireFromPositionInDirection(Spawn.position, direction, damageScale, eliteOrBoss);
    }

    // Spawns one projectile from a specific position in a known direction.
    protected void FireFromPositionInDirection(Vector3 position, Vector3 direction, float damageScale, bool eliteOrBoss)
    {
        if (Pool == null)
            return;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        int finalDamage = Mathf.RoundToInt(WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), GetCritMultiplierOverride()) * damageScale);
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, finalDamage, damageScale);
        Pool.TrySpawnProjectile(position, rotation, direction, finalDamage, knockback);
    }

    // Applies projectile/area size stat to weapon ranges and areas without affecting angles.
    protected float GetAreaSizeMultiplier()
    {
        return WeaponMath.GetStatScale(Stats, StatType.ProjectileAreaSize);
    }

    // Applies weapon knockback to a damage receiver after a successful hit.
    protected void ApplyKnockback(IDamageable damageable, Vector3 impactOrigin, int damage, float scale)
    {
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, damage, scale);
        EnemyKnockbackReceiver.TryApply(damageable, impactOrigin, knockback);
    }
}
