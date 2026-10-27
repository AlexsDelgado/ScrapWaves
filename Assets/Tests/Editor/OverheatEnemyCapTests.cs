using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Real placement/pool integration; explicit lifecycle messages supply EditMode callbacks.</summary>
public sealed class OverheatEnemyCapTests
{
    private readonly List<GameObject> _created = new();
    private readonly List<ScriptableObject> _assets = new();
    private EnemyEmergenceTestLifecycle _lifecycle;
    private SpawnBalanceProfile _profile;
    private HeatManager _heat;
    private OverheatManager _overheat;
    private PlayerPlatformState _player;
    private PlayerHealth _playerHealth;
    private EnemyPoolRegistry _pool;
    private Transform _poolHolder;
    private GameObject _prefab;
    private OrbitalSpawner _orbital;
    private OverheatEliteWaveSpawner _elites;
    private ChallengeProgressTracker _previousChallengeTracker;
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
        UnityEngine.Random.InitState(170);
        _previousChallengeTracker = ChallengeProgressTracker.Instance;
        // Match other EditMode damage fixtures: AddComponent does not run singleton Awake.
        ChallengeProgressTracker tracker = NewObject("Cap test challenge tracker").AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);

        GameObject floor = NewObject("Base");
        floor.transform.position = Vector3.down * .5f;
        floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
        floor.SetActive(true);
        GameObject region = NewObject("Upper region");
        region.transform.position = Vector3.up * 10f;
        BoxCollider box = region.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(100f, 10f, 100f);
        UpperLevelVolume upper = region.AddComponent<UpperLevelVolume>();
        Set(upper, "_baseSurfaceRoots", new[] { floor.transform });
        _lifecycle.SetActive(region, true);

        GameObject actor = NewObject("Player");
        CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = .5f;
        actor.AddComponent<Rigidbody>().useGravity = false;
        PlayerMovement movement = actor.AddComponent<PlayerMovement>();
        Set(movement, "_useFrictionlessMovementMaterial", false);
        _playerHealth = actor.AddComponent<PlayerHealth>();
        _player = actor.AddComponent<PlayerPlatformState>();
        Set(_player, "_upperLevelVolume", upper);
        _lifecycle.SetActive(actor, true);
        MovePlayer(1.02f);

        _profile = NewAsset<SpawnBalanceProfile>();
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 3 });
        GameObject heatRoot = NewObject("Heat");
        _heat = heatRoot.AddComponent<HeatManager>();
        _heat.SetProfile(_profile);
        _lifecycle.SetActive(heatRoot, true);
        GameObject overheatRoot = NewObject("Overheat");
        _overheat = overheatRoot.AddComponent<OverheatManager>();
        Set(_overheat, "_heatManager", _heat);
        _lifecycle.SetActive(overheatRoot, true);

        GameObject holder = NewObject("Pool instances");
        holder.SetActive(true);
        _poolHolder = holder.transform;
        GameObject registry = NewObject("Pool registry");
        _pool = registry.AddComponent<EnemyPoolRegistry>();
        Set(_pool, "_container", _poolHolder);
        Set(_pool, "_allowPoolGrowth", false);
        _lifecycle.SetActive(registry, true);
        _prefab = NewObject("Shared orbital and zone prefab");
        _prefab.layer = 2; // Placement probes must see Base rather than another test enemy.
        CharacterController controller = _prefab.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = .5f;
        controller.center = Vector3.up;
        _prefab.AddComponent<EnemyHealth>().ApplyConfiguredMaxHealth(20);
        _prefab.AddComponent<SwarmPooledEnemy>();
        _pool.RegisterPrefab(new EnemyPoolRegistry.Entry { Prefab = _prefab, InitialSize = 8, MaxSize = 8 });
        foreach (EnemyHealth health in holder.GetComponentsInChildren<EnemyHealth>(true))
        {
            // Prewarmed EditMode clones need Awake to initialize their prefab-health cache.
            _lifecycle.SetActive(health.gameObject, true);
            _lifecycle.SetActive(health.gameObject, false);
        }

        EnemySpawnRouletteConfig config = NewAsset<EnemySpawnRouletteConfig>();
        Set(config, "_entries", new[]
        {
            new EnemySpawnRouletteConfig.Entry
            {
                Kind = EnemySpawnKind.JunkSlime, Prefab = _prefab, BaseWeight = 1, BatchSize = 8
            }
        });
        GameObject spawner = NewObject("Orbital");
        _orbital = spawner.AddComponent<OrbitalSpawner>();
        _orbital.SetProfile(_profile);
        Set(_orbital, "_config", config);
        Set(_orbital, "_player", actor.transform);
        Set(_orbital, "_heatManager", _heat);
        Set(_orbital, "_overheatManager", _overheat);
        Set(_orbital, "_minSpawnRadius", 12f);
        Set(_orbital, "_maxSpawnRadius", 12f);
        SetGroundMasks(_orbital, 1);
        _lifecycle.SetActive(spawner, true);
        Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero, "Pool prewarming must not enroll enemies.");
    }

    [TearDown]
    public void TearDown()
    {
        if (_elites != null) _elites.ClearSpawned();
        if (_pool != null) _pool.ReleaseAllActive();
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) _lifecycle.Destroy(_created[i]);
        _created.Clear();
        foreach (ScriptableObject asset in _assets)
            if (asset != null) Object.DestroyImmediate(asset);
        _assets.Clear();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _previousChallengeTracker);
        UnityEngine.Random.state = _randomState;
        Time.timeScale = _timeScale;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void Profile_EmptyOrMissingCycleListAndMissingProfile_UseCompatibilityFallback()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", Array.Empty<int>());
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(50), Is.EqualTo(300));
        Set(_profile, "_maxActiveEnemiesByCycle", null);
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(0), Is.EqualTo(300));
        Set(_profile, "_maxActiveEnemies", 17);
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(50), Is.EqualTo(17));
        Set(_orbital, "_maxActiveEnemies", 9);
        _orbital.SetProfile(null);
        Assert.That(_orbital.MaxActiveEnemies, Is.EqualTo(9));
    }

    [Test]
    public void Profile_ClampsCycleIndexAndInvalidEntries_AndRepeatsFinalCapBelowGlobalCeiling()
    {
        Set(_profile, "_maxActiveEnemies", 30);
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 12, 0, -4, 500, 24 });
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(-1), Is.EqualTo(12));
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(0), Is.EqualTo(12));
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(1), Is.EqualTo(1));
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(2), Is.EqualTo(1));
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(3), Is.EqualTo(30));
        Assert.That(_profile.GetMaxActiveEnemiesForCycle(int.MaxValue), Is.EqualTo(24));
    }

    [Test]
    public void OrbitalBatch_StopsExactlyAtCap_AndDeathOpensOneConcurrentSlot()
    {
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(3));
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3));
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(3));
        FirstActiveEnemy().GetComponent<EnemyHealth>().ForceKill();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(2));
        SpawnWave();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3));
        Assert.That(_pool.TotalLeased, Is.EqualTo(3), "The cap is concurrent, not a lifetime spawn budget.");
    }

    [Test]
    public void ZoneSpawns_IgnoreFullCap_AndSamePooledPrefabNeverRetainsItsPreviousSource()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 1 });
        SpawnWave();
        GameObject firstOrbital = FirstActiveEnemy();
        ZoneSpawner zone = CreateZone(2);
        Invoke(zone, "SpawnAmbush");
        Assert.That(_pool.TotalLeased, Is.EqualTo(3), "A full orbital cap must not block an in-world ambush.");
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
        _pool.Release(firstOrbital);
        zone.ClearSpawned();
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);

        Set(zone, "_spawnCount", 8);
        Invoke(zone, "SpawnAmbush");
        Assert.That(Get<List<GameObject>>(zone, "_spawned"), Does.Contain(firstOrbital),
            "The fixture must reuse the very same object previously admitted as orbital.");
        Assert.That(_pool.TotalLeased, Is.EqualTo(8));
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
        zone.ClearSpawned();
        SpawnWave();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1), "A former zone lease must count when reused by orbital.");
    }

    [Test]
    public void FailedGroundPlacement_ReturnsPoolLease_WithoutConsumingCap()
    {
        SetGroundMasks(_orbital, 1 << 30);
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.Zero);
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
        SetGroundMasks(_orbital, 1);
        SpawnWave();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3));
    }

    [Test]
    public void SuccessfulEmergingEnemies_CountBeforeTargetability_AndRegistryRemovalDoesNotFreeCap()
    {
        foreach (EnemyHealth health in _poolHolder.GetComponentsInChildren<EnemyHealth>(true))
        {
            EnemyEmergence emergence = health.gameObject.AddComponent<EnemyEmergence>();
            emergence.Configure(null, 2f, 2f);
            // Supply the OnEnable that a real pool lease receives before placement.
            Invoke(emergence, "Awake");
            Invoke(emergence, "OnEnable");
        }
        SpawnWave();
        GameObject enemy = FirstActiveEnemy();
        Assert.That(enemy.GetComponent<EnemyEmergence>().IsEmerging, Is.True);
        Assert.That(enemy.GetComponent<EnemyEmergence>().BlocksGameplayNow, Is.True);
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3));
        EnemyRegistry.Register(enemy.transform);
        EnemyRegistry.Unregister(enemy.transform);
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(3), "Temporary combat-registry removal cannot grant an extra slot.");
    }

    [Test]
    public void DeathDespawnAndRepeatedPoolReuse_ReleaseMembershipExactlyOnce()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 1 });
        for (int i = 0; i < 12; i++)
        {
            SpawnWave();
            GameObject enemy = FirstActiveEnemy();
            EnemySpawnPopulationMember.RecordSpawn(enemy, EnemySpawnSource.Orbital);
            Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1), "Repeated admission must be idempotent.");
            enemy.GetComponent<EnemyHealth>().ForceKill();
            enemy.GetComponent<SwarmPooledEnemy>().Despawn();
            Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
            Assert.That(_pool.TotalLeased, Is.Zero);
        }
    }

    [Test]
    public void DisableAndDestroy_ReleaseMembership_AndReenableAloneDoesNotEnroll()
    {
        GameObject enemy = NewObject("Unpooled population member");
        enemy.AddComponent<EnemyHealth>();
        enemy.AddComponent<EnemySpawnPopulationMember>();
        _lifecycle.SetActive(enemy, true);
        EnemySpawnPopulationMember.RecordSpawn(enemy, EnemySpawnSource.Orbital);
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
        _lifecycle.SetActive(enemy, false);
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
        _lifecycle.SetActive(enemy, true);
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
        EnemySpawnPopulationMember.RecordSpawn(enemy, EnemySpawnSource.Orbital);
        _lifecycle.Destroy(enemy);
        Assert.That(_orbital.CappedActiveEnemies, Is.Zero);
    }

    [Test]
    public void CompletingCycleWithLowerCap_KeepsSurvivors_AndWaitsForRoom()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 3, 1 });
        SpawnWave();
        Invoke(_overheat, "OnMaxHeatReached");
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_heat.CompletedOverheatCycles, Is.EqualTo(1));
        Assert.That(_orbital.MaxActiveEnemies, Is.EqualTo(1));
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3), "A lower cap must not cull existing enemies.");
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(3));
        FirstActiveEnemy().GetComponent<EnemyHealth>().ForceKill();
        FirstActiveEnemy().GetComponent<EnemyHealth>().ForceKill();
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(1), "At the new cap, spawning remains blocked.");
        FirstActiveEnemy().GetComponent<EnemyHealth>().ForceKill();
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(1));
    }

    [Test]
    public void HeatResetRestoresFirstCap_AndPermanentOverheatKeepsConcurrentReplacement()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 2, 4 });
        _heat.ApplyEscalationAfterOverheat();
        SpawnWave();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(4));
        _heat.ResetHeatProgressAndEscalation();
        Assert.That(_orbital.MaxActiveEnemies, Is.EqualTo(2));
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(4), "Heat reset must not erase living population membership.");
        _pool.ReleaseAllActive();
        _heat.ApplyEscalationAfterOverheat();
        _overheat.EnterPermanentOverheat();
        SpawnWave();
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_overheat.IsPermanentOverheat, Is.True);
        Assert.That(_heat.CompletedOverheatCycles, Is.EqualTo(1));
        Assert.That(_orbital.MaxActiveEnemies, Is.EqualTo(4));
        FirstActiveEnemy().GetComponent<EnemyHealth>().ForceKill();
        SpawnWave();
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(4));
    }

    [Test]
    public void DeferredEliteAtFullCap_WaitsForBase_SpawnsOnce_AndCanCompleteOverheat()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 1 });
        SpawnWave();
        GameObject survivor = FirstActiveEnemy();
        CreateElites();
        MovePlayer(11f);
        Invoke(_overheat, "OnMaxHeatReached");
        Assert.That(_elites.IsWaitingForBase, Is.True);
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
        Invoke(_elites, "EndOverheatIfNoObjective");
        Assert.That(_overheat.IsOverheating, Is.True);
        MovePlayer(1.02f);
        Assert.That(_player.CanSpawnObjectives, Is.True);
        Invoke(_elites, "TryReleasePendingSpawn");
        Assert.That(_elites.ElitesRemaining, Is.EqualTo(1));
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(2), "Required objectives bypass admission but still contribute to population.");
        Invoke(_elites, "TryReleasePendingSpawn");
        SpawnWave();
        Assert.That(_pool.TotalLeased, Is.EqualTo(2));
        _elites.GetAliveEliteTransforms()[0].GetComponent<EnemyHealth>().ForceKill();
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
        Assert.That(survivor.activeInHierarchy, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PendingObjectiveCancelledByDeathOrExplicitReset_DoesNotLeakMembership(bool reset)
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 1 });
        SpawnWave();
        CreateElites();
        MovePlayer(11f);
        Invoke(_overheat, "OnMaxHeatReached");
        Assert.That(_elites.IsWaitingForBase, Is.True);
        if (reset) _playerHealth.FullHeal(resetPhase: true);
        else _playerHealth.TakeDamage(10000);
        Assert.That(_elites.IsWaitingForBase, Is.False);
        MovePlayer(1.02f);
        Invoke(_elites, "TryReleasePendingSpawn");
        Assert.That(_pool.TotalLeased, Is.EqualTo(1));
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
    }

    [Test]
    public void DeferredBossAtFullCap_WaitsForBase_AndSpawnsCapturedBatchOnce()
    {
        Set(_profile, "_maxActiveEnemiesByCycle", new[] { 1 });
        SpawnWave();
        GameObject root = NewObject("Required bosses");
        BossManager bosses = root.AddComponent<BossManager>();
        Set(bosses, "_overheatManager", _overheat);
        Set(bosses, "_bossPrefab", _prefab);
        Set(bosses, "_overheatCycleIndex", 1);
        Set(bosses, "_multiBossOverheatCycle", 2);
        Set(bosses, "_bossCountOnMultiCycle", 2);
        SetGroundMasks(bosses, 1);
        _lifecycle.SetActive(root, true);
        MovePlayer(11f);
        Invoke(_overheat, "OnMaxHeatReached");
        Assert.That(bosses.IsWaitingForBase, Is.True);
        Assert.That(bosses.HasActiveBosses, Is.False);
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(1));
        Invoke(bosses, "EndOverheatIfNoObjective");
        Assert.That(_overheat.IsOverheating, Is.True);

        Set(bosses, "_bossCountOnMultiCycle", 4);
        MovePlayer(1.02f);
        Assert.That(_player.CanSpawnObjectives, Is.True);
        Invoke(bosses, "TryReleasePendingSpawn");
        // Own instantiated boss cleanup before disabling the manager. This also supplies
        // EditMode lifecycle cleanup for components added by the real spawn path.
        foreach (EnemyHealth boss in bosses.ActiveBosses)
        {
            _created.Add(boss.gameObject);
            _lifecycle.SetActive(boss.gameObject, true);
        }
        Assert.That(bosses.IsWaitingForBase, Is.False);
        Assert.That(bosses.ActiveBosses.Count, Is.EqualTo(2), "The selected batch must bypass capacity and retain its captured count.");
        Assert.That(_orbital.CappedActiveEnemies, Is.EqualTo(3));
        Invoke(bosses, "TryReleasePendingSpawn");
        SpawnWave();
        Assert.That(bosses.ActiveBosses.Count, Is.EqualTo(2));
        Assert.That(_pool.TotalLeased, Is.EqualTo(1), "Boss admission must not reopen ambient capacity.");
        Assert.That(_overheat.IsOverheating, Is.True);
    }

    private ZoneSpawner CreateZone(int count)
    {
        GameObject root = NewObject("In-world zone");
        root.transform.position = new Vector3(25f, 0f, 0f);
        root.AddComponent<BoxCollider>();
        ZoneSpawner zone = root.AddComponent<ZoneSpawner>();
        Set(zone, "_enemyPrefab", _prefab);
        Set(zone, "_spawnCount", count);
        SetGroundMasks(zone, 1);
        _lifecycle.SetActive(root, true);
        return zone;
    }

    private void CreateElites()
    {
        GameObject root = NewObject("Required elites");
        _elites = root.AddComponent<OverheatEliteWaveSpawner>();
        Set(_elites, "_overheatManager", _overheat);
        Set(_elites, "_elites", new[] { new OverheatEliteWaveSpawner.EliteEntry { Prefab = _prefab, Count = 1 } });
        SetGroundMasks(_elites, 1);
        _lifecycle.SetActive(root, true);
    }

    private void SpawnWave() => Invoke(_orbital, "SpawnRouletteWave");
    private GameObject FirstActiveEnemy()
    {
        foreach (EnemyHealth health in _poolHolder.GetComponentsInChildren<EnemyHealth>(false))
            if (health.CurrentHealth > 0) return health.gameObject;
        Assert.Fail("Expected a living leased enemy.");
        return null;
    }

    private void MovePlayer(float y)
    {
        _player.transform.position = Vector3.up * y;
        _player.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    private GameObject NewObject(string name)
    {
        GameObject root = new GameObject(name);
        root.SetActive(false);
        _created.Add(root);
        return root;
    }

    private T NewAsset<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        _assets.Add(asset);
        return asset;
    }

    private static void SetGroundMasks(object target, int mask)
    {
        Set(target, "_groundRaycastMask", (LayerMask)mask);
        Set(target, "_fallbackGroundRaycastMask", (LayerMask)mask);
        Set(target, "_overlapSolidMask", (LayerMask)1);
    }

    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static object Invoke(object target, string name, params object[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(target.GetType().Name, name)).Invoke(target, args);
}
