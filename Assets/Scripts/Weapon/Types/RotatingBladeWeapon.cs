using System.Collections.Generic;
using UnityEngine;

public sealed class RotatingBladeWeapon : BasicProjectileWeapon
{
    private const int MaxContactTargets = 64;
    private const int MaxManualTargets = 64;
    private const int MaxActiveTargets = 128;
    private static readonly Color MultiBladeVfxColor = new(0.95f, 1f, 0.35f, 0.9f);
    private static readonly Color AtomicSharpnessVfxColor = new(0.6f, 0.95f, 1f, 0.95f);

    private readonly List<Transform> _targets = new();
    private readonly List<Vector3> _hitOrigins = new();
    private readonly Vector3[] _activeLinePoints = new Vector3[2];

    private RotatingBladeVfx _vfx;
    private float _spinAngle;
    private float _autoDamageTimer;
    private bool _multiBladeManualPending;
    private int _multiBladeManualSwingIndex;
    private int _multiBladeManualSwingCount;
    private float _multiBladeManualTimer;
    private float _multiBladeManualRange;
    private float _multiBladeManualDamageScale;
    private Vector3 _multiBladeManualOrigin;
    private Vector3 _multiBladeManualDirection;
    private bool _multiBladeActivePending;
    private int _multiBladeActiveThrustIndex;
    private int _multiBladeActiveThrustCount;
    private float _multiBladeActiveTimer;
    private float _multiBladeActiveRange;
    private float _multiBladeActiveLineWidth;
    private Vector3 _multiBladeActiveOrigin;
    private Vector3 _multiBladeActiveDirection;

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

        float hitRadius = GetScaledHitRadius(tuning);
        _autoDamageTimer -= deltaTime;
        bool shouldDamage = _autoDamageTimer <= 0f;
        if (shouldDamage)
            _autoDamageTimer = GetAutoDamageInterval(tuning);

        int bladeCount = GetBladeCount();
        for (int bladeIndex = 0; bladeIndex < bladeCount; bladeIndex++)
        {
            Vector3 bladeCenter = GetBladeCenter(tuning, bladeIndex, bladeCount);
            ShowOrbit(bladeCenter, hitRadius, tuning);
            if (shouldDamage && IsMultiBladePath())
                WeaponUpgradeVfx.SpawnRing(bladeCenter, hitRadius * 1.25f, MultiBladeVfxColor, tuning.BladeVisualDuration, 0.9f, bladeIndex == 0 ? "MULTI" : null);
            else if (shouldDamage && IsAtomicSharpnessPath())
                WeaponUpgradeVfx.SpawnRing(bladeCenter, hitRadius * 1.35f, AtomicSharpnessVfxColor, tuning.BladeVisualDuration, 1.1f, "ATOM");

            if (!shouldDamage)
                continue;

            int hitCount = EnemyRegistry.CollectClosestOnPlane(bladeCenter, hitRadius, MaxContactTargets, _targets);
            float knockbackScale = GetAtomicSharpnessKnockbackScale(GetAutomaticKnockbackScale(tuning));
            for (int i = 0; i < hitCount; i++)
                ApplyBladeDamage(_targets[i], GetAtomicSharpnessDamageScale(), bladeCenter, knockbackScale);
        }
    }

    // Performs repeated cone slashes while fire is held, spending one manual ammo per slash.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (TickPendingMultiBladeActions(deltaTime))
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
        float damageScale = GetManualDamageScale(tuning);
        int swingCount = GetBladeCount();
        if (IsMultiBladePath() && swingCount > 1)
        {
            StartMultiBladeManualSwings(origin, slashDirection, range, damageScale, swingCount);
            return;
        }

        ExecuteManualSwing(
            origin,
            slashDirection,
            range,
            damageScale,
            GetAtomicSharpnessKnockbackScale(tuning.BladeManualKnockbackScale),
            tuning,
            0,
            1);
        ShowSlash(origin, slashDirection, range, tuning);
    }

    // Thrusts forward in a thick line. Heat adds range in discrete 20% steps, capped by tuning.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        Vector3 thrustDirection = GetHorizontalAimDirection(aimDirection);
        if (thrustDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        Vector3 origin = GetOwnerOrigin();
        if (IsAtomicSharpnessPath())
        {
            ExecuteAtomicSharpnessDash(origin, thrustDirection, tuning);
            CompleteActiveAbility();
            return;
        }

        float range = GetScaledActiveRange(tuning);
        float lineWidth = GetScaledActiveLineWidth(tuning);
        int thrustCount = GetBladeCount();

        if (IsMultiBladePath() && thrustCount > 1)
        {
            StartMultiBladeActiveThrusts(origin, thrustDirection, range, lineWidth, thrustCount);
            return;
        }

        ExecuteActiveThrust(origin, thrustDirection, range, lineWidth, tuning, 0, 1);
        ShowThrust(origin, thrustDirection, range, lineWidth, tuning);
        CompleteActiveAbility();
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

    private Vector3 GetBladeCenter(RotatingBladeTuning tuning, int bladeIndex = 0, int bladeCount = 1)
    {
        Vector3 baseDirection = Owner != null ? Owner.forward : Vector3.forward;
        baseDirection.y = 0f;
        if (baseDirection.sqrMagnitude <= 0.0001f)
            baseDirection = Vector3.forward;

        baseDirection.Normalize();
        float offset = bladeCount <= 1 ? 0f : (360f / bladeCount) * bladeIndex;
        Vector3 orbitDirection = Quaternion.AngleAxis(_spinAngle + offset, Vector3.up) * baseDirection;
        return GetOwnerOrigin() + orbitDirection * GetScaledOrbitRadius(tuning);
    }

    private bool IsMultiBladePath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsAtomicSharpnessPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    private int GetBladeCount()
    {
        if (!IsMultiBladePath())
            return 1;

        return Mathf.Clamp(1 + Mathf.FloorToInt((Runtime.Level - 6) / 2f), 2, 4);
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

    private float GetAtomicSharpnessDamageScale() => IsAtomicSharpnessPath() ? 2f : 1f;

    private float GetAtomicSharpnessKnockbackScale(float original) => IsAtomicSharpnessPath() ? 0f : original;

    private float GetAtomicActiveDamageScale() => 1.5f;

    private float GetAtomicActiveInvulnerabilitySeconds() => 0.25f;

    private float GetAtomicDashDurationForHitCount(int hitCount)
    {
        return GetAtomicActiveInvulnerabilitySeconds() + Mathf.Max(0, hitCount) * 0.05f;
    }

    private float GetMultiBladeActionInterval() => 0.1f;

    private bool ShouldApplyMultiBladeKnockback(int actionIndex, int actionCount)
    {
        return actionIndex >= Mathf.Max(1, actionCount) - 1;
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

    private void ApplyBladeDamage(Transform target, float damageScale, Vector3 impactOrigin, float knockbackScale, bool isAbilityDamage = false)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), isAbilityDamage: isAbilityDamage) * Mathf.Max(0f, damageScale);
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));

        if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
            ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
    }

    private bool TickPendingMultiBladeActions(float deltaTime)
    {
        bool consumed = false;
        if (_multiBladeManualPending)
        {
            _multiBladeManualTimer -= deltaTime;
            if (_multiBladeManualTimer <= 0f)
                ExecuteNextMultiBladeManualSwing();

            consumed = true;
        }

        if (_multiBladeActivePending)
        {
            _multiBladeActiveTimer -= deltaTime;
            if (_multiBladeActiveTimer <= 0f)
                ExecuteNextMultiBladeActiveThrust();

            consumed = true;
        }

        return consumed;
    }

    private void StartMultiBladeManualSwings(Vector3 origin, Vector3 direction, float range, float damageScale, int swingCount)
    {
        _multiBladeManualPending = true;
        _multiBladeManualSwingIndex = 0;
        _multiBladeManualSwingCount = Mathf.Max(1, swingCount);
        _multiBladeManualTimer = 0f;
        _multiBladeManualOrigin = origin;
        _multiBladeManualDirection = direction;
        _multiBladeManualRange = range;
        _multiBladeManualDamageScale = damageScale;
        ExecuteNextMultiBladeManualSwing();
    }

    private void ExecuteNextMultiBladeManualSwing()
    {
        if (!_multiBladeManualPending)
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        int swing = _multiBladeManualSwingIndex;
        Vector3 swingDirection = GetMultiBladeOffsetDirection(_multiBladeManualDirection, swing, _multiBladeManualSwingCount);
        float knockbackScale = ShouldApplyMultiBladeKnockback(swing, _multiBladeManualSwingCount)
            ? tuning.BladeManualKnockbackScale
            : 0f;
        ExecuteManualSwing(
            _multiBladeManualOrigin,
            swingDirection,
            _multiBladeManualRange,
            _multiBladeManualDamageScale,
            knockbackScale,
            tuning,
            swing,
            _multiBladeManualSwingCount);

        _multiBladeManualSwingIndex++;
        if (_multiBladeManualSwingIndex >= _multiBladeManualSwingCount)
        {
            _multiBladeManualPending = false;
            ShowSlash(_multiBladeManualOrigin, _multiBladeManualDirection, _multiBladeManualRange, tuning);
            return;
        }

        _multiBladeManualTimer = GetMultiBladeActionInterval();
    }

    private void ExecuteManualSwing(
        Vector3 origin,
        Vector3 swingDirection,
        float range,
        float damageScale,
        float knockbackScale,
        RotatingBladeTuning tuning,
        int swingIndex,
        int swingCount)
    {
        if (IsMultiBladePath())
            WeaponUpgradeVfx.SpawnCone(origin, swingDirection, range, tuning.BladeManualConeAngle, MultiBladeVfxColor, tuning.BladeVisualDuration, 5, swingIndex == 0 ? "MULTI" : null);
        else if (IsAtomicSharpnessPath())
            WeaponUpgradeVfx.SpawnCone(origin, swingDirection, range, tuning.BladeManualConeAngle * 0.75f, AtomicSharpnessVfxColor, tuning.BladeVisualDuration, 5, "ATOM");

        int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(
            origin,
            swingDirection,
            range,
            tuning.BladeManualConeAngle,
            MaxManualTargets,
            _targets);

        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(
                _targets[i],
                damageScale * GetAtomicSharpnessDamageScale(),
                origin,
                GetAtomicSharpnessKnockbackScale(knockbackScale));
    }

    private void StartMultiBladeActiveThrusts(Vector3 origin, Vector3 direction, float range, float lineWidth, int thrustCount)
    {
        _multiBladeActivePending = true;
        _multiBladeActiveThrustIndex = 0;
        _multiBladeActiveThrustCount = Mathf.Max(1, thrustCount);
        _multiBladeActiveTimer = 0f;
        _multiBladeActiveOrigin = origin;
        _multiBladeActiveDirection = direction;
        _multiBladeActiveRange = range;
        _multiBladeActiveLineWidth = lineWidth;
        ExecuteNextMultiBladeActiveThrust();
    }

    private void ExecuteNextMultiBladeActiveThrust()
    {
        if (!_multiBladeActivePending)
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        int thrust = _multiBladeActiveThrustIndex;
        Vector3 thrustDirection = GetMultiBladeOffsetDirection(_multiBladeActiveDirection, thrust, _multiBladeActiveThrustCount);
        ExecuteActiveThrust(
            _multiBladeActiveOrigin,
            thrustDirection,
            _multiBladeActiveRange,
            _multiBladeActiveLineWidth,
            tuning,
            thrust,
            _multiBladeActiveThrustCount);

        _multiBladeActiveThrustIndex++;
        if (_multiBladeActiveThrustIndex >= _multiBladeActiveThrustCount)
        {
            _multiBladeActivePending = false;
            ShowThrust(_multiBladeActiveOrigin, _multiBladeActiveDirection, _multiBladeActiveRange, _multiBladeActiveLineWidth, tuning);
            CompleteActiveAbility();
            return;
        }

        _multiBladeActiveTimer = GetMultiBladeActionInterval();
    }

    private void ExecuteActiveThrust(
        Vector3 origin,
        Vector3 direction,
        float range,
        float lineWidth,
        RotatingBladeTuning tuning,
        int thrustIndex,
        int thrustCount)
    {
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * range;
        if (IsMultiBladePath())
            WeaponUpgradeVfx.SpawnBeam(_activeLinePoints[0], _activeLinePoints[1], MultiBladeVfxColor, tuning.BladeVisualDuration, lineWidth * 0.25f, thrustIndex == 0 ? "MULTI" : null);

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
            ApplyBladeDamage(
                _targets[i],
                tuning.BladeActiveDamageScale,
                impactOrigin,
                tuning.BladeActiveKnockbackScale,
                isAbilityDamage: true);
        }
    }

    private void ExecuteAtomicSharpnessDash(Vector3 origin, Vector3 direction, RotatingBladeTuning tuning)
    {
        float range = Mathf.Min(GetScaledActiveRange(tuning), GetScaledManualRange(tuning) * 3f);
        float lineWidth = GetScaledActiveLineWidth(tuning);
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * range;
        WeaponUpgradeVfx.SpawnBeam(_activeLinePoints[0], _activeLinePoints[1], AtomicSharpnessVfxColor, GetAtomicActiveInvulnerabilitySeconds(), lineWidth * 0.45f, "DASH");

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _activeLinePoints,
            _activeLinePoints.Length,
            lineWidth * 0.65f,
            MaxActiveTargets,
            _targets,
            _hitOrigins);

        float dashDuration = GetAtomicDashDurationForHitCount(hitCount);
        Owner?.GetComponent<PlayerHealth>()?.GrantInvulnerability(dashDuration);
        Owner?.GetComponent<PlayerMovement>()?.ApplyWeaponDash(
            direction,
            Mathf.Max(1f, range / Mathf.Max(0.05f, dashDuration)),
            dashDuration);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : origin;
            ApplyBladeDamage(
                _targets[i],
                GetAtomicActiveDamageScale(),
                impactOrigin,
                0f,
                isAbilityDamage: true);
            WeaponUpgradeVfx.SpawnTargetPulse(_targets[i], AtomicSharpnessVfxColor, 0.35f, "DASH");
        }
    }

    private Vector3 GetMultiBladeOffsetDirection(Vector3 direction, int actionIndex, int actionCount)
    {
        return Quaternion.AngleAxis((actionIndex - (Mathf.Max(1, actionCount) - 1) * 0.5f) * 8f, Vector3.up) * direction;
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
