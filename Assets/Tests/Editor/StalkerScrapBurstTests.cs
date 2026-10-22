using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Exercises actual particle lifetime and bounded reuse without physics debris.</summary>
public sealed class StalkerScrapBurstTests
{
    private const string SetupKey = "StalkerScrapBurstTests.SceneSetup";
    private const string ResourcePath = "VFX/StalkerScrapBurst";
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator ScrapBursts_UseFourSharedMeshesExpireAndReuseBoundedParticlesWithoutGameplayPhysics()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        yield return RunParticleLifecycle();
    }

    private IEnumerator RunParticleLifecycle()
    {
        Assert.That(Application.isPlaying, Is.True);
        Time.timeScale = 1f;
        Time.captureDeltaTime = 1f / 60f;
        Vector3 origin = new(17f, 3f, -9f);
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        Assert.That(prefab, Is.Not.Null, "The scrap manager must be available in a player build through Resources.");
        Assert.That(Object.FindObjectsByType<StalkerScrapBurstVfx>(FindObjectsSortMode.None), Is.Empty);

        UnityEngine.Random.State gameplayRandom = UnityEngine.Random.state;
        GigaWormGroundBurstVfx.Spawn(origin);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(gameplayRandom), "Creating cosmetic scrap consumed gameplay randomness.");
        StalkerScrapBurstVfx manager = Object.FindAnyObjectByType<StalkerScrapBurstVfx>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()), "The manager must belong to the current scene.");
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(3), "The authored following trail should remain a small burst.");
        Assert.That(manager.TrailBurstCount, Is.EqualTo(1));
        Assert.That(manager.EntryBurstCount, Is.EqualTo(0));
        ParticleSystem[] systems = Systems(manager.gameObject);
        ParticleSystem[] authored = Systems(prefab);
        Assert.That(systems.Length, Is.EqualTo(4), "Cog, bolt, nail and nut should each use one reusable particle system.");
        Assert.That(authored.Length, Is.EqualTo(4));
        int[] systemIds = systems.Select(system => system.GetInstanceID()).ToArray();
        int[] objectIds = manager.GetComponentsInChildren<Transform>(true).Select(item => item.gameObject.GetInstanceID()).ToArray();
        int[] sceneObjectIds = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(item => item.gameObject.scene == manager.gameObject.scene).Select(item => item.gameObject.GetInstanceID()).ToArray();
        Mesh[] sharedMeshes = systems.Select(system => system.GetComponent<ParticleSystemRenderer>().mesh).ToArray();
        Material[] sharedMaterials = systems.Select(system => system.GetComponent<ParticleSystemRenderer>().sharedMaterial).ToArray();
        Assert.That(sharedMeshes.Distinct().Count(), Is.EqualTo(4), "The four scrap families must have distinct mesh geometry.");
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer source = authored[i].GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
            Assert.That(renderer.mesh, Is.Not.Null);
            Assert.That(renderer.mesh, Is.SameAs(source.mesh), "A burst must share its authored mesh instead of cloning it.");
            Assert.That(renderer.mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(AssetDatabase.GetAssetPath(renderer.mesh).StartsWith("Assets/", StringComparison.Ordinal), Is.True,
                "Scrap must use the authored mesh asset rather than a built-in primitive.");
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            Assert.That(renderer.sharedMaterial, Is.SameAs(source.sharedMaterial), "A burst must not create a material instance per piece.");
            Assert.That(systems[i].main.maxParticles, Is.EqualTo(StalkerScrapBurstVfx.MaxParticlesPerKind));
            Assert.That(systems[i].collision.enabled, Is.False);
            Assert.That(systems[i].trigger.enabled, Is.False);
            Assert.That(systems[i].main.loop, Is.False);
        }
        AssertCosmeticOnly(manager);
        AssertParticlesBounded(manager, systems);
        Expire(systems);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0), "Particles must expire without leaving persistent debris.");

        Vector3 entry = origin + new Vector3(12f, 0f, 8f);
        GigaWormGroundBurstVfx.SpawnEntry(entry);
        Assert.That(Object.FindAnyObjectByType<StalkerScrapBurstVfx>(), Is.SameAs(manager));
        Assert.That(manager.EntryBurstCount, Is.EqualTo(1));
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(20), "Ground entry should use its authored larger burst.");
        Assert.That(Vector3.Distance(manager.LastEntryPosition, entry), Is.LessThan(0.001f));
        AssertParticlesBounded(manager, systems);
        Expire(systems);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0));

        // Explicit zero means no pieces; only a negative request selects authored defaults.
        manager.Emit(entry, true, 0);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0));
        manager.Emit(origin, false, int.MaxValue);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(StalkerScrapBurstVfx.MaxPiecesPerBurst));
        for (int burst = 0; burst < 24; burst++)
        {
            manager.Emit(origin + Vector3.right * burst, burst % 2 == 0, int.MaxValue);
            AssertParticlesBounded(manager, systems);
        }
        Assert.That(UnityEngine.Random.state, Is.EqualTo(gameplayRandom), "Repeated cosmetic bursts changed the gameplay/spit random stream.");
        Assert.That(manager.ActiveParticleCount, Is.GreaterThan(0));
        Assert.That(manager.ActiveParticleCount, Is.LessThanOrEqualTo(96));
        Assert.That(Systems(manager.gameObject).Select(system => system.GetInstanceID()), Is.EquivalentTo(systemIds));
        Assert.That(manager.GetComponentsInChildren<Transform>(true).Select(item => item.gameObject.GetInstanceID()),
            Is.EquivalentTo(objectIds), "Repeated bursts must not accumulate debris GameObjects.");
        Assert.That(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene == manager.gameObject.scene).Select(item => item.gameObject.GetInstanceID()),
            Is.EquivalentTo(sceneObjectIds), "Repeated bursts created extra debris outside the manager hierarchy.");
        Assert.That(Object.FindObjectsByType<StalkerScrapBurstVfx>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        AssertCosmeticOnly(manager);
        Expire(systems);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0), "Even a saturated burst must drain within the bounded lifetime.");

        GigaWormGroundBurstVfx.SpawnEntry(origin);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(20), "The same systems must emit again after draining.");
        for (int i = 0; i < systems.Length; i++)
        {
            Assert.That(systems[i].GetInstanceID(), Is.EqualTo(systemIds[i]));
            ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.mesh, Is.SameAs(sharedMeshes[i]));
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterials[i]));
        }
        Expire(systems);
        yield return null;
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0), "No automatic emission should restart after the burst expires.");
    }

    private static ParticleSystem[] Systems(GameObject root) => root.GetComponentsInChildren<ParticleSystem>(true)
        .OrderBy(system => system.name, StringComparer.Ordinal).ToArray();

    private static void AssertCosmeticOnly(StalkerScrapBurstVfx manager)
    {
        Assert.That(manager.GetComponentsInChildren<Collider>(true), Is.Empty, "Decorative scrap must never enter gameplay collision.");
        Assert.That(manager.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(manager.GetComponentsInChildren<EnemyHealth>(true), Is.Empty);
        Assert.That(manager.GetComponentsInChildren<EnemyDamageHitZone>(true), Is.Empty);
        Assert.That(manager.GetComponentsInChildren<Projectile>(true), Is.Empty);
        Assert.That(manager.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component is IDamageable), Is.False);
    }

    private static void AssertParticlesBounded(StalkerScrapBurstVfx manager, ParticleSystem[] systems)
    {
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(systems.Sum(system => system.particleCount)));
        Assert.That(manager.ActiveParticleCount, Is.LessThanOrEqualTo(96));
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[StalkerScrapBurstVfx.MaxParticlesPerKind];
        foreach (ParticleSystem system in systems)
        {
            Assert.That(system.particleCount, Is.LessThanOrEqualTo(StalkerScrapBurstVfx.MaxParticlesPerKind));
            int count = system.GetParticles(particles);
            for (int i = 0; i < count; i++)
                Assert.That(particles[i].startLifetime, Is.InRange(0.001f, 2.5f), "A scrap particle escaped the lifetime bound.");
        }
    }

    private static void Expire(ParticleSystem[] systems)
    {
        foreach (ParticleSystem system in systems) system.Simulate(3f, false, false);
    }

    private static void SaveAndClearScene()
    {
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = EditorSceneManager.GetSceneManagerSetup().Select(scene => new SavedScene
                { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [UnityTearDown]
    public IEnumerator CleanupAndRestoreScene()
    {
        Time.timeScale = 1f;
        Time.captureDeltaTime = 0f;
        if (Application.isPlaying) yield return new ExitPlayMode();
        string json = SessionState.GetString(SetupKey, string.Empty);
        SessionState.EraseString(SetupKey);
        if (string.IsNullOrEmpty(json)) yield break;
        SavedSetup saved = JsonUtility.FromJson<SavedSetup>(json);
        SceneSetup[] setup = saved.Scenes.Where(scene => !string.IsNullOrEmpty(scene.Path)).Select(scene =>
            new SceneSetup { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray();
        if (setup.Length > 0 && setup.Any(scene => scene.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
    }
}
