#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Weapon assets are authored inputs, including when the economy starts.</summary>
public sealed class WeaponBalanceStartupTests
{
    private const string StateKey = "WeaponBalanceStartupTests.State";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly string[] WeaponNames =
    {
        "Flamethrower", "RocketLauncher", "Mortar", "AutomaticCannon", "RotatingBlade"
    };

    [Serializable]
    private sealed class AssetSnapshot
    {
        public string Path;
        public string Json;
        public bool Dirty;
        public string Sha256;
    }

    [Serializable]
    private sealed class SavedScene
    {
        public string Path;
        public bool Loaded;
        public bool Active;
    }

    [Serializable]
    private sealed class StartupState
    {
        public AssetSnapshot[] Assets;
        public SavedScene[] Scenes;
    }

    [UnityTest]
    public IEnumerator EnterPlayMode_PreservesAuthoredWeaponsAndDoesNotDirtyOrSaveThem()
    {
        Assert.That(Application.isPlaying, Is.False);
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False,
                "Save or close edited scenes before running this startup test; it never saves user scenes.");

        SceneSetup[] sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        bool hasUnnamedScene = sceneSetup.Any(scene => string.IsNullOrEmpty(scene.path));
        Assert.That(!hasUnnamedScene || sceneSetup.Length == 1, Is.True,
            "Close unnamed additive scenes before running this test; only a sole clean unnamed scene can be restored safely.");

        string[] paths = AssetDatabase.FindAssets("t:WeaponData", new[]
            {
                "Assets/ScriptableObjects/WeaponSO",
                "Assets/Scripts/Weapon/Testing/SO"
            })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(path => path).ToArray();
        foreach (string name in WeaponNames)
            Assert.That(paths, Does.Contain("Assets/ScriptableObjects/WeaponSO/" + name + ".asset"));

        var state = new StartupState
        {
            Assets = paths.Select(Capture).ToArray(),
            Scenes = sceneSetup.Select(scene => new SavedScene
            {
                Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive
            }).ToArray()
        };
        // The test iterator and its fields can be recreated by a domain reload.
        SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        yield return new EnterPlayMode();
        yield return null;

        Assert.That(Application.isPlaying, Is.True);
        Assert.That(Object.FindAnyObjectByType<EconomyBootstrap>(), Is.Not.Null,
            "The real BeforeSceneLoad/Awake startup must execute for this regression.");
        Assert.That(EconomyBootstrap.RuntimeMaterialBalance, Is.Not.Null,
            "Material economy initialization must remain available.");
        AssertUnchanged(ReadState(), "during startup playback");

        yield return new ExitPlayMode();
        AssertUnchanged(ReadState(), "after leaving Play Mode");
    }

    [UnityTearDown]
    public IEnumerator RestoreSceneSetup()
    {
        if (Application.isPlaying)
            yield return new ExitPlayMode();

        string raw = SessionState.GetString(StateKey, string.Empty);
        if (string.IsNullOrEmpty(raw))
            yield break;

        StartupState state = JsonUtility.FromJson<StartupState>(raw);
        try
        {
            RestoreWeaponAssets(state);
        }
        finally
        {
            if (state.Scenes.Length > 0 && state.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
                EditorSceneManager.RestoreSceneManagerSetup(state.Scenes.Select(scene => new SceneSetup
                {
                    path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active
                }).ToArray());
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.EraseString(StateKey);
        }
    }

    [Test]
    public void MaterialInitialization_PreservesManualSentinelStatsAndAdvancedPathsForEveryWeaponType()
    {
        Assert.That(Application.isPlaying, Is.False);
        var cleanup = new List<Object>();
        FieldInfo runtimeMaterial = typeof(EconomyBootstrap).GetField(
            "<RuntimeMaterialBalance>k__BackingField", PrivateStatic);
        Assert.That(runtimeMaterial, Is.Not.Null);
        object previousMaterial = runtimeMaterial.GetValue(null);
        try
        {
            var weapons = new List<WeaponData>();
            foreach (string name in WeaponNames)
            {
                WeaponData source = AssetDatabase.LoadAssetAtPath<WeaponData>(
                    "Assets/ScriptableObjects/WeaponSO/" + name + ".asset");
                Assert.That(source, Is.Not.Null, name);
                WeaponData data = Object.Instantiate(source);
                cleanup.Add(data);
                AuthorSentinels(data);
                weapons.Add(data);
            }

            string[] before = weapons.Select(data => EditorJsonUtility.ToJson(data)).ToArray();
            bool[] dirtyBefore = weapons.Select(data => EditorUtility.IsDirty(data)).ToArray();
            var material = ScriptableObject.CreateInstance<MaterialUsageBalanceSO>();
            cleanup.Add(material);
            material.SetData(new List<MaterialRoleAssignment>
            {
                new() { Column = WeaponMaterialColumn.Flamethrower, Material = MaterialType.Gears,
                    Role = MaterialRole.Principal }
            }, new List<MaterialRoleTotalRow>
            {
                new() { Role = MaterialRole.Principal, Level = 4, Total = 57 }
            });

            var owner = new GameObject("Isolated material initialization regression");
            cleanup.Add(owner);
            owner.SetActive(false);
            EconomyBootstrap bootstrap = owner.AddComponent<EconomyBootstrap>();
            typeof(EconomyBootstrap).GetField("_materialBalance", PrivateInstance).SetValue(bootstrap, material);
            // If the removed field returns, direct its importer to disposable clones, never production.
            typeof(EconomyBootstrap).GetField("_weaponDataAssets", PrivateInstance)
                ?.SetValue(bootstrap, weapons.ToArray());
            MethodInfo initialize = typeof(EconomyBootstrap).GetMethod("LoadBalanceData", PrivateInstance);
            Assert.That(initialize, Is.Not.Null);
            initialize.Invoke(bootstrap, null);

            Assert.That(EconomyBootstrap.RuntimeMaterialBalance, Is.SameAs(material));
            Assert.That(material.GetTotalForRole(MaterialRole.Principal, 4), Is.EqualTo(57));
            for (int i = 0; i < weapons.Count; i++)
            {
                Assert.That(EditorJsonUtility.ToJson(weapons[i]), Is.EqualTo(before[i]), weapons[i].WeaponType.ToString());
                Assert.That(EditorUtility.IsDirty(weapons[i]), Is.EqualTo(dirtyBefore[i]), weapons[i].WeaponType.ToString());
            }
        }
        finally
        {
            runtimeMaterial.SetValue(null, previousMaterial);
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null)
                    Object.DestroyImmediate(cleanup[i]);
        }
    }

    [Test]
    public void EditorStartup_DoesNotRegisterTheRetiredAutomaticBalanceImporter()
    {
        // A restored InitializeOnLoad hook could save production tuning before a scene starts.
        Type importer = typeof(BalanceImportMenu).Assembly.GetType("BalanceAutoImporter");
        Assert.That(importer, Is.Null, "BalanceAutoImporter must not be registered on editor/domain startup.");
    }

    private static void AuthorSentinels(WeaponData data)
    {
        data.BaseDamage = 137.25f;
        data.BaseAttackRate = 2.75f;
        data.BaseRange = 19.25f;
        data.BaseKnockback = 3.125f;
        data.BaseManualAmmo = 213f;
        data.ActiveAbilityAmmoCost = 17f;
        data.SkillCooldown = 12.75f;
        data.LevelData = new List<WeaponLevelData>
        {
            Level(1, 1.125f, 0.875f, 1.25f), Level(4, 2.125f, 1.875f, 2.25f)
        };
        data.PathA = new WeaponUpgradePathData
        {
            PathName = "Hand-authored A", DamageMultiplier = 1.375f, AttackRateMultiplier = 0.625f,
            ManualAmmoOverride = 277f,
            LevelData = new List<WeaponLevelData> { Level(6, 3.125f, 2.875f, 3.25f), Level(10, 4.125f, 3.875f, 4.25f) }
        };
        data.PathB = new WeaponUpgradePathData
        {
            PathName = "Hand-authored B", DamageMultiplier = 2.375f, AttackRateMultiplier = 1.625f,
            ManualAmmoOverride = 311f,
            LevelData = new List<WeaponLevelData> { Level(7, 5.125f, 4.875f, 5.25f), Level(9, 6.125f, 5.875f, 6.25f) }
        };
        data.BalanceStats = new List<WeaponBalanceStatRow>
        {
            new() { StatId = "Damage", Zone = WeaponBalanceZone.Basic, Level = 1, Value = 909.25f, Notes = "Preserve authored row" },
            new() { StatId = "Custom advanced stat", Zone = WeaponBalanceZone.PathA, Level = 7, Value = 707.5f,
                Scaling = new WeaponStatScaling { StatType = StatType.DamageMultiplier, Effectiveness = 0.375f } },
            new() { StatId = "Custom advanced stat", Zone = WeaponBalanceZone.PathB, Level = 9, Value = 808.5f }
        };
        data.UpgradeSpecificStats = new List<WeaponUpgradeSpecificStatRow>
        {
            new() { StatId = "Manual path A row", Path = WeaponUpgradePath.PathA, Level = 7, Value = 41.25f },
            new() { StatId = "Manual path B row", Path = WeaponUpgradePath.PathB, Level = 9, Value = 61.25f,
                Scaling = new WeaponStatScaling { StatType = StatType.AttackSpeedMultiplier, Effectiveness = 0.625f } }
        };
    }

    private static WeaponLevelData Level(int level, float damage, float rate, float ammo) => new()
    {
        Level = level, DamageMultiplier = damage, AttackRateMultiplier = rate, ManualAmmoMultiplier = ammo
    };

    private static AssetSnapshot Capture(string path)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        Assert.That(data, Is.Not.Null, path);
        return new AssetSnapshot
        {
            Path = path, Json = EditorJsonUtility.ToJson(data), Dirty = EditorUtility.IsDirty(data), Sha256 = Hash(path)
        };
    }

    private static StartupState ReadState()
    {
        string raw = SessionState.GetString(StateKey, string.Empty);
        Assert.That(raw, Is.Not.Empty, "Startup snapshot must survive domain reload.");
        return JsonUtility.FromJson<StartupState>(raw);
    }

    private static void AssertUnchanged(StartupState state, string phase)
    {
        foreach (AssetSnapshot before in state.Assets)
        {
            AssetSnapshot after = Capture(before.Path);
            Assert.That(after.Json, Is.EqualTo(before.Json), before.Path + " changed in memory " + phase);
            Assert.That(after.Dirty, Is.EqualTo(before.Dirty), before.Path + " dirty state changed " + phase);
            Assert.That(after.Sha256, Is.EqualTo(before.Sha256), before.Path + " changed on disk " + phase);
        }
    }

    private static void RestoreWeaponAssets(StartupState state)
    {
        // Failed startup assertions must not leave designer tuning overwritten in memory.
        // Restore only captured state; never save assets or alter their on-disk contents.
        foreach (AssetSnapshot before in state.Assets)
        {
            WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(before.Path);
            Assert.That(data, Is.Not.Null, "Cannot restore startup snapshot: " + before.Path);
            if (EditorJsonUtility.ToJson(data) != before.Json)
                EditorJsonUtility.FromJsonOverwrite(before.Json, data);
            if (EditorUtility.IsDirty(data) != before.Dirty)
            {
                if (before.Dirty)
                    EditorUtility.SetDirty(data);
                else
                    EditorUtility.ClearDirty(data);
            }
        }
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
#endif
