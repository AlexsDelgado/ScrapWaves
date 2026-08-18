using System.Collections.Generic;
using UnityEngine;

public sealed class RotatingBladeWeapon : BasicProjectileWeapon
{
    private const int MaxContactTargets = 64;
    private const int MaxManualTargets = 64;
    private const int MaxActiveTargets = 128;
    private const float AtomicSharpnessSpinMultiplier = 1.5f;
    private const float AtomicActiveDashSegmentSeconds = 0.16f;
    private const float AtomicActivePostDashInvulnerabilitySeconds = 0.25f;
    private static readonly Color MultiBladeVfxColor = new(1f, 0.68f, 0.48f, 0.9f);
    private static readonly Color AtomicSharpnessVfxColor = new(0.36f, 0.04f, 0.55f, 0.95f);

    private readonly List<Transform> _targets = new();
    private readonly List<Vector3> _hitOrigins = new();
    private readonly Vector3[] _activeLinePoints = new Vector3[2];
    private readonly Vector3[] _bladeSweepPoints = new Vector3[12];

    private RotatingBladeVfx _vfx;
    private float _spinAngle;
    private float _lastAutoDamageSpinAngle;
    private float _autoDamageTimer;
    private bool _multiBladeManualPending;
    private int _multiBladeManualSwingIndex;
    private int _multiBladeManualSwingCount;
    private float _multiBladeManualTimer;
    private float _multiBladeManualRange;
    private float _multiBladeManualDamageScale;
    private Vector3 _multiBladeManualDirection;
    private bool _multiBladeActivePending;
    private int _multiBladeActiveThrustIndex;
    private int _multiBladeActiveThrustCount;
    private float _multiBladeActiveTimer;
    private float _multiBladeActiveRange;
    private float _multiBladeActiveLineWidth;
    private Vector3 _multiBladeActiveDirection;

    public float SpinAngle => _spinAngle;

    public RotatingBladeWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Spins one blade around the owner and damages enemies only when the blade itself contacts them.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (_multiBladeActivePending && TickPendingMultiBladeActions(deltaTime))
            return;

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
        float sweepStartAngle = _lastAutoDamageSpinAngle;
        for (int bladeIndex = 0; bladeIndex < bladeCount; bladeIndex++)
        {
            Vector3 bladeCenter = GetBladeCenter(tuning, bladeIndex, bladeCount);
            ShowOrbit(bladeCenter, hitRadius, tuning);

            if (!shouldDamage)
                continue;

            int hitCount = CollectBladeContactTargets(tuning, bladeIndex, bladeCount, sweepStartAngle, _spinAngle, hitRadius);
            float knockbackScale = GetAtomicSharpnessKnockbackScale(GetAutomaticKnockbackScale(tuning));
            for (int i = 0; i < hitCount; i++)
            {
                Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : bladeCenter;
                ApplyBladeDamage(
                    _targets[i],
                    GetAtomicSharpnessDamageScale(),
                    impactOrigin,
                    knockbackScale,
                    WeaponFeedbackMode.Automatic);
            }
        }

        if (shouldDamage)
            _lastAutoDamageSpinAngle = _spinAngle;
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
            StartMultiBladeManualSwings(slashDirection, range, damageScale, swingCount);
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
    }

    // Thrusts forward in a thick line. Heat adds range in discrete 20% steps, capped by tuning.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        Vector3 thrustDirection = GetHorizontalAimDirection(aimDirection);
        if (thrustDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: false))
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
            StartMultiBladeActiveThrusts(thrustDirection, range, lineWidth, thrustCount);
            return;
        }

        ExecuteActiveThrust(origin, thrustDirection, range, lineWidth, tuning.BladeActiveKnockbackScale, tuning, 0, 1);
        CompleteActiveAbility();
    }

    public override bool CanCrit() => true;

    private void TickSpin(float deltaTime, RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float spinRate = tuning.BladeBaseSpinDegreesPerSecond * attackSpeed * weaponRate * GetAtomicSharpnessSpinMultiplier();
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
        return GetBladeCenter(tuning, bladeIndex, bladeCount, _spinAngle);
    }

    private Vector3 GetBladeCenter(RotatingBladeTuning tuning, int bladeIndex, int bladeCount, float spinAngle)
    {
        Vector3 baseDirection = Vector3.forward;
        float offset = bladeCount <= 1 ? 0f : (360f / bladeCount) * bladeIndex;
        Vector3 orbitDirection = Quaternion.AngleAxis(spinAngle + offset, Vector3.up) * baseDirection;
        return GetOwnerOrigin() + orbitDirection * GetScaledOrbitRadius(tuning);
    }

    private int CollectBladeContactTargets(
        RotatingBladeTuning tuning,
        int bladeIndex,
        int bladeCount,
        float previousSpinAngle,
        float currentSpinAngle,
        float hitRadius)
    {
        float sweepDegrees = Mathf.Repeat(currentSpinAngle - previousSpinAngle + 360f, 360f);
        int segmentCount = Mathf.Clamp(Mathf.CeilToInt(sweepDegrees / 20f), 1, _bladeSweepPoints.Length - 1);
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float spinAngle = previousSpinAngle + sweepDegrees * t;
            _bladeSweepPoints[i] = GetBladeCenter(tuning, bladeIndex, bladeCount, spinAngle);
        }

        return EnemyRegistry.CollectClosestNearPolyline(
            _bladeSweepPoints,
            segmentCount + 1,
            hitRadius,
            MaxContactTargets,
            _targets,
            _hitOrigins);
    }

    private bool IsMultiBladePath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsAtomicSharpnessPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    private int GetBladeCount()
    {
        if (!IsMultiBladePath())
            return 1;

        return 1 + Mathf.Max(1, Runtime.Level - 5);
    }

    private Vector3 GetHorizontalAimDirection(Vector3 aimDirection)
    {
        if (TryGetCameraHorizontalAimDirection(out Vector3 cameraDirection))
            return cameraDirection;

        Vector3 direction = aimDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f && Owner != null)
        {
            direction = Owner.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private static bool TryGetCameraHorizontalAimDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        Camera camera = Camera.main;
        if (camera == null)
            return false;

        direction = camera.transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
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

    private float GetAtomicSharpnessSpinMultiplier() => IsAtomicSharpnessPath() ? AtomicSharpnessSpinMultiplier : 1f;

    private float GetAtomicActiveDamageScale() => 1.5f;

    private float GetAtomicActiveDashSegmentSeconds() => AtomicActiveDashSegmentSeconds;

    private float GetAtomicActivePostDashInvulnerabilitySeconds() => AtomicActivePostDashInvulnerabilitySeconds;

    private float GetAtomicDashDurationForHitCount(int hitCount)
    {
        return GetAtomicActiveDashSegmentSeconds() * (1f + Mathf.Max(0, hitCount));
    }

    private float GetAtomicActiveInvulnerabilityDuration(float dashDuration)
    {
        return Mathf.Max(0f, dashDuration) + GetAtomicActivePostDashInvulnerabilitySeconds();
    }

    private float GetAtomicDashBaseRange(RotatingBladeTuning tuning)
    {
        return GetScaledManualRange(tuning) * Mathf.Max(0f, tuning.AtomicDashBaseRangeMultiplier);
    }

    private float GetAtomicDashRangeForHitCount(float baseRange, int hitCount, RotatingBladeTuning tuning)
    {
        float perHitMultiplier = Mathf.Max(0f, tuning.AtomicDashRangePerHitMultiplier);
        return Mathf.Max(0f, baseRange) * (1f + Mathf.Max(0, hitCount) * perHitMultiplier);
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
        float areaSize = GetAreaSizeMultiplier();
        float scaledBladeWidth = Mathf.Max(0.05f, tuning.BladeHitRadius) * areaSize;
        float orbitGrowth = Mathf.Max(0f, tuning.BladeOrbitRadius) * Mathf.Max(0f, areaSize - 1f);
        return scaledBladeWidth + orbitGrowth;
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

    private void ApplyBladeDamage(
        Transform target,
        float damageScale,
        Vector3 impactOrigin,
        float knockbackScale,
        WeaponFeedbackMode feedbackMode,
        bool isAbilityDamage = false,
        bool strongImpact = false)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(target);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), isAbilityDamage: isAbilityDamage, targetPosition: target.position) * Mathf.Max(0f, damageScale);
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));

        int healthBefore = GetRemainingHealth(damageable);
        if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
        {
            ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
            bool kill = healthBefore > 0 && GetRemainingHealth(damageable) <= 0;
            EmitImpactFeedback(target, impactOrigin, finalDamage, feedbackMode, isAbilityDamage, kill, strongImpact);
        }
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

    private void StartMultiBladeManualSwings(Vector3 direction, float range, float damageScale, int swingCount)
    {
        _multiBladeManualPending = true;
        _multiBladeManualSwingIndex = 0;
        _multiBladeManualSwingCount = Mathf.Max(1, swingCount);
        _multiBladeManualTimer = 0f;
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
        Vector3 origin = GetOwnerOrigin();
        Vector3 swingDirection = GetMultiBladeOffsetDirection(_multiBladeManualDirection, swing, _multiBladeManualSwingCount);
        float knockbackScale = ShouldApplyMultiBladeKnockback(swing, _multiBladeManualSwingCount)
            ? tuning.BladeManualKnockbackScale
            : 0f;
        ExecuteManualSwing(
            origin,
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
        ShowSlash(origin, swingDirection, range, tuning);
        bool strongImpact = IsMultiBladePath() && swingCount > 1 && ShouldApplyMultiBladeKnockback(swingIndex, swingCount);
        EmitShotFeedback(
            WeaponFeedbackMode.Manual,
            origin,
            swingDirection,
            range,
            isAbilityDamage: false,
            eventIntensity: strongImpact ? 1.2f : swingCount > 1 ? 0.58f : 0.82f);

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
                GetAtomicSharpnessKnockbackScale(knockbackScale),
                WeaponFeedbackMode.Manual,
                strongImpact: strongImpact);
    }

    private void StartMultiBladeActiveThrusts(Vector3 direction, float range, float lineWidth, int thrustCount)
    {
        _multiBladeActivePending = true;
        _multiBladeActiveThrustIndex = 0;
        _multiBladeActiveThrustCount = Mathf.Max(1, thrustCount);
        _multiBladeActiveTimer = 0f;
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
        Vector3 origin = GetOwnerOrigin();
        Vector3 thrustDirection = _multiBladeActiveDirection;
        float knockbackScale = ShouldApplyMultiBladeKnockback(thrust, _multiBladeActiveThrustCount)
            ? tuning.BladeActiveKnockbackScale
            : 0f;
        ExecuteActiveThrust(
            origin,
            thrustDirection,
            _multiBladeActiveRange,
            _multiBladeActiveLineWidth,
            knockbackScale,
            tuning,
            thrust,
            _multiBladeActiveThrustCount);

        _multiBladeActiveThrustIndex++;
        if (_multiBladeActiveThrustIndex >= _multiBladeActiveThrustCount)
        {
            _multiBladeActivePending = false;
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
        float knockbackScale,
        RotatingBladeTuning tuning,
        int thrustIndex,
        int thrustCount)
    {
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * range;
        bool strongImpact = IsMultiBladePath() && thrustCount > 1 && ShouldApplyMultiBladeKnockback(thrustIndex, thrustCount);
        Vector3 visualDirection = IsMultiBladePath()
            ? GetMultiBladeOffsetDirection(direction, thrustIndex, thrustCount)
            : direction;
        ShowThrust(origin, visualDirection, range, lineWidth, tuning);
        EmitShotFeedback(
            WeaponFeedbackMode.Active,
            origin,
            visualDirection,
            range,
            isAbilityDamage: true,
            eventIntensity: strongImpact ? 1.35f : thrustCount > 1 ? 0.52f : 1f);

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
                knockbackScale,
                WeaponFeedbackMode.Active,
                isAbilityDamage: true,
                strongImpact: strongImpact);
        }
    }

    private void ExecuteAtomicSharpnessDash(Vector3 origin, Vector3 direction, RotatingBladeTuning tuning)
    {
        float baseRange = GetAtomicDashBaseRange(tuning);
        float lineWidth = GetScaledActiveLineWidth(tuning);

        int hitCount = CollectAtomicDashTargets(origin, direction, baseRange, lineWidth, tuning);
        float dashDuration = GetAtomicDashDurationForHitCount(hitCount);
        float dashRange = GetAtomicDashRangeForHitCount(baseRange, hitCount, tuning);
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * dashRange;
        EnsureVfx();
        _vfx.ShowDash(origin, direction, dashRange, lineWidth, dashDuration, AtomicSharpnessVfxColor);
        EmitShotFeedback(
            WeaponFeedbackMode.Active,
            origin,
            direction,
            dashRange,
            isAbilityDamage: true,
            eventIntensity: Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(hitCount / 4f)));

        Owner?.GetComponent<PlayerHealth>()?.GrantInvulnerability(GetAtomicActiveInvulnerabilityDuration(dashDuration));
        Owner?.GetComponent<PlayerMovement>()?.ApplyWeaponDash(
            direction,
            Mathf.Max(1f, baseRange / Mathf.Max(0.05f, GetAtomicActiveDashSegmentSeconds())),
            dashDuration);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : origin;
            ApplyBladeDamage(
                _targets[i],
                GetAtomicActiveDamageScale(),
                impactOrigin,
                0f,
                WeaponFeedbackMode.Active,
                isAbilityDamage: true);
        }
    }

    private int CollectAtomicDashTargets(Vector3 origin, Vector3 direction, float baseRange, float lineWidth, RotatingBladeTuning tuning)
    {
        float dashRange = Mathf.Max(0.01f, baseRange);
        int hitCount = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            _activeLinePoints[0] = origin;
            _activeLinePoints[1] = origin + direction * dashRange;
            hitCount = EnemyRegistry.CollectClosestNearPolyline(
                _activeLinePoints,
                _activeLinePoints.Length,
                lineWidth * 0.65f,
                MaxActiveTargets,
                _targets,
                _hitOrigins);

            float resetRange = GetAtomicDashRangeForHitCount(baseRange, hitCount, tuning);
            if (Mathf.Abs(resetRange - dashRange) <= 0.01f)
                break;

            dashRange = resetRange;
        }

        return hitCount;
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
        if (IsMultiBladePath())
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, MultiBladeVfxColor, GetNormalizedHeat());
        else if (IsAtomicSharpnessPath())
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, AtomicSharpnessVfxColor, GetNormalizedHeat());
        else
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, new Color(0.7f, 1f, 1f, 0.95f), GetNormalizedHeat());
    }

    private void ShowSlash(Vector3 origin, Vector3 direction, float range, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        if (IsMultiBladePath())
            _vfx.ShowSlash(origin, direction, range, tuning.BladeManualConeAngle, tuning.BladeVisualDuration, MultiBladeVfxColor);
        else if (IsAtomicSharpnessPath())
            _vfx.ShowSlash(origin, direction, range, tuning.BladeManualConeAngle, tuning.BladeVisualDuration, AtomicSharpnessVfxColor);
        else
            _vfx.ShowSlash(origin, direction, range, tuning.BladeManualConeAngle, tuning.BladeVisualDuration);
    }

    private void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        if (IsMultiBladePath())
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration, MultiBladeVfxColor);
        else if (IsAtomicSharpnessPath())
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration, AtomicSharpnessVfxColor);
        else
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration);
    }

    private void EnsureVfx()
    {
        if (_vfx == null)
            _vfx = RotatingBladeVfx.Create(Runtime?.Data?.PresentationProfile?.RotatingBlade?.RuntimeVfxPrefab);
    }

    private float GetNormalizedHeat() => Heat != null ? Heat.NormalizedHeat : 0f;

    private void EmitShotFeedback(
        WeaponFeedbackMode mode,
        Vector3 origin,
        Vector3 direction,
        float range,
        bool isAbilityDamage,
        float eventIntensity)
    {
        WeaponFeedbackContext context = new(
            Runtime,
            mode,
            GetNormalizedHeat(),
            origin,
            direction,
            isAbilityDamage: isAbilityDamage,
            explosionRadius: range,
            eventIntensity: eventIntensity,
            anchor: Owner);
        Feedback.OnShotFired(in context);
    }

    private void EmitImpactFeedback(
        Transform target,
        Vector3 impactOrigin,
        int damage,
        WeaponFeedbackMode mode,
        bool isAbilityDamage,
        bool kill,
        bool strongImpact)
    {
        Vector3 impactPosition = EnemyRegistry.GetAimPoint(target);
        Vector3 direction = impactPosition - impactOrigin;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Owner != null ? Owner.forward : Vector3.forward;
        Collider surfaceCollider = target.GetComponentInChildren<Collider>();
        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        WeaponFeedbackContext context = new(
            Runtime,
            mode,
            GetNormalizedHeat(),
            impactOrigin,
            direction,
            impactPosition,
            -direction,
            damage,
            isKill: kill,
            isAbilityDamage: isAbilityDamage,
            targetClass: WeaponEnemyClassifier.GetKind(target),
            surfaceType: ImpactSurfaceResolver.Resolve(surfaceCollider, damageable),
            eventIntensity: strongImpact ? 1.35f : 0.72f,
            target: target,
            anchor: target);
        Feedback.OnProjectileImpact(in context);
        Feedback.OnDamageConfirmed(in context);

        if (!strongImpact || Runtime?.Data?.PresentationProfile == null)
            return;
        WeaponPresentationContext finalImpact = new(
            WeaponPresentationCue.RotatingBladeMultiFinalImpact,
            Runtime,
            impactPosition,
            direction,
            1.35f,
            target,
            isAbilityDamage,
            anchor: target,
            mode: mode,
            upgradePath: Runtime.SelectedPath,
            weaponLevel: Runtime.Level,
            normalizedHeat: GetNormalizedHeat(),
            impactNormal: -direction,
            damageAmount: damage,
            isKill: kill,
            targetClass: WeaponEnemyClassifier.GetKind(target),
            surfaceType: ImpactSurfaceResolver.Resolve(surfaceCollider, damageable));
        Presentation.Emit(in finalImpact);
    }

    private static int GetRemainingHealth(IDamageable damageable)
    {
        if (damageable is EnemyHealth enemyHealth)
            return enemyHealth.CurrentHealth;
        if (damageable is WeaponDummyEnemy dummy)
            return dummy.CurrentHealth;
        if (damageable is Component component)
        {
            EnemyHealth parentHealth = component.GetComponentInParent<EnemyHealth>();
            if (parentHealth != null)
                return parentHealth.CurrentHealth;
            WeaponDummyEnemy parentDummy = component.GetComponentInParent<WeaponDummyEnemy>();
            if (parentDummy != null)
                return parentDummy.CurrentHealth;
        }
        return -1;
    }
}
