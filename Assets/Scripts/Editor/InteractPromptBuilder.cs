#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arma el prefab de la tecla "E" flotante (InteractPrompt) desde Assets/Art/Props/InteractKey/InteractKey_E.fbx
/// (ArtSource/UI/V2B/scripts/interact_key.py) y la ubica sobre cada crafting station de las escenas abiertas.
/// Los materiales del FBX se reemplazan por materiales URP con el mismo color; la letra es emisiva.
/// </summary>
public static class InteractPromptBuilder
{
    private const string ModelPath = "Assets/Art/Props/InteractKey/InteractKey_E.fbx";
    private const string MaterialFolder = "Assets/Art/Props/InteractKey/Materials";
    private const string PrefabPath = "Assets/Prefabs/Props/InteractPrompt_E.prefab";
    private const string LetterName = "Letra_E";
    /// <summary>El modelo se hace a escala del HUD (~0.24 m); en el mundo queda de ~0.8 m.</summary>
    private const float WorldScale = 3.4f;
    private const float HeightAboveStation = 0.9f;

    [MenuItem("ScrapWaves/UI/Build Interact Prompt (E) And Place On Crafting Stations")]
    public static void BuildAndPlace()
    {
        GameObject prefab = BuildPrefab();
        int placed = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CraftingStation station in root.GetComponentsInChildren<CraftingStation>(true))
                    if (Place(station, prefab))
                    {
                        placed++;
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
        }
        Debug.Log($"[{nameof(InteractPromptBuilder)}] Prefab {PrefabPath}; colocado en {placed} crafting station(s).");
    }

    public static GameObject BuildPrefab()
    {
        AssetDatabase.Refresh();
        RemapMaterials();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new FileNotFoundException($"Falta {ModelPath}. Corré interact_key.py en Blender.");

        var root = new GameObject("InteractPrompt_E");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = FrontToMinusZ(visual);
            visual.transform.localScale = Vector3.one * WorldScale;
            CenterOnRoot(visual.transform);
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            root.AddComponent<InteractPrompt>();

            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static bool Place(CraftingStation station, GameObject prefab)
    {
        InteractPrompt existing = station.GetComponentInChildren<InteractPrompt>(true);
        if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) != null)
            return false;
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
        Bounds bounds = new Bounds(station.transform.position, Vector3.zero);
        foreach (Renderer r in station.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(r.bounds);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, station.gameObject.scene);
        instance.transform.SetParent(station.transform, true);
        instance.transform.position = new Vector3(bounds.center.x, bounds.max.y + HeightAboveStation, bounds.center.z);
        instance.transform.rotation = Quaternion.identity;
        Undo.RegisterCreatedObjectUndo(instance, "Place Interact Prompt");
        return true;
    }

    /// <summary>La letra está del lado del frente: rota el modelo para que ese lado quede hacia -Z.</summary>
    private static Quaternion FrontToMinusZ(GameObject visual)
    {
        Transform letter = FindDeep(visual.transform, LetterName);
        Renderer letterRenderer = letter != null ? letter.GetComponent<Renderer>() : null;
        if (letterRenderer == null)
            return Quaternion.identity;
        Bounds all = letterRenderer.bounds;
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
            all.Encapsulate(r.bounds);
        Vector3 offset = visual.transform.InverseTransformVector(letterRenderer.bounds.center - all.center);
        Vector3 front = Snap(offset);
        // Gira solo alrededor del "arriba" del modelo (+Y): FromToRotation puede dar vuelta la letra.
        return Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(front, Vector3.up));
    }

    private static void CenterOnRoot(Transform visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);
        visual.position -= b.center - visual.parent.position;
    }

    private static Vector3 Snap(Vector3 v)
    {
        Vector3 a = new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
        if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
        return new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    private static void RemapMaterials()
    {
        if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            return;
        EnsureFolder(MaterialFolder);
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.importAnimation = false;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is not Material embedded)
                continue;
            string path = $"{MaterialFolder}/IK_{embedded.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit) { name = $"IK_{embedded.name}" };
                Color color = embedded.HasProperty("_BaseColor") ? embedded.GetColor("_BaseColor") : embedded.color;
                string n = embedded.name.ToLowerInvariant();
                bool metal = n.Contains("chapa") || n.Contains("filo") || n.Contains("metal") || n.Contains("cobre");
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", metal ? 0.85f : 0.15f);
                mat.SetFloat("_Smoothness", metal ? 0.6f : 0.3f);
                if (n.Contains("mostaza"))
                {
                    // La letra brilla un poco para leerse en la penumbra del depósito.
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 1.4f);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                AssetDatabase.CreateAsset(mat, path);
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded), mat);
        }
        importer.SaveAndReimport();
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void EnsureFolder(string folder)
    {
        string current = "Assets";
        foreach (string part in folder.Split('/')[1..])
        {
            string next = $"{current}/{part}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }
}
#endif
