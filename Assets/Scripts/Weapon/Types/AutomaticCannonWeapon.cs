using UnityEngine;

public sealed class AutomaticCannonWeapon : BasicProjectileWeapon
{
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
        FireLineBurst(
            target.position - Spawn.position,
            Mathf.Max(1, tuning.CannonAutoBurstCount),
            GetHeatDamageMultiplier(),
            tuning.CannonAutoLineSpacing,
            tuning.CannonAutoAccuracySpreadDegrees);
    }

    // Fires five-round burst in manual mode.
    public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
    {
        if (Runtime.State != WeaponState.Manual || !isFiring)
            return;

        FireTimer -= deltaTime;
        if (FireTimer > 0f)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        int manualBurstCount = Mathf.Max(1, tuning.CannonManualBurstCount);
        int bulletsToFire = Mathf.Clamp(Mathf.CeilToInt(Runtime.CurrentAmmo), 1, manualBurstCount);
        if (!TrySpendManualAmmo(bulletsToFire, requireFullAmount: false))
            return;

        FireTimer = GetFireInterval();
        FireLineBurst(
            aimDirection,
            bulletsToFire,
            1f,
            tuning.CannonManualLineSpacing,
            0f);
    }

    // Fires spread burst active ability, scaled by heat.
    public override void UseActiveAbility(Vector3 aimDirection)
    {
        if (Runtime.State != WeaponState.Manual)
            return;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        if (!TrySpendManualAmmo(Runtime.Data.ActiveAbilityAmmoCost, requireFullAmount: true))
            return;

        AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
        int extra = GetActiveHeatBonusBulletCount(tuning);
        FireScatterBurst(
            aimDirection,
            Mathf.Max(1, tuning.CannonActiveBaseBulletCount) + extra,
            1f,
            tuning.CannonAbilityScatterRadius);
    }

    // Automatic cannon can critically strike, with a custom multiplier override below.
    public override bool CanCrit() => true;

    // Automatic cannon gains damage at 25/50/75 heat, not fire-rate scaling.
    protected override float GetHeatFireRateMultiplier() => 1f;

    // Critical hits deal double the normal critical damage effect.
    protected override float GetCritMultiplierOverride()
    {
        return Runtime?.Data != null ? Runtime.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride : AutomaticCannonTuning.Defaults.CannonCriticalDamageMultiplierOverride;
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

    // Spawns normal cannon bursts as a straight line of projectiles.
    private void FireLineBurst(Vector3 aimDirection, int count, float damageScale, float lineSpacing, float accuracySpreadDegrees)
    {
        Vector3 baseDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Spawn.forward;
        baseDirection = ApplyAccuracySpread(baseDirection, accuracySpreadDegrees);
        float spacing = Mathf.Max(0f, lineSpacing);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = Spawn.position + baseDirection * (spacing * i);
            FireFromPositionInDirection(position, baseDirection, damageScale, false);
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
