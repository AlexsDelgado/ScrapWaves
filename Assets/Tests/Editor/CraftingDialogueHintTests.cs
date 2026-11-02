using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class CraftingDialogueHintTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _cleanup = new();
    private SaveManager _previousSave;

    [SetUp]
    public void SetUp()
    {
        _previousSave = SaveManager.Instance;
        SetSave(null);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        SetSave(_previousSave);
    }

    [TestCase(1)]
    [TestCase(5)]
    public void DueCraftingText_RequiresAnAffordableWeaponUpgrade(int level)
    {
        Harness h = Create(level);
        Call(h.Director, "EvaluateGuides");
        Assert.That(Queue(h).Count, Is.Zero);
        Fund(h, level == 5 ? h.Service.GetAdvancedTinkeringCost(h.Weapon.Data)
            : h.Service.GetUpgradeCost(h.Weapon.Data, WeaponUpgradePath.None, level + 1));
        Call(h.Director, "EvaluateGuides");
        Assert.That(Queue(h).Count, Is.EqualTo(1));
        Assert.That(h.Service.GetAvailableAction(h.Weapon), Is.EqualTo(level == 5
            ? CraftingActionKind.AdvancedTinkering : CraftingActionKind.UpgradeLevel));
    }

    [Test]
    public void QueuedText_RechecksResourcesBeforeDisplayAndWaitsForAffordability()
    {
        Harness h = Create();
        IReadOnlyList<MaterialCost> cost = h.Service.GetUpgradeCost(h.Weapon.Data, WeaponUpgradePath.None, 2);
        Fund(h, cost);
        h.Director.Play(h.Entry);
        Assert.That(Queue(h).Count, Is.EqualTo(1));
        h.Inventory.TrySpend(new[] { new MaterialCost(cost[0].Material, 1) });
        Call(h.Director, "PumpQueue");
        Assert.That(Queue(h).Count, Is.EqualTo(1), "The delayed hint must not be consumed while unavailable.");
        Assert.That(h.Box.CurrentEntry, Is.Null);
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.False);
        h.Inventory.Add(cost[0].Material, 1);
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.True);
    }

    [Test]
    public void StockOnlyQualifiesWhenAnUpgradeOrNewWeaponFitsTheExistingRules()
    {
        Harness h = Create();
        foreach (MaterialType material in Enum.GetValues(typeof(MaterialType))) h.Inventory.Add(material, 1000);
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.True);
        h.Weapon.Level = 10;
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.False, "A maximum-level weapon has no upgrade.");
        Set(h.Service, "_weaponPool", new List<WeaponData>
        {
            AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset"),
            AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset")
        });
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.True, "New-weapon tinkering actually crafts a weapon.");
        var equipped = Get<List<IWeaponBehaviour>>(h.Manager, "_equipped");
        equipped.Add(new StubWeapon(new WeaponInstance { Data = h.Weapon.Data, Level = 10 }));
        equipped.Add(new StubWeapon(new WeaponInstance { Data = h.Weapon.Data, Level = 10 }));
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.False, "Full slots prevent the new-weapon craft.");
        equipped.Clear();
        Set(h.Service, "_weaponPool", new List<WeaponData>());
        Assert.That((bool)Call(h.Director, "CanShow", h.Entry), Is.False, "An unequipped weapon cannot be upgraded.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VisibleHint_HidesWhenResourcesOrWeaponEligibilityDisappear(bool loseEligibility)
    {
        Harness h = Create();
        IReadOnlyList<MaterialCost> cost = h.Service.GetUpgradeCost(h.Weapon.Data, WeaponUpgradePath.None, 2);
        Fund(h, cost);
        Set(h.Box, "<CurrentEntry>k__BackingField", h.Entry);
        h.Group.alpha = 1f;
        Call(h.Director, "RefreshCraftingHint");
        Assert.That(h.Group.alpha, Is.EqualTo(1f));
        if (loseEligibility) h.Weapon.Level = 10;
        else h.Inventory.TrySpend(new[] { new MaterialCost(cost[0].Material, 1) });
        Call(h.Director, "RefreshCraftingHint");
        Assert.That(h.Box.CurrentEntry, Is.Null);
        Assert.That(h.Group.alpha, Is.Zero);
        Assert.That(Get<float>(h.Director, "_lastDialogueEndedAt"), Is.EqualTo(61f));
    }

    [Test]
    public void UnavailableCraftingHint_DoesNotBlockOrDismissUnrelatedDialogue()
    {
        Harness h = Create();
        Fund(h, h.Service.GetUpgradeCost(h.Weapon.Data, WeaponUpgradePath.None, 2));
        h.Director.Play(h.Entry);
        h.Weapon.Level = 10;
        var noKills = Track(ScriptableObject.CreateInstance<DialogueEntry>());
        Set(noKills, "_trigger", DialogueTrigger.NoKillsFor);
        Get<List<DialogueEntry>>(h.Set, "_entries").Add(noKills);
        Set(h.Director, "_noKillTime", 100f);
        Call(h.Director, "EvaluateGuides");
        Assert.That(Queue(h).Count, Is.EqualTo(2));
        Set(h.Box, "<CurrentEntry>k__BackingField", noKills);
        h.Group.alpha = 1f;
        Call(h.Director, "RefreshCraftingHint");
        Assert.That(h.Box.CurrentEntry, Is.SameAs(noKills));
        Assert.That(h.Group.alpha, Is.EqualTo(1f));
    }

    private Harness Create(int level = 1)
    {
        var root = Track(new GameObject("Crafting dialogue fixture"));
        root.SetActive(false);
        var manager = root.AddComponent<WeaponManager>();
        var inventory = root.AddComponent<MaterialInventory>();
        var service = root.AddComponent<WeaponCraftingService>();
        Set(service, "_weaponManager", manager);
        Set(service, "_inventory", inventory);
        service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
        var weapon = new WeaponInstance
        {
            Data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset"),
            Level = level
        };
        Get<List<IWeaponBehaviour>>(manager, "_equipped").Add(new StubWeapon(weapon));
        var group = root.AddComponent<CanvasGroup>();
        var box = root.AddComponent<DialogueBoxUI>();
        Set(box, "_group", group);
        var director = root.AddComponent<DialogueDirector>();
        Set(director, "_weaponManager", manager);
        Set(director, "_crafting", service);
        Set(director, "_box", box);
        Set(director, "_runTime", 61f);
        var entry = AssetDatabase.LoadAssetAtPath<DialogueEntry>("Assets/ScriptableObjects/Dialogue/Dialogue_Stalker_Guide_Crafting.asset");
        Assert.That(entry, Is.Not.Null);
        var set = Track(ScriptableObject.CreateInstance<DialogueSet>());
        Set(set, "_entries", new List<DialogueEntry> { entry });
        Set(director, "_set", set);
        return new Harness { Manager = manager, Inventory = inventory, Service = service, Weapon = weapon,
            Box = box, Director = director, Group = group, Entry = entry, Set = set };
    }

    private static void Fund(Harness h, IReadOnlyList<MaterialCost> cost)
    {
        foreach (MaterialCost item in cost) h.Inventory.Add(item.Material, item.Amount);
    }
    private static IList Queue(Harness h) => Get<IList>(h.Director, "_queue");
    private static void Set(object o, string field, object value) => o.GetType().GetField(field, Fields).SetValue(o, value);
    private static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Fields).GetValue(o);
    private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Fields).Invoke(o, args);
    private static void SetSave(SaveManager value) => typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private sealed class Harness
    {
        public WeaponManager Manager; public MaterialInventory Inventory; public WeaponCraftingService Service;
        public WeaponInstance Weapon; public DialogueBoxUI Box; public DialogueDirector Director;
        public CanvasGroup Group; public DialogueEntry Entry; public DialogueSet Set;
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
