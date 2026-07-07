using System.Collections.Generic;
using UnityEngine;

public sealed class AutomaticCannonWeapon : BasicProjectileWeapon
{
    private const float ContinuousFireActiveBulletsPerSecond = 40f;
    private const float HeadHunterPierceRadius = 0.45f;
    private const float HeadHunterActiveMinimumRange = 1000f;
    private const float HeadHunterProjectileVisualSpeedMultiplier = 3f;
    private const float HeadHunterFallbackProjectileSpeed = 54f;
    private const float DefaultLineBurstShotInterval = 0.05f;

    private readonly List<Transform> _piercingTargets = new();
    private readonly List<Vector3> _piercingHitOrigins = new();
    private readonly List<PendingHeadHunterImpact> _pendingHeadHunterImpacts = new();
    private readonly Vector3[] _piercingLine = new Vector3[2];

    private bool _lineBurstActive;
    private int _lineBurstRemaining;
    private int _lineBurstIndex;
    private float _lineBurstTimer;
    private Vector3 _lineBurstDirection;
    private float _lineBurstDamageScale;
    private float _lineBurstSpacing;
    private float _lineBurstScatterDegrees;
    private float _lineBurstShotInterval;
    private bool _lineBurstEliteOrBoss;

    private bool _continuousFireActive;
    private float _continuousFireActiveRemainingDuration;
    private float _continuousFireActiveShotAccumulator;
    private int _continuousFireActiveShotsRemaining;
    private Vector3 _continuousFireActiveDirection;

    private HeadHunterChargeVfx _headHunterChargeVfx;
    private bool _headHunterActiveCharging;
    private float _headHunterActiveChargeTimer;
    private Vector3 _headHunterChargedDirection;
    private bool _headHunterManualFireHeld;

    public AutomaticCannonWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Fires three-round burst in automatic mode.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        TickHeadHunterPendingImpacts(deltaTime);
        TickPendingLineBurst(deltaTime);
        if (_headHunterActiveCharging)
        {
            TickHeadHunterActiveCharge(deltaTime, aimDirection);
            return;
        }

        if (Runtime.State != WeaponState.Automatic)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        float targetRange = Mathf.Max(0f, Runtime.Data.BaseRange);
        if (!Targeting.TryGetTarget(Runtime, Owner, targetRange, aimDirection, out Transform target))
            return;

        Vector3 targetPoint = EnemyRegistry.GetAimPoint(target);
        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        FireTimer = GetAutomaticFireInterval(tuning);
        if (IsHeadHunterPath())
        {
            FireHeadHunterPiercingLine(
                targetPoint - Spawn.position,
                GetHeadHunterPierceLimit(),
                GetHeadHunterProjectileRange(),
                allowWeakPointHits: false,
                isAbilityDamage: false,
                label: null);
            return;
        }

        FireLineBurst(
            targetPoint - Spawn.position,
            GetAutomaticShotCount(tuning),
            GetHeatDamageMultiplier() * GetHeadHunterScale(target),
            tuning.CannonAutoLineSpacing,
            tuning.CannonAutoAccuracySpreadDegrees,
            tuning.CannonBurstProjectileScatterDegrees,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires five-round burst in manual mode.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        TickHeadHunterPendingImpacts(deltaTime);
        TickPendingLineBurst(deltaTime);

        if (Runtime.State != WeaponState.Manual)
        {
            _headHunterManualFireHeld = false;
            return;
        }

        if (TickHeadHunterActiveCharge(deltaTime, aimDirection))
        {
            if (IsHeadHunterPath())
                _headHunterManualFireHeld = isFiring;
            return;
        }

        if (TickContinuousFireActive(deltaTime, aimDirection))
            return;

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        bool isHeadHunter = IsHeadHunterPath();
        if (isHeadHunter)
        {
            if (!ConsumeHeadHunterManualClick(isFiring))
                return;
        }
        else
        {
            _headHunterManualFireHeld = false;
            if (!isFiring)
                return;
        }

        if (FireTimer > 0f)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        int manualShotCount = GetManualShotCount(tuning);
        int bulletsToFire = Mathf.Clamp(Mathf.CeilToInt(Runtime.CurrentAmmo), 1, manualShotCount);
        if (!TrySpendManualAmmo(bulletsToFire, requireFullAmount: false))
            return;

        FireTimer = AutomaticCannonFireLogic.GetManualBurstInterval(
            tuning.CannonManualBurstsPerSecond,
            WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier),
            WeaponMath.GetAttackRateMultiplier(Runtime) * GetManualAttackSpeedPathMultiplier());

        if (isHeadHunter)
        {
            FireHeadHunterPiercingLine(
                aimDirection,
                GetHeadHunterPierceLimit(),
                GetHeadHunterProjectileRange(),
                allowWeakPointHits: true,
                isAbilityDamage: false,
                label: null);
            return;
        }

        FireLineBurst(
            aimDirection,
            bulletsToFire,
            1f,
            tuning.CannonManualLineSpacing,
            0f,
            tuning.CannonBurstProjectileScatterDegrees,
            false);
    }

    private bool ConsumeHeadHunterManualClick(bool isFiring)
    {
        if (!isFiring)
        {
            _headHunterManualFireHeld = false;
            return false;
        }

        if (_headHunterManualFireHeld)
            return false;

        _headHunterManualFireHeld = true;
        return true;
    }

    // Fires spread burst active ability, scaled by heat.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(GetActiveAbilityAmmoCost(), requireFullAmount: false))
            return;

        if (IsHeadHunterPath())
        {
            BeginHeadHunterActiveCharge(aimDirection);
            return;
        }

        if (IsContinuousFirePath())
        {
            BeginContinuousFireActive(aimDirection);
            return;
        }

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        int extra = GetActiveHeatBonusBulletCount(tuning);
        FireScatterBurst(
            aimDirection,
            Mathf.Max(1, tuning.CannonActiveBaseBulletCount) + extra,
            Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB ? 1.25f : 1f,
            tuning.CannonAbilityScatterRadius);
        CompleteActiveAbility();
    }

    // Automatic cannon can critically strike, with a custom multiplier override below.
    public override bool CanCrit() => true;

    // Automatic cannon gains damage at 25/50/75 heat, not fire-rate scaling.
    protected override float GetHeatFireRateMultiplier() => 1f;

    // Critical hits deal double the normal critical damage effect.
    protected override float GetCritMultiplierOverride()
    {
        float multiplier = Runtime?.Data != null ? Runtime.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride : AutomaticCannonTuning.Defaults.CannonCriticalDamageMultiplierOverride;
        if (Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB)
            multiplier *= 1.5f;
        return multiplier;
    }

    // Converts 25/50/75 heat thresholds into stacking damage bonuses.
    private float GetHeatDamageMultiplier()
    {
        if (Heat == null)
            return 1f;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        float stepPercent = Mathf.Max(0.01f, tuning.CannonHeatDamageThresholdStepPercent);
        int maxThresholds = Mathf.Max(0, tuning.CannonHeatDamageThresholdCount);
        float percent = Heat.NormalizedHeat * 100f;
        int thresholds = Mathf.Clamp(Mathf.FloorToInt(percent / stepPercent), 0, maxThresholds);
        return 1f + thresholds * Mathf.Max(0f, tuning.CannonHeatDamageBonusPerThreshold);
    }

    // Converts each configured heat step into one extra active ability projectile.
    private int GetActiveHeatBonusBulletCount(AutomaticCannonTuning tuning)
    {
        if (Heat == null)
            return 0;

        float stepPercent = Mathf.Max(0.01f, tuning.CannonActiveHeatBulletStepPercent);
        return Mathf.FloorToInt((Heat.NormalizedHeat * 100f) / stepPercent);
    }

    private bool IsContinuousFirePath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

    private bool IsHeadHunterPath() =>
        Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

    private float GetContinuousFireAttackSpeedMultiplier()
    {
        if (!IsContinuousFirePath())
            return 1f;

        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        float heatBonus = Mathf.Floor(heatPercent / 2f) * 0.01f;
        return 1.25f + heatBonus;
    }

    private float GetContinuousFireAutoAttackSpeedMultiplier(AutomaticCannonTuning tuning)
    {
        if (!IsContinuousFirePath())
            return 1f;

        float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
        float heatBonus = Mathf.Floor(heatPercent / 2f) * 0.01f;
        return Mathf.Max(0.01f, tuning.ContinuousFireAutoAttackSpeedMultiplier) + heatBonus;
    }

    private float GetContinuousFireManualAttackSpeedMultiplier()
    {
        return GetContinuousFireAttackSpeedMultiplier() * (IsContinuousFirePath() ? 1.5f : 1f);
    }

    private float GetManualAttackSpeedPathMultiplier()
    {
        if (IsContinuousFirePath())
            return GetContinuousFireManualAttackSpeedMultiplier();
        if (IsHeadHunterPath())
            return 1.75f;
        return 1f;
    }

    private float GetAutomaticFireInterval(AutomaticCannonTuning tuning)
    {
        float attackSpeed = WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier);
        float weaponRate = WeaponMath.GetAttackRateMultiplier(Runtime);
        float burstsPerSecond = Mathf.Max(0.01f, tuning.CannonAutoBurstsPerSecond);
        float interval = 1f / Mathf.Max(0.05f, burstsPerSecond * attackSpeed * weaponRate);
        if (IsContinuousFirePath())
            return interval / Mathf.Max(0.01f, GetContinuousFireAutoAttackSpeedMultiplier(tuning));
        if (IsHeadHunterPath())
            return interval / Mathf.Max(0.01f, tuning.HeadHunterAutoAttackSpeedMultiplier);
        return interval;
    }

    private int GetAutomaticShotCount(AutomaticCannonTuning tuning)
    {
        if (IsContinuousFirePath() || IsHeadHunterPath())
            return 1;

        return Mathf.Max(1, tuning.CannonAutoBurstCount);
    }

    private int GetManualShotCount(AutomaticCannonTuning tuning)
    {
        if (IsContinuousFirePath() || IsHeadHunterPath())
            return 1;

        return Mathf.Max(1, tuning.CannonManualBurstCount);
    }

    private float GetContinuousFireActiveDuration()
    {
        float heatBonus = Heat != null ? Mathf.Floor(Heat.NormalizedHeat * 2f) : 0f;
        return 2f + heatBonus;
    }

    private float GetContinuousFireActiveBulletsPerSecond()
    {
        return ContinuousFireActiveBulletsPerSecond;
    }

    private int GetContinuousFireActiveBulletCount()
    {
        return Mathf.Max(1, Mathf.RoundToInt(GetContinuousFireActiveBulletsPerSecond() * GetContinuousFireActiveDuration()));
    }

    private float GetActiveAbilityAmmoCost()
    {
        return WeaponMath.GetActiveAbilityAmmoCost(Runtime);
    }

    private float GetHeadHunterWeakPointScale()
    {
        if (!IsHeadHunterPath())
            return 1f;

        float heat = Heat != null ? Heat.NormalizedHeat : 0f;
        int extraSteps = Mathf.FloorToInt(heat / 0.2f);
        return Mathf.Clamp(5f + extraSteps, 5f, 10f);
    }

    private int GetContinuousFireBonus()
    {
        int bonus = IsContinuousFirePath() ? 2 : 0;
        if (IsContinuousFirePath() && Runtime.Level >= 10)
            bonus += 2;
        return bonus;
    }

    private float GetHeadHunterScale(Transform target)
    {
        if (!IsHeadHunterPath())
            return 1f;

        return GetHeadHunterEnemyTypeScale(WeaponEnemyClassifier.GetKind(target));
    }

    private float GetHeadHunterEnemyTypeScale(WeaponEnemyKind kind)
    {
        return kind switch
        {
            WeaponEnemyKind.Boss => 3f,
            WeaponEnemyKind.Elite => 2f,
            _ => 1f
        };
    }

    private int GetHeadHunterPierceLimit()
    {
        return 10;
    }

    private float GetHeadHunterPierceDamageScale(int pierceIndex)
    {
        return Mathf.Clamp01(1f - Mathf.Max(0, pierceIndex) * 0.1f);
    }

    private float GetHeadHunterActiveChargeSeconds()
    {
        return 1f;
    }

    private float GetHeadHunterProjectileRange()
    {
        return HeadHunterActiveMinimumRange;
    }

    private int GetHeadHunterActivePierceLimit()
    {
        return int.MaxValue;
    }

    private float GetHeadHunterActivePierceRange()
    {
        float baseRange = Runtime?.Data != null ? Mathf.Max(0f, Runtime.Data.BaseRange) : 0f;
        return Mathf.Max(HeadHunterActiveMinimumRange, baseRange);
    }

    private float GetHeadHunterDamageScale(WeaponEnemyKind kind, int pierceIndex, bool weakPointHit, bool isActiveAbility)
    {
        float scale = GetHeadHunterEnemyTypeScale(kind);
        if (isActiveAbility)
            return scale * GetHeadHunterWeakPointScale();

        scale *= GetHeadHunterPierceDamageScale(pierceIndex);
        if (weakPointHit)
            scale *= GetHeadHunterWeakPointScale();

        return scale;
    }

    private void BeginContinuousFireActive(Vector3 aimDirection)
    {
        _continuousFireActive = true;
        _continuousFireActiveRemainingDuration = GetContinuousFireActiveDuration();
        _continuousFireActiveShotsRemaining = GetContinuousFireActiveBulletCount();
        _continuousFireActiveShotAccumulator = 1f;
        _continuousFireActiveDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;
        CompleteActiveAbility();
    }

    private bool TickContinuousFireActive(float deltaTime, Vector3 aimDirection)
    {
        if (!_continuousFireActive)
            return false;

        if (_continuousFireActiveShotsRemaining <= 0 || _continuousFireActiveRemainingDuration <= 0f)
        {
            EndContinuousFireActive();
            return false;
        }

        if (aimDirection.sqrMagnitude > 0.0001f)
            _continuousFireActiveDirection = aimDirection.normalized;

        float clampedDelta = Mathf.Max(0f, deltaTime);
        float activeDelta = Mathf.Min(clampedDelta, _continuousFireActiveRemainingDuration);
        _continuousFireActiveRemainingDuration = Mathf.Max(0f, _continuousFireActiveRemainingDuration - clampedDelta);
        _continuousFireActiveShotAccumulator += activeDelta * GetContinuousFireActiveBulletsPerSecond();

        int shotsToFire = Mathf.Min(_continuousFireActiveShotsRemaining, Mathf.FloorToInt(_continuousFireActiveShotAccumulator));
        if (shotsToFire > 0)
        {
            _continuousFireActiveShotAccumulator -= shotsToFire;
            _continuousFireActiveShotsRemaining -= shotsToFire;
            FireScatterBurst(
                _continuousFireActiveDirection,
                shotsToFire,
                1f,
                Runtime.Data.AutomaticCannon.CannonAbilityScatterRadius);
        }

        if (_continuousFireActiveShotsRemaining <= 0 || _continuousFireActiveRemainingDuration <= 0f)
            EndContinuousFireActive();

        return true;
    }

    private void EndContinuousFireActive()
    {
        _continuousFireActive = false;
        _continuousFireActiveRemainingDuration = 0f;
        _continuousFireActiveShotAccumulator = 0f;
        _continuousFireActiveShotsRemaining = 0;
        _continuousFireActiveDirection = Vector3.zero;
    }

    private void BeginHeadHunterActiveCharge(Vector3 aimDirection)
    {
        Vector3 direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;
        _headHunterActiveCharging = true;
        _headHunterActiveChargeTimer = GetHeadHunterActiveChargeSeconds();
        _headHunterChargedDirection = direction;
        BeginHeadHunterChargeVfx(GetCurrentHeadHunterChargedDirection());
        PlayerMovement movement = Owner != null ? Owner.GetComponent<PlayerMovement>() : null;
        if (movement != null)
            movement.ApplyMomentumPreservingStun(
                GetHeadHunterActiveChargeSeconds(),
                triggerStunFeedback: false,
                freezePlanarVelocity: false);
    }

    private bool TickHeadHunterActiveCharge(float deltaTime, Vector3 aimDirection)
    {
        if (!_headHunterActiveCharging)
            return false;

        UpdateHeadHunterChargedDirection(aimDirection);
        _headHunterActiveChargeTimer -= deltaTime;
        UpdateHeadHunterChargeVfx();
        if (_headHunterActiveChargeTimer > 0f)
            return true;

        _headHunterActiveCharging = false;
        DismissHeadHunterChargeVfx();
        FireHeadHunterPiercingLine(
            GetCurrentHeadHunterChargedDirection(),
            GetHeadHunterActivePierceLimit(),
            GetHeadHunterActivePierceRange(),
            allowWeakPointHits: false,
            isAbilityDamage: true,
            label: null);
        CompleteActiveAbility();
        return true;
    }

    private void UpdateHeadHunterChargedDirection(Vector3 aimDirection)
    {
        if (aimDirection.sqrMagnitude > 0.0001f)
            _headHunterChargedDirection = aimDirection.normalized;
    }

    private Vector3 GetCurrentHeadHunterChargedDirection()
    {
        return _headHunterChargedDirection.sqrMagnitude > 0.0001f
            ? _headHunterChargedDirection.normalized
            : Vector3.forward;
    }

    private void BeginHeadHunterChargeVfx(Vector3 aimDirection)
    {
        DismissHeadHunterChargeVfx();
        if (Spawn == null)
            return;

        _headHunterChargeVfx = HeadHunterChargeVfx.Spawn(Spawn, aimDirection, GetHeadHunterActiveChargeSeconds());
    }

    private void UpdateHeadHunterChargeVfx()
    {
        if (_headHunterChargeVfx == null)
            return;

        float duration = GetHeadHunterActiveChargeSeconds();
        float progress = 1f - Mathf.Clamp01(_headHunterActiveChargeTimer / Mathf.Max(0.01f, duration));
        _headHunterChargeVfx.SetChargeProgress(progress, GetCurrentHeadHunterChargedDirection());
    }

    private void DismissHeadHunterChargeVfx()
    {
        if (_headHunterChargeVfx == null)
            return;

        _headHunterChargeVfx.Dismiss();
        _headHunterChargeVfx = null;
    }

    private void FireHeadHunterPiercingLine(
        Vector3 aimDirection,
        int maxTargets,
        float range,
        bool allowWeakPointHits,
        bool isAbilityDamage,
        string label)
    {
        if (Spawn == null || aimDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 origin = Spawn.position;
        Vector3 direction = aimDirection.normalized;
        _piercingLine[0] = origin;
        _piercingLine[1] = origin + direction * Mathf.Max(0.01f, range);
        float projectileSpeed = SpawnHeadHunterProjectileVisual(origin, direction);

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _piercingLine,
            _piercingLine.Length,
            HeadHunterPierceRadius,
            Mathf.Max(1, maxTargets),
            _piercingTargets,
            _piercingHitOrigins);

        for (int i = 0; i < hitCount; i++)
        {
            IDamageable damageable = _piercingTargets[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            WeaponEnemyKind kind = WeaponEnemyClassifier.GetKind(_piercingTargets[i]);
            bool eliteOrBoss = kind == WeaponEnemyKind.Elite || kind == WeaponEnemyKind.Boss;
            bool weakPointHit = allowWeakPointHits && IsWeakPointHit(_piercingTargets[i], _piercingLine[0], _piercingLine[1]);
            float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), GetCritMultiplierOverride(), isAbilityDamage: isAbilityDamage);
            float scale = GetHeadHunterDamageScale(kind, i, weakPointHit, isAbilityDamage);

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * scale));
            Vector3 hitPoint = i < _piercingHitOrigins.Count ? _piercingHitOrigins[i] : origin;
            Vector3 impactOrigin = hitPoint - direction * HeadHunterPierceRadius;
            QueueHeadHunterImpact(
                _piercingTargets[i],
                finalDamage,
                weakPointHit,
                impactOrigin,
                GetHeadHunterImpactDelay(origin, direction, i, projectileSpeed));
        }
    }

    private float SpawnHeadHunterProjectileVisual(Vector3 origin, Vector3 direction)
    {
        if (Pool == null || direction.sqrMagnitude <= 0.0001f)
            return HeadHunterFallbackProjectileSpeed;

        direction.Normalize();
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
        if (Pool.TrySpawnVisualProjectile(
            origin,
            rotation,
            direction,
            HeadHunterProjectileVisualSpeedMultiplier,
            true,
            out Projectile projectile)
            && projectile != null)
            return Mathf.Max(0.01f, projectile.ActiveSpeed);

        return HeadHunterFallbackProjectileSpeed;
    }

    private float GetHeadHunterImpactDelay(Vector3 origin, Vector3 direction, int hitIndex, float projectileSpeed)
    {
        Vector3 hitPoint = hitIndex >= 0 && hitIndex < _piercingHitOrigins.Count
            ? _piercingHitOrigins[hitIndex]
            : origin;
        float distanceAlongShot = Mathf.Max(0f, Vector3.Dot(hitPoint - origin, direction));
        return distanceAlongShot / Mathf.Max(0.01f, projectileSpeed);
    }

    private void QueueHeadHunterImpact(Transform target, int damage, bool weakPointHit, Vector3 impactOrigin, float delay)
    {
        if (target == null || damage <= 0)
            return;

        _pendingHeadHunterImpacts.Add(new PendingHeadHunterImpact
        {
            Target = target,
            Damage = damage,
            WeakPointHit = weakPointHit,
            ImpactOrigin = impactOrigin,
            RemainingDelay = Mathf.Max(0f, delay)
        });
    }

    private void TickHeadHunterPendingImpacts(float deltaTime)
    {
        if (_pendingHeadHunterImpacts.Count == 0)
            return;

        float elapsed = Mathf.Max(0f, deltaTime);
        for (int i = _pendingHeadHunterImpacts.Count - 1; i >= 0; i--)
        {
            PendingHeadHunterImpact impact = _pendingHeadHunterImpacts[i];
            impact.RemainingDelay -= elapsed;
            if (impact.RemainingDelay > 0f)
            {
                _pendingHeadHunterImpacts[i] = impact;
                continue;
            }

            ApplyHeadHunterImpact(impact);
            _pendingHeadHunterImpacts.RemoveAt(i);
        }
    }

    private void ApplyHeadHunterImpact(PendingHeadHunterImpact impact)
    {
        if (impact.Target == null)
            return;

        IDamageable damageable = impact.Target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        if (WeaponDamageApplier.TryApplyDamage(damageable, impact.Damage))
        {
            ApplyKnockback(damageable, impact.ImpactOrigin, impact.Damage, 1f);
            if (impact.WeakPointHit)
                WeaponWeakPointFeedback.NotifyWeakPointHit();
        }
    }

    private bool IsWeakPointHit(Transform target, Vector3 lineStart, Vector3 lineEnd)
    {
        if (target == null)
            return false;

        Collider[] colliders = target.GetComponentsInChildren<Collider>(false);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || !IsWeakPointCollider(collider))
                continue;

            Vector3 closestOnLine = ClosestPointOnSegment(collider.bounds.center, lineStart, lineEnd);
            Vector3 closestOnCollider = collider.ClosestPoint(closestOnLine);
            if ((closestOnLine - closestOnCollider).sqrMagnitude <= HeadHunterPierceRadius * HeadHunterPierceRadius)
                return true;
        }

        return false;
    }

    private static bool IsWeakPointCollider(Collider collider)
    {
        return collider.GetComponent<WeaponDummyWeakPoint>() != null
            || collider.name.Contains("WeakPoint", System.StringComparison.OrdinalIgnoreCase)
            || collider.transform.name.Contains("WeakPoint", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denominator = ab.sqrMagnitude;
        if (denominator <= 0.0001f)
            return a;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
        return a + ab * t;
    }

    private struct PendingHeadHunterImpact
    {
        public Transform Target;
        public int Damage;
        public bool WeakPointHit;
        public Vector3 ImpactOrigin;
        public float RemainingDelay;
    }

    // Spawns normal cannon bursts as a straight line of projectiles.
    private void FireLineBurst(
        Vector3 aimDirection,
        int count,
        float damageScale,
        float lineSpacing,
        float accuracySpreadDegrees,
        float projectileScatterDegrees,
        bool eliteOrBoss)
    {
        if (Spawn == null || count <= 0)
            return;

        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        baseDirection = ApplyAccuracySpread(baseDirection, accuracySpreadDegrees);
        _lineBurstDirection = baseDirection;
        _lineBurstDamageScale = damageScale;
        _lineBurstSpacing = Mathf.Max(0f, lineSpacing);
        _lineBurstScatterDegrees = Mathf.Max(0f, projectileScatterDegrees);
        _lineBurstShotInterval = GetLineBurstShotInterval();
        _lineBurstEliteOrBoss = eliteOrBoss;
        _lineBurstIndex = 0;
        _lineBurstRemaining = Mathf.Max(0, count);
        _lineBurstTimer = 0f;
        _lineBurstActive = true;
        FireNextLineBurstShot();
        if (_lineBurstActive)
            _lineBurstTimer = _lineBurstShotInterval;
    }

    private void TickPendingLineBurst(float deltaTime)
    {
        if (!_lineBurstActive)
            return;

        if (Spawn == null)
        {
            _lineBurstActive = false;
            _lineBurstRemaining = 0;
            return;
        }

        _lineBurstTimer -= Mathf.Max(0f, deltaTime);
        while (_lineBurstActive && _lineBurstTimer <= 0f)
        {
            FireNextLineBurstShot();
            _lineBurstTimer += Mathf.Max(0.001f, _lineBurstShotInterval);
        }
    }

    private float GetLineBurstShotInterval()
    {
        AutomaticCannonTuning tuning = Runtime?.Data != null ? Runtime.Data.AutomaticCannon : AutomaticCannonTuning.Defaults;
        return Mathf.Max(0.001f, tuning.CannonBurstShotInterval > 0f ? tuning.CannonBurstShotInterval : DefaultLineBurstShotInterval);
    }

    private void FireNextLineBurstShot()
    {
        if (!_lineBurstActive || _lineBurstRemaining <= 0)
        {
            _lineBurstActive = false;
            _lineBurstRemaining = 0;
            return;
        }

        Vector3 position = Spawn.position + _lineBurstDirection * (_lineBurstSpacing * _lineBurstIndex);
        Vector3 shotDirection = AutomaticCannonFireLogic.ApplyProjectileScatter(
            _lineBurstDirection,
            _lineBurstScatterDegrees,
            UnityEngine.Random.insideUnitCircle);
        FireFromPositionInDirection(position, shotDirection, _lineBurstDamageScale, _lineBurstEliteOrBoss);

        _lineBurstIndex++;
        _lineBurstRemaining--;
        if (_lineBurstRemaining <= 0)
            _lineBurstActive = false;
    }

    // Medium automatic accuracy offsets the whole burst while keeping bullets in a clean line.
    private Vector3 ApplyAccuracySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0f)
            return direction;

        Quaternion aimRotation = Quaternion.LookRotation(direction, GetStableUp(direction));
        Vector2 spread = UnityEngine.Random.insideUnitCircle * spreadDegrees;
        return (aimRotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward).normalized;
    }

    // Spawns active ability burst with shotgun-style two-axis angular spread.
    private void FireScatterBurst(Vector3 aimDirection, int count, float damageScale, float spreadDegrees)
    {
        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        Quaternion aimRotation = Quaternion.LookRotation(baseDirection, GetStableUp(baseDirection));

        for (int i = 0; i < count; i++)
        {
            Vector2 spread = spreadDegrees > 0f ? UnityEngine.Random.insideUnitCircle * spreadDegrees : Vector2.zero;
            Vector3 shotDirection = aimRotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
            FireInDirection(shotDirection, damageScale, false, isAbilityDamage: true);
        }
    }

    // Avoids LookRotation instability if the shot direction points almost straight up/down.
    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
    }
}
