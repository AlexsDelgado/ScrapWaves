using System;

/// <summary>Blade dimensions are meters before player area scaling. Disabled values retain legacy formulas.</summary>
[Serializable]
public sealed class WeaponBladeStatOverrides
{
    public WeaponOptionalFloat OrbitRadiusMeters;
    public WeaponOptionalFloat AutomaticHitRadiusMeters;
    public WeaponOptionalFloat RotationsPerSecond;
    public WeaponOptionalFloat ManualConeAngleDegrees;
    public WeaponOptionalFloat ThrustLengthMeters;
    public WeaponOptionalFloat AbilityHitRadiusMeters;
    public WeaponOptionalFloat AtomicDashRangeMeters;
    public WeaponOptionalFloat AtomicHitRadiusMeters;
    public WeaponOptionalFloat AtomicRangePerHitMultiplier;
    public WeaponOptionalFloat AtomicDashSegmentSeconds;
    public WeaponOptionalFloat AtomicPostDashInvulnerabilitySeconds;
    public WeaponOptionalInt ExtraBladeCount;
    public WeaponOptionalFloat MultiBladeActionIntervalSeconds;
    public WeaponOptionalFloat MultiBladeSpreadAngleDegrees;

    public bool TryValidate(out string error)
    {
        if (!Valid(OrbitRadiusMeters) || !Valid(AutomaticHitRadiusMeters) || !Valid(RotationsPerSecond)
            || !Valid(ManualConeAngleDegrees, 180f)
            || ManualConeAngleDegrees.Enabled && ManualConeAngleDegrees.Value < 1f
            || !Valid(ThrustLengthMeters)
            || !Valid(AbilityHitRadiusMeters) || !Valid(AtomicDashRangeMeters) || !Valid(AtomicHitRadiusMeters)
            || !Valid(AtomicRangePerHitMultiplier) || !Valid(AtomicDashSegmentSeconds)
            || !Valid(AtomicPostDashInvulnerabilitySeconds) || !Valid(MultiBladeActionIntervalSeconds)
            || !Valid(MultiBladeSpreadAngleDegrees, 360f)
            || ExtraBladeCount.Enabled && (ExtraBladeCount.Value < 0 || ExtraBladeCount.Value > 63))
        {
            error = "Blade values must be finite and nonnegative; authored cone angle is 1-180 degrees, spread is 0-360 degrees, and extra blades are 0-63.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool Valid(WeaponOptionalFloat value, float maximum = float.MaxValue) =>
        !value.Enabled || !float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
        && value.Value >= 0f && value.Value <= maximum;
}
