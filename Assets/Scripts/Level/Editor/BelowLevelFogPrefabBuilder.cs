#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Crea material + prefab de niebla de piso inferior (Cull Back + fade al cruzar).
/// </summary>
public static class BelowLevelFogPrefabBuilder
{
    private const string ShaderPath = "Assets/GameFeel/Shaders/BelowLevelFog.shader";
    private const string MaterialPath = "Assets/GameFeel/Materials/BelowLevelFog.mat";
    private const string PrefabFolder = "Assets/Prefabs/Level";
    private const string PrefabPath = PrefabFolder + "/BelowLevelFogPlane.prefab";

    [MenuItem("ScrapWaves/Level/Create Below Level Fog Prefab")]
    public static void Create()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"[BelowLevelFog] Missing shader at {ShaderPath}");
            return;
        }

        if (!Directory.Exists("Assets/GameFeel/Materials"))
            Directory.CreateDirectory("Assets/GameFeel/Materials");
        if (!Directory.Exists(PrefabFolder))
            Directory.CreateDirectory(PrefabFolder);

        Color fogColor = new Color(0.45f, 0.52f, 0.6f, 0.92f);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "BelowLevelFog" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", fogColor);
        if (material.HasProperty("_NoiseStrength"))
            material.SetFloat("_NoiseStrength", 0.2f);
        if (material.HasProperty("_EdgeFade"))
            material.SetFloat("_EdgeFade", 0.15f);
        if (material.HasProperty("_Visibility"))
            material.SetFloat("_Visibility", 1f);
        EditorUtility.SetDirty(material);

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Quad);
        root.name = "BelowLevelFogPlane";
        Object.DestroyImmediate(root.GetComponent<MeshCollider>());

        Transform t = root.transform;
        t.localRotation = Quaternion.Euler(90f, 0f, 0f);
        t.localScale = new Vector3(300f, 300f, 1f);

        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Add by name so Editor assembly still compiles if runtime script is mid-import.
        var fadeType = System.Type.GetType("BelowLevelFogFade, Assembly-CSharp");
        if (fadeType != null && root.GetComponent(fadeType) == null)
            root.AddComponent(fadeType);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
            Selection.activeObject = prefab;

        Debug.Log($"[BelowLevelFog] Prefab listo en {PrefabPath}. Colocalo en el límite entre pisos (normal hacia arriba).", prefab);
    }
}
#endif
