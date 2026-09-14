using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class RunMenuUiTests
{
    private const string Folder = "Assets/Prefabs/UI/RunMenus/";
    private const string ContentPath = "Assets/Data/UI/RunMenus/RunMenuContent.asset";
    private readonly List<Object> _cleanup = new();
    private Random.State _randomState;
    private SaveManager _previousSaveManager;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _randomState = Random.state;
        _previousSaveManager = SaveManager.Instance;
        SetSaveManager(null);
        Time.timeScale = 1f;
        GameplayPause.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        // Controllers are registered after their views, so their cleanup can release listeners first.
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        SetSaveManager(_previousSaveManager);
        Random.state = _randomState;
        Time.timeScale = 1f;
        GameplayPause.Reset();
    }

    [TestCase("LevelUpMenu", 3)]
    [TestCase("WeaponSelectionMenu", 2)]
    public void ChoicePrefabs_HaveConfiguredAuthoredCards(string name, int cardCount)
    {
        ChoiceMenuView view = LoadPrefab(name).GetComponent<ChoiceMenuView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(view.Cards, Has.Length.EqualTo(cardCount));
        Assert.That(view.CanPresent(cardCount), Is.True);
        Assert.That(view.CanPresent(cardCount + 1), Is.False);
        AssertObjectReferences(view);
        foreach (ChoiceCardView card in view.Cards) AssertObjectReferences(card);
        AssertFonts(view.gameObject);
    }

    [Test]
    public void CraftingPrefab_HasAllThreeSlotsSixMaterialsAndAuthoredPanels()
    {
        CraftingMenuView view = LoadPrefab("CraftingMenu").GetComponent<CraftingMenuView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(view.IsConfigured, Is.True);
        AssertObjectReferences(view);
        Assert.That(view.Slots, Has.Length.EqualTo(3));
        Assert.That(view.Materials, Has.Length.EqualTo(6));
        Assert.That(view.Materials.Select(field => field.Type), Is.EquivalentTo(Enum.GetValues(typeof(MaterialType))));
        Assert.That(view.UpgradeStatLabels, Has.Length.EqualTo(3));
        Assert.That(view.UpgradeStatValues, Has.Length.EqualTo(3));
        Assert.That(view.Candidates.Length, Is.GreaterThanOrEqualTo(5));
        foreach (CraftingWeaponSlotView slot in view.Slots) AssertObjectReferences(slot);
        foreach (CraftingMaterialField field in view.Materials) AssertObjectReferences(field);
        foreach (CraftingCandidateField field in view.Candidates) AssertObjectReferences(field);
        AssertFonts(view.gameObject);
    }

    [Test]
    public void PlayerPrefab_KeepsContentAndControllersWithoutOwningSceneUi()
    {
        // Inspect the asset directly; do not run player Awake or persistent save initialization.
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(player, Is.Not.Null);
        LevelUpChoiceUI choices = player.GetComponent<LevelUpChoiceUI>();
        CraftingUI crafting = player.GetComponent<CraftingUI>();
        Assert.That(choices, Is.Not.Null);
        Assert.That(crafting, Is.Not.Null);
        Assert.That(player.GetComponentsInChildren<Canvas>(true), Is.Empty);
        Assert.That(player.GetComponentsInChildren<ChoiceMenuView>(true), Is.Empty);
        Assert.That(player.GetComponentsInChildren<CraftingMenuView>(true), Is.Empty);
        Assert.That(new SerializedObject(choices).FindProperty("_levelUpView").objectReferenceValue, Is.Null);
        Assert.That(new SerializedObject(choices).FindProperty("_weaponSelectionView").objectReferenceValue, Is.Null);
        Assert.That(new SerializedObject(crafting).FindProperty("_view").objectReferenceValue, Is.Null);
        RunMenuContent content = AssetDatabase.LoadAssetAtPath<RunMenuContent>(ContentPath);
        Assert.That(content, Is.Not.Null);
        Assert.That(new SerializedObject(choices).FindProperty("_content").objectReferenceValue, Is.EqualTo(content));
        Assert.That(new SerializedObject(crafting).FindProperty("_content").objectReferenceValue, Is.EqualTo(content));
        Assert.That(content.Weapons, Has.Length.EqualTo(5));
        foreach (WeaponMenuCopy copy in content.Weapons)
        {
            Assert.That(copy.Weapon, Is.Not.Null);
        }
        Assert.That(content.Weapons.Select(copy => copy.Weapon).Distinct().Count(), Is.EqualTo(5));
    }

    [Test]
    public void LevelUp_BindsExistingChoices_SelectsOnceAndRestoresPauseWithoutCreatingUi()
    {
        ChoiceMenuView view = InstantiateView<ChoiceMenuView>("LevelUpMenu");
        LevelUpChoiceUI controller = CreateChoiceController(view, null);
        int[] hierarchy = Hierarchy(view);
        int callbacks = 0;
        int selected = -1;
        var options = new[]
        {
            new LevelUpChoiceOption("Passive item", "Existing item description"),
            new LevelUpChoiceOption("Upgrade existing passive", "+1 item level"),
            new LevelUpChoiceOption("Another available passive", "Existing choice")
        };
        Time.timeScale = 0.5f;
        controller.Show("LEVEL UP", options, index => { callbacks++; selected = index; });
        Assert.That(view.Cards.Select(card => card.NameText.text), Is.EqualTo(options.Select(option => option.DisplayName)));
        Assert.That(view.Cards[0].DescriptionText.text, Is.EqualTo(options[0].Description));
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1));
        Assert.That(Time.timeScale, Is.Zero);

        view.Cards[1].SelectButton.onClick.Invoke();
        view.Cards[1].SelectButton.onClick.Invoke();
        Assert.That(callbacks, Is.EqualTo(1));
        Assert.That(selected, Is.EqualTo(1));
        Assert.That(controller.IsVisible, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(0.5f));

        controller.Show("LEVEL UP", options, _ => callbacks++);
        view.Cards[2].SelectButton.onClick.Invoke();
        Assert.That(callbacks, Is.EqualTo(2), "Reopening must not accumulate selection listeners.");
        Assert.That(Hierarchy(view), Is.EqualTo(hierarchy));
    }

    [Test]
    public void FirstWeapon_UsesTwoCardViewAndBlankEditableDescriptions()
    {
        ChoiceMenuView level = InstantiateView<ChoiceMenuView>("LevelUpMenu");
        ChoiceMenuView weapons = InstantiateView<ChoiceMenuView>("WeaponSelectionMenu");
        LevelUpChoiceUI controller = CreateChoiceController(level, weapons);
        WeaponData first = CreateWeapon("First weapon");
        WeaponData second = CreateWeapon("Second weapon");
        RunMenuContent content = Track(ScriptableObject.CreateInstance<RunMenuContent>());
        content.Weapons = new[] { new WeaponMenuCopy { Weapon = first }, new WeaponMenuCopy { Weapon = second } };
        SetField(controller, "_content", content);
        int selected = -1;
        int callbacks = 0;
        int[] hierarchy = Hierarchy(weapons);
        IEnumerator presentation = controller.PresentWeaponSelectionCoroutine("CHOOSE YOUR FIRST WEAPON", new[]
        {
            new LevelUpChoiceOption(first.DisplayName, weapon: first),
            new LevelUpChoiceOption(second.DisplayName, weapon: second)
        }, index => { selected = index; callbacks++; });
        Assert.That(presentation.MoveNext(), Is.True);
        Assert.That(weapons.gameObject.activeSelf, Is.True);
        Assert.That(level.gameObject.activeSelf, Is.False);
        Assert.That(weapons.SubtitleText.text, Is.EqualTo("Choose 1 of 2 weapons"));
        Assert.That(weapons.Cards[0].SummaryText.text, Is.Empty);
        Assert.That(weapons.Cards[0].DescriptionText.text, Is.Empty);
        weapons.Cards[0].SelectButton.onClick.Invoke();
        weapons.Cards[0].SelectButton.onClick.Invoke();
        Assert.That(presentation.MoveNext(), Is.False);
        Assert.That(selected, Is.Zero);
        Assert.That(callbacks, Is.EqualTo(1));
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Hierarchy(weapons), Is.EqualTo(hierarchy));
    }

    [Test]
    public void MissingChoiceView_FailsWithoutPauseOrRuntimeUi()
    {
        LevelUpChoiceUI controller = CreateChoiceController(null, null);
        int[] hierarchy = Hierarchy(controller);
        int result = 0;
        LogAssert.Expect(LogType.Warning, "LevelUpChoiceUI: assign an authored choice view with enough configured cards.");
        controller.Show("LEVEL UP", new[] { new LevelUpChoiceOption("Existing choice") }, index => result = index);
        Assert.That(result, Is.EqualTo(-1));
        Assert.That(controller.IsVisible, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Hierarchy(controller), Is.EqualTo(hierarchy));
        Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
    }

    [Test]
    public void Crafting_ShowsMaxWeaponEmptySlotsAndLiveMaterialAmounts()
    {
        CraftingHarness harness = CreateCraftingHarness(10);
        Open(harness);
        Assert.That(harness.View.Slots.Select(slot => slot.gameObject.activeSelf), Is.All.True);
        Assert.That(harness.View.Slots[0].NameText.text, Is.EqualTo(harness.Weapons[0].Data.DisplayName));
        Assert.That(harness.View.Slots[0].LevelText.text, Is.EqualTo("LV 10"));
        Assert.That(harness.View.Slots[1].NameText.text, Is.EqualTo("Empty slot"));
        Assert.That(harness.View.Slots[2].NameText.text, Is.EqualTo("Empty slot"));
        Assert.That(harness.View.UpgradeButton.interactable, Is.False);
        Assert.That(harness.View.UpgradeButtonText.text, Is.EqualTo("MAX LEVEL"));
        foreach (CraftingMaterialField field in harness.View.Materials)
        {
            Assert.That(field.AmountText.text, Is.EqualTo(harness.Inventory.GetAmount(field.Type).ToString()));
            harness.Inventory.Add(field.Type, 7);
            Assert.That(field.AmountText.text, Is.EqualTo(harness.Inventory.GetAmount(field.Type).ToString()));
        }
    }

    [TestCase(WeaponType.Flamethrower, "Auto mode range (m)", "Auto range")]
    [TestCase(WeaponType.RotatingBlade, "Auto blade length (m)", "Auto blade length")]
    public void UpgradePreview_UsesConfiguredRowsAndActualServiceCost(WeaponType type, string rangeId, string rangeLabel)
    {
        CraftingHarness harness = CreateCraftingHarness(4);
        WeaponData weapon = harness.Weapons[0].Data;
        weapon.WeaponType = type;
        AddRow(weapon, "Damage", 4, 55); AddRow(weapon, "Damage", 5, 65);
        AddRow(weapon, rangeId, 4, 3.5f); AddRow(weapon, rangeId, 5, 4);
        AddRow(weapon, "Manual ammo", 4, 160); AddRow(weapon, "Manual ammo", 5, 180);
        weapon.BaseDamage = 999f;
        weapon.BaseRange = 999f;
        weapon.BaseManualAmmo = 999f;
        Open(harness);
        Assert.That(harness.View.UpgradeStatValues.Select(label => label.text),
            Is.EqualTo(new[] { "55 → 65", "3.5 → 4 m", "160 → 180" }));
        Assert.That(harness.View.UpgradeStatLabels[1].text, Is.EqualTo(rangeLabel));
        Assert.That(harness.View.UpgradeCostText.text, Is.EqualTo(CostText(harness.Service.GetUpgradeCost(weapon, WeaponUpgradePath.None, 5))));
    }

    [Test]
    public void UpgradeButton_SpendsOnceAndRefreshesIntoExistingAdvancedPanel()
    {
        CraftingHarness harness = CreateCraftingHarness(4);
        Open(harness);
        int[] hierarchy = Hierarchy(harness.View);
        int transactions = 0;
        harness.Inventory.OnInventoryChanged += () => transactions++;
        harness.View.UpgradeButton.onClick.Invoke();
        Assert.That(harness.Weapons[0].Level, Is.EqualTo(5));
        Assert.That(transactions, Is.EqualTo(1));
        Assert.That(harness.View.AdvancedPanel.activeSelf, Is.True);
        Assert.That(Hierarchy(harness.View), Is.EqualTo(hierarchy));
    }

    [Test]
    public void Tinker_ShowsServiceCandidatesAndCurrentSlotCostWithoutCreatingUi()
    {
        CraftingHarness harness = CreateCraftingHarness(4, 3);
        WeaponData available = CreateWeapon("Available weapon");
        SetField(harness.Service, "_weaponPool", new List<WeaponData>
        {
            harness.Weapons[0].Data, available, harness.Weapons[1].Data
        });
        int[] hierarchy = Hierarchy(harness.View);
        Open(harness);
        harness.View.Slots[2].Button.onClick.Invoke();
        List<WeaponData> candidates = harness.Service.BuildUnequippedWeapons();
        Assert.That(candidates, Is.EqualTo(new[] { available }));
        Assert.That(harness.View.TinkerPanel.activeSelf, Is.True);
        Assert.That(harness.View.Candidates.Where(field => field.Root.activeSelf).Select(field => field.NameText.text),
            Is.EqualTo(candidates.Select(weapon => weapon.DisplayName)));
        Assert.That(harness.View.TinkerCostLabel.text, Is.EqualTo("COST · SLOT 3"));
        Assert.That(harness.View.TinkerCostText.text, Is.EqualTo(CostText(harness.Service.GetTinkeringCost(3))));
        Assert.That(harness.View.TinkerButton.interactable, Is.True);
        Assert.That(Hierarchy(harness.View), Is.EqualTo(hierarchy));
    }

    [Test]
    public void Tinker_NoCandidatesDisablesPurchase()
    {
        CraftingHarness harness = CreateCraftingHarness(4);
        SetField(harness.Service, "_weaponPool", new List<WeaponData> { harness.Weapons[0].Data });
        Open(harness);
        harness.View.Slots[1].Button.onClick.Invoke();
        Assert.That(harness.View.TinkerButton.interactable, Is.False);
        Assert.That(harness.View.TinkerCostText.text, Is.EqualTo("No new weapons available"));
        Assert.That(harness.View.Candidates.Any(field => field.Root.activeSelf), Is.False);
    }

    [Test]
    public void Advanced_UsesCurrentOfferDeclineAndAcceptanceWithServicePrices()
    {
        CraftingHarness harness = CreateCraftingHarness(5);
        WeaponInstance weapon = harness.Weapons[0];
        Open(harness);
        int[] hierarchy = Hierarchy(harness.View);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath initial), Is.True);
        Assert.That(harness.View.AdvancedPathText.text, Is.EqualTo(PathName(weapon.Data, initial)));
        Assert.That(harness.View.AdvancedDescriptionText.text, Is.Empty);
        Assert.That(harness.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: " + CostText(harness.Service.GetAdvancedTinkeringCost(weapon.Data))));
        int transactions = 0;
        harness.Inventory.OnInventoryChanged += () => transactions++;
        harness.View.DeclineButton.onClick.Invoke();
        Assert.That(harness.Service.TryGetGuaranteedPath(weapon.Data, out WeaponUpgradePath alternate), Is.True);
        Assert.That(alternate, Is.Not.EqualTo(initial));
        Assert.That(weapon.Level, Is.EqualTo(5));
        Assert.That(harness.View.AdvancedPathText.text, Is.EqualTo(PathName(weapon.Data, alternate)));
        Assert.That(harness.View.DeclineButton.interactable, Is.False);
        Assert.That(harness.View.AdvancedCostText.text, Is.EqualTo("Tinker cost: " + CostText(harness.Service.GetAdvancedTinkeringCost(weapon.Data))));
        Assert.That(harness.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(95));
        harness.View.AcceptButton.onClick.Invoke();
        Assert.That(weapon.Level, Is.EqualTo(6));
        Assert.That(weapon.SelectedPath, Is.EqualTo(alternate));
        Assert.That(harness.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(87));
        Assert.That(transactions, Is.EqualTo(2));
        Assert.That(harness.View.UpgradePanel.activeSelf, Is.True);
        Assert.That(Hierarchy(harness.View), Is.EqualTo(hierarchy));
    }

    [Test]
    public void Crafting_ReopenPreservesOfferAndHierarchyAndCloseCallbackRunsOnce()
    {
        CraftingHarness harness = CreateCraftingHarness(5);
        int[] hierarchy = Hierarchy(harness.View);
        Time.timeScale = 0.5f;
        int closed = 0;
        IEnumerator presentation = harness.Controller.PresentCoroutine(harness.Service, harness.Inventory, () => closed++);
        Assert.That(presentation.MoveNext(), Is.True);
        string offeredPath = harness.View.AdvancedPathText.text;
        Random.State afterOffer = Random.state;
        harness.View.CloseButton.onClick.Invoke();
        harness.View.CloseButton.onClick.Invoke();
        Assert.That(presentation.MoveNext(), Is.False);
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(Time.timeScale, Is.EqualTo(0.5f));
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Open(harness);
        Assert.That(harness.View.AdvancedPathText.text, Is.EqualTo(offeredPath));
        Assert.That(Random.state, Is.EqualTo(afterOffer));
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1));
        harness.View.CloseButton.onClick.Invoke();
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Hierarchy(harness.View), Is.EqualTo(hierarchy));
    }

    [Test]
    public void MissingCraftingView_FailsWithoutPauseOrRuntimeUi()
    {
        CraftingHarness harness = CreateCraftingHarness(4, createView: false);
        int closed = 0;
        LogAssert.Expect(LogType.Error, "CraftingUI requires its authored CraftingMenu view, crafting service, and inventory.");
        IEnumerator presentation = harness.Controller.PresentCoroutine(harness.Service, harness.Inventory, () => closed++);
        Assert.That(presentation.MoveNext(), Is.False);
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(harness.Controller.IsVisible, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty);
    }

    [TestCase("slot button")]
    [TestCase("material amount")]
    [TestCase("candidate icon")]
    [TestCase("upgrade label")]
    [TestCase("advanced description")]
    public void MissingCraftingNestedReference_FailsBeforeBindingOrPausing(string missing)
    {
        CraftingHarness harness = CreateCraftingHarness(4);
        int[] hierarchy = Hierarchy(harness.View);
        switch (missing)
        {
            case "slot button": harness.View.Slots[1].Button = null; break;
            case "material amount": harness.View.Materials[2].AmountText = null; break;
            case "candidate icon": harness.View.Candidates[0].Icon = null; break;
            case "upgrade label": harness.View.UpgradeStatLabels[1] = null; break;
            case "advanced description": harness.View.AdvancedDescriptionText = null; break;
        }
        Assert.That(harness.View.IsConfigured, Is.False);
        int closed = 0;
        LogAssert.Expect(LogType.Error, "CraftingUI requires its authored CraftingMenu view, crafting service, and inventory.");
        IEnumerator presentation = harness.Controller.PresentCoroutine(harness.Service, harness.Inventory, () => closed++);
        Assert.That(presentation.MoveNext(), Is.False);
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(harness.Controller.IsVisible, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(Hierarchy(harness.View), Is.EqualTo(hierarchy));
    }

    [TestCase("Flamethrower")]
    [TestCase("AutomaticCannon")]
    [TestCase("RocketLauncher")]
    [TestCase("Mortar")]
    [TestCase("RotatingBlade")]
    public void ProductionTuningFallback_ShowsConfiguredLevelsAndPathsWithoutGameplaySideEffects(string assetName)
    {
        WeaponData source = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/" + assetName + ".asset");
        Assert.That(source, Is.Not.Null);
        // Exercise the supported legacy-asset case even if balance rows are imported later.
        WeaponData copy = Track(Object.Instantiate(source));
        copy.BalanceStats.Clear();
        string before = EditorJsonUtility.ToJson(copy);
        GameObject statsOwner = Track(new GameObject("Neutral stats for configured ammo comparison"));
        statsOwner.SetActive(false);
        PlayerStats neutralStats = statsOwner.AddComponent<PlayerStats>();
        int combatEvents = 0;
        Action<WeaponDamageRoll> onDamage = _ => combatEvents++;
        WeaponDamageResolver.OnDamageResolved += onDamage;
        Random.State randomBefore = Random.state;
        try
        {
            var configurations = new[]
            {
                new WeaponInstance { Data = copy, Level = 4 },
                new WeaponInstance { Data = copy, Level = 6, SelectedPath = WeaponUpgradePath.PathA },
                new WeaponInstance { Data = copy, Level = 10, SelectedPath = WeaponUpgradePath.PathB }
            };
            foreach (WeaponInstance preview in configurations)
            {
                float expectedDamage = Mathf.Max(0f, copy.BaseDamage)
                    * WeaponDamageResolver.GetLevelDamageMultiplier(preview) * WeaponDamageResolver.GetPathDamageMultiplier(preview);
                float expectedAmmo = WeaponMath.GetMaxManualAmmo(preview, neutralStats);
                string rangeId = copy.WeaponType == WeaponType.RotatingBlade ? "Configured orbit radius" : "Auto mode range (m)";
                float expectedRange = copy.WeaponType == WeaponType.RotatingBlade ? copy.RotatingBlade.BladeOrbitRadius : copy.BaseRange;
                Assert.That(CraftingUI.FormatTuning(copy, "Damage", preview.Level, preview.SelectedPath), Is.EqualTo(Format(expectedDamage)));
                Assert.That(CraftingUI.FormatTuning(copy, "Manual ammo", preview.Level, preview.SelectedPath), Is.EqualTo(Format(expectedAmmo)));
                Assert.That(CraftingUI.FormatTuning(copy, rangeId, preview.Level, preview.SelectedPath), Is.EqualTo(Format(expectedRange)));
            }
            Assert.That(Random.state, Is.EqualTo(randomBefore));
            Assert.That(combatEvents, Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(copy), Is.EqualTo(before));
        }
        finally { WeaponDamageResolver.OnDamageResolved -= onDamage; }
    }

    [Test]
    public void LegacyBladePreview_LabelsOrbitRadiusAndRespectsPathAmmoOverride()
    {
        CraftingHarness harness = CreateCraftingHarness(6);
        WeaponInstance weapon = harness.Weapons[0];
        weapon.Data.WeaponType = WeaponType.RotatingBlade;
        weapon.SelectedPath = WeaponUpgradePath.PathB;
        weapon.Data.BaseManualAmmo = 100f;
        weapon.Data.PathB.ManualAmmoOverride = 40f;
        weapon.Data.PathB.LevelData.Add(new WeaponLevelData { Level = 6, ManualAmmoMultiplier = 2f });
        weapon.Data.PathB.LevelData.Add(new WeaponLevelData { Level = 7, ManualAmmoMultiplier = 3f });
        Open(harness);
        Assert.That(harness.View.UpgradeStatLabels[1].text, Is.EqualTo("Auto orbit radius"));
        Assert.That(harness.View.UpgradeStatValues[1].text, Is.EqualTo("2.2 → 2.2 m"));
        Assert.That(harness.View.UpgradeStatValues[2].text, Is.EqualTo("40 → 40"));
    }

    private LevelUpChoiceUI CreateChoiceController(ChoiceMenuView level, ChoiceMenuView weapons)
    {
        GameObject owner = Track(new GameObject("Choice controller test"));
        owner.SetActive(false);
        LevelUpChoiceUI controller = owner.AddComponent<LevelUpChoiceUI>();
        SetField(controller, "_levelUpView", level);
        SetField(controller, "_weaponSelectionView", weapons);
        level?.Hide();
        weapons?.Hide();
        return controller;
    }

    private CraftingHarness CreateCraftingHarness(int firstLevel, int? secondLevel = null, bool createView = true)
    {
        CraftingMenuView view = createView ? InstantiateView<CraftingMenuView>("CraftingMenu") : null;
        GameObject owner = Track(new GameObject("Crafting controller test"));
        owner.SetActive(false);
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
        WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
        CraftingUI controller = owner.AddComponent<CraftingUI>();
        SetField(controller, "_view", view);
        SetField(service, "_weaponManager", manager);
        SetField(service, "_inventory", inventory);
        service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(
            "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
        var equipped = GetField<List<IWeaponBehaviour>>(manager, "_equipped");
        var weapons = new List<WeaponInstance>();
        foreach (int level in secondLevel.HasValue ? new[] { firstLevel, secondLevel.Value } : new[] { firstLevel })
        {
            var runtime = new WeaponInstance { Data = CreateWeapon($"Test weapon {weapons.Count + 1}"), Level = level };
            weapons.Add(runtime);
            equipped.Add(new StubWeapon(runtime));
        }
        foreach (MaterialType material in Enum.GetValues(typeof(MaterialType))) inventory.Add(material, 100);
        return new CraftingHarness(view, controller, service, inventory, weapons);
    }

    private WeaponData CreateWeapon(string name)
    {
        WeaponData weapon = Track(ScriptableObject.CreateInstance<WeaponData>());
        weapon.WeaponId = name;
        weapon.DisplayName = name;
        weapon.PathA = new WeaponUpgradePathData { PathName = "Test path A" };
        weapon.PathB = new WeaponUpgradePathData { PathName = "Test path B" };
        return weapon;
    }

    private static void Open(CraftingHarness harness)
    {
        IEnumerator presentation = harness.Controller.PresentCoroutine(harness.Service, harness.Inventory, () => { });
        Assert.That(presentation.MoveNext(), Is.True);
        Assert.That(harness.Controller.IsVisible, Is.True);
    }

    private static void AddRow(WeaponData weapon, string id, int level, float value) => weapon.BalanceStats.Add(
        new WeaponBalanceStatRow { StatId = id, Level = level, Value = value, Zone = WeaponBalanceZone.Basic });

    private static string PathName(WeaponData weapon, WeaponUpgradePath path) =>
        path == WeaponUpgradePath.PathA ? weapon.PathA.PathName : weapon.PathB.PathName;

    private static string CostText(IReadOnlyList<MaterialCost> costs) => costs.Count == 0 ? "Free" :
        string.Join(" + ", costs.Select(cost => cost.Amount.ToString(CultureInfo.InvariantCulture) + " " + MaterialCatalog.GetDisplayName(cost.Material)));

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static GameObject LoadPrefab(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab");
        Assert.That(prefab, Is.Not.Null, "Missing authored menu: " + name);
        return prefab;
    }

    private T InstantiateView<T>(string name) where T : Component =>
        Track((GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab(name))).GetComponent<T>();

    private static int[] Hierarchy(Component root) => root.GetComponentsInChildren<Transform>(true)
        .Select(item => item.GetInstanceID()).OrderBy(id => id).ToArray();

    private static void AssertFonts(GameObject root)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) Assert.That(text.font, Is.EqualTo(font), text.name);
    }

    private static void AssertObjectReferences(object owner)
    {
        Assert.That(owner, Is.Not.Null);
        foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (typeof(Object).IsAssignableFrom(field.FieldType))
                Assert.That(field.GetValue(owner), Is.Not.Null, owner.GetType().Name + "." + field.Name);
            else if (field.FieldType.IsArray)
            {
                var values = (Array)field.GetValue(owner);
                Assert.That(values, Is.Not.Null, field.Name);
                foreach (object value in values) Assert.That(value, Is.Not.Null, field.Name);
            }
        }
    }

    private static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void SetSaveManager(SaveManager value) => typeof(SaveManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }

    private sealed class CraftingHarness
    {
        public readonly CraftingMenuView View;
        public readonly CraftingUI Controller;
        public readonly WeaponCraftingService Service;
        public readonly MaterialInventory Inventory;
        public readonly List<WeaponInstance> Weapons;
        public CraftingHarness(CraftingMenuView view, CraftingUI controller, WeaponCraftingService service,
            MaterialInventory inventory, List<WeaponInstance> weapons)
        { View = view; Controller = controller; Service = service; Inventory = inventory; Weapons = weapons; }
    }

    private sealed class StubWeapon : IWeaponBehaviour
    {
        public WeaponInstance Runtime { get; }
        public StubWeapon(WeaponInstance runtime) => Runtime = runtime;
        public void Setup(WeaponInstance instance, Transform owner, PlayerStats stats, HeatManager heat) { }
        public void TickAutomatic(float deltaTime, Vector3 aimDirection) { }
        public void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring) { }
        public void UseActiveAbility(Vector3 aimDirection) { }
        public bool CanCrit() => false;
    }
}
