using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class TrashGeyserDebrisTests
{
    private GameObject _instance;
    private ParticleSystem _debris;
    private GeyserVfx _presentation;

    [SetUp]
    public void Setup()
    {
        _instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GeyserModelAuthoring.Folder + "/Prefabs/TrashGeyser_Visual.prefab"));
        _presentation = _instance.GetComponent<GeyserVfx>();
        _presentation.ApplyTuning();
        _debris = _instance.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "LightTumblingDebris");
        foreach (var particles in _instance.GetComponentsInChildren<ParticleSystem>())
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    [TearDown] public void Cleanup() { if (_instance != null) Object.DestroyImmediate(_instance); }

    [Test]
    public void NativeMeshParticles_AreReadableAndActuallyTumbleOnEveryAxis()
    {
        var renderer = _debris.GetComponent<ParticleSystemRenderer>();
        Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
        Assert.That(renderer.mesh, Is.SameAs(AssetDatabase.LoadAssetAtPath<Mesh>(GeyserModelAuthoring.Folder + "/Meshes/LightScrap.asset")));
        Assert.That(renderer.mesh.vertexCount, Is.EqualTo(4));
        Assert.That(renderer.mesh.triangles.Length, Is.EqualTo(12));
        _debris.Emit(1);
        var particles = new ParticleSystem.Particle[20];
        Assert.That(_debris.GetParticles(particles), Is.EqualTo(1));
        uint seed = particles[0].randomSeed;
        Vector3 before = particles[0].rotation3D;
        Assert.That(particles[0].GetCurrentSize(_debris), Is.InRange(.27f, .45f));
        Vector3 origin = particles[0].position;
        _debris.Simulate(.2f, false, false, false);
        int count = _debris.GetParticles(particles);
        var same = particles.Take(count).Single(p => p.randomSeed == seed);
        for (int axis = 0; axis < 3; axis++)
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(before[axis], same.rotation3D[axis])), Is.InRange(5f, 55f), "Native tumble axis " + axis);
        Assert.That((same.position - origin).magnitude, Is.GreaterThan(.3f), "The tumbling scrap still travels with the airflow.");
        TestContext.WriteLine($"Native scrap size={same.GetCurrentSize(_debris):F3}m; .2s rotation {before} -> {same.rotation3D}; travel={(same.position - origin).magnitude:F3}m");
        Assert.That(_instance.GetComponentsInChildren<Rigidbody>(), Is.Empty);
    }

    [Test]
    public void ContinuousFlowAndRealEntry_EmitDebrisWithoutExceedingTheExistingCap()
    {
        _debris.Simulate(2.5f, false, true, false);
        int ambient = _debris.particleCount;
        Assert.That(ambient, Is.InRange(8, 12));
        _presentation.PlayEntryBurst();
        Assert.That(_debris.particleCount, Is.EqualTo(Mathf.Min(ambient + 6, 20)));
        var burst = _instance.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "EntryBurst");
        Assert.That(burst.particleCount, Is.EqualTo(28));
        for (int frame = 0; frame < 120; frame++)
        {
            if (frame % 10 == 0) _presentation.PlayEntryBurst();
            foreach (var system in _instance.GetComponentsInChildren<ParticleSystem>()) system.Simulate(1f / 30f, false, false, false);
            Assert.That(_debris.particleCount, Is.LessThanOrEqualTo(20));
            Assert.That(_presentation.ActiveParticleCount, Is.LessThanOrEqualTo(108));
        }
        Assert.That(_debris.main.maxParticles, Is.EqualTo(20));
        Assert.That(_debris.emission.rateOverTime.constant, Is.EqualTo(4f));
        Assert.That(_presentation.MaximumParticleBudget, Is.EqualTo(108));
    }
}

public sealed class TrashGeyserSavedPrefabTests
{
    [Test]
    public void SavedPrefab_ContainsDebrisTuningBeforeRuntimeApplyTuning()
    {
        string path = GeyserModelAuthoring.Folder + "/Prefabs/TrashGeyser_Visual.prefab";
        // Read disk before loading an asset: OnValidate or ApplyTuning must not
        // disguise missing fields or stale particle modules in the saved prefab.
        string saved = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(EditorUtility.IsPersistent(prefab), Is.True);
        var presentation = prefab.GetComponent<GeyserVfx>();
        string root = SavedComponent(saved, presentation);
        Assert.That(SavedNumber(root, "_debrisSize", 2), Is.EqualTo(.36f).Within(.0001f));
        Assert.That(SavedNumber(root, "_debrisTumbleRate", 2), Is.EqualTo(1f));
        Assert.That(SavedNumber(root, "_debrisPerSecond", 2), Is.EqualTo(4f));
        Assert.That(SavedNumber(root, "_debrisBudget", 2), Is.EqualTo(20f));

        var debris = prefab.GetComponentsInChildren<ParticleSystem>(true)
            .Single(p => p.name == "LightTumblingDebris");
        string particles = SavedComponent(saved, debris);
        string initial = SavedSection(particles, "InitialModule", 2);
        AssertSavedRange(initial, "startSize", .27f, .45f);
        Assert.That(SavedNumber(initial, "maxNumParticles", 4), Is.EqualTo(20f));
        string rotation = SavedSection(particles, "RotationModule", 2);
        Assert.That(SavedNumber(rotation, "enabled", 4), Is.EqualTo(1f));
        Assert.That(SavedNumber(rotation, "separateAxes", 4), Is.EqualTo(1f));
        AssertSavedRange(rotation, "x", 1.8f, 3.2f);
        AssertSavedRange(rotation, "y", -4f, -2.2f);
        AssertSavedRange(rotation, "curve", .9f, 2.5f); // Unity serializes Z as curve.
    }

    private static string SavedComponent(string saved, Object component)
    {
        Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string _, out long id), Is.True);
        string block = Regex.Match(saved,
            @"(?ms)^--- !u!\d+ &" + id.ToString(CultureInfo.InvariantCulture) + @"\n.*?(?=^--- !u!|\z)").Value;
        Assert.That(block, Is.Not.Empty, "Missing saved component " + component.name);
        return block;
    }

    private static string SavedSection(string saved, string name, int indent)
    {
        string spaces = new string(' ', indent);
        string section = Regex.Match(saved,
            @"(?ms)^" + spaces + Regex.Escape(name) + @":\n.*?(?=^" + spaces + @"\S|\z)").Value;
        Assert.That(section, Is.Not.Empty, "Missing saved section " + name);
        return section;
    }

    private static float SavedNumber(string saved, string name, int indent)
    {
        var value = Regex.Match(saved,
            @"(?m)^" + new string(' ', indent) + Regex.Escape(name) + @": ([^\n]+)$");
        Assert.That(value.Success, Is.True, "Missing saved property " + name);
        return float.Parse(value.Groups[1].Value.Trim(), CultureInfo.InvariantCulture);
    }

    private static void AssertSavedRange(string saved, string name, float minimum, float maximum)
    {
        string range = SavedSection(saved, name, 4);
        Assert.That(SavedNumber(range, "minMaxState", 6), Is.EqualTo(3f), name + " uses two constants");
        Assert.That(SavedNumber(range, "minScalar", 6), Is.EqualTo(minimum).Within(.0001f), name);
        Assert.That(SavedNumber(range, "scalar", 6), Is.EqualTo(maximum).Within(.0001f), name);
    }
}
