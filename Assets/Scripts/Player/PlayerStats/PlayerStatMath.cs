using UnityEngine;

public static class PlayerStatMath
{
    public static int ApplyDamageResistance(PlayerStats stats, int incomingDamage)
    {
        if (incomingDamage <= 0)
            return 0;

        float resistance = GetFractionStat(stats, StatType.DamageResistance, 0f, 0.95f);
        return Mathf.Max(1, Mathf.RoundToInt(incomingDamage * (1f - resistance)));
    }

    public static float GetHealthRegenerationPerSecond(PlayerStats stats)
    {
        if (!HasStat(stats, StatType.HealthRegeneration))
            return 0f;

        return Mathf.Max(0f, stats.GetStat(StatType.HealthRegeneration));
    }

    public static int CalculateLifestealHeal(PlayerStats stats, int damageDealt)
    {
        if (damageDealt <= 0)
            return 0;

        float lifesteal = GetFractionStat(stats, StatType.Lifesteal, 0f, 1f);
        return Mathf.Max(0, Mathf.RoundToInt(damageDealt * lifesteal));
    }

    public static float GetPickupRange(PlayerStats stats, float fallback)
    {
        if (!HasStat(stats, StatType.PickupRange))
            return Mathf.Max(0f, fallback);

        return Mathf.Max(0f, stats.GetStat(StatType.PickupRange));
    }

    public static float GetExtraEliteChance(PlayerStats stats)
    {
        return GetFractionStat(stats, StatType.ExtraEliteChance, 0f, 0.95f);
    }

    private static float GetFractionStat(PlayerStats stats, StatType statType, float fallback, float max)
    {
        if (!HasStat(stats, statType))
            return fallback;

        return Mathf.Clamp(stats.GetStat(statType), 0f, Mathf.Max(0f, max));
    }

    private static bool HasStat(PlayerStats stats, StatType statType)
    {
        return stats != null && stats.GetDefinition(statType) != null;
    }
}

public static class PlayerDropMath
{
    public static int RollMaterialDropCount(PlayerStats stats, float dropRoll, float doubleDropRoll)
    {
        if (dropRoll >= GetScavengingDropChance(stats))
            return 0;

        return doubleDropRoll < GetDoubleDropChance(stats) ? 2 : 1;
    }

    public static float GetScavengingDropChance(PlayerStats stats)
    {
        if (stats == null || stats.GetDefinition(StatType.Scavenging) == null)
            return 0f;

        return Mathf.Clamp01(stats.GetStat(StatType.Scavenging) / 100f);
    }

    public static float GetDoubleDropChance(PlayerStats stats)
    {
        if (stats == null || stats.GetDefinition(StatType.DoubleDrop) == null)
            return 0f;

        float scavengingBonus = 0f;
        StatDefinition scavengingDefinition = stats.GetDefinition(StatType.Scavenging);
        if (scavengingDefinition != null)
            scavengingBonus = Mathf.Max(0f, stats.GetStat(StatType.Scavenging) - scavengingDefinition.BaseValue);

        return Mathf.Clamp01((stats.GetStat(StatType.DoubleDrop) + scavengingBonus) / 100f);
    }
}
