using System.Collections.Generic;
using UnityEngine;

public sealed class RotatingBladeWeapon : BasicProjectileWeapon
{
    protected override void CollectDiagnostics(List<WeaponDiagnosticSection> sections)
    {
        RotatingBladeTuning t = Runtime.Data.RotatingBlade;
        var automatic = DiagnosticMode("Automatic", 1f, GetAutoDamageInterval(t),
            GetScaledOrbitRadius(t), GetBladeCount(), 0f, knockbackScale: GetAtomicSharpnessKnockbackScale(GetAutomaticKnockbackScale(t)),
            weaponDamageScale: GetAtomicSharpnessDamageScale());
        var manual = DiagnosticMode("Manual", GetManualDamageScale(t),
            GetManualSwingInterval(t), GetScaledManualRange(t), GetBladeCount(), 1f,
            knockbackScale: GetAtomicSharpnessKnockbackScale(t.BladeManualKnockbackScale),
            weaponDamageScale: GetAtomicSharpnessDamageScale());
        var active = DiagnosticMode("Active Ability", 1f,
            0f, IsAtomicSharpnessPath() ? GetAtomicDashBaseRange(t) : GetScaledActiveRange(t), GetBladeCount(),
            WeaponMath.GetActiveAbilityAmmoCost(Runtime), true, IsAtomicSharpnessPath() ? 0f : t.BladeActiveKnockbackScale,
            weaponDamageScale: IsAtomicSharpnessPath() ? GetAtomicActiveDamageScale() : t.BladeActiveDamageScale);
        automatic.Add("Hit radius", GetScaledHitRadius(t), " m");
        automatic.Add("Rotations per second", GetRotationsPerSecond(t));
        manual.Add("Cone angle", GetManualConeAngle(t), " degrees");
        active.Add("Line width", GetScaledActiveLineWidth(t), " m");
        active.Add("Hit radius", GetScaledActiveHitRadius(t), " m");
        if (IsMultiBladePath())
        {
            manual.Add("Spacing between swings", GetMultiBladeActionInterval(), " s");
            active.Add("Spacing between thrusts", GetMultiBladeActionInterval(), " s");
        }
        if (IsAtomicSharpnessPath())
            active.Add("Dash range with one target", GetAtomicDashRangeForHitCount(GetAtomicDashBaseRange(t), 1, t), " m")
                .Add("Dash duration without targets", GetAtomicDashDurationForHitCount(0), " s")
                .Add("Post-dash invulnerability", GetAtomicActivePostDashInvulnerabilitySeconds(), " s")
                .Add("Range assumption", "Base range; each hit extends dash range and duration");
        sections.Add(automatic); sections.Add(manual); sections.Add(active);
    }

    public override float? GetManualAimConvergenceDistance()
    {
        if (!(WeaponStatAuthoringResolver.GetLevelRow(Runtime)?.Manual.RangeMeters.Enabled ?? false)) return null;
        return GetScaledManualRange(Runtime.Data.RotatingBlade);
    }

    public override WeaponGeometrySnapshot CaptureGeometry()
    {
        if (Runtime?.Data == null || Runtime.Data.RotatingBlade == null) return new WeaponGeometrySnapshot();
        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        var guides = new List<WeaponGeometryGuide>();
        float orbit = GetScaledOrbitRadius(tuning);
        float contactRadius = GetScaledHitRadius(tuning);
        if (orbit > 0f)
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Automatic, WeaponGeometryPurpose.ProjectilePath,
                WeaponGeometryShape.Circle, WeaponGeometryAnchor.Owner, radiusMeters: orbit,
                direction: WeaponGeometryDirection.OwnerForward));
        if (contactRadius > 0f)
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Automatic, WeaponGeometryPurpose.Hitbox,
                orbit > 0f ? WeaponGeometryShape.SweptCircle : WeaponGeometryShape.Sphere,
                WeaponGeometryAnchor.Owner, rangeMeters: orbit, radiusMeters: contactRadius,
                direction: WeaponGeometryDirection.OwnerForward));
        float manualRange = GetScaledManualRange(tuning);
        if (manualRange > 0f)
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.Hitbox,
                WeaponGeometryShape.SphericalCone, WeaponGeometryAnchor.Owner, rangeMeters: manualRange,
                fullAngleDegrees: GetManualConeAngle(tuning), direction: WeaponGeometryDirection.HorizontalAim));
        float activeRange = IsAtomicSharpnessPath() ? GetAtomicDashBaseRange(tuning) : GetScaledActiveRange(tuning);
        float activeRadius = GetScaledActiveHitRadius(tuning);
        if (activeRange > 0f && activeRadius > 0f)
            guides.Add(new WeaponGeometryGuide(WeaponStatMode.Ability, WeaponGeometryPurpose.Hitbox,
                WeaponGeometryShape.Capsule, WeaponGeometryAnchor.Owner, rangeMeters: activeRange,
                radiusMeters: activeRadius, direction: IsAtomicSharpnessPath()
                    ? WeaponGeometryDirection.HorizontalAim : WeaponGeometryDirection.Aim));
        return new WeaponGeometrySnapshot(guides.ToArray());
    }

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
    private readonly List<Transform> _bladeArcCandidates = new();
    private readonly List<Collider> _bladeArcColliders = new();
    private readonly List<float> _bladeArcDistances = new();
    private readonly Vector3[] _activeLinePoints = new Vector3[2];
    private readonly Vector3[] _bladeSweepPoints = new Vector3[181];

    private RotatingBladeVfx _vfx;
    private float _spinAngle;
    private float _lastAutoDamageSpinAngle;
    private double _spinDegreesSinceAutoDamage;
    private float _autoDamageTimer;
    private bool _multiBladeManualPending;
    private int _multiBladeManualSwingIndex;
    private int _multiBladeManualSwingCount;
    private float _multiBladeManualTimer;
    private float _multiBladeManualRange;
    private float _multiBladeManualDamageScale;
    private Vector3 _multiBladeManualDirection;
    private int _multiBladeManualSequenceId;
    private bool _multiBladeActivePending;
    private int _multiBladeActiveThrustIndex;
    private int _multiBladeActiveThrustCount;
    private float _multiBladeActiveTimer;
    private float _multiBladeActiveRange;
    private float _multiBladeActiveLineWidth;
    private Vector3 _multiBladeActiveDirection;
    private int _multiBladeActiveSequenceId;

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
        float damageInterval = GetAutoDamageInterval(tuning);
        if (float.IsPositiveInfinity(_autoDamageTimer) && !float.IsPositiveInfinity(damageInterval))
            _autoDamageTimer = 0f;
        _autoDamageTimer -= deltaTime;
        bool shouldDamage = !float.IsPositiveInfinity(damageInterval) && _autoDamageTimer <= 0f;
        if (shouldDamage)
            _autoDamageTimer = damageInterval;

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
                    1f,
                    impactOrigin,
                    knockbackScale,
                    WeaponFeedbackMode.Automatic,
                    actionSequenceId: 0,
                    damageKind: DamageFeedbackKind.SustainedContact,
                    weaponDamageScale: GetAtomicSharpnessDamageScale());
            }
        }

        if (shouldDamage)
        {
            _lastAutoDamageSpinAngle = _spinAngle;
            _spinDegreesSinceAutoDamage = 0d;
        }
    }

    // Performs repeated cone slashes while fire is held, spending one manual ammo per slash.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (TickPendingMultiBladeActions(deltaTime))
            return;

        float swingInterval = GetManualSwingInterval(Runtime.Data.RotatingBlade);
        if (float.IsPositiveInfinity(swingInterval))
            return;
        if (float.IsPositiveInfinity(FireTimer)) FireTimer = 0f;
        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        Vector3 slashDirection = aimDirection.sqrMagnitude > 0.0001f
            ? aimDirection.normalized
            : Owner != null ? Owner.forward : Vector3.forward;
        if (slashDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(1f, requireFullAmount: false))
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        FireTimer = swingInterval;

        Vector3 origin = GetManualSlashOrigin();
        float range = GetScaledManualRange(tuning);
        float damageScale = GetManualDamageScale(tuning);
        int swingCount = GetBladeCount();
        if (IsMultiBladePath() && swingCount > 1)
        {
            StartMultiBladeManualSwings(slashDirection, range, damageScale, swingCount);
            return;
        }

        int actionSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(
            DamageFeedbackKind.ManualMultiHit,
            1);
        ExecuteManualSwing(
            origin,
            slashDirection,
            range,
            damageScale,
            GetAtomicSharpnessKnockbackScale(tuning.BladeManualKnockbackScale),
            tuning,
            0,
            1,
            actionSequenceId);
        DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
    }

    // Thrusts forward in a thick line. Heat adds range in discrete 20% steps, capped by tuning.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        // Atomic Sharpness is a ground dash; its damage and movement remain planar.
        // Thrusts use the resolved player aim, including pitch and obstacle convergence.
        Vector3 thrustDirection = IsAtomicSharpnessPath()
            ? GetHorizontalAimDirection(aimDirection)
            : GetThrustAimDirection(aimDirection);
        if (thrustDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(WeaponMath.GetActiveAbilityAmmoCost(Runtime), requireFullAmount: false))
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

        int actionSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Ability, 1);
        ExecuteActiveThrust(
            origin,
            thrustDirection,
            range,
            lineWidth,
            tuning.BladeActiveKnockbackScale,
            tuning,
            0,
            1,
            actionSequenceId);
        DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
        CompleteActiveAbility();
    }

    public override bool CanCrit() => true;

    private void TickSpin(float deltaTime, RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        WeaponOptionalFloat authored = BladeOverrides?.RotationsPerSecond ?? default;
        float spinRate = tuning.BladeBaseSpinDegreesPerSecond * attackSpeed * weaponRate * GetAtomicSharpnessSpinMultiplier();
        double spinStep = authored.Enabled
            ? (double)authored.Value * 360d * attackSpeed * Mathf.Max(0f, deltaTime)
            : (double)spinRate * deltaTime;
        // Keep ordinary legacy rounding, but retain complete revolutions for hit sweeps.
        _spinAngle = authored.Enabled
            ? (float)((_spinAngle + spinStep) % 360d)
            : Mathf.Repeat(_spinAngle + spinRate * deltaTime, 360f);
        _spinDegreesSinceAutoDamage = System.Math.Min(360d, _spinDegreesSinceAutoDamage + spinStep);
    }

    private float GetAutoDamageInterval(RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float interval = Mathf.Max(0.01f, tuning.BladeAutoDamageInterval);
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Automatic,
            WeaponStatField.AttackCyclesPerSecond, weaponRate / interval);
        if (!rate.IsAuthored) return interval / Mathf.Max(0.05f, attackSpeed * weaponRate);
        return rate.Value <= 0f ? float.PositiveInfinity : 1f / (rate.Value * attackSpeed);
    }

    private float GetManualSwingInterval(RotatingBladeTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float interval = Mathf.Max(0.01f, tuning.BladeManualCooldown);
        WeaponResolvedStat rate = WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.AttackCyclesPerSecond, weaponRate / interval);
        if (!rate.IsAuthored) return interval / Mathf.Max(0.05f, attackSpeed * weaponRate);
        return rate.Value <= 0f ? float.PositiveInfinity : 1f / (rate.Value * attackSpeed);
    }

    private Vector3 GetBladeCenter(RotatingBladeTuning tuning, int bladeIndex = 0, int bladeCount = 1)
    {
        return GetBladeCenter(tuning, bladeIndex, bladeCount, _spinAngle);
    }

    private Vector3 GetBladeCenter(RotatingBladeTuning tuning, int bladeIndex, int bladeCount, float spinAngle)
    {
        return GetBladeCenterAtRadius(GetScaledOrbitRadius(tuning), bladeIndex, bladeCount, spinAngle);
    }

    private Vector3 GetBladeCenterAtRadius(float radius, int bladeIndex, int bladeCount, float spinAngle)
    {
        Vector3 baseDirection = Vector3.forward;
        float offset = bladeCount <= 1 ? 0f : (360f / bladeCount) * bladeIndex;
        Vector3 orbitDirection = Quaternion.AngleAxis(spinAngle + offset, Vector3.up) * baseDirection;
        return GetOwnerOrigin() + orbitDirection * radius;
    }

    // Authored angular rates use the circular arc itself so narrow contact radii cannot
    // tunnel between polyline chords. Target eligibility and closest-64 ordering match
    // EnemyRegistry.CollectClosestNearPolyline; disabled authoring retains that path.
    private int CollectBladeArcTargets(Vector3 origin, float radius, float startAngle, float sweepDegrees, float hitRadius)
    {
        _targets.Clear();
        _hitOrigins.Clear();
        _bladeArcDistances.Clear();
        if (hitRadius <= 0f) return 0;
        EnemyRegistry.CollectActive(_bladeArcCandidates);
        float hitRadiusSqr = hitRadius * hitRadius;
        foreach (Transform candidate in _bladeArcCandidates)
        {
            Vector3 closest = ClosestBladeArcPoint(candidate.position, origin, radius, startAngle, sweepDegrees);
            float distance = (candidate.position - closest).sqrMagnitude;
            candidate.GetComponentsInChildren(false, _bladeArcColliders);
            foreach (Collider collider in _bladeArcColliders)
            {
                if (collider == null || !collider.enabled) continue;
                Vector3 arcPoint = ClosestBladeArcPoint(collider.bounds.center, origin, radius, startAngle, sweepDegrees);
                float colliderDistance = (arcPoint - collider.ClosestPoint(arcPoint)).sqrMagnitude;
                if (colliderDistance >= distance) continue;
                distance = colliderDistance;
                closest = arcPoint;
            }
            _bladeArcColliders.Clear();
            if (distance > hitRadiusSqr) continue;
            int index = 0;
            while (index < _bladeArcDistances.Count && _bladeArcDistances[index] <= distance) index++;
            if (index >= MaxContactTargets) continue;
            _bladeArcDistances.Insert(index, distance);
            _targets.Insert(index, candidate);
            _hitOrigins.Insert(index, closest);
            if (_targets.Count > MaxContactTargets)
            {
                _bladeArcDistances.RemoveAt(MaxContactTargets);
                _targets.RemoveAt(MaxContactTargets);
                _hitOrigins.RemoveAt(MaxContactTargets);
            }
        }
        _bladeArcCandidates.Clear();
        return _targets.Count;
    }

    private static Vector3 ClosestBladeArcPoint(Vector3 point, Vector3 origin, float radius, float startAngle, float sweepDegrees)
    {
        Vector3 delta = point - origin;
        float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        float relativeAngle = Mathf.Repeat(angle - startAngle, 360f);
        if (sweepDegrees < 360f && relativeAngle > sweepDegrees)
        {
            Vector3 start = origin + Quaternion.AngleAxis(startAngle, Vector3.up) * Vector3.forward * radius;
            Vector3 end = origin + Quaternion.AngleAxis(startAngle + sweepDegrees, Vector3.up) * Vector3.forward * radius;
            return (point - start).sqrMagnitude <= (point - end).sqrMagnitude ? start : end;
        }
        return origin + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;
    }

    private int CollectBladeContactTargets(
        RotatingBladeTuning tuning,
        int bladeIndex,
        int bladeCount,
        float previousSpinAngle,
        float currentSpinAngle,
        float hitRadius)
    {
        bool authoredSpin = BladeOverrides?.RotationsPerSecond.Enabled ?? false;
        float sweepDegrees = authoredSpin && _spinDegreesSinceAutoDamage >= 360d ? 360f
            : Mathf.Repeat(currentSpinAngle - previousSpinAngle + 360f, 360f);
        float degreesPerSegment = sweepDegrees >= 360f ? 2f : 20f;
        int maximumSegments = authoredSpin ? _bladeSweepPoints.Length - 1 : 11;
        int segmentCount = Mathf.Clamp(Mathf.CeilToInt(sweepDegrees / degreesPerSegment), 1, maximumSegments);
        float orbitRadius = GetScaledOrbitRadius(tuning);
        if (authoredSpin)
        {
            float bladeOffset = bladeCount <= 1 ? 0f : 360f / bladeCount * bladeIndex;
            return CollectBladeArcTargets(GetOwnerOrigin(), orbitRadius, previousSpinAngle + bladeOffset, sweepDegrees, hitRadius);
        }
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float spinAngle = previousSpinAngle + sweepDegrees * t;
            _bladeSweepPoints[i] = GetBladeCenterAtRadius(orbitRadius, bladeIndex, bladeCount, spinAngle);
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

        WeaponOptionalInt extra = BladeOverrides?.ExtraBladeCount ?? default;
        return 1 + (extra.Enabled ? extra.Value : Mathf.Max(1, Runtime.Level - 5));
    }

    private Vector3 GetThrustAimDirection(Vector3 aimDirection)
    {
        if (aimDirection.sqrMagnitude > 0.0001f)
            return aimDirection.normalized;

        return Owner != null ? Owner.forward : Vector3.forward;
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
        if (!ReticleAimProvider.TryGetGameplayRay(Camera.main, out Ray ray))
            return false;

        direction = ray.direction;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
    }

    // The complete manual cone starts at the player; its outer radius is the slash VFX.
    private Vector3 GetManualSlashOrigin() => GetOwnerOrigin();

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

    private float GetAtomicActiveDashSegmentSeconds() => ResolveBlade(BladeOverrides?.AtomicDashSegmentSeconds ?? default, AtomicActiveDashSegmentSeconds);

    private float GetAtomicActivePostDashInvulnerabilitySeconds() => ResolveBlade(BladeOverrides?.AtomicPostDashInvulnerabilitySeconds ?? default, AtomicActivePostDashInvulnerabilitySeconds);

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
        WeaponOptionalFloat range = BladeOverrides?.AtomicDashRangeMeters ?? default;
        return range.Enabled ? range.Value * GetAreaSizeMultiplier()
            : GetLegacyScaledManualRange(tuning) * Mathf.Max(0f, tuning.AtomicDashBaseRangeMultiplier);
    }

    private float GetAtomicDashRangeForHitCount(float baseRange, int hitCount, RotatingBladeTuning tuning)
    {
        float perHitMultiplier = ResolveBlade(BladeOverrides?.AtomicRangePerHitMultiplier ?? default, Mathf.Max(0f, tuning.AtomicDashRangePerHitMultiplier));
        return Mathf.Max(0f, baseRange) * (1f + Mathf.Max(0, hitCount) * perHitMultiplier);
    }

    private float GetMultiBladeActionInterval() => ResolveBlade(BladeOverrides?.MultiBladeActionIntervalSeconds ?? default, 0.1f);

    private WeaponBladeStatOverrides BladeOverrides => WeaponStatAuthoringResolver.GetLevelRow(Runtime)?.Blade;

    private float ResolveBlade(WeaponOptionalFloat value, float legacy) =>
        WeaponStatAuthoringResolver.ResolveOptional(Runtime, value, legacy).Value;

    private float GetManualConeAngle(RotatingBladeTuning tuning) =>
        ResolveBlade(BladeOverrides?.ManualConeAngleDegrees ?? default, tuning.BladeManualConeAngle);

    private float GetRotationsPerSecond(RotatingBladeTuning tuning) =>
        ResolveBlade(BladeOverrides?.RotationsPerSecond ?? default,
            tuning.BladeBaseSpinDegreesPerSecond * WeaponMath.GetAttackRateMultiplier(Runtime)
            * GetAtomicSharpnessSpinMultiplier() / 360f)
        * WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);

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
        return ResolveBlade(BladeOverrides?.OrbitRadiusMeters ?? default, Mathf.Max(0f, tuning.BladeOrbitRadius)) * GetAreaSizeMultiplier();
    }

    private float GetScaledHitRadius(RotatingBladeTuning tuning)
    {
        float areaSize = GetAreaSizeMultiplier();
        WeaponOptionalFloat radius = BladeOverrides?.AutomaticHitRadiusMeters ?? default;
        if (radius.Enabled) return radius.Value * areaSize;
        float scaledBladeWidth = Mathf.Max(0.05f, tuning.BladeHitRadius) * areaSize;
        float orbitGrowth = Mathf.Max(0f, tuning.BladeOrbitRadius) * Mathf.Max(0f, areaSize - 1f);
        return scaledBladeWidth + orbitGrowth;
    }

    private float GetScaledManualRange(RotatingBladeTuning tuning)
    {
        return WeaponStatAuthoringResolver.ResolveModeValue(Runtime, WeaponStatMode.Manual,
            WeaponStatField.RangeMeters, Mathf.Max(0f, tuning.BladeManualRange)).Value * GetAreaSizeMultiplier();
    }

    // Legacy global tuning is a shared source. A new Manual row override is mode-local.
    private float GetLegacyScaledManualRange(RotatingBladeTuning tuning) =>
        Mathf.Max(0f, tuning.BladeManualRange) * GetAreaSizeMultiplier();

    private float GetScaledActiveRange(RotatingBladeTuning tuning)
    {
        float stepPercent = Mathf.Max(0.01f, tuning.BladeActiveHeatStepPercent);
        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        float bonusSteps = Mathf.Floor(heatPercent / stepPercent);
        float multiplier = Mathf.Min(
            Mathf.Max(1f, tuning.BladeActiveBaseRangeMultiplier) + bonusSteps,
            Mathf.Max(tuning.BladeActiveBaseRangeMultiplier, tuning.BladeActiveMaxRangeMultiplier));

        WeaponOptionalFloat range = BladeOverrides?.ThrustLengthMeters ?? default;
        if (!range.Enabled) return GetLegacyScaledManualRange(tuning) * multiplier;
        // Explicit length is the zero-heat thrust; heat retains its relative extension.
        float baseMultiplier = Mathf.Max(1f, tuning.BladeActiveBaseRangeMultiplier);
        return range.Value * GetAreaSizeMultiplier() * (multiplier / baseMultiplier);
    }

    private float GetScaledActiveLineWidth(RotatingBladeTuning tuning)
    {
        WeaponOptionalFloat radius = IsAtomicSharpnessPath()
            ? BladeOverrides?.AtomicHitRadiusMeters ?? default
            : BladeOverrides?.AbilityHitRadiusMeters ?? default;
        return radius.Enabled ? radius.Value * GetAreaSizeMultiplier() * 2f
            : Mathf.Max(0.05f, tuning.BladeActiveLineWidth) * GetAreaSizeMultiplier();
    }

    private float GetScaledActiveHitRadius(RotatingBladeTuning tuning)
    {
        WeaponOptionalFloat radius = IsAtomicSharpnessPath()
            ? BladeOverrides?.AtomicHitRadiusMeters ?? default
            : BladeOverrides?.AbilityHitRadiusMeters ?? default;
        return radius.Enabled ? radius.Value * GetAreaSizeMultiplier()
            : GetScaledActiveLineWidth(tuning) * (IsAtomicSharpnessPath() ? .65f : .5f);
    }

    private void ApplyBladeDamage(
        Transform target,
        float damageScale,
        Vector3 impactOrigin,
        float knockbackScale,
        WeaponFeedbackMode feedbackMode,
        bool isAbilityDamage = false,
        bool strongImpact = false,
        int actionSequenceId = 0,
        DamageFeedbackKind damageKind = DamageFeedbackKind.Direct,
        float weaponDamageScale = 1f)
    {
        if (target == null)
            return;

        IDamageable damageable = target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        WeaponDamageContext damageContext = CreateDamageContext(
                damageScale,
                isAbilityDamage,
                knockbackScale,
                mode: isAbilityDamage ? WeaponStatMode.Ability
                    : feedbackMode == WeaponFeedbackMode.Automatic ? WeaponStatMode.Automatic : WeaponStatMode.Manual,
                weaponDamageScale: weaponDamageScale)
            .WithFeedbackMetadata(actionSequenceId, damageKind);
        // Preserve the legacy one-damage fallback used by lightweight sandbox/test
        // targets when no PlayerStats dependency is available.
        int finalDamage = damageContext.IsValid ? damageContext.CalculateDamage(target) : 1;
        string weaponId = Runtime?.Data != null ? Runtime.Data.WeaponId : null;
        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            damageable,
            finalDamage,
            sourceWeaponId: weaponId);
        if (result.Applied)
        {
            if (damageContext.IsValid)
                EnemyKnockbackReceiver.TryApply(damageable, impactOrigin, damageContext.CalculateKnockback(finalDamage));
            else
                ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
            EmitImpactFeedback(
                target,
                impactOrigin,
                result.ResolvedDamage,
                feedbackMode,
                isAbilityDamage,
                result.Killed,
                strongImpact,
                damageContext.IsCritical,
                damageContext.ReferenceDamage,
                actionSequenceId,
                damageKind,
                result.IsAuthoritative);
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
        _multiBladeManualSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(
            DamageFeedbackKind.ManualMultiHit,
            _multiBladeManualSwingCount);
        ExecuteNextMultiBladeManualSwing();
    }

    private void ExecuteNextMultiBladeManualSwing()
    {
        if (!_multiBladeManualPending)
            return;

        RotatingBladeTuning tuning = Runtime.Data.RotatingBlade;
        int swing = _multiBladeManualSwingIndex;
        Vector3 origin = GetManualSlashOrigin();
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
            _multiBladeManualSwingCount,
            _multiBladeManualSequenceId);

        _multiBladeManualSwingIndex++;
        if (_multiBladeManualSwingIndex >= _multiBladeManualSwingCount)
        {
            _multiBladeManualPending = false;
            DamageFeedbackSequenceRuntime.CompleteSequence(_multiBladeManualSequenceId);
            _multiBladeManualSequenceId = 0;
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
        int swingCount,
        int actionSequenceId)
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

        int hitCount = EnemyRegistry.CollectClosestInCone(
            origin,
            swingDirection,
            range,
            GetManualConeAngle(tuning),
            MaxManualTargets,
            _targets);

        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(
                _targets[i],
                damageScale,
                origin,
                GetAtomicSharpnessKnockbackScale(knockbackScale),
                WeaponFeedbackMode.Manual,
                strongImpact: strongImpact,
                actionSequenceId: actionSequenceId,
                damageKind: DamageFeedbackKind.ManualMultiHit,
                weaponDamageScale: GetAtomicSharpnessDamageScale());
        DamageFeedbackSequenceRuntime.CompleteContributor(actionSequenceId);
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
        _multiBladeActiveSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(
            DamageFeedbackKind.Ability,
            _multiBladeActiveThrustCount);
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
            _multiBladeActiveThrustCount,
            _multiBladeActiveSequenceId);

        _multiBladeActiveThrustIndex++;
        if (_multiBladeActiveThrustIndex >= _multiBladeActiveThrustCount)
        {
            _multiBladeActivePending = false;
            DamageFeedbackSequenceRuntime.CompleteSequence(_multiBladeActiveSequenceId);
            _multiBladeActiveSequenceId = 0;
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
        int thrustCount,
        int actionSequenceId)
    {
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * range;
        bool strongImpact = IsMultiBladePath() && thrustCount > 1 && ShouldApplyMultiBladeKnockback(thrustIndex, thrustCount);
        ShowThrust(origin, direction, range, lineWidth, tuning);
        EmitShotFeedback(
            WeaponFeedbackMode.Active,
            origin,
            direction,
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
                1f,
                impactOrigin,
                knockbackScale,
                WeaponFeedbackMode.Active,
                isAbilityDamage: true,
                strongImpact: strongImpact,
                actionSequenceId: actionSequenceId,
                damageKind: DamageFeedbackKind.Ability,
                weaponDamageScale: IsAtomicSharpnessPath() ? GetAtomicActiveDamageScale() : tuning.BladeActiveDamageScale);
        }
        DamageFeedbackSequenceRuntime.CompleteContributor(actionSequenceId);
    }

    private void ExecuteAtomicSharpnessDash(Vector3 origin, Vector3 direction, RotatingBladeTuning tuning)
    {
        int actionSequenceId = DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Ability, 1);
        float baseRange = GetAtomicDashBaseRange(tuning);
        float lineWidth = GetScaledActiveLineWidth(tuning);

        int hitCount = CollectAtomicDashTargets(origin, direction, baseRange, lineWidth, tuning);
        float dashDuration = GetAtomicDashDurationForHitCount(hitCount);
        float dashRange = GetAtomicDashRangeForHitCount(baseRange, hitCount, tuning);
        _activeLinePoints[0] = origin;
        _activeLinePoints[1] = origin + direction * dashRange;
        EnsureVfx();
        _vfx.ShowDash(origin, direction, dashRange, lineWidth, dashDuration, AtomicSharpnessVfxColor,
            BladeOverrides?.AtomicHitRadiusMeters.Enabled ?? false);
        EmitShotFeedback(
            WeaponFeedbackMode.Active,
            origin,
            direction,
            dashRange,
            isAbilityDamage: true,
            eventIntensity: Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(hitCount / 4f)));

        Owner?.GetComponent<PlayerHealth>()?.GrantInvulnerability(GetAtomicActiveInvulnerabilityDuration(dashDuration));
        WeaponBladeStatOverrides authored = BladeOverrides;
        bool authoredMotion = authored != null && (authored.AtomicDashRangeMeters.Enabled
            || authored.AtomicRangePerHitMultiplier.Enabled || authored.AtomicDashSegmentSeconds.Enabled);
        float dashSpeed = authoredMotion
            ? (dashDuration > 0f ? dashRange / dashDuration : 0f)
            : Mathf.Max(1f, baseRange / Mathf.Max(0.05f, GetAtomicActiveDashSegmentSeconds()));
        if ((!authoredMotion || baseRange > 0f) && dashDuration > 0f)
            Owner?.GetComponent<PlayerMovement>()?.ApplyWeaponDash(direction, dashSpeed, dashDuration, restartTimer: authoredMotion);

        for (int i = 0; i < hitCount; i++)
        {
            Vector3 impactOrigin = i < _hitOrigins.Count ? _hitOrigins[i] : origin;
            ApplyBladeDamage(
                _targets[i],
                1f,
                impactOrigin,
                0f,
                WeaponFeedbackMode.Active,
                isAbilityDamage: true,
                actionSequenceId: actionSequenceId,
                damageKind: DamageFeedbackKind.Ability,
                weaponDamageScale: IsAtomicSharpnessPath() ? GetAtomicActiveDamageScale() : tuning.BladeActiveDamageScale);
        }
        DamageFeedbackSequenceRuntime.CompleteContributor(actionSequenceId);
        DamageFeedbackSequenceRuntime.CompleteSequence(actionSequenceId);
    }

    private int CollectAtomicDashTargets(Vector3 origin, Vector3 direction, float baseRange, float lineWidth, RotatingBladeTuning tuning)
    {
        WeaponBladeStatOverrides authored = BladeOverrides;
        bool hasAuthoredRange = authored?.AtomicDashRangeMeters.Enabled ?? false;
        bool hasAuthoredRadius = authored?.AtomicHitRadiusMeters.Enabled ?? false;
        if (hasAuthoredRange && baseRange <= 0f || hasAuthoredRadius && lineWidth <= 0f)
        {
            _targets.Clear();
            _hitOrigins.Clear();
            return 0;
        }
        float dashRange = hasAuthoredRange ? baseRange : Mathf.Max(0.01f, baseRange);
        int hitCount = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            _activeLinePoints[0] = origin;
            _activeLinePoints[1] = origin + direction * dashRange;
            hitCount = EnemyRegistry.CollectClosestNearPolyline(
                _activeLinePoints,
                _activeLinePoints.Length,
                GetScaledActiveHitRadius(tuning),
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
        Quaternion frame = Quaternion.LookRotation(direction,
            Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);
        float angle = (actionIndex - (Mathf.Max(1, actionCount) - 1) * 0.5f) * ResolveBlade(BladeOverrides?.MultiBladeSpreadAngleDegrees ?? default, 8f);
        return frame * (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward);
    }

    private void ShowOrbit(Vector3 bladeCenter, float hitRadius, RotatingBladeTuning tuning)
    {
        if (Owner == null)
            return;

        EnsureVfx();
        if (IsMultiBladePath())
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, MultiBladeVfxColor, GetNormalizedHeat(), BladeOverrides?.AutomaticHitRadiusMeters.Enabled ?? false);
        else if (IsAtomicSharpnessPath())
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, AtomicSharpnessVfxColor, GetNormalizedHeat(), BladeOverrides?.AutomaticHitRadiusMeters.Enabled ?? false);
        else
            _vfx.ShowOrbit(GetOwnerOrigin(), bladeCenter, hitRadius, tuning.BladeVisualDuration, new Color(0.7f, 1f, 1f, 0.95f), GetNormalizedHeat(), BladeOverrides?.AutomaticHitRadiusMeters.Enabled ?? false);
    }

    private void ShowSlash(Vector3 origin, Vector3 direction, float range, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        if (IsMultiBladePath())
            _vfx.ShowSlash(origin, direction, range, GetManualConeAngle(tuning), tuning.BladeVisualDuration, MultiBladeVfxColor);
        else if (IsAtomicSharpnessPath())
            _vfx.ShowSlash(origin, direction, range, GetManualConeAngle(tuning), tuning.BladeVisualDuration, AtomicSharpnessVfxColor);
        else
            _vfx.ShowSlash(origin, direction, range, GetManualConeAngle(tuning), tuning.BladeVisualDuration);
    }

    private void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, RotatingBladeTuning tuning)
    {
        EnsureVfx();
        if (IsMultiBladePath())
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration, MultiBladeVfxColor,
                BladeOverrides?.AbilityHitRadiusMeters.Enabled ?? false);
        else if (IsAtomicSharpnessPath())
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration, AtomicSharpnessVfxColor,
                BladeOverrides?.AtomicHitRadiusMeters.Enabled ?? false);
        else
            _vfx.ShowThrust(origin, direction, range, lineWidth, tuning.BladeVisualDuration, new Color(.25f, .95f, 1f, .85f),
                BladeOverrides?.AbilityHitRadiusMeters.Enabled ?? false);
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
        bool strongImpact,
        bool isCritical,
        float referenceDamage,
        int actionSequenceId,
        DamageFeedbackKind damageKind,
        bool authoritative)
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
            isCritical: isCritical,
            isKill: kill,
            isAbilityDamage: isAbilityDamage,
            targetClass: WeaponEnemyClassifier.GetKind(target),
            surfaceType: ImpactSurfaceResolver.Resolve(surfaceCollider, damageable),
            eventIntensity: strongImpact ? 1.35f : 0.72f,
            target: target,
            anchor: target,
            referenceDamage: referenceDamage,
            actionSequenceId: actionSequenceId,
            damageKind: damageKind);
        Feedback.OnProjectileImpact(in context);
        if (authoritative && damage > 0)
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
            isCritical: isCritical,
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
