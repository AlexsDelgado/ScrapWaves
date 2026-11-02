using System;

/// <summary>Exact level/path replacements; disabled values preserve the current Flame formulas.</summary>
[Serializable]
public sealed class WeaponFlameStatOverrides
{
    public WeaponOptionalFloat AbilityWaveExpansionSpeedMetersPerSecond;
    public WeaponOptionalFloat ContactPuddleRadiusMeters;
    public WeaponOptionalFloat ContactPuddleDurationSeconds;
    public WeaponOptionalFloat AbilityPuddleRadiusMeters;
    public WeaponOptionalFloat AbilityPuddleDurationSeconds;
    public WeaponOptionalFloat AbilityFreezeDurationSeconds;

    public WeaponOptionalFloat NitrogenInitialSlowMultiplier;
    public WeaponOptionalFloat NitrogenMinimumSlowMultiplier;
    public WeaponOptionalInt NitrogenHitsToMinimum;
    public WeaponOptionalFloat NitrogenSlowDurationSeconds;
    public WeaponOptionalFloat AbilitySlowMultiplier;
    public WeaponOptionalFloat AbilitySlowDurationPerFreezeSecond;
    public WeaponOptionalFloat JellifiedBurnDamageMultiplier;
    public WeaponOptionalFloat JellifiedBurnDurationMultiplier;

    public bool TryValidate(out string error)
    {
        if (AbilityWaveExpansionSpeedMetersPerSecond.Enabled
            && (!WeaponStatProfile.ValidOptional(AbilityWaveExpansionSpeedMetersPerSecond, float.MaxValue)
                || AbilityWaveExpansionSpeedMetersPerSecond.Value <= 0f))
        {
            error = "Flame Q wave expansion speed must be finite and greater than zero (m/s). Disable the override to inherit the expansion duration.";
            return false;
        }
        if (!WeaponStatProfile.ValidOptional(ContactPuddleRadiusMeters, float.MaxValue)
            || !WeaponStatProfile.ValidOptional(ContactPuddleDurationSeconds, float.MaxValue)
            || !WeaponStatProfile.ValidOptional(AbilityPuddleRadiusMeters, float.MaxValue)
            || !WeaponStatProfile.ValidOptional(AbilityPuddleDurationSeconds, float.MaxValue)
            || !WeaponStatProfile.ValidOptional(AbilityFreezeDurationSeconds, float.MaxValue))
        {
            error = "Flame puddle radius, duration and freeze duration must be finite and nonnegative. Zero disables that effect.";
            return false;
        }
        if (!WeaponStatProfile.ValidOptional(NitrogenInitialSlowMultiplier, 1f)
            || !WeaponStatProfile.ValidOptional(NitrogenMinimumSlowMultiplier, 1f)
            || !WeaponStatProfile.ValidOptional(NitrogenSlowDurationSeconds, 120f)
            || !WeaponStatProfile.ValidOptional(AbilitySlowMultiplier, 1f)
            || !WeaponStatProfile.ValidOptional(AbilitySlowDurationPerFreezeSecond, 100f)
            || (NitrogenHitsToMinimum.Enabled && (NitrogenHitsToMinimum.Value < 1 || NitrogenHitsToMinimum.Value > 1024))
            || !PositiveFactor(JellifiedBurnDamageMultiplier)
            || !PositiveFactor(JellifiedBurnDurationMultiplier))
        {
            error = "Flame slow multipliers must be 0-1, ramp hits 1-1024, ordinary slow duration 0-120 s, Q slow seconds per freeze second 0-100, and Jellified burn factors greater than zero up to 1000.";
            return false;
        }
        float initial = NitrogenInitialSlowMultiplier.Enabled ? NitrogenInitialSlowMultiplier.Value : 0.5f;
        float minimum = NitrogenMinimumSlowMultiplier.Enabled ? NitrogenMinimumSlowMultiplier.Value : 0.1f;
        if (minimum > initial)
        {
            error = "Nitrogen minimum slow multiplier cannot exceed its initial multiplier (including inherited values).";
            return false;
        }
        float freeze = AbilityFreezeDurationSeconds.Enabled ? AbilityFreezeDurationSeconds.Value : 2f;
        float slowRatio = AbilitySlowDurationPerFreezeSecond.Enabled ? AbilitySlowDurationPerFreezeSecond.Value : 2f;
        if (float.IsInfinity(freeze * slowRatio))
        {
            error = "The resolved Q slow duration must remain finite.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool PositiveFactor(WeaponOptionalFloat value) => !value.Enabled
        || (WeaponStatProfile.ValidOptional(value, 1000f) && value.Value > 0f);
}
