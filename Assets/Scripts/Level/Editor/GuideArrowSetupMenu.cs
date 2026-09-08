#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Instancia <c>Arrow_Guide</c> en la escena abierta con <see cref="GuideArrow"/> +
/// <see cref="GuideArrowController"/>. Las referencias se resuelven solas en Awake.
/// </summary>
public static class GuideArrowSetupMenu
{
    private const string PrefabPath = "Assets/Prefabs/UI/Arrow_Guide.prefab";

    [MenuItem("ScrapWaves/Level/Create Guide Arrow In Scene")]
    public static void CreateGuideArrowInScene()
    {
        GuideArrow existing = Object.FindAnyObjectByType<GuideArrow>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("Ya hay una GuideArrow en la escena.", existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"No se encontró el prefab en {PrefabPath}");
            return;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Arrow_Guide";
        // Keep root active so GuideArrowController.Update runs; GuideArrow.Hide() toggles renderers.

        if (go.GetComponent<GuideArrow>() == null)
            go.AddComponent<GuideArrow>();
        if (go.GetComponent<GuideArrowController>() == null)
            go.AddComponent<GuideArrowController>();

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Guide Arrow");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Creada Arrow_Guide con GuideArrow + GuideArrowController. Recordá guardar la escena.", go);
    }
}
#endif
