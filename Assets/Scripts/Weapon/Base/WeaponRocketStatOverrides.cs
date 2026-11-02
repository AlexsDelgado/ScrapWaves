using System;

/// <summary>Exact-level Rocket controls. Disabled entries retain current authored tuning and path formulas.</summary>
[Serializable]
public sealed class WeaponRocketStatOverrides
{
    public WeaponOptionalFloat AutomaticProjectileTravelRangeMeters;
    public WeaponOptionalFloat AbilityProjectileTravelRangeMeters;
    public WeaponOptionalFloat AbilityClusterExplosionRadiusMeters;
    public WeaponOptionalFloat AbilityClusterTravelRangeMeters;
    public WeaponOptionalFloat KineticDebuffDurationSeconds;
    public WeaponOptionalFloat KineticDamageTakenMultiplier;
    public WeaponOptionalFloat FragmentConeRangePerExplosionRadius;
    public WeaponOptionalFloat FragmentConeAngleDegrees;
    public WeaponOptionalFloat AutomaticProjectileLifetimeSeconds;
    public WeaponOptionalFloat ManualProjectileLifetimeSeconds;
    public WeaponOptionalFloat AbilityProjectileLifetimeSeconds;
    public WeaponOptionalFloat KineticExplosionRadiusMultiplier;
    public WeaponOptionalFloat KineticExplosionFalloffMultiplier;
    public WeaponOptionalFloat KineticOrdinaryKnockbackMultiplier;
    public WeaponOptionalFloat KineticAbilityKnockbackMultiplier;
    public WeaponOptionalFloat FragmentOrdinaryExplosionRadiusMultiplier;
    public WeaponOptionalFloat FragmentKnockbackMultiplier;
    public WeaponOptionalInt FragmentAdditionalAutomaticRockets;
    public WeaponOptionalFloat FragmentDamageMultiplier;
    public WeaponOptionalInt AbilityClusterRocketCount;
    public WeaponOptionalFloat AbilityClusterDamageMultiplier;
    public WeaponOptionalFloat AbilityClusterSpeedMultiplier;
    public WeaponOptionalFloat AbilityClusterFalloff;
    public WeaponOptionalFloat AbilityClusterLifetimeSeconds;

    public bool TryValidate(out string error)
    {
        if (FragmentConeAngleDegrees.Enabled && FragmentConeAngleDegrees.Value > 0f && FragmentConeAngleDegrees.Value < 1f)
        {
            error = "Rocket fragment cone angle must be zero (disabled), or at least 1 degree.";
            return false;
        }
        return Valid(AutomaticProjectileTravelRangeMeters, nameof(AutomaticProjectileTravelRangeMeters), float.Epsilon, float.MaxValue, out error)
            && Valid(AbilityProjectileTravelRangeMeters, nameof(AbilityProjectileTravelRangeMeters), float.Epsilon, float.MaxValue, out error)
            && Valid(AbilityClusterExplosionRadiusMeters, nameof(AbilityClusterExplosionRadiusMeters), 0.05f, float.MaxValue, out error)
            && Valid(AbilityClusterTravelRangeMeters, nameof(AbilityClusterTravelRangeMeters), 0.1f, float.MaxValue, out error)
            && Valid(KineticDebuffDurationSeconds, nameof(KineticDebuffDurationSeconds), 0f, float.MaxValue, out error)
            && Valid(KineticDamageTakenMultiplier, nameof(KineticDamageTakenMultiplier), 1f, float.MaxValue, out error)
            && Valid(FragmentConeRangePerExplosionRadius, nameof(FragmentConeRangePerExplosionRadius), 0f, float.MaxValue, out error)
            && Valid(FragmentConeAngleDegrees, nameof(FragmentConeAngleDegrees), 0f, 180f, out error)
            && Valid(AutomaticProjectileLifetimeSeconds, nameof(AutomaticProjectileLifetimeSeconds), .05f, 120f, out error)
            && Valid(ManualProjectileLifetimeSeconds, nameof(ManualProjectileLifetimeSeconds), .05f, 120f, out error)
            && Valid(AbilityProjectileLifetimeSeconds, nameof(AbilityProjectileLifetimeSeconds), .05f, 120f, out error)
            && Valid(KineticExplosionRadiusMultiplier, nameof(KineticExplosionRadiusMultiplier), float.Epsilon, float.MaxValue, out error)
            && Valid(KineticExplosionFalloffMultiplier, nameof(KineticExplosionFalloffMultiplier), 0f, float.MaxValue, out error)
            && Valid(KineticOrdinaryKnockbackMultiplier, nameof(KineticOrdinaryKnockbackMultiplier), 0f, float.MaxValue, out error)
            && Valid(KineticAbilityKnockbackMultiplier, nameof(KineticAbilityKnockbackMultiplier), 0f, float.MaxValue, out error)
            && Valid(FragmentOrdinaryExplosionRadiusMultiplier, nameof(FragmentOrdinaryExplosionRadiusMultiplier), float.Epsilon, float.MaxValue, out error)
            && Valid(FragmentKnockbackMultiplier, nameof(FragmentKnockbackMultiplier), 0f, float.MaxValue, out error)
            && Valid(FragmentAdditionalAutomaticRockets, nameof(FragmentAdditionalAutomaticRockets), out error)
            && Valid(FragmentDamageMultiplier, nameof(FragmentDamageMultiplier), 0f, float.MaxValue, out error)
            && Valid(AbilityClusterRocketCount, nameof(AbilityClusterRocketCount), out error)
            && Valid(AbilityClusterDamageMultiplier, nameof(AbilityClusterDamageMultiplier), float.Epsilon, float.MaxValue, out error)
            && Valid(AbilityClusterSpeedMultiplier, nameof(AbilityClusterSpeedMultiplier), .01f, float.MaxValue, out error)
            && Valid(AbilityClusterFalloff, nameof(AbilityClusterFalloff), 0f, 1f, out error)
            && Valid(AbilityClusterLifetimeSeconds, nameof(AbilityClusterLifetimeSeconds), .05f, 120f, out error);
    }

    private static bool Valid(WeaponOptionalInt value, string name, out string error)
    {
        if (value.Enabled && (value.Value < 0 || value.Value > 1024))
        {
            error = $"Rocket {name} must be between 0 and 1024.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool Valid(WeaponOptionalFloat value, string name, float minimum, float maximum, out string error)
    {
        if (value.Enabled && (float.IsNaN(value.Value) || float.IsInfinity(value.Value)
            || value.Value < minimum || value.Value > maximum))
        {
            error = $"Rocket {name} must be finite and between {minimum} and {maximum}.";
            return false;
        }
        error = null;
        return true;
    }
}
