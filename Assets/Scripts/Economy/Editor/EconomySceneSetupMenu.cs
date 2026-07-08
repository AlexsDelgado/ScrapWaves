#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class EconomySceneSetupMenu
{
    [MenuItem("ScrapWaves/Economy/Add Economy To Player Prefab")]
    public static void AddEconomyToPlayer()
    {
        const string playerPath = "Assets/Prefabs/player.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
        if (prefab == null)
        {
            Debug.LogError($"Missing player prefab at {playerPath}");
            return;
        }

        GameObject instance = PrefabUtility.LoadPrefabContents(playerPath);
        EnsureComponent<MaterialInventory>(instance);
        EnsureComponent<MaterialPickupReceiver>(instance);
        EnsureComponent<WeaponCraftingService>(instance);
        EnsureComponent<RunStartWeaponChoice>(instance);
        EnsureComponent<CraftingUI>(instance);
        EnsureComponent<MaterialInventoryHUD>(instance);

        WeaponCraftingService crafting = instance.GetComponent<WeaponCraftingService>();
        SerializedObject craftingSo = new SerializedObject(crafting);
        craftingSo.FindProperty("_materialBalance").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset");

        string[] names = { "Flamethrower", "RocketLauncher", "Mortar", "AutomaticCannon", "RotatingBlade" };
        SerializedProperty pool = craftingSo.FindProperty("_weaponPool");
        pool.ClearArray();
        for (int i = 0; i < names.Length; i++)
        {
            pool.InsertArrayElementAtIndex(i);
            pool.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{names[i]}.asset");
        }
        craftingSo.ApplyModifiedPropertiesWithoutUndo();

        RunStartWeaponChoice runStart = instance.GetComponent<RunStartWeaponChoice>();
        SerializedObject runStartSo = new SerializedObject(runStart);
        SerializedProperty runPool = runStartSo.FindProperty("_weaponPool");
        runPool.ClearArray();
        for (int i = 0; i < names.Length; i++)
        {
            runPool.InsertArrayElementAtIndex(i);
            runPool.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{names[i]}.asset");
        }
        runStartSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(instance, playerPath);
        PrefabUtility.UnloadPrefabContents(instance);
        AssetDatabase.SaveAssets();
        Debug.Log("Economy components added to player prefab.");
    }

    [MenuItem("ScrapWaves/Economy/Create Crafting Station In Scene")]
    public static void CreateCraftingStationInScene()
    {
        var station = new GameObject("CraftingStation");
        station.AddComponent<CraftingStation>();
        Selection.activeGameObject = station;
        Undo.RegisterCreatedObjectUndo(station, "Create Crafting Station");
    }

    private const string MaterialOrbPrefabPath = "Assets/Prefabs/material drop.prefab";
    private const string MaterialOrbMaterialPath = "Assets/Prefabs/MaterialDropOrb.mat";

    [MenuItem("ScrapWaves/Economy/Create Material Orb Prefab")]
    public static GameObject CreateMaterialOrbPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MaterialOrbPrefabPath);
        if (existing != null)
        {
            Debug.Log($"Material orb prefab already exists at {MaterialOrbPrefabPath}");
            return existing;
        }

        Material orbMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialOrbMaterialPath);
        if (orbMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            orbMaterial = new Material(shader);
            Color color = new Color(1f, 0.72f, 0.2f);
            if (orbMaterial.HasProperty("_BaseColor"))
                orbMaterial.SetColor("_BaseColor", color);
            if (orbMaterial.HasProperty("_Color"))
                orbMaterial.SetColor("_Color", color);
            AssetDatabase.CreateAsset(orbMaterial, MaterialOrbMaterialPath);
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        temp.name = "material drop";
        Collider collider = temp.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        temp.transform.localScale = Vector3.one * 0.35f;
        temp.GetComponent<MeshRenderer>().sharedMaterial = orbMaterial;
        temp.AddComponent<MaterialDrop>();
        temp.AddComponent<MaterialPoolMember>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, MaterialOrbPrefabPath);
        Object.DestroyImmediate(temp);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created material orb prefab at {MaterialOrbPrefabPath}");
        return prefab;
    }

    [MenuItem("ScrapWaves/Economy/Create Material Pool In Scene")]
    public static void CreateMaterialPoolInScene()
    {
        MaterialPool existing = Object.FindAnyObjectByType<MaterialPool>();
        if (existing != null)
        {
            Debug.Log("A MaterialPool already exists in the scene.", existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        GameObject orbPrefab = CreateMaterialOrbPrefab();

        var poolGo = new GameObject("MaterialPool");
        MaterialPool pool = poolGo.AddComponent<MaterialPool>();

        SerializedObject so = new SerializedObject(pool);
        so.FindProperty("_materialOrbPrefab").objectReferenceValue = orbPrefab;
        so.FindProperty("_initialPoolSize").intValue = 128;
        so.FindProperty("_allowPoolGrowth").boolValue = true;
        so.FindProperty("_maxPoolSize").intValue = 612;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = poolGo;
        Undo.RegisterCreatedObjectUndo(poolGo, "Create Material Pool");
        Debug.Log("Created MaterialPool in scene. Remember to save the scene.", poolGo);
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }
}
#endif
