#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class EconomyBalanceTests
{
    private const string MaterialCsv = "Assets/Data/Balance/balance_material_usage.csv";
    private MaterialUsageBalanceSO _balance;
    private string _temporaryAssetPath;

    [SetUp]
    public void SetUp()
    {
        Assert.That(File.Exists(MaterialCsv), Is.True, "Material CSV missing");
        MaterialUsageParser.Parse(CsvReader.ReadAllRows(MaterialCsv), out var assignments, out var totals);
        _balance = ScriptableObject.CreateInstance<MaterialUsageBalanceSO>();
        _balance.SetData(assignments, totals);
    }

    [TearDown]
    public void TearDown()
    {
        if (_balance != null) Object.DestroyImmediate(_balance);
        if (!string.IsNullOrEmpty(_temporaryAssetPath)) AssetDatabase.DeleteAsset(_temporaryAssetPath);
    }

    [Test]
    public void MaterialUsageCsv_ParsesFlamethrowerPrincipalCostAtLevel4()
    {
        Assert.That(_balance.GetRole(WeaponMaterialColumn.Flamethrower, MaterialType.Gears), Is.EqualTo(MaterialRole.Principal));
        Assert.That(_balance.GetTotalForRole(MaterialRole.Principal, 4), Is.EqualTo(50));
    }

    [Test]
    public void MaterialUsageCsv_ParsesPrincipalExtraForBladesA()
    {
        Assert.That(_balance.GetRole(WeaponMaterialColumn.BladesA, MaterialType.SheetMetal), Is.EqualTo(MaterialRole.PrincipalExtra));
        Assert.That(_balance.GetTotalForRole(MaterialRole.PrincipalExtra, 10), Is.EqualTo(275));
    }

    [Test]
    public void CraftingCost_FlamethrowerLevel4_UsesPrincipalTotal()
    {
        var costs = WeaponCraftingCostCalculator.GetUpgradeCost(_balance, WeaponType.Flamethrower, WeaponUpgradePath.None, 4);
        Assert.That(costs.Any(cost => cost.Material == MaterialType.Gears && cost.Amount == 50), Is.True);
    }

    [Test]
    public void AdvancedTinkeringCost_IncreasesAfterReject()
    {
        var normal = WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(1, false);
        var rejected = WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(1, true);
        Assert.That(rejected[0].Amount, Is.EqualTo(8));
        Assert.That(normal[0].Amount, Is.EqualTo(5));
    }

    [Test]
    public void MaterialUsageImport_SavesOnlyItsDestinationAndPreservesWeapons()
    {
        // Never import into the checked-in balance, weapon or drop assets from a test.
        var weapons = AssetDatabase.FindAssets("t:WeaponData").Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<WeaponData>).ToArray();
        var json = weapons.Select(weapon => EditorJsonUtility.ToJson(weapon)).ToArray();
        var dirty = weapons.Select(EditorUtility.IsDirty).ToArray();
        _temporaryAssetPath = "Assets/__MaterialUsageImportTest_" + Guid.NewGuid().ToString("N") + ".asset";
        var imported = MaterialUsageCsvImporter.Import(MaterialCsv, _temporaryAssetPath);
        AssetDatabase.SaveAssetIfDirty(imported);
        AssetDatabase.ImportAsset(_temporaryAssetPath, ImportAssetOptions.ForceUpdate);
        var reloaded = AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(_temporaryAssetPath);

        Assert.That(reloaded.RoleAssignments.Count, Is.EqualTo(_balance.RoleAssignments.Count));
        Assert.That(reloaded.GetTotalForRole(MaterialRole.Principal, 4), Is.EqualTo(50));
        for (int i = 0; i < weapons.Length; i++)
        {
            Assert.That(EditorJsonUtility.ToJson(weapons[i]), Is.EqualTo(json[i]), weapons[i].name);
            Assert.That(EditorUtility.IsDirty(weapons[i]), Is.EqualTo(dirty[i]), weapons[i].name + " dirty state");
        }
    }

    [Test]
    public void ImportMenu_OnlyOffersMaterialCsvImport()
    {
        var menu = typeof(BalanceImportMenu).GetMethod(nameof(BalanceImportMenu.ImportMaterials));
        var entries = menu.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().ToArray();
        Assert.That(entries.Select(entry => entry.menuItem), Does.Contain("ScrapWaves/Balance/Import Material Usage CSV"));
        Assert.That(typeof(BalanceImportMenu).GetMethod("ImportAll"), Is.Null, "The obsolete weapon import must not remain callable.");
    }
}
#endif
