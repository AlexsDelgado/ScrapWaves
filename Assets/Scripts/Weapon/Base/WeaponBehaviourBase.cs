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

public interface IWeaponPresentationReceiver
{
    IWeaponPresentationSink PresentationSink { get; }
    void SetPresentationSink(IWeaponPresentationSink presentationSink);
}

public interface IHoldActiveAbilityBehaviour
{
    bool IsActiveAbilityCharging { get; }
    void BeginActiveAbility(Vector3 aimDirection);
    void TickActiveAbility(float deltaTime, Vector3 aimDirection);
    void ReleaseActiveAbility(Vector3 aimDirection);
    void CancelActiveAbility();
}

public interface IRocketReticleStatus
{
    bool IsTargetingActive { get; }
    int CurrentRocketLocks { get; }
    int InitialRocketLocks { get; }
    int MaximumRocketLocks { get; }
}

public interface IMortarReticleStatus
{
    float ManualExplosionRadius { get; }
    float ShellCollisionRadius { get; }
    float ManualTravelTime { get; }
    float ArcHeight { get; }
}

public class BasicProjectileWeapon : IWeaponBehaviour, IWeaponPresentationReceiver
{
    protected readonly IWeaponTargeting Targeting;
    protected readonly ProjectilePool Pool;
    protected readonly Transform Spawn;

    protected Transform Owner;
    protected PlayerStats Stats;
    protected HeatManager Heat;
    protected float FireTimer;
    protected IWeaponPresentationSink Presentation { get; private set; } = NullWeaponPresentationSink.Instance;
    protected IWeaponFeedbackSink Feedback => WeaponFeedbackEmitter.Resolve(Presentation);

    public WeaponInstance Runtime { get; protected set; }
    public IWeaponPresentationSink PresentationSink => Presentation;

    public BasicProjectileWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
    {
        Targeting = targeting;
        Pool = pool;
        Spawn = spawn;
    }

    public void SetPresentationSink(IWeaponPresentationSink presentationSink)
    {
        Presentation = presentationSink ?? NullWeaponPresentationSink.Instance;
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

        FireAt(EnemyRegistry.GetAimPoint(target), 1f, WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires manually toward aim direction and consumes one ammo.
    public virtual void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        FireTimer = GetFireInterval();
        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        FireAt(Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange, 1f, false);
    }

    // Executes baseline active ability projectile and ammo spending.
    public virtual void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
            return;

        FireAt(Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange, 1.75f, false, isAbilityDamage: true);
        CompleteActiveAbility();
    }

    protected bool CanBeginActiveAbility() =>
        Runtime != null
        && Runtime.State == WeaponState.Manual
        && Runtime.AbilityCooldownTimer <= 0f;

    protected void CompleteActiveAbility()
    {
        if (Runtime?.Data == null)
            return;
        Runtime.AbilityCooldownTimer = Mathf.Max(0f, WeaponMath.GetAbilityCooldownDuration(Runtime, Stats));
    }

    // Enables critical hits by default for generic projectile weapons.
    public virtual bool CanCrit() => true;

    // Computes interval from base rate, stats, level/path, and heat.
    protected virtual float GetFireInterval()
    {
        return GetFireIntervalWithoutHeat() / Mathf.Max(0.01f, GetHeatFireRateMultiplier());
    }

    protected float GetFireIntervalWithoutHeat()
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = Mathf.Max(0.01f, Runtime.Data.BaseAttackRate * WeaponMath.GetAttackRateMultiplier(Runtime));
        return 1f / Mathf.Max(0.05f, weaponRate * attackSpeed);
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
        if (Runtime == null || Runtime.State != WeaponState.Manual)
            return false;

        float cost = Mathf.Max(0f, amount);
        if (cost <= 0f)
            return true;

        if (Runtime.CurrentAmmo <= 0f)
            return false;

        if (requireFullAmount && Runtime.CurrentAmmo < cost)
            return false;

        Runtime.CurrentAmmo = Mathf.Max(0f, Runtime.CurrentAmmo - cost);
        return true;
    }

    // Spawns explosive projectile with configurable radius and falloff behavior.
    protected bool FireExplosiveAt(Vector3 targetPosition, float damageScale, bool eliteOrBoss, float explosionRadius, float falloff)
    {
        return FireExplosiveAt(targetPosition, damageScale, eliteOrBoss, explosionRadius, falloff, 1f, 0f, false);
    }

    // Spawns explosive projectile with speed and max-range detonation options.
    protected bool FireExplosiveAt(
        Vector3 targetPosition,
        float damageScale,
        bool eliteOrBoss,
        float explosionRadius,
        float falloff,
        float speedMultiplier,
        float maxTravelDistance,
        bool explodeOnMaxTravel,
        bool isAbilityDamage = false)
    {
        if (Pool == null || Spawn == null)
            return false;

        Vector3 direction = (targetPosition - Spawn.position).normalized;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        float scaledExplosionRadius = explosionRadius * GetAreaSizeMultiplier();
        WeaponDamageContext damageContext = CreateDamageContext(damageScale, isAbilityDamage);
        int finalDamage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(finalDamage);
        return Pool.TrySpawnExplosiveProjectile(
            Spawn.position,
            rotation,
            direction,
            finalDamage,
            scaledExplosionRadius,
            falloff,
            knockback,
            speedMultiplier,
            maxTravelDistance,
            explodeOnMaxTravel,
            damageContext);
    }

    // Spawns one projectile toward position and resolves final scaled damage.
    protected bool FireAt(Vector3 targetPosition, float damageScale, bool eliteOrBoss, bool isAbilityDamage = false)
    {
        if (Spawn == null)
            return false;

        return FireInDirection(targetPosition - Spawn.position, damageScale, eliteOrBoss, isAbilityDamage);
    }

    // Spawns one projectile in a known direction and resolves final scaled damage.
    protected bool FireInDirection(Vector3 direction, float damageScale, bool eliteOrBoss, bool isAbilityDamage = false)
    {
        if (Spawn == null)
            return false;

        return FireFromPositionInDirection(Spawn.position, direction, damageScale, eliteOrBoss, isAbilityDamage);
    }

    // Spawns one projectile from a specific position in a known direction.
    protected bool FireFromPositionInDirection(
        Vector3 position,
        Vector3 direction,
        float damageScale,
        bool eliteOrBoss,
        bool isAbilityDamage = false)
    {
        return FireFromPositionInDirection(
            position,
            direction,
            damageScale,
            eliteOrBoss,
            out _,
            isAbilityDamage);
    }

    // Spawns one projectile and returns the configured pooled instance when successful.
    protected bool FireFromPositionInDirection(
        Vector3 position,
        Vector3 direction,
        float damageScale,
        bool eliteOrBoss,
        out Projectile spawnedProjectile,
        bool isAbilityDamage = false)
    {
        spawnedProjectile = null;
        if (Pool == null)
            return false;

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        WeaponDamageContext damageContext = CreateDamageContext(damageScale, isAbilityDamage);
        int finalDamage = damageContext.EstimateDamage(eliteOrBoss);
        float knockback = damageContext.CalculateKnockback(finalDamage);
        return Pool.TrySpawnProjectile(
            position,
            rotation,
            direction,
            finalDamage,
            knockback,
            damageContext,
            out spawnedProjectile);
    }

    protected WeaponDamageContext CreateDamageContext(float damageScale, bool isAbilityDamage, float knockbackScale = -1f)
    {
        float resolvedKnockbackScale = knockbackScale >= 0f ? knockbackScale : damageScale;
        return new WeaponDamageContext(
            Stats,
            Runtime,
            CanCrit(),
            GetCritMultiplierOverride(),
            damageScale,
            isAbilityDamage,
            resolvedKnockbackScale);
    }

    // Applies projectile/area size stat to weapon ranges and areas without affecting angles.
    protected float GetAreaSizeMultiplier()
    {
        return WeaponMath.GetStatScale(Stats, StatType.ProjectileAreaSize);
    }

    protected float GetScaledWeaponRange(float range)
    {
        return Mathf.Max(0f, range) * GetAreaSizeMultiplier();
    }

    // Applies weapon knockback to a damage receiver after a successful hit.
    protected void ApplyKnockback(IDamageable damageable, Vector3 impactOrigin, int damage, float scale)
    {
        float knockback = WeaponMath.CalculateKnockback(Stats, Runtime, damage, scale);
        EnemyKnockbackReceiver.TryApply(damageable, impactOrigin, knockback);
    }
}
