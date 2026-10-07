#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authors only the two approved launch roots; receiving pads and unrelated scene content stay authored.</summary>
public static class GeyserModelAuthoring
{
    public const string Folder = "Assets/Art/Environment/Geysers";
    public const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    [MenuItem("ScrapWaves/Level/Author Approved Geysers")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author outside Play Mode.");
        Directory.CreateDirectory(Folder + "/Materials"); Directory.CreateDirectory(Folder + "/Prefabs"); Directory.CreateDirectory(Folder + "/Meshes");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader wisp = Shader.Find("ScrapWaves/Level/Geyser Wisp");
        Shader heat = Shader.Find("ScrapWaves/Level/Geyser Heat Shimmer");
        Shader debrisShader = Shader.Find("ScrapWaves/Level/Geyser Debris");
        if (lit == null || wisp == null || heat == null || debrisShader == null) throw new InvalidOperationException("Required geyser shaders unavailable.");
        ConfigureVisualMaterials(lit);
        Material plume = Material("Geyser_Updraft",wisp,Color.white);
        Material shimmer = Material("Geyser_HeatShimmer",heat,Color.white);
        Material debrisMaterial = Material("Geyser_LightDebris",debrisShader,new Color(.76f,.72f,.56f,1f));
        Mesh debrisMesh = DebrisMesh();
        Mesh volumeMesh = VolumeMesh();
        foreach (string name in new[] { "TrashGeyser", "HotAirGeyser" })
        {
            GeyserVfx.Kind kind = name == "TrashGeyser" ? GeyserVfx.Kind.Trash : GeyserVfx.Kind.HotAir;
            GameObject model = ImportModel(name,false);
            GameObject collision = ImportModel(name + "Collision",true);
            GameObject root = new(name + "_Visual");
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform,false);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + m.name + ".mat")).ToArray();
                GameObject colliderObject = (GameObject)PrefabUtility.InstantiatePrefab(collision);
                colliderObject.name = "WalkableMound"; colliderObject.transform.SetParent(root.transform,false);
                // Keep the full FBX transform hierarchy, matching the visual import basis exactly.
                foreach (MeshFilter filter in colliderObject.GetComponentsInChildren<MeshFilter>())
                {
                    MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh; collider.convex = false;
                }
                foreach (Renderer renderer in colliderObject.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                var flow = new GameObject("UpdraftDirection"); flow.transform.SetParent(root.transform,false);
                ParticleSystem updraft = Particles("ContinuousUpdraft",flow.transform,plume,true,48);
                updraft.transform.localPosition = new Vector3(0,1.05f,0);
                updraft.transform.localRotation = Quaternion.LookRotation(new Vector3(0,1,.16f));
                var upMain = updraft.main; upMain.prewarm = true;
                var growth = updraft.sizeOverLifetime; growth.enabled = true;
                growth.size = new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,.55f),new Keyframe(.45f,1f),new Keyframe(1,1.6f)));
                ParticleSystem debris = null;
                if (kind == GeyserVfx.Kind.Trash)
                {
                    debris = Particles("LightTumblingDebris",flow.transform,debrisMaterial,true,20);
                    debris.transform.localPosition = new Vector3(0,1.05f,0);
                    debris.transform.localRotation = Quaternion.LookRotation(new Vector3(0,1,.12f));
                    var main = debris.main; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2f,3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f,2.8f); main.startSize = new ParticleSystem.MinMaxCurve(.13f,.25f);
                    main.startColor = Color.white; main.startRotation3D = true;
                    main.startRotationX = new ParticleSystem.MinMaxCurve(-3.14f,3.14f);
                    main.startRotationY = new ParticleSystem.MinMaxCurve(-3.14f,3.14f);
                    main.startRotationZ = new ParticleSystem.MinMaxCurve(-3.14f,3.14f);
                    var rotation = debris.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true;
                    rotation.x = new ParticleSystem.MinMaxCurve(-2.5f,2.5f); rotation.y = new ParticleSystem.MinMaxCurve(-3f,3f); rotation.z = new ParticleSystem.MinMaxCurve(-2f,2f);
                    var renderer = debris.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = debrisMesh; renderer.enableGPUInstancing = false;
                    var shape = debris.shape; shape.radius = 1.1f;
                }
                ParticleSystem burst = Particles("EntryBurst",flow.transform,plume,false,40);
                burst.transform.localPosition = new Vector3(0,1.1f,0);
                burst.transform.localRotation = Quaternion.LookRotation(new Vector3(0,1,.20f));
                var burstMain = burst.main; burstMain.startLifetime = new ParticleSystem.MinMaxCurve(.8f,1.2f);
                burstMain.startSpeed = new ParticleSystem.MinMaxCurve(3f,5f); burstMain.startSize = new ParticleSystem.MinMaxCurve(.9f,1.5f);
                var burstShape = burst.shape; burstShape.angle = 20f; burstShape.radius = 1.15f;
                var heatObject = new GameObject("HeatShimmer",typeof(MeshFilter),typeof(MeshRenderer));
                heatObject.transform.SetParent(flow.transform,false); heatObject.transform.localPosition = new Vector3(0,1.05f,0);
                heatObject.transform.localScale = new Vector3(1.8f,6f,1.8f);
                heatObject.GetComponent<MeshFilter>().sharedMesh = volumeMesh;
                MeshRenderer heatRenderer = heatObject.GetComponent<MeshRenderer>(); heatRenderer.sharedMaterial = shimmer;
                heatRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; heatRenderer.receiveShadows = false;
                GeyserVfx presentation = root.AddComponent<GeyserVfx>();
                presentation.ConfigureAuthoring(kind,updraft,debris,burst,heatRenderer);
                PrefabUtility.SaveAsPrefabAsset(root,Folder + "/Prefabs/" + name + "_Visual.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (GameObject root in scene.GetRootGameObjects().Where(g => g.name == "Jump floor" || g.name == "Jump sky"))
        {
            var launcher = root.GetComponent<MapLaunchPad>();
            var serialized = new SerializedObject(launcher);
            Transform landing = (Transform)serialized.FindProperty("_landing").objectReferenceValue;
            if (landing == null) throw new InvalidOperationException(root.name + " has no authored destination");
            serialized.FindProperty("_launchFromTrigger").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            BoxCollider trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true; trigger.size = new Vector3(.29f,1.3f,.29f); trigger.center = new Vector3(0,.35f,0);
            root.GetComponent<MeshRenderer>().enabled = false;
            // Re-authoring replaces our visual rather than stacking colliders and VFX.
            for (int child = root.transform.childCount - 1; child >= 0; child--)
            {
                Transform old = root.transform.GetChild(child);
                if (old.name == "PadVisual" || old.name == "GeyserVisual") Object.DestroyImmediate(old.gameObject);
            }
            string name = root.name == "Jump floor" ? "TrashGeyser" : "HotAirGeyser";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/" + name + "_Visual.prefab");
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
            visual.name = "GeyserVisual"; visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity;
            Vector3 scale = root.transform.localScale; visual.transform.localScale = new Vector3(1/scale.x,1/scale.y,1/scale.z);
            Vector3 direction = landing.position-root.transform.position; direction.y = 0;
            Transform flow = visual.transform.Find("UpdraftDirection"); flow.rotation = Quaternion.LookRotation(direction.normalized,Vector3.up);
            visual.GetComponent<GeyserVfx>().BindLauncher(launcher);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.GetComponent<GeyserVfx>());
            EditorUtility.SetDirty(launcher); EditorUtility.SetDirty(trigger);
        }
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("GEYSERS_AUTHORED: roots, destinations and 12m apex retained; mound collision and immediate opening triggers installed.");
    }

    [MenuItem("ScrapWaves/Level/Refresh Geyser Visuals Only")]
    public static void RefreshVisuals()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refresh outside Play Mode.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureVisualMaterials(Shader.Find("Universal Render Pipeline/Lit"));
        foreach (string name in new[] { "TrashGeyser", "HotAirGeyser" })
        {
            string modelPath = Folder + "/Models/" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Material[] imported = model.GetComponentsInChildren<MeshRenderer>().Single().sharedMaterials;
            string prefabPath = Folder + "/Prefabs/" + name + "_Visual.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                MeshRenderer visual = root.GetComponentsInChildren<MeshRenderer>().Single(r =>
                    r.enabled && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh) == modelPath);
                visual.sharedMaterials = imported.Select(m => AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + m.name + ".mat")).ToArray();
                if (visual.sharedMaterials.Any(m => m == null)) throw new InvalidOperationException("Unmapped visual material: " + name);
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("GEYSER_VISUALS_REFRESHED: bent scrap geometry and materials only; no scene, collider or VFX regeneration.");
    }

    [MenuItem("ScrapWaves/Level/Refresh Trash Geyser Debris Only")]
    public static void RefreshTrashDebris()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refresh outside Play Mode.");
        string path = Folder + "/Prefabs/TrashGeyser_Visual.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var presentation = root.GetComponent<GeyserVfx>();
            if (presentation == null || presentation.GeyserKind != GeyserVfx.Kind.Trash)
                throw new InvalidOperationException("Expected the existing trash geyser presentation.");
            presentation.ApplyTuning();
            EditorUtility.SetDirty(presentation);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("TRASH_DEBRIS_REFRESHED: .27-.45m scraps, three-axis tumble, original 4/s and 20-particle cap; no scene or hot-air edits.");
    }

    private static void ConfigureVisualMaterials(Shader lit)
    {
        if (lit == null) throw new InvalidOperationException("URP Lit unavailable.");
        var palette = new (string name, Color color, float metal, float smooth)[]
        {
            ("DarkCompactedScrap",new(.22f,.235f,.24f),.25f,.12f),
            ("RustScrap",new(.40f,.245f,.16f),.15f,.10f),
            ("PaleScrap",new(.66f,.65f,.57f),.10f,.12f),
            ("DullScrap",new(.37f,.405f,.42f),.45f,.15f),
            ("HeatDarkenedMetal",new(.225f,.235f,.25f),.35f,.12f),
            ("BareSteel",new(.62f,.66f,.67f),.50f,.18f),
            ("DeepOpening",new(.025f,.025f,.019f),0f,.08f)
        };
        foreach (var entry in palette)
        {
            Material material = Material("Geyser_" + entry.name,lit,entry.color);
            material.SetColor("_BaseColor",entry.color); material.SetFloat("_Metallic",entry.metal); material.SetFloat("_Smoothness",entry.smooth);
            EditorUtility.SetDirty(material);
        }
    }

    private static GameObject ImportModel(string name, bool readable)
    {
        string path = Folder + "/Models/" + name + ".fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new InvalidOperationException("Generate Blender model first: " + path);
        importer.importAnimation = false; importer.addCollider = false; importer.globalScale = 1f; importer.isReadable = readable;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
    private static Material Material(string name, Shader shader, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = name }; material.SetColor("_BaseColor",color);
        if (shader.name == "Universal Render Pipeline/Lit") { material.SetFloat("_Metallic",0f); material.SetFloat("_Smoothness",.08f); }
        AssetDatabase.CreateAsset(material,path); return material;
    }
    private static ParticleSystem Particles(string name, Transform parent, Material material, bool loop, int budget)
    {
        var go = new GameObject(name,typeof(ParticleSystem)); go.transform.SetParent(parent,false);
        ParticleSystem particles = go.GetComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.useAutoRandomSeed = false; particles.randomSeed = name == "EntryBurst" ? 1753u : name == "LightTumblingDebris" ? 1752u : 1751u;
        var main = particles.main; main.loop = loop; main.playOnAwake = loop; main.maxParticles = budget;
        main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = 3f; main.startSpeed = 2.2f; main.startSize = 1f;
        var emission = particles.emission; emission.rateOverTime = loop ? 12f : 0f;
        var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 9f; shape.radius = 1.25f;
        var colors = particles.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },
            new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.12f),new GradientAlphaKey(.65f,.65f),new GradientAlphaKey(0,1) });
        colors.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.maxParticleSize = .5f;
        return particles;
    }
    private static Mesh DebrisMesh()
    {
        string path = Folder + "/Meshes/LightScrap.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (mesh != null) return mesh;
        mesh = new Mesh { name = "LightScrap" };
        mesh.vertices = new[] { new Vector3(-.5f,0,-.3f),new Vector3(.5f,.1f,-.3f),new Vector3(.35f,0,.3f),new Vector3(-.45f,.18f,.3f) };
        mesh.triangles = new[] { 0,1,2,0,2,3,2,1,0,3,2,0 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,path); return mesh;
    }
    private static Mesh VolumeMesh()
    {
        string path = Folder + "/Meshes/UpdraftVolume.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (mesh != null) return mesh;
        const int Segments = 20, Rings = 5;
        var vertices = new Vector3[(Segments+1)*Rings]; var uv = new Vector2[vertices.Length]; var triangles = new int[Segments*(Rings-1)*6];
        for (int ring = 0; ring < Rings; ring++) for (int i = 0; i <= Segments; i++)
        {
            float t = ring/(float)(Rings-1), angle = i*2*Mathf.PI/Segments, radius = Mathf.Lerp(.7f,1.25f,t);
            int index = ring*(Segments+1)+i; vertices[index] = new Vector3(Mathf.Cos(angle)*radius,t,Mathf.Sin(angle)*radius+t*.5f); uv[index] = new Vector2(i/(float)Segments,t);
        }
        int k = 0;
        for (int ring = 0; ring < Rings-1; ring++) for (int i = 0; i < Segments; i++)
        {
            int a = ring*(Segments+1)+i, b = a+1, c = a+Segments+2, d = a+Segments+1;
            triangles[k++]=a;triangles[k++]=d;triangles[k++]=c;triangles[k++]=a;triangles[k++]=c;triangles[k++]=b;
        }
        mesh = new Mesh { name = "UpdraftVolume",vertices = vertices,uv = uv,triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path); return mesh;
    }
}
#endif
