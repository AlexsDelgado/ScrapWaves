using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

public class MaterialSourceTooltipTests
{
    private GameObject _root;

    [TearDown]
    public void TearDown()
    {
        if (_root != null) Object.DestroyImmediate(_root);
    }

    [Test]
    public void CraftingPrefab_EveryMaterialRowHasHoverTargetWithDropSources()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RunMenuPrefabBuilder.CraftingPrefabPath);
        var view = prefab.GetComponent<CraftingMenuView>();
        var tooltip = prefab.GetComponentInChildren<MaterialSourceTooltip>(true);
        Assert.NotNull(tooltip, "Run 'ScrapWaves/UI/Author Crafting Material Tooltip'.");
        foreach (MaterialType type in System.Enum.GetValues(typeof(MaterialType)))
        {
            Assert.IsTrue(tooltip.TryGetText(type, out string text), type.ToString());
            StringAssert.Contains("Dropped by:", text);
            StringAssert.DoesNotContain("unknown", text);
        }
        foreach (CraftingMaterialReadout.Entry entry in view.BalanceReadout.Entries)
            Assert.NotNull(entry.Root.GetComponent<MaterialHoverTarget>(), entry.Type.ToString());
    }

    [Test]
    public void Hover_ShowsPanelWithMaterialTextAndExitHidesIt()
    {
        _root = new GameObject("Tooltip", typeof(RectTransform), typeof(MaterialSourceTooltip));
        var tooltip = _root.GetComponent<MaterialSourceTooltip>();
        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.transform.SetParent(_root.transform, false);
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(panelGo.transform, false);
        tooltip.Panel = (RectTransform)panelGo.transform;
        tooltip.Label = labelGo.GetComponent<TextMeshProUGUI>();
        tooltip.Sources = new[] { new MaterialSourceTooltip.Source { Type = MaterialType.Gears, Text = "Gears\nDropped by: X" } };
        panelGo.SetActive(false);

        var row = new GameObject("Row", typeof(RectTransform), typeof(MaterialHoverTarget));
        row.transform.SetParent(_root.transform, false);
        var hover = row.GetComponent<MaterialHoverTarget>();
        hover.Type = MaterialType.Gears;
        hover.Tooltip = tooltip;

        hover.OnPointerEnter(null);
        Assert.IsTrue(panelGo.activeSelf);
        StringAssert.Contains("Dropped by: X", tooltip.Label.text);

        hover.OnPointerExit(null);
        Assert.IsFalse(panelGo.activeSelf);

        hover.Type = MaterialType.Wiring; // no text authored: stays hidden
        hover.OnPointerEnter(null);
        Assert.IsFalse(panelGo.activeSelf);
    }
}
