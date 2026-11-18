#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the "dropped by" hover tooltip to the crafting menu prefab and (re)generates its texts
/// from the MaterialDropConfig assets. Rerun after changing drop tables; layout is preserved.
/// </summary>
public static class MaterialSourceTooltipAuthoring
{
    [MenuItem("ScrapWaves/UI/Author Crafting Material Tooltip")]
    public static void Author()
    {
        string path = RunMenuPrefabBuilder.CraftingPrefabPath;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = root.GetComponent<CraftingMenuView>();
            if (view == null)
                throw new InvalidOperationException("CraftingMenuView missing on the crafting prefab.");

            MaterialSourceTooltip tooltip = root.GetComponentInChildren<MaterialSourceTooltip>(true);
            if (tooltip == null)
                tooltip = CreateTooltip(root.transform, view.Materials[0].AmountText.font);

            tooltip.Sources = BuildSources();

            foreach (CraftingMaterialReadout readout in new[]
                     { view.BalanceReadout, view.UpgradeReadout, view.TinkerReadout, view.AdvancedReadout })
            {
                if (readout == null || readout.Entries == null) continue;
                foreach (CraftingMaterialReadout.Entry entry in readout.Entries)
                {
                    if (entry?.Root == null) continue;
                    EnsureHitArea(entry.Root);
                    var hover = entry.Root.GetComponent<MaterialHoverTarget>();
                    if (hover == null) hover = entry.Root.AddComponent<MaterialHoverTarget>();
                    hover.Type = entry.Type;
                    hover.Tooltip = tooltip;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("MATERIAL_TOOLTIP_AUTHORED: " + string.Join(" | ", Array.ConvertAll(tooltip.Sources, s => s.Type + "=" + s.Text.Replace("\n", " / "))));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static MaterialSourceTooltip CreateTooltip(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("MaterialSourceTooltip", typeof(RectTransform), typeof(MaterialSourceTooltip));
        go.transform.SetParent(parent, false);
        var holder = (RectTransform)go.transform;
        holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
        holder.sizeDelta = Vector2.zero;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(holder, false);
        var panel = (RectTransform)panelGo.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(360f, 96f);
        var bg = panelGo.GetComponent<Image>();
        bg.color = new Color(0.035f, 0.043f, 0.039f, 0.96f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(panel, false);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 22f;
        text.color = new Color(0.949f, 0.961f, 0.922f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);

        panelGo.SetActive(false);
        var tooltip = go.GetComponent<MaterialSourceTooltip>();
        tooltip.Panel = panel;
        tooltip.Label = text;
        return tooltip;
    }

    // A transparent graphic so the row receives pointer events (icons/labels do not raycast).
    private static void EnsureHitArea(GameObject row)
    {
        var image = row.GetComponent<Image>();
        if (image == null) image = row.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
    }

    private static MaterialSourceTooltip.Source[] BuildSources()
    {
        var droppers = new Dictionary<MaterialType, List<(string enemy, float weight)>>();
        foreach (string guid in AssetDatabase.FindAssets("t:MaterialDropConfig"))
        {
            var config = AssetDatabase.LoadAssetAtPath<MaterialDropConfig>(AssetDatabase.GUIDToAssetPath(guid));
            if (config == null) continue;
            string enemy = Humanize(config.name);
            foreach (MaterialDropEntry entry in config.Pool)
            {
                if (entry == null || entry.WeightPercent <= 0f) continue;
                if (!droppers.TryGetValue(entry.Material, out var list))
                    droppers[entry.Material] = list = new List<(string, float)>();
                list.Add((enemy, entry.WeightPercent));
            }
        }

        var sources = new List<MaterialSourceTooltip.Source>();
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            var sb = new StringBuilder();
            sb.Append(MaterialCatalog.GetDisplayName(type));
            if (MaterialCatalog.GetCategory(type) == MaterialCategory.Rare) sb.Append(" (rare)");
            sb.Append("\nDropped by: ");
            if (droppers.TryGetValue(type, out var list) && list.Count > 0)
            {
                list.Sort((a, b) => b.weight.CompareTo(a.weight));
                sb.Append(string.Join(", ", list.ConvertAll(x => x.enemy)));
            }
            else
            {
                sb.Append("unknown");
            }
            sources.Add(new MaterialSourceTooltip.Source { Type = type, Text = sb.ToString() });
        }
        return sources.ToArray();
    }

    // "JunkSlimeDrops" -> "Junk Slime".
    private static string Humanize(string assetName)
    {
        string trimmed = assetName.EndsWith("Drops", StringComparison.Ordinal) ? assetName[..^5] : assetName;
        return Regex.Replace(trimmed, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
#endif
