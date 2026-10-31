using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Checks the optional hardware prefabs through their actual imported geometry and traversal components.</summary>
public sealed class GeyserHardwareVariantTests
{
    private const string Folder = "Assets/Art/Environment/Geysers/Prefabs/";

    [SetUp]
    public void SetUp()
    {
        // EnterPlayMode resumes the EditMode fixture after a domain reload and invokes SetUp again.
        if (!Application.isPlaying)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [TestCase("Fan", .530625f)]
    [TestCase("Vent", .45075f)]
    public void ApprovedHardware_HasReducedSizeContinuousWalkableSupportAndBoundFlow(string variant, float height)
    {
        GameObject root = Instantiate(variant);
        Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
        MapLaunchPad launcher = root.GetComponent<MapLaunchPad>();
        Assert.That(launcher, Is.Not.Null);
        var settings = new SerializedObject(launcher);
        Assert.That(settings.FindProperty("_landing").objectReferenceValue, Is.Null, "Designer assigns a receiving platform.");
        Assert.That(settings.FindProperty("_apexClearance").floatValue, Is.EqualTo(12f), "Model scaling must not scale the route.");
        Assert.That(settings.FindProperty("_launchFromTrigger").boolValue, Is.True);
        Assert.That(root.GetComponent<Collider>().isTrigger, Is.True);
        Assert.That(launcher.TryGetLaunchVelocity(Vector3.up, 1f, out _, out _), Is.False);

        var renderers = root.transform.Find("Hardware").GetComponentsInChildren<MeshRenderer>();
        Assert.That(renderers, Is.Not.Empty);
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            Assert.That(renderer.sharedMaterials.All(material => material != null), Is.True, renderer.name);
        }
        Assert.That(bounds.size.x, Is.EqualTo(3.6f).Within(.02f));
        Assert.That(bounds.size.z, Is.EqualTo(3.6f).Within(.02f));
        Assert.That(bounds.size.y, Is.EqualTo(height).Within(.015f));
        Assert.That(bounds.min.y, Is.EqualTo(0f).Within(.015f), "Floor-centre model pivot.");

        MeshCollider surface = root.transform.Find("WalkableSurface").GetComponent<MeshCollider>();
        Assert.That(surface, Is.Not.Null);
        Assert.That(surface.isTrigger, Is.False);
        Assert.That(surface.sharedMesh, Is.Not.Null);
        Assert.That(root.GetComponentsInChildren<Collider>().Count(collider => !collider.isTrigger), Is.EqualTo(1));
        Physics.SyncTransforms();
        // Sample both across the grille gaps and over the fan guard. The simplified support must not snag or drop feet.
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
        {
            Vector3 origin = new Vector3(x * .5f, 2f, z * .5f);
            Assert.That(surface.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 3f), Is.True, $"{variant}: support {x},{z}");
            Assert.That(Vector3.Angle(hit.normal, Vector3.up), Is.LessThan(50f), $"{variant}: traversable slope {x},{z}");
        }
        GeyserVfx vfx = root.GetComponentInChildren<GeyserVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(new SerializedObject(vfx).FindProperty("_launcher").objectReferenceValue, Is.EqualTo(launcher));
        Assert.That(vfx.GeyserKind, Is.EqualTo(GeyserVfx.Kind.HotAir));
        Assert.That(vfx.MaximumParticleBudget, Is.LessThanOrEqualTo(88));
        foreach (ParticleSystem particles in vfx.GetComponentsInChildren<ParticleSystem>())
            Assert.That(particles.transform.position.y, Is.GreaterThan(surface.bounds.max.y), particles.name + " starts above the walkable surface");
        Assert.That(root.GetComponentsInChildren<Rigidbody>(), Is.Empty);
    }

    [TestCase("Fan")]
    [TestCase("Vent")]
    public void RotatedHardware_TracksChangedDestinationWithRealEmittedVelocities(string variant)
    {
        GameObject root = Instantiate(variant);
        root.transform.SetPositionAndRotation(new Vector3(7f, 2f, -3f), Quaternion.Euler(0f, 73f, 0f));
        GameObject landing = Landing(new Vector3(31f, 8f, -16f));
        MapLaunchPad launcher = root.GetComponent<MapLaunchPad>();
        Set(launcher, "_landing", landing.transform);
        GeyserVfx vfx = root.GetComponentInChildren<GeyserVfx>();
        var systems = vfx.GetComponentsInChildren<ParticleSystem>();
        Vector3[] anchors = systems.Select(system => system.transform.position).ToArray();
        Vector3 previousDirection = Vector3.zero;
        foreach (Vector3 target in new[] { new Vector3(31f, 8f, -16f), new Vector3(-24f, 10f, -31f) })
        {
            landing.transform.position = target;
            Physics.SyncTransforms();
            vfx.ApplyTuning();
            var updraft = systems.Single(system => system.name == "ContinuousUpdraft");
            Vector3 start = updraft.transform.position + Vector3.up;
            Assert.That(launcher.TryGetLaunchVelocity(start, 1f, out Vector3 velocity, out float time), Is.True);
            Vector3 expected = landing.GetComponent<Collider>().bounds.center;
            expected.y = landing.GetComponent<Collider>().bounds.max.y + 1.05f;
            Assert.That(Vector3.Distance(start + velocity * time + .5f * Physics.gravity * time * time, expected), Is.LessThan(.002f));
            if (previousDirection != Vector3.zero)
                Assert.That(Vector3.Angle(previousDirection, velocity), Is.GreaterThan(20f));
            previousDirection = velocity;
            for (int i = 0; i < systems.Length; i++)
            {
                Assert.That(Vector3.Distance(systems[i].transform.position, anchors[i]), Is.LessThan(.0001f));
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].Emit(16);
                var emitted = new ParticleSystem.Particle[16];
                Assert.That(systems[i].GetParticles(emitted), Is.EqualTo(16));
                Vector3 mean = Vector3.zero;
                foreach (var particle in emitted) mean += particle.velocity;
                Assert.That(Vector3.Angle(mean, velocity), Is.LessThan(5.1f), variant + "/" + systems[i].name);
                Assert.That(systems[i].main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            }
        }
    }

    [UnityTest]
    public IEnumerator BothHardwarePrefabs_TriggerLaunchLandWithFeedbackAndRearm()
    {
        yield return new EnterPlayMode();
        yield return ExerciseHardware();
        yield return new ExitPlayMode();
    }

    private static IEnumerator ExerciseHardware()
    {
        float oldCapture = Time.captureDeltaTime;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        GameObject root = null, landing = null, actor = null, camera = null, floor = null;
        try
        {
            Time.timeScale = 1f; Time.captureDeltaTime = .02f; GameplayPause.Reset();
            foreach (string variant in new[] { "Fan", "Vent" })
            {
                root = Instantiate(variant);
                root.transform.rotation = Quaternion.Euler(0f, variant == "Fan" ? 73f : 151f, 0f);
                landing = Landing(new Vector3(31f, 7f, -16f));
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(100f, 1f, 100f);
                MapLaunchPad launcher = root.GetComponent<MapLaunchPad>();
                Set(launcher, "_landing", landing.transform);
                actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"));
                actor.transform.localScale = Vector3.one * 1.2f;
                foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                PlayerMovement movement = actor.GetComponent<PlayerMovement>();
                Rigidbody body = actor.GetComponent<Rigidbody>();
                camera = new GameObject("Disposable hardware traversal camera", typeof(Camera));
                Set(movement, "_cameraTransform", camera.transform); movement.enabled = true;
                Physics.SyncTransforms();
                float halfHeight = actor.GetComponent<Collider>().bounds.extents.y;
                Vector3 entry = Vector3.up * (root.transform.Find("WalkableSurface").GetComponent<Collider>().bounds.max.y + halfHeight + .04f);
                body.position = entry; actor.transform.position = entry; body.linearVelocity = Vector3.zero;
                var burst = root.GetComponentsInChildren<ParticleSystem>().Single(system => system.name == "EntryBurst");
                int launches = 0, landingEvents = 0, burstAtLaunch = 0;
                Vector3 start = default, velocity = default; float flightTime = 0f;
                launcher.OnLaunched += player =>
                {
                    launches++; start = player.transform.position; velocity = player.CurrentVelocity; burstAtLaunch = burst.particleCount;
                    Assert.That(launcher.TryGetLaunchVelocity(start, halfHeight, out _, out flightTime), Is.True);
                };
                movement.OnLanded += () =>
                {
                    if (launches > 0 && Vector3.Distance(Vector3.ProjectOnPlane(body.position - landing.transform.position, Vector3.up), Vector3.zero) < 2f)
                        landingEvents++;
                };
                Physics.SyncTransforms();
                yield return Until(() => launches > 0, 3f);
                Assert.That(launches, Is.EqualTo(1));
                Assert.That(movement.IsLaunching, Is.True);
                Assert.That(burstAtLaunch, Is.GreaterThan(0), "Real trigger entry drives VFX feedback.");
                Assert.That(Vector3.Angle(burst.transform.forward, velocity), Is.LessThan(1f));
                Bounds bounds = landing.GetComponent<Collider>().bounds;
                Vector3 endpoint = new Vector3(bounds.center.x, bounds.max.y + halfHeight + .05f, bounds.center.z);
                Assert.That(Vector3.Distance(start + velocity * flightTime + .5f * Physics.gravity * flightTime * flightTime, endpoint), Is.LessThan(.015f));
                yield return Until(() => !movement.IsLaunching, 12f);
                Assert.That(landingEvents, Is.GreaterThan(0), "Production OnLanded feedback event fires at the receiving platform.");
                Assert.That(movement.PlanarSpeed, Is.LessThan(.02f), "Existing landing dead stop remains intact.");
                Assert.That(Vector3.Distance(Vector3.ProjectOnPlane(body.position - endpoint, Vector3.up), Vector3.zero), Is.LessThan(1.1f));
                Assert.That(Mathf.Abs(body.position.y - endpoint.y), Is.LessThan(.2f));
                TestContext.WriteLine($"{variant}: launch={velocity}, flight={flightTime:F3}s, landing={body.position}, landing events={landingEvents}, entry particles={burstAtLaunch}");
                body.position = entry; actor.transform.position = entry; body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
                yield return Until(() => launches == 2, 3f);
                Object.Destroy(actor); Object.Destroy(root); Object.Destroy(landing); Object.Destroy(camera); Object.Destroy(floor);
                actor = root = landing = camera = floor = null; yield return null;
            }
        }
        finally
        {
            foreach (GameObject go in new[] { actor, root, landing, camera, floor }) if (go != null) Object.Destroy(go);
            InputSystem.RemoveDevice(keyboard); Time.captureDeltaTime = oldCapture; Time.timeScale = 1f; GameplayPause.Reset();
        }
    }

    private static GameObject Instantiate(string variant)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + variant + "JumpPad.prefab");
        Assert.That(prefab, Is.Not.Null, variant + " optional prefab exists");
        return Object.Instantiate(prefab);
    }

    private static GameObject Landing(Vector3 position)
    {
        GameObject landing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        landing.name = "Disposable receiving platform"; landing.transform.position = position;
        landing.transform.localScale = new Vector3(10f, 1f, 10f);
        return landing;
    }

    private static IEnumerator Until(Func<bool> predicate, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, "Native hardware traversal timed out.");
    }

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.captureDeltaTime = 0f; Time.timeScale = 1f; yield return new ExitPlayMode(); }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
