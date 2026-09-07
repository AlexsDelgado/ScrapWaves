#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Crea/actualiza <c>Assets/Scenes/Testing/test_balance.unity</c> para pruebas de balance.
/// </summary>
public static class BalanceTestingSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Testing/test_balance.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/player.prefab";
    private const string ProjectilePoolPrefabPath = "Assets/Prefabs/ProjectilePool.prefab";
    private const string RoulettePath = "Assets/ScriptableObjects/Spawning/DefaultEnemySpawnRoulette.asset";
    private const string StartingWeaponPath = "Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset";
    private const string DestroyerPrefabPath = "Assets/Prefabs/Destroyer_Boss.prefab";
    private const string StalkerPrefabPath = "Assets/Prefabs/Stalker.prefab";

    [MenuItem("Tools/ScrapWaves/Build Balance Testing Scene (test_balance)")]
    public static void CreateOrUpdateScene()
    {
        EnsureFolder("Assets/Scenes", "Testing");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "test_balance";

        CreateLighting();
        CreateGround();
        HeatManager heat = CreateHeatManager();
        CreateProjectilePool();
        Transform player = CreatePlayer();
        CreateMainCamera(player);
        CreateEventSystem();
        CreateDifficultyAndBrain();
        CraftingStation station = CreateCraftingStation(player);
        Transform[] powerupPoints = CreatePowerupSpawnPoints();
        BalanceTestingController controller = CreateController(player, station, powerupPoints);

        _ = heat;
        _ = controller;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EnsureBuildSettingsEntry();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"test_balance scene generated at {ScenePath}. Open it and press Play.");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void CreateLighting()
    {
        GameObject lightGo = new("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.53f, 1f);
    }

    private static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(80f, 0.2f, 80f);
        ground.isStatic = true;
    }

    private static HeatManager CreateHeatManager()
    {
        GameObject go = new("HeatManager");
        return go.AddComponent<HeatManager>();
    }

    private static void CreateProjectilePool()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePoolPrefabPath);
        GameObject instance = prefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
            : new GameObject("ProjectilePool");
        instance.name = "ProjectilePool";
        if (instance.GetComponent<ProjectilePool>() == null)
            instance.AddComponent<ProjectilePool>();
    }

    private static Transform CreatePlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject player = prefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
            : GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.transform.position = new Vector3(0f, 1.1f, 0f);

        // Keep real combat systems; only skip run-start choice / overheat noise.
        RunStartWeaponChoice runStart = player.GetComponent<RunStartWeaponChoice>();
        if (runStart != null)
            runStart.enabled = false;

        OverheatManager overheat = player.GetComponent<OverheatManager>();
        if (overheat != null)
            overheat.enabled = false;

        if (player.GetComponent<TemporaryPowerupController>() == null)
            player.AddComponent<TemporaryPowerupController>();
        if (player.GetComponent<MetaProgressionApplier>() == null)
            player.AddComponent<MetaProgressionApplier>();

        return player.transform;
    }

    private static void CreateMainCamera(Transform player)
    {
        GameObject cameraGo = new("Main Camera");
        cameraGo.tag = "MainCamera";
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 400f;
        cameraGo.AddComponent<AudioListener>();
        ThirdPersonCamera thirdPersonCamera = cameraGo.AddComponent<ThirdPersonCamera>();
        thirdPersonCamera.ApplyMainGameOrbitDefaults();
        thirdPersonCamera.SetFollowTarget(player);
        cameraGo.transform.position = player.position + new Vector3(0f, 1.9f, -4.2f);
        cameraGo.transform.rotation = Quaternion.LookRotation(
            (player.position + new Vector3(0f, 1.2f, 0f)) - cameraGo.transform.position,
            Vector3.up);
    }

    private static void CreateEventSystem()
    {
        GameObject eventSystem = new("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private static void CreateDifficultyAndBrain()
    {
        if (Object.FindAnyObjectByType<DifficultyManager>() == null)
            new GameObject("DifficultyManager").AddComponent<DifficultyManager>();
        if (Object.FindAnyObjectByType<EnemyFollowBrain>() == null)
            new GameObject("EnemyFollowBrain").AddComponent<EnemyFollowBrain>();
    }

    private static CraftingStation CreateCraftingStation(Transform player)
    {
        GameObject stationGo = new("CraftingStation");
        stationGo.transform.position = player.position + Vector3.forward * 4f;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Visual";
        visual.transform.SetParent(stationGo.transform, false);
        visual.transform.localPosition = Vector3.up * 0.5f;
        visual.transform.localScale = new Vector3(1.4f, 0.5f, 1.4f);
        Collider visualCol = visual.GetComponent<Collider>();
        if (visualCol != null)
            Object.DestroyImmediate(visualCol);

        return stationGo.AddComponent<CraftingStation>();
    }

    private static Transform[] CreatePowerupSpawnPoints()
    {
        TemporaryPowerupType[] types = (TemporaryPowerupType[])System.Enum.GetValues(typeof(TemporaryPowerupType));
        Transform[] points = new Transform[types.Length];
        GameObject root = new("PowerupSpawnPoints");
        const float radius = 11f;
        for (int i = 0; i < types.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / types.Length;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"PowerupSpawn_{types[i]}";
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = new Vector3(Mathf.Cos(angle) * radius, 0.15f, Mathf.Sin(angle) * radius);
            marker.transform.localScale = new Vector3(1.2f, 0.08f, 1.2f);
            Collider col = marker.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);
            points[i] = marker.transform;
        }

        return points;
    }

    [MenuItem("Tools/ScrapWaves/Open Balance Testing Scene (test_balance)")]
    public static void OpenScene()
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            CreateOrUpdateScene();
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static BalanceTestingController CreateController(Transform player, CraftingStation station, Transform[] powerupPoints)
    {
        GameObject root = new("BalanceTesting");
        BalanceTestingController controller = root.AddComponent<BalanceTestingController>();
        SerializedObject so = new(controller);
        so.FindProperty("_player").objectReferenceValue = player;
        so.FindProperty("_craftingStation").objectReferenceValue = station;
        so.FindProperty("_rouletteConfig").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<EnemySpawnRouletteConfig>(RoulettePath);
        so.FindProperty("_startingWeapon").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<WeaponData>(StartingWeaponPath);
        so.FindProperty("_destroyerBossPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(DestroyerPrefabPath);
        so.FindProperty("_stalkerBossPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(StalkerPrefabPath);

        SerializedProperty points = so.FindProperty("_powerupSpawnPoints");
        points.arraySize = powerupPoints != null ? powerupPoints.Length : 0;
        for (int i = 0; i < points.arraySize; i++)
            points.GetArrayElementAtIndex(i).objectReferenceValue = powerupPoints[i];

        so.ApplyModifiedPropertiesWithoutUndo();
        return controller;
    }

    private static void EnsureBuildSettingsEntry()
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].path == ScenePath)
            {
                if (!current[i].enabled)
                {
                    current[i].enabled = true;
                    EditorBuildSettings.scenes = current;
                }

                return;
            }
        }

        var list = new List<EditorBuildSettingsScene>(current)
        {
            new(ScenePath, true)
        };
        EditorBuildSettings.scenes = list.ToArray();
    }
}
#endif
