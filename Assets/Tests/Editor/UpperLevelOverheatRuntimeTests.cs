using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class UpperLevelOverheatRuntimeTests
{
    [UnityTest]
    public IEnumerator NativeUpperDamage_StopsDuringDescent_BeforeBaseUnlocks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        GameObject world = new("Native upper overheat fixture");
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        SimulationMode oldMode = Physics.simulationMode;
        Vector3 oldGravity = Physics.gravity;
        float oldStep = Time.fixedDeltaTime;
        try
        {
            Time.timeScale = 1f;
            GameplayPause.Reset();
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Time.fixedDeltaTime = .02f;
            BoxCollider ground = Box(world.transform, "Base", new Vector3(0f, -30.5f, 0f), new Vector3(100f, 1f, 100f));
            BoxCollider upper = Box(world.transform, "Upper support", new Vector3(0f, 10.5f, 0f), new Vector3(20f, 1f, 20f));
            BoxCollider region = Box(world.transform, "One upper volume", new Vector3(0f, 20f, 0f), new Vector3(40f, 20f, 40f));
            region.isTrigger = true;
            var volume = region.gameObject.AddComponent<UpperLevelVolume>();
            Set(volume, "_baseSurfaceRoots", new[] { ground.transform });

            var player = new GameObject("Native player");
            player.SetActive(false);
            player.transform.SetParent(world.transform, false);
            player.transform.position = new Vector3(0f, 12.02f, 0f);
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 2f; capsule.radius = .5f;
            var body = player.AddComponent<Rigidbody>();
            body.useGravity = true;
            var stats = player.AddComponent<PlayerStats>();
            var definitions = new List<StatDefinition>();
            foreach (string name in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed", "MaxHealth", "DamageResistance" })
            {
                var definition = AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{name}.asset");
                Assert.That(definition, Is.Not.Null, name);
                definitions.Add(definition);
            }
            Set(stats, "_statDefinitions", definitions);
            var movement = player.AddComponent<PlayerMovement>();
            Set(movement, "_cameraTransform", world.transform);
            var health = player.AddComponent<PlayerHealth>();
            var state = player.AddComponent<PlayerPlatformState>();
            Set(state, "_upperLevelVolume", volume);
            var managerObject = new GameObject("Overheat manager");
            managerObject.SetActive(false);
            managerObject.transform.SetParent(world.transform, false);
            var overheat = managerObject.AddComponent<OverheatManager>();
            Set(overheat, "_playerStats", stats);
            var effectObject = new GameObject("Authored test fire");
            effectObject.transform.SetParent(player.transform, false);
            var effect = effectObject.AddComponent<ParticleSystem>();
            var main = effect.main; main.playOnAwake = false; main.loop = true; main.maxParticles = 12;
            var hazard = player.AddComponent<PlayerUpperLevelOverheat>();
            Set(hazard, "_platformState", state); Set(hazard, "_health", health);
            Set(hazard, "_upperLevelVolume", volume);
            Set(hazard, "_overheatManager", overheat); Set(hazard, "_effects", new[] { effect });
            managerObject.SetActive(true);
            player.SetActive(true);
            yield return Wait(() => movement.IsGroundedOnSurface, "settle on upper support");
            Assert.That(state.IsInsideUpper, Is.True);
            Assert.That(state.CanSpawnObjectives, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(100));
            Assert.That(effect.isPlaying, Is.False);

            typeof(OverheatManager).GetMethod("OnMaxHeatReached", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overheat, null);
            yield return new WaitForSeconds(1f);
            Assert.That(health.CurrentHealth, Is.EqualTo(100), "The initial grace must run in the native Update loop.");
            Assert.That(effect.isPlaying, Is.True);
            yield return Wait(() => health.CurrentHealth < 100, "first native hazard hit");
            Assert.That(health.CurrentHealth, Is.EqualTo(99));

            upper.enabled = false; // Begin an ordinary physics fall; no manual Tick or Physics.Simulate.
            yield return Wait(() => !state.IsInsideUpper, "leave the upper volume on descent");
            yield return null; // Let this frame's hazard Update reconcile the boundary.
            Assert.That(state.CanSpawnObjectives, Is.False, "Leaving danger is not a base landing.");
            Assert.That(effect.isPlaying, Is.False);
            Assert.That(effect.particleCount, Is.Zero);
            float safeSince = Time.time;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!state.CanSpawnObjectives && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(health.CurrentHealth, Is.EqualTo(99), "Airborne descent outside the volume must stay safe.");
                yield return null;
            }
            Assert.That(state.CanSpawnObjectives, Is.True, "The real motor must eventually identify authored Base support.");
            Assert.That(Time.time - safeSince, Is.GreaterThan(2f), "The descent crossed a full damage interval before landing.");
            Assert.That(movement.TryGetGroundSupport(out Collider support), Is.True);
            Assert.That(support, Is.SameAs(ground));
            Assert.That(health.CurrentHealth, Is.EqualTo(99));
            overheat.NotifyOverheatObjectiveCleared();
            yield return null;
            Assert.That(overheat.IsOverheating, Is.False);
            Assert.That(effect.isPlaying, Is.False);
        }
        finally
        {
            Object.Destroy(world);
            InputSystem.RemoveDevice(keyboard);
            Physics.simulationMode = oldMode;
            Physics.gravity = oldGravity;
            Time.fixedDeltaTime = oldStep;
            Time.timeScale = 1f;
            GameplayPause.Reset();
        }
        yield return null;
        yield return new ExitPlayMode();
    }

    private static BoxCollider Box(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position;
        var box = go.AddComponent<BoxCollider>(); box.size = size; return box;
    }

    private static IEnumerator Wait(Func<bool> ready, string purpose)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(ready(), Is.True, "Native runtime timeout: " + purpose);
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.timeScale = 1f; yield return new ExitPlayMode(); }
    }
}
