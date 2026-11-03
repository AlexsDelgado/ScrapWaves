#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring of saved spawner visuals. Never constructs geometry at runtime.</summary>
public static class ZoneSpawnerAuthoring
{
    private const string Art = "Assets/Art/Spawners";
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string PlatformPath = Art + "/FabricationPlatform.prefab";
    private static readonly string[] Types = { "Drone", "Slime", "Chaser" };
    private static readonly string[] Models = { "Assets/Arte/Avispa2.fbx", "Assets/Arte/Basurita (1).fbx", "Assets/Arte/Larguirucho.fbx" };

    [MenuItem("ScrapWaves/Level/Author Upper Island Spawners")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes before authoring spawners.");
        Scene scene = Preflight();
        EnsureFolder(Art);
        EnsureFolder(Art + "/Materials");
        EnsureFolder(Art + "/Meshes");
        Material steel = Lit("WeatheredSteel", new Color(.21f, .245f, .23f), .65f, .25f);
        Material dark = Lit("RecessedIron", new Color(.055f, .07f, .063f), .55f, .18f);
        Material rust = Lit("OxideEdges", new Color(.31f, .17f, .085f), .4f, .16f);
        Material caution = Lit("WornOchre", new Color(.53f, .43f, .16f), .3f, .2f);
        Material off = Lit("LampOff", new Color(.045f, .075f, .06f), .15f, .3f);
        Material on = Lit("LampOn", new Color(.26f, .79f, .53f), .05f, .3f);
        on.EnableKeyword("_EMISSION");
        on.SetColor("_EmissionColor", new Color(.25f, 1.15f, .62f));
        EditorUtility.SetDirty(on);
        Material hologram = LoadOrCreateMaterial("EnemyHologram", Shader.Find("ScrapWaves/Spawners/Hologram"));
        hologram.SetColor("_BaseColor", new Color(.16f, .65f, .46f, 1));
        hologram.SetColor("_RimColor", new Color(.48f, 1.12f, .78f, 1));
        EditorUtility.SetDirty(hologram);
        Mesh plateMesh = GetPlateMesh();
        AuthorPlatform(plateMesh, steel, dark, rust, caution, off);
        for (int i = 0; i < Types.Length; i++) AuthorSpawner(i, hologram, on, off);
        AssetDatabase.SaveAssets();

        // Prefab saves may refresh loaded instances; reacquire them after the preflighted asset writes.
        var zones = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ZoneSpawner>(true)).ToArray();
        foreach (ZoneSpawner zone in zones)
        {
            // Remove only the known legacy FBX additions. Never remove arbitrary designer children.
            foreach (Transform child in zone.transform.Cast<Transform>().ToArray())
            {
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (Models.Contains(source)) Object.DestroyImmediate(child.gameObject);
            }
            Transform hologramRoot = zone.transform.Find("Hologram");
            if (hologramRoot == null) throw new InvalidOperationException("Authored hologram missing in " + zone.name);
            Vector3 scale = zone.transform.lossyScale;
            if (Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < .001f)
                throw new InvalidOperationException("Cannot normalize a zero-scaled spawner visual.");
            // Keep the gameplay scale/trigger intact; cancel nonuniform scale above the rotating visual only.
            hologramRoot.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hologramRoot);
        }
        ValidateScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save spawner migration.");
        AssetDatabase.SaveAssets();
        Debug.Log("ZONE_SPAWNERS_AUTHORED: three linked prefabs, migrated scene previews, four availability lamps each; gameplay transforms/configuration preserved.");
    }

    private static Scene Preflight()
    {
        // Validate every input before creating folders or changing any saved visual asset.
        foreach (string shaderName in new[] { "Universal Render Pipeline/Lit", "ScrapWaves/Spawners/Hologram" })
            if (Shader.Find(shaderName) == null)
                throw new InvalidOperationException("Required spawner shader was not imported: " + shaderName);

        var expectedPrefabs = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Types.Length; i++)
        {
            string path = "Assets/Prefabs/ZoneSpawner_" + Types[i] + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<ZoneSpawner>() == null || prefab.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Required spawner prefab must have its controller and collider on the root: " + path);
            ValidateScale(prefab.transform);
            expectedPrefabs.Add(path);

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Models[i]);
            if (model == null || PrefabUtility.GetPrefabAssetType(model) != PrefabAssetType.Model ||
                model.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException("Required spawner preview must be an imported model with renderers: " + Models[i]);
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new InvalidOperationException("Missing gameplay scene: " + ScenePath);
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
        var zones = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ZoneSpawner>(true)).ToArray();
        if (zones.Length != Types.Length)
            throw new InvalidOperationException("Expected exactly three gameplay zone spawners; inspect changed scene before migrating.");
        foreach (ZoneSpawner zone in zones)
        {
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(zone.gameObject);
            if (PrefabUtility.GetNearestPrefabInstanceRoot(zone.gameObject) != zone.gameObject || !expectedPrefabs.Remove(source))
                throw new InvalidOperationException("Expected one linked instance of each Drone, Slime and Chaser spawner prefab; inspect " + zone.name + ".");
            if (zone.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Spawner root collider is missing: " + zone.name);
            ValidateScale(zone.transform);
        }
        return scene;
    }

    private static void ValidateScale(Transform root)
    {
        Vector3 scale = root.lossyScale;
        for (int axis = 0; axis < 3; axis++)
            if (float.IsNaN(scale[axis]) || float.IsInfinity(scale[axis]) || Mathf.Abs(scale[axis]) < .001f)
                throw new InvalidOperationException("Cannot normalize a zero or invalid spawner scale: " + root.name);
    }

    private static void AuthorSpawner(int index, Material hologram, Material on, Material off)
    {
        string path = "Assets/Prefabs/ZoneSpawner_" + Types[index] + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            DestroyChild(root.transform, "ZoneMarker");
            DestroyChild(root.transform, "FabricationPlatform");
            DestroyChild(root.transform, "Hologram");
            GameObject platform = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPath), root.transform);
            platform.name = "FabricationPlatform";
            Transform hologramRoot = Child(root.transform, "Hologram");
            Transform pivot = Child(hologramRoot, "UprightPreviewPivot");
            pivot.localPosition = new Vector3(0, 2.2f, 0);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Models[index]), pivot);
            model.name = Types[index] + "PreviewMesh";
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one;
            // The Chaser FBX is authored along a different import axis; leave its pivot upright.
            model.transform.localRotation = index == 2 ? Quaternion.Euler(0, 90, 90) : Quaternion.identity;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = Enumerable.Repeat(hologram, renderer.sharedMaterials.Length).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Bounds bounds = BoundsOf(model);
            float factor = Mathf.Min(9.2f / Mathf.Max(bounds.size.x, .01f), 8.5f / Mathf.Max(bounds.size.y, .01f), 9.2f / Mathf.Max(bounds.size.z, .01f));
            model.transform.localScale = Vector3.one * factor;
            bounds = BoundsOf(model);
            Vector3 delta = new Vector3(-bounds.center.x, pivot.position.y - bounds.min.y, -bounds.center.z);
            model.transform.position += delta;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            ZoneSpawnerPresentation view = root.GetComponent<ZoneSpawnerPresentation>();
            if (view == null) view = root.AddComponent<ZoneSpawnerPresentation>();
            var data = new SerializedObject(view);
            data.FindProperty("_spawner").objectReferenceValue = root.GetComponent<ZoneSpawner>();
            data.FindProperty("_hologramRoot").objectReferenceValue = hologramRoot.gameObject;
            data.FindProperty("_hologramPivot").objectReferenceValue = pivot;
            data.FindProperty("_lampOnMaterial").objectReferenceValue = on;
            data.FindProperty("_lampOffMaterial").objectReferenceValue = off;
            Renderer[] lamps = platform.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("LampLens_", StringComparison.Ordinal)).OrderBy(r => r.name).ToArray();
            if (lamps.Length != 4) throw new InvalidOperationException("Platform requires four lamps.");
            SerializedProperty array = data.FindProperty("_cornerLamps");
            array.arraySize = lamps.Length;
            for (int j = 0; j < lamps.Length; j++) array.GetArrayElementAtIndex(j).objectReferenceValue = lamps[j];
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("ZONE_PREVIEW_MESH: " + Types[index] + " normalized scale=" + factor + "; size=" + BoundsOf(model).size);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AuthorPlatform(Mesh mesh, Material steel, Material dark, Material rust, Material caution, Material off)
    {
        GameObject root = new("FabricationPlatform");
        try
        {
            Part(root.transform, "ChamferedSteelChassis", mesh, steel, new Vector3(0, .12f, 0), new Vector3(9.5f, .3f, 9.5f));
            Part(root.transform, "InsetProjectionBed", mesh, dark, new Vector3(0, .29f, 0), new Vector3(7.3f, .1f, 7.3f));
            // Unequal repair plates and oxide edges keep the base in the existing scrap-metal language.
            Box(root.transform, "RivetedRepairPlate", steel, new Vector3(-2.1f, .36f, -2.1f), new Vector3(2.2f, .1f, 2.35f), -7);
            Box(root.transform, "OxideRepairPlate", rust, new Vector3(2.35f, .36f, 1.75f), new Vector3(1.7f, .1f, 2.9f), 5);
            Box(root.transform, "FrontSteelRail", steel, new Vector3(0, .39f, -4.12f), new Vector3(6.3f, .25f, .42f));
            Box(root.transform, "BackOxideRail", rust, new Vector3(0, .38f, 4.12f), new Vector3(6.3f, .23f, .42f));
            for (int i = 0; i < 6; i++)
            {
                Box(root.transform, "FrontCautionStripe_" + i, caution, new Vector3(-1.9f + i * .77f, .53f, -4.12f), new Vector3(.34f, .025f, .44f), -22);
                Box(root.transform, "CoolingSlat_" + i, steel, new Vector3(-3.13f, .37f, -1.4f + i * .55f), new Vector3(.95f, .11f, .13f));
            }
            Part(root.transform, "ProjectorSocket", mesh, rust, new Vector3(0, .39f, 0), new Vector3(3.3f, .12f, 3.3f));
            Part(root.transform, "ProjectorLensBed", mesh, dark, new Vector3(0, .47f, 0), new Vector3(2.7f, .07f, 2.7f));
            for (int i = 0; i < 3; i++)
                Box(root.transform, "ProjectorConductor_" + i, steel, new Vector3(-.7f + i * .7f, .52f, 0), new Vector3(.12f, .03f, 1.9f));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    string corner = (x < 0 ? "L" : "R") + (z < 0 ? "Front" : "Back");
                    Vector3 pos = new(x * 3.7f, .49f, z * 3.7f);
                    Box(root.transform, "CornerHousing_" + corner, rust, pos, new Vector3(1.05f, .4f, 1.05f), 45);
                    Box(root.transform, "LampRim_" + corner, dark, pos + Vector3.up * .23f, new Vector3(.8f, .12f, .8f));
                    Box(root.transform, "LampLens_" + corner, off, pos + Vector3.up * .34f, new Vector3(.58f, .13f, .58f));
                    Box(root.transform, "LampGuard_" + corner, steel, pos + Vector3.up * .43f, new Vector3(.08f, .05f, .76f));
                    Box(root.transform, "CornerFastener_" + corner, dark, new Vector3(x * 4.28f, .3f, z * 2.9f), new Vector3(.22f, .12f, .22f), 30);
                }
            PrefabUtility.SaveAsPrefabAsset(root, PlatformPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static void ValidateScene(Scene scene)
    {
        foreach (ZoneSpawner zone in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ZoneSpawner>(true)))
        {
            if (zone.GetComponent<ZoneSpawnerPresentation>() == null || zone.transform.Find("ZoneMarker") != null)
                throw new InvalidOperationException("Incomplete visual migration: " + zone.name);
            foreach (Transform child in zone.transform)
                if (Models.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject)))
                    throw new InvalidOperationException("Legacy solid preview remains: " + child.name);
            Transform pivot = zone.transform.Find("Hologram/UprightPreviewPivot");
            Vector3 s = pivot.lossyScale;
            if (Mathf.Abs(s.x - s.y) > .01f || Mathf.Abs(s.y - s.z) > .01f)
                throw new InvalidOperationException("Rotating preview inherits nonuniform scale: " + zone.name);
        }
    }

    private static Mesh GetPlateMesh()
    {
        string path = Art + "/Meshes/ChamferedPlate.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        Vector2[] outline = { new(-.38f, -.5f), new(.38f, -.5f), new(.5f, -.38f), new(.5f, .38f), new(.38f, .5f), new(-.38f, .5f), new(-.5f, .38f), new(-.5f, -.38f) };
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        void Triangle(Vector3 a, Vector3 b, Vector3 c) { int first = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); indices.Add(first); indices.Add(first + 1); indices.Add(first + 2); }
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
            Vector3 at = new(a.x, .5f, a.y), bt = new(b.x, .5f, b.y), ab = new(a.x, -.5f, a.y), bb = new(b.x, -.5f, b.y);
            Triangle(Vector3.up * .5f, bt, at);
            Triangle(Vector3.down * .5f, ab, bb);
            Triangle(at, bt, ab);
            Triangle(bt, bb, ab);
        }
        Mesh mesh = new() { name = "ChamferedPlate" };
        mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Transform Child(Transform parent, string name) { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
    private static void DestroyChild(Transform parent, string name) { Transform child = parent.Find(name); if (child != null) Object.DestroyImmediate(child.gameObject); }
    private static Bounds BoundsOf(GameObject go) { Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true); if (renderers.Length == 0) throw new InvalidOperationException("Preview has no renderers."); Bounds b = renderers[0].bounds; foreach (Renderer r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b; }
    private static GameObject Box(Transform parent, string name, Material material, Vector3 position, Vector3 scale, float yaw = 0)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(box.GetComponent<Collider>());
        box.name = name; box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = scale; box.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }
    private static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        Transform t = Child(parent, name); t.localPosition = position; t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }
    private static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        Material mat = LoadOrCreateMaterial(name, Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color); mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", smoothness); EditorUtility.SetDirty(mat); return mat;
    }
    private static Material LoadOrCreateMaterial(string name, Shader shader)
    {
        if (shader == null) throw new InvalidOperationException("Required spawner shader was not imported.");
        string path = Art + "/Materials/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        return mat;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
