using System;
using UnityEngine;

public enum WeaponStatValueSource
{
    Legacy,
    AuthoredReplacement,
    AuthoredAdditionToPlayer,
    AuthoredPlayerReplacement
}

/// <summary>A value and its origin; observing it never rolls randomness or changes state.</summary>
public readonly struct WeaponResolvedStat
{
    public readonly float Value;
    public readonly WeaponStatValueSource Source;
    public readonly WeaponType WeaponType;
    public readonly int Level;
    public readonly WeaponUpgradePath Path;
    public bool IsAuthored => Source != WeaponStatValueSource.Legacy;
    public string Provenance => IsAuthored
        ? $"{WeaponType}, level {Level}, {Path}: {Source}"
        : "Inherited current tuning";

    public WeaponResolvedStat(float value, WeaponStatValueSource source, WeaponInstance instance)
    {
        Value = value;
        Source = source;
        WeaponType = instance?.Data != null ? instance.Data.WeaponType : default;
        Level = instance?.Level ?? 0;
        Path = instance != null && instance.Level >= 6 ? instance.SelectedPath : WeaponUpgradePath.None;
    }
}

/// <summary>
/// Resolves only explicitly authored controls. Empty/disabled rows use current gameplay
/// tuning, and path rows replace exact levels without inheriting level 5 or another path.
/// </summary>
public static class WeaponStatAuthoringResolver
{
    public static WeaponStatLevelRow GetLevelRow(WeaponInstance instance)
    {
        if (instance?.Data == null) return null;
        WeaponUpgradePath path = instance.Level < 6 ? WeaponUpgradePath.None : instance.SelectedPath;
        return FindRow(instance.Data.StatProfile, instance.Data.WeaponType, instance.Level, path);
    }

    public static WeaponStatLevelRow FindRow(WeaponStatProfile profile, WeaponType type, int level, WeaponUpgradePath path)
    {
        if (profile == null) return null;
        if (!profile.TryValidate(type, out string error))
            throw new InvalidOperationException(error);
        // Legacy runtimes can temporarily have no advanced path. Preserve their old
        // behavior until a valid progression key is selected; never borrow a row.
        if (!WeaponStatProfile.IsValidKey(type, level, path)) return null;
        for (int i = 0; i < profile.Levels.Count; i++)
        {
            WeaponStatLevelRow row = profile.Levels[i];
            if (row.WeaponType == type && row.Level == level && row.Path == path)
                return row;
        }
        return null;
    }

    public static WeaponStatModeValues GetModeValues(WeaponStatLevelRow row, WeaponStatMode mode) => row == null ? null : mode switch
    {
        WeaponStatMode.Automatic => row.Automatic,
        WeaponStatMode.Manual => row.Manual,
        WeaponStatMode.Ability => row.Ability,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static WeaponResolvedStat ResolveModeValue(WeaponInstance instance, WeaponStatMode mode, WeaponStatField field, float legacyNeutral)
    {
        WeaponStatModeValues values = GetModeValues(GetLevelRow(instance), mode);
        return ResolveOptional(instance, values != null ? values.Get(field) : default, legacyNeutral);
    }

    public static WeaponResolvedStat ResolveOptional(WeaponInstance instance, WeaponOptionalFloat value, float legacyNeutral)
    {
        if (value.Enabled && !WeaponStatProfile.ValidOptional(value, float.MaxValue))
            throw new InvalidOperationException("An authored scalar must be finite and nonnegative.");
        return new WeaponResolvedStat(value.Enabled ? value.Value : legacyNeutral,
            value.Enabled ? WeaponStatValueSource.AuthoredReplacement : WeaponStatValueSource.Legacy, instance);
    }

    public static WeaponResolvedInt ResolveOptional(WeaponInstance instance, WeaponOptionalInt value, int legacyNeutral)
    {
        if (value.Enabled && value.Value < 0)
            throw new InvalidOperationException("An authored count must be nonnegative.");
        return new WeaponResolvedInt(value.Enabled ? value.Value : legacyNeutral,
            value.Enabled ? WeaponStatValueSource.AuthoredReplacement : WeaponStatValueSource.Legacy);
    }

    // This fallback is only for immediate legacy call sites. Deferred emitters carry
    // their explicit action mode instead of reading a subsequently switched runtime.
    public static WeaponStatMode GetImmediateMode(WeaponInstance instance, bool isAbilityDamage = false) =>
        isAbilityDamage ? WeaponStatMode.Ability
        : instance?.State == WeaponState.Manual ? WeaponStatMode.Manual : WeaponStatMode.Automatic;
    public static bool SupportsCriticalHits(WeaponInstance instance) =>
        instance?.Data == null || instance.Data.WeaponType != WeaponType.Mortar
        || GetLevelRow(instance)?.Mortar?.EnableCriticalHits == true;
    public static WeaponResolvedStat ResolveCriticalChance(WeaponInstance instance, float playerChance)
    {
        WeaponStatLevelRow row = GetLevelRow(instance);
        if (row == null || !row.CriticalChance.Enabled || !SupportsCriticalHits(instance))
            return new WeaponResolvedStat(Mathf.Clamp01(playerChance), WeaponStatValueSource.Legacy, instance);
        bool replace = row.CriticalChanceComposition == WeaponCriticalChanceComposition.ReplacePlayer;
        float chance = replace ? row.CriticalChance.Value : playerChance + row.CriticalChance.Value;
        return new WeaponResolvedStat(Mathf.Clamp01(chance), replace
            ? WeaponStatValueSource.AuthoredPlayerReplacement
            : WeaponStatValueSource.AuthoredAdditionToPlayer, instance);
    }

    public static float GetLegacyCriticalDamageMultiplier(WeaponInstance instance)
    {
        if (instance?.Data == null || instance.Data.WeaponType != WeaponType.AutomaticCannon)
            return 1f;
        float multiplier = instance.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride;
        if (instance.HasAdvancedPath && instance.SelectedPath == WeaponUpgradePath.PathB)
            multiplier *= 1.5f;
        return multiplier;
    }
    // The authored factor replaces only the legacy weapon factor. Player critical
    // severity and the existing final minimum of 1 remain in the damage pipeline.
    public static WeaponResolvedStat ResolveCriticalDamageMultiplier(WeaponInstance instance, float legacyWeaponMultiplier)
    {
        WeaponStatLevelRow row = GetLevelRow(instance);
        bool authored = row != null && row.CriticalDamageMultiplier.Enabled && SupportsCriticalHits(instance);
        return new WeaponResolvedStat(authored ? row.CriticalDamageMultiplier.Value : legacyWeaponMultiplier,
            authored ? WeaponStatValueSource.AuthoredReplacement : WeaponStatValueSource.Legacy, instance);
    }

    public static WeaponResolvedStat ResolveAbilityAmmoCost(WeaponInstance instance)
    {
        WeaponStatLevelRow row = GetLevelRow(instance);
        bool authored = row != null && row.AbilityAmmoCost.Enabled;
        float legacy = instance?.Data != null ? Mathf.Max(0f, instance.Data.ActiveAbilityAmmoCost) : 0f;
        if (instance?.Data != null && instance.HasAdvancedPath
            && instance.Data.WeaponType == WeaponType.AutomaticCannon && instance.SelectedPath == WeaponUpgradePath.PathA)
            legacy = 80f;
        return new WeaponResolvedStat(authored ? row.AbilityAmmoCost.Value : legacy,
            authored ? WeaponStatValueSource.AuthoredReplacement : WeaponStatValueSource.Legacy, instance);
    }
    // Player cooldown reduction is applied afterwards by WeaponMath, in the same order.
    public static WeaponResolvedStat ResolveAbilityCooldownSeconds(WeaponInstance instance)
    {
        WeaponStatLevelRow row = GetLevelRow(instance);
        bool authored = row != null && row.AbilityCooldownSeconds.Enabled;
        float legacy = instance?.Data != null ? Mathf.Max(0f, instance.Data.SkillCooldown) : 0f;
        return new WeaponResolvedStat(authored ? row.AbilityCooldownSeconds.Value : legacy,
            authored ? WeaponStatValueSource.AuthoredReplacement : WeaponStatValueSource.Legacy, instance);
    }
}