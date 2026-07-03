using UnityEngine;

public readonly struct WeaponDamageRoll
{
    public readonly WeaponInstance Weapon;
    public readonly bool EliteOrBoss;
    public readonly bool CanCrit;
    public readonly bool IsCritical;
    public readonly bool IsAbilityDamage;
    public readonly float BaseDamage;
    public readonly float FinalDamage;

    public WeaponDamageRoll(WeaponInstance weapon, bool eliteOrBoss, bool canCrit, bool isCritical, float baseDamage, float finalDamage, bool isAbilityDamage = false)
    {
        Weapon = weapon;
        EliteOrBoss = eliteOrBoss;
        CanCrit = canCrit;
        IsCritical = isCritical;
        IsAbilityDamage = isAbilityDamage;
        BaseDamage = baseDamage;
        FinalDamage = finalDamage;
    }
}

public static class WeaponDamageResolver
{
    public static event System.Action<WeaponDamageRoll> OnDamageResolved;

    // Calculates damage from weapon base, level/path, stats, and crit.
    public static float CalculateDamage(PlayerStats stats, WeaponInstance instance, bool eliteOrBoss, bool canCrit, float critMultiplierOverride = 1f, bool isAbilityDamage = false)
    {
        float damage = Mathf.Max(0f, instance.Data.BaseDamage);
        float baseDamage = damage;
        damage *= GetLevelDamageMultiplier(instance);
        damage *= GetPathDamageMultiplier(instance);
        damage *= Mathf.Max(0f, stats.GetStat(StatType.DamageMultiplier));

        if (isAbilityDamage)
            damage *= WeaponMath.GetStatScale(stats, StatType.AbilityDamageMultiplier);

        if (eliteOrBoss)
            damage *= Mathf.Max(0f, stats.GetStat(StatType.EliteDamageMultiplier));

        bool isCritical = canCrit && RollCrit(stats);
        if (isCritical)
            damage *= Mathf.Max(1f, stats.GetStat(StatType.CriticalDamage) * critMultiplierOverride);

        OnDamageResolved?.Invoke(new WeaponDamageRoll(instance, eliteOrBoss, canCrit, isCritical, baseDamage, damage, isAbilityDamage));
        return damage;
    }

    // Returns configured level damage multiplier for weapon instance.
    private static float GetLevelDamageMultiplier(WeaponInstance instance)
    {
        WeaponLevelData levelData = WeaponMath.GetLevelData(instance);
        return levelData != null ? Mathf.Max(0.01f, levelData.DamageMultiplier) : 1f;
    }

    // Returns selected path damage multiplier if advanced path exists.
    private static float GetPathDamageMultiplier(WeaponInstance instance)
    {
        WeaponUpgradePathData pathData = WeaponMath.GetPathData(instance);
        return pathData != null ? Mathf.Max(0.01f, pathData.DamageMultiplier) : 1f;
    }

    // Rolls crit chance from stat system with clamping.
    private static bool RollCrit(PlayerStats stats)
    {
        float critChance = Mathf.Clamp01(stats.GetStat(StatType.CriticalChance));
        return UnityEngine.Random.value <= critChance;
    }
}
