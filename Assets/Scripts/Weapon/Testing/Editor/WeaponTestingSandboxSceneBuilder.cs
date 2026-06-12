using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public static class WeaponTestingSandboxSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/WeaponTestingSandbox.unity";
    private const string WeaponAssetFolder = "Assets/Scripts/Weapon/Testing/SO";
    private const string DummyPrefabFolder = "Assets/Prefabs/Testing";
    private const string DummyPrefabPath = DummyPrefabFolder + "/WeaponDummyEnemy.prefab";

    [MenuItem("Tools/ScrapWaves/Build Weapon Testing Sandbox")]
    public static void CreateOrUpdateScene()
    {
        EnsureFolders();
        List<WeaponData> weapons = CreateSandboxWeaponAssets();
        GameObject dummyPrefab = CreateDummyPrefab();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "WeaponTestingSandbox";

        CreateLighting();
        Transform arenaRoot = CreateArenaHierarchy();
        Transform spawnedDummiesRoot = new GameObject("Spawned Dummies").transform;
        HeatManager heatManager = CreateHeatManager();
        ProjectilePool projectilePool = CreateProjectilePool();
        Transform player = CreateSandboxPlayer();
        CreateMainCamera(player);
        CreateEventSystem();
        CreateSandboxManager(weapons, dummyPrefab, player, projectilePool, heatManager, arenaRoot, spawnedDummiesRoot);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"WeaponTestingSandbox scene generated at {ScenePath}");
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets/Scripts/Weapon/Testing", "SO");
        EnsureFolder("Assets/Prefabs", "Testing");
        EnsureFolder("Assets/Scenes", null);
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (child == null)
            return;

        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static List<WeaponData> CreateSandboxWeaponAssets()
    {
        return new List<WeaponData>
        {
            CreateWeaponAsset(WeaponType.Flamethrower, "Sandbox_Flamethrower", "Flamethrower", WeaponTargetingMode.IgnoreCameraClosest, WeaponManualMode.Cone, 5f, 1f, 7f, 1f, 100f, 40f, "Jellified Fuel", "Liquid Nitrogen"),
            CreateWeaponAsset(WeaponType.RocketLauncher, "Sandbox_RocketLauncher", "Rocket Launcher", WeaponTargetingMode.ClosestInRange, WeaponManualMode.AimAtReticle, 20f, 1f, 20f, 5f, 40f, 10f, "Kinetic Explosion", "Fragmentation Cap"),
            CreateWeaponAsset(WeaponType.Mortar, "Sandbox_Mortar", "Mortar", WeaponTargetingMode.RandomInRange, WeaponManualMode.AimAtReticle, 18f, 0.75f, 22f, 4f, 36f, 12f, "Grapeshot", "Multi-Charged Shells"),
            CreateWeaponAsset(WeaponType.AutomaticCannon, "Sandbox_AutomaticCannon", "Automatic Cannon", WeaponTargetingMode.ClosestInRange, WeaponManualMode.AimAtReticle, 10f, 5f, 12f, 1f, 200f, 20f, "Continuous Fire", "Head Hunter"),
            CreateWeaponAsset(WeaponType.RotatingBlade, "Sandbox_RotatingBlade", "Rotating Blade", WeaponTargetingMode.IgnoreCameraClosest, WeaponManualMode.Cone, 12f, 1f, 2.4f, 2.5f, 50f, 8f, "Multi-Blade", "Atomic Sharpness")
        };
    }

    private static WeaponData CreateWeaponAsset(
        WeaponType weaponType,
        string assetName,
        string displayName,
        WeaponTargetingMode targetingMode,
        WeaponManualMode manualMode,
        float baseDamage,
        float baseAttackRate,
        float baseRange,
        float baseKnockback,
        float baseManualAmmo,
        float activeAbilityAmmoCost,
        string pathAName,
        string pathBName)
    {
        string path = $"{WeaponAssetFolder}/{assetName}.asset";
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<WeaponData>();
            AssetDatabase.CreateAsset(data, path);
        }

        data.WeaponId = assetName;
        data.DisplayName = displayName;
        data.WeaponType = weaponType;
        data.AutoTargetingMode = targetingMode;
        data.ManualMode = manualMode;
        data.BaseDamage = baseDamage;
        data.BaseAttackRate = baseAttackRate;
        data.BaseRange = baseRange;
        data.BaseKnockback = baseKnockback;
        data.BaseManualAmmo = baseManualAmmo;
        data.ActiveAbilityAmmoCost = activeAbilityAmmoCost;
        data.EnsureSpecificTuningForCurrentType();
        ConfigureLevelData(data);
        ConfigurePathData(data, pathAName, pathBName);
        ConfigureWeaponSpecificData(data);
        EditorUtility.SetDirty(data);
        return data;
    }

    private static void ConfigureLevelData(WeaponData data)
    {
        data.LevelData.Clear();
        for (int level = 1; level <= 10; level++)
        {
            data.LevelData.Add(new WeaponLevelData
            {
                Level = level,
                DamageMultiplier = 1f + (level - 1) * 0.12f,
                AttackRateMultiplier = 1f + (level - 1) * 0.045f,
                ManualAmmoMultiplier = 1f + (level - 1) * 0.055f
            });
        }
    }

    private static void ConfigurePathData(WeaponData data, string pathAName, string pathBName)
    {
        data.PathA ??= new WeaponUpgradePathData();
        data.PathA.PathName = pathAName;
        data.PathA.DamageMultiplier = 1.22f;
        data.PathA.AttackRateMultiplier = 1.12f;
        data.PathA.ManualAmmoOverride = -1f;

        data.PathB ??= new WeaponUpgradePathData();
        data.PathB.PathName = pathBName;
        data.PathB.DamageMultiplier = 1.36f;
        data.PathB.AttackRateMultiplier = 0.92f;
        data.PathB.ManualAmmoOverride = -1f;
    }

    private static void ConfigureWeaponSpecificData(WeaponData data)
    {
        switch (data.WeaponType)
        {
            case WeaponType.AutomaticCannon:
                data.AutomaticCannon.CannonAbilityScatterRadius = 11f;
                data.AutomaticCannon.CannonManualLineSpacing = 1f;
                data.AutomaticCannon.CannonAutoLineSpacing = 1f;
                break;
            case WeaponType.RocketLauncher:
                data.RocketLauncher.RocketActiveConeAngle = 90f;
                data.RocketLauncher.RocketAutoExplosionRadius = 1.8f;
                data.RocketLauncher.RocketManualExplosionRadius = 2.4f;
                break;
            case WeaponType.Flamethrower:
                data.Flamethrower.FlameHoseRadius = 0.75f;
                data.Flamethrower.FlameHoseSegmentCount = 12;
                data.Flamethrower.FlameHoseNearFollow = 28f;
                data.Flamethrower.FlameHoseFarFollow = 2.25f;
                data.Flamethrower.FlameHoseTurbulence = 0.08f;
                data.Flamethrower.FlameActiveRadius = 6f;
                break;
            case WeaponType.Mortar:
                data.Mortar.MortarAutoAccuracyRadius = 3.8f;
                data.Mortar.MortarManualAccuracyRadius = 0.75f;
                data.Mortar.MortarBarrageRadius = 6f;
                data.Mortar.MortarShellCollisionRadius = 0.18f;
                data.Mortar.MortarActiveDropHeight = 14f;
                break;
            case WeaponType.RotatingBlade:
                data.RotatingBlade.BladeOrbitRadius = 2.2f;
                data.RotatingBlade.BladeHitRadius = 0.6f;
                data.RotatingBlade.BladeManualRange = 2.4f;
                data.RotatingBlade.BladeActiveBaseRangeMultiplier = 5f;
                data.RotatingBlade.BladeActiveMaxRangeMultiplier = 10f;
                break;
        }
    }

    private static GameObject CreateDummyPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
        if (existing != null)
            return existing;

        GameObject root = new GameObject("WeaponDummyEnemy");
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = Vector3.up;
        collider.height = 2f;
        collider.radius = 0.5f;

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        root.AddComponent<EnemyRegistryMember>();
        root.AddComponent<EnemyKnockbackReceiver>();
        root.AddComponent<WeaponDummyEnemy>();

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up;
        visual.transform.localScale = Vector3.one;
        Renderer renderer = visual.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateMaterial("WeaponDummyEnemyMaterial", new Color(0.74f, 0.78f, 0.82f, 1f));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, DummyPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static Material CreateMaterial(string name, Color color)
    {
        string path = $"{DummyPrefabFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CreateSandboxManager(
        List<WeaponData> weapons,
        GameObject dummyPrefab,
        Transform player,
        ProjectilePool projectilePool,
        HeatManager heatManager,
        Transform arenaRoot,
        Transform spawnedDummiesRoot)
    {
        GameObject go = new GameObject("WeaponTestingSandbox");
        WeaponTestingSandboxManager manager = go.AddComponent<WeaponTestingSandboxManager>();
        go.AddComponent<WeaponStatOverride>();
        go.AddComponent<WeaponHeatOverride>();
        go.AddComponent<WeaponTestMetrics>();
        go.AddComponent<WeaponDummySpawner>();
        go.AddComponent<WeaponDebugGizmos>();
        go.AddComponent<WeaponSandboxDebugUI>();

        SerializedObject serialized = new SerializedObject(manager);
        serialized.FindProperty("_playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        serialized.FindProperty("_projectilePoolPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProjectilePool.prefab");
        serialized.FindProperty("_dummyPrefab").objectReferenceValue = dummyPrefab;
        serialized.FindProperty("_playerInstance").objectReferenceValue = player;
        serialized.FindProperty("_projectilePoolInstance").objectReferenceValue = projectilePool;
        serialized.FindProperty("_heatManagerInstance").objectReferenceValue = heatManager;
        serialized.FindProperty("_arenaRoot").objectReferenceValue = arenaRoot;
        serialized.FindProperty("_spawnedDummiesRoot").objectReferenceValue = spawnedDummiesRoot;
        serialized.FindProperty("_buildMissingSceneObjectsAtRuntime").boolValue = false;

        SerializedProperty weaponList = serialized.FindProperty("_weaponData");
        weaponList.arraySize = weapons.Count;
        for (int i = 0; i < weapons.Count; i++)
            weaponList.GetArrayElementAtIndex(i).objectReferenceValue = weapons[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private static Transform CreateArenaHierarchy()
    {
        Transform root = new GameObject("Weapon Sandbox Arena").transform;
        CreateFloorTile(root, "Sandbox Floor", Vector3.zero, new Vector3(52f, 0.12f, 52f), new Color(0.12f, 0.13f, 0.14f, 1f));
        CreateZone(root, "Zone 1 - Single Target Test", new Vector3(0f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.18f, 0.26f, 0.32f, 1f), "Single Target Spawn Anchor");
        CreateZone(root, "Zone 2 - Group Damage Test", new Vector3(18f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.24f, 0.22f, 0.14f, 1f), "Group Spawn Anchor");
        CreateZone(root, "Zone 3 - Moving Target Test", new Vector3(-18f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.16f, 0.24f, 0.17f, 1f), "Moving Spawn Anchor");
        CreateZone(root, "Zone 4 - Elite and Boss Test", new Vector3(0f, 0.01f, -16f), new Vector3(16f, 0.08f, 12f), new Color(0.28f, 0.18f, 0.18f, 1f), "Elite Boss Spawn Anchor");
        CreateZone(root, "Zone 5 - Knockback Test", new Vector3(18f, 0.01f, -16f), new Vector3(14f, 0.08f, 18f), new Color(0.18f, 0.18f, 0.29f, 1f), "Knockback Lane Anchor");
        CreateZone(root, "Zone 6 - Heat / Zone 7 - Upgrade Path Test", new Vector3(-18f, 0.01f, -16f), new Vector3(14f, 0.08f, 18f), new Color(0.24f, 0.16f, 0.24f, 1f), "Heat Upgrade Anchor");
        CreateKnockbackLines(root, new Vector3(18f, 0.08f, -22f));
        return root;
    }

    private static void CreateZone(Transform root, string name, Vector3 center, Vector3 scale, Color color, string anchorName)
    {
        Transform zone = new GameObject(name).transform;
        zone.SetParent(root);
        CreateFloorTile(zone, "Zone Tile", center, scale, color);
        CreateWorldLabel(zone, name, center + new Vector3(0f, 0.25f, -scale.z * 0.45f));

        Transform anchor = new GameObject(anchorName).transform;
        anchor.SetParent(zone);
        anchor.position = new Vector3(center.x, 0f, center.z);
    }

    private static void CreateFloorTile(Transform parent, string name, Vector3 center, Vector3 scale, Color color)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = name;
        tile.transform.SetParent(parent);
        tile.transform.position = center;
        tile.transform.localScale = scale;
        tile.GetComponent<Renderer>().sharedMaterial = CreateMaterial(MakeAssetName(name) + "Material", color);
    }

    private static void CreateWorldLabel(Transform parent, string text, Vector3 position)
    {
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(parent);
        labelGo.transform.position = position;
        labelGo.transform.rotation = Quaternion.Euler(65f, 0f, 0f);

        TextMeshPro label = labelGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = 3f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
    }

    private static void CreateKnockbackLines(Transform parent, Vector3 laneStart)
    {
        Transform lane = new GameObject("Knockback Distance Lines").transform;
        lane.SetParent(parent);

        for (int i = 0; i <= 8; i++)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = $"Distance Line {i * 2}m";
            line.transform.SetParent(lane);
            line.transform.position = laneStart + Vector3.forward * (i * 2f);
            line.transform.localScale = new Vector3(9f, 0.08f, 0.08f);
            line.GetComponent<Renderer>().sharedMaterial = CreateMaterial("KnockbackDistanceLineMaterial", new Color(0.85f, 0.9f, 1f, 1f));
            CreateWorldLabel(lane, $"{i * 2}m", line.transform.position + Vector3.right * 5f + Vector3.up * 0.2f);
        }
    }

    private static HeatManager CreateHeatManager()
    {
        GameObject go = new GameObject("SandboxHeatManager");
        return go.AddComponent<HeatManager>();
    }

    private static ProjectilePool CreateProjectilePool()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProjectilePool.prefab");
        GameObject instance = prefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
            : new GameObject("SandboxProjectilePool");
        instance.name = "SandboxProjectilePool";
        return instance.GetComponent<ProjectilePool>() ?? instance.AddComponent<ProjectilePool>();
    }

    private static Transform CreateSandboxPlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        GameObject player = prefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
            : GameObject.CreatePrimitive(PrimitiveType.Capsule);

        player.name = "WeaponSandboxPlayer";
        player.transform.position = new Vector3(0f, 1.1f, 0f);
        DisableProductionRuntimeComponents(player);
        return player.transform;
    }

    private static void CreateLighting()
    {
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.53f, 1f);
    }

    private static void CreateMainCamera(Transform player)
    {
        GameObject cameraGo = new GameObject("Main Camera");
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
        cameraGo.transform.rotation = Quaternion.LookRotation((player.position + new Vector3(0f, 1.2f, 0f)) - cameraGo.transform.position, Vector3.up);
    }

    private static void CreateEventSystem()
    {
        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private static void DisableProductionRuntimeComponents(GameObject player)
    {
        WeaponManager weaponManager = player.GetComponent<WeaponManager>();
        if (weaponManager != null)
            weaponManager.enabled = false;

        PlayerAutoAttack autoAttack = player.GetComponent<PlayerAutoAttack>();
        if (autoAttack != null)
            autoAttack.enabled = false;

        LevelUpOrchestrator levelUpOrchestrator = player.GetComponent<LevelUpOrchestrator>();
        if (levelUpOrchestrator != null)
            levelUpOrchestrator.enabled = false;

        OverheatManager overheatManager = player.GetComponent<OverheatManager>();
        if (overheatManager != null)
            overheatManager.enabled = false;

        WeaponDebugMonitor monitor = player.GetComponent<WeaponDebugMonitor>();
        if (monitor != null)
            monitor.enabled = false;
    }

    private static string MakeAssetName(string name)
    {
        return name.Replace(" ", "").Replace("/", "").Replace("-", "");
    }
}
