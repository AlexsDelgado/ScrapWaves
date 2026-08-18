using System.Collections.Generic;
using UnityEngine;

public sealed class AutomaticCannonWeapon : BasicProjectileWeapon
{
    private static readonly float[] PresentationHeatThresholds = { 0.25f, 0.5f, 0.75f, 0.8f, 1f };
    private const float ContinuousFireActiveBulletsPerSecond = 40f;
    private const float HeadHunterPierceRadius = 0.45f;
    private const float HeadHunterActiveMinimumRange = 1000f;
    private const float HeadHunterProjectileVisualSpeedMultiplier = 3f;
    private const float HeadHunterFallbackProjectileSpeed = 54f;
    private const float CannonBaseProjectileSpeed = 18f;
    private const float DefaultLineBurstShotInterval = 0.08f;

    private readonly List<Transform> _piercingTargets = new();
    private readonly List<Vector3> _piercingHitOrigins = new();
    private readonly List<PendingHeadHunterImpact> _pendingHeadHunterImpacts = new();
    private readonly List<PendingHeadHunterWorldImpact> _pendingHeadHunterWorldImpacts = new();
    private readonly Vector3[] _piercingLine = new Vector3[2];

    private bool _lineBurstActive;
    private int _lineBurstRemaining;
    private int _lineBurstIndex;
    private float _lineBurstTimer;
    private Vector3 _lineBurstDirection;
    private float _lineBurstDamageScale;
    private float _lineBurstScatterDegrees;
    private float _lineBurstShotInterval;
    private float _lineBurstAccuracySpreadDegrees;
    private Vector2 _lineBurstAccuracySample;
    private Vector3 _lineBurstLiveAimDirection;
    private Transform _lineBurstTrackingTarget;
    private bool _lineBurstFollowsLiveAim;
    private bool _lineBurstEliteOrBoss;
    private WeaponPresentationCue _lineBurstShotCue;
    private WeaponPresentationCue _lineBurstEventCue;
    private bool _lineBurstEventEmitted;

    private bool _continuousFireActive;
    private float _continuousFireActiveRemainingDuration;
    private float _continuousFireActiveShotAccumulator;
    private int _continuousFireActiveShotsRemaining;
    private Vector3 _continuousFireActiveDirection;
    private bool _continuousFireActivePresentationStarted;
    private bool _continuousBurstFeedbackActive;
    private WeaponPresentationLoopHandle _legacyContinuousLoopHandle;

    private WeaponPresentationLoopHandle _legacyHeadHunterChargeLoop;
    private HeadHunterChargeVfx _debugHeadHunterChargeVfx;
    private bool _headHunterActiveCharging;
    private float _headHunterActiveChargeTimer;
    private Vector3 _headHunterChargedDirection;
    private bool _headHunterManualFireHeld;
    private int _presentationShotIndex;
    private float _lastPresentedHeat;

    public AutomaticCannonWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Fires three-round burst in automatic mode.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        TickHeadHunterPendingImpacts(deltaTime);
        _lineBurstLiveAimDirection = aimDirection;
        TickPendingLineBurst(deltaTime);
        if (_lineBurstActive)
            return;
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

        BeginPresentationLineBurst(
            targetPoint - Spawn.position,
            GetAutomaticShotCount(tuning),
            GetHeatDamageMultiplier() * GetHeadHunterScale(target),
            tuning.CannonAutoLineSpacing,
            tuning.CannonAutoAccuracySpreadDegrees,
            tuning.CannonBurstProjectileScatterDegrees,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target),
            IsContinuousFirePath()
                ? WeaponPresentationCue.AutomaticCannonContinuousShot
                : WeaponPresentationCue.AutomaticCannonAutoShot,
            IsContinuousFirePath()
                ? WeaponPresentationCue.None
                : WeaponPresentationCue.AutomaticCannonAutoBurst,
            trackingTarget: target,
            followsLiveAim: false);
    }

    // Fires five-round burst in manual mode.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        TickHeadHunterPendingImpacts(deltaTime);
        _lineBurstLiveAimDirection = aimDirection;
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

        if (_lineBurstActive)
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
        {
            EmitAmmoEmptyFeedback(aimDirection);
            return;
        }

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

        BeginPresentationLineBurst(
            aimDirection,
            bulletsToFire,
            1f,
            tuning.CannonManualLineSpacing,
            0f,
            tuning.CannonBurstProjectileScatterDegrees,
            false,
            IsContinuousFirePath()
                ? WeaponPresentationCue.AutomaticCannonContinuousShot
                : WeaponPresentationCue.AutomaticCannonManualShot,
            IsContinuousFirePath()
                ? WeaponPresentationCue.None
                : WeaponPresentationCue.AutomaticCannonManualVolley,
            trackingTarget: null,
            followsLiveAim: true);
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
        {
            EmitAmmoEmptyFeedback(aimDirection);
            return;
        }

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
            tuning.CannonAbilityScatterRadius,
            WeaponPresentationCue.None,
            WeaponPresentationCue.AutomaticCannonBaseActive);
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
        if (!IsContinuousFirePath())
            return 1f;

        AutomaticCannonTuning tuning = Runtime?.Data != null
            ? Runtime.Data.AutomaticCannon
            : AutomaticCannonTuning.Defaults;
        return GetContinuousFireAttackSpeedMultiplier() * Mathf.Max(1, tuning.CannonManualBurstCount);
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
        {
            float replacedBurstRounds = Mathf.Max(1, tuning.CannonAutoBurstCount);
            float continuousMultiplier = GetContinuousFireAutoAttackSpeedMultiplier(tuning) * replacedBurstRounds;
            return interval / Mathf.Max(0.01f, continuousMultiplier);
        }
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
        _continuousFireActivePresentationStarted = false;
        BeginSustainedFeedback(_continuousFireActiveDirection, isAbility: true);
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
            int successfulShots = FireScatterBurst(
                _continuousFireActiveDirection,
                shotsToFire,
                1f,
                Runtime.Data.AutomaticCannon.CannonAbilityScatterRadius,
                WeaponPresentationCue.AutomaticCannonContinuousShot,
                _continuousFireActivePresentationStarted
                    ? WeaponPresentationCue.None
                    : WeaponPresentationCue.AutomaticCannonContinuousActive);
            if (successfulShots > 0)
                _continuousFireActivePresentationStarted = true;
        }

        if (_continuousFireActiveShotsRemaining <= 0 || _continuousFireActiveRemainingDuration <= 0f)
            EndContinuousFireActive();

        return true;
    }

    private void EndContinuousFireActive()
    {
        if (_continuousFireActive)
            EndSustainedFeedback(_continuousFireActiveDirection, isAbility: true);
        _continuousFireActive = false;
        _continuousFireActiveRemainingDuration = 0f;
        _continuousFireActiveShotAccumulator = 0f;
        _continuousFireActiveShotsRemaining = 0;
        _continuousFireActiveDirection = Vector3.zero;
        _continuousFireActivePresentationStarted = false;
    }

    private void BeginHeadHunterActiveCharge(Vector3 aimDirection)
    {
        Vector3 direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;
        _headHunterActiveCharging = true;
        _headHunterActiveChargeTimer = GetHeadHunterActiveChargeSeconds();
        _headHunterChargedDirection = direction;
        BeginHeadHunterChargeFeedback(GetCurrentHeadHunterChargedDirection());
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
        UpdateHeadHunterChargeFeedback();
        if (_headHunterActiveChargeTimer > 0f)
            return true;

        _headHunterActiveCharging = false;
        DismissHeadHunterChargeFeedback();
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

    private void BeginHeadHunterChargeFeedback(Vector3 aimDirection)
    {
        DismissHeadHunterChargeFeedback();
        if (Spawn == null)
            return;

        WeaponFeedbackContext feedback = CreateFeedbackContext(
            WeaponFeedbackMode.Active,
            Spawn.position,
            aimDirection,
            isAbility: true,
            anchor: Spawn);
        if (object.ReferenceEquals(Presentation, NullWeaponPresentationSink.Instance))
        {
            _debugHeadHunterChargeVfx = HeadHunterChargeVfx.Spawn(
                Spawn,
                aimDirection,
                GetHeadHunterActiveChargeSeconds());
            return;
        }

        if (Presentation is IWeaponFeedbackSink semantic)
        {
            semantic.OnChargeStarted(in feedback);
            return;
        }

        WeaponPresentationContext legacy = CreateLegacyContext(
            WeaponPresentationCue.AutomaticCannonHeadHunterCharge,
            in feedback);
        _legacyHeadHunterChargeLoop = Presentation.BeginLoop(in legacy);
    }

    private void UpdateHeadHunterChargeFeedback()
    {
        if (Spawn == null)
            return;

        float duration = GetHeadHunterActiveChargeSeconds();
        float progress = 1f - Mathf.Clamp01(_headHunterActiveChargeTimer / Mathf.Max(0.01f, duration));
        WeaponFeedbackContext feedback = CreateFeedbackContext(
            WeaponFeedbackMode.Active,
            Spawn.position,
            GetCurrentHeadHunterChargedDirection(),
            isAbility: true,
            intensity: progress,
            anchor: Spawn);
        if (_debugHeadHunterChargeVfx != null)
        {
            _debugHeadHunterChargeVfx.SetChargeProgress(progress, GetCurrentHeadHunterChargedDirection());
            return;
        }

        if (Presentation is IWeaponFeedbackSink semantic)
        {
            semantic.OnChargeUpdated(in feedback, progress);
            return;
        }

        WeaponPresentationContext legacy = CreateLegacyContext(
            WeaponPresentationCue.AutomaticCannonHeadHunterCharge,
            in feedback);
        Presentation.UpdateLoop(_legacyHeadHunterChargeLoop, in legacy);
    }

    private void DismissHeadHunterChargeFeedback()
    {
        if (Spawn == null)
            return;

        WeaponFeedbackContext feedback = CreateFeedbackContext(
            WeaponFeedbackMode.Active,
            Spawn.position,
            GetCurrentHeadHunterChargedDirection(),
            isAbility: true,
            anchor: Spawn);
        if (_debugHeadHunterChargeVfx != null)
        {
            _debugHeadHunterChargeVfx.Dismiss();
            _debugHeadHunterChargeVfx = null;
        }
        else if (Presentation is IWeaponFeedbackSink semantic)
            semantic.OnChargeCancelled(in feedback);
        else
        {
            WeaponPresentationContext legacy = CreateLegacyContext(
                WeaponPresentationCue.AutomaticCannonHeadHunterCharge,
                in feedback);
            Presentation.EndLoop(_legacyHeadHunterChargeLoop, in legacy);
        }

        _legacyHeadHunterChargeLoop = default;
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
        float shotDistance = Mathf.Max(0.01f, range);
        bool blockedByWorld = TryGetHeadHunterWorldImpact(
            origin,
            direction,
            shotDistance,
            out RaycastHit worldImpact);
        if (blockedByWorld)
            shotDistance = Mathf.Max(0.01f, worldImpact.distance);

        _piercingLine[0] = origin;
        _piercingLine[1] = origin + direction * shotDistance;
        bool visualSpawned = TrySpawnHeadHunterProjectileVisual(
            origin,
            direction,
            isAbilityDamage,
            shotDistance,
            out float projectileSpeed);
        if (visualSpawned)
        {
            EmitShotFeedback(
                GetHeadHunterFireCue(isAbilityDamage),
                origin,
                direction,
                isAbilityDamage,
                target: null,
                anchor: Spawn);
        }

        if (blockedByWorld)
        {
            QueueHeadHunterWorldImpact(
                worldImpact,
                direction,
                isAbilityDamage,
                shotDistance / Mathf.Max(0.01f, projectileSpeed));
        }

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
            WeaponDamageContext damageContext = CreateDamageContext(1f, isAbilityDamage);
            float damage = damageContext.CalculateDamageValue(eliteOrBoss, _piercingTargets[i].position);
            float scale = GetHeadHunterDamageScale(kind, i, weakPointHit, isAbilityDamage);

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * scale));
            Vector3 hitPoint = i < _piercingHitOrigins.Count ? _piercingHitOrigins[i] : origin;
            Vector3 impactOrigin = hitPoint - direction * HeadHunterPierceRadius;
            QueueHeadHunterImpact(
                _piercingTargets[i],
                finalDamage,
                weakPointHit,
                damageContext.IsCritical,
                isAbilityDamage,
                impactOrigin,
                hitPoint,
                direction,
                i,
                GetHeadHunterImpactDelay(origin, direction, i, projectileSpeed));
        }
    }

    private bool TrySpawnHeadHunterProjectileVisual(
        Vector3 origin,
        Vector3 direction,
        bool isAbility,
        float maxTravelDistance,
        out float projectileSpeed)
    {
        projectileSpeed = HeadHunterFallbackProjectileSpeed;
        if (Pool == null || direction.sqrMagnitude <= 0.0001f)
            return false;

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
        {
            projectileSpeed = Mathf.Max(0.01f, projectile.ActiveSpeed);
            projectile.ConfigureMaxTravel(Mathf.Max(0.01f, maxTravelDistance), explodeOnMaxTravel: false);
            if (Presentation is IWeaponFeedbackSink semantic)
            {
                WeaponFeedbackContext feedback = CreateFeedbackContext(
                    GetFeedbackMode(isAbility),
                    origin,
                    direction,
                    isAbility,
                    anchor: Spawn);
                semantic.ConfigureProjectile(
                    projectile,
                    ProjectilePresentationArchetypeId.HeadHunterBolt,
                    in feedback);
            }
            return true;
        }

        return false;
    }

    // Head Hunter pierces enemies, but the first solid non-enemy collider terminates the shot.
    private bool TryGetHeadHunterWorldImpact(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaycastHit closestWorldImpact)
    {
        closestWorldImpact = default;
        if (direction.sqrMagnitude <= 0.0001f || maxDistance <= 0f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction.normalized,
            maxDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (!IsHeadHunterWorldBlocker(collider) || hits[i].distance >= closestDistance)
                continue;

            closestDistance = hits[i].distance;
            closestWorldImpact = hits[i];
            found = true;
        }

        return found;
    }

    private bool IsHeadHunterWorldBlocker(Collider collider)
    {
        if (collider == null || collider.isTrigger)
            return false;

        Transform collisionTransform = collider.transform;
        if (Owner != null && (collisionTransform == Owner || collisionTransform.IsChildOf(Owner)))
            return false;
        if (Spawn != null && (collisionTransform == Spawn || collisionTransform.IsChildOf(Spawn)))
            return false;
        if (collider.GetComponentInParent<PlayerHealth>() != null)
            return false;

        // Enemy colliders are intentionally transparent to this piercing weapon.
        return collider.GetComponentInParent<IDamageable>() == null;
    }

    private float GetHeadHunterImpactDelay(Vector3 origin, Vector3 direction, int hitIndex, float projectileSpeed)
    {
        Vector3 hitPoint = hitIndex >= 0 && hitIndex < _piercingHitOrigins.Count
            ? _piercingHitOrigins[hitIndex]
            : origin;
        float distanceAlongShot = Mathf.Max(0f, Vector3.Dot(hitPoint - origin, direction));
        return distanceAlongShot / Mathf.Max(0.01f, projectileSpeed);
    }

    private void QueueHeadHunterImpact(
        Transform target,
        int damage,
        bool weakPointHit,
        bool criticalHit,
        bool isAbility,
        Vector3 impactOrigin,
        Vector3 impactPosition,
        Vector3 direction,
        int pierceIndex,
        float delay)
    {
        if (target == null || damage <= 0)
            return;

        _pendingHeadHunterImpacts.Add(new PendingHeadHunterImpact
        {
            Target = target,
            Damage = damage,
            WeakPointHit = weakPointHit,
            CriticalHit = criticalHit,
            IsAbility = isAbility,
            ImpactOrigin = impactOrigin,
            ImpactPosition = impactPosition,
            Direction = direction,
            PierceIndex = Mathf.Max(0, pierceIndex),
            RemainingDelay = Mathf.Max(0f, delay)
        });
    }

    private void TickHeadHunterPendingImpacts(float deltaTime)
    {
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

        for (int i = _pendingHeadHunterWorldImpacts.Count - 1; i >= 0; i--)
        {
            PendingHeadHunterWorldImpact impact = _pendingHeadHunterWorldImpacts[i];
            impact.RemainingDelay -= elapsed;
            if (impact.RemainingDelay > 0f)
            {
                _pendingHeadHunterWorldImpacts[i] = impact;
                continue;
            }

            EmitHeadHunterWorldImpact(impact);
            _pendingHeadHunterWorldImpacts.RemoveAt(i);
        }
    }

    private void QueueHeadHunterWorldImpact(
        RaycastHit hit,
        Vector3 direction,
        bool isAbility,
        float delay)
    {
        _pendingHeadHunterWorldImpacts.Add(new PendingHeadHunterWorldImpact
        {
            Target = hit.collider != null ? hit.collider.transform : null,
            ImpactPosition = hit.point,
            ImpactNormal = hit.normal,
            Direction = direction,
            SurfaceType = ImpactSurfaceResolver.Resolve(hit.collider),
            IsAbility = isAbility,
            RemainingDelay = Mathf.Max(0f, delay)
        });
    }

    private void EmitHeadHunterWorldImpact(PendingHeadHunterWorldImpact impact)
    {
        if (Presentation is IWeaponFeedbackSink semantic)
        {
            WeaponFeedbackContext baseFeedback = CreateFeedbackContext(
                GetFeedbackMode(impact.IsAbility),
                impact.ImpactPosition,
                impact.Direction,
                impact.IsAbility,
                target: impact.Target);
            WeaponFeedbackContext feedback = baseFeedback.WithImpact(
                impact.ImpactPosition,
                impact.ImpactNormal,
                0,
                false,
                false,
                false,
                impact.Target,
                WeaponEnemyKind.Normal,
                impact.SurfaceType);
            semantic.OnProjectileImpact(in feedback);
            return;
        }

        EmitPresentationCue(
            WeaponPresentationCue.AutomaticCannonImpact,
            impact.ImpactPosition,
            impact.Direction,
            impact.IsAbility,
            impact.Target);
    }

    private void ApplyHeadHunterImpact(PendingHeadHunterImpact impact)
    {
        if (impact.Target == null)
            return;

        IDamageable damageable = impact.Target.GetComponentInParent<IDamageable>();
        if (damageable == null)
            return;

        int healthBefore = GetRemainingHealth(damageable);
        if (WeaponDamageApplier.TryApplyDamage(damageable, impact.Damage))
        {
            ApplyKnockback(damageable, impact.ImpactOrigin, impact.Damage, 1f);
            if (impact.WeakPointHit)
                WeaponWeakPointFeedback.NotifyWeakPointHit();

            bool kill = healthBefore > 0 && GetRemainingHealth(damageable) <= 0;
            if (Presentation is IWeaponFeedbackSink semantic)
            {
                Collider surfaceCollider = impact.Target.GetComponentInChildren<Collider>();
                WeaponFeedbackContext baseFeedback = CreateFeedbackContext(
                    GetFeedbackMode(impact.IsAbility),
                    impact.ImpactOrigin,
                    impact.Direction,
                    impact.IsAbility,
                    intensity: GetHeadHunterImpactIntensity(impact.PierceIndex),
                    target: impact.Target);
                WeaponFeedbackContext feedback = baseFeedback.WithImpact(
                    impact.ImpactPosition,
                    -impact.Direction,
                    impact.Damage,
                    impact.CriticalHit,
                    impact.WeakPointHit,
                    kill,
                    impact.Target,
                    WeaponEnemyClassifier.GetKind(impact.Target),
                    ImpactSurfaceResolver.Resolve(surfaceCollider, damageable));
                semantic.OnProjectileImpact(in feedback);
                semantic.OnDamageConfirmed(in feedback);
            }
            else
            {
                WeaponPresentationCue impactCue = impact.WeakPointHit
                    ? WeaponPresentationCue.AutomaticCannonWeakPointImpact
                    : impact.CriticalHit
                        ? WeaponPresentationCue.AutomaticCannonCriticalImpact
                        : WeaponPresentationCue.AutomaticCannonImpact;
                EmitPresentationCue(
                    impactCue,
                    impact.ImpactPosition,
                    impact.Direction,
                    impact.IsAbility,
                    impact.Target,
                    impact.CriticalHit,
                    impact.WeakPointHit);
            }
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
        public bool CriticalHit;
        public bool IsAbility;
        public Vector3 ImpactOrigin;
        public Vector3 ImpactPosition;
        public Vector3 Direction;
        public int PierceIndex;
        public float RemainingDelay;
    }

    private struct PendingHeadHunterWorldImpact
    {
        public Transform Target;
        public Vector3 ImpactPosition;
        public Vector3 ImpactNormal;
        public Vector3 Direction;
        public ImpactSurfaceType SurfaceType;
        public bool IsAbility;
        public float RemainingDelay;
    }

    private float GetHeadHunterImpactIntensity(int pierceIndex)
    {
        int maximumAccents = Runtime?.Data?.PresentationProfile?.AutomaticCannon?.MaximumPiercingAccents ?? 6;
        maximumAccents = Mathf.Max(1, maximumAccents);
        if (pierceIndex <= 0)
            return 1f;
        if (pierceIndex >= maximumAccents)
            return 0.12f;

        float normalized = pierceIndex / Mathf.Max(1f, maximumAccents - 1f);
        return Mathf.Lerp(0.65f, 0.25f, normalized);
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
        BeginPresentationLineBurst(
            aimDirection,
            count,
            damageScale,
            lineSpacing,
            accuracySpreadDegrees,
            projectileScatterDegrees,
            eliteOrBoss,
            WeaponPresentationCue.None,
            WeaponPresentationCue.None);
    }

    private void BeginPresentationLineBurst(
        Vector3 aimDirection,
        int count,
        float damageScale,
        float lineSpacing,
        float accuracySpreadDegrees,
        float projectileScatterDegrees,
        bool eliteOrBoss,
        WeaponPresentationCue shotCue,
        WeaponPresentationCue eventCue,
        Transform trackingTarget = null,
        bool followsLiveAim = false)
    {
        if (Spawn == null || count <= 0)
            return;

        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        _lineBurstAccuracySpreadDegrees = Mathf.Max(0f, accuracySpreadDegrees);
        _lineBurstAccuracySample = UnityEngine.Random.insideUnitCircle;
        baseDirection = ApplyAccuracySpread(
            baseDirection,
            _lineBurstAccuracySpreadDegrees,
            _lineBurstAccuracySample);
        _lineBurstDirection = baseDirection;
        _lineBurstLiveAimDirection = aimDirection;
        _lineBurstTrackingTarget = trackingTarget;
        _lineBurstFollowsLiveAim = followsLiveAim;
        _lineBurstDamageScale = damageScale;
        _lineBurstScatterDegrees = Mathf.Max(0f, projectileScatterDegrees);
        _lineBurstShotInterval = GetLineBurstShotInterval(lineSpacing);
        _lineBurstEliteOrBoss = eliteOrBoss;
        _lineBurstShotCue = shotCue;
        _lineBurstEventCue = eventCue;
        _lineBurstEventEmitted = false;
        _lineBurstIndex = 0;
        _lineBurstRemaining = Mathf.Max(0, count);
        _lineBurstTimer = 0f;
        _lineBurstActive = true;
        if (shotCue == WeaponPresentationCue.AutomaticCannonContinuousShot)
        {
            _continuousBurstFeedbackActive = true;
            BeginSustainedFeedback(baseDirection, isAbility: false);
        }
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
            CompleteLineBurst();
            return;
        }

        RefreshPendingLineBurstDirection();
        _lineBurstTimer -= Mathf.Max(0f, deltaTime);
        while (_lineBurstActive && _lineBurstTimer <= 0f)
        {
            FireNextLineBurstShot();
            _lineBurstTimer += Mathf.Max(0.001f, _lineBurstShotInterval);
        }
    }

    private float GetLineBurstShotInterval(float desiredProjectileSeparation)
    {
        AutomaticCannonTuning tuning = Runtime?.Data != null ? Runtime.Data.AutomaticCannon : AutomaticCannonTuning.Defaults;
        float authoredInterval = Mathf.Max(
            0.001f,
            tuning.CannonBurstShotInterval > 0f
                ? tuning.CannonBurstShotInterval
                : DefaultLineBurstShotInterval);
        if (IsContinuousFirePath())
            return authoredInterval;

        float spacingInterval = Mathf.Max(0f, desiredProjectileSeparation) / CannonBaseProjectileSpeed;
        return Mathf.Max(authoredInterval, spacingInterval);
    }

    private void FireNextLineBurstShot()
    {
        if (!_lineBurstActive || _lineBurstRemaining <= 0)
        {
            CompleteLineBurst();
            return;
        }

        // A real burst launches every round at the muzzle. Advancing later spawn points
        // cancelled the authored delay and made all bullets reach the target together.
        Vector3 position = Spawn.position;
        Vector3 shotDirection = AutomaticCannonFireLogic.ApplyProjectileScatter(
            _lineBurstDirection,
            _lineBurstScatterDegrees,
            UnityEngine.Random.insideUnitCircle);
        ProjectilePresentationArchetypeId projectileArchetype = GetLineBurstProjectileArchetype();
        bool spawned = TryFireCannonProjectile(
            position,
            shotDirection,
            _lineBurstDamageScale,
            _lineBurstEliteOrBoss,
            _lineBurstShotCue,
            isAbilityDamage: false,
            projectileArchetype: projectileArchetype);
        if (spawned && !_lineBurstEventEmitted && _lineBurstEventCue != WeaponPresentationCue.None)
        {
            EmitPresentationCue(
                _lineBurstEventCue,
                position,
                shotDirection,
                isAbility: false);
            _lineBurstEventEmitted = true;
        }

        _lineBurstIndex++;
        _lineBurstRemaining--;
        if (_lineBurstRemaining <= 0)
            CompleteLineBurst();
    }

    private void RefreshPendingLineBurstDirection()
    {
        Vector3 liveDirection;
        if (_lineBurstTrackingTarget != null)
        {
            liveDirection = EnemyRegistry.GetAimPoint(_lineBurstTrackingTarget) - Spawn.position;
        }
        else if (_lineBurstFollowsLiveAim)
        {
            liveDirection = _lineBurstLiveAimDirection;
        }
        else
        {
            return;
        }

        if (liveDirection.sqrMagnitude <= 0.0001f)
            return;

        _lineBurstDirection = ApplyAccuracySpread(
            liveDirection.normalized,
            _lineBurstAccuracySpreadDegrees,
            _lineBurstAccuracySample);
    }

    private void CompleteLineBurst()
    {
        if (_continuousBurstFeedbackActive)
        {
            EndSustainedFeedback(_lineBurstDirection, isAbility: false);
            _continuousBurstFeedbackActive = false;
        }
        _lineBurstActive = false;
        _lineBurstRemaining = 0;
        _lineBurstTrackingTarget = null;
        _lineBurstFollowsLiveAim = false;
    }

    // Applies one stable accuracy sample to the whole burst while allowing its live aim axis to move.
    private Vector3 ApplyAccuracySpread(
        Vector3 direction,
        float spreadDegrees,
        Vector2 unitCircleSample)
    {
        if (spreadDegrees <= 0f)
            return direction;

        Quaternion aimRotation = Quaternion.LookRotation(direction, GetStableUp(direction));
        Vector2 spread = Vector2.ClampMagnitude(unitCircleSample, 1f) * spreadDegrees;
        return (aimRotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward).normalized;
    }

    // Spawns active ability burst with shotgun-style two-axis angular spread.
    private int FireScatterBurst(
        Vector3 aimDirection,
        int count,
        float damageScale,
        float spreadDegrees,
        WeaponPresentationCue shotCue,
        WeaponPresentationCue eventCue)
    {
        if (Spawn == null || count <= 0)
            return 0;

        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        Quaternion aimRotation = Quaternion.LookRotation(baseDirection, GetStableUp(baseDirection));
        int successfulShots = 0;
        bool eventEmitted = false;

        for (int i = 0; i < count; i++)
        {
            Vector2 spread = spreadDegrees > 0f ? UnityEngine.Random.insideUnitCircle * spreadDegrees : Vector2.zero;
            Vector3 shotDirection = aimRotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
            bool spawned = TryFireCannonProjectile(
                Spawn.position,
                shotDirection,
                damageScale,
                eliteOrBoss: false,
                shotCue: shotCue,
                isAbilityDamage: true,
                emitSemanticShotFeedback: successfulShots == 0);
            if (!spawned)
                continue;

            successfulShots++;
            if (!eventEmitted && eventCue != WeaponPresentationCue.None)
            {
                EmitPresentationCue(
                    eventCue,
                    Spawn.position,
                    shotDirection,
                    isAbility: true);
                eventEmitted = true;
            }
        }

        return successfulShots;
    }

    private bool TryFireCannonProjectile(
        Vector3 position,
        Vector3 direction,
        float damageScale,
        bool eliteOrBoss,
        WeaponPresentationCue shotCue,
        bool isAbilityDamage,
        bool emitSemanticShotFeedback = true,
        ProjectilePresentationArchetypeId projectileArchetype = ProjectilePresentationArchetypeId.Default)
    {
        bool spawned = FireFromPositionInDirection(
            position,
            direction,
            damageScale,
            eliteOrBoss,
            out Projectile projectile,
            isAbilityDamage);
        if (!spawned || projectile == null)
            return false;

        Vector3 origin = Spawn != null ? Spawn.position : position;
        if (Presentation is IWeaponFeedbackSink semantic)
        {
            WeaponFeedbackContext feedback = CreateFeedbackContext(
                GetFeedbackMode(isAbilityDamage),
                origin,
                direction,
                isAbilityDamage,
                anchor: Spawn);
            projectile.ConfigureFeedback(semantic, in feedback, allowWeakPoint: false);
            ProjectilePresentationArchetypeId resolvedArchetype = projectileArchetype == ProjectilePresentationArchetypeId.Default
                ? GetCannonProjectileArchetype()
                : projectileArchetype;
            semantic.ConfigureProjectile(projectile, resolvedArchetype, in feedback);
            if (emitSemanticShotFeedback)
            {
                EmitHeatThresholdIfNeeded(semantic, in feedback);
                semantic.OnShotFired(in feedback);
            }
        }
        else
        {
            projectile.ConfigurePresentation(
                Presentation,
                Runtime,
                WeaponPresentationCue.AutomaticCannonImpact,
                WeaponPresentationCue.AutomaticCannonCriticalImpact,
                WeaponPresentationCue.AutomaticCannonWeakPointImpact,
                isAbilityDamage,
                allowWeakPoint: false);

            EmitPresentationCue(
                shotCue,
                origin,
                direction,
                isAbilityDamage,
                anchor: Spawn);
        }
        return true;
    }

    private WeaponPresentationCue GetHeadHunterFireCue(bool isAbility)
    {
        if (isAbility)
            return WeaponPresentationCue.AutomaticCannonHeadHunterActive;

        return Runtime != null && Runtime.State == WeaponState.Manual
            ? WeaponPresentationCue.AutomaticCannonHeadHunterManual
            : WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic;
    }

    private ProjectilePresentationArchetypeId GetCannonProjectileArchetype()
    {
        AutomaticCannonPresentationSettings presentation = Runtime?.Data?.PresentationProfile?.AutomaticCannon;
        int frequency = IsContinuousFirePath()
            ? presentation?.ContinuousTracerFrequency ?? 5
            : presentation?.BaseTracerFrequency ?? 3;
        _presentationShotIndex++;
        return _presentationShotIndex % Mathf.Max(1, frequency) == 0
            ? ProjectilePresentationArchetypeId.CannonTracer
            : ProjectilePresentationArchetypeId.CannonRound;
    }

    private ProjectilePresentationArchetypeId GetLineBurstProjectileArchetype()
    {
        if (IsContinuousFirePath())
            return ProjectilePresentationArchetypeId.Default;

        // A middle-of-burst trail hides every later round when the shooter is stationary.
        return _lineBurstRemaining <= 1
            ? ProjectilePresentationArchetypeId.CannonTracer
            : ProjectilePresentationArchetypeId.CannonRound;
    }

    private WeaponFeedbackMode GetFeedbackMode(bool isAbility)
    {
        if (isAbility)
            return WeaponFeedbackMode.Active;
        return Runtime != null && Runtime.State == WeaponState.Manual
            ? WeaponFeedbackMode.Manual
            : WeaponFeedbackMode.Automatic;
    }

    private WeaponFeedbackContext CreateFeedbackContext(
        WeaponFeedbackMode mode,
        Vector3 position,
        Vector3 direction,
        bool isAbility,
        float intensity = 1f,
        Transform target = null,
        Transform anchor = null)
    {
        return new WeaponFeedbackContext(
            Runtime,
            mode,
            Heat != null ? Heat.NormalizedHeat : 0f,
            position,
            direction,
            isAbilityDamage: isAbility,
            eventIntensity: intensity,
            target: target,
            anchor: anchor);
    }

    private static WeaponPresentationContext CreateLegacyContext(
        WeaponPresentationCue cue,
        in WeaponFeedbackContext feedback)
    {
        return new WeaponPresentationContext(
            cue,
            feedback.Weapon,
            feedback.Origin,
            feedback.Direction,
            feedback.EventIntensity,
            feedback.Target,
            feedback.IsAbilityDamage,
            feedback.IsCritical,
            feedback.IsWeakPoint,
            feedback.Anchor,
            feedback.Mode,
            feedback.UpgradePath,
            feedback.WeaponLevel,
            feedback.NormalizedHeat,
            feedback.ImpactNormal,
            feedback.DamageAmount,
            feedback.IsKill,
            feedback.TargetClass,
            feedback.SurfaceType,
            feedback.ExplosionRadius);
    }

    private void EmitShotFeedback(
        WeaponPresentationCue legacyCue,
        Vector3 position,
        Vector3 direction,
        bool isAbility,
        Transform target = null,
        Transform anchor = null)
    {
        WeaponFeedbackContext feedback = CreateFeedbackContext(
            GetFeedbackMode(isAbility),
            position,
            direction,
            isAbility,
            target: target,
            anchor: anchor);
        if (Presentation is IWeaponFeedbackSink semantic)
        {
            EmitHeatThresholdIfNeeded(semantic, in feedback);
            semantic.OnShotFired(in feedback);
        }
        else
            EmitPresentationCue(legacyCue, position, direction, isAbility, target, anchor: anchor);
    }

    private void BeginSustainedFeedback(Vector3 direction, bool isAbility)
    {
        if (Spawn == null)
            return;

        WeaponFeedbackContext feedback = CreateFeedbackContext(
            GetFeedbackMode(isAbility),
            Spawn.position,
            direction,
            isAbility,
            anchor: Spawn);
        if (Presentation is IWeaponFeedbackSink semantic)
        {
            semantic.OnSustainedFireStarted(in feedback);
            return;
        }

        if (_legacyContinuousLoopHandle.IsValid)
            return;
        WeaponPresentationContext legacy = CreateLegacyContext(
            WeaponPresentationCue.AutomaticCannonContinuousLoop,
            in feedback);
        _legacyContinuousLoopHandle = Presentation.BeginLoop(in legacy);
    }

    private void EndSustainedFeedback(Vector3 direction, bool isAbility)
    {
        if (Spawn == null)
            return;

        WeaponFeedbackContext feedback = CreateFeedbackContext(
            GetFeedbackMode(isAbility),
            Spawn.position,
            direction,
            isAbility,
            anchor: Spawn);
        if (Presentation is IWeaponFeedbackSink semantic)
            semantic.OnSustainedFireStopped(in feedback);
        else if (_legacyContinuousLoopHandle.IsValid)
        {
            WeaponPresentationContext legacy = CreateLegacyContext(
                WeaponPresentationCue.AutomaticCannonContinuousLoop,
                in feedback);
            Presentation.EndLoop(_legacyContinuousLoopHandle, in legacy);
        }

        _legacyContinuousLoopHandle = default;
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

    private void EmitAmmoEmptyFeedback(Vector3 direction)
    {
        Vector3 position = Spawn != null ? Spawn.position : Owner != null ? Owner.position : Vector3.zero;
        WeaponFeedbackContext feedback = CreateFeedbackContext(
            WeaponFeedbackMode.Manual,
            position,
            direction,
            isAbility: false,
            anchor: Spawn);
        if (Presentation is IWeaponFeedbackSink semantic)
            semantic.OnAmmoEmpty(in feedback);
    }

    private void EmitHeatThresholdIfNeeded(
        IWeaponFeedbackSink semantic,
        in WeaponFeedbackContext feedback)
    {
        float current = feedback.NormalizedHeat;
        if (current + 0.001f < _lastPresentedHeat)
            _lastPresentedHeat = current;

        float crossed = 0f;
        for (int i = 0; i < PresentationHeatThresholds.Length; i++)
        {
            if (_lastPresentedHeat < PresentationHeatThresholds[i] && current >= PresentationHeatThresholds[i])
                crossed = PresentationHeatThresholds[i];
        }

        _lastPresentedHeat = current;
        if (crossed > 0f)
            semantic.OnHeatThresholdCrossed(in feedback, crossed);
    }

    private void EmitPresentationCue(
        WeaponPresentationCue cue,
        Vector3 position,
        Vector3 direction,
        bool isAbility,
        Transform target = null,
        bool isCritical = false,
        bool isWeakPoint = false,
        Transform anchor = null)
    {
        if (cue == WeaponPresentationCue.None)
            return;

        WeaponPresentationContext context = new(
            cue,
            Runtime,
            position,
            direction,
            target: target,
            isAbility: isAbility,
            isCritical: isCritical,
            isWeakPoint: isWeakPoint,
            anchor: anchor);
        Presentation.Emit(in context);
    }

    // Avoids LookRotation instability if the shot direction points almost straight up/down.
    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
    }
}
