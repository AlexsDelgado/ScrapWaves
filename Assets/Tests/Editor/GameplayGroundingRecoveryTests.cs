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

/// <summary>Saved-scene collision and objective eligibility, using the production player body.</summary>
public sealed class GameplayGroundingRecoveryTests
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [TestCase(.2f, .2f)]
    [TestCase(.5f, .5f)]
    [TestCase(.8f, .8f)]
    public void ReplacementRamp_PhysicallySupportsPlayerAndUnlocksObjectives(float x, float z)
    {
        WithSavedScene(probe =>
        {
            Collider ramp = MapCollider(probe.Scene, "Cube.003");
            Assert.That(ramp, Is.TypeOf<MeshCollider>());
            RaycastHit surface = Surface(ramp, x, z);
            Assert.That(probe.Volume.Contains(surface.point + Vector3.up * .01f), Is.False);
            Assert.That(probe.Volume.IsBaseSupport(ramp), Is.True, "Replacement ramp must be an explicit Base support.");
            DropAndCheckSupport(probe, ramp, surface);
            Assert.That(probe.Platform.IsInsideUpper, Is.False);
            Assert.That(probe.Platform.IsBaseSupported, Is.True);
            Assert.That(probe.Platform.CanSpawnObjectives, Is.True);
        });
    }

    [Test]
    public void ActualUpperPlatform_SupportsPlayerWithoutUnlockingObjectives()
    {
        WithSavedScene(probe =>
        {
            // Existing lowest plateau: saved top is approximately y=155.122, above the upper boundary.
            Collider upper = MapCollider(probe.Scene, "Cube.004");
            RaycastHit surface = Surface(upper, .5f, .5f);
            Assert.That(probe.Volume.Contains(surface.point + Vector3.up * .01f), Is.True);
            Assert.That(probe.Volume.IsBaseSupport(upper), Is.False);
            DropAndCheckSupport(probe, upper, surface);
            Assert.That(probe.Platform.IsInsideUpper, Is.True);
            Assert.That(probe.Platform.IsBaseSupported, Is.False);
            Assert.That(probe.Platform.CanSpawnObjectives, Is.False);
        });
    }

    [Test]
    public void BaseSupportRoots_ContainRampWithoutNullsOrElevatedGeometry()
    {
        WithSavedScene(probe =>
        {
            Transform map = probe.Scene.GetRootGameObjects().Single(go => go.name == "Map grayboxv3").transform;
            Collider ramp = MapCollider(probe.Scene, "Cube.003");
            SerializedProperty roots = new SerializedObject(probe.Volume).FindProperty("_baseSurfaceRoots");
            Assert.That(roots, Is.Not.Null);
            bool containsRamp = false;
            float upperBottom = probe.Volume.GetComponent<BoxCollider>().bounds.min.y;
            for (int i = 0; i < roots.arraySize; i++)
            {
                Transform root = roots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                Assert.That(root, Is.Not.Null, "Base root " + i);
                Assert.That(root, Is.Not.SameAs(map), "The shared map root includes upper platforms.");
                containsRamp |= root == ramp.transform;
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                Assert.That(colliders, Is.Not.Empty, root.name);
                foreach (Collider collider in colliders)
                {
                    Assert.That(collider.enabled && collider.gameObject.activeInHierarchy, Is.True, root.name);
                    Assert.That(collider.bounds.max.y, Is.LessThan(upperBottom), root.name + " includes elevated collision.");
                }
            }
            Assert.That(containsRamp, Is.True, "Register the replacement ramp's own transform.");
        });
    }

    private static void DropAndCheckSupport(Probe probe, Collider expected, RaycastHit surface)
    {
        Rigidbody body = probe.Movement.GetComponent<Rigidbody>();
        Assert.That(probe.Movement.GetComponent<CapsuleCollider>(), Is.Not.Null, "Use the saved production capsule.");
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.None;
        Physics.SyncTransforms();
        probe.Movement.transform.position += surface.point + Vector3.up * .3f - probe.Movement.FeetPosition;
        body.position = probe.Movement.transform.position;
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.WakeUp();
        Physics.SyncTransforms();
        float initialFeet = probe.Movement.FeetPosition.y;
        float lateFeet = initialFeet;
        for (int step = 0; step < 30; step++)
        {
            Physics.Simulate(.02f);
            if (step == 24) lateFeet = probe.Movement.FeetPosition.y;
        }
        Physics.SyncTransforms();
        Vector3 feet = probe.Movement.FeetPosition;
        Assert.That(initialFeet - feet.y, Is.GreaterThan(.15f), "Body must actually fall, not be pinned at the probe pose.");
        Assert.That(Mathf.Abs(feet.y - lateFeet), Is.LessThan(.04f), "Body must settle during the final physics steps.");
        Assert.That(Mathf.Abs(body.linearVelocity.y), Is.LessThan(.5f), "Body must not still be falling through the floor.");
        Ray ray = new(new Vector3(feet.x, expected.bounds.max.y + 2f, feet.z), Vector3.down);
        Assert.That(expected.Raycast(ray, out RaycastHit beneath, expected.bounds.size.y + 4f), Is.True);
        Assert.That(feet.y - beneath.point.y, Is.InRange(-.04f, .12f), "Actual capsule feet must remain at the expected surface.");
        Assert.That(probe.Movement.TryGetGroundSupport(out Collider support), Is.True);
        Assert.That(support, Is.SameAs(expected), "A different overlapping collider must not satisfy the test.");
        Assert.That(probe.Platform.IsSimulationActive, Is.True, "Fixture must not hide a gate failure behind pause/startup state.");
    }

    private static RaycastHit Surface(Collider collider, float x, float z)
    {
        Bounds bounds = collider.bounds;
        Ray ray = new(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x), bounds.max.y + 2f,
            Mathf.Lerp(bounds.min.z, bounds.max.z, z)), Vector3.down);
        Assert.That(collider.Raycast(ray, out RaycastHit hit, bounds.size.y + 4f), Is.True, collider.name);
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThan(Mathf.Cos(50f * Mathf.Deg2Rad)));
        return hit;
    }

    private static void WithSavedScene(Action<Probe> check)
    {
        Assert.That(Application.isPlaying, Is.False, "This is an EditMode physics fixture.");
        Assert.That(SceneManager.GetSceneByPath(ScenePath).isLoaded, Is.False,
            "Run with GameplayScene closed; never reuse or discard a user's open scene.");
        Scene previousActive = SceneManager.GetActiveScene();
        SimulationMode previousMode = Physics.simulationMode;
        Vector3 previousGravity = Physics.gravity;
        float previousTimeScale = Time.timeScale;
        int previousPauseLocks = GameplayPause.LockCount;
        FieldInfo startup = typeof(EntranceCinematic).GetField("<StartupHeld>k__BackingField", PrivateStatic);
        FieldInfo movementInstance = typeof(PlayerMovement).GetField("s_Instance", PrivateStatic);
        FieldInfo gameInstance = typeof(GameManager).GetField("<Instance>k__BackingField", PrivateStatic);
        object previousStartup = startup.GetValue(null);
        object previousMovement = movementInstance.GetValue(null);
        object previousGame = gameInstance.GetValue(null);
        var frozen = new List<BodySnapshot>();
        Scene scene = default;
        PlayerMovement movement = null;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Assert.That(Physics.gravity.y, Is.LessThan(0f), "Exercise the project's authored gravity.");
            Time.timeScale = 1f;
            GameplayPause.Reset();
            startup.SetValue(null, false);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            movement = Components<PlayerMovement>(scene).Single(item => item.gameObject.activeInHierarchy);
            PlayerPlatformState platform = movement.GetComponent<PlayerPlatformState>();
            Assert.That(platform, Is.Not.Null);
            InvokeAwake(movement);
            InvokeAwake(movement.GetComponent<PlayerHealth>());
            InvokeAwake(platform);
            GameManager manager = Components<GameManager>(scene).Single();
            Assert.That(manager.IsPlaying, Is.True);
            gameInstance.SetValue(null, manager);
            Rigidbody playerBody = movement.GetComponent<Rigidbody>();
            foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (body == playerBody) continue;
                frozen.Add(new BodySnapshot(body));
                body.isKinematic = true;
            }
            Physics.SyncTransforms();
            check(new Probe { Scene = scene, Movement = movement, Platform = platform,
                Volume = Components<UpperLevelVolume>(scene).Single() });
        }
        finally
        {
            foreach (BodySnapshot body in frozen) body.Restore();
            // Awake was invoked explicitly, so also release its transient physics material explicitly.
            if (movement != null) typeof(PlayerMovement).GetMethod("OnDestroy", PrivateInstance).Invoke(movement, null);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            Physics.simulationMode = previousMode;
            Physics.gravity = previousGravity;
            Time.timeScale = previousTimeScale;
            GameplayPause.Reset();
            for (int i = 0; i < previousPauseLocks; i++) GameplayPause.Push();
            startup.SetValue(null, previousStartup);
            movementInstance.SetValue(null, previousMovement);
            gameInstance.SetValue(null, previousGame);
            Physics.SyncTransforms();
        }
    }

    private static Collider MapCollider(Scene scene, string name) => Components<Collider>(scene)
        .Single(collider => collider.name == name && collider.transform.parent != null && collider.transform.parent.name == "Map grayboxv3");
    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void InvokeAwake(Component component)
    {
        Assert.That(component, Is.Not.Null);
        component.GetType().GetMethod("Awake", PrivateInstance).Invoke(component, null);
    }

    private sealed class Probe
    {
        public Scene Scene;
        public PlayerMovement Movement;
        public PlayerPlatformState Platform;
        public UpperLevelVolume Volume;
    }

    private sealed class BodySnapshot
    {
        private readonly Rigidbody _body;
        private readonly bool _kinematic, _sleeping;
        private readonly Vector3 _position, _linear, _angular;
        private readonly Quaternion _rotation;
        public BodySnapshot(Rigidbody body)
        {
            _body = body; _kinematic = body.isKinematic; _sleeping = body.IsSleeping();
            _position = body.position; _rotation = body.rotation;
            _linear = body.linearVelocity; _angular = body.angularVelocity;
        }
        public void Restore()
        {
            if (_body == null) return;
            _body.position = _position; _body.rotation = _rotation;
            _body.isKinematic = _kinematic;
            if (!_kinematic)
            {
                _body.linearVelocity = _linear; _body.angularVelocity = _angular;
                if (_sleeping) _body.Sleep(); else _body.WakeUp();
            }
        }
    }
}
