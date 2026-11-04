using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class CraftingStationTransactionTests
{
    private readonly List<Object> _cleanup = new();
    private Scene _scene;
    private SaveManager _previousSave;
    private Random.State _previousRandom;
    private CountingRandom _random;

    [SetUp]
    public void SetUp()
    {
        // Preview scenes isolate the fixture without requiring the user's untitled scene to be saved.
        _scene = EditorSceneManager.NewPreviewScene();
        _previousSave = SaveManager.Instance;
        SetSave(null);
        _previousRandom = Random.state;
        _random = new CountingRandom();
        CraftingStationRotation.SetRandomSource(_scene, _random);
        GameplayPause.Reset();
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
        SetSave(_previousSave);
        Random.state = _previousRandom;
        GameplayPause.Reset();
        Time.timeScale = 1f;
    }

    [Test]
    public void FreeUpgrade_CommitsBeforeOneRollAndRejectsPaymentCallbackReentry()
    {
        Harness h = CreateHarness(1);
        CraftingStation station = CreateStation(0f);
        CraftingStationRotation.InitializeScene(_scene);
        int callbacks = 0;
        h.Inventory.OnInventoryChanged += () =>
        {
            callbacks++;
            Assert.That(_random.Rolls, Is.Zero);
            Assert.That(h.Service.TryUpgradeWeapon(h.Weapon.Data, 2, station).Success, Is.False);
        };
        _random.OnRoll = () => Assert.That(h.Weapon.Level, Is.EqualTo(2), "The award precedes station wear.");

        CraftingActionResult result = h.Service.TryUpgradeWeapon(h.Weapon.Data, 2, station);
        Assert.That(result.Success && result.TransactionCompleted, Is.True);
        Assert.That(result.StationBroke, Is.False);
        Assert.That(callbacks, Is.EqualTo(1), "An authored empty recipe still completes one transaction.");
        Assert.That(_random.Rolls, Is.EqualTo(1));
        Assert.That(station.CurrentBreakChancePercent, Is.EqualTo(5f));
        Assert.That(h.Service.TryUpgradeWeapon(h.Weapon.Data, 2, station).TransactionCompleted, Is.False);
        Assert.That(_random.Rolls, Is.EqualTo(1));
    }

    [Test]
    public void BrokenSource_RejectsNewOperationsWithoutSpendingOrRolling()
    {
        Harness h = CreateHarness(5);
        CreateStation(0f);
        CraftingStation broken = CreateStation(0f);
        CraftingStationRotation.InitializeScene(_scene);
        Assert.That(broken.IsAvailable, Is.False);

        Assert.That(h.Service.TryUpgradeWeapon(h.Weapon.Data, 7, broken).Success, Is.False);
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapon.Data, broken).Success, Is.False);
        Assert.That(h.Service.TryBeginTinkering(broken).Success, Is.False);
        WeaponData offered = h.Service.GetTinkeringOffer()[0];
        Assert.That(h.Service.TryTinkerWeapon(offered, broken).Success, Is.False);
        Assert.That(_random.Rolls, Is.Zero);
        Assert.That(h.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(100));
        Assert.That(h.Inventory.GetAmount(MaterialType.Gears), Is.EqualTo(100));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AdvancedAcceptOrReject_RollsOnceAfterResolutionAtTheOriginalStation(bool accept)
    {
        Harness h = CreateHarness(5);
        CraftingStation source = CreateStation(100f);
        CraftingStation next = CreateStation(0f);
        CraftingStationRotation.InitializeScene(_scene);
        CraftingActionResult purchase = h.Service.TryBeginAdvancedTinkering(h.Weapon.Data, source);
        Assert.That(purchase.Success, Is.True);
        Assert.That(purchase.TransactionCompleted || purchase.StationBroke, Is.False);
        Assert.That(h.Service.TryBeginAdvancedTinkering(h.Weapon.Data, next).TransactionCompleted, Is.False);
        Assert.That(h.Service.PendingTransactionStation, Is.SameAs(source));
        Assert.That(_random.Rolls, Is.Zero);
        Assert.That(h.Service.TryGetAdvancedOffer(h.Weapon.Data, out WeaponUpgradePath offered), Is.True);
        _random.OnRoll = () =>
        {
            Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
            Assert.That(h.Weapon.Level, Is.EqualTo(accept ? 6 : 5));
            Assert.That(h.Service.WasAdvancedRejected(h.Weapon.Data), Is.EqualTo(!accept));
        };

        CraftingActionResult result = h.Service.TryAdvancedTinkering(h.Weapon.Data, offered, accept);
        Assert.That(result.Success && result.TransactionCompleted && result.StationBroke, Is.True);
        Assert.That(_random.Rolls, Is.EqualTo(1));
        Assert.That(source.IsAvailable, Is.False);
        Assert.That(next.IsAvailable, Is.True);
        Assert.That(h.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(95));
        Assert.That(h.Service.TryAdvancedTinkering(h.Weapon.Data, offered, accept).TransactionCompleted, Is.False);
        Assert.That(_random.Rolls, Is.EqualTo(1));
    }

    [Test]
    public void BasicOffer_BeginResumeAndInvalidChoiceDoNotWearUntilTheWeaponIsAwarded()
    {
        Harness h = CreateHarness(1);
        CraftingStation source = CreateStation(0f);
        CraftingStationRotation.InitializeScene(_scene);
        Assert.That(h.Service.TryBeginTinkering(source).TransactionCompleted, Is.False);
        Assert.That(h.Service.TryBeginTinkering(source).TransactionCompleted, Is.False);
        Assert.That(h.Service.TryTinkerWeapon(null).TransactionCompleted, Is.False);
        Assert.That(_random.Rolls, Is.Zero);
        WeaponData chosen = h.Service.GetTinkeringOffer()[0];
        _random.OnRoll = () =>
        {
            Assert.That(h.Manager.GetEquippedWeapons().Count, Is.EqualTo(2));
            Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
        };

        Assert.That(h.Service.TryTinkerWeapon(chosen).TransactionCompleted, Is.True);
        Assert.That(source.CurrentBreakChancePercent, Is.EqualTo(5f));
        Assert.That(h.Inventory.GetAmount(MaterialType.Gears), Is.EqualTo(95));
        Assert.That(h.Service.TryTinkerWeapon(chosen).TransactionCompleted, Is.False);
        Assert.That(_random.Rolls, Is.EqualTo(1));
    }

    [Test]
    public void InvalidatedBasicOffer_RefundsWithoutCompletingOrWearingTheStation()
    {
        Harness h = CreateHarness(1);
        CraftingStation source = CreateStation(100f);
        CraftingStationRotation.InitializeScene(_scene);
        Assert.That(h.Service.TryBeginTinkering(source).Success, Is.True);
        WeaponData chosen = h.Service.GetTinkeringOffer()[0];
        Get<List<IWeaponBehaviour>>(h.Manager, "_equipped").Add(new StubWeapon(new WeaponInstance
            { Data = CreateWeapon("External equipment"), Level = 1 }));

        CraftingActionResult result = h.Service.TryTinkerWeapon(chosen);
        Assert.That(result.Success || result.TransactionCompleted || result.StationBroke, Is.False);
        Assert.That(h.Inventory.GetAmount(MaterialType.Gears), Is.EqualTo(100));
        Assert.That(h.Service.HasPendingTinkeringChoice, Is.False);
        Assert.That(h.Service.HasStationBoundPendingChoice, Is.False);
        Assert.That(h.Service.PendingTransactionStation, Is.Null);
        Assert.That(_random.Rolls, Is.Zero);
        Assert.That(source.IsAvailable, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PaidOffer_SurvivesPresenterReplacementAndClosesExactlyOnceAfterBreak(bool accept)
    {
        Harness h = CreateHarness(5);
        CraftingStation source = CreateStation(100f);
        CreateStation(0f);
        CraftingStationRotation.InitializeScene(_scene);
        (CraftingUI ui, CraftingMenuView view) = CreatePresenter();
        int firstClosed = 0;
        IEnumerator first = ui.PresentCoroutine(h.Service, h.Inventory, () => firstClosed++, source);
        Assert.That(first.MoveNext(), Is.True);
        view.AdvancedPurchaseButton.onClick.Invoke();
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.True);
        Assert.That(_random.Rolls, Is.Zero);
        view.CloseButton.onClick.Invoke();
        Assert.That(ui.IsVisible, Is.True);
        Invoke(ui, "OnDisable");
        Assert.That(firstClosed, Is.EqualTo(1), "Disabling the coroutine host must not delay its release callback.");
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Object.DestroyImmediate(ui);

        (CraftingUI resumed, CraftingMenuView resumedView) = CreatePresenter();
        int secondClosed = 0;
        IEnumerator second = resumed.PresentCoroutine(h.Service, h.Inventory, () => secondClosed++);
        Assert.That(second.MoveNext(), Is.True);
        int hierarchy = resumedView.GetComponentsInChildren<Transform>(true).Length;
        if (accept) resumedView.AcceptButton.onClick.Invoke();
        else resumedView.DeclineButton.onClick.Invoke();
        Assert.That(_random.Rolls, Is.EqualTo(1));
        Assert.That(source.IsAvailable, Is.False);
        Assert.That(h.Weapon.Level, Is.EqualTo(accept ? 6 : 5));
        Assert.That(h.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(95));
        Assert.That(resumed.IsVisible, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(secondClosed, Is.EqualTo(1));
        Assert.That(second.MoveNext(), Is.False);
        Assert.That(first.MoveNext(), Is.False);
        Assert.That(firstClosed, Is.EqualTo(1));
        resumedView.AcceptButton.onClick.Invoke();
        resumedView.DeclineButton.onClick.Invoke();
        Assert.That(secondClosed, Is.EqualTo(1));
        Assert.That(_random.Rolls, Is.EqualTo(1));
        Assert.That(resumedView.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(hierarchy));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LostSource_StillAwardsPaidChoiceAndReleasesThePresentation(bool destroy)
    {
        Harness h = CreateHarness(5);
        CraftingStation source = CreateStation(100f);
        CraftingStationRotation.InitializeScene(_scene);
        (CraftingUI ui, CraftingMenuView view) = CreatePresenter();
        int closed = 0;
        IEnumerator presentation = ui.PresentCoroutine(h.Service, h.Inventory, () => closed++, source);
        Assert.That(presentation.MoveNext(), Is.True);
        view.AdvancedPurchaseButton.onClick.Invoke();
        if (destroy) Object.DestroyImmediate(source.gameObject);
        else source.gameObject.SetActive(false);

        view.AcceptButton.onClick.Invoke();
        Assert.That(h.Weapon.Level, Is.EqualTo(6));
        Assert.That(h.Inventory.GetAmount(MaterialType.Wiring), Is.EqualTo(95));
        Assert.That(h.Service.HasPendingAdvancedChoice, Is.False);
        Assert.That(ui.IsVisible, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(_random.Rolls, Is.Zero, "A missing source never transfers wear to an unrelated station.");
    }

    [Test]
    public void SingleStationBreak_ClosesTheMenuEvenThoughTheSameStationReactivates()
    {
        Harness h = CreateHarness(1);
        CraftingStation source = CreateStation(100f);
        CraftingStationRotation.InitializeScene(_scene);
        (CraftingUI ui, CraftingMenuView view) = CreatePresenter();
        int closed = 0;
        IEnumerator presentation = ui.PresentCoroutine(h.Service, h.Inventory, () => closed++, source);
        Assert.That(presentation.MoveNext(), Is.True);
        view.UpgradeButton.onClick.Invoke();

        Assert.That(h.Weapon.Level, Is.EqualTo(2));
        Assert.That(_random.Rolls, Is.EqualTo(1));
        Assert.That(source.IsAvailable, Is.True);
        Assert.That(source.CurrentBreakChancePercent, Is.EqualTo(100f));
        Assert.That(ui.IsVisible, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test]
    public void OpeningClosingAndStationlessCrafting_DoNotWearAStation()
    {
        Harness h = CreateHarness(1);
        CraftingStation source = CreateStation(100f);
        CraftingStationRotation.InitializeScene(_scene);
        (CraftingUI ui, CraftingMenuView view) = CreatePresenter();
        IEnumerator presentation = ui.PresentCoroutine(h.Service, h.Inventory, () => { }, source);
        Assert.That(presentation.MoveNext(), Is.True);
        view.CloseButton.onClick.Invoke();
        Assert.That(presentation.MoveNext(), Is.False);
        CraftingActionResult standalone = h.Service.TryUpgradeWeapon(h.Weapon.Data, 2);
        Assert.That(standalone.TransactionCompleted, Is.True);
        Assert.That(standalone.StationBroke, Is.False);
        Assert.That(_random.Rolls, Is.Zero);
    }

    private Harness CreateHarness(int level)
    {
        GameObject owner = CreateObject("Crafting transaction fixture", false);
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
        WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
        Set(service, "_weaponManager", manager);
        Set(service, "_inventory", inventory);
        service.SetMaterialBalance(Track(ScriptableObject.CreateInstance<MaterialUsageBalanceSO>()));
        var weapon = new WeaponInstance { Data = CreateWeapon("Equipped"), Level = level };
        Get<List<IWeaponBehaviour>>(manager, "_equipped").Add(new StubWeapon(weapon));
        Set(service, "_weaponPool", new List<WeaponData> { CreateWeapon("Candidate A"), CreateWeapon("Candidate B") });
        foreach (MaterialType material in Enum.GetValues(typeof(MaterialType))) inventory.Add(material, 100);
        return new Harness(service, inventory, manager, weapon);
    }

    private CraftingStation CreateStation(float initialChance)
    {
        CraftingStation station = CreateObject("Transaction station", true).AddComponent<CraftingStation>();
        Set(station, "_initialBreakChancePercent", initialChance);
        return station;
    }

    private (CraftingUI, CraftingMenuView) CreatePresenter()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab");
        GameObject viewOwner = Track(Object.Instantiate(prefab));
        SceneManager.MoveGameObjectToScene(viewOwner, _scene);
        CraftingMenuView view = viewOwner.GetComponent<CraftingMenuView>();
        CraftingUI ui = CreateObject("Transaction presenter", false).AddComponent<CraftingUI>();
        Set(ui, "_view", view);
        return (ui, view);
    }

    private WeaponData CreateWeapon(string id)
    {
        WeaponData weapon = Track(ScriptableObject.CreateInstance<WeaponData>());
        weapon.WeaponId = id;
        weapon.DisplayName = id;
        weapon.PathA = new WeaponUpgradePathData { PathName = "Path A" };
        weapon.PathB = new WeaponUpgradePathData { PathName = "Path B" };
        return weapon;
    }

    private GameObject CreateObject(string name, bool active)
    {
        GameObject owner = Track(new GameObject(name));
        owner.SetActive(active);
        SceneManager.MoveGameObjectToScene(owner, _scene);
        return owner;
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void Set(object owner, string field, object value) => owner.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static T Get<T>(object owner, string field) => (T)owner.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void Invoke(object owner, string method) => owner.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
    private static void SetSave(SaveManager save) => typeof(SaveManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, save);

    private sealed class CountingRandom : CraftingStationRotation.IRandomSource
    {
        public int Rolls;
        public Action OnRoll;
        public double NextDouble() { OnRoll?.Invoke(); Rolls++; return 0.9d; }
        public int NextIndex(int exclusiveMax) => 0;
    }

    private sealed class Harness
    {
        public readonly WeaponCraftingService Service;
        public readonly MaterialInventory Inventory;
        public readonly WeaponManager Manager;
        public readonly WeaponInstance Weapon;
        public Harness(WeaponCraftingService service, MaterialInventory inventory, WeaponManager manager, WeaponInstance weapon)
        { Service = service; Inventory = inventory; Manager = manager; Weapon = weapon; }
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
