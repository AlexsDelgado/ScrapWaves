using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RearThreatSensorTests
{
    private readonly List<GameObject> _objects = new();
    private readonly List<Transform> _registered = new();
    private Transform _player;
    private RearThreatSensor _sensor;

    [SetUp]
    public void SetUp()
    {
        _player = Create("Threat sensor player").transform;
        _player.position = new Vector3(10000f, 20000f, 30000f);
        _sensor = Create("Threat sensor").AddComponent<RearThreatSensor>();
        _sensor.BindPlayer(_player);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _objects.Count; i++)
            if (_objects[i] != null && _objects[i].TryGetComponent(out RearThreatSensor sensor)) Invoke(sensor, "OnDisable");
        for (int i = 0; i < _registered.Count; i++) EnemyRegistry.Unregister(_registered[i]);
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        EnemyRegistry.CollectActive(_registered); // Prune destroyed roots that had no registry-member component.
        _registered.Clear();
        _objects.Clear();
    }

    [TestCase(5f, 0f, 0f, true)]
    [TestCase(10f, 0f, 0f, true)]
    [TestCase(10.02f, 0f, 0f, false)]
    [TestCase(5f, 90f, 0f, true)]
    [TestCase(5f, -90f, 0f, true)]
    [TestCase(5f, 91f, 0f, false)]
    [TestCase(5f, -91f, 0f, false)]
    [TestCase(5f, 0f, 3f, true)]
    [TestCase(5f, 0f, -3f, true)]
    [TestCase(5f, 0f, 3.02f, false)]
    [TestCase(5f, 0f, -3.02f, false)]
    public void FiltersUsingRootPlanarDistanceRearArcAndStrictHeight(float distance, float angle, float height, bool expected)
    {
        Enemy(distance, angle, height);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.True);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(expected ? 1 : 0));
    }

    [TestCase(0f, 1f)]
    [TestCase(1f, 1f)]
    [TestCase(2f, 1f)]
    [TestCase(6f, 0.5f)]
    [TestCase(10f, 0f)]
    public void UrgencyIsClampedLinearDistance(float distance, float expected)
    {
        Enemy(distance);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        Assert.That(_sensor.Snapshot[0].Urgency, Is.EqualTo(expected).Within(0.001f));
        Assert.That(_sensor.Snapshot.OverallUrgency, Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void BoundsAndVisualOffsetsDoNotChangeRootAnchorEligibility()
    {
        EnemyHealth eligible = Enemy(5f, -30f, 1.25f);
        GameObject visual = Create("High drone visual");
        visual.transform.SetParent(eligible.transform, false);
        visual.transform.localPosition = Vector3.up * 20f;
        visual.AddComponent<BoxCollider>();
        Enemy(5f, 30f, 3.02f);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(5f).Within(0.005f));
    }

    [Test]
    public void RegistryMembershipAloneDoesNotMakeDeadInactiveOrUnknownObjectsThreats()
    {
        EnemyHealth dead = Enemy(4f, -60f);
        Set(dead, "_currentHealth", 0);
        EnemyHealth inactive = Enemy(4f, 0f);
        inactive.gameObject.SetActive(false);
        EnemyRegistry.Register(inactive.transform); // Defend against a manually registered inactive root.
        GameObject unknown = Create("Registered object without health");
        unknown.transform.position = Position(4f, 60f);
        Register(unknown.transform);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
    }

    [Test]
    public void SandboxDummyUsesItsOwnHealthWithoutEnemyHealth()
    {
        GameObject root = Create("Sandbox dummy");
        root.transform.position = Position(5f);
        root.AddComponent<BoxCollider>();
        WeaponDummyEnemy dummy = root.AddComponent<WeaponDummyEnemy>();
        Set(dummy, "_currentHealth", 100);
        Register(root.transform);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        Set(dummy, "_currentHealth", 0);
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
    }

    [Test]
    public void SamplingWaitsForIntervalButForwardRefreshesEveryPresentationFrame()
    {
        EnemyHealth enemy = Enemy(6f);
        _sensor.RefreshForPresentation(0f);
        Vector3 sampledDirection = _sensor.Snapshot[0].WorldDirection;
        enemy.transform.position = Position(2f);
        _player.rotation = Quaternion.Euler(0f, 20f, 0f);
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(6f));
        Assert.That(_sensor.Snapshot[0].WorldDirection, Is.EqualTo(sampledDirection));
        Assert.That(Vector3.Distance(_sensor.Snapshot.Forward, _player.forward), Is.LessThan(0.0001f));
        _sensor.RefreshForPresentation(0.11f);
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(2f));
    }

    [Test]
    public void DeadAndDisabledMembersDisappearBetweenSamples()
    {
        EnemyHealth enemy = Enemy(4f);
        _sensor.RefreshForPresentation(0f);
        Set(enemy, "_currentHealth", 0);
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero, "Do not wait for delayed destruction or another scan.");
        enemy.ApplyConfiguredMaxHealth(100);
        _sensor.RefreshForPresentation(0.11f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        enemy.GetComponent<EnemyRegistryMember>().enabled = false;
        _sensor.RefreshForPresentation(0.12f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero, "Burrowing can disable membership while the object stays active.");
    }

    [Test]
    public void ExplicitUnregisterInvalidatesImmediatelyAndPooledReuseGetsFreshTrack()
    {
        EnemyHealth enemy = Enemy(4f);
        _sensor.RefreshForPresentation(0f);
        int oldTrack = _sensor.Snapshot[0].TrackId;
        EnemyRegistry.Unregister(enemy.transform);
        Assert.That(_sensor.Snapshot.Count, Is.Zero, "The registry callback invalidates without a presentation call.");
        enemy.gameObject.SetActive(false);
        enemy.gameObject.SetActive(true);
        enemy.ApplyConfiguredMaxHealth(100);
        EnemyRegistry.Register(enemy.transform);
        _sensor.RefreshForPresentation(0.11f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        Assert.That(_sensor.Snapshot[0].TrackId, Is.Not.EqualTo(oldTrack));
    }

    [Test]
    public void DestroyedRootDisappearsEvenWithoutItsRegistryMember()
    {
        GameObject enemy = Create("Manually registered enemy");
        enemy.transform.position = Position(4f);
        enemy.AddComponent<EnemyHealth>().ApplyConfiguredMaxHealth(100);
        Register(enemy.transform);
        _sensor.RefreshForPresentation(0f);
        Object.DestroyImmediate(enemy);
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
    }

    [Test]
    public void RangeHysteresisDoesNotAdmitNewOutsideTargetsAndResetsAfterExit()
    {
        EnemyHealth enemy = Enemy(10.2f);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
        enemy.transform.position = Position(9.9f);
        _sensor.RefreshForPresentation(1f);
        int track = _sensor.Snapshot[0].TrackId;
        enemy.transform.position = Position(10.4f);
        _sensor.RefreshForPresentation(2f);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.EqualTo(track));
        Assert.That(_sensor.Snapshot[0].Urgency, Is.Zero);
        enemy.transform.position = Position(10.6f);
        _sensor.RefreshForPresentation(3f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
        enemy.transform.position = Position(10.2f);
        _sensor.RefreshForPresentation(4f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
    }

    [Test]
    public void ArcHysteresisIsFiveDegreesPerEdgeAndVerticalLimitStaysStrict()
    {
        EnemyHealth enemy = Enemy(4f, 94f);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
        enemy.transform.position = Position(4f, 89f);
        _sensor.RefreshForPresentation(1f);
        int track = _sensor.Snapshot[0].TrackId;
        enemy.transform.position = Position(4f, 94f);
        _sensor.RefreshForPresentation(2f);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.EqualTo(track));
        enemy.transform.position = Position(4f, 96f);
        _sensor.RefreshForPresentation(3f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
        enemy.transform.position = Position(4f, 0f, 3f);
        _sensor.RefreshForPresentation(4f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        enemy.transform.position = Position(4f, 0f, 3.02f);
        _sensor.RefreshForPresentation(5f);
        Assert.That(_sensor.Snapshot.Count, Is.Zero, "Height has no exit hysteresis.");
    }

    [Test]
    public void ClustersUseNearestSeedAndDoNotChainThroughIntermediateEnemies()
    {
        Enemy(3f, -70f);
        Enemy(4f, -58f);
        Enemy(5f, -46f);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(2));
        Assert.That(_sensor.Snapshot[0].EnemyCount, Is.EqualTo(2));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(3f).Within(0.005f));
        Assert.That(_sensor.Snapshot[1].EnemyCount, Is.EqualTo(1));
        Assert.That(_sensor.Snapshot[1].Distance, Is.EqualTo(5f).Within(0.005f));
    }

    [Test]
    public void DeterministicSelectionDoesNotDependOnRegistryInsertionOrder()
    {
        for (int i = 0; i < 6; i++) Enemy(3f + i, -75f + i * 30f);
        _sensor.RefreshForPresentation(0f);
        RearThreatSnapshot first = _sensor.Snapshot;
        Assert.That(first.Count, Is.EqualTo(5));
        for (int i = 0; i < _registered.Count; i++) EnemyRegistry.Unregister(_registered[i]);
        for (int i = _registered.Count - 1; i >= 0; i--) EnemyRegistry.Register(_registered[i]);
        _sensor.BindPlayer(null);
        _sensor.BindPlayer(_player);
        _sensor.RefreshForPresentation(1f);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.That(_sensor.Snapshot[i].Distance, Is.EqualTo(first[i].Distance));
            Assert.That(_sensor.Snapshot[i].WorldDirection, Is.EqualTo(first[i].WorldDirection));
        }
    }

    [Test]
    public void GroupTrackSurvivesSeedChangeAndSeedDespawn()
    {
        EnemyHealth first = Enemy(4f, -4f);
        EnemyHealth second = Enemy(5f, 4f);
        _sensor.RefreshForPresentation(0f);
        int track = _sensor.Snapshot[0].TrackId;
        first.transform.position = Position(6f, -4f);
        _sensor.RefreshForPresentation(1f);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.EqualTo(track));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(5f).Within(0.005f));
        EnemyRegistry.Unregister(second.transform);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.EqualTo(track));
        Assert.That(_sensor.Snapshot[0].EnemyCount, Is.EqualTo(1));
    }

    [Test]
    public void ChallengerNeedsHalfMeterAdvantageButAllEnemiesContributeUrgency()
    {
        Set(_sensor, "_maxSpikes", 1);
        Enemy(8f, -30f);
        _sensor.RefreshForPresentation(0f);
        int retainedTrack = _sensor.Snapshot[0].TrackId;
        EnemyHealth challenger = Enemy(7.7f, 30f);
        _sensor.RefreshForPresentation(1f);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.EqualTo(retainedTrack));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(8f).Within(0.005f));
        Assert.That(_sensor.Snapshot.OverallUrgency, Is.EqualTo((10f - 7.7f) / 8f).Within(0.001f));
        challenger.transform.position = Position(7.4f, 30f);
        _sensor.RefreshForPresentation(2f);
        Assert.That(_sensor.Snapshot[0].TrackId, Is.Not.EqualTo(retainedTrack));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(7.4f).Within(0.005f));
    }

    [Test]
    public void RetainedSnapshotIsAnImmutableValueCopy()
    {
        EnemyHealth enemy = Enemy(8f);
        _sensor.RefreshForPresentation(0f);
        RearThreatSnapshot previous = _sensor.Snapshot;
        enemy.transform.position = Position(2f);
        _sensor.RefreshForPresentation(1f);
        Assert.That(previous[0].Distance, Is.EqualTo(8f));
        Assert.That(previous.OverallUrgency, Is.EqualTo(0.25f));
        Assert.That(_sensor.Snapshot[0].Distance, Is.EqualTo(2f));
    }

    [Test]
    public void MissingInactiveDeadAndReboundPlayersClearState()
    {
        Enemy(4f);
        _sensor.RefreshForPresentation(0f);
        _player.gameObject.SetActive(false);
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False);
        _player.gameObject.SetActive(true);
        _sensor.RefreshForPresentation(0.02f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        _sensor.BindPlayer(null);
        _sensor.RefreshForPresentation(0.03f);
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False);

        GameObject replacement = Create("Replacement player");
        replacement.transform.position = _player.position;
        PlayerHealth health = replacement.AddComponent<PlayerHealth>();
        Set(health, "_currentHealth", 10);
        _sensor.BindPlayer(replacement.transform);
        _sensor.RefreshForPresentation(0.04f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        Set(health, "_currentHealth", 0);
        _sensor.RefreshForPresentation(0.05f);
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
    }

    [Test]
    public void DestroyedPlayerRecoversFromSameScenePlayerMovementSingleton()
    {
        FieldInfo singleton = typeof(PlayerMovement).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
        object original = singleton.GetValue(null);
        try
        {
            Enemy(4f);
            _sensor.RefreshForPresentation(0f);
            GameObject replacement = Create("Respawned player");
            replacement.transform.position = _player.position;
            PlayerMovement movement = replacement.AddComponent<PlayerMovement>();
            singleton.SetValue(null, movement);
            Object.DestroyImmediate(_player.gameObject);
            _sensor.RefreshForPresentation(0.01f);
            Assert.That(_sensor.Player, Is.EqualTo(replacement.transform));
            Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
        }
        finally { singleton.SetValue(null, original); }
    }

    [Test]
    public void DisabledSensorClearsAndCanResumeWithFreshTracking()
    {
        Enemy(4f);
        _sensor.RefreshForPresentation(0f);
        _sensor.enabled = false;
        Invoke(_sensor, "OnDisable");
        _sensor.RefreshForPresentation(1f);
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False);
        Assert.That(_sensor.Snapshot.Count, Is.Zero);
        _sensor.enabled = true;
        Invoke(_sensor, "OnEnable");
        _sensor.RefreshForPresentation(1f);
        Assert.That(_sensor.Snapshot.Count, Is.EqualTo(1));
    }

    [Test]
    public void CameraReferenceUsesGameplayForwardAndFallsBackToPlayerWhenMissing()
    {
        GameObject cameraObject = Create("Threat reference camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        ThirdPersonCamera gameplay = cameraObject.AddComponent<ThirdPersonCamera>();
        Set(gameplay, "_gameplayRotation", Quaternion.Euler(0f, 90f, 0f));
        cameraObject.transform.rotation = Quaternion.Euler(0f, -40f, 0f);
        Enemy(4f, 0f);
        _sensor.CameraReference = camera;
        _sensor.ReferenceFrame = RearThreatReferenceFrame.CameraFacing;
        _sensor.RefreshForPresentation(0f);
        Assert.That(Vector3.Distance(_sensor.Snapshot.Forward, Vector3.right), Is.LessThan(0.0001f));
        _sensor.CameraReference = null;
        _sensor.RefreshForPresentation(0.01f);
        Assert.That(_sensor.Snapshot.Forward, Is.EqualTo(Vector3.forward));
    }

    [Test]
    public void EditorReferencePreviewUsesEditedCameraPoseWithoutScanning()
    {
        GameObject cameraObject = Create("Edited threat reference camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        ThirdPersonCamera gameplay = cameraObject.AddComponent<ThirdPersonCamera>();
        Set(gameplay, "_gameplayRotation", Quaternion.identity);
        cameraObject.transform.rotation = Quaternion.Euler(25f, 70f, 0f);
        _sensor.CameraReference = camera;
        _sensor.GameplayCamera = gameplay;
        _sensor.ReferenceFrame = RearThreatReferenceFrame.CameraFacing;

        Vector3 expected = Quaternion.Euler(0f, 70f, 0f) * Vector3.forward;
        Assert.That(Vector3.Distance(_sensor.GetReferenceForward(), expected), Is.LessThan(0.0001f));
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False, "Editor orientation preview must not scan enemies.");
        _sensor.CameraReference = null;
        Assert.That(Vector3.Distance(_sensor.GetReferenceForward(), expected), Is.LessThan(0.0001f));

        cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _player.rotation = Quaternion.Euler(0f, 30f, 0f);
        Assert.That(Vector3.Distance(_sensor.GetReferenceForward(), _player.forward), Is.LessThan(0.0001f));
        Assert.That(_sensor.Snapshot.IsValidPlayer, Is.False);
    }

    [Test]
    public void ValidationKeepsFullStrengthStrictlyInsideDetectionRadius()
    {
        Set(_sensor, "_detectionRadius", 1f);
        Set(_sensor, "_fullStrengthDistance", 2f);
        Invoke(_sensor, "OnValidate");
        Assert.That(Get<float>(_sensor, "_fullStrengthDistance"), Is.LessThan(Get<float>(_sensor, "_detectionRadius")));
        Enemy(0.5f);
        _sensor.RefreshForPresentation(0f);
        Assert.That(_sensor.Snapshot.OverallUrgency, Is.EqualTo(1f));
    }

    [Test]
    public void RegistryRemovalNotificationIsOncePerSuccessfulRemoval()
    {
        EnemyHealth enemy = Enemy(4f);
        int removals = 0;
        Action<Transform> handler = root => { if (root == enemy.transform) removals++; };
        EnemyRegistry.Unregistered += handler;
        try
        {
            EnemyRegistry.Register(enemy.transform);
            EnemyRegistry.Unregister(enemy.transform);
            EnemyRegistry.Unregister(enemy.transform);
            Assert.That(removals, Is.EqualTo(1));
        }
        finally { EnemyRegistry.Unregistered -= handler; }
    }

    [Test]
    public void RepeatedHundredEnemyScansAllocateNothingAndPreserveRandomAndHealth()
    {
        for (int i = 0; i < 100; i++) Enemy(2f + i % 8, -80f + (i * 17 % 160));
        for (int i = 0; i < 5; i++) _sensor.RefreshForPresentation(i);
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 5; i < 105; i++) _sensor.RefreshForPresentation(i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(randomState));
        Assert.That(_sensor.Snapshot.Count, Is.LessThanOrEqualTo(5));
        for (int i = 0; i < _registered.Count; i++)
            Assert.That(_registered[i].GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(100));
    }

    [Test]
    public void DetectionDoesNotEmitCombatOrDeathEvents()
    {
        EnemyHealth enemy = Enemy(4f);
        int damageEvents = 0;
        int deathEvents = 0;
        Action<WeaponDamageRoll> damage = _ => damageEvents++;
        Action death = () => deathEvents++;
        WeaponDamageResolver.OnDamageResolved += damage;
        enemy.OnDied += death;
        try
        {
            for (int i = 0; i < 10; i++) _sensor.RefreshForPresentation(i);
            Assert.That(damageEvents, Is.Zero);
            Assert.That(deathEvents, Is.Zero);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100));
        }
        finally
        {
            WeaponDamageResolver.OnDamageResolved -= damage;
            enemy.OnDied -= death;
        }
    }

    private GameObject Create(string name)
    {
        GameObject result = new(name);
        _objects.Add(result);
        return result;
    }

    private EnemyHealth Enemy(float distance, float angle = 0f, float height = 0f)
    {
        GameObject root = Create("Rear threat enemy");
        root.transform.position = Position(distance, angle, height);
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        root.AddComponent<EnemyRegistryMember>();
        health.ApplyConfiguredMaxHealth(100);
        Register(root.transform);
        return health;
    }

    private Vector3 Position(float distance, float angle = 0f, float height = 0f)
    {
        return _player.position + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.back * distance + Vector3.up * height;
    }

    private void Register(Transform root)
    {
        EnemyRegistry.Register(root);
        _registered.Add(root);
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }

    private static T Get<T>(object target, string field)
    {
        return (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    }
}
