#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayHudPrefabBuilder
{
    const string PrefabPath = "Assets/Prefabs/UI/GameplayHud.prefab";
    const string PlayerBarsPrefabPath = "Assets/Prefabs/UI/PlayerBarsHUD.prefab";

    [MenuItem("ScrapWaves/UI/Build GameplayHud Prefab")]
    public static void BuildPrefab()
    {
        EnsureDirectory("Assets/Prefabs/UI");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            RebuildBottomStripInPrefab(forceFullRebuild: true);
            return;
        }

        CreateFullPrefabFromScratch();
    }

    [MenuItem("ScrapWaves/UI/Rebuild BottomStrip In Prefab")]
    public static void RebuildBottomStripMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogWarning("No existe GameplayHud.prefab. Usá Build GameplayHud Prefab primero.");
            return;
        }

        RebuildBottomStripInPrefab(forceFullRebuild: false);
    }

    static void CreateFullPrefabFromScratch()
    {
        var root = new GameObject("GameplayHud");
        root.AddComponent<GameplayHudRoot>();
        Transform playerBarsContent = ExtractPlayerBarsRootForEmbed();
        GameplayHudHierarchyBuilder.Build(root.transform, playerBarsContent);

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"GameplayHud prefab creado en {PrefabPath} (BottomStrip completo, editable en editor).", prefabAsset);
    }

    /// <summary>
    /// Regenera Passives + WeaponPanel dentro del prefab existente sin dar Play.
    /// </summary>
    static void RebuildBottomStripInPrefab(bool forceFullRebuild)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (forceFullRebuild)
            {
                Transform canvas = contents.transform.Find("GameplayHudCanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas.gameObject);

                Transform playerBarsContent = ExtractPlayerBarsRootForEmbed();
                GameplayHudHierarchyBuilder.Build(contents.transform, playerBarsContent);
            }
            else
            {
                Transform bottomStrip = FindBottomStrip(contents.transform);
                if (bottomStrip == null)
                {
                    Transform playerBarsContent = ExtractPlayerBarsRootForEmbed();
                    GameplayHudHierarchyBuilder.Build(contents.transform, playerBarsContent);
                }
                else
                {
                    Transform center = bottomStrip.Find(GameplayHudHierarchyBuilder.ColumnCenterName);
                    Transform right = bottomStrip.Find(GameplayHudHierarchyBuilder.ColumnRightName);
                    HudBottomStripLayouts.BuildPassivesColumn(center);
                    HudBottomStripLayouts.BuildWeaponColumn(right);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            Debug.Log(forceFullRebuild
                ? $"GameplayHud prefab regenerado en {PrefabPath}."
                : $"BottomStrip actualizado en {PrefabPath} (Passives + Weapons visibles en editor).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static Transform FindBottomStrip(Transform root)
    {
        Transform canvas = root.Find("GameplayHudCanvas");
        return canvas != null ? canvas.Find(GameplayHudHierarchyBuilder.BottomStripName) : root.Find(GameplayHudHierarchyBuilder.BottomStripName);
    }

    static Transform ExtractPlayerBarsRootForEmbed()
    {
        GameObject prefabContents = PrefabUtility.LoadPrefabContents(PlayerBarsPrefabPath);
        if (prefabContents == null)
            return null;

        try
        {
            Transform source = prefabContents.transform.Find("PlayerBarsRoot");
            if (source == null)
            {
                Debug.LogWarning($"[{nameof(GameplayHudPrefabBuilder)}] {PlayerBarsPrefabPath} no contiene PlayerBarsRoot.");
                return null;
            }

            GameObject embedded = Object.Instantiate(source.gameObject);
            embedded.name = "PlayerBarsRoot";
            return embedded.transform;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
    }

    [MenuItem("ScrapWaves/UI/Wire SampleScene GameplayHud")]
    public static void WireSampleScene()
    {
        RebuildBottomStripInPrefab(forceFullRebuild: true);

        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("No se encontró GameplayHud.prefab.");
            return;
        }

        GameObject existing = GameObject.Find("GameplayHud");
        if (existing != null)
            Object.DestroyImmediate(existing);

        PlayerBarsHud[] oldBars = Object.FindObjectsByType<PlayerBarsHud>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < oldBars.Length; i++)
        {
            if (oldBars[i] != null && oldBars[i].transform.root.name.Contains("PlayerBars"))
                Object.DestroyImmediate(oldBars[i].gameObject);
        }

        SurvivorHud survivor = Object.FindAnyObjectByType<SurvivorHud>(FindObjectsInactive.Include);
        if (survivor != null)
            survivor.gameObject.SetActive(false);

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance != null)
        {
            instance.name = "GameplayHud";
            SceneManager.MoveGameObjectToScene(instance, scene);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("SampleScene actualizada con GameplayHud prefab.");
    }

    static void EnsureDirectory(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
#endif
