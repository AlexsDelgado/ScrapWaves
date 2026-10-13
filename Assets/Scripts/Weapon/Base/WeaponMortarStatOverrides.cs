using System;

/// <summary>Typed mortar controls. Grapeshot is path A; Multi-Charged is path B.</summary>
[Serializable]
public sealed class WeaponMortarStatOverrides
{
    public bool EnableCriticalHits;
    public WeaponOptionalFloat BarrageRadiusMeters;
    public WeaponOptionalInt ActiveShellCount;
    // The legacy sheet says "Extra projectile number"; this is TOTAL children per ordinary shell.
    public WeaponOptionalInt GrapeshotPelletCount;
    public WeaponOptionalFloat GrapeshotAbilityProjectilesPerSecond;
    public WeaponOptionalFloat GrapeshotAbilityDurationSeconds;
    // Includes the initial explosion; 1 means no repeats.
    public WeaponOptionalInt MultiChargedTotalExplosions;
    public WeaponOptionalFloat MultiChargedRepeatDelaySeconds;

    // Target distribution radius, independent of each child shell's blast radius.
    public WeaponOptionalFloat GrapeshotAutomaticLandingSpreadMeters;
    public WeaponOptionalFloat GrapeshotManualLandingSpreadMeters;
    public WeaponOptionalFloat GrapeshotDamageMultiplier;
    public WeaponOptionalFloat AbilityHeatShellBonus;
    public WeaponOptionalFloat GrapeshotAbilityHeatProjectilesPerSecond;
    public WeaponOptionalFloat BasicAbilityShellSpacingSeconds;

    public bool TryValidate(out string error)
    {
        if (!Valid(BarrageRadiusMeters, 0f, 10000f)
            || !Valid(ActiveShellCount, 0, 1024)
            || !Valid(GrapeshotPelletCount, 0, 1024)
            || !Valid(GrapeshotAbilityProjectilesPerSecond, 0f, 1000f)
            || !Valid(GrapeshotAbilityDurationSeconds, 0.01f, 120f)
            || !Valid(MultiChargedTotalExplosions, 1, 1024)
            || !Valid(MultiChargedRepeatDelaySeconds, 0.01f, 120f)
            || !Valid(GrapeshotAutomaticLandingSpreadMeters, 0f, 10000f)
            || !Valid(GrapeshotManualLandingSpreadMeters, 0f, 10000f)
            || !Valid(GrapeshotDamageMultiplier, 0f, 1000f)
            || !Valid(AbilityHeatShellBonus, 0f, 1024f)
            || !Valid(GrapeshotAbilityHeatProjectilesPerSecond, 0f, 1000f)
            || !Valid(BasicAbilityShellSpacingSeconds, 0f, 120f))
        {
            error = "Mortar: radius 0-10000 m; shell/pellet count 0-1024; rain rate 0-1000/s; duration/repeat delay 0.01-120 s; total explosions 1-1024.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool Valid(WeaponOptionalFloat value, float minimum, float maximum) => !value.Enabled
        || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value >= minimum && value.Value <= maximum);
    private static bool Valid(WeaponOptionalInt value, int minimum, int maximum) => !value.Enabled
        || (value.Value >= minimum && value.Value <= maximum);
}
