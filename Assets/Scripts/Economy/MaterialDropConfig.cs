using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MaterialDropEntry
{
    public MaterialType Material;
    [Range(0f, 100f)] public float WeightPercent;
}

[CreateAssetMenu(fileName = "MaterialDropConfig", menuName = "ScrapWaves/Economy/Material Drop Config")]
public class MaterialDropConfig : ScriptableObject
{
    [SerializeField, Range(0f, 1f)] private float _dropChance = 0.5f;
    [SerializeField] private List<MaterialDropEntry> _pool = new();

    public float DropChance => _dropChance;
    public IReadOnlyList<MaterialDropEntry> Pool => _pool;

    public bool TryRoll(out MaterialType material, System.Random rng = null)
    {
        material = default;
        if (_pool == null || _pool.Count == 0)
            return false;

        rng ??= new System.Random();
        float roll = (float)rng.NextDouble() * 100f;
        float cumulative = 0f;
        for (int i = 0; i < _pool.Count; i++)
        {
            MaterialDropEntry entry = _pool[i];
            if (entry == null || entry.WeightPercent <= 0f)
                continue;

            cumulative += entry.WeightPercent;
            if (roll <= cumulative)
            {
                material = entry.Material;
                return true;
            }
        }

        material = _pool[_pool.Count - 1].Material;
        return true;
    }
}
