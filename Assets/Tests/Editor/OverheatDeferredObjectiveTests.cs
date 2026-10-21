using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// EditMode integration: real support casts, overheat events, placement and pool lifecycle.
/// Update/lifecycle entry points are driven explicitly; this does not replace PlayMode QA.
/// </summary>
public sealed class OverheatDeferredObjectiveTests
{
    private readonly List<GameObject> _created = new();
    private EnemyEmergenceTestLifecycle _lifecycle;
    private PlayerPlatformState _player;
    private PlayerHealth _health;
    private OverheatManager _overheat;
    private UpperLevelVolume _upper;
    private EnemyPoolRegistry _pool;
    private GameObject _prefab;
    private OverheatEliteWaveSpawner _elites;
    private UnityEngine.Random.State _randomState;
    private float _timeScale;
    private int _pauseLocks;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _lifecycle = new EnemyEmergenceTestLifecycle();
        _randomState = UnityEngine.Random.state;
        _timeScale = Time.timeScale;
        _pauseLocks = GameplayPause.LockCount;
        Time.timeScale = 1f;
        GameplayPause.Reset();
        DeferredObjectivePoolResetProbe.OnSpawn = null;

        GameObject floor = NewObject("Base");
        floor.transform.position = new Vector3(0f, -.5f, 0f);
        floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
        floor.SetActive(true);
        GameObject region = NewObject("Upper region");
        region.transform.position = Vector3.up * 10f;
        BoxCollider box = region.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(100f, 10f, 100f);
        _upper = region.AddComponent<UpperLevelVolume>();
        Set(_upper, "_baseSurfaceRoots", new[] { floor.transform });
        _lifecycle.SetActive(region, true);

        GameObject actor = NewObject("Player");
        CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = .5f;
        actor.AddComponent<Rigidbody>().useGravity = false;
        PlayerMovement movement = actor.AddComponent<PlayerMovement>();
        Set(movement, "_useFrictionlessMovementMaterial", false);
        _health = actor.AddComponent<PlayerHealth>();
        _player = actor.AddComponent<PlayerPlatformState>();
        Set(_player, "_upperLevelVolume", _upper);
        _lifecycle.SetActive(actor, true);
        MovePlayer(11f);

        GameObject manager = NewObject("Overheat");
        _overheat = manager.AddComponent<OverheatManager>();
        _lifecycle.SetActive(manager, true);

        GameObject holder = NewObject("Pool instances");
        holder.SetActive(true);
        GameObject registry = NewObject("Pool registry");
        _pool = registry.AddComponent<EnemyPoolRegistry>();
        Set(_pool, "_container", holder.transform);
        Set(_pool, "_allowPoolGrowth", false);
        _lifecycle.SetActive(registry, true);

        _prefab = NewObject("Elite test prefab");
        CharacterController controller = _prefab.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = .5f;
        controller.center = Vector3.up;
        _prefab.AddComponent<EnemyHealth>().ApplyConfiguredMaxHealth(20);
        _prefab.AddComponent<SwarmPooledEnemy>();
        _prefab.AddComponent<DeferredObjectivePoolResetProbe>();
        _pool.RegisterPrefab(new EnemyPoolRegistry.Entry { Prefab = _prefab, InitialSize = 1, MaxSize = 1 });
        // The pool uses ordinary SetActive. In EditMode the prewarmed clone does not
        // receive Awake, so EnemyHealth's nonserialized prefab-health cache stays zero.
        // Supply the lifecycle before the first lease, just as Unity does in PlayMode.
        foreach (EnemyHealth pooledHealth in holder.GetComponentsInChildren<EnemyHealth>(true))
        {
            _lifecycle.SetActive(pooledHealth.gameObject, true);
            _lifecycle.SetActive(pooledHealth.gameObject, false);
        }

        GameObject spawner = NewObject("Elites");
        _elites = spawner.AddComponent<OverheatEliteWaveSpawner>();
        Set(_elites, "_overheatManager", _overheat);
        Set(_elites, "_elites", new[] { new OverheatEliteWaveSpawner.EliteEntry { Prefab = _prefab, Count = 1 } });
        Set(_elites, "_groundRaycastMask", (LayerMask)1);
        Set(_elites, "_fallbackGroundRaycastMask", (LayerMask)1);
        Set(_elites, "_overlapSolidMask", (LayerMask)1);
        _lifecycle.SetActive(spawner, true);
    }

    [TearDown]
    public void TearDown()
    {
        DeferredObjectivePoolResetProbe.OnSpawn = null;
        if (_elites != null) _elites.ClearSpawned();
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) _lifecycle.Destroy(_created[i]);
        _created.Clear();
        UnityEngine.Random.state = _randomState;
        Time.timeScale = _timeScale;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void UpperStart_DescentThenRealBaseSupport_DispatchesCapturedBatchOnce_AndLastDeathEnds()
    {
        StartOverheat();
        Assert.That(_player.IsInsideUpper, Is.True);
        Assert.That(_elites.IsWaitingForBase, Is.True);
        Assert.That(_pool.TotalLeased, Is.Zero);
        Assert.That(_elites.ElitesRemaining, Is.Zero);
        Invoke(_elites, "EndOverheatIfNoObjective");
        Assert.That(_overheat.IsOverheating, Is.True, "A pending batch is an unfinished objective.");

        // Inspector changes after selection must not change this cycle's captured count.
        Get<OverheatEliteWaveSpawner.EliteEntry[]>(_elites, "_elites")[0].Count = 3;
        MovePlayer(4f);
        Assert.That(_player.IsInsideUpper, Is.False);
        Assert.That(_player.IsBaseSupported, Is.False);
        TickPending();
        Assert.That(_elites.IsWaitingForBase, Is.True, "Leaving the upper volume is not a landing.");

        MovePlayer(1.02f);
        Assert.That(_player.IsBaseSupported, Is.True, "Fixture must satisfy the actual movement spherecast.");
        GameplayPause.Push();
        TickPending();
        Assert.That(_pool.TotalLeased, Is.Zero);
        GameplayPause.Pop();
        TickPending();
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_elites.EliteWaveTotal, Is.EqualTo(1));
        Assert.That(_elites.ElitesRemaining, Is.EqualTo(1));
        Assert.That(_pool.TotalLeased, Is.EqualTo(1));
        TickPending();
        Assert.That(_pool.TotalLeased, Is.EqualTo(1), "A repeated landing tick cannot release twice.");

        Assert.That(_elites.GetAliveEliteTransforms().Count, Is.EqualTo(1), "The dispatched lease must have initialized living health.");
        EnemyHealth objective = _elites.GetAliveEliteTransforms()[0].GetComponent<EnemyHealth>();
        objective.ForceKill();
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_elites.ElitesRemaining, Is.Zero);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void QaClearWhileWaiting_DoesNotLease_AndLaterOddCycleReusesPreviousInstance()
    {
        MovePlayer(1.02f);
        StartOverheat();
        Assert.That(_elites.GetAliveEliteTransforms().Count, Is.EqualTo(1));
        Transform first = _elites.GetAliveEliteTransforms()[0];
        first.GetComponent<EnemyHealth>().ForceKill();
        MovePlayer(11f);
        StartOverheat(); // Even cycle belongs to bosses; no boss manager in this fixture.
        _overheat.NotifyOverheatObjectiveCleared();
        StartOverheat();
        Assert.That(_elites.IsWaitingForBase, Is.True);
        EnemyLifecycleCoordinator.ClearAllForQa();
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_pool.TotalLeased, Is.Zero);

        MovePlayer(1.02f);
        TickPending();
        Assert.That(_pool.TotalLeased, Is.Zero, "A QA-cancelled batch must not return on landing.");
        StartOverheat(); // Next even cycle.
        _overheat.NotifyOverheatObjectiveCleared();
        StartOverheat();
        Assert.That(_elites.GetAliveEliteTransforms().Count, Is.EqualTo(1));
        Assert.That(_elites.GetAliveEliteTransforms()[0], Is.SameAs(first));
        Assert.That(_elites.ElitesRemaining, Is.EqualTo(1));
        first.GetComponent<SwarmPooledEnemy>().Despawn();
        Assert.That(_overheat.IsOverheating, Is.False, "Pool despawn also clears the final objective.");
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void OrdinaryHealingKeepsPendingObjective_ExplicitResetCancelsIt()
    {
        StartOverheat();
        _health.FullHeal();
        Assert.That(_elites.IsWaitingForBase, Is.True);
        Assert.That(_overheat.IsOverheating, Is.True, "QA healing must not complete an unfinished objective.");
        _health.FullHeal(resetPhase: true);
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void ResetDuringPoolActivation_CancelsBatchAndReturnsUntrackedLease()
    {
        MovePlayer(1.02f);
        DeferredObjectivePoolResetProbe.OnSpawn = () => _health.FullHeal(resetPhase: true);
        StartOverheat();
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_elites.ElitesRemaining, Is.Zero);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void DisabledRegionWhileWaiting_EndsEmptyObjectiveWithoutSpawning()
    {
        StartOverheat();
        _upper.GetComponent<BoxCollider>().enabled = false;
        LogAssert.Expect(LogType.Error, "[EliteWave] PlayerPlatformState was lost while waiting; ending the empty objective.");
        TickPending();
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void BoundedPlacementFailure_EndsEmptyPhase_WithoutLeavingPoolLease()
    {
        MovePlayer(1.02f);
        Set(_elites, "_placementAttemptsPerElite", 1);
        Set(_elites, "_guaranteeSpawn", false);
        Set(_elites, "_groundRaycastMask", (LayerMask)(1 << 30));
        Set(_elites, "_fallbackGroundRaycastMask", (LayerMask)(1 << 30));
        StartOverheat();
        Assert.That(_elites.IsWaitingForBase, Is.False);
        Assert.That(_elites.IsEliteWaveActive, Is.False);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UpperClosestToPlayerRoot_IsRejectedBeforeObjectivePlacement_AndLeaseIsReturned(bool controllerEnabled)
    {
        ConfigureLowUpperBoundary();
        GameObject slab = NewObject("Nearby upper slab");
        slab.transform.position = new Vector3(12f, .95f, 0f);
        slab.AddComponent<BoxCollider>().size = new Vector3(4f, .5f, 4f);
        slab.SetActive(true);
        MovePlayer(1.02f);
        Assert.That(_player.CanSpawnObjectives, Is.True, "Player feet are still positively supported by Base.");
        Assert.That(_pool.TryGet(_prefab, out GameObject prepared), Is.True);
        prepared.GetComponent<CharacterController>().enabled = controllerEnabled;
        _pool.Release(prepared);

        // Prove this fixture exercises the old resolver's upper-surface preference.
        Assert.That(SpawnAtFixedRadius(null, out GameObject unfiltered, out Vector3 unfilteredPosition), Is.True);
        Assert.That(_player.IsUpperPosition(unfilteredPosition), Is.True);
        _pool.Release(unfiltered);
        Assert.That(SpawnAtFixedRadius(point => !_player.IsUpperPosition(point), out GameObject rejected, out _), Is.False);
        Assert.That(rejected, Is.Null);
        Assert.That(_pool.TotalLeased, Is.Zero, "A rejected upper surface cannot leak its pooled instance.");
    }

    [Test]
    public void GroundPredicate_UsesSupportPoint_NotRaisedEnemyPivot()
    {
        ConfigureLowUpperBoundary();
        MovePlayer(1.02f);
        Assert.That(_pool.TryGet(_prefab, out GameObject prepared), Is.True);
        CharacterController controller = prepared.GetComponent<CharacterController>();
        controller.center = Vector3.zero;
        controller.height = 4f;
        _pool.Release(prepared);

        Assert.That(SpawnAtFixedRadius(point => !_player.IsUpperPosition(point), out GameObject accepted, out Vector3 position), Is.True);
        Assert.That(_player.IsUpperPosition(position), Is.True, "Seating raises this tall enemy's pivot into upper bounds.");
        Assert.That(accepted.GetComponent<CharacterController>().bounds.min.y, Is.LessThan(.5f),
            "Its actual support remains on Base, so rejecting the pivot would be incorrect.");
        _pool.Release(accepted);
        Assert.That(_pool.TotalLeased, Is.Zero);
    }

    [Test]
    public void BossPlacement_RejectsSelectedUpperSurfaceDespiteBaseSupportedPlayer()
    {
        ConfigureLowUpperBoundary();
        GameObject slab = NewObject("Upper boss candidate");
        slab.transform.position = new Vector3(12f, .95f, 0f);
        slab.AddComponent<BoxCollider>().size = new Vector3(4f, .5f, 4f);
        slab.SetActive(true);
        MovePlayer(1.02f);
        Assert.That(_player.CanSpawnObjectives, Is.True);
        BossManager bosses = NewObject("Boss placement only").AddComponent<BossManager>();
        Set(bosses, "_groundRaycastMask", (LayerMask)1);
        Set(bosses, "_fallbackGroundRaycastMask", (LayerMask)1);
        Set(bosses, "_placementAttemptsPerBoss", 1);
        Assert.That(_pool.TryGet(_prefab, out GameObject candidate), Is.True);
        bool placed = (bool)Invoke(bosses, "TryPlaceBoss", _player, candidate.transform,
            candidate.GetComponent<CharacterController>(), _player.transform.position, Vector3.right * 12f);
        Assert.That(placed, Is.False);
        _pool.Release(candidate);
    }

    [Test]
    public void BossWaiting_QaClearCancelsCapturedEvenCycle_AndPlacementHasNoVoidFallback()
    {
        GameObject root = NewObject("Boss manager");
        BossManager bosses = root.AddComponent<BossManager>();
        Set(bosses, "_overheatManager", _overheat);
        Set(bosses, "_bossPrefab", _prefab);
        Set(bosses, "_overheatCycleIndex", 1);
        Set(bosses, "_placementAttemptsPerBoss", 1);
        Set(bosses, "_groundRaycastMask", (LayerMask)(1 << 30));
        Set(bosses, "_fallbackGroundRaycastMask", (LayerMask)(1 << 30));
        _lifecycle.SetActive(root, true);
        Set(_elites, "_cycleIndex", 1);
        StartOverheat();
        Assert.That(bosses.CurrentOverheatCycle, Is.EqualTo(2));
        Assert.That(bosses.IsWaitingForBase, Is.True);
        Assert.That(bosses.HasActiveBosses, Is.False);
        Invoke(bosses, "EndOverheatIfNoObjective");
        Assert.That(_overheat.IsOverheating, Is.True);
        EnemyLifecycleCoordinator.ClearAllForQa();
        Assert.That(bosses.IsWaitingForBase, Is.False);
        Assert.That(_overheat.IsOverheating, Is.False);
        MovePlayer(1.02f);
        Invoke(bosses, "TryReleasePendingSpawn");
        Assert.That(bosses.HasActiveBosses, Is.False);
        bool placed = (bool)Invoke(bosses, "TryPlaceBoss", _player, _prefab.transform,
            _prefab.GetComponent<CharacterController>(), _player.transform.position, Vector3.right * 12f);
        Assert.That(placed, Is.False, "Exhausting bounded ground probes must not accept a void position.");
    }

    private void ConfigureLowUpperBoundary()
    {
        // Match the map's narrow gap: upper begins above the player's feet but below its root.
        _upper.GetComponent<BoxCollider>().size = new Vector3(100f, 19f, 100f);
        Physics.SyncTransforms();
    }

    private bool SpawnAtFixedRadius(Predicate<Vector3> acceptGround, out GameObject instance, out Vector3 position) =>
        OrbitalSpawnPlacement.TrySpawnAtOrbitalPoint(_player.transform, _prefab, 2, 12f, 12f, 0f,
            (LayerMask)1, (LayerMask)1, (LayerMask)1, 48f, 220f, 6f, .02f, 14, .08f, .06f,
            out instance, out position, out _, acceptGround);

    private void StartOverheat() => Invoke(_overheat, "OnMaxHeatReached");
    private void TickPending() => Invoke(_elites, "TryReleasePendingSpawn");
    private void MovePlayer(float y)
    {
        _player.transform.position = new Vector3(0f, y, 0f);
        _player.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    private GameObject NewObject(string name)
    {
        GameObject go = new GameObject(name);
        go.SetActive(false);
        _created.Add(go);
        return go;
    }

    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static object Invoke(object target, string name, params object[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(target.GetType().Name, name)).Invoke(target, args);
}

public sealed class DeferredObjectivePoolResetProbe : MonoBehaviour, IEnemySpawnLifecycle
{
    public static Action OnSpawn;
    public void OnPoolSpawn() => OnSpawn?.Invoke();
    public void OnPoolDespawn() { }
}
