using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public static class WeaponStatsParser
{
    private static readonly Dictionary<string, WeaponType> WeaponLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Flamethrower", WeaponType.Flamethrower },
        { "Rocket Launcher", WeaponType.RocketLauncher },
        { "Mortar", WeaponType.Mortar },
        { "Automatic Cannon", WeaponType.AutomaticCannon },
        { "Rotating blade", WeaponType.RotatingBlade }
    };

    public static void ImportAll(List<string[]> rows, IReadOnlyList<WeaponData> weaponAssets)
    {
        Dictionary<WeaponType, List<string[]>> blocks = SplitWeaponBlocks(rows);
        Dictionary<WeaponType, WeaponData> weaponMap = BuildWeaponMap(weaponAssets);

        foreach (KeyValuePair<WeaponType, List<string[]>> block in blocks)
        {
            if (!weaponMap.TryGetValue(block.Key, out WeaponData data) || data == null)
                continue;

            ApplyBlock(data, block.Value);
            SyncLegacyLevelData(data);
        }
    }

    private static Dictionary<WeaponType, WeaponData> BuildWeaponMap(IReadOnlyList<WeaponData> weaponAssets)
    {
        var map = new Dictionary<WeaponType, WeaponData>();
        if (weaponAssets == null)
            return map;

        for (int i = 0; i < weaponAssets.Count; i++)
        {
            WeaponData data = weaponAssets[i];
            if (data != null)
                map[data.WeaponType] = data;
        }

        return map;
    }

    private static Dictionary<WeaponType, List<string[]>> SplitWeaponBlocks(List<string[]> rows)
    {
        var blocks = new Dictionary<WeaponType, List<string[]>>();
        WeaponType? current = null;

        for (int i = 0; i < rows.Count; i++)
        {
            string[] row = rows[i];
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace))
                continue;

            string first = row[0].Trim();
            if (WeaponLookup.TryGetValue(first, out WeaponType weaponType))
            {
                current = weaponType;
                blocks[current.Value] = new List<string[]>();
                continue;
            }

            if (current.HasValue)
                blocks[current.Value].Add(row);
        }

        return blocks;
    }

    public static void ApplyBlock(WeaponData data, List<string[]> rows)
    {
        data.BalanceStats.Clear();
        data.UpgradeSpecificStats.Clear();

        int basicRow = FindRowIndex(rows, r => r.Length > 1 && r[1].Trim().Equals("Basic", StringComparison.OrdinalIgnoreCase));
        if (basicRow < 0)
            return;

        string pathAName = rows[basicRow].Length > 7 ? rows[basicRow][7].Trim() : string.Empty;
        string pathBName = rows[basicRow].Length > 14 ? rows[basicRow][14].Trim() : string.Empty;
        data.PathA ??= new WeaponUpgradePathData();
        data.PathB ??= new WeaponUpgradePathData();
        data.PathA.PathName = pathAName;
        data.PathB.PathName = pathBName;

        int levelHeaderRow = basicRow + 1;
        if (levelHeaderRow >= rows.Count)
            return;

        int[] basicLevels = ParseLevelColumns(rows[levelHeaderRow], 2, 6);
        int[] pathALevels = ParseLevelColumns(rows[levelHeaderRow], 8, 13);
        int[] pathBLevels = ParseLevelColumns(rows[levelHeaderRow], 15, 20);

        bool inUpgradeSpecific = false;
        for (int r = levelHeaderRow + 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            if (row.Length == 0)
                continue;

            string label = row[0].Trim();
            if (label.Equals("Upgrade-specific stats", StringComparison.OrdinalIgnoreCase))
            {
                inUpgradeSpecific = true;
                ParseUpgradeSpecificRow(data, row);
                continue;
            }

            if (inUpgradeSpecific)
            {
                ParseUpgradeSpecificRow(data, row);
                continue;
            }

            if (!label.Equals("Total", StringComparison.OrdinalIgnoreCase))
                continue;

            string statName = rows[r - 1][0].Trim();
            if (string.IsNullOrEmpty(statName))
                continue;

            ImportStatValues(data, statName, row, WeaponBalanceZone.Basic, basicLevels, 2);
            ImportStatValues(data, statName, row, WeaponBalanceZone.PathA, pathALevels, 8);
            ImportStatValues(data, statName, row, WeaponBalanceZone.PathB, pathBLevels, 15);

            WeaponStatScaling scaling = ParseScaling(row);
            ApplyScalingToLastRows(data, statName, scaling);
        }

        ApplyDerivedBaseStats(data);
    }

    private static void ImportStatValues(
        WeaponData data,
        string statId,
        string[] totalRow,
        WeaponBalanceZone zone,
        int[] levels,
        int startColumn)
    {
        for (int i = 0; i < levels.Length; i++)
        {
            int col = startColumn + i;
            if (col >= totalRow.Length || !TryParseFloat(totalRow[col], out float value))
                continue;

            data.BalanceStats.Add(new WeaponBalanceStatRow
            {
                StatId = statId,
                Zone = zone,
                Level = levels[i],
                Value = value
            });
        }
    }

    private static void ParseUpgradeSpecificRow(WeaponData data, string[] row)
    {
        string statName = row.Length > 7 ? row[7].Trim() : string.Empty;
        if (string.IsNullOrEmpty(statName) || statName.Equals("Upgrade-specific stats", StringComparison.OrdinalIgnoreCase))
            statName = row.Length > 14 ? row[14].Trim() : string.Empty;

        if (string.IsNullOrEmpty(statName) || statName.Equals("Upgrade-specific stats", StringComparison.OrdinalIgnoreCase))
            return;

        if (row.Length > 8 && row[8].Trim().Equals("Total", StringComparison.OrdinalIgnoreCase))
            ImportUpgradeSpecific(data, statName, row, WeaponUpgradePath.PathA, 9);

        if (row.Length > 15 && row[15].Trim().Equals("Total", StringComparison.OrdinalIgnoreCase))
            ImportUpgradeSpecific(data, statName, row, WeaponUpgradePath.PathB, 16);
    }

    private static void ImportUpgradeSpecific(WeaponData data, string statId, string[] row, WeaponUpgradePath path, int valueStart)
    {
        int[] levels = { 1, 7, 8, 9, 10 };
        for (int i = 0; i < levels.Length; i++)
        {
            int col = valueStart + i;
            if (col >= row.Length || !TryParseFloat(row[col], out float value))
                continue;

            data.UpgradeSpecificStats.Add(new WeaponUpgradeSpecificStatRow
            {
                StatId = statId,
                Path = path,
                Level = levels[i],
                Value = value
            });
        }
    }

    private static void ApplyDerivedBaseStats(WeaponData data)
    {
        data.BaseDamage = data.TryGetBalanceStat("Damage", 1, WeaponUpgradePath.None, data.BaseDamage);
        data.BaseManualAmmo = data.TryGetBalanceStat("Manual ammo", 1, WeaponUpgradePath.None, data.BaseManualAmmo);
        data.BaseRange = data.TryGetBalanceStat("Auto mode range (m)", 1, WeaponUpgradePath.None, data.BaseRange);
        data.BaseKnockback = data.TryGetBalanceStat("Knockback strength", 1, WeaponUpgradePath.None, data.BaseKnockback);
        data.ActiveAbilityAmmoCost = data.TryGetBalanceStat("Ability damage", 1, WeaponUpgradePath.None, data.ActiveAbilityAmmoCost);
        data.SkillCooldown = data.TryGetBalanceStat("Ability cooldown (s)", 1, WeaponUpgradePath.None, data.SkillCooldown);
        data.BaseAttackRate = data.TryGetBalanceStat("Attack speed", 1, WeaponUpgradePath.None,
            data.TryGetBalanceStat("Auto attack speed", 1, WeaponUpgradePath.None, data.BaseAttackRate));
    }

    public static void SyncLegacyLevelData(WeaponData data)
    {
        data.LevelData.Clear();
        for (int level = 1; level <= 10; level++)
        {
            float baseDamage = data.TryGetBalanceStat("Damage", 1, WeaponUpgradePath.None, data.BaseDamage);
            float levelDamage = data.TryGetBalanceStat("Damage", level, WeaponUpgradePath.None, baseDamage);
            float baseAmmo = data.TryGetBalanceStat("Manual ammo", 1, WeaponUpgradePath.None, data.BaseManualAmmo);
            float levelAmmo = data.TryGetBalanceStat("Manual ammo", level, WeaponUpgradePath.None, baseAmmo);
            float baseRate = data.TryGetBalanceStat("Attack speed", 1, WeaponUpgradePath.None,
                data.TryGetBalanceStat("Auto attack speed", 1, WeaponUpgradePath.None, data.BaseAttackRate));
            float levelRate = data.TryGetBalanceStat("Attack speed", level, WeaponUpgradePath.None,
                data.TryGetBalanceStat("Auto attack speed", level, WeaponUpgradePath.None, baseRate));

            data.LevelData.Add(new WeaponLevelData
            {
                Level = level,
                DamageMultiplier = baseDamage > 0f ? levelDamage / baseDamage : 1f,
                ManualAmmoMultiplier = baseAmmo > 0f ? levelAmmo / baseAmmo : 1f,
                AttackRateMultiplier = baseRate > 0f ? levelRate / baseRate : 1f
            });
        }
    }

    private static int[] ParseLevelColumns(string[] row, int start, int endExclusive)
    {
        var levels = new List<int>();
        for (int i = start; i < endExclusive && i < row.Length; i++)
        {
            string cell = row[i]?.Trim();
            if (cell.Equals("Base", StringComparison.OrdinalIgnoreCase))
            {
                levels.Add(1);
                continue;
            }

            if (float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                levels.Add(Mathf.RoundToInt(value));
        }

        return levels.ToArray();
    }

    private static WeaponStatScaling ParseScaling(string[] totalRow)
    {
        string raw = totalRow.Length > 21 ? totalRow[21] : string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        float effectiveness = 1f;
        int paren = raw.IndexOf('(');
        if (paren >= 0)
        {
            int close = raw.IndexOf(')', paren);
            if (close > paren)
            {
                string percentText = raw.Substring(paren + 1, close - paren - 1).Replace("%", string.Empty);
                if (float.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
                    effectiveness = percent / 100f;
            }

            raw = raw.Substring(0, paren).Trim();
        }

        StatType? statType = MapScalingStat(raw);
        if (statType == null)
            return null;

        return new WeaponStatScaling { StatType = statType.Value, Effectiveness = effectiveness };
    }

    private static StatType? MapScalingStat(string raw)
    {
        string lower = raw.ToLowerInvariant();
        if (lower.Contains("damage multiplier")) return StatType.DamageMultiplier;
        if (lower.Contains("elite damage")) return StatType.EliteDamageMultiplier;
        if (lower.Contains("ability damage")) return StatType.DamageMultiplier;
        if (lower.Contains("projectile area")) return StatType.ProjectileAreaSize;
        if (lower.Contains("ammo multiplier")) return StatType.AmmoMultiplier;
        if (lower.Contains("attack speed")) return StatType.AttackSpeedMultiplier;
        if (lower.Contains("ability cooldown")) return StatType.BaseFireInterval;
        if (lower.Contains("critical chance")) return StatType.CriticalChance;
        if (lower.Contains("critical damage")) return StatType.CriticalDamage;
        if (lower.Contains("knockback")) return StatType.Knockback;
        return null;
    }

    private static void ApplyScalingToLastRows(WeaponData data, string statId, WeaponStatScaling scaling)
    {
        if (scaling == null)
            return;

        for (int i = data.BalanceStats.Count - 1; i >= 0; i--)
        {
            WeaponBalanceStatRow row = data.BalanceStats[i];
            if (row.StatId != statId)
                break;
            row.Scaling = scaling;
        }
    }

    private static int FindRowIndex(List<string[]> rows, Func<string[], bool> predicate)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (predicate(rows[i]))
                return i;
        }

        return -1;
    }

    private static bool TryParseFloat(string raw, out float value)
    {
        return float.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
