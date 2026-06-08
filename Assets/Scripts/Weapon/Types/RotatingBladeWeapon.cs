using System.Collections.Generic;
using UnityEngine;

public sealed class RotatingBladeWeapon : BasicProjectileWeapon
{
    private const int MaxContactTargets = 64;
    private const int MaxManualTargets = 64;
    private const int MaxActiveTargets = 128;

    private readonly List<Transform> _targets = new();
    private readonly List<Vector3> _hitOrigins = new();
    private readonly Vector3[] _activeLinePoints = new Vector3[2];

    private RotatingBladeVfx _vfx;
    private float _spinAngle;
    private float _autoDamageTimer;

    public float SpinAngle => _spinAngle;

    public RotatingBladeWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Spins one blade around the owner and damages enemies only when the blade itself contacts them.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic || Owner == null)
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        TickSpin(deltaTime, tuning);

        Vector3 bladeCenter = GetBladeCenter(tuning);
        float hitRadius = GetScaledHitRadius(tuning);
        ShowOrbit(bladeCenter, hitRadius, tuning);

        _autoDamageTimer -= deltaTime;
        if (_autoDamageTimer > 0f)
            return;

        _autoDamageTimer = GetAutoDamageInterval(tuning);
        int hitCount = EnemyRegistry.CollectClosestOnPlane(bladeCenter, hitRadius, MaxContactTargets, _targets);
        float knockbackScale = GetAutomaticKnockbackScale(tuning);

        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], 1f, bladeCenter, knockbackScale);
    }

    // Performs repeated cone slashes while fire is held, spending one manual ammo per slash.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        Vector3 slashDirection = GetHorizontalAimDirection(aimDirection);
        if (slashDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        FireTimer = GetManualSwingInterval(tuning);

        Vector3 origin = GetOwnerOrigin();
        float range = GetScaledManualRange(tuning);
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            origin,
            slashDirection,
            range,
            tuning.BladeManualConeAngle,
            MaxManualTargets,
            _targets);

        float damageScale = GetManualDamageScale(tuning);
        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], damageScale, origin, tuning.BladeManualKnockbackScale);

        ShowSlash(origin, slashDirection, range, tuning);
    }

    // Thrusts forward in a thick line. Heat adds range in discrete 20% steps, capped by tuning.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        Vector3 thrustDirection = GetHorizontalAimDirection(aimDirection);
        if (thrustDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        Vector3 origin = GetOwnerOrigin();
        float range = GetScaledActiveRange(tuning);
        float lineWidth = GetScaledActiveLineWidth(tuning);

        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + thrustDirection * range;

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _activeLinePoints,
            _activeLinePoints.Length,
            lineWidth * 0.5f,
            MaxActiveTargets,
            _targets,
            _hitOrigins);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : origin;
            ApplyBladeDamage(_targets[i], tuning.BladeActiveDamageScale, impactOrigin, tuning.BladeActiveKnockbackScale);
        }

        ShowThrust(origin, thrustDirection, range, lineWidth, tuning);
    }

    public override bool CanCrit() => true;

    private void TickSpin(float deltaTime, RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float spinRate = tuning.BladeBaseSpinDegreesPerSecond * attackSpeed * weaponRate;
        _spinAngle = Mathf.Repeat(_spinAngle + spinRate * deltaTime, 360f);
    }

    private float GetAutoDamageInterval(RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float interval = Mathf.Max(0.01f, tuning.BladeAutoDamageInterval);
        return interval / Mathf.Max(0.05f, attackSpeed * weaponRate);
    }

    private float GetManualSwingInterval(RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float interval = Mathf.Max(0.01f, tuning.BladeManualCooldown);
        return interval / Mathf.Max(0.05f, attackSpeed * weaponRate);
    }

    private Vector3 GetBladeCenter(RotatingBladeTuning tuning)
    {
        Vector3 baseDirection = Owner != null ? Owner.forward : Vector3.forward;
        baseDirection.y = 0f;
        if (baseDirection.sqrMagnitude <= 0.0001f)
            baseDirection = Vector3.forward;

        baseDirection.Normalize();
        Vector3 orbitDirection = Quaternion.AngleAxis(_spinAngle, Vector3.up) * baseDirection;
        return GetOwnerOrigin() + orbitDirection * GetScaledOrbitRadius(tuning);
    }

    private Vector3 GetHorizontalAimDirection(Vector3 aimDirection)
    {
        Vector3 direction = aimDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f && Owner != null)
        {
            direction = Owner.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private Vector3 GetOwnerOrigin()
    {
        if (Owner != null)
            return Owner.position;

        return Spawn != null ? Spawn.position : Vector3.zero;
    }

    private float GetManualDamageScale(RotatingBladeTuning tuning)
    {
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        return 1f + heat * Mathf.Max(0f, tuning.BladeManualMaxHeatDamageBonus);
    }

    private float GetAutomaticKnockbackScale(RotatingBladeTuning tuning)
    {
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        return Mathf.Max(0f, tuning.BladeAutoKnockbackScale) + heat * Mathf.Max(0f, tuning.BladeAutoMaxHeatKnockbackBonus);
    }

    private float GetScaledOrbitRadius(RotatingBladeTuning tuning)
    {
        return Mathf.Max(0f, tuning.BladeOrbitRadius) * GetAreaSizeMultiplier();
    }

    private float GetScaledHitRadius(RotatingBladeTuning tuning)
    {
        return Mathf.Max(0.05f, tuning.BladeHitRadius) * GetAreaSizeMultiplier();
    }

    private float GetScaledManualRange(RotatingBladeTuning tuning)
    {
        return Mathf.Max(0f, tuning.BladeManualRange) * GetAreaSizeMultiplier();
    }

    private float GetScaledActiveRange(RotatingBladeTuning tuning)
    {
        float stepPercent = Mathf.Max(0.01f, tuning.BladeActiveHeatStepPercent);
        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        float bonusSteps = Mathf.Floor(heatPercent / stepPercent);
        float multiplier = Mathf.Min(
            Mathf.Max(1f, tuning.BladeActiveBaseRangeMultiplier) + bonusSteps,
            Mathf.Max(tuning.BladeActiveBaseRangeMultiplier, tuning.BladeActiveMaxRangeMultiplier));

        return GetScaledManualRange(tuning) * multiplier;
    }

    private float GetScaledActiveLineWidth(RotatingBladeTuning tuning)
    {
        return Mathf.Max(0.05f, tuning.BladeActiveLineWidth) * GetAreaSizeMultiplier();
    }

    private void ApplyBladeDamage(Transform target, float damageScale, Vector3 impactOrigin, float knockbackScale)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit()) * Mathf.Max(0f, damageScale);
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));

        if (damageable.ApplyDamage(finalDamage))
            ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
    }

    private void ShowOrbit(Vector3 bladeCenter, float hitRadius, RotatingBladeTuning tuning)
    {
        if (Owner == null)
            return;

        EnsureVfx();
        _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration);
    }

    private void ShowSlash(Vector3 origin, Vector3 direction, float range, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        _vfx.ShowSlash(origin, direction, range, tuning.BladeManualConeAngle, tuning.BladeVisualDuration);
    }

    private void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration);
    }

    private void EnsureVfx()
    {
        if (_vfx == null)
            _vfx = RotatingBladeVfx.Create();
    }
}
