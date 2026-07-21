#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class EconomyBalanceTests
{
    private const string MaterialCsv = "Assets/Data/Balance/balance_material_usage.csv";
    private const string WeaponStatsCsv = "Assets/Data/Balance/balance_weapon_stats.csv";

    [Test]
    public void MaterialUsageCsv_ParsesFlamethrowerPrincipalCostAtLevel4()
    {
        Assume.That(File.Exists(MaterialCsv), "Material CSV missing");
        MaterialUsageBalanceSO balance = MaterialUsageCsvImporter.Import(MaterialCsv, "Assets/ScriptableObjects/Economy/MaterialUsageBalance_Test.asset");

        MaterialRole role = balance.GetRole(WeaponMaterialColumn.Flamethrower, MaterialType.Gears);
        Assert.That(role, Is.EqualTo(MaterialRole.Principal));
        Assert.That(balance.GetTotalForRole(MaterialRole.Principal, 4), Is.EqualTo(50));
    }

    [Test]
    public void MaterialUsageCsv_ParsesPrincipalExtraForBladesA()
    {
        Assume.That(File.Exists(MaterialCsv), "Material CSV missing");
        MaterialUsageBalanceSO balance = MaterialUsageCsvImporter.Import(MaterialCsv, "Assets/ScriptableObjects/Economy/MaterialUsageBalance_Test.asset");

        MaterialRole role = balance.GetRole(WeaponMaterialColumn.BladesA, MaterialType.SheetMetal);
        Assert.That(role, Is.EqualTo(MaterialRole.PrincipalExtra));
        Assert.That(balance.GetTotalForRole(MaterialRole.PrincipalExtra, 10), Is.EqualTo(275));
    }

    [Test]
    public void CraftingCost_FlamethrowerLevel4_UsesPrincipalTotal()
    {
        Assume.That(File.Exists(MaterialCsv), "Material CSV missing");
        MaterialUsageBalanceSO balance = MaterialUsageCsvImporter.Import(MaterialCsv, "Assets/ScriptableObjects/Economy/MaterialUsageBalance_Test.asset");
        var costs = WeaponCraftingCostCalculator.GetUpgradeCost(balance, WeaponType.Flamethrower, WeaponUpgradePath.None, 4);

        bool hasGears50 = false;
        for (int i = 0; i < costs.Count; i++)
        {
            if (costs[i].Material == MaterialType.Gears && costs[i].Amount == 50)
                hasGears50 = true;
        }

        Assert.That(hasGears50, Is.True);
    }

    [Test]
    public void WeaponStatsCsv_ImportsFlamethrowerDamageBase()
    {
        Assume.That(File.Exists(WeaponStatsCsv), "Weapon stats CSV missing");
        WeaponStatsCsvImporter.ImportAll(WeaponStatsCsv, "Assets/ScriptableObjects/WeaponSO");
        WeaponData flamethrower = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        Assume.That(flamethrower, Is.Not.Null);

        Assert.That(flamethrower.TryGetBalanceStat("Damage", 1, WeaponUpgradePath.None), Is.EqualTo(25f).Within(0.01f));
        Assert.That(flamethrower.PathA.PathName, Is.EqualTo("Jellified fuel"));
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
    public void ImportAll_SavesBalanceAssets()
    {
        Assume.That(File.Exists(MaterialCsv), "Material CSV missing");
        Assume.That(File.Exists(WeaponStatsCsv), "Weapon stats CSV missing");
        BalanceImportMenu.ImportAll();
        BalanceImportMenu.CreateDefaultDropConfigs();
        AssetDatabase.SaveAssets();
        MaterialUsageBalanceSO balance = AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(
            "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset");
        Assert.That(balance, Is.Not.Null);
        Assert.That(balance.RoleAssignments.Count, Is.GreaterThan(0));
    }
}
#endif
