#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authoring and migration coverage; transaction tests belong to the crafting fixture.</summary>
public sealed class TinkeringCostAuthoringTests
{
    private const string MaterialCsv = "Assets/Data/Balance/balance_material_usage.csv";
    private const string LegacyAsset = "Assets/ScriptableObjects/Economy/MaterialUsageBalance_Test.asset";
    private static readonly string[] CostFields =
    {
        "_basicSecondWeaponCost", "_basicThirdWeaponCost",
        "_advancedFirstWeaponCost", "_advancedSecondWeaponCost", "_advancedThirdWeaponCost",
        "_rejectedFirstWeaponCost", "_rejectedSecondWeaponCost", "_rejectedThirdWeaponCost"
    };
    private static readonly int[] DefaultCosts = { 5, 15, 5, 15, 30, 8, 22, 45 };
    private static readonly int[] CustomCosts = { 13, 29, 41, 53, 67, 79, 83, 97 };
    private static readonly MaterialType[] CommonMaterials =
        { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears };
    private static readonly MaterialType[] RareMaterials =
        { MaterialType.JellifiedFuel, MaterialType.PlasticExplosive, MaterialType.Wiring };

    private readonly List<MaterialUsageBalanceSO> _transientAssets = new List<MaterialUsageBalanceSO>();
    private readonly List<string> _temporaryAssetPaths = new List<string>();

    [TearDown]
    public void TearDown()
    {
        foreach (var asset in _transientAssets)
            if (asset != null) Object.DestroyImmediate(asset);
        _transientAssets.Clear();
        foreach (string path in _temporaryAssetPaths)
        {
            if (AssetDatabase.DeleteAsset(path)) continue;
            // An import failure can leave our raw YAML unregistered with AssetDatabase.
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        }
        _temporaryAssetPaths.Clear();
    }

    [Test]
    public void NewAsset_AllEightSerializedDefaultsMatchTheExistingPrices()
    {
        var balance = NewBalance();
        AssertSerializedCosts(balance, DefaultCosts);
        AssertAuthoredRecipes(balance, DefaultCosts);
    }

    [TestCase(2, 5)]
    [TestCase(3, 15)]
    public void BasicDefaults_LegacyAndNullBalanceOverloadsKeepBothBooleanCallers(int slot, int amount)
    {
        var balance = NewBalance();
        Assert.That(MaterialUsageBalanceSO.GetDefaultTinkeringCommonCost(slot), Is.EqualTo(amount));
        Assert.That(balance.GetTinkeringCommonCost(slot), Is.EqualTo(amount));
        AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(slot, false), CommonMaterials, amount);
        AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(slot, true), CommonMaterials, amount);
        AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(null, slot), CommonMaterials, amount);
        AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, slot), CommonMaterials, amount);
    }

    [TestCase(1, false, 5)]
    [TestCase(2, false, 15)]
    [TestCase(3, false, 30)]
    [TestCase(1, true, 8)]
    [TestCase(2, true, 22)]
    [TestCase(3, true, 45)]
    public void AdvancedDefaults_LegacyAndNullBalanceOverloadsKeepEveryIndexAndRejectionPrice(
        int index, bool rejected, int amount)
    {
        var balance = NewBalance();
        Assert.That(MaterialUsageBalanceSO.GetDefaultAdvancedTinkeringRareCost(index, rejected), Is.EqualTo(amount));
        Assert.That(balance.GetAdvancedTinkeringRareCost(index, rejected), Is.EqualTo(amount));
        AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(index, rejected), RareMaterials, amount);
        AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(null, index, rejected), RareMaterials, amount);
        AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, index, rejected), RareMaterials, amount);
    }

    [Test]
    public void AuthoredFields_MapIndependentlyToTheirOwnIndexAndRejectionState()
    {
        var balance = NewBalance();
        SetSerializedCosts(balance, CustomCosts);
        AssertSerializedCosts(balance, CustomCosts);
        AssertAuthoredRecipes(balance, CustomCosts);

        // Authoring a balance must not alter the compatibility defaults used without an asset.
        AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(2, false), CommonMaterials, 5);
        AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(3, true), RareMaterials, 45);
    }

    [TestCase(0)]
    [TestCase(-7)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void AuthoredAmounts_ClampNegativeValuesAtReadTimeAndAllowExplicitZero(int authored)
    {
        var balance = NewBalance();
        var authoredValues = Enumerable.Repeat(authored, CostFields.Length).ToArray();
        SetSerializedCosts(balance, authoredValues);
        AssertAuthoredRecipes(balance, Enumerable.Repeat(Math.Max(0, authored), CostFields.Length).ToArray());
        AssertSerializedCosts(balance, authoredValues);
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(4)]
    [TestCase(int.MaxValue)]
    public void InvalidIndices_ReturnNoRecipeForAuthoredDefaultAndLegacyEntryPoints(int index)
    {
        var balance = NewBalance();
        SetSerializedCosts(balance, CustomCosts);
        Assert.That(balance.GetTinkeringCommonCost(index), Is.Zero);
        Assert.That(MaterialUsageBalanceSO.GetDefaultTinkeringCommonCost(index), Is.Zero);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, index), Is.Empty);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(null, index), Is.Empty);
        foreach (bool rejected in new[] { false, true })
        {
            Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(index, rejected), Is.Empty);
            Assert.That(balance.GetAdvancedTinkeringRareCost(index, rejected), Is.Zero);
            Assert.That(MaterialUsageBalanceSO.GetDefaultAdvancedTinkeringRareCost(index, rejected), Is.Zero);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, index, rejected), Is.Empty);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(null, index, rejected), Is.Empty);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(index, rejected), Is.Empty);
        }
    }

    [Test]
    public void FirstWeaponSlot_HasNoBasicPurchaseRecipeEvenWhenOtherSlotsAreAuthored()
    {
        var balance = NewBalance();
        SetSerializedCosts(balance, CustomCosts);
        Assert.That(balance.GetTinkeringCommonCost(1), Is.Zero);
        Assert.That(MaterialUsageBalanceSO.GetDefaultTinkeringCommonCost(1), Is.Zero);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, 1), Is.Empty);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(null, 1), Is.Empty);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(1, false), Is.Empty);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(1, true), Is.Empty);
        AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, 1, false), RareMaterials, CustomCosts[2]);
    }

    [Test]
    public void LegacyYamlWithoutNewFields_DeserializesAllHistoricalDefaults()
    {
        string path = CreateLegacyAssetCopy();
        var legacy = LoadFromDisk(path);
        AssertSerializedCosts(legacy, DefaultCosts);
        AssertAuthoredRecipes(legacy, DefaultCosts);
        Assert.That(legacy.RoleAssignments.Count, Is.GreaterThan(0));
        Assert.That(legacy.RoleTotals.Count, Is.GreaterThan(0));

        EditorUtility.SetDirty(legacy);
        legacy = SaveAndReload(legacy, path);
        AssertSerializedCosts(legacy, DefaultCosts);
        AssertAuthoredRecipes(legacy, DefaultCosts);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CsvImportThenReimport_PreserveEveryAuthoredPriceAndRefreshOnlyTheOldLists(bool includeFreePrice)
    {
        string path = CreateLegacyAssetCopy();
        var balance = LoadFromDisk(path);
        int[] authored = (int[])CustomCosts.Clone();
        if (includeFreePrice) authored[0] = authored[3] = authored[7] = 0;
        SetSerializedCosts(balance, authored);
        balance = SaveAndReload(balance, path);
        var expected = NewBalanceFromCsv();

        for (int pass = 0; pass < 2; pass++)
        {
            // Ensure each import really replaces data rather than simply reading unchanged lists.
            balance.SetData(new List<MaterialRoleAssignment>(), new List<MaterialRoleTotalRow>());
            EditorUtility.SetDirty(balance);
            AssetDatabase.SaveAssetIfDirty(balance);
            balance = MaterialUsageCsvImporter.Import(MaterialCsv, path);
            balance = SaveAndReload(balance, path);
            AssertSerializedCosts(balance, authored);
            AssertAuthoredRecipes(balance, authored);
            Assert.That(RoleDataSnapshot(balance), Is.EqualTo(RoleDataSnapshot(expected)), "CSV data after pass " + pass);
        }
    }

    [Test]
    public void CsvImportIntoANewDestination_UsesHistoricalTinkeringDefaults()
    {
        string path = NewTemporaryAssetPath();
        var balance = MaterialUsageCsvImporter.Import(MaterialCsv, path);
        balance = SaveAndReload(balance, path);
        AssertSerializedCosts(balance, DefaultCosts);
        AssertAuthoredRecipes(balance, DefaultCosts);
        Assert.That(RoleDataSnapshot(balance), Is.EqualTo(RoleDataSnapshot(NewBalanceFromCsv())));
    }

    [Test]
    public void SetDataWithNullLists_PreservesAuthoredPrices()
    {
        var balance = NewBalanceFromCsv();
        SetSerializedCosts(balance, CustomCosts);
        balance.SetData(null, null);
        Assert.That(balance.RoleAssignments, Is.Empty);
        Assert.That(balance.RoleTotals, Is.Empty);
        AssertSerializedCosts(balance, CustomCosts);
        AssertAuthoredRecipes(balance, CustomCosts);
    }

    [Test]
    public void ChangingOnlyTinkeringPrices_LeavesAllOrdinaryUpgradeRecipesUnchanged()
    {
        var balance = NewBalanceFromCsv();
        string[] original = UpgradeRecipeSnapshot(balance);
        Assert.That(original.Any(entry => !entry.EndsWith("=")), Is.True, "The comparison must include nonempty recipes.");
        SetSerializedCosts(balance, CustomCosts);
        Assert.That(UpgradeRecipeSnapshot(balance), Is.EqualTo(original));
        SetSerializedCosts(balance, Enumerable.Repeat(0, CostFields.Length).ToArray());
        Assert.That(UpgradeRecipeSnapshot(balance), Is.EqualTo(original));
    }

    private MaterialUsageBalanceSO NewBalance()
    {
        var balance = ScriptableObject.CreateInstance<MaterialUsageBalanceSO>();
        _transientAssets.Add(balance);
        return balance;
    }

    private MaterialUsageBalanceSO NewBalanceFromCsv()
    {
        MaterialUsageParser.Parse(CsvReader.ReadAllRows(MaterialCsv), out var assignments, out var totals);
        var balance = NewBalance();
        balance.SetData(assignments, totals);
        return balance;
    }

    private string NewTemporaryAssetPath()
    {
        string path = "Assets/__TinkeringCostAuthoring_" + Guid.NewGuid().ToString("N") + ".asset";
        _temporaryAssetPaths.Add(path);
        return path;
    }

    private string CreateLegacyAssetCopy()
    {
        string yaml = File.ReadAllText(LegacyAsset);
        foreach (string field in CostFields)
            Assert.That(yaml.Contains("  " + field + ":"), Is.False, "The legacy fixture must omit " + field);
        string path = NewTemporaryAssetPath();
        File.WriteAllText(path, yaml);
        return path;
    }

    private static MaterialUsageBalanceSO LoadFromDisk(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var balance = AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(path);
        Assert.That(balance, Is.Not.Null, path);
        return balance;
    }

    private static MaterialUsageBalanceSO SaveAndReload(MaterialUsageBalanceSO balance, string path)
    {
        AssetDatabase.SaveAssetIfDirty(balance);
        Resources.UnloadAsset(balance);
        return LoadFromDisk(path);
    }

    private static void SetSerializedCosts(MaterialUsageBalanceSO balance, int[] values)
    {
        using var serialized = new SerializedObject(balance);
        for (int i = 0; i < CostFields.Length; i++)
        {
            var property = serialized.FindProperty(CostFields[i]);
            Assert.That(property, Is.Not.Null, CostFields[i] + " must remain editor-authored.");
            property.intValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssertSerializedCosts(MaterialUsageBalanceSO balance, int[] expected)
    {
        using var serialized = new SerializedObject(balance);
        for (int i = 0; i < CostFields.Length; i++)
        {
            var property = serialized.FindProperty(CostFields[i]);
            Assert.That(property, Is.Not.Null, CostFields[i]);
            Assert.That(property.intValue, Is.EqualTo(expected[i]), CostFields[i]);
        }
    }

    private static void AssertAuthoredRecipes(MaterialUsageBalanceSO balance, int[] expected)
    {
        for (int slot = 2; slot <= 3; slot++)
        {
            int amount = expected[slot - 2];
            Assert.That(balance.GetTinkeringCommonCost(slot), Is.EqualTo(amount), "Basic slot " + slot);
            AssertRecipe(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, slot), CommonMaterials, amount);
        }
        foreach (bool rejected in new[] { false, true })
            for (int index = 1; index <= 3; index++)
            {
                int amount = expected[(rejected ? 5 : 2) + index - 1];
                Assert.That(balance.GetAdvancedTinkeringRareCost(index, rejected), Is.EqualTo(amount),
                    "Advanced index " + index + "; rejected=" + rejected);
                AssertRecipe(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, index, rejected), RareMaterials, amount);
            }
    }

    private static void AssertRecipe(IReadOnlyList<MaterialCost> recipe, MaterialType[] materials, int amount)
    {
        if (amount == 0) { Assert.That(recipe, Is.Empty); return; }
        Assert.That(recipe.Count, Is.EqualTo(materials.Length));
        Assert.That(recipe.Select(cost => cost.Material).ToArray(), Is.EqualTo(materials));
        Assert.That(recipe.Select(cost => cost.Amount).ToArray(), Is.EqualTo(Enumerable.Repeat(amount, materials.Length).ToArray()));
    }

    private static string[] RoleDataSnapshot(MaterialUsageBalanceSO balance)
    {
        return balance.RoleAssignments.Select(row => "A:" + row.Column + ":" + row.Material + ":" + row.Role)
            .Concat(balance.RoleTotals.Select(row => "T:" + row.Role + ":" + row.Level + ":" + row.Total)).ToArray();
    }

    private static string[] UpgradeRecipeSnapshot(MaterialUsageBalanceSO balance)
    {
        var result = new List<string>();
        foreach (WeaponType weapon in Enum.GetValues(typeof(WeaponType)))
            foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
                for (int level = 1; level <= 10; level++)
                {
                    var recipe = WeaponCraftingCostCalculator.GetUpgradeCost(balance, weapon, path, level);
                    result.Add(weapon + ":" + path + ":" + level + "=" +
                        string.Join(",", recipe.Select(cost => cost.Material + ":" + cost.Amount)));
                }
        return result.ToArray();
    }
}
#endif
