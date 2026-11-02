using System;

/// <summary>Exact-level cannon controls. Disabled values retain current tuning and heat formulas.</summary>
[Serializable]
public sealed class WeaponCannonStatOverrides
{
    // Independent maximum flight time; manual distance remains a separate upper bound.
    public WeaponOptionalFloat ManualProjectileLifetimeSeconds;
    public WeaponOptionalFloat ContinuousAbilityBulletsPerSecond;
    public WeaponOptionalFloat ContinuousAbilityDurationSeconds;
    public WeaponOptionalFloat HeadHunterWeakPointMultiplier;
    public WeaponOptionalFloat HeadHunterWeakPointHeatStep;
    public WeaponOptionalFloat HeadHunterWeakPointMaximum;

    public WeaponOptionalFloat ContinuousAbilityHeatDurationSteps;
    public WeaponOptionalFloat ContinuousModeHeatStep;
    public WeaponOptionalFloat ContinuousModeHeatSpeedBonus;
    public WeaponOptionalFloat HeadHunterEliteDamageMultiplier;
    public WeaponOptionalFloat HeadHunterBossDamageMultiplier;
    public WeaponOptionalInt HeadHunterPierceTargetCount;
    public WeaponOptionalFloat HeadHunterPierceDamageLossPerTarget;
    public WeaponOptionalFloat HeadHunterAbilityChargeSeconds;
    public WeaponOptionalFloat HeadHunterAbilityPierceRangeMeters;

    public bool TryValidate(out string error)
    {
        if (!Valid(ManualProjectileLifetimeSeconds, 0.05f, 120f)
            || !Valid(ContinuousAbilityBulletsPerSecond, 0f, 1000f)
            || !Valid(ContinuousAbilityDurationSeconds, 0.01f, 120f)
            || !Valid(HeadHunterWeakPointMultiplier, 0f, 1000f)
            || !Valid(HeadHunterWeakPointHeatStep, 0.001f, 1f)
            || !Valid(HeadHunterWeakPointMaximum, 0f, 1000f)
            || !Valid(ContinuousAbilityHeatDurationSteps, 0f, 120f)
            || !Valid(ContinuousModeHeatStep, 0.001f, 1f)
            || !Valid(ContinuousModeHeatSpeedBonus, 0f, 100f)
            || !Valid(HeadHunterEliteDamageMultiplier, 0f, 1000f)
            || !Valid(HeadHunterBossDamageMultiplier, 0f, 1000f)
            || (HeadHunterPierceTargetCount.Enabled && (HeadHunterPierceTargetCount.Value < 1 || HeadHunterPierceTargetCount.Value > 1024))
            || !Valid(HeadHunterPierceDamageLossPerTarget, 0f, 1f)
            || !Valid(HeadHunterAbilityChargeSeconds, 0f, 120f)
            || !Valid(HeadHunterAbilityPierceRangeMeters, 0.01f, 10000f))
        {
            error = "Cannon: Q rate must be 0-1000 bullets/s, duration 0.01-120 s, weak-point factors 0-1000, and heat step 0.001-1 (fraction).";
            return false;
        }
        error = null;
        return true;
    }

    private static bool Valid(WeaponOptionalFloat value, float minimum, float maximum) => !value.Enabled
        || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value >= minimum && value.Value <= maximum);
}
