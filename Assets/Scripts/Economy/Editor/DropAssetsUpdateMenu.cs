using UnityEditor;
using UnityEngine;

public static class DropAssetsUpdateMenu
{
    private const string CellBatteryPath = "Assets/Prefabs/Pickups/CellBattery.prefab";
    private const string PlasticPath = "Assets/Prefabs/Pickups/PlasticExplosive.prefab";
    private const string PlasticV2Fbx = "Assets/Art/Drops/PlasticExplosiveV2.fbx";

    [MenuItem("ScrapWaves/Economy/Apply Drop Art Updates (CellBattery + PlasticV2)")]
    public static void Apply()
    {
        EnsureCellBatteryPickup();
        UpdatePlasticExplosive();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Drop art updates applied: CellBattery pickup scripts, PlasticExplosive=PlasticExplosiveV2");
    }

    private static void EnsureCellBatteryPickup()
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(CellBatteryPath);
        if (root == null)
        {
            Debug.LogError($"Missing {CellBatteryPath}");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(CellBatteryPath);
        try
        {
            WorldPickup world = contents.GetComponent<WorldPickup>();
            if (world == null)
                world = contents.AddComponent<WorldPickup>();

            world.PickupRadius = 1.5f;
            world.MagnetRadius = 6f;
            world.MagnetSpeed = 12f;

            SerializedObject soWorld = new SerializedObject(world);
            SerializedProperty useRange = soWorld.FindProperty("_usePlayerPickupRange");
            if (useRange != null)
                useRange.boolValue = true;
            soWorld.ApplyModifiedPropertiesWithoutUndo();

            if (contents.GetComponent<KeyPickup>() == null)
                contents.AddComponent<KeyPickup>();

            PrefabUtility.SaveAsPrefabAsset(contents, CellBatteryPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void UpdatePlasticExplosive()
    {
        MeshFilter sourceFilter = FindFirstMeshFilter(PlasticV2Fbx);
        if (sourceFilter == null)
        {
            Debug.LogError($"No MeshFilter found in {PlasticV2Fbx}");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PlasticPath);
        try
        {
            MeshFilter filter = contents.GetComponent<MeshFilter>();
            MeshRenderer renderer = contents.GetComponent<MeshRenderer>();
            if (filter == null)
                filter = contents.AddComponent<MeshFilter>();
            if (renderer == null)
                renderer = contents.AddComponent<MeshRenderer>();

            filter.sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer != null
                ? sourceRenderer.sharedMaterials
                : renderer.sharedMaterials;

            // Match other material pickups: keep authored orientation, scale near 1
            // because PlasticExplosiveV2 already imports with globalScale 0.3.
            contents.transform.localScale = Vector3.one;
            contents.transform.localRotation = Quaternion.identity;

            PrefabUtility.SaveAsPrefabAsset(contents, PlasticPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static MeshFilter FindFirstMeshFilter(string assetPath)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (root == null)
            return null;

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null && filters[i].sharedMesh != null)
                return filters[i];
        }

        return null;
    }
}
