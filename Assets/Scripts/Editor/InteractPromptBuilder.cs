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
        // Upgrade the authored prefab in place. Re-running this command must not discard
        // a designer's positions, materials, or the disabled-state presentation.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                EnsureStateVisuals(contents);
                return PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
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
            EnsureStateVisuals(root);

            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    public static void UpgradeStateVisuals(InteractPrompt prompt) => EnsureStateVisuals(prompt.gameObject);

    private static void EnsureStateVisuals(GameObject root)
    {
        var prompt = root.GetComponent<InteractPrompt>();
        var settings = new SerializedObject(prompt);
        if (settings.FindProperty("_availableVisual").objectReferenceValue != null
            && settings.FindProperty("_unavailableVisual").objectReferenceValue != null)
            return;

        Transform available = settings.FindProperty("_visual").objectReferenceValue as Transform;
        if (available == null) available = root.transform.GetChild(0);
        var common = new GameObject("State Visuals").transform;
        common.SetParent(root.transform, false);
        available.SetParent(common, false);
        available.name = "Available";
        var unavailable = Object.Instantiate(available.gameObject, common, false);
        unavailable.name = "Unavailable - Grey E and Cross";
        EnsureFolder(MaterialFolder);
        foreach (Renderer renderer in unavailable.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i];
                if (original == null) continue;
                string path = $"{MaterialFolder}/Unavailable_{original.name}.mat";
                Material grey = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (grey == null)
                {
                    Color color = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.color;
                    float luminance = color.grayscale;
                    float value = Mathf.Lerp(.12f, .56f, luminance);
                    grey = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Unavailable_" + original.name };
                    grey.SetColor("_BaseColor", new Color(value, value, value));
                    grey.SetFloat("_Metallic", .25f);
                    grey.SetFloat("_Smoothness", .2f);
                    AssetDatabase.CreateAsset(grey, path);
                }
                materials[i] = grey;
            }
            renderer.sharedMaterials = materials;
        }
        // The cross is authored geometry, not a runtime overlay. Keep the E readable
        // and use a small oxidized-metal X overlapping the lower-right corner.
        var cross = new GameObject("Unavailable Cross").transform;
        cross.SetParent(unavailable.transform, false);
        cross.SetParent(common, true);
        cross.localScale = Vector3.one;
        cross.localRotation = Quaternion.identity;
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (Renderer renderer in unavailable.GetComponentsInChildren<Renderer>(true))
        {
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }
        Vector3 size = common.InverseTransformVector(bounds.size);
        Vector3 center = common.InverseTransformPoint(bounds.center);
        cross.localPosition = center + new Vector3(size.x * .34f, -size.y * .32f, -size.z * .5f - .035f);
        Material crossMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Unavailable_Cross.mat");
        if (crossMaterial == null)
        {
            crossMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Unavailable_Cross" };
            crossMaterial.SetColor("_BaseColor", new Color(.65f, .28f, .16f));
            crossMaterial.SetFloat("_Metallic", .4f);
            crossMaterial.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(crossMaterial, $"{MaterialFolder}/Unavailable_Cross.mat");
        }
        for (int i = 0; i < 2; i++)
        {
            var stroke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stroke.name = i == 0 ? "Cross Slash" : "Cross Backslash";
            stroke.transform.SetParent(cross, false);
            stroke.transform.localScale = new Vector3(size.x * .42f, size.y * .085f, .04f);
            stroke.transform.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45f : -45f);
            Object.DestroyImmediate(stroke.GetComponent<Collider>());
            stroke.GetComponent<Renderer>().sharedMaterial = crossMaterial;
        }
        cross.SetParent(unavailable.transform, true);
        unavailable.SetActive(false);
        settings.FindProperty("_visual").objectReferenceValue = common;
        settings.FindProperty("_availableVisual").objectReferenceValue = available.gameObject;
        settings.FindProperty("_unavailableVisual").objectReferenceValue = unavailable;
        settings.ApplyModifiedPropertiesWithoutUndo();
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
