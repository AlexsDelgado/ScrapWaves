using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EnemyBlasterCollisionTests
{
    private readonly List<GameObject> _objects = new();
    private readonly Vector3 _origin = new(9000f, 9000f, 9000f);
    private GameObject _prefab;
    private EnemyProjectilePool _pool;
    private ChallengeProgressTracker _previousTracker;
    private bool _previousStartupHeld;

    [SetUp]
    public void SetUp()
    {
        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyBullet.prefab");
        Assert.That(_prefab, Is.Not.Null);
        _pool = Go("Enemy bolt test pool").AddComponent<EnemyProjectilePool>();
        Set(_pool, "_container", Go("Enemy bolt test pool contents").transform);
        _previousTracker = ChallengeProgressTracker.Instance;
        var tracker = Go("Enemy bolt test tracking").AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        _previousStartupHeld = EntranceCinematic.StartupHeld;
        SetStartupHeld(false);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _previousTracker);
        SetStartupHeld(_previousStartupHeld);
    }

    [Test]
    public void AuthoredCapsule_FollowsBrightCoreWithoutChangingProjectileStats()
    {
        CapsuleCollider capsule = _prefab.GetComponent<CapsuleCollider>();
        Assert.That(capsule, Is.Not.Null);
        Assert.That(_prefab.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1));
        Assert.That(capsule.direction, Is.EqualTo(2));
        Assert.That(capsule.isTrigger, Is.True);
        Assert.That(capsule.radius * _prefab.transform.localScale.x, Is.EqualTo(0.0455f).Within(0.00001f));
        Assert.That(capsule.height * _prefab.transform.localScale.z, Is.EqualTo(1.47f).Within(0.00001f));
        var serialized = new SerializedObject(_prefab.GetComponent<EnemyProjectile>());
        Assert.That(serialized.FindProperty("_damage").intValue, Is.EqualTo(8));
        Assert.That(serialized.FindProperty("_speed").floatValue, Is.EqualTo(16));
        Assert.That(serialized.FindProperty("_maxLifetime").floatValue, Is.EqualTo(5));
    }

    [TestCase(0)]
    [TestCase(40)]
    public void FastBolt_HitsNearestThinTerrainEvenWhenQueryBufferIsFull(int irrelevantTriggers)
    {
        EnemyProjectile bolt = Bolt(Vector3.forward);
        for (int i = 0; i < irrelevantTriggers; i++)
        {
            BoxCollider ignored = Box("Irrelevant trigger", new Vector3(0, 0, 1 + i * 0.03f), new Vector3(1, 1, 0.01f));
            ignored.isTrigger = true;
        }
        BoxCollider wall = Box("Thin terrain", new Vector3(0, 0, 3), new Vector3(3, 3, 0.01f));
        wall.gameObject.layer = LayerMask.NameToLayer("Terrain");
        PlayerHealth player = Player(new Vector3(0, 0, 5), new Vector3(1, 2, 0.01f));
        Physics.SyncTransforms();

        Step(bolt);

        Assert.That(bolt.gameObject.activeSelf, Is.False, "The bolt must stop before crossing the thin wall in one fast step.");
        Assert.That(player.CurrentHealth, Is.EqualTo(100), "Nearest terrain must shield a player behind it.");
        Assert.That(bolt.transform.position.z - _origin.z, Is.LessThan(3f));
    }

    [TestCase(0.05f, true)]
    [TestCase(0.07f, false)]
    public void FastBolt_PlayerGrazingUsesCoreRadiusNotOuterGlow(float lateralOffset, bool shouldHit)
    {
        EnemyProjectile bolt = Bolt(Vector3.forward);
        PlayerHealth player = Player(new Vector3(lateralOffset, 0, 4), new Vector3(0.02f, 1, 0.01f));
        Physics.SyncTransforms();

        Step(bolt);

        Assert.That(player.CurrentHealth, Is.EqualTo(shouldHit ? 87 : 100));
        Assert.That(bolt.gameObject.activeSelf, Is.EqualTo(!shouldHit));
    }

    [Test]
    public void LongNose_InitialOverlapAndTriggerCallbackDamageOnlyOnce()
    {
        EnemyProjectile bolt = Bolt(Vector3.forward);
        PlayerHealth player = Player(new Vector3(0, 0, 0.7f), new Vector3(1, 2, 0.01f));
        Physics.SyncTransforms();

        Step(bolt);
        Invoke(bolt, "OnTriggerEnter", player.GetComponent<Collider>());
        Step(bolt);

        Assert.That(player.CurrentHealth, Is.EqualTo(87));
        Assert.That(bolt.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void PoolReuse_ResetsConsumptionAndRotatesColliderWithNewFlightDirection()
    {
        EnemyProjectile bolt = Bolt(Vector3.forward);
        BoxCollider first = Box("First thin wall", new Vector3(0, 0, 3), new Vector3(1, 2, 0.01f));
        first.gameObject.layer = LayerMask.NameToLayer("Terrain");
        Physics.SyncTransforms();
        Step(bolt);
        Assert.That(bolt.gameObject.activeSelf, Is.False);
        first.enabled = false;
        Vector3 secondStart = _origin + new Vector3(0, 5, 0);
        bool reused = (bool)Invoke(_pool, "TryLaunchInternal", _prefab, secondStart, Quaternion.identity,
            Vector3.right, 13, 1000f);
        Assert.That(reused, Is.True);
        Assert.That(bolt.gameObject.activeSelf, Is.True, "The same released instance must return from the pool.");
        Assert.That(Vector3.Dot(bolt.transform.forward, Vector3.right), Is.GreaterThan(0.999f));
        BoxCollider second = Box("Second thin wall", new Vector3(3, 5, 0), new Vector3(0.01f, 2, 1));
        second.gameObject.layer = LayerMask.NameToLayer("Terrain");
        Physics.SyncTransforms();
        Assert.That(bolt.GetComponent<CapsuleCollider>().bounds.size.x, Is.GreaterThan(1.46f));

        Step(bolt);

        Assert.That(bolt.gameObject.activeSelf, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Sweep_RespectsLayerMatrixAndExplicitIgnoredColliderPairs(bool ignorePair)
    {
        int terrain = LayerMask.NameToLayer("Terrain");
        bool previous = Physics.GetIgnoreLayerCollision(0, terrain);
        EnemyProjectile bolt = null;
        BoxCollider wall = null;
        try
        {
            Physics.IgnoreLayerCollision(0, terrain, !ignorePair);
            bolt = Bolt(Vector3.forward);
            wall = Box("Ignored thin terrain", new Vector3(0, 0, 3), new Vector3(2, 2, 0.01f));
            wall.gameObject.layer = terrain;
            if (ignorePair) Physics.IgnoreCollision(bolt.GetComponent<Collider>(), wall, true);
            Physics.SyncTransforms();
            Step(bolt);
            Assert.That(bolt.gameObject.activeSelf, Is.True);
        }
        finally
        {
            if (ignorePair && bolt != null && wall != null) Physics.IgnoreCollision(bolt.GetComponent<Collider>(), wall, false);
            Physics.IgnoreLayerCollision(0, terrain, previous);
        }
    }

    private EnemyProjectile Bolt(Vector3 direction)
    {
        GameObject instance = Object.Instantiate(_prefab, _origin, Quaternion.identity);
        _objects.Add(instance);
        var bolt = instance.GetComponent<EnemyProjectile>();
        Invoke(bolt, "Awake");
        Set(bolt, "_hitPushForce", 0f);
        bolt.BindPool(_pool, _prefab);
        bolt.Launch(direction, 13, 1000f);
        return bolt;
    }

    private PlayerHealth Player(Vector3 position, Vector3 size)
    {
        BoxCollider box = Box("Thin player target", position, size);
        box.gameObject.layer = LayerMask.NameToLayer("Player");
        var health = box.gameObject.AddComponent<PlayerHealth>();
        Invoke(health, "Awake");
        Set(health, "_hitInvulnerabilitySeconds", 0f);
        return health;
    }

    private BoxCollider Box(string name, Vector3 position, Vector3 size)
    {
        GameObject go = Go(name);
        go.transform.position = _origin + position;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    private GameObject Go(string name)
    {
        var go = new GameObject(name);
        _objects.Add(go);
        return go;
    }

    private static void Step(EnemyProjectile bolt) => Invoke(bolt, "FixedUpdate");
    private static object Invoke(object target, string method, params object[] arguments) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void SetStartupHeld(bool value) => typeof(EntranceCinematic)
        .GetField("<StartupHeld>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
}
