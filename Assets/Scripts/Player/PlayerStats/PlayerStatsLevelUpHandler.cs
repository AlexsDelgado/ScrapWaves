using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerStatsLevelUpHandler : MonoBehaviour
{
    [SerializeField] private PlayerStats _playerStats;
    [SerializeField] private int _levelCap = 36;

    private readonly Dictionary<StatType, int> _rouletteWeights = new();

    private void Awake()
    {
        if (_playerStats == null)
            _playerStats = GetComponent<PlayerStats>();
        InitializeRouletteWeights();
    }

    private void InitializeRouletteWeights()
    {
        _rouletteWeights.Clear();
        if (_playerStats == null)
            return;

        foreach (StatDefinition definition in _playerStats.GetAllDefinitions())
        {
            if (definition != null && definition.UpgradeableByLevel)
                _rouletteWeights[definition.StatType] = 5;
        }
    }

    public List<StatUpgradeResult> ApplyLevelUpStats(int newLevel)
    {
        var results = new List<StatUpgradeResult>();
        if (_rouletteWeights.Count == 0)
            return results;

        int upgradeCount = GetUpgradeCountForLevel(newLevel);
        HashSet<StatType> selectedThisLevel = new();

        for (int i = 0; i < upgradeCount; i++)
        {
            StatType selectedStat = RollStat();
            selectedThisLevel.Add(selectedStat);
            float amount = ApplyUpgradeToStat(selectedStat, newLevel);
            if (amount > 0f)
                results.Add(new StatUpgradeResult(selectedStat, amount));
            _rouletteWeights[selectedStat] = Mathf.Max(1, _rouletteWeights[selectedStat] - 1);
        }

        IncreaseWeightsForUnselectedStats(selectedThisLevel);
        return results;
    }

    private StatType RollStat()
    {
        int totalWeight = 0;
        foreach (int weight in _rouletteWeights.Values)
            totalWeight += weight;

        int roll = Random.Range(0, totalWeight);
        int current = 0;

        foreach (KeyValuePair<StatType, int> pair in _rouletteWeights)
        {
            current += pair.Value;
            if (roll < current)
                return pair.Key;
        }

        foreach (StatType type in _rouletteWeights.Keys)
            return type;

        return StatType.MaxHealth;
    }

    private float ApplyUpgradeToStat(StatType statType, int newLevel)
    {
        StatDefinition definition = _playerStats.GetDefinition(statType);
        if (definition == null || definition.LevelUpgradeBaseAmount <= 0f)
            return 0f;

        float amount = StatMath.CalculateStatUpgradeAmount(definition.LevelUpgradeBaseAmount, newLevel, _levelCap);
        _playerStats.AddModifier(new StatModifier(statType, amount, StatUpgradeSource.LevelUp));

        if (statType == StatType.MaxHealth && TryGetComponent(out PlayerHealth health))
            health.ApplyMaxHealthIncrease(Mathf.RoundToInt(amount));

        return amount;
    }

    private void IncreaseWeightsForUnselectedStats(HashSet<StatType> selectedThisLevel)
    {
        List<StatType> allStats = new(_rouletteWeights.Keys);
        foreach (StatType statType in allStats)
        {
            if (!selectedThisLevel.Contains(statType))
                _rouletteWeights[statType] += 1;
        }
    }

    private static int GetUpgradeCountForLevel(int level)
    {
        if (level >= 36) return 20;
        if (level >= 30) return 11;
        if (level >= 26) return 10;
        if (level >= 21) return 9;
        if (level >= 16) return 8;
        if (level >= 11) return 7;
        if (level >= 6) return 6;
        return 5;
    }
}

public static class StatMath
{
    public static float CalculateLevelScale(int currentLevel, int levelCap) =>
        1f + (currentLevel / (levelCap / 2f));

    public static float CalculateStatUpgradeAmount(float baseUpgradeAmount, int currentLevel, int levelCap)
    {
        float levelScale = CalculateLevelScale(currentLevel, levelCap);
        float randomFactor = Random.Range(0.9f, 1.1f);
        return baseUpgradeAmount * levelScale * randomFactor;
    }
}
