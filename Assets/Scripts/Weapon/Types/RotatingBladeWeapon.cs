using System.Collections.Generic;
using UnityEngine;

public sealed class RotatingBladeWeapon : BasicProjectileWeapon
{
    private readonly List<Transform> _targets = new();
    private float _spinAngle;
    private float _invincibilityTimer;

    public float SpinAngle => _spinAngle;
    public float InvincibilityTimer => _invincibilityTimer;

    public RotatingBladeWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        TickSpin(deltaTime);
        TickInvincibility(deltaTime);

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        FireTimer = GetContactInterval();
        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        float radius = GetBladeRadius(tuning);
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            Owner.position,
            Owner.forward,
            radius,
            360f,
            GetMaxContactTargets(),
            _targets);

        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], 1f, Owner.position, GetHeatKnockbackScale(tuning));
    }

    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        TickSpin(deltaTime);
        TickInvincibility(deltaTime);

        if (!isFiring)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        if (!TrySpendManualAmmo(tuning.BladeManualAmmoCost, requireFullAmount: false))
            return;

        FireTimer = GetFireInterval();
        float range = tuning.BladeManualSlashRange * GetAreaSizeMultiplier();
        int hitCount = EnemyRegistry.CollectClosestInCone(
            Spawn.position,
            aimDirection,
            range,
            tuning.BladeManualSlashAngle,
            64,
            _targets);

        float damageScale = 1f + (Heat != null ? Heat.NormalizedHeat * tuning.BladeHeatDamageBonus : 0f);
        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], damageScale, Spawn.position, GetHeatKnockbackScale(tuning));
    }

    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        Vector3 thrustDirection = aimDirection.normalized;
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        float range = tuning.BladeActiveThrustRange * (1f + heat * tuning.BladeHeatThrustRangeBonus) * GetAreaSizeMultiplier();
        float widthAngle = Mathf.Clamp(tuning.BladeActiveThrustWidth * 12f, 1f, 90f);

        int hitCount = EnemyRegistry.CollectClosestInCone(
            Spawn.position,
            thrustDirection,
            range,
            widthAngle,
            128,
            _targets);

        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], tuning.BladeActiveDamageScale, Spawn.position, GetHeatKnockbackScale(tuning));

        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB)
            DashOwner(thrustDirection, tuning);

        _invincibilityTimer = Mathf.Max(_invincibilityTimer, tuning.BladeInvincibilityDuration);
    }

    public override bool CanCrit() => true;

    private void TickSpin(float deltaTime)
    {
        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        float attackSpeed = Stats != null ? WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier) : 1f;
        _spinAngle = Mathf.Repeat(_spinAngle + tuning.BladeSpinDegreesPerSecond * attackSpeed * deltaTime, 360f);
    }

    private void TickInvincibility(float deltaTime)
    {
        if (_invincibilityTimer > 0f)
            _invincibilityTimer = Mathf.Max(0f, _invincibilityTimer - deltaTime);
    }

    private float GetContactInterval()
    {
        float attackSpeed = Stats != null ? WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier) : 1f;
        return Mathf.Max(0.03f, Runtime.Data.RotatingBlade.BladeContactTickInterval / attackSpeed / WeaponMath.GetAttackRateMultiplier(Runtime));
    }

    private int GetMaxContactTargets()
    {
        int count = Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA ? 12 : 6;
        if (Runtime.Level >= 10)
            count += 6;
        return count;
    }

    private float GetBladeRadius(RotatingBladeTuning tuning)
    {
        float radius = tuning.BladeOrbitRadius + tuning.BladeContactRadius;
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
            radius += tuning.BladeContactRadius;
        return radius * GetAreaSizeMultiplier();
    }

    private float GetHeatKnockbackScale(RotatingBladeTuning tuning)
    {
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        return 1f + heat * Mathf.Max(0f, tuning.BladeHeatKnockbackBonus);
    }

    private void ApplyBladeDamage(Transform target, float damageScale, Vector3 impactOrigin, float knockbackScale)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float pathScale = Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB ? 1.25f : 1f;
        int damage = Mathf.Max(1, Mathf.RoundToInt(WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit()) * damageScale * pathScale));
        if (!damageable.ApplyDamage(damage))
            return;

        ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);

        if (damageable is Component component)
        {
            WeaponDummyEnemy dummy = component.GetComponent<WeaponDummyEnemy>();
            dummy?.ApplyStatus(Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB ? "Atomic Sharpness" : "Bleed", 2f);
        }
    }

    private void DashOwner(Vector3 direction, RotatingBladeTuning tuning)
    {
        if (Owner == null)
            return;

        CharacterController controller = Owner.GetComponent<CharacterController>();
        Vector3 displacement = direction.normalized * Mathf.Max(0f, tuning.BladeActiveDashDistance);
        if (controller != null)
            controller.Move(displacement);
        else
            Owner.position += displacement;
    }
}
