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
    public void UnpaidOffer_IsHiddenAndAvailabilityNeverRolls()
    {
        Harness h = CreateHarness();
        Random.State before = Random.state;
        for (int i = 0; i < 3; i++)
        {
            Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out WeaponUpgradePath path), Is.False);
            Assert.That(path, Is.EqualTo(WeaponUpgradePath.None));
            Assert.That(h.Service.GetAvailableAction(h.Weapons[0]), Is.EqualTo(CraftingActionKind.AdvancedTinkering));
            Assert.That(h.Service.HasAnyAvailableCraftingAction(), Is.True);
        }
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(h.Service.PendingAdvancedWeapon, Is.Null);
        Assert.That(h.Service.GetPendingAdvancedCost(), Is.Empty);
        Assert.That(h.Service.CanRejectAdvancedOffer(h.Weapons[0].Data), Is.False);
        Assert.That(Random.state, Is.EqualTo(before));
        AssertRareAmounts(h, 100);
    }

    [Test]
    public void Purchase_SpendsBeforeRevealingWithoutApplyingUpgrade()
    {
        Harness h = CreateHarness();
        WeaponData weapon = h.Weapons[0].Data;
        Random.State before = Random.state;
        int spent = 0;
        h.Inventory.OnMaterialsSpent += () =>
        {
            spent++;
            AssertRareAmounts(h, 95);
            Assert.That(h.Service.TryGetAdvancedOffer(weapon, out _), Is.False);
            Assert.That(Random.state, Is.EqualTo(before), "Payment must finish before the random draw.");
            Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.False, "Payment callbacks cannot reenter.");
            Assert.That(h.Service.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, true).Success, Is.False);
        };

        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(spent, Is.EqualTo(1));
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.True);
        Assert.That(h.Service.PendingAdvancedWeapon, Is.SameAs(weapon));
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath offered), Is.True);
        Assert.That(offered, Is.Not.EqualTo(WeaponUpgradePath.None));
        AssertRareCost(h.Service.GetPendingAdvancedCost(), 5);
        AssertUnadvanced(h.Weapons[0]);
    }

    [Test]
    public void PaidOffer_ReopeningAndRepeatedPurchaseDoNotChargeOrReroll()
    {
        Harness h = CreateHarness();
        WeaponData weapon = h.Weapons[0].Data;
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath first), Is.True);
        Random.State after = Random.state;
        int changes = 0;
        h.Inventory.OnInventoryChanged += () => changes++;
        h.Service.enabled = false;
        h.Service.enabled = true;
        for (int i = 0; i < 3; i++)
        {
            Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
            Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath repeated), Is.True);
            Assert.That(repeated, Is.EqualTo(first));
        }
        Assert.That(Random.state, Is.EqualTo(after));
        Assert.That(changes, Is.Zero);
        AssertRareAmounts(h, 95);
        AssertUnadvanced(h.Weapons[0]);
    }

    [Test]
    public void Purchase_CanChooseEitherUnlockedPath()
    {
        var offeredPaths = new HashSet<WeaponUpgradePath>();
        for (int seed = 0; seed < 32 && offeredPaths.Count < 2; seed++)
        {
            Random.InitState(seed);
            Harness h = CreateHarness();
            Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapons[0].Data).Success, Is.True);
            Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out WeaponUpgradePath path), Is.True);
            offeredPaths.Add(path);
        }
        Assert.That(offeredPaths, Is.EquivalentTo(new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB }));
    }

    [TestCase(4, WeaponUpgradePath.None)]
    [TestCase(6, WeaponUpgradePath.None)]
    [TestCase(10, WeaponUpgradePath.PathA)]
    [TestCase(5, WeaponUpgradePath.PathA)]
    public void Purchase_RejectsIneligibleRuntimeWithoutRollingOrSpending(int level, WeaponUpgradePath selectedPath)
    {
        Harness h = CreateHarness();
        h.Weapons[0].Level = level;
        h.Weapons[0].SelectedPath = selectedPath;
        Random.State before = Random.state;
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapons[0].Data).Success, Is.False);
        Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out _), Is.False);
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(Random.state, Is.EqualTo(before));
        AssertRareAmounts(h, 100);
    }

    [Test]
    public void Purchase_RejectsNullUnequippedAndUnaffordableWithoutReveal()
    {
        Harness h = CreateHarness(1, 4);
        h.Inventory.Add(MaterialType.JellifiedFuel, 96);
        h.Inventory.Add(MaterialType.PlasticExplosive, 96);
        Random.State before = Random.state;
        int changes = 0;
        h.Inventory.OnInventoryChanged += () => changes++;
        foreach (WeaponData weapon in new[] { null, CreateWeapon("Unequipped"), h.Weapons[0].Data })
        {
            Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.False);
            Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath path), Is.False);
            Assert.That(path, Is.EqualTo(WeaponUpgradePath.None));
        }
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(Random.state, Is.EqualTo(before));
        Assert.That(changes, Is.Zero);
        Assert.That(h.Inventory.GetAmount(MaterialType.JellifiedFuel), Is.EqualTo(100));
        Assert.That(h.Inventory.GetAmount(MaterialType.PlasticExplosive), Is.EqualTo(100));
        Assert.That(h.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(4));
        AssertUnadvanced(h.Weapons[0]);
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void Resolution_RequiresPaidMatchingOffer(WeaponUpgradePath path)
    {
        Harness h = CreateHarness();
        WeaponData weapon = h.Weapons[0].Data;
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, true).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, false).Success, Is.False);
        AssertRareAmounts(h, 100);
        AssertUnadvanced(h.Weapons[0]);
        Assert.That(h.Service.WasAdvancedRejected(weapon), Is.False);
    }

    [Test]
    public void Accept_ResolvesOnceWithoutFurtherPaymentAndRejectsWrongPath()
    {
        Harness h = CreateHarness(1, 5);
        WeaponData weapon = h.Weapons[0].Data;
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath path), Is.True);
        WeaponUpgradePath other = h.Service.GetGuaranteedAlternatePath(weapon, path);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, other, true).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, other, false).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, WeaponUpgradePath.None, true).Success, Is.False);
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.True);
        int changes = 0;
        h.Inventory.OnInventoryChanged += () => changes++;
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, true).Success, Is.True);
        Assert.That(h.Weapons[0].Level, Is.EqualTo(6));
        Assert.That(h.Weapons[0].SelectedPath, Is.EqualTo(path));
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, true).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, false).Success, Is.False);
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.False);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out _), Is.False);
        Assert.That(changes, Is.Zero);
        AssertRareAmounts(h, 0);
    }

    [TestCase(WeaponUpgradePath.PathA, WeaponUpgradePath.PathB)]
    [TestCase(WeaponUpgradePath.PathB, WeaponUpgradePath.PathA)]
    public void Decline_ConsumesPaidAttemptAndNextPurchaseGuaranteesOppositeOnce(WeaponUpgradePath first, WeaponUpgradePath alternate)
    {
        Harness h = CreateHarnessOffering(first);
        WeaponData weapon = h.Weapons[0].Data;
        int changes = 0;
        h.Inventory.OnInventoryChanged += () => changes++;
        Assert.That(h.Service.TryAdvancedTinkering(weapon, first, false).Success, Is.True);
        AssertRareAmounts(h, 95);
        AssertUnadvanced(h.Weapons[0]);
        Assert.That(changes, Is.Zero, "Decline neither refunds nor charges again.");
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out _), Is.False, "The retry stays hidden until purchased.");
        Assert.That(h.Service.WasAdvancedRejected(weapon), Is.True);
        Assert.That(h.Service.TryGetGuaranteedPath(weapon, out WeaponUpgradePath guaranteed), Is.True);
        Assert.That(guaranteed, Is.EqualTo(alternate));
        AssertRareCost(h.Service.GetAdvancedTinkeringCost(weapon), 8);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, first, false).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, alternate, true).Success, Is.False);
        Random.State before = Random.state;
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath next), Is.True);
        Assert.That(next, Is.EqualTo(alternate));
        Assert.That(Random.state, Is.EqualTo(before));
        Assert.That(h.Service.CanRejectAdvancedOffer(weapon), Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, next, false).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, first, true).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, next, true).Success, Is.True);
        AssertRareAmounts(h, 87);
        Assert.That(changes, Is.EqualTo(1), "Only the second purchase spends again.");
        Assert.That(h.Weapons[0].SelectedPath, Is.EqualTo(alternate));
        Assert.That(h.Service.WasAdvancedRejected(weapon), Is.False);
        Assert.That(h.Service.TryGetGuaranteedPath(weapon, out _), Is.False);
    }

    [Test]
    public void Price_TracksAdvancementOrderInsteadOfEquipmentSlot()
    {
        Harness h = CreateHarness(3);
        int[] order = { 2, 0, 1 }, costs = { 5, 15, 30 };
        int remaining = 100;
        for (int i = 0; i < order.Length; i++)
        {
            WeaponInstance weapon = h.Weapons[order[i]];
            AssertRareCost(h.Service.GetAdvancedTinkeringCost(weapon.Data), costs[i]);
            Assert.That(h.Service.TryBeginAdvancedTinkering(weapon.Data).Success, Is.True);
            Assert.That(h.Service.TryGetAdvancedOffer(weapon.Data, out WeaponUpgradePath path), Is.True);
            Assert.That(h.Service.TryAdvancedTinkering(weapon.Data, path, true).Success, Is.True);
            remaining -= costs[i];
            AssertRareAmounts(h, remaining);
        }
    }

    [TestCase(0, 5, 8)]
    [TestCase(1, 15, 22)]
    [TestCase(2, 30, 45)]
    public void RejectedPrice_UsesNumberOfAdvancedWeapons(int advancedCount, int normalPrice, int rejectedPrice)
    {
        Harness h = CreateHarness(3);
        for (int i = 0; i < advancedCount; i++)
        {
            h.Weapons[i].Level = 6;
            h.Weapons[i].SelectedPath = WeaponUpgradePath.PathA;
        }
        WeaponData weapon = h.Weapons[2].Data;
        AssertRareCost(h.Service.GetAdvancedTinkeringCost(weapon), normalPrice);
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath path), Is.True);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, false).Success, Is.True);
        AssertRareCost(h.Service.GetAdvancedTinkeringCost(weapon), rejectedPrice);
        AssertRareAmounts(h, 100 - normalPrice);
    }

    [Test]
    public void PaidOffer_BlocksOtherCraftingAndKeepsReservedPrice()
    {
        Harness h = CreateHarness(2);
        MaterialUsageBalanceSO balance = Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>());
        h.Service.SetMaterialBalance(balance);
        SetField(h.Service, "_weaponPool", new List<WeaponData> { CreateWeapon("A"), CreateWeapon("B") });
        foreach (MaterialType material in new[] { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears }) h.Inventory.Add(material, 100);
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapons[0].Data).Success, Is.True);
        SetField(balance, "_advancedFirstWeaponCost", 50);
        AssertRareCost(h.Service.GetPendingAdvancedCost(), 5);
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapons[1].Data).Success, Is.False);
        Assert.That(h.Service.TryBeginTinkering().Success, Is.False);
        Assert.That(h.Service.CanTinkerNewWeapon(), Is.False);
        Assert.That(h.Service.GetTinkeringOffer(), Is.Empty);
        Assert.That(h.Service.GetAvailableAction(h.Weapons[1]), Is.Null);
        Assert.That(h.Service.TryUpgradeWeapon(h.Weapons[1].Data, 7).Success, Is.False);
        Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out WeaponUpgradePath path), Is.True);
        Assert.That(h.Service.TryAdvancedTinkering(h.Weapons[0].Data, path, true).Success, Is.True);
        AssertRareAmounts(h, 95);
    }

    [Test]
    public void PaidBasicChoice_BlocksAdvancedPurchaseWithoutRolling()
    {
        Harness h = CreateHarness();
        SetField(h.Service, "_weaponPool", new List<WeaponData> { CreateWeapon("A"), CreateWeapon("B") });
        foreach (MaterialType material in new[] { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears }) h.Inventory.Add(material, 5);
        Assert.That(h.Service.TryBeginTinkering().Success, Is.True);
        Random.State before = Random.state;
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapons[0].Data).Success, Is.False);
        Assert.That(h.Service.TryGetAdvancedOffer(h.Weapons[0].Data, out _), Is.False);
        Assert.That(Random.state, Is.EqualTo(before));
        AssertRareAmounts(h, 100);
    }

    [Test]
    public void LockedPathB_OnlyOffersAAndDisablesRejection()
    {
        CreateSaveManager();
        Harness h = CreateHarness();
        WeaponData weapon = h.Weapons[0].Data;
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath path), Is.True);
        Assert.That(path, Is.EqualTo(WeaponUpgradePath.PathA));
        Assert.That(h.Service.CanRejectAdvancedOffer(weapon), Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, false).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathB, true).Success, Is.False);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, true).Success, Is.True);
        AssertRareAmounts(h, 95);
    }

    [Test]
    public void PathLockedAfterPayment_PreservesPaidChoiceWithoutApplyingOrChargingAgain()
    {
        Harness h = CreateHarnessOffering(WeaponUpgradePath.PathB);
        CreateSaveManager();
        Assert.That(h.Service.TryAdvancedTinkering(h.Weapons[0].Data, WeaponUpgradePath.PathB, true).Success, Is.False);
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.True);
        AssertRareAmounts(h, 95);
        AssertUnadvanced(h.Weapons[0]);
    }

    [Test]
    public void Availability_RefreshesAfterDeclineAndRetryMaterials()
    {
        Harness h = CreateHarness(1, 5);
        WeaponData weapon = h.Weapons[0].Data;
        Assert.That(h.Service.TryBeginAdvancedTinkering(weapon).Success, Is.True);
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath path), Is.True);
        Assert.That(h.Service.TryAdvancedTinkering(weapon, path, false).Success, Is.True);
        Assert.That(h.Service.GetAvailableAction(h.Weapons[0]), Is.Null);
        foreach (MaterialType material in RareMaterials) h.Inventory.Add(material, 8);
        Assert.That(h.Service.GetAvailableAction(h.Weapons[0]), Is.EqualTo(CraftingActionKind.AdvancedTinkering));
        Assert.That(h.Service.TryGetAdvancedOffer(weapon, out _), Is.False);
    }

    [Test]
    public void Availability_BasicRequiresTwoEligibleCandidatesAndEveryMaterial()
    {
        Harness harness = CreateHarness(1, 0);
        var pool = new List<WeaponData> { CreateWeapon("Offer A"), CreateWeapon("Offer B") };
        SetField(harness.Service, "_weaponPool", pool);
        foreach (MaterialType material in new[] { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears }) harness.Inventory.Add(material, 4);
        Assert.That(harness.Service.CanTinkerNewWeapon(), Is.False);
        foreach (MaterialType material in new[] { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears }) harness.Inventory.Add(material, 1);
        Random.State before = Random.state;
        Assert.That(harness.Service.CanTinkerNewWeapon(), Is.True);
        Assert.That(Random.state, Is.EqualTo(before));
        Assert.That(((List<WeaponData>)typeof(WeaponCraftingService).GetField("_tinkerOffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(harness.Service)).Count, Is.Zero);
        pool.RemoveAt(1);
        Assert.That(harness.Service.CanTinkerNewWeapon(), Is.False);
    }

    [Test]
    public void Availability_UpgradeUsesAllCostsAndAggregateIncludesNonManualWeapon()
    {
        Harness harness=CreateHarness(2, 0);
        MaterialUsageBalanceSO balance=Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>());
        balance.SetData(new List<MaterialRoleAssignment> {
            new MaterialRoleAssignment {Column=WeaponMaterialColumn.Flamethrower,Material=MaterialType.Gears,Role=MaterialRole.Principal},
            new MaterialRoleAssignment {Column=WeaponMaterialColumn.Flamethrower,Material=MaterialType.MetalPipe,Role=MaterialRole.Secondary}
        },new List<MaterialRoleTotalRow> {
            new MaterialRoleTotalRow {Role=MaterialRole.Principal,Level=3,Total=5},
            new MaterialRoleTotalRow {Role=MaterialRole.Secondary,Level=3,Total=5}
        });
        harness.Service.SetMaterialBalance(balance);harness.Weapons[1].Level=2;harness.Weapons[1].Data.WeaponType=WeaponType.Flamethrower;
        harness.Inventory.Add(MaterialType.Gears,5);harness.Inventory.Add(MaterialType.MetalPipe,4);
        Assert.That(harness.Service.GetAvailableAction(harness.Weapons[1]),Is.Null);
        Assert.That(harness.Service.HasAnyAvailableCraftingAction(),Is.False);
        harness.Inventory.Add(MaterialType.MetalPipe,1);
        Assert.That(harness.Service.GetAvailableAction(harness.Weapons[1]),Is.EqualTo(CraftingActionKind.UpgradeLevel));
        Assert.That(harness.Service.HasAnyAvailableCraftingAction(),Is.True);
        harness.Inventory.TrySpend(new[]{new MaterialCost(MaterialType.Gears,1)});
        Assert.That(harness.Service.HasAnyAvailableCraftingAction(),Is.False);
    }

    [Test]
    public void AuthoredPrefabs_HaveSixCorrectMaterialSpritesAndIndependentGlows()
    {
        GameObject root=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab");
        CraftingMenuView view=root.GetComponent<CraftingMenuView>();
        Assert.That(view.IsConfigured,Is.True);
        foreach(var readout in new[]{view.BalanceReadout,view.UpgradeReadout,view.TinkerReadout,view.AdvancedReadout})
        {
            Assert.That(readout,Is.Not.Null);Assert.That(readout.Entries.Length,Is.EqualTo(6));Assert.That(readout.Notice,Is.Not.Null);
            var seen=new HashSet<MaterialType>();
            foreach(var entry in readout.Entries){Assert.That(seen.Add(entry.Type),Is.True);Assert.That(entry.Root,Is.Not.Null);Assert.That(entry.Quantity,Is.Not.Null);Assert.That(entry.Icon.sprite,Is.EqualTo(UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/Icons/Materials/Material_{entry.Type}.png")));}
        }
        foreach(var slot in view.Slots){Assert.That(slot.Availability,Is.Not.Null);Assert.That(slot.Availability.Glow,Is.Not.Null);Assert.That(slot.Availability.Marker,Is.Not.Null);}
        root=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GameplayHud V2.prefab");
        Assert.That(root.GetComponentsInChildren<CraftingAvailabilityView>(true).Length,Is.EqualTo(3));
    }

    [Test]
    public void TableGuide_IsGatedAndLossOfAffordabilityDoesNotHideExitGuide()
    {
        Harness harness=CreateHarness(1,4);
        GameObject owner=Track(new GameObject("Guide fixture"));owner.SetActive(false);
        GuideArrow guide=owner.AddComponent<GuideArrow>();GuideArrowController controller=owner.AddComponent<GuideArrowController>();
        GameObject tableOwner=Track(new GameObject("Table fixture"));tableOwner.SetActive(false);CraftingStation table=tableOwner.AddComponent<CraftingStation>();
        SetField(controller,"_guideArrow",guide);SetField(controller,"_craftingStation",table);SetField(controller,"_crafting",harness.Service);
        var show=typeof(GuideArrowController).GetMethod("ShowCraftingGuide",BindingFlags.Instance|BindingFlags.NonPublic);
        var update=typeof(GuideArrowController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        SetField(controller,"_craftingStationDelaySeconds",0f);
        update.Invoke(controller,null);
        Assert.That((bool)typeof(GuideArrowController).GetField("_craftingHintShown",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller),Is.False);
        show.Invoke(controller,new object[]{10f});Assert.That(guide.Target,Is.Null);
        foreach(var type in RareMaterials)harness.Inventory.Add(type,1);
        update.Invoke(controller,null);Assert.That(guide.Target,Is.EqualTo(table.transform));
        harness.Inventory.TrySpend(new[]{new MaterialCost(MaterialType.Wiring,1)});update.Invoke(controller,null);Assert.That(guide.Target,Is.Null);
        GameObject exit=Track(new GameObject("Exit fixture"));guide.Show(exit.transform);update.Invoke(controller,null);Assert.That(guide.Target,Is.EqualTo(exit.transform));
        Assert.That((bool)typeof(GuideArrowController).GetField("_craftingHintShown",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller),Is.True);
    }

    [Test]
    public void MaterialReadout_ExactThresholdMixedCostsAndRebindDoNotCreateUi()
    {
        Harness harness=CreateHarness(1,5);
        GameObject clone=Track(Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab")));
        CraftingMenuView view=clone.GetComponent<CraftingMenuView>();var readout=view.AdvancedReadout;
        int before=clone.GetComponentsInChildren<Transform>(true).Length;
        readout.Bind(harness.Inventory,new[]{new MaterialCost(MaterialType.JellifiedFuel,6),new MaterialCost(MaterialType.PlasticExplosive,5),new MaterialCost(MaterialType.Wiring,4)});
        Assert.That(readout.Entries[3].Quantity.text,Is.EqualTo("5/6"));Assert.That(readout.Entries[3].Quantity.color,Is.EqualTo(readout.InsufficientColor));
        Assert.That(readout.Entries[4].Quantity.text,Is.EqualTo("5/5"));Assert.That(readout.Entries[4].Quantity.color,Is.EqualTo(readout.SufficientColor));
        Assert.That(readout.Entries[5].Quantity.color,Is.EqualTo(readout.SufficientColor));Assert.That(readout.Entries[0].Root.activeSelf,Is.False);
        readout.Bind(harness.Inventory,new[]{new MaterialCost(MaterialType.JellifiedFuel,5)});
        Assert.That(readout.Entries[3].Quantity.color,Is.EqualTo(readout.SufficientColor));Assert.That(readout.Entries[4].Root.activeSelf,Is.False);
        readout.Bind(harness.Inventory,new MaterialCost[0]);Assert.That(readout.Notice.text,Is.EqualTo("Free"));
        readout.Bind(harness.Inventory,new MaterialCost[0],"No further upgrades");Assert.That(readout.Notice.text,Is.EqualTo("No further upgrades"));
        Assert.That(clone.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(before));
        foreach(var field in view.Materials)Assert.That(field.NameText.gameObject.activeSelf,Is.False);
        Assert.That(view.AdvancedPurchaseCostText.gameObject.activeSelf,Is.False);
        Assert.That(view.AdvancedCostText.gameObject.activeSelf,Is.True);
    }

    [Test]
    public void AvailabilityPulse_IsTranslucentAndKeepsSymbolsSteadyWhilePaused()
    {
        GameObject root=Track(Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab")));
        CraftingAvailabilityView view=root.GetComponent<CraftingMenuView>().Slots[0].Availability;
        Assert.That(view.OutlineEdges.Length,Is.EqualTo(4));
        view.Bind(CraftingActionKind.UpgradeLevel);Color marker=view.Marker.color;float previous=Time.timeScale;
        try
        {
            Time.timeScale=0f;view.RefreshPulse(0.5f*Mathf.PI/view.PulseSpeed);
            float low=view.Glow.color.a;float lowEdge=view.OutlineEdges[0].color.a;
            view.RefreshPulse(1.5f*Mathf.PI/view.PulseSpeed);
            Assert.That(view.Glow.color.a,Is.GreaterThan(low));Assert.That(view.Glow.color.a,Is.LessThanOrEqualTo(0.05f));
            Assert.That(view.OutlineEdges[0].color.a,Is.GreaterThan(lowEdge+0.3f));
            Assert.That(view.Marker.color,Is.EqualTo(marker));Assert.That(view.Marker.gameObject.activeSelf,Is.True);
            Assert.That(view.Marker.text,Is.EqualTo("↑"));view.Bind(CraftingActionKind.TinkerNewWeapon);Assert.That(view.Marker.text,Is.EqualTo("+"));
        }
        finally{Time.timeScale=previous;}
    }

    private Harness CreateHarnessOffering(WeaponUpgradePath desired)
    {
        // Search a fixed seed set through the public API, without depending on its random-index mapping.
        for (int seed = 0; seed < 32; seed++)
        {
            Random.InitState(seed);
            Harness harness = CreateHarness();
            Assert.That(harness.Service.TryBeginAdvancedTinkering(harness.Weapons[0].Data).Success, Is.True);
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
