using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class OverheatEnemyCapRuntimeTests
{
    private const string SaveDirectoryVariable = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
    private const string SaveIsolationKey = "OverheatEnemyCapRuntimeTests.SaveIsolationActive";
    private const string PreviousSaveDirectoryKey = "OverheatEnemyCapRuntimeTests.PreviousSaveDirectory";
    private const string HadSaveDirectoryKey = "OverheatEnemyCapRuntimeTests.HadSaveDirectory";

    [UnityTest]
    public IEnumerator NativeActivationDeathAndPoolReuse_KeepSourceMembershipForOnlyTheCurrentLease()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // SessionState survives the domain reload on Play Mode entry/exit. Isolate before
        // entering so SaveManager's bootstrap cannot load or write the user's profile.
        string previousDirectory = Environment.GetEnvironmentVariable(SaveDirectoryVariable);
        SessionState.SetBool(HadSaveDirectoryKey, previousDirectory != null);
        SessionState.SetString(PreviousSaveDirectoryKey, previousDirectory ?? string.Empty);
        SessionState.SetBool(SaveIsolationKey, true);
        Environment.SetEnvironmentVariable(SaveDirectoryVariable,
            Path.Combine(Path.GetTempPath(), "ScrapWaves-CapTests", Guid.NewGuid().ToString("N")));
        yield return new EnterPlayMode();
        GameObject world = new("Native enemy cap fixture");
        SaveManager previousSave = SaveManager.Instance;
        try
        {
            // Native death must exercise OnDied/despawn without awarding persistent progress.
            typeof(SaveManager).GetProperty(nameof(SaveManager.Instance)).SetValue(null, null);
            GameObject holder = Child(world.transform, "Pool instances");
            holder.SetActive(true);
            GameObject prefab = Child(world.transform, "Shared source prefab");
            prefab.AddComponent<EnemyHealth>().ApplyConfiguredMaxHealth(20);
            prefab.AddComponent<SwarmPooledEnemy>();
            // Keep unrelated loot rolls out of this lifecycle test.
            prefab.AddComponent<EnemyScrapDrop>().enabled = false;
            prefab.AddComponent<EnemyTemporaryPowerupDrop>().enabled = false;
            GameObject registryObject = Child(world.transform, "Pool registry");
            EnemyPoolRegistry registry = registryObject.AddComponent<EnemyPoolRegistry>();
            Set(registry, "_container", holder.transform);
            Set(registry, "_allowPoolGrowth", false);
            Set(registry, "_entries", new[]
            {
                new EnemyPoolRegistry.Entry { Prefab = prefab, InitialSize = 1, MaxSize = 1 }
            });
            registryObject.SetActive(true);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);

            Assert.That(registry.TryGet(prefab, out GameObject first), Is.True);
            Assert.That(first.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(20), "Native Awake/OnEnable must initialize the lease.");
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero, "Activation alone is not successful admission.");
            EnemySpawnPopulationMember.RecordSpawn(first, EnemySpawnSource.Orbital);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.EqualTo(1));
            first.SetActive(false);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);
            first.SetActive(true);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero, "Native reenable cannot restore the old source.");
            EnemySpawnPopulationMember.RecordSpawn(first, EnemySpawnSource.InWorld);
            Assert.That(first.GetComponent<EnemySpawnPopulationMember>().Source, Is.EqualTo(EnemySpawnSource.InWorld));
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);
            registry.Release(first);
            Assert.That(registry.TotalLeased, Is.Zero);

            Assert.That(registry.TryGet(prefab, out GameObject reused), Is.True);
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.GetComponent<EnemySpawnPopulationMember>().Source, Is.EqualTo(EnemySpawnSource.Unassigned));
            EnemySpawnPopulationMember.RecordSpawn(reused, EnemySpawnSource.Orbital);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.EqualTo(1));
            reused.GetComponent<EnemyHealth>().ForceKill();
            Assert.That(registry.TotalLeased, Is.Zero);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero, "Death followed by native pool disable must release exactly once.");

            Assert.That(registry.TryGet(prefab, out GameObject objective), Is.True);
            Assert.That(objective, Is.SameAs(first));
            EnemySpawnPopulationMember.RecordSpawn(objective, EnemySpawnSource.Objective);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.EqualTo(1));
            registry.Release(objective);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);
            Assert.That(registry.TryGet(prefab, out GameObject inWorld), Is.True);
            Assert.That(inWorld, Is.SameAs(first));
            EnemySpawnPopulationMember.RecordSpawn(inWorld, EnemySpawnSource.InWorld);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);
            inWorld.GetComponent<EnemyHealth>().ForceKill();
            Assert.That(registry.TotalLeased, Is.Zero);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);

            GameObject unpooled = Child(world.transform, "Unpooled enemy");
            unpooled.AddComponent<EnemyHealth>();
            unpooled.SetActive(true);
            EnemySpawnPopulationMember.RecordSpawn(unpooled, EnemySpawnSource.Orbital);
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.EqualTo(1));
            Object.Destroy(unpooled);
            yield return null;
            Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero, "Native destruction also releases admission.");
        }
        finally
        {
            Object.Destroy(world);
            typeof(SaveManager).GetProperty(nameof(SaveManager.Instance)).SetValue(null, previousSave);
        }
        yield return null;
        Assert.That(EnemySpawnPopulationMember.ActiveCount, Is.Zero);
        yield return new ExitPlayMode();
    }

    private static GameObject Child(Transform parent, string name)
    {
        GameObject go = new(name);
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        if (!SessionState.GetBool(SaveIsolationKey, false)) yield break;
        Environment.SetEnvironmentVariable(SaveDirectoryVariable,
            SessionState.GetBool(HadSaveDirectoryKey, false)
                ? SessionState.GetString(PreviousSaveDirectoryKey, string.Empty)
                : null);
        SessionState.EraseBool(SaveIsolationKey);
        SessionState.EraseBool(HadSaveDirectoryKey);
        SessionState.EraseString(PreviousSaveDirectoryKey);
    }
}
