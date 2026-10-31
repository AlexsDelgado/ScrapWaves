using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GeyserDirectionTests
{
    [SetUp]
    public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TearDown]
    public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [Test]
    public void EveryAuthoredRoute_EmitsTowardItsSolvedTakeoffWithoutMovingItsMouth()
    {
        var scene = EditorSceneManager.OpenScene(GeyserModelAuthoring.ScenePath, OpenSceneMode.Additive);
        try
        {
            var launchers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MapLaunchPad>(true)).ToArray();
            Assert.That(launchers.Length, Is.GreaterThanOrEqualTo(9), "Include the tutorial and duplicated designer routes.");
            Physics.SyncTransforms();
            foreach (var launcher in launchers)
            {
                var vfx = launcher.GetComponentInChildren<GeyserVfx>(true);
                Assert.That(vfx, Is.Not.Null, launcher.name);
                Assert.That(new SerializedObject(vfx).FindProperty("_launcher").objectReferenceValue,
                    Is.EqualTo(launcher), launcher.name + " retains its own launcher binding");
                var systems = vfx.GetComponentsInChildren<ParticleSystem>(true);
                var anchors = systems.Select(p => p.transform.position).ToArray();
                launcher.gameObject.SetActive(true);
                vfx.gameObject.SetActive(true);
                vfx.ApplyTuning();
                var updraft = systems.Single(p => p.name == "ContinuousUpdraft");
                Assert.That(launcher.TryGetLaunchVelocity(updraft.transform.position + Vector3.up, 1f,
                    out Vector3 expected, out _), Is.True, launcher.name);
                for (int i = 0; i < systems.Length; i++)
                {
                    Assert.That(Vector3.Distance(systems[i].transform.position, anchors[i]), Is.LessThan(.0001f),
                        launcher.name + "/" + systems[i].name + " mouth");
                    Clear(systems[i]);
                    systems[i].Emit(16);
                    AssertFlow(systems[i], expected, launcher.name);
                }
                TestContext.WriteLine(launcher.name + ": takeoff " + expected.normalized);
            }
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [TestCase("TrashGeyser")]
    [TestCase("HotAirGeyser")]
    public void MovingDestination_RedirectsNewParticlesWithoutTurningParticlesAlreadyInFlight(string variant)
    {
        var launcher = CreateLauncher(out GameObject landing);
        var vfx = CreateVisual(variant, launcher);
        var systems = vfx.GetComponentsInChildren<ParticleSystem>();
        var original = new ParticleSystem.Particle[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            Clear(systems[i]);
            systems[i].Emit(1);
            original[i] = Read(systems[i]).Single();
        }

        landing.transform.position = new Vector3(-60f, 5f, -40f);
        Physics.SyncTransforms();
        Call(vfx, "Update");
        var updraft = systems.Single(p => p.name == "ContinuousUpdraft");
        Assert.That(launcher.TryGetLaunchVelocity(updraft.transform.position + Vector3.up, 1f,
            out Vector3 expected, out _), Is.True);
        Assert.That(Vector3.Angle(original[0].velocity, expected), Is.GreaterThan(20f), "Exercise a material direction change.");
        for (int i = 0; i < systems.Length; i++)
        {
            var retained = Read(systems[i]).Single();
            Assert.That(Vector3.Distance(retained.position, original[i].position), Is.LessThan(.0001f), systems[i].name);
            Assert.That(Vector3.Distance(retained.velocity, original[i].velocity), Is.LessThan(.0001f), systems[i].name);
            systems[i].Emit(16);
            AssertFlow(systems[i], expected, variant, original[i].randomSeed);
        }
    }

    [TestCase("TrashGeyser")]
    [TestCase("HotAirGeyser")]
    public void RealLaunchEvent_UsesActualPlayerVelocityForTheEntryBurst(string variant)
    {
        var launcher = CreateLauncher(out _);
        var vfx = CreateVisual(variant, launcher);
        var actor = new GameObject("Off-centre launch sample", typeof(CapsuleCollider), typeof(Rigidbody));
        actor.transform.position = new Vector3(0f, 2f, 35f);
        var movement = actor.AddComponent<PlayerMovement>();
        Set(movement, "_rb", actor.GetComponent<Rigidbody>());
        Physics.SyncTransforms();
        var burst = vfx.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "EntryBurst");
        Vector3 ambient = burst.transform.forward;
        foreach (var particles in vfx.GetComponentsInChildren<ParticleSystem>()) Clear(particles);

        Call(launcher, "TryLaunch", actor.GetComponent<Collider>(), false);

        Assert.That(movement.IsLaunching, Is.True, "Exercise the real launcher event rather than calling the VFX handler.");
        Assert.That(Vector3.Angle(ambient, movement.CurrentVelocity), Is.GreaterThan(10f), "Distinguish actual entry from ambient preview.");
        Assert.That(burst.particleCount, Is.EqualTo(28));
        AssertFlow(burst, movement.CurrentVelocity, variant);
        Assert.That(Vector3.Angle(burst.transform.forward, movement.CurrentVelocity), Is.LessThan(.05f));
        // Refreshing ambient direction on the next frame must not redirect this one-shot.
        var before = Read(burst);
        Call(vfx, "Update");
        var after = Read(burst);
        for (int i = 0; i < before.Length; i++)
            Assert.That(Vector3.Distance(before[i].velocity, after[i].velocity), Is.LessThan(.0001f));
    }

    [TestCase("TrashGeyser")]
    [TestCase("HotAirGeyser")]
    public void StandalonePrefab_UsesVerticalFlowAndRetainsItsParticleCap(string variant)
    {
        var vfx = CreateVisual(variant, null);
        vfx.transform.SetPositionAndRotation(new Vector3(11f, 4f, -8f), Quaternion.Euler(0f, 73f, 0f));
        vfx.ApplyTuning();
        foreach (var particles in vfx.GetComponentsInChildren<ParticleSystem>())
        {
            Clear(particles);
            particles.Emit(16);
            AssertFlow(particles, Vector3.up, variant);
        }
        Assert.That(vfx.MaximumParticleBudget, Is.EqualTo(variant == "TrashGeyser" ? 108 : 88));
        Assert.That(vfx.ActiveParticleCount, Is.LessThanOrEqualTo(vfx.MaximumParticleBudget));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LaunchQuery_ReachesColliderBoundsAndHandlesMissingDestinationWithoutChangingBindings(bool findByName)
    {
        var launcher = CreateLauncher(out GameObject landing);
        if (findByName) Set(launcher, "_landing", null);
        landing.transform.SetPositionAndRotation(new Vector3(31f, 16f, -9f), Quaternion.Euler(0f, 37f, 0f));
        landing.transform.localScale = new Vector3(9f, 3f, 5f);
        landing.GetComponent<BoxCollider>().center = new Vector3(.3f, .2f, -.4f);
        Physics.SyncTransforms();
        Vector3 start = new Vector3(2f, 3f, -4f);
        const float halfHeight = 1.3f;
        Assert.That(launcher.TryGetLaunchVelocity(start, halfHeight, out Vector3 velocity, out float time), Is.True);
        Bounds bounds = landing.GetComponent<Collider>().bounds;
        Vector3 target = new Vector3(bounds.center.x, bounds.max.y + halfHeight + .05f, bounds.center.z);
        Vector3 endpoint = start + velocity * time + .5f * Physics.gravity * time * time;
        Assert.That(Vector3.Distance(endpoint, target), Is.LessThan(.002f));
        if (findByName)
            Assert.That(new SerializedObject(launcher).FindProperty("_landing").objectReferenceValue, Is.Null,
                "Preview lookup must not serialize or rebind a destination.");
        Object.DestroyImmediate(landing);
        Assert.That(launcher.TryGetLaunchVelocity(start, halfHeight, out velocity, out time), Is.False);
        Assert.That(velocity, Is.EqualTo(Vector3.zero));
        Assert.That(time, Is.Zero);
    }

    private static MapLaunchPad CreateLauncher(out GameObject landing)
    {
        var source = new GameObject("Jump Direction Test", typeof(BoxCollider));
        source.transform.SetPositionAndRotation(new Vector3(3f, 0f, 2f), Quaternion.Euler(0f, 61f, 0f));
        source.transform.localScale = new Vector3(10f, 2f, 10f);
        landing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        landing.name = "Land Direction Test";
        landing.transform.position = new Vector3(60f, 8f, -15f);
        var launcher = source.AddComponent<MapLaunchPad>();
        Set(launcher, "_landing", landing.transform);
        Call(launcher, "Awake");
        return launcher;
    }

    private static GeyserVfx CreateVisual(string variant, MapLaunchPad launcher)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GeyserModelAuthoring.Folder + "/Prefabs/" + variant + "_Visual.prefab");
        Assert.That(prefab, Is.Not.Null);
        var instance = Object.Instantiate(prefab);
        if (launcher != null)
        {
            instance.transform.SetParent(launcher.transform, false);
            instance.transform.localScale = new Vector3(.1f, .5f, .1f);
        }
        var vfx = instance.GetComponent<GeyserVfx>();
        vfx.BindLauncher(launcher);
        vfx.ApplyTuning();
        return vfx;
    }

    private static void Clear(ParticleSystem system) => system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

    private static ParticleSystem.Particle[] Read(ParticleSystem system)
    {
        var particles = new ParticleSystem.Particle[system.main.maxParticles];
        int count = system.GetParticles(particles);
        return particles.Take(count).ToArray();
    }

    private static void AssertFlow(ParticleSystem system, Vector3 expected, string context, uint? excludedSeed = null)
    {
        Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), context + "/" + system.name);
        var emitted = Read(system).Where(p => !excludedSeed.HasValue || p.randomSeed != excludedSeed.Value).ToArray();
        Assert.That(emitted, Is.Not.Empty, context + "/" + system.name);
        Vector3 mean = Vector3.zero;
        foreach (var particle in emitted) mean += particle.velocity;
        Assert.That(mean.sqrMagnitude, Is.GreaterThan(.001f));
        Assert.That(Vector3.Angle(mean, expected), Is.LessThanOrEqualTo(5.1f),
            context + "/" + system.name + " world velocity; mean=" + mean.normalized.ToString("F4") +
            ", expected=" + expected.normalized.ToString("F4") +
            ", transformForward=" + system.transform.forward.ToString("F4") +
            ", lossyScale=" + system.transform.lossyScale.ToString("F4") +
            ", scalingMode=" + system.main.scalingMode);
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
