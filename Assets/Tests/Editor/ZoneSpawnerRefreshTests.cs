using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Drives real Overheat events and trigger entry points without requiring a gameplay scene.</summary>
public sealed class ZoneSpawnerRefreshTests
{
    private readonly List<GameObject> _created = new();
    private readonly List<bool> _armedChanges = new();
    private EnemyEmergenceTestLifecycle _lifecycle;
    private ZoneSpawner _zone;
    private OverheatManager _overheat;
    private PlayerHealth _health;
    private Collider _playerCollider;
    private float _previousScale;
    private int _previousPauseLocks;
    private bool _previousStartupHeld;
    private GameManager _previousGameManager;
    private TutorialDirector _previousTutorial;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _lifecycle = new EnemyEmergenceTestLifecycle();
        _previousScale = Time.timeScale;
        _previousPauseLocks = GameplayPause.LockCount;
        _previousStartupHeld = EntranceCinematic.StartupHeld;
        _previousGameManager = GameManager.Instance;
        _previousTutorial = TutorialDirector.Instance;
        SetStaticProperty(typeof(GameManager), nameof(GameManager.Instance), null);
        SetStaticProperty(typeof(TutorialDirector), nameof(TutorialDirector.Instance), null);
        SetStaticProperty(typeof(EntranceCinematic), nameof(EntranceCinematic.StartupHeld), false);
        Time.timeScale = 1f;
        GameplayPause.Reset();
        _armedChanges.Clear();

        GameObject player = NewObject("Zone test player");
        _playerCollider = player.AddComponent<CapsuleCollider>();
        _health = player.AddComponent<PlayerHealth>();
        _lifecycle.SetActive(player, true);

        GameObject manager = NewObject("Zone test Overheat");
        _overheat = manager.AddComponent<OverheatManager>();
        _lifecycle.SetActive(manager, true);

        GameObject zone = NewObject("Zone test spawner");
        zone.AddComponent<BoxCollider>();
        _zone = zone.AddComponent<ZoneSpawner>();
        Set(_zone, "_overheatManager", _overheat);
        _lifecycle.SetActive(zone, true);
        _zone.ArmedChanged += armed =>
        {
            Assert.That(_zone.IsArmed, Is.EqualTo(armed), "Observers must see the committed state.");
            _armedChanges.Add(armed);
        };
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) _lifecycle.Destroy(_created[i]);
        _created.Clear();
        SetStaticProperty(typeof(GameManager), nameof(GameManager.Instance), _previousGameManager);
        SetStaticProperty(typeof(TutorialDirector), nameof(TutorialDirector.Instance), _previousTutorial);
        SetStaticProperty(typeof(EntranceCinematic), nameof(EntranceCinematic.StartupHeld), _previousStartupHeld);
        Time.timeScale = _previousScale;
        GameplayPause.Reset();
        for (int i = 0; i < _previousPauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void ConsumedZone_RefreshesAtObjectiveCompletion_NotAtOverheatStart()
    {
        Consume();
        GameObject survivor = NewObject("Existing ambush survivor");
        survivor.SetActive(true);
        Read<List<GameObject>>(_zone, "_spawned").Add(survivor);

        StartOverheat();
        Assert.That(_zone.IsArmed, Is.False);
        Assert.That(_armedChanges, Is.EqualTo(new[] { false }));

        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_zone.IsArmed, Is.True);
        Assert.That(_zone.IsAvailable, Is.True);
        Assert.That(_armedChanges, Is.EqualTo(new[] { false, true }));
        Assert.That(survivor != null && survivor.activeSelf, Is.True, "Refresh must leave ambush survivors alive.");
        Assert.That(Read<List<GameObject>>(_zone, "_spawned"), Does.Contain(survivor));
    }

    [Test]
    public void InterruptedOverheat_DoesNotRefreshConsumedZone()
    {
        Consume();
        StartOverheat();
        _lifecycle.SetActive(_overheat.gameObject, false);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_zone.IsArmed, Is.False);
        Assert.That(_armedChanges, Is.EqualTo(new[] { false }));
    }

    [Test]
    public void RefreshDisabled_RespectsAuthoredOption()
    {
        Set(_zone, "_rearmOnOverheat", false);
        Consume();
        StartOverheat();
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_zone.IsArmed, Is.False);
    }

    [Test]
    public void PermanentOverheat_ObjectiveClearDoesNotRefresh()
    {
        Consume();
        _overheat.EnterPermanentOverheat();
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_overheat.IsPermanentOverheat, Is.True);
        Assert.That(_zone.IsArmed, Is.False);
    }

    [Test]
    public void OccupantMustLeaveAndReenterAfterRefresh_AndConsumptionOnlyNotifiesOnce()
    {
        Consume();
        Invoke(_zone, "OnTriggerEnter", _playerCollider);
        StartOverheat();
        _overheat.NotifyOverheatObjectiveCleared();
        Invoke(_zone, "OnTriggerStay", _playerCollider);
        Invoke(_zone, "OnTriggerStay", _playerCollider);
        Assert.That(_zone.IsArmed, Is.True, "Refreshing an occupied zone must not spawn another ambush.");
        Assert.That(_armedChanges, Is.EqualTo(new[] { false, true }));

        Invoke(_zone, "OnTriggerExit", _playerCollider);
        Consume();
        _zone.Rearm();
        _zone.Rearm();
        Assert.That(_armedChanges, Is.EqualTo(new[] { false, true, false, true }));
    }

    [Test]
    public void Preparation_DefersEntryUntilReady_ThenConsumesOnce()
    {
        StartOverheat();
        Invoke(_zone, "OnTriggerEnter", _playerCollider);
        Assert.That(_zone.IsArmed, Is.True);
        Assert.That(_zone.IsAvailable, Is.False);
        Assert.That(_armedChanges, Is.Empty);

        Set(_overheat, "_isPreparing", false);
        LogAssert.Expect(LogType.Warning, "[ZoneSpawner] _enemyPrefab is not assigned.");
        Invoke(_zone, "OnTriggerStay", _playerCollider);
        Invoke(_zone, "OnTriggerStay", _playerCollider);
        Assert.That(_zone.IsArmed, Is.False);
        Assert.That(_armedChanges, Is.EqualTo(new[] { false }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreparationCompletionOrInterruption_ClearsDeferredEntries(bool interrupted)
    {
        StartOverheat();
        Invoke(_zone, "OnTriggerEnter", _playerCollider);
        if (interrupted) _lifecycle.SetActive(_overheat.gameObject, false);
        else _overheat.NotifyOverheatObjectiveCleared();
        Invoke(_zone, "OnTriggerStay", _playerCollider);
        Assert.That(_zone.IsArmed, Is.True);
        Assert.That(_armedChanges, Is.Empty);
    }

    [Test]
    public void Availability_TracksTemporaryGatesWithoutConsumingTheZone()
    {
        Assert.That(_zone.IsAvailable, Is.True);
        Time.timeScale = 0f;
        AssertUnavailableAndEntryIgnored();
        Time.timeScale = 1f;
        GameplayPause.Push();
        AssertUnavailableAndEntryIgnored();
        GameplayPause.Pop();
        SetStaticProperty(typeof(EntranceCinematic), nameof(EntranceCinematic.StartupHeld), true);
        AssertUnavailableAndEntryIgnored();
        SetStaticProperty(typeof(EntranceCinematic), nameof(EntranceCinematic.StartupHeld), false);
        Set(_health, "_currentHealth", 0);
        AssertUnavailableAndEntryIgnored();
        Set(_health, "_currentHealth", 100);

        GameManager game = NewObject("Inactive run").AddComponent<GameManager>();
        SetStaticProperty(typeof(GameManager), nameof(GameManager.Instance), game);
        Set(game, "_state", GameManager.GameState.GameOver);
        AssertUnavailableAndEntryIgnored();
        Set(game, "_state", GameManager.GameState.Playing);

        TutorialDirector tutorial = NewObject("Tutorial gate").AddComponent<TutorialDirector>();
        SetStaticProperty(typeof(TutorialDirector), nameof(TutorialDirector.Instance), tutorial);
        Set(tutorial, "_active", true);
        AssertUnavailableAndEntryIgnored();
        Set(tutorial, "_active", false);
        _zone.enabled = false;
        Assert.That(_zone.IsAvailable, Is.False);
        _zone.enabled = true;
        Assert.That(_zone.IsAvailable, Is.True);
        Assert.That(_armedChanges, Is.Empty);
    }

    [Test]
    public void Availability_DoesNotRequirePlayerPresenceOrProximity()
    {
        _health.transform.position = new Vector3(10000f, 10000f, 10000f);
        Assert.That(_zone.IsAvailable, Is.True);
        _lifecycle.Destroy(_health.gameObject);
        Assert.That(_zone.IsAvailable, Is.True);
    }

    private void Consume()
    {
        // A missing prefab is an accepted activation attempt in the existing gameplay contract.
        LogAssert.Expect(LogType.Warning, "[ZoneSpawner] _enemyPrefab is not assigned.");
        Invoke(_zone, "OnTriggerEnter", _playerCollider);
        Assert.That(_zone.IsArmed, Is.False);
        Assert.That(_zone.IsAvailable, Is.False);
    }

    private void AssertUnavailableAndEntryIgnored()
    {
        Assert.That(_zone.IsAvailable, Is.False);
        Invoke(_zone, "OnTriggerEnter", _playerCollider);
        Assert.That(_zone.IsArmed, Is.True);
        Assert.That(_armedChanges, Is.Empty);
    }

    private void StartOverheat() => Invoke(_overheat, "OnMaxHeatReached");

    private GameObject NewObject(string name)
    {
        GameObject root = new(name);
        root.SetActive(false);
        _created.Add(root);
        return root;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static T Read<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static void SetStaticProperty(System.Type type, string name, object value) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
}
