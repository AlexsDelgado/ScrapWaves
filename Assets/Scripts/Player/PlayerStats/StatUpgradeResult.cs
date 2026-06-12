using System;

[Serializable]
public readonly struct StatUpgradeResult
{
    public StatUpgradeResult(StatType statType, float amount)
    {
        StatType = statType;
        Amount = amount;
    }

    public StatType StatType { get; }
    public float Amount { get; }
}
