#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Crea el objeto de la flecha guía en la escena abierta (mismo patrón que EconomySceneSetupMenu /
/// DestroyerPrefabMenu). GuideArrowController resuelve sus referencias solas por FindAnyObjectByType
/// en Awake, así que no hace falta cablear nada a mano en el Inspector.
/// </summary>
public static class GuideArrowSetupMenu
{
    [MenuItem("ScrapWaves/Level/Create Guide Arrow In Scene")]
    public static void CreateGuideArrowInScene()
    {
        GuideArrow existing = Object.FindAnyObjectByType<GuideArrow>();
        if (existing != null)
        {
            Debug.Log("Ya hay una GuideArrow en la escena.", existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var go = new GameObject("GuideArrow");
        go.AddComponent<GuideArrow>();
        go.AddComponent<GuideArrowController>();

        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Guide Arrow");
        Debug.Log("Creada GuideArrow en la escena. Recordá guardarla.", go);
    }
}
#endif
