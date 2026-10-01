#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds readiness views to existing scene-owned reticles without rebuilding other UI.</summary>
public static class ManualCycleReticleAuthoring
{
    [MenuItem("ScrapWaves/UI/Author Manual Switch Readiness In Current Scene")]
    public static void AuthorCurrentScene() => Author(SceneManager.GetActiveScene());

    public static void AuthorPlayerScenes()
    {
        foreach(string path in new[]{"Assets/Scenes/GameplayScene.unity","Assets/Scenes/SampleScene.unity",
            "Assets/Scenes/Testing/enemiesTesting.unity","Assets/Scenes/Testing/test_balance.unity"})
        {
            Scene scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try{Author(scene);EditorSceneManager.SaveScene(scene);}
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
    }
    private static void Author(Scene scene)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Author reticles outside Play Mode.");
        foreach(var hud in scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<ReticleHud>(true)))
        {
            if(!hud.HasAuthoredUi)continue;
            hud.AuthorManualCycleUi();PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif
