using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class CraftingAuthoredPriceTests
{
    private const string BalancePath = "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset";
    private const string MenuPath = "Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MaterialType[] Common = { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears };
    private static readonly MaterialType[] Rare = { MaterialType.JellifiedFuel, MaterialType.PlasticExplosive, MaterialType.Wiring };
    private static readonly string[] PriceFields =
    {
        "_basicSecondWeaponCost", "_basicThirdWeaponCost",
        "_advancedFirstWeaponCost", "_advancedSecondWeaponCost", "_advancedThirdWeaponCost",
        "_rejectedFirstWeaponCost", "_rejectedSecondWeaponCost", "_rejectedThirdWeaponCost"
    };
    private static readonly int[] AuthoredPrices = { 9, 21, 7, 11, 19, 13, 23, 37 };
    private readonly List<Object> _cleanup = new();
    private SaveManager _previousSave;
    private Random.State _previousRandom;
    private float _previousTimeScale;
    private int _previousPauseLocks;
    private SceneSetup[] _previousSetup;
    private bool _openedScene;

    [SetUp]
    public void SetUp()
    {
        _openedScene = false;
        Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
        // Dedicated batch runs own Unity's temporary unsaved startup scene.
        for (int index = 0; !Application.isBatchMode && index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            Assert.That(scene.isDirty, Is.False, "Save or discard dirty scenes before this fixture: " + scene.name);
            Assert.That(string.IsNullOrEmpty(scene.path) && scene.rootCount > 0, Is.False,
                "Save an untitled scene with content before this fixture.");
        }
        _previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(_previousSetup.Length > 1 && _previousSetup.Any(scene => string.IsNullOrEmpty(scene.path)), Is.False,
            "Save untitled additive scenes so the original setup can be restored.");
        _previousRandom = Random.state;
        _previousTimeScale = Time.timeScale;
        _previousPauseLocks = GameplayPause.LockCount;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _openedScene = true;
        _previousSave = SaveManager.Instance;
        SetSaveManager(null);
        GameplayPause.Reset();
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (!_openedScene) return;
        try
        {
            for (int i = _cleanup.Count - 1; i >= 0; i--)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        }
        finally
        {
            _cleanup.Clear();
            GameplayPause.Reset();
            for (int index = 0; index < _previousPauseLocks; index++) GameplayPause.Push();
            Time.timeScale = _previousTimeScale;
            Random.state = _previousRandom;
            SetSaveManager(_previousSave);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (_previousSetup != null && _previousSetup.Length > 0
                && _previousSetup.All(scene => !string.IsNullOrEmpty(scene.path)))
                EditorSceneManager.RestoreSceneManagerSetup(_previousSetup);
            _openedScene = false;
        }
    }

    [Test]
    public void NewBalanceAndLegacyCalculatorCalls_PreserveTheExistingDefaultPrices()
    {
        MaterialUsageBalanceSO balance = Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>());
        foreach (int slot in new[] { 2, 3 })
        {
            int expected = slot == 2 ? 5 : 15;
            Assert.That(balance.GetTinkeringCommonCost(slot), Is.EqualTo(expected));
            AssertCosts(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, slot), Common, expected);
            AssertCosts(WeaponCraftingCostCalculator.GetTinkeringSlotCost(null, slot), Common, expected);
            AssertCosts(WeaponCraftingCostCalculator.GetTinkeringSlotCost(slot, false), Common, expected);
            AssertCosts(WeaponCraftingCostCalculator.GetTinkeringSlotCost(slot, true), Common, expected,
                "The retained basic overload's old flag must remain irrelevant to its price.");
        }
        int[] normal = { 5, 15, 30 }, rejected = { 8, 22, 45 };
        for (int tier = 1; tier <= 3; tier++)
        foreach (bool declined in new[] { false, true })
        {
            int expected = (declined ? rejected : normal)[tier - 1];
            Assert.That(balance.GetAdvancedTinkeringRareCost(tier, declined), Is.EqualTo(expected));
            AssertCosts(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, tier, declined), Rare, expected);
            AssertCosts(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(null, tier, declined), Rare, expected);
            AssertCosts(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(tier, declined), Rare, expected);
        }
    }

    [TestCase(2, 9)]
    [TestCase(3, 21)]
    public void AuthoredBasicPrice_ReachesMenuAffordabilityAndOnePaidChoice(int targetSlot, int price)
    {
        Harness h = CreateHarness(Enumerable.Repeat(1, targetSlot - 1).ToArray());
        AuthorDistinctPrices(h.Balance);
        Add(h, Rare, 41);
        Open(h);
        h.View.Slots[targetSlot - 1].Button.onClick.Invoke();
        Assert.That(h.View.TinkerPanel.activeSelf, Is.True);
        AssertCosts(h.Service.GetTinkeringCost(targetSlot), Common, price);
        Assert.That(h.View.TinkerCostText.text, Is.EqualTo(CostText(Common, price)));
        AssertReadout(h.View.TinkerReadout, h.Inventory, Common, price);
        Assert.That(h.Service.CanTinkerNewWeapon(), Is.False);
        Assert.That(h.View.TinkerButton.interactable, Is.False);

        Add(h, Common, price);
        foreach (MaterialType material in Common)
        {
            Assert.That(h.Inventory.TrySpend(new[] { new MaterialCost(material, 1) }), Is.True);
            Assert.That(h.Service.CanTinkerNewWeapon(), Is.False, material + " must independently meet the authored price.");
            Assert.That(h.View.TinkerButton.interactable, Is.False);
            Assert.That(h.View.Slots[targetSlot - 1].Availability.Glow.gameObject.activeSelf, Is.False);
            AssertReadout(h.View.TinkerReadout, h.Inventory, Common, price);
            Assert.That(h.Service.TryBeginTinkering().Success, Is.False);
            Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
            h.Inventory.Add(material, 1);
        }
        Assert.That(h.Service.CanTinkerNewWeapon(), Is.True);
        Assert.That(h.View.TinkerButton.interactable, Is.True);
        Assert.That(h.View.Slots[targetSlot - 1].Availability.Glow.gameObject.activeSelf, Is.True);
        AssertReadout(h.View.TinkerReadout, h.Inventory, Common, price);

        int transactions = 0;
        h.Inventory.OnInventoryChanged += () => transactions++;
        h.View.TinkerButton.onClick.Invoke();
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.True);
        Assert.That(h.View.TinkerChoicePanel.activeSelf, Is.True);
        Assert.That(h.Manager.GetEquippedWeapons().Count, Is.EqualTo(targetSlot - 1));
        AssertAmounts(h.Inventory, Common, 0);
        AssertAmounts(h.Inventory, Rare, 41);
        Assert.That(transactions, Is.EqualTo(1));
        WeaponData chosen = h.Service.GetTinkeringOffer()[0];
        h.View.Candidates[0].Button.onClick.Invoke();
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
        Assert.That(h.Manager.GetEquippedWeapons().Count, Is.EqualTo(targetSlot));
        Assert.That(h.Manager.GetEquippedWeapons()[targetSlot - 1].Runtime.Data, Is.SameAs(chosen));
        AssertAmounts(h.Inventory, Common, 0);
        Assert.That(transactions, Is.EqualTo(1), "Choosing the paid offer must not spend again.");
    }

    [TestCase(1, 7, 13)]
    [TestCase(2, 11, 23)]
    [TestCase(3, 19, 37)]
    public void AuthoredAdvancedPrices_FollowAdvancedCountAndSurviveDeclineAndReopen(int advancedIndex, int firstPrice, int retryPrice)
    {
        // Always three equipped weapons and always select slot one. Only the number
        // of other advanced weapons changes, so inventory slot/count cannot set this price.
        Harness h = CreateHarness(5, 4, 4);
        AuthorDistinctPrices(h.Balance);
        for (int i = 1; i < advancedIndex; i++)
        {
            h.Weapons[i].Level = 6;
            h.Weapons[i].SelectedPath = WeaponUpgradePath.PathA;
        }
        WeaponInstance target = h.Weapons[0];
        Add(h, Common, 43);
        Open(h);
        Assert.That(h.View.AdvancedPanel.activeSelf, Is.True);
        AssertCosts(h.Service.GetAdvancedTinkeringCost(target.Data), Rare, firstPrice);
        Assert.That(h.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: " + CostText(Rare, firstPrice)));
        Assert.That(h.View.AdvancedNoticeText.text, Does.Not.Contain("50%"), "An independently authored retry price need not be a 50% increase.");
        AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, firstPrice);
        Assert.That(h.View.AcceptButton.interactable, Is.False);
        Assert.That(h.Service.GetAvailableAction(target), Is.Null);
        Assert.That(h.Service.TryGetAdvancedOffer(target.Data, out WeaponUpgradePath initial), Is.True);

        Add(h, Rare, firstPrice);
        AssertEveryRareThreshold(h, target, initial, firstPrice);
        int transactions = 0;
        h.Inventory.OnInventoryChanged += () => transactions++;
        h.View.DeclineButton.onClick.Invoke();
        Assert.That(transactions, Is.EqualTo(1));
        Assert.That(target.Level, Is.EqualTo(5));
        Assert.That(target.SelectedPath, Is.EqualTo(WeaponUpgradePath.None));
        AssertAmounts(h.Inventory, Rare, 0);
        AssertAmounts(h.Inventory, Common, 43);
        Assert.That(h.Service.TryGetGuaranteedPath(target.Data, out WeaponUpgradePath alternate), Is.True);
        Assert.That(alternate, Is.Not.EqualTo(initial));
        AssertCosts(h.Service.GetAdvancedTinkeringCost(target.Data), Rare, retryPrice);
        Assert.That(h.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: " + CostText(Rare, retryPrice)));
        Assert.That(h.View.AdvancedNoticeText.text, Does.Not.Contain("50%"));
        Assert.That(h.View.DeclineButton.interactable, Is.False);
        Assert.That(h.View.AcceptButton.interactable, Is.False);
        AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, retryPrice);

        h.View.CloseButton.onClick.Invoke();
        Open(h);
        Assert.That(h.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: " + CostText(Rare, retryPrice)));
        Assert.That(h.View.AdvancedNoticeText.text, Does.Not.Contain("50%"));
        Assert.That(h.Service.TryGetGuaranteedPath(target.Data, out WeaponUpgradePath reopened), Is.True);
        Assert.That(reopened, Is.EqualTo(alternate));
        Add(h, Rare, retryPrice);
        AssertEveryRareThreshold(h, target, alternate, retryPrice);
        int beforeAccept = transactions;
        h.View.AcceptButton.onClick.Invoke();
        Assert.That(transactions, Is.EqualTo(beforeAccept + 1));
        Assert.That(target.Level, Is.EqualTo(6));
        Assert.That(target.SelectedPath, Is.EqualTo(alternate));
        AssertAmounts(h.Inventory, Rare, 0);
        AssertAmounts(h.Inventory, Common, 43);
        Assert.That(h.View.UpgradePanel.activeSelf, Is.True);
    }

    [Test]
    public void InvalidatedPaidChoice_RefundsItsReservedPriceAfterAuthoringChangesWithoutDoubleRefund()
    {
        Harness h = CreateHarness(1);
        SetPrice(h.Balance, "_basicSecondWeaponCost", 9);
        Add(h, Common, 9);
        Add(h, Rare, 41);
        Open(h);
        h.View.Slots[1].Button.onClick.Invoke();
        h.View.TinkerButton.onClick.Invoke();
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.True);
        AssertAmounts(h.Inventory, Common, 0);
        WeaponData chosen = h.Service.GetTinkeringOffer()[0];
        // A live authoring edit must not change the refund owed for the paid offer.
        SetPrice(h.Balance, "_basicSecondWeaponCost", 25);
        SetField(h.Service, "_weaponPool", new List<WeaponData>());
        h.View.Candidates[0].Button.onClick.Invoke();
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
        Assert.That(h.Manager.GetEquippedWeapons().Count, Is.EqualTo(1));
        AssertAmounts(h.Inventory, Common, 9);
        AssertAmounts(h.Inventory, Rare, 41);
        AssertCosts(h.Service.GetTinkeringCost(2), Common, 25);
        Assert.That(h.Service.TryTinkerWeapon(chosen).Success, Is.False);
        AssertAmounts(h.Inventory, Common, 9);
        AssertAmounts(h.Inventory, Rare, 41);
    }

    [TestCase(0)]
    [TestCase(-6)]
    public void FreeOrNegativeBasicPrice_ShowsFreeAndStillCompletesMandatoryChoice(int authored)
    {
        Harness h = CreateHarness(1);
        SetPrice(h.Balance, "_basicSecondWeaponCost", authored);
        Open(h);
        h.View.Slots[1].Button.onClick.Invoke();
        Assert.That(h.Service.GetTinkeringCost(2), Is.Empty);
        Assert.That(h.Service.CanTinkerNewWeapon(), Is.True);
        Assert.That(h.View.TinkerCostText.text, Is.EqualTo("Free"));
        AssertReadout(h.View.TinkerReadout, h.Inventory, Common, 0);
        Assert.That(h.View.TinkerButton.interactable, Is.True);
        int spent = 0;
        h.Inventory.OnMaterialsSpent += () => spent++;
        h.View.TinkerButton.onClick.Invoke();
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.True, "An empty reserved recipe is still a mandatory choice.");
        h.View.Candidates[0].Button.onClick.Invoke();
        Assert.That(h.Manager.GetEquippedWeapons().Count, Is.EqualTo(2));
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
        AssertAmounts(h.Inventory, Common, 0);
        AssertAmounts(h.Inventory, Rare, 0);
        Assert.That(spent, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-6)]
    public void FreeOrNegativeAdvancedPrices_KeepDeclineAndAcceptanceAvailableWithoutMaterials(int authored)
    {
        Harness h = CreateHarness(5);
        SetPrice(h.Balance, "_advancedFirstWeaponCost", authored);
        SetPrice(h.Balance, "_rejectedFirstWeaponCost", authored);
        Open(h);
        Assert.That(h.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: Free"));
        AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, 0);
        Assert.That(h.Service.GetAvailableAction(h.Weapons[0]), Is.EqualTo(CraftingActionKind.AdvancedTinkering));
        Assert.That(h.View.AcceptButton.interactable, Is.True);
        Assert.That(h.View.DeclineButton.interactable, Is.True);
        int spent = 0;
        h.Inventory.OnMaterialsSpent += () => spent++;
        h.View.DeclineButton.onClick.Invoke();
        Assert.That(h.Weapons[0].Level, Is.EqualTo(5));
        Assert.That(h.View.DeclineButton.interactable, Is.False);
        Assert.That(h.View.AcceptButton.interactable, Is.True);
        Assert.That(h.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: Free"));
        AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, 0);
        Assert.That(h.Service.TryGetGuaranteedPath(h.Weapons[0].Data, out WeaponUpgradePath alternate), Is.True);
        h.View.AcceptButton.onClick.Invoke();
        Assert.That(h.Weapons[0].Level, Is.EqualTo(6));
        Assert.That(h.Weapons[0].SelectedPath, Is.EqualTo(alternate));
        Assert.That(h.Weapons[0].SelectedPath, Is.Not.EqualTo(WeaponUpgradePath.None));
        AssertAmounts(h.Inventory, Rare, 0);
        AssertAmounts(h.Inventory, Common, 0);
        Assert.That(spent, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-8)]
    public void EveryAuthoredPrice_ClampsNonpositiveValuesToFreeRecipes(int authored)
    {
        MaterialUsageBalanceSO balance = Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>());
        foreach (string field in PriceFields) SetPrice(balance, field, authored);
        foreach (int slot in new[] { 2, 3 })
        {
            Assert.That(balance.GetTinkeringCommonCost(slot), Is.Zero);
            Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, slot), Is.Empty);
        }
        for (int tier = 1; tier <= 3; tier++)
        foreach (bool declined in new[] { false, true })
        {
            Assert.That(balance.GetAdvancedTinkeringRareCost(tier, declined), Is.Zero);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, tier, declined), Is.Empty);
        }
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(4)]
    public void InvalidIndices_NeverCreateMaterialCosts(int index)
    {
        MaterialUsageBalanceSO balance = Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>());
        AuthorDistinctPrices(balance);
        Assert.That(balance.GetTinkeringCommonCost(index), Is.Zero);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, index), Is.Empty);
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(null, index), Is.Empty);
        foreach (bool declined in new[] { false, true })
        {
            Assert.That(balance.GetAdvancedTinkeringRareCost(index, declined), Is.Zero);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, index, declined), Is.Empty);
            Assert.That(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(null, index, declined), Is.Empty);
        }
        Assert.That(balance.GetTinkeringCommonCost(1), Is.Zero, "The starter weapon remains free.");
        Assert.That(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, 1), Is.Empty);
    }

    [Test]
    public void AuthoringTinkeringAndReplacingRecipeRows_PreserveOrdinaryRecipesAndSeparatePriceFields()
    {
        MaterialUsageBalanceSO balance = CloneBalance();
        string[] originalRecipes = OrdinaryRecipes(balance);
        AuthorDistinctPrices(balance);
        Assert.That(OrdinaryRecipes(balance), Is.EqualTo(originalRecipes));
        // The CSV importer writes these two lists through SetData. Reimporting
        // ordinary recipes must not silently reset the separately authored prices.
        balance.SetData(new List<MaterialRoleAssignment>(balance.RoleAssignments), new List<MaterialRoleTotalRow>(balance.RoleTotals));
        Assert.That(OrdinaryRecipes(balance), Is.EqualTo(originalRecipes));
        for (int i = 0; i < PriceFields.Length; i++)
            Assert.That(new SerializedObject(balance).FindProperty(PriceFields[i]).intValue, Is.EqualTo(AuthoredPrices[i]), PriceFields[i]);
        AssertCosts(WeaponCraftingCostCalculator.GetTinkeringSlotCost(balance, 2), Common, 9);
        AssertCosts(WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(balance, 3, true), Rare, 37);
    }

    [Test]
    public void OrdinaryUpgrade_StillDisplaysAndSpendsItsMaterialRoleRecipeAfterTinkeringEdits()
    {
        Harness h = CreateHarness(1);
        h.Balance.SetData(new List<MaterialRoleAssignment>
        {
            new() { Column = WeaponMaterialColumn.Flamethrower, Material = MaterialType.SheetMetal, Role = MaterialRole.Principal },
            new() { Column = WeaponMaterialColumn.Flamethrower, Material = MaterialType.MetalPipe, Role = MaterialRole.Secondary }
        }, new List<MaterialRoleTotalRow>
        {
            new() { Role = MaterialRole.Principal, Level = 2, Total = 17 },
            new() { Role = MaterialRole.Secondary, Level = 2, Total = 6 }
        });
        AuthorDistinctPrices(h.Balance);
        h.Inventory.Add(MaterialType.SheetMetal, 17);
        h.Inventory.Add(MaterialType.MetalPipe, 5);
        Open(h);
        Assert.That(h.View.UpgradeCostText.text, Is.EqualTo("17 " + MaterialCatalog.GetDisplayName(MaterialType.SheetMetal)
            + " + 6 " + MaterialCatalog.GetDisplayName(MaterialType.MetalPipe)));
        Assert.That(h.View.UpgradeButton.interactable, Is.False);
        Assert.That(h.Service.TryUpgradeWeapon(h.Weapons[0].Data, 2).Success, Is.False);
        h.Inventory.Add(MaterialType.MetalPipe, 1);
        Assert.That(h.View.UpgradeButton.interactable, Is.True);
        h.View.UpgradeButton.onClick.Invoke();
        Assert.That(h.Weapons[0].Level, Is.EqualTo(2));
        AssertAmounts(h.Inventory, Common, 0);
        AssertAmounts(h.Inventory, Rare, 0);
    }

    [Test]
    public void ServiceWithoutAssignedBalance_KeepsLegacyTinkeringAndRejectedPrices()
    {
        Harness h = CreateHarness(5);
        h.Service.SetMaterialBalance(null);
        AssertCosts(h.Service.GetTinkeringCost(2), Common, 5);
        AssertCosts(h.Service.GetTinkeringCost(3), Common, 15);
        AssertCosts(h.Service.GetAdvancedTinkeringCost(h.Weapons[0].Data), Rare, 5);
        Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out WeaponUpgradePath offer), Is.True);
        Add(h, Rare, 5);
        Assert.That(h.Service.TryAdvancedTinkering(h.Weapons[0].Data, offer, false).Success, Is.True);
        AssertCosts(h.Service.GetAdvancedTinkeringCost(h.Weapons[0].Data), Rare, 8);
    }

    private static void AssertEveryRareThreshold(Harness h, WeaponInstance weapon, WeaponUpgradePath offer, int price)
    {
        foreach (MaterialType material in Rare)
        {
            Assert.That(h.Inventory.TrySpend(new[] { new MaterialCost(material, 1) }), Is.True);
            Assert.That(h.Service.GetAvailableAction(weapon), Is.Null, material + " must independently meet the configured price.");
            Assert.That(h.View.AcceptButton.interactable, Is.False);
            Assert.That(h.View.Slots[0].Availability.Glow.gameObject.activeSelf, Is.False);
            AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, price);
            Assert.That(h.Service.TryAdvancedTinkering(weapon.Data, offer, true).Success, Is.False);
            Assert.That(weapon.Level, Is.EqualTo(5));
            h.Inventory.Add(material, 1);
        }
        Assert.That(h.Service.GetAvailableAction(weapon), Is.EqualTo(CraftingActionKind.AdvancedTinkering));
        Assert.That(h.View.AcceptButton.interactable, Is.True);
        Assert.That(h.View.Slots[0].Availability.Glow.gameObject.activeSelf, Is.True);
        AssertReadout(h.View.AdvancedReadout, h.Inventory, Rare, price);
    }

    private Harness CreateHarness(params int[] levels)
    {
        MaterialUsageBalanceSO balance = CloneBalance();
        GameObject menu = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath)));
        GameObject owner = Track(new GameObject("Authored crafting prices fixture"));
        owner.SetActive(false);
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
        WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
        CraftingUI controller = owner.AddComponent<CraftingUI>();
        CraftingMenuView view = menu.GetComponent<CraftingMenuView>();
        SetField(controller, "_view", view);
        SetField(service, "_weaponManager", manager);
        SetField(service, "_inventory", inventory);
        service.SetMaterialBalance(balance);
        var weapons = new List<WeaponInstance>();
        var equipped = (List<IWeaponBehaviour>)typeof(WeaponManager).GetField("_equipped", PrivateInstance).GetValue(manager);
        for (int i = 0; i < levels.Length; i++)
        {
            var runtime = new WeaponInstance { Data = CreateWeapon("Equipped " + i), Level = levels[i] };
            weapons.Add(runtime);
            equipped.Add(new StubWeapon(runtime));
        }
        SetField(service, "_weaponPool", new List<WeaponData> { CreateWeapon("Candidate A"), CreateWeapon("Candidate B") });
        return new Harness { Balance = balance, View = view, Controller = controller, Service = service,
            Inventory = inventory, Manager = manager, Weapons = weapons };
    }

    private MaterialUsageBalanceSO CloneBalance()
    {
        var asset = AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(BalancePath);
        Assert.That(asset, Is.Not.Null);
        return Track(Object.Instantiate(asset));
    }

    private WeaponData CreateWeapon(string name)
    {
        WeaponData weapon = Track(ScriptableObject.CreateInstance<WeaponData>());
        weapon.WeaponId = name;
        weapon.DisplayName = name;
        weapon.WeaponType = WeaponType.Flamethrower;
        weapon.PathA = new WeaponUpgradePathData { PathName = "Path A" };
        weapon.PathB = new WeaponUpgradePathData { PathName = "Path B" };
        return weapon;
    }

    private static void AuthorDistinctPrices(MaterialUsageBalanceSO balance)
    {
        for (int i = 0; i < PriceFields.Length; i++) SetPrice(balance, PriceFields[i], AuthoredPrices[i]);
    }

    private static void SetPrice(MaterialUsageBalanceSO balance, string field, int amount)
    {
        var serialized = new SerializedObject(balance);
        SerializedProperty property = serialized.FindProperty(field);
        Assert.That(property, Is.Not.Null, "Missing authored price field: " + field);
        property.intValue = amount;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Open(Harness h)
    {
        Assert.That(h.Controller.PresentCoroutine(h.Service, h.Inventory, () => { }).MoveNext(), Is.True);
        Assert.That(h.Controller.IsVisible, Is.True);
    }

    private static void Add(Harness h, IEnumerable<MaterialType> materials, int amount)
    {
        foreach (MaterialType material in materials) h.Inventory.Add(material, amount);
    }

    private static void AssertCosts(IReadOnlyList<MaterialCost> costs, MaterialType[] materials, int amount, string message = null)
    {
        Assert.That(costs.Select(c => c.Material), Is.EqualTo(materials), message);
        Assert.That(costs.Select(c => c.Amount), Is.EqualTo(Enumerable.Repeat(amount, materials.Length)), message);
    }

    private static void AssertAmounts(MaterialInventory inventory, IEnumerable<MaterialType> materials, int amount)
    {
        foreach (MaterialType material in materials) Assert.That(inventory.GetAmount(material), Is.EqualTo(amount), material.ToString());
    }

    private static void AssertReadout(CraftingMaterialReadout readout, MaterialInventory inventory, MaterialType[] materials, int required)
    {
        Assert.That(readout, Is.Not.Null, "Use the authored material readout, not just the legacy text field.");
        foreach (CraftingMaterialReadout.Entry entry in readout.Entries)
        {
            bool visible = required > 0 && materials.Contains(entry.Type);
            Assert.That(entry.Root.activeSelf, Is.EqualTo(visible), entry.Type.ToString());
            if (!visible) continue;
            int owned = inventory.GetAmount(entry.Type);
            Assert.That(entry.Quantity.text, Is.EqualTo(owned + "/" + required), entry.Type.ToString());
            Assert.That(entry.Quantity.color, Is.EqualTo(owned >= required ? readout.SufficientColor : readout.InsufficientColor));
        }
        Assert.That(readout.Notice.text, Is.EqualTo(required == 0 ? "Free" : string.Empty));
    }

    private static string CostText(IEnumerable<MaterialType> materials, int amount) =>
        string.Join(" + ", materials.Select(material => amount + " " + MaterialCatalog.GetDisplayName(material)));

    private static string[] OrdinaryRecipes(MaterialUsageBalanceSO balance) =>
        (from WeaponType weapon in Enum.GetValues(typeof(WeaponType))
         from WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath))
         from level in Enumerable.Range(1, 10)
         select weapon + ":" + path + ":" + level + "=" + string.Join(";",
             WeaponCraftingCostCalculator.GetUpgradeCost(balance, weapon, path, level).Select(cost => cost.Material + ":" + cost.Amount))).ToArray();

    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static void SetSaveManager(SaveManager value) => typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }

    private sealed class Harness
    {
        public MaterialUsageBalanceSO Balance;
        public CraftingMenuView View;
        public CraftingUI Controller;
        public WeaponCraftingService Service;
        public MaterialInventory Inventory;
        public WeaponManager Manager;
        public List<WeaponInstance> Weapons;
    }

    private sealed class StubWeapon : IWeaponBehaviour
    {
        public WeaponInstance Runtime { get; }
        public StubWeapon(WeaponInstance runtime) { Runtime = runtime; }
        public void Setup(WeaponInstance instance, Transform owner, PlayerStats stats, HeatManager heat) { }
        public void TickAutomatic(float deltaTime, Vector3 aimDirection) { }
        public void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring) { }
        public void UseActiveAbility(Vector3 aimDirection) { }
        public bool CanCrit() => false;
    }
}
