using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeaponLevelRecord
{
    public string WeaponId;
    public int HighestLevel;
}

[Serializable]
public class CustomProgressRecord
{
    public string Key;
    public float Value;
}

[Serializable]
public class WeaponKillRecord
{
    public string WeaponId;
    public int Kills;
}

[Serializable]
public class MetaStatLevelRecord
{
    public string StatTypeName;
    public int Level;
}

[Serializable]
public class MetaItemUpgradeRecord
{
    public string UnlockId;
    public int Level;
}

[Serializable]
public class SaveData
{
    public const int CurrentVersion = 3;

    public int Version = CurrentVersion;
    public int Scrap;
    public List<string> UnlockedIds = new();
    public List<string> UnlockedAchievementIds = new();

    public PresentationAccessibilitySettings PresentationAccessibility = new();

    public int TotalBossKills;
    public int TotalRunsCompleted;
    public int TotalEnemiesKilled;
    public float BestSurvivalTimeSeconds;
    public int HighestPlayerLevel;
    public int TotalEliteOrBossKills;
    public int TotalDropsLooted;

    public List<WeaponLevelRecord> WeaponLevels = new();
    public List<CustomProgressRecord> CustomProgress = new();
    public List<WeaponKillRecord> WeaponKills = new();
    public List<MetaStatLevelRecord> MetaStatLevels = new();
    public List<MetaItemUpgradeRecord> MetaItemUpgrades = new();

    public void Sanitize()
    {
        if (Version < CurrentVersion)
            Version = CurrentVersion;
        UnlockedIds ??= new List<string>();
        UnlockedAchievementIds ??= new List<string>();
        WeaponLevels ??= new List<WeaponLevelRecord>();
        CustomProgress ??= new List<CustomProgressRecord>();
        WeaponKills ??= new List<WeaponKillRecord>();
        MetaStatLevels ??= new List<MetaStatLevelRecord>();
        MetaItemUpgrades ??= new List<MetaItemUpgradeRecord>();
        PresentationAccessibility ??= new PresentationAccessibilitySettings();
        PresentationAccessibility.Sanitize();
    }

    public int GetMetaStatLevel(StatType statType)
    {
        string key = statType.ToString();
        for (int i = 0; i < MetaStatLevels.Count; i++)
        {
            if (MetaStatLevels[i].StatTypeName == key)
                return Mathf.Clamp(MetaStatLevels[i].Level, 0, 10);
        }

        return 0;
    }

    public void SetMetaStatLevel(StatType statType, int level)
    {
        string key = statType.ToString();
        level = Mathf.Clamp(level, 0, 10);
        for (int i = 0; i < MetaStatLevels.Count; i++)
        {
            if (MetaStatLevels[i].StatTypeName == key)
            {
                MetaStatLevels[i].Level = level;
                return;
            }
        }

        MetaStatLevels.Add(new MetaStatLevelRecord { StatTypeName = key, Level = level });
    }

    public int GetMetaItemUpgradeLevel(string unlockId)
    {
        if (string.IsNullOrEmpty(unlockId))
            return 0;
        for (int i = 0; i < MetaItemUpgrades.Count; i++)
        {
            if (MetaItemUpgrades[i].UnlockId == unlockId)
                return Mathf.Clamp(MetaItemUpgrades[i].Level, 0, 3);
        }

        return 0;
    }

    public void SetMetaItemUpgradeLevel(string unlockId, int level)
    {
        if (string.IsNullOrEmpty(unlockId))
            return;
        level = Mathf.Clamp(level, 0, 3);
        for (int i = 0; i < MetaItemUpgrades.Count; i++)
        {
            if (MetaItemUpgrades[i].UnlockId == unlockId)
            {
                MetaItemUpgrades[i].Level = level;
                return;
            }
        }

        MetaItemUpgrades.Add(new MetaItemUpgradeRecord { UnlockId = unlockId, Level = level });
    }
}
