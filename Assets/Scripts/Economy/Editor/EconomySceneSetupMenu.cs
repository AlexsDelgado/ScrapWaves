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

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }
}
#endif
