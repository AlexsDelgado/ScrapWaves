using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemySpawnWeightSnapshot
{
    public EnemySpawnKind Kind { get; }
    public int EffectiveWeight { get; }
    public int CumulativeMin { get; }
    public int CumulativeMax { get; }
    public float Percent { get; }

    public EnemySpawnWeightSnapshot(
        EnemySpawnKind kind,
        int effectiveWeight,
        int cumulativeMin,
        int cumulativeMax,
        float percent)
    {
        Kind = kind;
        EffectiveWeight = effectiveWeight;
        CumulativeMin = cumulativeMin;
        CumulativeMax = cumulativeMax;
        Percent = percent;
    }
}

public readonly struct EnemySpawnRollResult
{
    public EnemySpawnKind SelectedKind { get; }
    public int BatchSize { get; }
    public GameObject Prefab { get; }
    public int TotalWeight { get; }
    public int RollIndex { get; }
    public int VariantWeightBonus { get; }
    public IReadOnlyList<EnemySpawnWeightSnapshot> Snapshots { get; }

    public EnemySpawnRollResult(
        EnemySpawnKind selectedKind,
        int batchSize,
        GameObject prefab,
        int totalWeight,
        int rollIndex,
        int variantWeightBonus,
        IReadOnlyList<EnemySpawnWeightSnapshot> snapshots)
    {
        SelectedKind = selectedKind;
        BatchSize = batchSize;
        Prefab = prefab;
        TotalWeight = totalWeight;
        RollIndex = rollIndex;
        VariantWeightBonus = variantWeightBonus;
        Snapshots = snapshots;
    }
}

public class EnemySpawnRoulette
{
    private struct EffectiveEntry
    {
        public EnemySpawnRouletteConfig.Entry Entry;
        public int Weight;
    }

    private readonly EnemySpawnRouletteConfig _config;
    private readonly List<EnemySpawnWeightSnapshot> _snapshotBuffer = new(8);
    private readonly List<EffectiveEntry> _effectiveEntries = new(8);

    public EnemySpawnRoulette(EnemySpawnRouletteConfig config)
    {
        _config = config;
    }

    public int GetVariantWeightBonus(float runTimeSeconds)
    {
        if (_config == null) return 0;

        float interval = Mathf.Max(1f, _config.VariantWeightBonusEverySeconds);
        int steps = Mathf.FloorToInt(runTimeSeconds / interval);
        return steps * Mathf.Max(0, _config.VariantWeightBonusPerStep);
    }

    public Dictionary<EnemySpawnKind, int> GetEffectiveWeights(float runTimeSeconds)
    {
        return GetEffectiveWeights(runTimeSeconds, null);
    }

    public Dictionary<EnemySpawnKind, int> GetEffectiveWeights(float runTimeSeconds, PlayerStats stats)
    {
        var weights = new Dictionary<EnemySpawnKind, int>();
        BuildEffectiveEntries(runTimeSeconds, stats);

        for (int i = 0; i < _effectiveEntries.Count; i++)
            weights[_effectiveEntries[i].Entry.Kind] = _effectiveEntries[i].Weight;

        return weights;
    }

    public EnemySpawnRollResult Roll(float runTimeSeconds)
    {
        return Roll(runTimeSeconds, null);
    }

    public EnemySpawnRollResult Roll(float runTimeSeconds, PlayerStats stats)
    {
        _snapshotBuffer.Clear();
        BuildEffectiveEntries(runTimeSeconds, stats);
        if (_effectiveEntries.Count == 0)
        {
            return new EnemySpawnRollResult(
                default,
                0,
                null,
                0,
                0,
                0,
                _snapshotBuffer);
        }

        int variantBonus = GetVariantWeightBonus(runTimeSeconds);
        int totalWeight = 0;

        for (int i = 0; i < _effectiveEntries.Count; i++)
        {
            EffectiveEntry effectiveEntry = _effectiveEntries[i];

            int cumulativeMin = totalWeight;
            totalWeight += effectiveEntry.Weight;
            int cumulativeMax = totalWeight - 1;
            float percent = 0f;
            _snapshotBuffer.Add(new EnemySpawnWeightSnapshot(
                effectiveEntry.Entry.Kind,
                effectiveEntry.Weight,
                cumulativeMin,
                cumulativeMax,
                percent));
        }

        if (totalWeight <= 0)
        {
            return new EnemySpawnRollResult(
                default,
                0,
                null,
                0,
                0,
                variantBonus,
                _snapshotBuffer);
        }

        for (int i = 0; i < _snapshotBuffer.Count; i++)
        {
            EnemySpawnWeightSnapshot snap = _snapshotBuffer[i];
            _snapshotBuffer[i] = new EnemySpawnWeightSnapshot(
                snap.Kind,
                snap.EffectiveWeight,
                snap.CumulativeMin,
                snap.CumulativeMax,
                snap.EffectiveWeight / (float)totalWeight * 100f);
        }

        int rollIndex = UnityEngine.Random.Range(0, totalWeight);
        EnemySpawnKind selected = _snapshotBuffer[_snapshotBuffer.Count - 1].Kind;
        EnemySpawnRouletteConfig.Entry selectedEntry = null;

        foreach (EnemySpawnWeightSnapshot snap in _snapshotBuffer)
        {
            if (rollIndex >= snap.CumulativeMin && rollIndex <= snap.CumulativeMax)
            {
                selected = snap.Kind;
                break;
            }
        }

        selectedEntry = _config.GetEntry(selected);
        int batchSize = selectedEntry != null ? Mathf.Max(1, selectedEntry.BatchSize) : 1;
        GameObject prefab = selectedEntry?.Prefab;

        return new EnemySpawnRollResult(
            selected,
            batchSize,
            prefab,
            totalWeight,
            rollIndex,
            variantBonus,
            new List<EnemySpawnWeightSnapshot>(_snapshotBuffer));
    }

    private void BuildEffectiveEntries(float runTimeSeconds, PlayerStats stats)
    {
        _effectiveEntries.Clear();
        if (_config?.Entries == null)
            return;

        int variantBonus = GetVariantWeightBonus(runTimeSeconds);
        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null || entry.BaseWeight <= 0)
                continue;

            int weight = entry.BaseWeight;
            if (entry.IsVariant)
                weight += variantBonus;

            _effectiveEntries.Add(new EffectiveEntry
            {
                Entry = entry,
                Weight = Mathf.Max(1, weight)
            });
        }

        ApplyExtraEliteChance(stats);
    }

    private void ApplyExtraEliteChance(PlayerStats stats)
    {
        float extraEliteChance = PlayerStatMath.GetExtraEliteChance(stats);
        if (extraEliteChance <= 0f)
            return;

        int normalWeight = 0;
        int variantWeight = 0;
        for (int i = 0; i < _effectiveEntries.Count; i++)
        {
            if (_effectiveEntries[i].Entry.IsVariant)
                variantWeight += _effectiveEntries[i].Weight;
            else
                normalWeight += _effectiveEntries[i].Weight;
        }

        if (normalWeight <= 0 || variantWeight <= 0)
            return;

        float baseVariantChance = variantWeight / (float)(normalWeight + variantWeight);
        if (baseVariantChance >= 0.95f)
            return;

        float targetVariantChance = Mathf.Clamp(baseVariantChance + extraEliteChance, baseVariantChance, 0.95f);
        int targetVariantWeight = Mathf.RoundToInt(targetVariantChance * normalWeight / (1f - targetVariantChance));
        int bonusToDistribute = Mathf.Max(0, targetVariantWeight - variantWeight);
        if (bonusToDistribute <= 0)
            return;

        int variantEntriesRemaining = 0;
        for (int i = 0; i < _effectiveEntries.Count; i++)
        {
            if (_effectiveEntries[i].Entry.IsVariant)
                variantEntriesRemaining++;
        }

        int remainingBonus = bonusToDistribute;
        for (int i = 0; i < _effectiveEntries.Count; i++)
        {
            EffectiveEntry effective = _effectiveEntries[i];
            if (!effective.Entry.IsVariant)
                continue;

            variantEntriesRemaining--;
            int share = variantEntriesRemaining == 0
                ? remainingBonus
                : Mathf.RoundToInt(bonusToDistribute * (effective.Weight / (float)variantWeight));

            share = Mathf.Clamp(share, 0, remainingBonus);
            effective.Weight += share;
            _effectiveEntries[i] = effective;

            remainingBonus -= share;
        }
    }
}
