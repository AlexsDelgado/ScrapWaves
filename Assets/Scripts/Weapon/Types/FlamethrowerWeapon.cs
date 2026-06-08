using System.Collections.Generic;
using UnityEngine;

public sealed class FlamethrowerWeapon : BasicProjectileWeapon
{
    private readonly PlayerMovement _movement;
    private readonly List<Transform> _targets = new();

    private FlamethrowerStreamVfx _streamVfx;
    private float _autoTickTimer;
    private float _manualTickTimer;

    public FlamethrowerWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn, PlayerMovement movement)
        : base(targeting, pool, spawn)
    {
        _movement = movement;
    }

    // Ticks off-hand cone damage in movement direction, with camera pitch controlling vertical aim.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        Vector3 flameDirection = GetAutomaticFlameDirection(aimDirection);
        float range = GetScaledRange(Runtime.Data.BaseRange);
        ShowStream(flameDirection, range, tuning.FlameAutoConeAngle, tuning.FlameVisualDuration);

        _autoTickTimer -= deltaTime;
        if (_autoTickTimer > 0f)
            return;

        ApplyConeDamage(
            flameDirection,
            range,
            tuning.FlameAutoConeAngle,
            1f,
            applyBurn: false,
            knockbackScale: 0f,
            tuning: tuning);

        _autoTickTimer = GetAutomaticTickInterval(tuning);
    }

    // Holds a continuous stream in the manual aim direction and spends ammo over time.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual || !isFiring)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        float ammoCost = tuning.FlameManualAmmoPerSecond * deltaTime;
        if (!TrySpendManualAmmo(ammoCost, requireFullAmount: false))
            return;

        float range = GetManualRange(tuning);
        ShowStream(aimDirection, range, tuning.FlameManualConeAngle, tuning.FlameVisualDuration);

        _manualTickTimer -= deltaTime;
        if (_manualTickTimer > 0f)
            return;

        ApplyConeDamage(
            aimDirection,
            range,
            tuning.FlameManualConeAngle,
            1f,
            applyBurn: true,
            knockbackScale: tuning.FlameManualKnockbackScale,
            tuning: tuning);
        _manualTickTimer = Mathf.Max(0.01f, tuning.FlameManualTickInterval);
    }

    // Emits a circular flame burst around the player.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        float activeRadius = GetScaledRange(GetPathAdjustedActiveRadius(tuning));
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            Owner.position,
            Owner.forward,
            activeRadius,
            360f,
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets);

        for (int i = 0; i < hitCount; i++)
        {
            int damage = CalculateDirectDamage(tuning.FlameActiveDamageScale, _targets[i]);
            int burnDamage = CalculateBurnDamage(tuning, _targets[i]);
            ApplyDamageToTarget(_targets[i], damage, Owner.position, tuning.FlameActiveKnockbackScale);
            ApplyBurnToTarget(_targets[i], burnDamage, tuning, activeAbility: true);
        }

        FlamethrowerStreamVfx.SpawnRing(Owner.position, activeRadius, tuning.FlameActiveVisualDuration);
    }

    // Flamethrower direct ticks can crit; burn ticks do not.
    public override bool CanCrit() => true;

    // Uses movement for horizontal aim, while preserving reticle pitch so the stream can climb upward.
    private Vector3 GetAutomaticFlameDirection(Vector3 aimDirection)
    {
        Vector3 horizontal = _movement != null ? _movement.CurrentMoveDirectionWorld : Vector3.zero;
        horizontal.y = 0f;

        if (horizontal.sqrMagnitude <= 0.0001f && Owner != null)
        {
            horizontal = Owner.forward;
            horizontal.y = 0f;
        }

        if (horizontal.sqrMagnitude <= 0.0001f)
            horizontal = Vector3.forward;

        float pitch = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized.y : 0f;
        Vector3 direction = horizontal.normalized;
        direction.y = pitch;
        return direction.normalized;
    }

    // Applies faster automatic ticks above the configured heat threshold.
    private float GetAutomaticTickInterval(FlamethrowerTuning tuning)
    {
        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        if (heatPercent >= tuning.FlameOverheatTickThresholdPercent)
            return Mathf.Max(0.01f, tuning.FlameOverheatAutoTickInterval);

        return Mathf.Max(0.01f, tuning.FlameAutoTickInterval);
    }

    // Manual stream reaches farther as heat rises.
    private float GetManualRange(FlamethrowerTuning tuning)
    {
        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        return GetScaledRange(Runtime.Data.BaseRange) * (1f + Mathf.Max(0f, tuning.FlameManualRangeHeatMultiplier) * heat);
    }

    // Damages enemies inside a cone and optionally refreshes burn on them.
    private int ApplyConeDamage(Vector3 direction, float range, float coneAngle, float damageScale, bool applyBurn, float knockbackScale, FlamethrowerTuning tuning)
    {
        if (Owner == null)
            return 0;

        Vector3 origin = Spawn != null ? Spawn.position : Owner.position;
        int hitCount = EnemyRegistry.CollectClosestInCone(
            origin,
            direction,
            range,
            coneAngle,
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets);

        for (int i = 0; i < hitCount; i++)
        {
            int damage = CalculateDirectDamage(damageScale, _targets[i]);
            ApplyDamageToTarget(_targets[i], damage, origin, knockbackScale);
            if (applyBurn)
            {
                int burnDamage = CalculateBurnDamage(tuning, _targets[i]);
                ApplyBurnToTarget(_targets[i], burnDamage, tuning, activeAbility: false);
            }
        }

        return hitCount;
    }

    // Calculates one direct flamethrower damage tick from shared weapon rules.
    private int CalculateDirectDamage(float damageScale, Transform target)
    {
        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit()) * Mathf.Max(0f, damageScale);
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    // Calculates burn damage separately so damage-over-time does not roll critical hits.
    private int CalculateBurnDamage(FlamethrowerTuning tuning, Transform target)
    {
        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float pathScale = Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA ? 1.35f : 1f;
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, canCrit: false) * Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale;
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    // Applies immediate damage to one enemy transform if it has a damage receiver.
    private void ApplyDamageToTarget(Transform target, int damage, Vector3 impactOrigin, float knockbackScale)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.ApplyDamage(damage))
            ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);
    }

    // Refreshes a simple burn component on the target's damage receiver.
    private void ApplyBurnToTarget(Transform target, int damagePerTick, FlamethrowerTuning tuning, bool activeAbility)
    {
        if (target == null || tuning.FlameBurnDuration <= 0f)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable is not Component damageComponent)
            return;

        FlamethrowerBurnStatus burn = damageComponent.GetComponent<FlamethrowerBurnStatus>();
        if (burn == null)
            burn = damageComponent.gameObject.AddComponent<FlamethrowerBurnStatus>();

        float duration = GetPathAdjustedBurnDuration(tuning);
        burn.Refresh(damageable, damagePerTick, duration, tuning.FlameBurnTickInterval);

        WeaponDummyEnemy dummy = damageComponent.GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
        {
            if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
                dummy.ApplyStatus("Jellified Fuel", duration);
            if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB)
                dummy.ApplyStatus(activeAbility ? "Freeze" : "Liquid Nitrogen", activeAbility ? 1.2f : 2.5f);
        }
    }

    // Keeps one reusable stream visual alive while the weapon fires.
    private void ShowStream(Vector3 direction, float range, float coneAngle, float duration)
    {
        if (Spawn == null)
            return;

        if (_streamVfx == null)
            _streamVfx = FlamethrowerStreamVfx.Create();

        _streamVfx.ShowCone(Spawn.position, direction, range, coneAngle, duration);
    }

    private float GetScaledRange(float range)
    {
        return Mathf.Max(0f, range) * GetAreaSizeMultiplier();
    }

    private float GetPathAdjustedBurnDuration(FlamethrowerTuning tuning)
    {
        float duration = tuning.FlameBurnDuration;
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
            duration *= 1.5f;
        return duration;
    }

    private float GetPathAdjustedActiveRadius(FlamethrowerTuning tuning)
    {
        float radius = tuning.FlameActiveRadius;
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA)
            radius *= 1.2f;
        if (Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB)
            radius *= 0.9f;
        return radius;
    }
}
