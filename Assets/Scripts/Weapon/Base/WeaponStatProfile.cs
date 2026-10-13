using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>An enabled value is authored even when its value is zero.</summary>
[Serializable]
public struct WeaponOptionalFloat
{
    public bool Enabled;
    public float Value;
}

public enum WeaponCriticalChanceComposition
{
    AddToPlayer,
    ReplacePlayer
}

/// <summary>One independent replacement row; earlier levels are never accumulated.</summary>
[Serializable]
public sealed class WeaponStatLevelRow
{
    public WeaponType WeaponType;
    public int Level = 1;
    public WeaponUpgradePath Path;
    public WeaponStatModeValues Automatic = new();
    public WeaponStatModeValues Manual = new();
    public WeaponStatModeValues Ability = new();
    public WeaponCannonStatOverrides Cannon = new();
    public WeaponFlameStatOverrides Flame = new();
    public WeaponRocketStatOverrides Rocket = new();
    public WeaponMortarStatOverrides Mortar = new();
    public WeaponBladeStatOverrides Blade = new();
    public WeaponOptionalFloat CriticalChance;
    public WeaponCriticalChanceComposition CriticalChanceComposition = WeaponCriticalChanceComposition.AddToPlayer;
    public WeaponOptionalFloat CriticalDamageMultiplier;
    public WeaponOptionalFloat AbilityCooldownSeconds;
    public WeaponOptionalFloat AbilityAmmoCost;
}

/// <summary>
/// Versioned authoring data. Other weapon-type rows remain stored when the Inspector's
/// weapon selection changes, but only an exact current-type/level/path key can resolve.
/// </summary>
[Serializable]
public sealed class WeaponStatProfile
{
    // Typed readers avoid boxing optional structs while keeping direct mutable edits
    // visible immediately. Metadata discovery runs once; new optional fields safely
    // fall back to reflection until their allocation-free typed reader is added.
    private readonly struct OptionalPayloadField
    {
        public readonly string Name;
        public readonly Func<object, bool> IsEnabled;
        public OptionalPayloadField(string name, Func<object, bool> isEnabled) { Name = name; IsEnabled = isEnabled; }
    }
    private static readonly WeaponStatField[] ModeFields =
    {
        WeaponStatField.Damage, WeaponStatField.RangeMeters, WeaponStatField.AttackCyclesPerSecond,
        WeaponStatField.AmmoCapacity, WeaponStatField.KnockbackStrength, WeaponStatField.ExplosionRadiusMeters
    };
    private static readonly Dictionary<Type, OptionalPayloadField[]> OptionalPayloadFields = new()
    {
        { typeof(WeaponCannonStatOverrides), GetOptionalFields(typeof(WeaponCannonStatOverrides),
            new(nameof(WeaponCannonStatOverrides.ManualProjectileLifetimeSeconds), static value => ((WeaponCannonStatOverrides)value).ManualProjectileLifetimeSeconds.Enabled),
            new(nameof(WeaponCannonStatOverrides.ContinuousAbilityBulletsPerSecond), static value => ((WeaponCannonStatOverrides)value).ContinuousAbilityBulletsPerSecond.Enabled),
            new(nameof(WeaponCannonStatOverrides.ContinuousAbilityDurationSeconds), static value => ((WeaponCannonStatOverrides)value).ContinuousAbilityDurationSeconds.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterWeakPointMultiplier), static value => ((WeaponCannonStatOverrides)value).HeadHunterWeakPointMultiplier.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterWeakPointHeatStep), static value => ((WeaponCannonStatOverrides)value).HeadHunterWeakPointHeatStep.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterWeakPointMaximum), static value => ((WeaponCannonStatOverrides)value).HeadHunterWeakPointMaximum.Enabled),
            new(nameof(WeaponCannonStatOverrides.ContinuousAbilityHeatDurationSteps), static value => ((WeaponCannonStatOverrides)value).ContinuousAbilityHeatDurationSteps.Enabled),
            new(nameof(WeaponCannonStatOverrides.ContinuousModeHeatStep), static value => ((WeaponCannonStatOverrides)value).ContinuousModeHeatStep.Enabled),
            new(nameof(WeaponCannonStatOverrides.ContinuousModeHeatSpeedBonus), static value => ((WeaponCannonStatOverrides)value).ContinuousModeHeatSpeedBonus.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterEliteDamageMultiplier), static value => ((WeaponCannonStatOverrides)value).HeadHunterEliteDamageMultiplier.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterBossDamageMultiplier), static value => ((WeaponCannonStatOverrides)value).HeadHunterBossDamageMultiplier.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterPierceTargetCount), static value => ((WeaponCannonStatOverrides)value).HeadHunterPierceTargetCount.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterPierceDamageLossPerTarget), static value => ((WeaponCannonStatOverrides)value).HeadHunterPierceDamageLossPerTarget.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterAbilityChargeSeconds), static value => ((WeaponCannonStatOverrides)value).HeadHunterAbilityChargeSeconds.Enabled),
            new(nameof(WeaponCannonStatOverrides.HeadHunterAbilityPierceRangeMeters), static value => ((WeaponCannonStatOverrides)value).HeadHunterAbilityPierceRangeMeters.Enabled)) },
        { typeof(WeaponFlameStatOverrides), GetOptionalFields(typeof(WeaponFlameStatOverrides),
            new(nameof(WeaponFlameStatOverrides.ContactPuddleRadiusMeters), static value => ((WeaponFlameStatOverrides)value).ContactPuddleRadiusMeters.Enabled),
            new(nameof(WeaponFlameStatOverrides.ContactPuddleDurationSeconds), static value => ((WeaponFlameStatOverrides)value).ContactPuddleDurationSeconds.Enabled),
            new(nameof(WeaponFlameStatOverrides.AbilityPuddleRadiusMeters), static value => ((WeaponFlameStatOverrides)value).AbilityPuddleRadiusMeters.Enabled),
            new(nameof(WeaponFlameStatOverrides.AbilityPuddleDurationSeconds), static value => ((WeaponFlameStatOverrides)value).AbilityPuddleDurationSeconds.Enabled),
            new(nameof(WeaponFlameStatOverrides.AbilityFreezeDurationSeconds), static value => ((WeaponFlameStatOverrides)value).AbilityFreezeDurationSeconds.Enabled),
            new(nameof(WeaponFlameStatOverrides.NitrogenInitialSlowMultiplier), static value => ((WeaponFlameStatOverrides)value).NitrogenInitialSlowMultiplier.Enabled),
            new(nameof(WeaponFlameStatOverrides.NitrogenMinimumSlowMultiplier), static value => ((WeaponFlameStatOverrides)value).NitrogenMinimumSlowMultiplier.Enabled),
            new(nameof(WeaponFlameStatOverrides.NitrogenHitsToMinimum), static value => ((WeaponFlameStatOverrides)value).NitrogenHitsToMinimum.Enabled),
            new(nameof(WeaponFlameStatOverrides.NitrogenSlowDurationSeconds), static value => ((WeaponFlameStatOverrides)value).NitrogenSlowDurationSeconds.Enabled),
            new(nameof(WeaponFlameStatOverrides.AbilitySlowMultiplier), static value => ((WeaponFlameStatOverrides)value).AbilitySlowMultiplier.Enabled),
            new(nameof(WeaponFlameStatOverrides.AbilitySlowDurationPerFreezeSecond), static value => ((WeaponFlameStatOverrides)value).AbilitySlowDurationPerFreezeSecond.Enabled),
            new(nameof(WeaponFlameStatOverrides.JellifiedBurnDamageMultiplier), static value => ((WeaponFlameStatOverrides)value).JellifiedBurnDamageMultiplier.Enabled),
            new(nameof(WeaponFlameStatOverrides.JellifiedBurnDurationMultiplier), static value => ((WeaponFlameStatOverrides)value).JellifiedBurnDurationMultiplier.Enabled)) },
        { typeof(WeaponRocketStatOverrides), GetOptionalFields(typeof(WeaponRocketStatOverrides),
            new(nameof(WeaponRocketStatOverrides.AutomaticProjectileTravelRangeMeters), static value => ((WeaponRocketStatOverrides)value).AutomaticProjectileTravelRangeMeters.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityProjectileTravelRangeMeters), static value => ((WeaponRocketStatOverrides)value).AbilityProjectileTravelRangeMeters.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterExplosionRadiusMeters), static value => ((WeaponRocketStatOverrides)value).AbilityClusterExplosionRadiusMeters.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterTravelRangeMeters), static value => ((WeaponRocketStatOverrides)value).AbilityClusterTravelRangeMeters.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticDebuffDurationSeconds), static value => ((WeaponRocketStatOverrides)value).KineticDebuffDurationSeconds.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticDamageTakenMultiplier), static value => ((WeaponRocketStatOverrides)value).KineticDamageTakenMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentConeRangePerExplosionRadius), static value => ((WeaponRocketStatOverrides)value).FragmentConeRangePerExplosionRadius.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentConeAngleDegrees), static value => ((WeaponRocketStatOverrides)value).FragmentConeAngleDegrees.Enabled),
            new(nameof(WeaponRocketStatOverrides.AutomaticProjectileLifetimeSeconds), static value => ((WeaponRocketStatOverrides)value).AutomaticProjectileLifetimeSeconds.Enabled),
            new(nameof(WeaponRocketStatOverrides.ManualProjectileLifetimeSeconds), static value => ((WeaponRocketStatOverrides)value).ManualProjectileLifetimeSeconds.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityProjectileLifetimeSeconds), static value => ((WeaponRocketStatOverrides)value).AbilityProjectileLifetimeSeconds.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticExplosionRadiusMultiplier), static value => ((WeaponRocketStatOverrides)value).KineticExplosionRadiusMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticExplosionFalloffMultiplier), static value => ((WeaponRocketStatOverrides)value).KineticExplosionFalloffMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticOrdinaryKnockbackMultiplier), static value => ((WeaponRocketStatOverrides)value).KineticOrdinaryKnockbackMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.KineticAbilityKnockbackMultiplier), static value => ((WeaponRocketStatOverrides)value).KineticAbilityKnockbackMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentOrdinaryExplosionRadiusMultiplier), static value => ((WeaponRocketStatOverrides)value).FragmentOrdinaryExplosionRadiusMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentKnockbackMultiplier), static value => ((WeaponRocketStatOverrides)value).FragmentKnockbackMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentAdditionalAutomaticRockets), static value => ((WeaponRocketStatOverrides)value).FragmentAdditionalAutomaticRockets.Enabled),
            new(nameof(WeaponRocketStatOverrides.FragmentDamageMultiplier), static value => ((WeaponRocketStatOverrides)value).FragmentDamageMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterRocketCount), static value => ((WeaponRocketStatOverrides)value).AbilityClusterRocketCount.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterDamageMultiplier), static value => ((WeaponRocketStatOverrides)value).AbilityClusterDamageMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterSpeedMultiplier), static value => ((WeaponRocketStatOverrides)value).AbilityClusterSpeedMultiplier.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterFalloff), static value => ((WeaponRocketStatOverrides)value).AbilityClusterFalloff.Enabled),
            new(nameof(WeaponRocketStatOverrides.AbilityClusterLifetimeSeconds), static value => ((WeaponRocketStatOverrides)value).AbilityClusterLifetimeSeconds.Enabled)) },
        { typeof(WeaponMortarStatOverrides), GetOptionalFields(typeof(WeaponMortarStatOverrides),
            new(nameof(WeaponMortarStatOverrides.BarrageRadiusMeters), static value => ((WeaponMortarStatOverrides)value).BarrageRadiusMeters.Enabled),
            new(nameof(WeaponMortarStatOverrides.ActiveShellCount), static value => ((WeaponMortarStatOverrides)value).ActiveShellCount.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotPelletCount), static value => ((WeaponMortarStatOverrides)value).GrapeshotPelletCount.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotAbilityProjectilesPerSecond), static value => ((WeaponMortarStatOverrides)value).GrapeshotAbilityProjectilesPerSecond.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotAbilityDurationSeconds), static value => ((WeaponMortarStatOverrides)value).GrapeshotAbilityDurationSeconds.Enabled),
            new(nameof(WeaponMortarStatOverrides.MultiChargedTotalExplosions), static value => ((WeaponMortarStatOverrides)value).MultiChargedTotalExplosions.Enabled),
            new(nameof(WeaponMortarStatOverrides.MultiChargedRepeatDelaySeconds), static value => ((WeaponMortarStatOverrides)value).MultiChargedRepeatDelaySeconds.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotAutomaticLandingSpreadMeters), static value => ((WeaponMortarStatOverrides)value).GrapeshotAutomaticLandingSpreadMeters.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotManualLandingSpreadMeters), static value => ((WeaponMortarStatOverrides)value).GrapeshotManualLandingSpreadMeters.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotDamageMultiplier), static value => ((WeaponMortarStatOverrides)value).GrapeshotDamageMultiplier.Enabled),
            new(nameof(WeaponMortarStatOverrides.AbilityHeatShellBonus), static value => ((WeaponMortarStatOverrides)value).AbilityHeatShellBonus.Enabled),
            new(nameof(WeaponMortarStatOverrides.GrapeshotAbilityHeatProjectilesPerSecond), static value => ((WeaponMortarStatOverrides)value).GrapeshotAbilityHeatProjectilesPerSecond.Enabled),
            new(nameof(WeaponMortarStatOverrides.BasicAbilityShellSpacingSeconds), static value => ((WeaponMortarStatOverrides)value).BasicAbilityShellSpacingSeconds.Enabled)) },
        { typeof(WeaponBladeStatOverrides), GetOptionalFields(typeof(WeaponBladeStatOverrides),
            new(nameof(WeaponBladeStatOverrides.OrbitRadiusMeters), static value => ((WeaponBladeStatOverrides)value).OrbitRadiusMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.AutomaticHitRadiusMeters), static value => ((WeaponBladeStatOverrides)value).AutomaticHitRadiusMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.RotationsPerSecond), static value => ((WeaponBladeStatOverrides)value).RotationsPerSecond.Enabled),
            new(nameof(WeaponBladeStatOverrides.ManualConeAngleDegrees), static value => ((WeaponBladeStatOverrides)value).ManualConeAngleDegrees.Enabled),
            new(nameof(WeaponBladeStatOverrides.ThrustLengthMeters), static value => ((WeaponBladeStatOverrides)value).ThrustLengthMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.AbilityHitRadiusMeters), static value => ((WeaponBladeStatOverrides)value).AbilityHitRadiusMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.AtomicDashRangeMeters), static value => ((WeaponBladeStatOverrides)value).AtomicDashRangeMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.AtomicHitRadiusMeters), static value => ((WeaponBladeStatOverrides)value).AtomicHitRadiusMeters.Enabled),
            new(nameof(WeaponBladeStatOverrides.AtomicRangePerHitMultiplier), static value => ((WeaponBladeStatOverrides)value).AtomicRangePerHitMultiplier.Enabled),
            new(nameof(WeaponBladeStatOverrides.AtomicDashSegmentSeconds), static value => ((WeaponBladeStatOverrides)value).AtomicDashSegmentSeconds.Enabled),
            new(nameof(WeaponBladeStatOverrides.AtomicPostDashInvulnerabilitySeconds), static value => ((WeaponBladeStatOverrides)value).AtomicPostDashInvulnerabilitySeconds.Enabled),
            new(nameof(WeaponBladeStatOverrides.ExtraBladeCount), static value => ((WeaponBladeStatOverrides)value).ExtraBladeCount.Enabled),
            new(nameof(WeaponBladeStatOverrides.MultiBladeActionIntervalSeconds), static value => ((WeaponBladeStatOverrides)value).MultiBladeActionIntervalSeconds.Enabled),
            new(nameof(WeaponBladeStatOverrides.MultiBladeSpreadAngleDegrees), static value => ((WeaponBladeStatOverrides)value).MultiBladeSpreadAngleDegrees.Enabled)) }
    };
    public const int CurrentVersion = 1;
    public int Version = CurrentVersion;
    public List<WeaponStatLevelRow> Levels = new();

    public static WeaponStatLevelRow CreateRow(WeaponType type, int level, WeaponUpgradePath path)
    {
        if (!IsValidKey(type, level, path)) throw new ArgumentException("A row requires Basic levels 1-5/None or advanced levels 6-10/Path A or B.");
        return new WeaponStatLevelRow { WeaponType = type, Level = level, Path = path };
    }
    public bool TryValidate(WeaponType expectedType, out string error)
    {
        if (!IsSupportedWeaponType(expectedType))
        {
            error = "The weapon type is not supported.";
            return false;
        }
        return TryValidate(out error);
    }

    public bool TryValidate(out string error)
    {
        if (Version != CurrentVersion)
        {
            error = $"Unsupported weapon stat profile version {Version}; expected {CurrentVersion}.";
            return false;
        }
        if (Levels == null)
        {
            error = "Weapon stat profile rows cannot be null.";
            return false;
        }
        for (int i = 0; i < Levels.Count; i++)
        {
            WeaponStatLevelRow row = Levels[i];
            if (row == null)
            {
                error = $"Weapon stat profile row {i + 1} is null.";
                return false;
            }
            if (!IsValidKey(row.WeaponType, row.Level, row.Path))
            {
                error = $"Invalid weapon stat row {i + 1}: use Basic levels 1-5 with no path, or path A/B levels 6-10.";
                return false;
            }
            if (row.CriticalChanceComposition != WeaponCriticalChanceComposition.AddToPlayer && row.CriticalChanceComposition != WeaponCriticalChanceComposition.ReplacePlayer)
            {
                error = $"Weapon stat row {i + 1} has an unsupported critical chance composition.";
                return false;
            }
            if (!ValidOptional(row.CriticalChance, 1f)
                || !ValidOptional(row.CriticalDamageMultiplier, float.MaxValue)
                || !ValidOptional(row.AbilityCooldownSeconds, float.MaxValue))
            {
                error = $"Weapon stat row {i + 1} has an invalid enabled value: chance must be 0-1; damage factor and cooldown must be finite and nonnegative.";
                return false;
            }
            if (!ValidOptional(row.AbilityAmmoCost, float.MaxValue))
            {
                error = $"Weapon stat row {i + 1}: Q ammo cost (AbilityAmmoCost) must be finite and nonnegative.";
                return false;
            }
            if (!ValidateMode(row, WeaponStatMode.Automatic, row.Automatic, out error)
                || !ValidateMode(row, WeaponStatMode.Manual, row.Manual, out error)
                || !ValidateMode(row, WeaponStatMode.Ability, row.Ability, out error)) return false;
            bool payloadValid = row.WeaponType switch
            {
                WeaponType.AutomaticCannon => row.Cannon != null && row.Cannon.TryValidate(out error),
                WeaponType.Flamethrower => row.Flame != null && row.Flame.TryValidate(out error),
                WeaponType.RocketLauncher => row.Rocket != null && row.Rocket.TryValidate(out error),
                WeaponType.Mortar => row.Mortar != null && row.Mortar.TryValidate(out error),
                WeaponType.RotatingBlade => row.Blade != null && row.Blade.TryValidate(out error),
                _ => false
            };
            if (!payloadValid)
            {
                error = error ?? $"Missing typed payload for {row.WeaponType} at level {row.Level}.";
                return false;
            }
            object currentPayload = row.WeaponType switch
            {
                WeaponType.AutomaticCannon => row.Cannon,
                WeaponType.Flamethrower => row.Flame,
                WeaponType.RocketLauncher => row.Rocket,
                WeaponType.Mortar => row.Mortar,
                WeaponType.RotatingBlade => row.Blade,
                _ => null
            };
            if (!ValidatePayloadApplicability(row, currentPayload, out error)) return false;
            for (int previous = 0; previous < i; previous++)
            {
                WeaponStatLevelRow other = Levels[previous];
                if (other.WeaponType == row.WeaponType && other.Level == row.Level && other.Path == row.Path)
                {
                    error = $"Duplicate weapon stat row for {row.WeaponType}, level {row.Level}, {row.Path}.";
                    return false;
                }
            }
        }
        error = null;
        return true;
    }

    public static bool IsValidKey(WeaponType type, int level, WeaponUpgradePath path)
    {
        if (!IsSupportedWeaponType(type)) return false;
        return level >= 1 && level <= 5 && path == WeaponUpgradePath.None
            || level >= 6 && level <= 10 && (path == WeaponUpgradePath.PathA || path == WeaponUpgradePath.PathB);
    }

    private static OptionalPayloadField[] GetOptionalFields(Type type, params OptionalPayloadField[] typed)
    {
        var result = new List<OptionalPayloadField>(typed);
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType != typeof(WeaponOptionalFloat) && field.FieldType != typeof(WeaponOptionalInt)) continue;
            bool found = false;
            foreach (OptionalPayloadField known in typed)
                if (known.Name == field.Name) { found = true; break; }
            if (found) continue;
            result.Add(new OptionalPayloadField(field.Name, value => field.FieldType == typeof(WeaponOptionalFloat)
                ? ((WeaponOptionalFloat)field.GetValue(value)).Enabled : ((WeaponOptionalInt)field.GetValue(value)).Enabled));
        }
        return result.ToArray();
    }

    private static bool IsSupportedWeaponType(WeaponType type) => type == WeaponType.AutomaticCannon
        || type == WeaponType.Flamethrower || type == WeaponType.RocketLauncher
        || type == WeaponType.Mortar || type == WeaponType.RotatingBlade;
    private static bool ValidatePayloadApplicability(WeaponStatLevelRow row, object payload, out string error)
    {
        foreach (OptionalPayloadField field in OptionalPayloadFields[payload.GetType()])
        {
            if (!field.IsEnabled(payload)) continue;
            if (WeaponStatCapabilities.SupportsSpecificField(row.WeaponType, row.Level, row.Path, WeaponStatMode.Automatic, field.Name)
                || WeaponStatCapabilities.SupportsSpecificField(row.WeaponType, row.Level, row.Path, WeaponStatMode.Manual, field.Name)
                || WeaponStatCapabilities.SupportsSpecificField(row.WeaponType, row.Level, row.Path, WeaponStatMode.Ability, field.Name)) continue;
            error = $"{row.WeaponType}, level {row.Level}, {row.Path}: {field.Name} has no supported consumer on this path.";
            return false;
        }
        error = null;
        return true;
    }
    private static bool ValidateMode(WeaponStatLevelRow row, WeaponStatMode mode, WeaponStatModeValues values, out string error)
    {
        if (values == null)
        {
            error = $"Missing {mode} values for {row.WeaponType}, level {row.Level}.";
            return false;
        }
        foreach (WeaponStatField field in ModeFields)
        {
            WeaponOptionalFloat value = values.Get(field);
            if (!value.Enabled) continue;
            if (!WeaponStatCapabilities.SupportsModeField(row.WeaponType, row.Level, row.Path, mode, field))
            {
                error = $"{row.WeaponType}, level {row.Level}, {row.Path}: {mode}.{field} has no supported consumer.";
                return false;
            }
            if (!ValidOptional(value, float.MaxValue))
            {
                error = $"{mode}.{field} must be finite and nonnegative.";
                return false;
            }
            if (row.WeaponType == WeaponType.Flamethrower && field == WeaponStatField.AttackCyclesPerSecond && value.Value > 100f)
            {
                error = $"Flamethrower {mode}.AttackCyclesPerSecond cannot exceed the consumer's 100 ticks/second neutral limit.";
                return false;
            }
            if (row.WeaponType == WeaponType.Mortar && field == WeaponStatField.ExplosionRadiusMeters && value.Value > 0f && value.Value < 0.01f)
            {
                error = $"Mortar {mode}.ExplosionRadiusMeters must be zero (point hit/no ring), or at least 0.01 m to match the shell and landing-ring consumers.";
                return false;
            }
            if (row.WeaponType == WeaponType.RocketLauncher && field == WeaponStatField.ExplosionRadiusMeters && value.Value < 0.05f)
            {
                error = $"RocketLauncher {mode}.ExplosionRadiusMeters must be at least 0.05 m, matching the projectile explosion consumer.";
                return false;
            }
            if ((field == WeaponStatField.Damage || field == WeaponStatField.RangeMeters || field == WeaponStatField.AttackCyclesPerSecond) && value.Value <= 0f)
            {
                error = $"Authored {mode}.{field} must be greater than zero. Inherited behavior retains its existing minimum/unlimited conventions; zero needs a separate mechanic decision.";
                return false;
            }
        }
        error = null;
        return true;
    }
    public static bool ValidOptional(WeaponOptionalFloat value, float maximum) =>
        !value.Enabled || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
            && value.Value >= 0f && value.Value <= maximum);
}