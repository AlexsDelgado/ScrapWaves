#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Adds per-instance stat ranges to normal/elite prefabs without editing scenes or boss assets.</summary>
public static class EnemyStatRandomizerAuthoring
{
    public static readonly string[] EnemyPrefabPaths =
    {
        "Assets/Prefabs/Enemy.prefab", "Assets/Prefabs/EnemyPro.prefab",
        "Assets/Prefabs/Slime (variant).prefab", "Assets/Prefabs/Slime_Elite.prefab",
        "Assets/Prefabs/Chaser.prefab", "Assets/Prefabs/Chaser (variant).prefab",
        "Assets/Prefabs/Chaser_Elite.prefab", "Assets/Prefabs/Drone.prefab",
        "Assets/Prefabs/Drone (variant).prefab", "Assets/Prefabs/Drone_Elite.prefab"
    };

    public static readonly string[] BossPrefabPaths =
    {
        "Assets/Prefabs/Boss.prefab", "Assets/Prefabs/Boss_2.prefab",
        "Assets/Prefabs/Stalker.prefab", "Assets/Prefabs/Destroyer_Boss.prefab"
    };

    [MenuItem("Tools/ScrapWaves/Enemies/Add Stat Randomizer To Normal And Elite Prefabs")]
    public static void ApplyBatch()
    {
        RequireEditMode();

        // Validate the complete target list before saving the first prefab.
        foreach (string path in EnemyPrefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<EnemyHealth>() == null)
                throw new InvalidOperationException("Expected an enemy root at " + path);
            if (prefab.GetComponentInChildren<BossKeyDrop>(true) != null)
                throw new InvalidOperationException("Boss must not receive a randomizer: " + path);
        }
        ValidateBossExclusions();

        int changed = 0;
        foreach (string path in EnemyPrefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Preserve any designer tuning on subsequent runs.
                if (root.GetComponent<EnemyStatRandomizer>() != null) continue;
                EnemyStatRandomizer randomizer = root.AddComponent<EnemyStatRandomizer>();
                var serialized = new SerializedObject(randomizer);
                serialized.FindProperty("_healthMultiplierRange").vector2Value = Vector2.one;
                serialized.FindProperty("_sizeMultiplierRange").vector2Value = Vector2.one;
                serialized.FindProperty("_movementSpeedMultiplierRange").vector2Value = Vector2.one;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save " + path);
                changed++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"ENEMY_STAT_RANDOMIZER_AUTHORING changed={changed}; " + Validate());
    }

    [MenuItem("Tools/ScrapWaves/Enemies/Validate Stat Randomizer Authoring")]
    public static void ValidateMenu() => Debug.Log(Validate());

    public static string Validate()
    {
        RequireEditMode();
        var report = new StringBuilder();
        foreach (string path in EnemyPrefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponents<EnemyStatRandomizer>().Length != 1)
                throw new InvalidOperationException("Expected exactly one root randomizer: " + path);
            var serialized = new SerializedObject(prefab.GetComponent<EnemyStatRandomizer>());
            ValidateRange(serialized, "_healthMultiplierRange", path);
            ValidateRange(serialized, "_sizeMultiplierRange", path);
            ValidateRange(serialized, "_movementSpeedMultiplierRange", path);
            report.AppendLine("PASS " + path);
        }
        ValidateBossExclusions();
        report.Append("PASS normal/elite=10, excluded bosses=4; scenes untouched.");
        return report.ToString();
    }

    private static void ValidateBossExclusions()
    {
        foreach (string path in BossPrefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Missing boss prefab: " + path);
            if (prefab.GetComponentInChildren<EnemyStatRandomizer>(true) != null)
                throw new InvalidOperationException("Boss has a randomizer: " + path);
        }
    }

    private static void ValidateRange(SerializedObject serialized, string propertyName, string path)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException("Missing range " + propertyName + " on " + path);
        Vector2 range = property.vector2Value;
        if (float.IsNaN(range.x) || float.IsInfinity(range.x) || float.IsNaN(range.y)
            || float.IsInfinity(range.y) || range.x <= 0f || range.y < range.x)
            throw new InvalidOperationException("Invalid " + propertyName + " on " + path);
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run stat randomizer authoring outside Play Mode.");
    }
}
#endif
