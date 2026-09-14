using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class AdvancedTinkeringTests
{
    private static readonly MaterialType[] RareMaterials =
    {
        MaterialType.JellifiedFuel, MaterialType.PlasticExplosive, MaterialType.Wiring
    };

    private readonly List<Object> _cleanup = new();
    private Random.State _randomState;
    private SaveManager _previousSaveManager;

    [SetUp]
    public void SetUp()
    {
        _randomState = Random.state;
        _previousSaveManager = SaveManager.Instance;
        SetSaveManager(null);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }

        _cleanup.Clear();
        SetSaveManager(_previousSaveManager);
        Random.state = _randomState;
    }

    [Test]
    public void Offer_CanChooseEitherUnlockedPath()
    {
        var offeredPaths = new HashSet<WeaponUpgradePath>();
        for (int seed = 0; seed < 32 && offeredPaths.Count < 2; seed++)
        {
            Random.InitState(seed);
            Harness harness = CreateHarness();
            Assert.That(harness.Service.TryGetAdvancedOffer(harness.Weapons[0].Data, out WeaponUpgradePath path), Is.True);
            offeredPaths.Add(path);
        }

        Assert.That(offeredPaths, Is.EquivalentTo(new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB }));
    }

    [Test]
    public void Offer_ReopeningPreservesPathRandomStateAndInventory()
    {
        Harness harness = CreateHarness();
        WeaponData weapon = harness.Weapons[0].Data;
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath first), Is.True);
        Random.State afterOffer = Random.state;
        int inventoryChanges = 0;
        harness.Inventory.OnInventoryChanged += () => inventoryChanges++;

        for (int i = 0; i < 10; i++)
        {
            Assert.That(harness.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath reopened), Is.True);
            Assert.That(reopened, Is.EqualTo(first));
        }

        Assert.That(Random.state, Is.EqualTo(afterOffer));
        AssertRareAmounts(harness, 100);
        Assert.That(inventoryChanges, Is.Zero);
        Assert.That(harness.Service.CanRejectAdvancedOffer(weapon), Is.True);
    }

    [TestCase(4, WeaponUpgradePath.None)]
    [TestCase(6, WeaponUpgradePath.None)]
    [TestCase(10, WeaponUpgradePath.PathA)]
    [TestCase(5, WeaponUpgradePath.PathA)]
    public void Offer_RejectsIneligibleRuntimeWithoutRolling(int level, WeaponUpgradePath selectedPath)
    {
        Harness harness = CreateHarness();
        WeaponInstance weapon = harness.Weapons[0];
        weapon.Level = level;
        weapon.SelectedPath = selectedPath;
        Random.State before = Random.state;

        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out _), Is.False);
        Assert.That(harness.Service.CanRejectAdvancedOffer(weapon.Data), Is.False);
        Assert.That(Random.state, Is.EqualTo(before));
        AssertRareAmounts(harness, 100);
    }

    [Test]
    public void Offer_RejectsNullAndUnequippedWeapons()
    {
        Harness harness = CreateHarness();
        WeaponData unequipped = CreateWeapon("Unequipped");
        Assert.That(harness.Service.TryGetAdvancedOffer(null, out _), Is.False);
        Assert.That(harness.Service.TryGetAdvancedOffer(unequipped, out _), Is.False);
        Assert.That(harness.Service.CanRejectAdvancedOffer(null), Is.False);
        Assert.That(harness.Service.CanRejectAdvancedOffer(unequipped), Is.False);
    }

    [Test]
    public void Price_TracksAdvancementOrderInsteadOfEquipmentSlot()
    {
        Harness harness = CreateHarness(3);
        int[] order = { 2, 0, 1 };
        int[] costs = { 5, 15, 30 };
        int remaining = 100;

        for (int i = 0; i < order.Length; i++)
        {
            WeaponInstance weapon = harness.Weapons[order[i]];
            AssertRareCost(harness.Service.GetAdvancedTinkeringCost(weapon.Data), costs[i]);
            Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath path), Is.True);
            Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, path, true).Success, Is.True);
            remaining -= costs[i];
            AssertRareAmounts(harness, remaining);
            Assert.That(weapon.Level, Is.EqualTo(6));
            Assert.That(weapon.SelectedPath, Is.EqualTo(path));
        }
    }

    [Test]
    public void Price_DoesNotCountLevelSixWithoutAnAdvancedPath()
    {
        Harness harness = CreateHarness(2);
        harness.Weapons[0].Level = 6;
        harness.Weapons[0].SelectedPath = WeaponUpgradePath.None;

        AssertRareCost(harness.Service.GetAdvancedTinkeringCost(harness.Weapons[1].Data), 5);
    }

    [TestCase(WeaponUpgradePath.PathA, WeaponUpgradePath.PathB)]
    [TestCase(WeaponUpgradePath.PathB, WeaponUpgradePath.PathA)]
    public void Reject_GuaranteesActualOppositeAndChargesEachActionOnce(
        WeaponUpgradePath first, WeaponUpgradePath alternate)
    {
        Harness harness = CreateHarnessOffering(first);
        WeaponInstance weapon = harness.Weapons[0];
        int inventoryChanges = 0;
        harness.Inventory.OnInventoryChanged += () => inventoryChanges++;

        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, first, false).Success, Is.True);
        AssertRareAmounts(harness, 95);
        AssertUnadvanced(weapon);
        Assert.That(harness.Service.WasAdvancedRejected(weapon.Data), Is.True);
        Assert.That(harness.Service.TryGetGuaranteedPath(weapon.Data, out WeaponUpgradePath guaranteed), Is.True);
        Assert.That(guaranteed, Is.EqualTo(alternate));
        AssertRareCost(harness.Service.GetAdvancedTinkeringCost(weapon.Data), 8);

        Random.State afterRejection = Random.state;
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath next), Is.True);
        Assert.That(next, Is.EqualTo(alternate));
        Assert.That(Random.state, Is.EqualTo(afterRejection));
        Assert.That(harness.Service.CanRejectAdvancedOffer(weapon.Data), Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, next, true).Success, Is.True);

        AssertRareAmounts(harness, 87);
        Assert.That(inventoryChanges, Is.EqualTo(2));
        Assert.That(weapon.Level, Is.EqualTo(6));
        Assert.That(weapon.SelectedPath, Is.EqualTo(alternate));
        Assert.That(harness.Service.WasAdvancedRejected(weapon.Data), Is.False);
        Assert.That(harness.Service.TryGetGuaranteedPath(weapon.Data, out _), Is.False);
    }

    [TestCase(0, 5, 8)]
    [TestCase(1, 15, 22)]
    [TestCase(2, 30, 45)]
    public void RejectedPrice_UsesNumberOfAdvancedWeapons(int advancedCount, int normalPrice, int rejectedPrice)
    {
        Harness harness = CreateHarness(3);
        for (int i = 0; i < advancedCount; i++)
        {
            harness.Weapons[i].Level = 6;
            harness.Weapons[i].SelectedPath = WeaponUpgradePath.PathA;
        }

        WeaponData weapon = harness.Weapons[2].Data;
        AssertRareCost(harness.Service.GetAdvancedTinkeringCost(weapon), normalPrice);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath offered), Is.True);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon, offered, false).Success, Is.True);
        AssertRareCost(harness.Service.GetAdvancedTinkeringCost(weapon), rejectedPrice);
        AssertRareAmounts(harness, 100 - normalPrice);
    }

    [TestCase(false, 15, 80)]
    [TestCase(true, 22, 68)]
    public void PendingOffer_RepricesWhenAnotherWeaponAdvances(bool rejectFirst, int updatedPrice, int remaining)
    {
        Harness harness = CreateHarness(2);
        WeaponData pending = harness.Weapons[0].Data;
        WeaponData other = harness.Weapons[1].Data;
        Assert.That(harness.Service.TryGetAdvancedOffer(pending, out WeaponUpgradePath pendingPath), Is.True);
        if (rejectFirst)
        {
            Assert.That(harness.Service.TryAdvancedTinkering(pending, pendingPath, false).Success, Is.True);
            Assert.That(harness.Service.TryGetAdvancedOffer(pending, out pendingPath), Is.True);
        }

        Assert.That(harness.Service.TryGetAdvancedOffer(other, out WeaponUpgradePath otherPath), Is.True);
        Assert.That(harness.Service.TryAdvancedTinkering(other, otherPath, true).Success, Is.True);

        AssertRareCost(harness.Service.GetAdvancedTinkeringCost(pending), updatedPrice);
        Assert.That(harness.Service.TryGetAdvancedOffer(pending, out WeaponUpgradePath stillOffered), Is.True);
        Assert.That(stillOffered, Is.EqualTo(pendingPath));
        Assert.That(harness.Service.TryAdvancedTinkering(pending, pendingPath, true).Success, Is.True);
        AssertRareAmounts(harness, remaining);
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void Action_RequiresAnExistingOfferBeforeSpending(WeaponUpgradePath path)
    {
        Harness harness = CreateHarness();
        WeaponInstance weapon = harness.Weapons[0];
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, path, true).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, path, false).Success, Is.False);
        AssertRareAmounts(harness, 100);
        AssertUnadvanced(weapon);
        Assert.That(harness.Service.WasAdvancedRejected(weapon.Data), Is.False);
    }

    [Test]
    public void Action_RejectsUnOfferedPathWithoutSpendingOrChangingOffer()
    {
        Harness harness = CreateHarness();
        WeaponInstance weapon = harness.Weapons[0];
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath offered), Is.True);
        WeaponUpgradePath other = offered == WeaponUpgradePath.PathA ? WeaponUpgradePath.PathB : WeaponUpgradePath.PathA;

        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, other, true).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, other, false).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, WeaponUpgradePath.None, true).Success, Is.False);
        AssertRareAmounts(harness, 100);
        AssertUnadvanced(weapon);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath retained), Is.True);
        Assert.That(retained, Is.EqualTo(offered));
    }

    [Test]
    public void GuaranteedOffer_CannotBeRejectedAgainOrAcceptPreviouslyRejectedPath()
    {
        Harness harness = CreateHarness();
        WeaponInstance weapon = harness.Weapons[0];
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath first), Is.True);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, first, false).Success, Is.True);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath alternate), Is.True);

        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, alternate, false).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, first, false).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, first, true).Success, Is.False);
        AssertRareAmounts(harness, 95);
        AssertUnadvanced(weapon);
        Assert.That(harness.Service.TryGetGuaranteedPath(weapon.Data, out WeaponUpgradePath retained), Is.True);
        Assert.That(retained, Is.EqualTo(alternate));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InsufficientMaterials_PreserveAllAmountsAndOfferState(bool accept)
    {
        Harness harness = CreateHarness(1, 4);
        harness.Inventory.Add(MaterialType.JellifiedFuel, 96);
        harness.Inventory.Add(MaterialType.PlasticExplosive, 96);
        WeaponInstance weapon = harness.Weapons[0];
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath offered), Is.True);
        int inventoryChanges = 0;
        harness.Inventory.OnInventoryChanged += () => inventoryChanges++;

        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, offered, accept).Success, Is.False);
        Assert.That(harness.Inventory.GetAmount(MaterialType.JellifiedFuel), Is.EqualTo(100));
        Assert.That(harness.Inventory.GetAmount(MaterialType.PlasticExplosive), Is.EqualTo(100));
        Assert.That(harness.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(4));
        Assert.That(inventoryChanges, Is.Zero);
        AssertUnadvanced(weapon);
        Assert.That(harness.Service.WasAdvancedRejected(weapon.Data), Is.False);
        Assert.That(harness.Service.TryGetGuaranteedPath(weapon.Data, out _), Is.False);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath retained), Is.True);
        Assert.That(retained, Is.EqualTo(offered));
    }

    [Test]
    public void AcceptedOffer_CannotBeChargedAgain()
    {
        Harness harness = CreateHarness();
        WeaponData weapon = harness.Weapons[0].Data;
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath offered), Is.True);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon, offered, true).Success, Is.True);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon, offered, true).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon, offered, false).Success, Is.False);
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon, out _), Is.False);
        AssertRareAmounts(harness, 95);
    }

    [Test]
    public void LockedPathB_OnlyOffersAAndDisablesRejection()
    {
        CreateSaveManager();
        Harness harness = CreateHarness();
        WeaponInstance weapon = harness.Weapons[0];
        Assert.That(harness.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath offered), Is.True);
        Assert.That(offered, Is.EqualTo(WeaponUpgradePath.PathA));
        Assert.That(harness.Service.CanRejectAdvancedOffer(weapon.Data), Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, offered, false).Success, Is.False);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, WeaponUpgradePath.PathB, true).Success, Is.False);
        AssertRareAmounts(harness, 100);
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, offered, true).Success, Is.True);
        Assert.That(weapon.SelectedPath, Is.EqualTo(WeaponUpgradePath.PathA));
        AssertRareAmounts(harness, 95);
    }

    [Test]
    public void PathLockedAfterOffer_CannotBeAcceptedOrCharged()
    {
        Harness harness = CreateHarnessOffering(WeaponUpgradePath.PathB);
        WeaponInstance weapon = harness.Weapons[0];
        CreateSaveManager();
        Assert.That(harness.Service.TryAdvancedTinkering(weapon.Data, WeaponUpgradePath.PathB, true).Success, Is.False);
        AssertRareAmounts(harness, 100);
        AssertUnadvanced(weapon);
    }

    private Harness CreateHarnessOffering(WeaponUpgradePath desired)
    {
        // Search a fixed seed set through the public API, without depending on its random-index mapping.
        for (int seed = 0; seed < 32; seed++)
        {
            Random.InitState(seed);
            Harness harness = CreateHarness();
            Assert.That(harness.Service.TryGetAdvancedOffer(harness.Weapons[0].Data, out WeaponUpgradePath offered), Is.True);
            if (offered == desired)
                return harness;
        }

        Assert.Fail($"No seeded offer selected {desired}.");
        return null;
    }

    private Harness CreateHarness(int weaponCount = 1, int rareAmount = 100)
    {
        GameObject owner = Track(new GameObject("Advanced Tinkering Test"));
        owner.SetActive(false);
        // Inactive objects avoid gameplay Awake/OnEnable, including persistent save I/O.
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
        WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
        SetField(service, "_weaponManager", manager);
        SetField(service, "_inventory", inventory);
        var equipped = (List<IWeaponBehaviour>)typeof(WeaponManager)
            .GetField("_equipped", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        var weapons = new List<WeaponInstance>();
        for (int i = 0; i < weaponCount; i++)
        {
            WeaponInstance weapon = new() { Data = CreateWeapon($"Weapon {i}"), Level = 5 };
            weapons.Add(weapon);
            equipped.Add(new StubWeapon(weapon));
        }

        foreach (MaterialType material in RareMaterials)
            inventory.Add(material, rareAmount);
        return new Harness(service, inventory, weapons);
    }

    private WeaponData CreateWeapon(string id)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.name = id;
        data.WeaponId = id;
        data.DisplayName = id;
        return data;
    }

    private void CreateSaveManager()
    {
        GameObject owner = Track(new GameObject("In-memory SaveManager"));
        owner.SetActive(false);
        SaveManager save = owner.AddComponent<SaveManager>();
        SetField(save, "_data", new SaveData());
        SetSaveManager(save);
    }

    private static void AssertRareCost(IReadOnlyList<MaterialCost> costs, int amount)
    {
        Assert.That(costs.Count, Is.EqualTo(RareMaterials.Length));
        var materials = new List<MaterialType>();
        foreach (MaterialCost cost in costs)
        {
            materials.Add(cost.Material);
            Assert.That(cost.Amount, Is.EqualTo(amount), cost.Material.ToString());
        }
        Assert.That(materials, Is.EquivalentTo(RareMaterials));
    }

    private static void AssertRareAmounts(Harness harness, int amount)
    {
        foreach (MaterialType material in RareMaterials)
            Assert.That(harness.Inventory.GetAmount(material), Is.EqualTo(amount), material.ToString());
    }

    private static void AssertUnadvanced(WeaponInstance weapon)
    {
        Assert.That(weapon.Level, Is.EqualTo(5));
        Assert.That(weapon.SelectedPath, Is.EqualTo(WeaponUpgradePath.None));
    }

    private static void SetSaveManager(SaveManager value) => typeof(SaveManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private T Track<T>(T value) where T : Object
    {
        _cleanup.Add(value);
        return value;
    }

    private sealed class Harness
    {
        public readonly WeaponCraftingService Service;
        public readonly MaterialInventory Inventory;
        public readonly List<WeaponInstance> Weapons;

        public Harness(WeaponCraftingService service, MaterialInventory inventory, List<WeaponInstance> weapons)
        {
            Service = service;
            Inventory = inventory;
            Weapons = weapons;
        }
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
