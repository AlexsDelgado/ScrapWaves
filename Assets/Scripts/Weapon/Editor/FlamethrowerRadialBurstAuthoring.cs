using UnityEditor;
using UnityEngine;

public static class FlamethrowerRadialBurstAuthoring
{
    [MenuItem("Tools/ScrapWaves/Game Feel/Refresh Flamethrower Q Presentation Only")]
    public static void RefreshActivePrefabs()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameFeel/Shaders/FlamethrowerRadialIgnition.shader");
        if (shader == null) throw new System.InvalidOperationException("Q ignition shader was not imported.");
        foreach (string style in new[] { "FlameActiveBurst", "JellifiedActiveBurst", "NitrogenActiveBurst" })
        {
            string path = "Assets/GameFeel/Prefabs/Weapons/Flamethrower/GF_Flamethrower_" + style + ".prefab";
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform oldVisual = contents.transform.Find("Animated Visual");
                if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);
                Light oldLight = contents.GetComponent<Light>();
                if (oldLight != null) Object.DestroyImmediate(oldLight);
                var serialized = new SerializedObject(contents.GetComponent<FlamethrowerCueVfx>());
                serialized.FindProperty("_activeBurstShader").objectReferenceValue = shader;
                serialized.FindProperty("_meshLayers").arraySize = 0;
                serialized.FindProperty("_particleLayers").arraySize = 0;
                serialized.FindProperty("_animatedRoots").arraySize = 0;
                serialized.FindProperty("_lightPulse").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        Debug.Log("Refreshed only the three flamethrower Q presentation prefabs.");
    }
}
