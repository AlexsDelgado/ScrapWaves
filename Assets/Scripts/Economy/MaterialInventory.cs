using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MaterialInventory : MonoBehaviour
{
    public static MaterialInventory Instance { get; private set; }

    private readonly Dictionary<MaterialType, int> _amounts = new();

    public event Action<MaterialType, int> OnMaterialChanged;
    public event Action OnInventoryChanged;

    private void OnEnable() => Instance = this;
    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    public int GetAmount(MaterialType type) => _amounts.TryGetValue(type, out int amount) ? amount : 0;

    public void Add(MaterialType type, int amount)
    {
        if (amount <= 0)
            return;

        _amounts[type] = GetAmount(type) + amount;
        OnMaterialChanged?.Invoke(type, _amounts[type]);
        OnInventoryChanged?.Invoke();
    }

    public bool CanAfford(IReadOnlyList<MaterialCost> costs)
    {
        if (costs == null)
            return true;

        for (int i = 0; i < costs.Count; i++)
        {
            MaterialCost cost = costs[i];
            if (GetAmount(cost.Material) < cost.Amount)
                return false;
        }

        return true;
    }

    public bool TrySpend(IReadOnlyList<MaterialCost> costs)
    {
        if (!CanAfford(costs))
            return false;

        for (int i = 0; i < costs.Count; i++)
        {
            MaterialCost cost = costs[i];
            _amounts[cost.Material] = GetAmount(cost.Material) - cost.Amount;
            OnMaterialChanged?.Invoke(cost.Material, _amounts[cost.Material]);
        }

        OnInventoryChanged?.Invoke();
        return true;
    }
}
