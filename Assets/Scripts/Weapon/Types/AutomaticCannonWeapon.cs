using System.Collections.Generic;
using UnityEngine;

public sealed class AutomaticCannonWeapon : BasicProjectileWeapon
{
    private static readonly Color ContinuousFireVfxColor = new(1f, 0.58f, 0.12f, 0.85f);
    private static readonly Color HeadHunterVfxColor = new(0.25f, 0.95f, 1f, 0.95f);

    private readonly List<Transform> _piercingTargets = new();
    private readonly List<Vector3> _piercingHitOrigins = new();
    private readonly Vector3[] _piercingLine = new Vector3[2];

    public AutomaticCannonWeapon(IWeaponTargeting targeting, ProjectilePool pool, Transform spawn)
        : base(targeting, pool, spawn)
    {
    }

    // Fires three-round burst in automatic mode.
    public override void TickAutomatic(float deltaTime, Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Automatic)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (Spawn == null)
            return;

        if (!Targeting.TryGetTarget(Runtime, Owner, Runtime.Data.BaseRange, aimDirection, out Transform target))
            return;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        FireTimer = GetFireInterval();
        if (IsHeadHunterPath())
            WeaponUpgradeVfx.SpawnTargetPulse(target, HeadHunterVfxColor, 0.35f, WeaponEnemyClassifier.CountsAsEliteOrBoss(target) ? "HUNT" : null);

        FireLineBurst(
            target.position - Spawn.position,
            Mathf.Max(1, tuning.CannonAutoBurstCount + GetContinuousFireBonus()),
            GetHeatDamageMultiplier() * GetHeadHunterScale(target),
            tuning.CannonAutoLineSpacing,
            tuning.CannonAutoAccuracySpreadDegrees,
            tuning.CannonBurstProjectileScatterDegrees,
            WeaponEnemyClassifier.CountsAsEliteOrBoss(target));
    }

    // Fires five-round burst in manual mode.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
        if (!isFiring || FireTimer > 0f)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        int manualBurstCount = Mathf.Max(1, tuning.CannonManualBurstCount + GetContinuousFireBonus());
        int bulletsToFire = Mathf.Clamp(Mathf.CeilToInt(Runtime.CurrentAmmo), 1, manualBurstCount);
        if (!TrySpendManualAmmo(bulletsToFire, requireFullAmount: false))
            return;

        FireTimer = AutomaticCannonFireLogic.GetManualBurstInterval(
            tuning.CannonManualBurstsPerSecond,
            WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier),
            WeaponMath.GetAttackRateMultiplier(Runtime) * GetContinuousFireAttackSpeedMultiplier());
        FireLineBurst(
            aimDirection,
            bulletsToFire,
            1f,
            tuning.CannonManualLineSpacing,
            0f,
            tuning.CannonBurstProjectileScatterDegrees,
            false);
    }

    // Fires spread burst active ability, scaled by heat.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (!CanBeginActiveAbility())
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        if (IsHeadHunterPath())
        {
            FirePiercingHeadHunterShot(aimDirection);
            CompleteActiveAbility();
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

        return WeaponEnemyClassifier.GetKind(target) switch
        {
            WeaponEnemyKind.Boss => 3f,
            WeaponEnemyKind.Elite => 2f,
            _ => 1.15f
        };
    }

    private void FirePiercingHeadHunterShot(Vector3 aimDirection)
    {
        if (Spawn == null || aimDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 origin = Spawn.position;
        Vector3 direction = aimDirection.normalized;
        _piercingLine[0] = origin;
        _piercingLine[1] = origin + direction * Runtime.Data.BaseRange;
        WeaponUpgradeVfx.SpawnBeam(_piercingLine[0], _piercingLine[1], HeadHunterVfxColor, 0.5f, 0.14f, "WEAK");

        int hitCount = EnemyRegistry.CollectClosestNearPolyline(
            _piercingLine,
            _piercingLine.Length,
            0.45f,
            128,
            _piercingTargets,
            _piercingHitOrigins);

        for (int i = 0; i < hitCount; i++)
        {
            IDamageable damageable = _piercingTargets[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(_piercingTargets[i]);
            float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), GetCritMultiplierOverride());
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * GetHeadHunterWeakPointScale()));
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
                WeaponUpgradeVfx.SpawnTargetPulse(_piercingTargets[i], HeadHunterVfxColor, 0.55f, "WEAK");
        }
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
        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        baseDirection = ApplyAccuracySpread(baseDirection, accuracySpreadDegrees);
        float spacing = Mathf.Max(0f, lineSpacing);
        if (IsContinuousFirePath())
        {
            float visualRange = Runtime?.Data != null ? Runtime.Data.BaseRange : spacing * Mathf.Max(1, count);
            WeaponUpgradeVfx.SpawnBeam(Spawn.position, Spawn.position + baseDirection * visualRange, ContinuousFireVfxColor, 0.14f, 0.06f, null);
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 position = Spawn.position + baseDirection * (spacing * i);
            Vector3 shotDirection = AutomaticCannonFireLogic.ApplyProjectileScatter(
                baseDirection,
                projectileScatterDegrees,
                UnityEngine.Random.insideUnitCircle);
            FireFromPositionInDirection(position, shotDirection, damageScale, eliteOrBoss);
        }
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
            FireInDirection(shotDirection, damageScale, false);
        }
    }

    // Avoids LookRotation instability if the shot direction points almost straight up/down.
    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
    }
}
