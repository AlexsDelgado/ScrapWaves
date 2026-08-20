using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Crea y mantiene <c>GameplayScene</c> como nivel 1 de producción (copia de SampleScene + pools).
/// </summary>
public static class GameplaySceneBuilder
{
    public const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
    private const string EnemyBulletPrefabPath = "Assets/Prefabs/EnemyBullet.prefab";

    [MenuItem("Tools/Scenes/Create Gameplay Scene (Level 1)")]
    public static void CreateGameplayScene()
    {
        EnsureSceneAssetExists();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureGameplayPools();
        ApplyBuildSettings();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"GameplayScene lista en {ScenePath}. Escena de juego (nivel 1) configurada en Build Settings y SceneNavigation.");
    }

    public static void EnsureSceneAssetExists()
    {
        if (System.IO.File.Exists(ScenePath))
            return;

        if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            Debug.LogError($"No se pudo copiar {SourceScenePath} → {ScenePath}.");
    }

    private static void EnsureGameplayPools()
    {
        GameObject root = GameObject.Find("GameplayPools");
        if (root == null)
            root = new GameObject("GameplayPools");

        EnemyPoolRegistry registry = GetOrAdd<EnemyPoolRegistry>(root);
        WireRegistryRoulette(registry);

        EnemyProjectilePool projectilePool = GetOrAdd<EnemyProjectilePool>(root);
        GameObject bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBulletPrefabPath);
        if (bulletPrefab != null)
        {
            SerializedObject projectileSo = new SerializedObject(projectilePool);
            projectileSo.FindProperty("_projectilePrefab").objectReferenceValue = bulletPrefab;
            projectileSo.ApplyModifiedPropertiesWithoutUndo();
        }

        GetOrAdd<EnemyTimedAreaPool>(root);
        GetOrAdd<ExplosionRadiusVfxPool>(root);
        GetOrAdd<EnemyPoolProfilerHud>(root);
    }

    private static void WireRegistryRoulette(EnemyPoolRegistry registry)
    {
        OrbitalSpawner spawner = Object.FindAnyObjectByType<OrbitalSpawner>();
        if (spawner == null)
            return;

        SerializedObject spawnerSo = new SerializedObject(spawner);
        Object config = spawnerSo.FindProperty("_config").objectReferenceValue;

        SerializedObject registrySo = new SerializedObject(registry);
        registrySo.FindProperty("_rouletteConfig").objectReferenceValue = config;
        registrySo.FindProperty("_useEnemyPool").boolValue = true;
        registrySo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T GetOrAdd<T>(GameObject root) where T : Component
    {
        T existing = root.GetComponent<T>();
        if (existing != null)
            return existing;

        return root.AddComponent<T>();
    }

    public static void ApplyBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/TitleScreen.unity", true),
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(SourceScenePath, false),
            new EditorBuildSettingsScene("Assets/Scenes/Testing/WeaponTestingSandbox.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Testing/enemiesTesting.unity", true)
        };
    }
}
