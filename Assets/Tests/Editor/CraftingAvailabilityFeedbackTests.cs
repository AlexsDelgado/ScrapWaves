using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class CraftingAvailabilityFeedbackTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _cleanup = new();
    private SaveManager _previousSave;
    private Random.State _previousRandom;
    private float _previousTimeScale;

    [SetUp]
    public void SetUp()
    {
        _previousSave = SaveManager.Instance;
        _previousRandom = Random.state;
        _previousTimeScale = Time.timeScale;
        SetSaveManager(null);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        GameplayPause.Reset();
        Time.timeScale = _previousTimeScale;
        Random.state = _previousRandom;
        SetSaveManager(_previousSave);
    }

    [TestCase("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab")]
    [TestCase("Assets/Prefabs/UI/GameplayHud V2.prefab")]
    public void AuthoredPulse_HasStrongPerimeterAndStableReadableContentWhilePaused(string path)
    {
        GameObject root = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)));
        CraftingAvailabilityView[] indicators = root.GetComponentsInChildren<CraftingAvailabilityView>(true);
        Assert.That(indicators.Length, Is.EqualTo(3));
        int[] hierarchy = root.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()).ToArray();
        var animatedGraphics = new HashSet<Graphic>();
        foreach (var indicator in indicators)
        {
            animatedGraphics.Add(indicator.Glow);
            foreach (var edge in indicator.OutlineEdges) animatedGraphics.Add(edge);
        }
        Graphic[] stableGraphics = root.GetComponentsInChildren<Graphic>(true)
            .Where(graphic => !animatedGraphics.Contains(graphic)).ToArray();
        Color[] stableColors = stableGraphics.Select(graphic => graphic.color).ToArray();

        Time.timeScale = 0f;
        foreach (CraftingAvailabilityView indicator in indicators)
        {
            indicator.Bind(CraftingActionKind.UpgradeLevel);
            float period = 2f * Mathf.PI / indicator.PulseSpeed;
            Assert.That(period, Is.InRange(1.25f, 1.7f), "The stronger pulse must be noticeable without strobing.");
            indicator.RefreshPulse(period * 0.25f);
            float low = indicator.OutlineEdges[0].color.a;
            indicator.RefreshPulse(period * 0.75f);
            Assert.That(indicator.Glow.color.a, Is.LessThanOrEqualTo(0.05f), "Do not wash out the icon or text.");
            Assert.That(indicator.OutlineEdges.Length, Is.EqualTo(4));
            foreach (Image edge in indicator.OutlineEdges)
            {
                Assert.That(edge.color.a, Is.GreaterThanOrEqualTo(0.95f));
                Assert.That(edge.color.a - low, Is.GreaterThanOrEqualTo(0.85f));
                Assert.That(Mathf.Max(edge.rectTransform.sizeDelta.x, edge.rectTransform.sizeDelta.y), Is.GreaterThanOrEqualTo(5f));
                Assert.That(edge.raycastTarget, Is.False);
            }
            Assert.That(indicator.Glow.raycastTarget, Is.False);
            Assert.That(indicator.Marker.raycastTarget, Is.False);
            for (int i = 0; i < 8; i++)
            {
                indicator.Bind(null);
                Assert.That(indicator.Glow.gameObject.activeSelf, Is.False);
                Assert.That(indicator.Marker.gameObject.activeSelf, Is.False);
                indicator.Bind(CraftingActionKind.TinkerNewWeapon);
                Assert.That(indicator.Marker.text, Is.EqualTo("+"));
            }
        }
        Assert.That(stableGraphics.Select(graphic => graphic.color), Is.EqualTo(stableColors),
            "Pulse only the authored fill/edges; keep labels, icons, symbols and selection colors steady.");
        Assert.That(root.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()), Is.EqualTo(hierarchy));
    }

    [TestCase(1)]
    [TestCase(5)]
    public void MenuAffordability_TracksEveryCostAndReopenWithoutCreatingUi(int level)
    {
        Harness harness = CreateHarness(level);
        IReadOnlyList<MaterialCost> costs = level == 5
            ? harness.Service.GetAdvancedTinkeringCost(harness.Weapon.Data)
            : harness.Service.GetUpgradeCost(harness.Weapon.Data, WeaponUpgradePath.None, level + 1);
        Assert.That(costs.Count, Is.GreaterThan(0));
        Open(harness);
        int childCount = harness.View.GetComponentsInChildren<Transform>(true).Length;
        CraftingAvailabilityView indicator = harness.View.Slots[0].Availability;
        Assert.That(indicator.Glow.gameObject.activeSelf, Is.False);
        for (int i = 0; i < costs.Count; i++)
            harness.Inventory.Add(costs[i].Material, costs[i].Amount - (i == costs.Count - 1 ? 1 : 0));
        Assert.That(indicator.Glow.gameObject.activeSelf, Is.False, "Every material must meet the price.");
        harness.Inventory.Add(costs[costs.Count - 1].Material, 1);
        Assert.That(indicator.Glow.gameObject.activeSelf, Is.True);
        Assert.That((level == 5 ? harness.View.AcceptButton : harness.View.UpgradeButton).interactable, Is.True);
        foreach (MaterialCost cost in costs)
        {
            Assert.That(harness.Inventory.TrySpend(new[] { new MaterialCost(cost.Material, 1) }), Is.True);
            Assert.That(indicator.Glow.gameObject.activeSelf, Is.False, cost.Material + " must independently meet the price.");
            Assert.That(indicator.Marker.gameObject.activeSelf, Is.False);
            Assert.That((level == 5 ? harness.View.AcceptButton : harness.View.UpgradeButton).interactable, Is.False);
            harness.Inventory.Add(cost.Material, 1);
            Assert.That(indicator.Glow.gameObject.activeSelf, Is.True);
        }
        Assert.That(harness.Inventory.TrySpend(new[] { new MaterialCost(costs[0].Material, 1) }), Is.True);
        Assert.That(indicator.Glow.gameObject.activeSelf, Is.False);
        Assert.That(indicator.Marker.gameObject.activeSelf, Is.False);
        harness.View.CloseButton.onClick.Invoke();
        harness.Inventory.Add(costs[0].Material, 1);
        Open(harness);
        Assert.That(indicator.Glow.gameObject.activeSelf, Is.True);
        Assert.That(harness.View.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(childCount));
    }

    [TestCase(1)]
    [TestCase(5)]
    public void PaidWeaponChoice_SuppressesUnavailableUpgradeSignalAcrossReopen(int level)
    {
        Harness harness = CreateHarness(level);
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType))) harness.Inventory.Add(type, 100);
        Open(harness);
        Assert.That(harness.View.Slots[0].Availability.Glow.gameObject.activeSelf, Is.True);
        Assert.That(harness.View.Slots[1].Availability.Glow.gameObject.activeSelf, Is.True);
        Assert.That(harness.View.Slots[2].Availability.Glow.gameObject.activeSelf, Is.False, "Only the next empty slot is actionable.");
        harness.View.Slots[1].Button.onClick.Invoke();
        harness.View.TinkerButton.onClick.Invoke();
        Assert.That(harness.Service.HasPendingTinkeringChoice, Is.True);
        Assert.That(harness.View.TinkerChoicePanel.activeSelf, Is.True);
        Random.State afterOffer = Random.state;
        int inventoryChanges = 0;
        harness.Inventory.OnInventoryChanged += () => inventoryChanges++;
        AssertNoAvailableSignals(harness);
        Invoke(harness.Controller, "OnDisable");
        Open(harness);
        AssertNoAvailableSignals(harness);
        Assert.That(Random.state, Is.EqualTo(afterOffer), "Availability cannot reroll the paid choice.");
        Assert.That(inventoryChanges, Is.Zero, "Availability cannot spend or refund materials.");
    }

    [Test]
    public void MaximumLevelAndUnequippedRuntime_NeverSignalAnUpgradeDespiteStock()
    {
        Harness harness = CreateHarness(10);
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType))) harness.Inventory.Add(type, 100);
        Open(harness);
        Assert.That(harness.View.Slots[0].Availability.Glow.gameObject.activeSelf, Is.False);
        Assert.That(harness.View.UpgradeButton.interactable, Is.False);
        Assert.That(harness.Service.GetAvailableAction(new WeaponInstance { Data = harness.Weapon.Data, Level = 1 }), Is.Null);
    }

    private static void AssertNoAvailableSignals(Harness harness)
    {
        Assert.That(harness.Service.GetAvailableAction(harness.Weapon), Is.Null);
        Assert.That(harness.Service.HasAnyAvailableCraftingAction(), Is.False);
        foreach (CraftingWeaponSlotView slot in harness.View.Slots)
        {
            Assert.That(slot.Availability.Glow.gameObject.activeSelf, Is.False);
            Assert.That(slot.Availability.Marker.gameObject.activeSelf, Is.False);
            Assert.That(slot.Button.interactable, Is.False);
        }
    }

    private Harness CreateHarness(int level)
    {
        GameObject menu = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab")));
        GameObject player = Track(new GameObject("Availability feedback fixture"));
        player.SetActive(false);
        var manager = player.AddComponent<WeaponManager>();
        var inventory = player.AddComponent<MaterialInventory>();
        var service = player.AddComponent<WeaponCraftingService>();
        var controller = player.AddComponent<CraftingUI>();
        var view = menu.GetComponent<CraftingMenuView>();
        Set(controller, "_view", view);
        Set(service, "_weaponManager", manager);
        Set(service, "_inventory", inventory);
        service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
        var weapon = new WeaponInstance { Data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset"), Level = level };
        ((List<IWeaponBehaviour>)typeof(WeaponManager).GetField("_equipped", PrivateInstance).GetValue(manager)).Add(new StubWeapon(weapon));
        Set(service, "_weaponPool", new List<WeaponData>
        {
            AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset"),
            AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset")
        });
        return new Harness { View = view, Controller = controller, Inventory = inventory, Service = service, Weapon = weapon };
    }

    private static void Open(Harness harness) => Assert.That(harness.Controller.PresentCoroutine(harness.Service, harness.Inventory, () => { }).MoveNext(), Is.True);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null);
    private static void SetSaveManager(SaveManager value) => typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }

    private sealed class Harness
    {
        public CraftingMenuView View;
        public CraftingUI Controller;
        public MaterialInventory Inventory;
        public WeaponCraftingService Service;
        public WeaponInstance Weapon;
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
