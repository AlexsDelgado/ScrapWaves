using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class EnemyEmergenceIntegrationTests
{
    private readonly List<GameObject> _created = new();
    private readonly EnemyEmergenceTestLifecycle _lifecycle = new();
    private UnityEngine.Random.State _randomState;
    private int _pauseLocks;

    [SetUp]
    public void SetUp()
    {
        _randomState = UnityEngine.Random.state;
        _pauseLocks = GameplayPause.LockCount;
        GameplayPause.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] != null)
            {
                EnemyRegistry.Unregister(_created[i].transform);
                _lifecycle.Destroy(_created[i]);
            }
        }
        _created.Clear();
        UnityEngine.Random.state = _randomState;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void Emergence_BlocksDirectDotAndChildHitZoneDamageWithoutOverwritingCombatInvincibility()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        GameObject head = NewObject("Head");
        head.transform.SetParent(enemy.transform, false);
        EnemyDamageHitZone zone = head.AddComponent<EnemyDamageHitZone>();
        _lifecycle.SetActive(enemy, true);
        health.ApplyConfiguredMaxHealth(100);

        Assert.That(health.ApplyDamage(10), Is.False, "Pending placement is already protected.");
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(health.ApplyDamage(10), Is.False);
        Assert.That(health.ApplyDotDamage(10), Is.False);
        Assert.That(zone.ApplyDamage(10), Is.False);
        health.NotifyDamagedByWeapon("emergence-test");
        Assert.That(health.LastDamagingWeaponId, Is.Null);
        Assert.That(health.CurrentHealth, Is.EqualTo(100));

        health.SetInvincible(true);
        emergence.Simulate(1f);
        Assert.That(health.IsInvincible, Is.True, "Completion must not clear another behavior's immunity.");
        Assert.That(health.ApplyDamage(10), Is.False);
        Assert.That(health.ApplyDotDamage(10), Is.True, "Historical combat invincibility still permits DoT.");
        health.SetInvincible(false);
        Assert.That(zone.ApplyDamage(10), Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(78));
    }

    [Test]
    public void ForceKillDuringRise_CancelsWithoutAStaleGameplayUnlock()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        _lifecycle.SetActive(enemy, true);
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        int deaths = 0;
        health.OnDied += () => deaths++;
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        emergence.Simulate(0.25f);

        health.ForceKill();
        health.ForceKill();
        emergence.Simulate(100f);

        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(emergence.IsEmerging, Is.False);
        Assert.That(emergence.BlocksGameplayNow, Is.True,
            "ForceKill is allowed, but a cancelled active GameObject must never resume behavior.");
    }

    [Test]
    public void ContactDamage_IsBlockedWhenCalledDirectlyAndResumesAfterCompletion()
    {
        // Player damage reports challenge progress. Supply the singleton without calling its
        // PlayMode-only DontDestroyOnLoad initialization, and preserve any preexisting instance.
        FieldInfo trackerInstance = typeof(ChallengeProgressTracker).GetField(
            "<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(trackerInstance, Is.Not.Null);
        object previousTracker = trackerInstance.GetValue(null);
        ChallengeProgressTracker tracker = NewObject("Emergence contact challenge tracker").AddComponent<ChallengeProgressTracker>();
        trackerInstance.SetValue(null, tracker);
        try
        {
            GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
            EnemyContactDamage contact = enemy.AddComponent<EnemyContactDamage>();
            SetField(contact, "_contactPushForce", 0f);
            PlayerHealth player = NewObject("Player").AddComponent<PlayerHealth>();
            _lifecycle.SetActive(player.gameObject, true);
            _lifecycle.SetActive(enemy, true);
            int healthBefore = player.CurrentHealth;

            contact.TryApplyContactDamage(player);
            emergence.BeginEmergence(Vector3.zero, Vector3.up);
            contact.TryApplyContactDamage(player);
            Assert.That(player.CurrentHealth, Is.EqualTo(healthBefore));
            emergence.Simulate(1f);
            contact.TryApplyContactDamage(player);
            Assert.That(player.CurrentHealth, Is.LessThan(healthBefore));
        }
        finally { trackerInstance.SetValue(null, previousTracker); }
    }

    [Test]
    public void Registry_KeepsSpawnCountsButExcludesEmergenceFromTargetingAndSuctionCollections()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        enemy.transform.position = new Vector3(10000f, 0f, 10000f);
        _lifecycle.SetActive(enemy, true);
        int previousCount = EnemyRegistry.ActiveCount;
        EnemyRegistry.Register(enemy.transform);
        emergence.BeginEmergence(enemy.transform.position, Vector3.up);
        Vector3 from = enemy.transform.position - Vector3.forward * 2f;
        List<Transform> targets = new();
        List<Collider> colliders = new();
        Vector3[] line = { from, from + Vector3.forward * 4f };

        Assert.That(EnemyRegistry.ActiveCount, Is.EqualTo(previousCount + 1));
        Assert.That(EnemyRegistry.TryGetClosestOnPlane(from, 5f, out _), Is.False);
        Assert.That(EnemyRegistry.TryGetRandomOnPlane(from, 5f, out _), Is.False);
        Assert.That(EnemyRegistry.TryGetClosestOnPlaneInCone(from, Vector3.forward, 5f, 90f, out _), Is.False);
        Assert.That(EnemyRegistry.TryGetClosestOnPlaneWithinVerticalDelta(from, 5f, 5f, out _), Is.False);
        EnemyRegistry.CollectClosestNearPolyline(line, line.Length, 2f, 5, targets, null);
        CollectionAssert.DoesNotContain(targets, enemy.transform);
        EnemyRegistry.CollectActive(targets);
        CollectionAssert.DoesNotContain(targets, enemy.transform);
        EnemyRegistry.CollectActiveEnemyColliders(colliders);
        CollectionAssert.DoesNotContain(colliders, enemy.GetComponent<Collider>());

        emergence.Simulate(1f);
        Assert.That(EnemyRegistry.ActiveCount, Is.EqualTo(previousCount + 1));
        Assert.That(EnemyRegistry.TryGetClosestOnPlane(from, 5f, out Transform closest), Is.True);
        Assert.That(closest, Is.EqualTo(enemy.transform));
    }

    [Test]
    public void KnockbackDuringEmergence_IsDiscardedInsteadOfAccumulatingForCompletion()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        EnemyKnockbackReceiver knockback = enemy.AddComponent<EnemyKnockbackReceiver>();
        _lifecycle.SetActive(enemy, true);
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        knockback.ApplyKnockback(Vector3.left, 10f);
        Assert.That(knockback.ConsumeDisplacement(0.1f), Is.EqualTo(Vector3.zero));
        emergence.Simulate(1f);
        Assert.That(knockback.ConsumeDisplacement(0.1f), Is.EqualTo(Vector3.zero));
        knockback.ApplyKnockback(Vector3.left, 10f);
        Assert.That(knockback.ConsumeDisplacement(0.1f).sqrMagnitude, Is.GreaterThan(0f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FreezeAndEmergence_ReleaseTheirOwnGatesIndependently(bool freezeEndsFirst)
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        EnemyFollow follower = enemy.AddComponent<EnemyFollow>();
        _lifecycle.SetActive(enemy, true);
        WeaponMovementFreezeStatus freeze = enemy.AddComponent<WeaponMovementFreezeStatus>();
        freeze.Refresh(2f);
        emergence.BeginEmergence(Vector3.zero, Vector3.up);

        if (freezeEndsFirst)
        {
            freeze.Simulate(2f);
            Assert.That(follower.enabled, Is.True);
            Assert.That(emergence.BlocksGameplayNow, Is.True,
                "A status restoring the follower flag must not bypass emergence.");
            Vector3 position = enemy.transform.position;
            InvokeUpdate(follower);
            Assert.That(enemy.transform.position, Is.EqualTo(position));
            emergence.Simulate(1f);
        }
        else
        {
            emergence.Simulate(1f);
            Assert.That(follower.enabled, Is.False, "Emergence must not clear a still-active freeze.");
            freeze.Simulate(2f);
        }

        Assert.That(follower.enabled, Is.True);
        Assert.That(emergence.BlocksGameplayNow, Is.False);
    }

    [Test]
    public void Dormancy_PausesTheRemainingRiseAndRestoresCollisionOnlyAfterCompletion()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out Transform pivot);
        EnemyVerticalEngagement engagement = enemy.AddComponent<EnemyVerticalEngagement>();
        _lifecycle.SetActive(enemy, true);
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        emergence.Simulate(0.3f);
        float sampledDuration = emergence.SampledDuration;
        float progress = emergence.NormalizedProgress;
        Vector3 partialPose = pivot.localPosition;

        engagement.EnterDormancy();
        emergence.Simulate(10f);
        Assert.That(engagement.IsDormant, Is.True);
        Assert.That(emergence.NormalizedProgress, Is.EqualTo(progress));
        engagement.ExitDormancy();
        Assert.That(emergence.SampledDuration, Is.EqualTo(sampledDuration));
        Assert.That(Vector3.Distance(pivot.localPosition, partialPose), Is.LessThan(0.0001f));
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.False);
        emergence.Simulate(0.8f);
        Assert.That(emergence.BlocksGameplayNow, Is.False);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
    }

    [Test]
    public void PendingPlacementWhileDormant_DoesNotCaptureDormantFlagsOrStartUntilWake()
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out Transform pivot);
        EnemyVerticalEngagement engagement = enemy.AddComponent<EnemyVerticalEngagement>();
        _lifecycle.SetActive(enemy, true);
        engagement.EnterDormancy();

        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        emergence.BeginAtCurrentPlacement();
        emergence.Simulate(10f);

        Assert.That(emergence.IsEmerging, Is.False);
        Assert.That(emergence.SampledDuration, Is.Zero);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.False);
        Assert.That(pivot.GetComponentInChildren<Renderer>().enabled, Is.False);
        engagement.ExitDormancy();
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(emergence.IsEmerging, Is.True);
        emergence.Simulate(1f);
        Assert.That(emergence.BlocksGameplayNow, Is.False);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
        Assert.That(pivot.GetComponentInChildren<Renderer>().enabled, Is.True);
        Assert.That(pivot.GetComponentInChildren<Renderer>().forceRenderingOff, Is.False);
    }

    [Test]
    public void OrbitalPlacement_BuriesAsymmetricModelUsingFinalFacingOnSlopedGround()
    {
        // A long Z-axis body becomes wide across this X-axis slope when the orbital spawn faces west.
        // Measuring burial before the final 90-degree yaw leaves its uphill corners above the plane.
        const int groundLayer = 30;
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _created.Add(ground);
        ground.name = "Emergence sloped ground";
        ground.layer = groundLayer;
        ground.transform.SetPositionAndRotation(new Vector3(10000f, 0f, 10000f), Quaternion.Euler(0f, 0f, 30f));
        ground.transform.localScale = new Vector3(30f, 1f, 30f);
        Vector3 surfacePoint = ground.transform.TransformPoint(Vector3.up * 0.5f);
        Vector3 surfaceNormal = ground.transform.up;
        Transform player = NewObject("Orbital player target").transform;
        player.position = new Vector3(10000f, surfacePoint.y, 10000f);
        GameObject prefab = NewEnemy(out EnemyEmergence sourceEmergence, out Transform sourcePivot);
        sourcePivot.GetComponentInChildren<Renderer>().transform.localScale = new Vector3(0.4f, 1f, 8f);
        _lifecycle.SetActive(prefab, true);
        sourceEmergence.BeginEmergence(Vector3.zero, Vector3.up);
        sourceEmergence.Simulate(1f);
        Physics.SyncTransforms();

        EnemyPoolRegistry registry = EnemyPoolRegistry.Instance;
        bool poolingWasEnabled = registry != null && (bool)GetField(registry, "_useEnemyPool");
        if (registry != null) SetField(registry, "_useEnemyPool", false);
        GameObject instance = null;
        try
        {
            bool placed = OrbitalSpawnPlacement.TrySpawnAtOrbitalPoint(
                player, prefab, 2, 4f, 4f, 0f,
                1 << groundLayer, 0, 1 << groundLayer,
                20f, 50f, 0f, 0.05f, 3, 0.05f, 0.2f,
                out instance, out _, out string placementLog);
            if (instance != null) _created.Add(instance);
            Assert.That(placed, Is.True, placementLog);
            Vector3 facing = player.position - instance.transform.position;
            facing.y = 0f;
            Assert.That(Vector3.Dot(instance.transform.forward, facing.normalized), Is.GreaterThan(0.999f));
            EnemyEmergence emergence = instance.GetComponent<EnemyEmergence>();
            Assert.That(emergence.IsEmerging, Is.True);
            Assert.That(emergence.SampledDuration, Is.EqualTo(1f));
            Bounds bounds = emergence.VisualPivot.GetComponentInChildren<Renderer>().bounds;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        Assert.That(Vector3.Dot(corner - surfacePoint, surfaceNormal), Is.LessThanOrEqualTo(0.001f),
                            "Final orbital yaw must be applied before measuring the complete body's burial depth.");
                    }
            emergence.Simulate(1f);
            Assert.That(Vector3.Distance(emergence.VisualPivot.localPosition, sourcePivot.localPosition), Is.LessThan(0.0001f));
        }
        finally
        {
            if (registry != null) SetField(registry, "_useEnemyPool", poolingWasEnabled);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DormantPoolReturn_RestoresFlagsRegardlessOfLifecycleCallbackOrder(bool emergenceFirst)
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out Transform pivot);
        EnemyVerticalEngagement engagement = enemy.AddComponent<EnemyVerticalEngagement>();
        Vector3 rest = pivot.localPosition;
        _lifecycle.SetActive(enemy, true);
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        emergence.Simulate(0.3f);
        engagement.EnterDormancy();

        if (emergenceFirst)
        {
            emergence.OnPoolDespawn();
            engagement.OnPoolDespawn();
        }
        else
        {
            engagement.OnPoolDespawn();
            emergence.OnPoolDespawn();
        }
        _lifecycle.SetActive(enemy, false);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
        Assert.That(pivot.GetComponentInChildren<Renderer>().enabled, Is.True);
        Assert.That(Vector3.Distance(pivot.localPosition, rest), Is.LessThan(0.0001f));
        _lifecycle.SetActive(enemy, true);
        engagement.OnPoolSpawn();
        emergence.OnPoolSpawn();
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        emergence.Simulate(1f);
        Assert.That(engagement.IsDormant, Is.False);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
        Assert.That(emergence.BlocksGameplayNow, Is.False);
    }

    [TestCase(typeof(ChargerEnemyBehavior), "Chase", "Charging", 1f)]
    [TestCase(typeof(HellfireSlimeBehavior), "Seek", "Launching", 1f)]
    [TestCase(typeof(FlyingRangedBehavior), "Approaching", "Locking", 5f)]
    [TestCase(typeof(BomberDroneBehavior), "Engage", "Dash", 5f)]
    public void Behavior_CannotEngageNearbyPlayerUntilRiseCompletes(Type behaviorType, string initialState, string nextState, float distance)
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out _);
        EnemyBehaviorBase behavior = (EnemyBehaviorBase)enemy.AddComponent(behaviorType);
        Transform player = NewObject("Player target").transform;
        player.position = new Vector3(0f, 0f, distance);
        _lifecycle.SetActive(enemy, true);
        SetField(behavior, "_player", player, typeof(EnemyBehaviorBase));
        emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Vector3 restPosition = enemy.transform.position;

        InvokeUpdate(behavior);
        Assert.That(GetField(behavior, "_state").ToString(), Is.EqualTo(initialState));
        Assert.That(enemy.transform.position, Is.EqualTo(restPosition));
        Assert.That(enemy.GetComponent<EnemyHealth>().IsInvincible, Is.False,
            "Hellfire must not enter its launch immunity before emergence finishes.");
        emergence.Simulate(1f);
        InvokeUpdate(behavior);
        Assert.That(GetField(behavior, "_state").ToString(), Is.EqualTo(nextState));
    }

    [TestCase(typeof(FlyingRangedBehavior), 12f, 0f, 15.5f)]
    [TestCase(typeof(FlyingRangedBehavior), 2f, 20f, 5.5f)]
    [TestCase(typeof(BomberDroneBehavior), 12f, 0f, 15.5f)]
    [TestCase(typeof(BomberDroneBehavior), 2f, 20f, 5.5f)]
    public void FlyingEmergence_UsesGroundRelativeHoverAndPreservesEndpoint(Type behaviorType, float groundY, float playerY, float expectedY)
    {
        GameObject enemy = NewEnemy(out EnemyEmergence emergence, out Transform pivot);
        enemy.transform.position = new Vector3(9f, groundY + 0.5f, -7f);
        EnemyBehaviorBase behavior = (EnemyBehaviorBase)enemy.AddComponent(behaviorType);
        Rigidbody body = enemy.AddComponent<Rigidbody>();
        Transform player = NewObject("Player target").transform;
        player.position = new Vector3(20f, playerY, 20f);
        _lifecycle.SetActive(enemy, true);
        SetField(behavior, "_player", player, typeof(EnemyBehaviorBase));
        Vector3 expected = new(9f, expectedY, -7f);
        Assert.That(behavior.ResolveEmergenceRestPosition(new Vector3(9f, groundY, -7f)), Is.EqualTo(expected));
        Vector3 localRest = new(0f, 0.75f, 0f);

        emergence.BeginEmergence(new Vector3(9f, groundY, -7f), Vector3.up);
        Assert.That(enemy.transform.position, Is.EqualTo(expected));
        player.position += Vector3.up * 10f;
        emergence.Simulate(0.5f);
        InvokeUpdate(behavior);
        Assert.That(enemy.transform.position, Is.EqualTo(expected), "The endpoint stays fixed throughout the rise.");
        emergence.Simulate(0.5f);
        Assert.That(enemy.transform.position, Is.EqualTo(expected));
        Assert.That(Vector3.Distance(pivot.localPosition, localRest), Is.LessThan(0.0001f));
        Assert.That(body.isKinematic, Is.True, "Completion must preserve the flight behavior's physics ownership.");
    }

    [TestCase("Enemy")]
    [TestCase("EnemyPro")]
    [TestCase("Chaser")]
    [TestCase("Chaser (variant)")]
    [TestCase("Chaser_Elite")]
    [TestCase("Slime (variant)")]
    [TestCase("Slime_Elite")]
    [TestCase("Drone")]
    [TestCase("Drone (variant)")]
    [TestCase("Drone_Elite")]
    public void GameplayEnemyPrefab_HasExactlyOneEnabledEmergenceComponent(string prefabName)
    {
        string path = "Assets/Prefabs/" + prefabName + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        EnemyEmergence[] components = prefab.GetComponentsInChildren<EnemyEmergence>(true);
        Assert.That(components, Has.Length.EqualTo(1), path);
        Assert.That(components[0].gameObject, Is.EqualTo(prefab), path);
        Assert.That(components[0].enabled, Is.True, path);
        Assert.That(components[0].VisualPivot, Is.Not.Null, path);
        Assert.That(components[0].VisualPivot.IsChildOf(prefab.transform), Is.True, path);
        Assert.That(components[0].VisualPivot.GetComponentsInChildren<Renderer>(true), Is.Not.Empty, path);
        Assert.That(components[0].VisualPivot.GetComponentsInChildren<Collider>(true), Is.Empty,
            "Only art belongs below the moving presentation pivot: " + path);
        Assert.That(components[0].MinimumDuration, Is.GreaterThanOrEqualTo(0f), path);
        Assert.That(components[0].MaximumDuration, Is.GreaterThanOrEqualTo(components[0].MinimumDuration), path);
    }

    [TestCase("Boss")]
    [TestCase("Boss_2")]
    [TestCase("Stalker")]
    [TestCase("Destroyer_Boss")]
    public void BossPrefab_DoesNotHaveAnEmergenceComponent(string prefabName)
    {
        string path = "Assets/Prefabs/" + prefabName + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        Assert.That(prefab.GetComponentsInChildren<EnemyEmergence>(true), Is.Empty, path);
    }

    [Test]
    public void TutorialActors_AllHaveEmergenceWithoutLosingTheirLessonComponents()
    {
        const string path = "Assets/Scenes/GameplayScene.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        Scene previousActive = SceneManager.GetActiveScene();
        if (openedForTest) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            TutorialEnemy[] actors = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TutorialEnemy>(true)).ToArray();
            Assert.That(actors, Has.Length.EqualTo(8));
            CollectionAssert.AreEquivalent(new[]
            {
                "1 - Charger", "2 - Junk Slime", "2 - Vigilance Drone", "2 - Charger",
                "3 - Moving slime 1", "3 - Moving slime 2", "3 - Moving slime 3", "3 - Moving slime 4"
            }, actors.Select(actor => actor.name).ToArray());
            foreach (TutorialEnemy actor in actors)
            {
                Assert.That(actor.GetComponent<EnemyHealth>(), Is.Not.Null, actor.name);
                EnemyEmergence emergence = actor.GetComponent<EnemyEmergence>();
                Assert.That(emergence, Is.Not.Null, actor.name);
                Assert.That(emergence.enabled, Is.True, actor.name);
                Assert.That(emergence.VisualPivot, Is.Not.Null, actor.name);
                Assert.That(emergence.VisualPivot.GetComponentsInChildren<Collider>(true), Is.Empty, actor.name);
                Assert.That(new SerializedObject(actor).FindProperty("_stationary").boolValue,
                    Is.EqualTo(!actor.name.StartsWith("3 -", StringComparison.Ordinal)), actor.name);
            }
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            if (openedForTest) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [TestCase(typeof(FlyingRangedBehavior))]
    [TestCase(typeof(BomberDroneBehavior))]
    public void FlyingHover_IgnoresPlayerJumpAndHoldsAltitudeWithoutGround(Type behaviorType)
    {
        EnemyBehaviorBase behavior = NewHoveringDrone(behaviorType, out Transform player, out BoxCollider ground);
        SetField(behavior, "_hoverHeight", 4f);
        behavior.transform.position = behavior.ResolveEmergenceRestPosition(new Vector3(10000f, 0f, 10000f));
        Vector3 rest = behavior.transform.position;
        BoxCollider trigger = NewObject("Hover trigger above ground").AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.transform.position = rest + Vector3.up * 5f;
        trigger.gameObject.layer = ground.gameObject.layer;
        Physics.SyncTransforms();

        foreach (float playerY in new[] { 0f, 12f, 3f, 0f })
        {
            player.position = new Vector3(10010f, playerY, 10000f);
            InvokeHover(behavior, 0.1f);
            Assert.That(behavior.transform.position, Is.EqualTo(rest),
                "Player jumping and trigger volumes must not change ground-relative altitude.");
        }

        ground.enabled = false;
        Physics.SyncTransforms();
        InvokeHover(behavior, 0.5f);
        Assert.That(behavior.transform.position, Is.EqualTo(rest), "A missed ground probe must hold altitude.");
    }

    [TestCase(typeof(FlyingRangedBehavior))]
    [TestCase(typeof(BomberDroneBehavior))]
    public void FlyingHover_CapsAscentAndDescentOverPlatformsWithoutOvershoot(Type behaviorType)
    {
        EnemyBehaviorBase behavior = NewHoveringDrone(behaviorType, out _, out BoxCollider ground);
        SetField(behavior, "_maxVerticalSpeed", 2f);
        Vector3 start = behavior.transform.position;
        ground.transform.position += Vector3.up * 10f;
        Physics.SyncTransforms();
        InvokeHover(behavior, 0.2f);
        Assert.That(behavior.transform.position.y, Is.EqualTo(start.y + 0.4f).Within(0.0001f));
        Assert.That(behavior.transform.position.x, Is.EqualTo(start.x));
        Assert.That(behavior.transform.position.z, Is.EqualTo(start.z));

        ground.transform.position -= Vector3.up * 20f;
        Physics.SyncTransforms();
        InvokeHover(behavior, 0.2f);
        Assert.That(behavior.transform.position.y, Is.EqualTo(start.y).Within(0.0001f));
        InvokeHover(behavior, 0f);
        Assert.That(behavior.transform.position.y, Is.EqualTo(start.y).Within(0.0001f));

        ground.transform.position += Vector3.up * 10f;
        behavior.transform.position = start - Vector3.up * 0.1f;
        Physics.SyncTransforms();
        InvokeHover(behavior, 0.2f);
        Assert.That(behavior.transform.position.y, Is.InRange(start.y - 0.1f, start.y));
        Assert.That(behavior.transform.position.y, Is.GreaterThan(start.y - 0.1f));
    }

    private EnemyBehaviorBase NewHoveringDrone(Type behaviorType, out Transform player, out BoxCollider ground)
    {
        GameObject enemy = NewObject("Hover test drone");
        enemy.SetActive(false);
        EnemyBehaviorBase behavior = (EnemyBehaviorBase)enemy.AddComponent(behaviorType);
        _lifecycle.SetActive(enemy, true);
        enemy.transform.position = new Vector3(10000f, 3.5f, 10000f);
        player = NewObject("Hover player target").transform;
        SetField(behavior, "_player", player, typeof(EnemyBehaviorBase));
        ground = NewObject("Hover platform").AddComponent<BoxCollider>();
        ground.gameObject.layer = LayerMask.NameToLayer("Terrain");
        ground.size = new Vector3(20f, 1f, 20f);
        ground.transform.position = new Vector3(10000f, -0.5f, 10000f);
        SetField(behavior, "_groundMask", (LayerMask)(1 << ground.gameObject.layer));
        Physics.SyncTransforms();
        return behavior;
    }

    private static void InvokeHover(EnemyBehaviorBase behavior, float deltaTime)
    {
        MethodInfo method = behavior.GetType().GetMethod("MaintainHover", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(behavior, new object[] { deltaTime });
    }

    private GameObject NewEnemy(out EnemyEmergence emergence, out Transform pivot)
    {
        GameObject root = NewObject("Emergence integration enemy");
        root.SetActive(false);
        root.AddComponent<EnemyHealth>();
        root.AddComponent<BoxCollider>();
        pivot = new GameObject("EmergenceVisual").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, 0.75f, 0f);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(pivot, false);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        emergence = root.AddComponent<EnemyEmergence>();
        emergence.Configure(pivot, 1f, 1f);
        return root;
    }

    private GameObject NewObject(string name)
    {
        GameObject go = new(name);
        _created.Add(go);
        return go;
    }

    private static object GetField(object target, string fieldName) =>
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    private static void SetField(object target, string fieldName, object value, Type ownerType = null)
    {
        FieldInfo field = (ownerType ?? target.GetType()).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }

    private static void InvokeUpdate(object target)
    {
        MethodInfo method = target.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(target, null);
    }
}
