using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>All rows and sprites are authored in the editor; runtime only binds data.</summary>
public sealed class CraftingMaterialReadout : MonoBehaviour
{
    [Serializable] public sealed class Entry
    {
        public MaterialType Type;
        public GameObject Root;
        public Image Icon;
        public TMP_Text Quantity;
    }
    public Entry[] Entries;
    public TMP_Text Notice;
    public Color SufficientColor = new(0.35f, 0.9f, 0.48f, 1f);
    public Color InsufficientColor = new(1f, 0.42f, 0.35f, 1f);
    public Color BalanceColor = Color.white;

    public void Bind(MaterialInventory inventory, IReadOnlyList<MaterialCost> costs, string notice = null)
    {
        bool balance = costs == null;
        foreach (Entry entry in Entries)
        {
            int required = 0;
            if (costs != null) foreach (MaterialCost cost in costs)
                if (cost.Material == entry.Type) required += cost.Amount;
            bool visible = balance || (string.IsNullOrEmpty(notice) && required > 0);
            entry.Root.SetActive(visible);
            if (!visible) continue;
            int owned = inventory.GetAmount(entry.Type);
            entry.Quantity.text = balance ? owned.ToString() : $"{owned}/{required}";
            entry.Quantity.color = balance ? BalanceColor : owned >= required ? SufficientColor : InsufficientColor;
        }
        Notice.text = notice ?? (!balance && costs.Count == 0 ? "Free" : string.Empty);
        Notice.gameObject.SetActive(!string.IsNullOrEmpty(Notice.text));
    }
}
