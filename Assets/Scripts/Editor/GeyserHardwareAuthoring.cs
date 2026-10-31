#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Creates optional launch hardware without changing scenes or existing authored prefabs.</summary>
public static class GeyserHardwareAuthoring
{
    public const string Folder = GeyserModelAuthoring.Folder;

    [MenuItem("ScrapWaves/Level/Create Missing Optional Fan and Vent Jump Pads")]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author outside Play Mode.");

        Directory.CreateDirectory(Folder + "/Prefabs");
        Directory.CreateDirectory(Folder + "/Meshes");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        int created = 0;
        if (Create("FloorFan", "FanJumpPad", .535f, .62f, 1.05f)) created++;
        if (Create("FloorVent", "VentJumpPad", .455f, .54f, 1.2f)) created++;
        Debug.Log($"OPTIONAL_GEYSER_HARDWARE_AUTHORED: {created} new prefabs; existing prefabs and all scenes retained. Assign each placed pad's Landing to a receiving object with a Collider.");
    }

    private static bool Create(string modelName, string prefabName, float supportHeight, float mouthHeight, float openingRadius)
    {
        string prefabPath = Folder + "/Prefabs/" + prefabName + ".prefab";
        // Re-running authoring must not erase destination bindings, tuning or designer modifications.
        if (File.Exists(prefabPath))
            return false;

        GameObject model = ImportModel(modelName);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/HotAirGeyser_Visual.prefab");
        if (source == null) throw new InvalidOperationException("The existing hot-air presentation prefab is required.");

        var root = new GameObject(prefabName);
        try
        {
            // Keep a unit-scale gameplay root: only the approved geometry was reduced to 75%.
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, supportHeight + .55f, 0f);
            trigger.size = new Vector3(openingRadius * 2f, 1.2f, openingRadius * 2f);
            MapLaunchPad launcher = root.AddComponent<MapLaunchPad>();
            var launchSettings = new SerializedObject(launcher);
            launchSettings.FindProperty("_landing").objectReferenceValue = null;
            launchSettings.FindProperty("_apexClearance").floatValue = 12f;
            launchSettings.FindProperty("_launchFromTrigger").boolValue = true;
            launchSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject hardware = (GameObject)PrefabUtility.InstantiatePrefab(model);
            hardware.name = "Hardware";
            hardware.transform.SetParent(root.transform, false);
            foreach (Renderer renderer in hardware.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(MapMaterial).ToArray();

            var support = new GameObject("WalkableSurface", typeof(MeshCollider));
            support.transform.SetParent(root.transform, false);
            MeshCollider collision = support.GetComponent<MeshCollider>();
            collision.sharedMesh = SupportMesh(prefabName, supportHeight);
            collision.convex = false;
            collision.isTrigger = false;

            // Copy the established flow settings and shader references, then detach this new presentation.
            // Existing source meshes, materials and particle prefabs are never modified.
            GameObject presentation = (GameObject)PrefabUtility.InstantiatePrefab(source);
            presentation.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(presentation, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            presentation.name = "FlowPresentation";
            RemoveMoundGeometry(presentation.transform);
            GeyserVfx vfx = presentation.GetComponent<GeyserVfx>();
            if (vfx == null) throw new InvalidOperationException("Hot-air presentation has no GeyserVfx.");
            foreach (ParticleSystem particles in presentation.GetComponentsInChildren<ParticleSystem>(true))
                particles.transform.localPosition = new Vector3(0f, mouthHeight, 0f);
            Transform heat = presentation.transform.Find("UpdraftDirection/HeatShimmer");
            if (heat != null) heat.localPosition = new Vector3(0f, mouthHeight, 0f);
            var flowSettings = new SerializedObject(vfx);
            flowSettings.FindProperty("_openingRadius").floatValue = openingRadius;
            flowSettings.ApplyModifiedPropertiesWithoutUndo();
            vfx.BindLauncher(launcher);
            vfx.ApplyTuning();

            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                throw new InvalidOperationException("Could not save " + prefabPath);
            return true;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static GameObject ImportModel(string name)
    {
        string path = Folder + "/Models/" + name + ".fbx";
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Generate the scaled hardware model first: " + path);
        importer.importAnimation = false;
        importer.addCollider = false;
        importer.globalScale = 1f;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static Material MapMaterial(Material imported)
    {
        if (imported == null) throw new InvalidOperationException("Hardware contains an unassigned material.");
        string name = imported.name;
        if (name.StartsWith("Draft_", StringComparison.Ordinal)) name = "Geyser_" + name.Substring(6);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + name + ".mat");
        if (material == null) throw new InvalidOperationException("Unmapped hardware material: " + imported.name);
        return material;
    }

    private static void RemoveMoundGeometry(Transform presentation)
    {
        bool removedModel = false, removedCollision = false;
        for (int i = presentation.childCount - 1; i >= 0; i--)
        {
            Transform child = presentation.GetChild(i);
            if (child.name == "WalkableMound")
            {
                Object.DestroyImmediate(child.gameObject);
                removedCollision = true;
            }
            else if (child.GetComponentsInChildren<MeshFilter>(true).Any(filter =>
                AssetDatabase.GetAssetPath(filter.sharedMesh) == Folder + "/Models/HotAirGeyser.fbx"))
            {
                Object.DestroyImmediate(child.gameObject);
                removedModel = true;
            }
        }
        if (!removedModel || !removedCollision)
            throw new InvalidOperationException("Hot-air prefab structure changed; refusing to retain duplicate mound geometry.");
    }

    private static Mesh SupportMesh(string name, float height)
    {
        string path = Folder + "/Meshes/" + name + "Collision.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        // A closed 20-triangle envelope bridges decorative gaps and slopes inward from the low flange.
        // Its steepest approach is 36 degrees; the blades, guard and louvers never become snagging solids.
        var vertices = new Vector3[12];
        float[] halfWidths = { 1.8f, 1.8f, 1.2f };
        float[] heights = { 0f, .1f, height };
        for (int ring = 0; ring < 3; ring++)
        {
            float half = halfWidths[ring], y = heights[ring];
            vertices[ring * 4] = new Vector3(-half, y, -half);
            vertices[ring * 4 + 1] = new Vector3(half, y, -half);
            vertices[ring * 4 + 2] = new Vector3(half, y, half);
            vertices[ring * 4 + 3] = new Vector3(-half, y, half);
        }
        var triangles = new int[60];
        int index = 0;
        for (int ring = 0; ring < 2; ring++)
        {
            for (int side = 0; side < 4; side++)
            {
                int next = (side + 1) % 4;
                int a = ring * 4 + side, b = ring * 4 + next, c = (ring + 1) * 4 + side, d = (ring + 1) * 4 + next;
                triangles[index++] = a; triangles[index++] = c; triangles[index++] = d;
                triangles[index++] = a; triangles[index++] = d; triangles[index++] = b;
            }
        }
        foreach (int vertex in new[] { 0, 1, 2, 0, 2, 3, 8, 10, 9, 8, 11, 10 }) triangles[index++] = vertex;
        var mesh = new Mesh { name = name + "Collision", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
#endif
