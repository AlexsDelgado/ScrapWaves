using UnityEngine;

public static class WeaponMath
{
    // Gets exact level tuning row for current weapon level.
    public static WeaponLevelData GetLevelData(WeaponInstance instance)
    {
        if (instance == null || instance.Data == null)
            return null;

        for (int i = 0; i < instance.Data.LevelData.Count; i++)
        {
            WeaponLevelData entry = instance.Data.LevelData[i];
            if (entry != null && entry.Level == instance.Level)
                return entry;
        }

        return null;
    }

    // Gets selected path tuning payload for current weapon.
    public static WeaponUpgradePathData GetPathData(WeaponInstance instance)
    {
        if (instance == null || instance.Data == null || !instance.HasAdvancedPath)
            return null;

        return instance.SelectedPath switch
        {
            WeaponUpgradePath.PathA => instance.Data.PathA,
            WeaponUpgradePath.PathB => instance.Data.PathB,
            _ => null
        };
    }

    // Calculates max manual ammo including level and path modifiers.
    public static float GetMaxManualAmmo(WeaponInstance instance, PlayerStats stats)
    {
        if (instance == null || instance.Data == null || stats == null)
            return 0f;

        float ammo = Mathf.Max(0f, instance.Data.BaseManualAmmo);
        ammo *= GetStatScale(stats, StatType.AmmoMultiplier);

        WeaponLevelData levelData = GetLevelData(instance);
        WeaponUpgradePathData pathData = GetPathData(instance);

        if (levelData != null)
            ammo *= Mathf.Max(0.01f, levelData.ManualAmmoMultiplier);

        if (pathData != null && pathData.ManualAmmoOverride >= 0f)
            ammo = pathData.ManualAmmoOverride;

        return ammo;
    }

    // Reads a multiplier stat with a safe neutral fallback.
    public static float GetStatScale(PlayerStats stats, StatType statType)
    {
        if (stats == null)
            return 1f;

        if (stats.GetDefinition(statType) == null)
            return 1f;

        return Mathf.Max(0.01f, stats.GetStat(statType));
    }

    public static float GetAbilityCooldownDuration(WeaponInstance instance, PlayerStats stats)
    {
        if (instance?.Data == null)
            return 0f;

        float baseCooldown = Mathf.Max(0f, instance.Data.SkillCooldown);
        float reduction = GetAbilityCooldownReduction(stats);
        return baseCooldown * (1f - reduction);
    }

    public static float GetActiveAbilityAmmoCost(WeaponInstance instance)
    {
        if (instance?.Data == null)
            return 0f;

        if (instance.HasAdvancedPath
            && instance.Data.WeaponType == WeaponType.AutomaticCannon
            && instance.SelectedPath == WeaponUpgradePath.PathA)
            return 80f;

        return Mathf.Max(0f, instance.Data.ActiveAbilityAmmoCost);
    }

    public static float GetAbilityCooldownReduction(PlayerStats stats)
    {
        if (stats == null || stats.GetDefinition(StatType.AbilityCooldownReduction) == null)
            return 0f;

        return Mathf.Clamp(stats.GetStat(StatType.AbilityCooldownReduction), 0f, 0.95f);
    }

    // Calculates a gameplay-friendly impulse from weapon tuning, damage, player stat, and caller scale.
    public static float CalculateKnockback(PlayerStats stats, WeaponInstance instance, int damage, float scale = 1f, float falloffScale = 1f)
    {
        if (instance?.Data == null || scale <= 0f || falloffScale <= 0f)
            return 0f;

        float baseKnockback = Mathf.Max(0f, instance.Data.BaseKnockback);
        float statKnockback = GetStatScale(stats, StatType.Knockback);
        float damageFactor = 1f + Mathf.Sqrt(Mathf.Max(0, damage)) * 0.25f;
        return baseKnockback * statKnockback * Mathf.Max(0f, scale) * Mathf.Max(0f, falloffScale) * damageFactor;
    }

    // Calculates final attack-rate scalar from level and selected path.
    public static float GetAttackRateMultiplier(WeaponInstance instance)
    {
        float result = 1f;
        WeaponLevelData levelData = GetLevelData(instance);
        WeaponUpgradePathData pathData = GetPathData(instance);

        if (levelData != null)
            result *= Mathf.Max(0.01f, levelData.AttackRateMultiplier);
        if (pathData != null)
            result *= Mathf.Max(0.01f, pathData.AttackRateMultiplier);

        return result;
    }
}
