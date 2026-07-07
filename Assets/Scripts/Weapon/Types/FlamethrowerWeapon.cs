using System.Collections.Generic;
using UnityEngine;

public sealed class FlamethrowerWeapon : BasicProjectileWeapon
{
    private readonly PlayerMovement _movement;
    private readonly List<Transform> _targets = new();
    private readonly List<Vector3> _hitOrigins = new();
    private readonly FlamethrowerHoseStream _hoseStream = new();

    private FlamethrowerStreamVfx _streamVfx;
    private float _autoTickTimer;
    private float _manualTickTimer;

    public FlamethrowerWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn, PlayerMovement movement)
        : base(targeting, pool, spawn)
    {
        _movement = movement;
    }

    // Emits an automatic cone in the player's movement direction.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        Vector3 flameDirection = GetAutomaticFlameDirection();
        float range = GetScaledRange(Runtime.Data.BaseRange);
        ShowAutomaticCone(flameDirection, range, tuning);

        _autoTickTimer -= deltaTime;
        if (_autoTickTimer > 0f)
            return;

        ApplyAutomaticConeDamage(flameDirection, range, tuning);
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
        ShowStream(aimDirection, range, tuning, deltaTime);

        _manualTickTimer -= deltaTime;
        if (_manualTickTimer > 0f)
            return;

        ApplyHoseDamage(
            1f,
            applyBurn: true,
            knockbackScale: tuning.FlameManualKnockbackScale,
            tuning: tuning);
        _manualTickTimer = Mathf.Max(0.01f, tuning.FlameManualTickInterval);
    }

    // Emits a circular flame burst around the player.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        FlamethrowerTuning tuning = Runtime.Data.Flamethrower;
        SpendAbilityAmmo(Runtime.Data.ActiveAbilityAmmoCost);

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
        CompleteActiveAbility();
    }

    // Flamethrower direct ticks can crit; burn ticks do not.
    public override bool CanCrit() => true;

    // Uses current movement as the automatic aim, falling back to the player's facing while idle.
    private Vector3 GetAutomaticFlameDirection()
    {
        Vector3 direction = _movement != null ? _movement.CurrentMoveDirectionWorld : Vector3.zero;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f && Owner != null)
        {
            direction = Owner.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

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

    // Damages every enemy inside the horizontal automatic flame cone.
    private int ApplyAutomaticConeDamage(Vector3 direction, float range, FlamethrowerTuning tuning)
    {
        if (Owner == null)
            return 0;

        Vector3 origin = Spawn != null ? Spawn.position : Owner.position;
        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            origin,
            direction,
            range,
            tuning.FlameAutoConeAngle,
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets);

        for (int i = 0; i < hitCount; i++)
        {
            int damage = CalculateDirectDamage(1f, _targets[i]);
            ApplyDamageToTarget(_targets[i], damage, origin, knockbackScale: 0f);
        }

        return hitCount;
    }

    // Damages enemies near the simulated hose path and optionally refreshes burn on them.
    private int ApplyHoseDamage(float damageScale, bool applyBurn, float knockbackScale, FlamethrowerTuning tuning)
    {
        if (Owner == null || _hoseStream.Points == null || _hoseStream.PointCount <= 0)
            return 0;

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _hoseStream.Points,
            _hoseStream.PointCount,
            GetScaledHoseRadius(tuning),
            Mathf.Max(1, tuning.FlameMaxTargetsPerTick),
            _targets,
            _hitOrigins);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : (Spawn != null ? Spawn.position : Owner.position);
            int damage = CalculateDirectDamage(damageScale, _targets[i]);
            ApplyDamageToTarget(_targets[i], damage, impactOrigin, knockbackScale);
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
        float pathScale = IsJellifiedFuelPath() ? 1.35f : 1f;
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, canCrit: false) * Mathf.Max(0f, tuning.FlameBurnDamageScale) * pathScale;
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    private bool IsJellifiedFuelPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsLiquidNitrogenPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    // Applies immediate damage to one enemy transform if it has a damage receiver.
    private void ApplyDamageToTarget(Transform target, int damage, Vector3 impactOrigin, float knockbackScale)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable != null && WeaponDamageApplier.TryApplyDamage(damageable, damage))
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

        if (IsJellifiedFuelPath())
        {
            float levelScale = Runtime != null ? Mathf.Max(1f, Runtime.Level / 6f) : 1f;
            float radius = GetScaledHoseRadius(tuning) * levelScale;
            FlamethrowerFuelPuddle.Spawn(target.position, radius, damagePerTick, duration, tuning.FlameBurnTickInterval);
        }

        WeaponDummyEnemy dummy = damageComponent.GetComponent<WeaponDummyEnemy>();
        if (IsLiquidNitrogenPath())
        {
            float statusDuration = activeAbility ? 2f : 3f;
            if (dummy != null)
                dummy.ApplyStatus(activeAbility ? "Freeze" : "Liquid Nitrogen", statusDuration);
            if (activeAbility)
                WeaponMovementFreezeStatus.Apply(target, statusDuration);
        }
        else if (dummy != null && IsJellifiedFuelPath())
        {
            dummy.ApplyStatus("Jellified Fuel", duration);
        }
    }

    // Keeps one reusable stream simulation and visual alive while the weapon fires.
    private void ShowStream(Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime)
    {
        if (Spawn == null)
            return;

        if (_streamVfx == null)
            _streamVfx = FlamethrowerStreamVfx.Create();

        _hoseStream.Update(Spawn.position, direction, range, tuning, deltaTime);
        _streamVfx.ShowHose(_hoseStream.Points, _hoseStream.PointCount, GetScaledHoseRadius(tuning), tuning.FlameVisualDuration);
    }

    // Keeps the automatic visual aligned with the same cone used for damage.
    private void ShowAutomaticCone(Vector3 direction, float range, FlamethrowerTuning tuning)
    {
        if (Spawn == null)
            return;

        if (_streamVfx == null)
            _streamVfx = FlamethrowerStreamVfx.Create();

        _streamVfx.ShowCone(Spawn.position, direction, range, tuning.FlameAutoConeAngle, tuning.FlameVisualDuration);
    }

    private float GetScaledRange(float range)
    {
        return Mathf.Max(0f, range) * GetAreaSizeMultiplier();
    }

    private float GetScaledHoseRadius(FlamethrowerTuning tuning)
    {
        return Mathf.Max(0.05f, tuning.FlameHoseRadius) * GetAreaSizeMultiplier();
    }

    private float GetPathAdjustedBurnDuration(FlamethrowerTuning tuning)
    {
        float duration = tuning.FlameBurnDuration;
        if (IsJellifiedFuelPath())
            duration *= 1.5f;
        return duration;
    }

    private float GetPathAdjustedActiveRadius(FlamethrowerTuning tuning)
    {
        float radius = tuning.FlameActiveRadius;
        if (IsJellifiedFuelPath())
            radius *= 1.2f;
        if (IsLiquidNitrogenPath())
            radius *= 0.9f;
        return radius;
    }
}

internal sealed class FlamethrowerHoseStream
{
    private const int MaxSegmentCount = 48;

    private Vector3[] _points;
    private bool _initialized;
    private float _lastUpdateTime;
    private int _lastPointCount;

    public Vector3[] Points => _points;
    public int PointCount { get; private set; }

    public void Update(Vector3 origin, Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime)
    {
        int pointCount = Mathf.Clamp(tuning.FlameHoseSegmentCount, 2, MaxSegmentCount);
        EnsureCapacity(pointCount);

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

        direction.Normalize();
        range = Mathf.Max(0.01f, range);
        deltaTime = Mathf.Clamp(deltaTime, 0.001f, 0.05f);

        bool shouldReset = !_initialized
            || _lastPointCount != pointCount
            || Time.time - _lastUpdateTime > Mathf.Max(0.05f, tuning.FlameVisualDuration * 1.25f);

        if (shouldReset)
            Initialize(origin, direction, range, pointCount);
        else
            Simulate(origin, direction, range, tuning, deltaTime, pointCount);

        PointCount = pointCount;
        _lastPointCount = pointCount;
        _lastUpdateTime = Time.time;
        _initialized = true;
    }

    private void EnsureCapacity(int pointCount)
    {
        if (_points == null || _points.Length < pointCount)
            _points = new Vector3[pointCount];
    }

    private void Initialize(Vector3 origin, Vector3 direction, float range, int pointCount)
    {
        for (int i = 0; i < pointCount; i++)
        {
            float t = pointCount == 1 ? 0f : i / (float)(pointCount - 1);
            _points[i] = origin + direction * (range * t);
        }
    }

    private void Simulate(Vector3 origin, Vector3 direction, float range, FlamethrowerTuning tuning, float deltaTime, int pointCount)
    {
        _points[0] = origin;

        float nearFollow = Mathf.Max(0.01f, tuning.FlameHoseNearFollow);
        float farFollow = Mathf.Max(0.01f, tuning.FlameHoseFarFollow);
        float turbulence = Mathf.Max(0f, tuning.FlameHoseTurbulence);
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(Vector3.forward, direction);
        side.Normalize();

        Vector3 vertical = Vector3.Cross(direction, side).normalized;

        for (int i = 1; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            Vector3 desired = origin + direction * (range * t);
            float response = Mathf.Lerp(nearFollow, farFollow, Mathf.Pow(t, 1.35f));
            float follow = 1f - Mathf.Exp(-response * deltaTime);
            _points[i] = Vector3.Lerp(_points[i], desired, follow);

            if (turbulence <= 0f)
                continue;

            float wave = Mathf.Sin(Time.time * 9.5f + i * 1.73f);
            float ripple = Mathf.Sin(Time.time * 6.2f + i * 2.19f);
            float weight = Mathf.Sin(t * Mathf.PI);
            _points[i] += (side * wave + vertical * ripple) * (turbulence * weight * deltaTime);
        }
    }
}
