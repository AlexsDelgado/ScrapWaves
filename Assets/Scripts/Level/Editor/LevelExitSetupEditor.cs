#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LevelExitSetupEditor
{
    const string KeyPickupPrefabPath = "Assets/Prefabs/Pickups/KeyPickup.prefab";
    const string ExitDoorPrefabPath = "Assets/Prefabs/Level/ExitDoor.prefab";
    const string BossPrefabPath = "Assets/Prefabs/Boss.prefab";
    const string Boss2PrefabPath = "Assets/Prefabs/Boss_2.prefab";
    const string GameplayHudV2Path = "Assets/Prefabs/UI/GameplayHud V2.prefab";

    [MenuItem("ScrapWaves/Level/Setup Exit System")]
    public static void SetupExitSystem()
    {
        EnsureDirectory("Assets/Prefabs/Pickups");
        EnsureDirectory("Assets/Prefabs/Level");

        GameObject keyPrefab = CreateOrLoadKeyPickupPrefab();
        GameObject doorPrefab = CreateOrLoadExitDoorPrefab();
        WireBossPrefabs(keyPrefab);

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogWarning("LevelExitSetup: abrí SampleScene antes de ejecutar el setup de escena.");
            AssetDatabase.SaveAssets();
            return;
        }

        SetupSceneObjects(keyPrefab, doorPrefab);
        WireGameplayHud();

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("LevelExitSetup: sistema de salida configurado.");
    }

    private static GameObject CreateOrLoadKeyPickupPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPickupPrefabPath);
        if (existing != null)
            return existing;

        var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        root.name = "KeyPickup";
        root.transform.localScale = Vector3.one * 0.65f;
        Object.DestroyImmediate(root.GetComponent<Collider>());
        root.AddComponent<KeyPickup>();

        var renderer = root.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.95f, 0.82f, 0.15f, 1f);
            renderer.sharedMaterial = mat;
            AssetDatabase.CreateAsset(mat, "Assets/Prefabs/Pickups/KeyPickup_Mat.mat");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, KeyPickupPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateOrLoadExitDoorPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ExitDoorPrefabPath);
        if (existing != null)
            return existing;

        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "ExitDoor";
        root.transform.localScale = new Vector3(3f, 4f, 0.6f);

        var collider = root.GetComponent<BoxCollider>();
        if (collider != null)
            collider.isTrigger = false;

        var renderer = root.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.25f, 0.55f, 0.95f, 1f);
            renderer.sharedMaterial = mat;
            AssetDatabase.CreateAsset(mat, "Assets/Prefabs/Level/ExitDoor_Mat.mat");
        }

        root.AddComponent<ExitDoor>();

        var interaction = new GameObject("InteractionPoint");
        interaction.transform.SetParent(root.transform, false);
        interaction.transform.localPosition = new Vector3(0f, 0f, 1.2f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ExitDoorPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void WireBossPrefabs(GameObject keyPrefab)
    {
        WireBossPrefab(BossPrefabPath, keyPrefab);
        WireBossPrefab(Boss2PrefabPath, keyPrefab);
    }

    private static void WireBossPrefab(string path, GameObject keyPrefab)
    {
        GameObject boss = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (boss == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(boss);
        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
        BossKeyDrop drop = contents.GetComponent<BossKeyDrop>();
        if (drop == null)
            drop = contents.AddComponent<BossKeyDrop>();

        SerializedObject so = new SerializedObject(drop);
        so.FindProperty("_keyPickupPrefab").objectReferenceValue = keyPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void SetupSceneObjects(GameObject keyPrefab, GameObject doorPrefab)
    {
        LevelExitObjective objective = Object.FindAnyObjectByType<LevelExitObjective>();
        if (objective == null)
        {
            var go = new GameObject("LevelExitObjective");
            objective = go.AddComponent<LevelExitObjective>();
        }

        LevelExitPressure pressure = Object.FindAnyObjectByType<LevelExitPressure>();
        if (pressure == null)
        {
            var go = new GameObject("LevelExitPressure");
            pressure = go.AddComponent<LevelExitPressure>();
        }

        ExitDoor door = Object.FindAnyObjectByType<ExitDoor>();
        if (door == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab);
            instance.name = "ExitDoor";
            instance.transform.position = new Vector3(25f, 0f, 25f);
            door = instance.GetComponent<ExitDoor>();
        }

        SerializedObject doorSo = new SerializedObject(door);
        doorSo.FindProperty("_exitObjective").objectReferenceValue = objective;
        doorSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject pressureSo = new SerializedObject(pressure);
        pressureSo.FindProperty("_exitObjective").objectReferenceValue = objective;
        pressureSo.ApplyModifiedPropertiesWithoutUndo();

        _ = keyPrefab;
    }

    private static void WireGameplayHud()
    {
        string scenePath = SceneManager.GetActiveScene().path;
        GameObject hudInstance = GameObject.Find("GameplayHud V2") ?? GameObject.Find("GameplayHud");
        if (hudInstance != null && hudInstance.GetComponentInChildren<LevelExitHud>(true) == null)
            hudInstance.AddComponent<LevelExitHud>();

        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayHudV2Path);
        if (hudPrefab == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(hudPrefab);
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        if (contents.GetComponentInChildren<LevelExitHud>(true) == null)
            contents.AddComponent<LevelExitHud>();

        PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}

#endif
