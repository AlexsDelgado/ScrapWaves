using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors the imported escape prefab and adds it without rebuilding the arena.</summary>
public static class CompactorEscapeSetupEditor
{
    public const string ModelPath = "Assets/Art/Level/CompactorDoor/Compactor_Open.fbx";
    public const string PrefabPath = "Assets/Prefabs/Level/Compactor/CompactorExitDoor.prefab";
    private const string MaterialFolder = "Assets/Art/Level/CompactorDoor/Materials";
    private const string SuctionPath = MaterialFolder + "/CompactorSuction.mat";
    private const string SandboxPath = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";

    [MenuItem("Tools/ScrapWaves/Open Compactor Escape Test")]
    public static void OpenForTesting()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene(SandboxPath);
        var door = Object.FindAnyObjectByType<CompactorDoorPresentation>();
        if (door == null) return;
        Selection.activeGameObject = door.gameObject;
        SceneView.lastActiveSceneView?.LookAt(door.transform.position + Vector3.up * 2.6f,
            Quaternion.Euler(8f, 20f, 0f), 8f);
    }

    [MenuItem("Tools/ScrapWaves/Setup Compactor Escape Test")]
    public static void Setup()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        ImportModel();
        CreatePrefab();
        foreach (string path in new[] { SandboxPath, "Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity" })
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            AddToScene(scene);
            EditorSceneManager.SaveScene(scene);
        }
        EditorSceneManager.OpenScene(SandboxPath);
        AssetDatabase.SaveAssets();
        Debug.Log("COMPACTOR_SETUP_COMPLETE");
    }

    private static void ImportModel()
    {
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.preserveHierarchy = true;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.motionNodeName = "Compactor_Root";
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;
        importer.SaveAndReimport();
        var clips = importer.defaultClipAnimations;
        if (clips.Length != 1) throw new InvalidOperationException("Compactor must contain exactly one opening take.");
        clips[0].name = "Open";
        clips[0].loopTime = false;
        clips[0].loopPose = false;
        clips[0].wrapMode = WrapMode.ClampForever;
        clips[0].lockRootPositionXZ = true;
        clips[0].lockRootHeightY = true;
        clips[0].lockRootRotation = true;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        // Store portable URP material assets and explicitly remap each FBX slot.
        foreach (Material source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            string path = MaterialFolder + "/" + source.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetTexture("_BaseMap", null);
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name), material);
        }
        importer.SaveAndReimport();
    }

    public static AnimationClip OpeningClip => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
        .OfType<AnimationClip>().Single(c => c.name == "Open");

    private static GameObject CreatePrefab()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        var root = new GameObject("CompactorExitDoor");
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.transform.SetParent(root.transform, false);
        // The FBX faces -Z; expose +Z as this gameplay prefab's front.
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;
        var door = root.AddComponent<ExitDoor>();
        var interaction = new GameObject("InteractionPoint").transform;
        interaction.SetParent(root.transform, false);
        interaction.localPosition = new Vector3(0f, 0f, 1.4f);
        var doorSo = new SerializedObject(door);
        doorSo.FindProperty("_interactionPoint").objectReferenceValue = interaction;
        doorSo.ApplyModifiedPropertiesWithoutUndo();

        BoxCollider blocker = CreateBox(root.transform, "Closed Door Blocker", new Vector3(0f, 1.57f, 0.36f), new Vector3(2.54f, 2.46f, 0.24f));
        CreateBox(root.transform, "Left Frame Collision", new Vector3(-1.55f, 2.66f, 0f), new Vector3(.58f, 5.32f, .8f));
        CreateBox(root.transform, "Right Frame Collision", new Vector3(1.55f, 2.66f, 0f), new Vector3(.58f, 5.32f, .8f));
        CreateBox(root.transform, "Chamber Back Collision", new Vector3(0f, 1.58f, -1.02f), new Vector3(2.95f, 3.05f, .16f));
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Black Suction Opening";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = new Vector3(0f, 1.62f, 0.18f);
        quad.transform.localScale = new Vector3(2.55f, 2.75f, 1f);
        var suction = quad.GetComponent<MeshRenderer>();
        suction.shadowCastingMode = ShadowCastingMode.Off;
        suction.receiveShadows = false;
        Shader shader = Shader.Find("ScrapWaves/Level/Compactor Suction");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Suction shader must compile.");
        Material suctionMat = AssetDatabase.LoadAssetAtPath<Material>(SuctionPath);
        if (suctionMat == null)
        {
            suctionMat = new Material(shader) { name = "CompactorSuction" };
            AssetDatabase.CreateAsset(suctionMat, SuctionPath);
        }
        suctionMat.SetFloat("_Activity", 0f);
        suction.sharedMaterial = suctionMat;
        root.AddComponent<CompactorDoorPresentation>().Configure(door, animator, OpeningClip, suction, blocker);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        root.GetComponent<CompactorDoorPresentation>().Dispose();
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static BoxCollider CreateBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        BoxCollider collider = go.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
        return collider;
    }

    public static void AddToScene(Scene scene)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;
        var existing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompactorEscapeTestController>(true)).FirstOrDefault();
        GameObject instance = existing != null ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        if (existing == null)
        {
            instance.name = "Compactor Escape Test";
            instance.transform.SetPositionAndRotation(new Vector3(7f, .06f, 7f), Quaternion.Euler(0f, 180f, 0f));
        }
        var objective = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelExitObjective>(true)).FirstOrDefault();
        if (objective == null)
        {
            var objectiveGo = new GameObject("Sandbox Exit Objective");
            SceneManager.MoveGameObjectToScene(objectiveGo, scene);
            objective = objectiveGo.AddComponent<LevelExitObjective>();
        }
        var door = instance.GetComponent<ExitDoor>();
        var doorSo = new SerializedObject(door);
        doorSo.FindProperty("_exitObjective").objectReferenceValue = objective;
        doorSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(door);
        var controller = existing != null ? existing : instance.AddComponent<CompactorEscapeTestController>();
        controller.Configure(door, objective);
        foreach (var ui in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WeaponSandboxDebugUI>(true)))
        {
            var so = new SerializedObject(ui);
            so.FindProperty("_escapeTest").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void RenderPreview()
    {
        EditorSceneManager.OpenScene(SandboxPath);
        var door = Object.FindAnyObjectByType<CompactorDoorPresentation>();
        var animator = door.GetComponentInChildren<Animator>();
        OpeningClip.SampleAnimation(animator.gameObject, OpeningClip.length);
        var suction = door.transform.Find("Black Suction Opening").GetComponent<Renderer>();
        var block = new MaterialPropertyBlock(); block.SetFloat("_Activity", 1f); suction.SetPropertyBlock(block);
        var cameraGo = new GameObject("Compactor Preview Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.transform.position = door.transform.TransformPoint(new Vector3(4.3f, 3.9f, 9.5f));
        camera.transform.LookAt(door.transform.TransformPoint(new Vector3(0f, 2.6f, 0f)));
        camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f,.08f,.09f);
        var rt = new RenderTexture(1000, 1000, 24); camera.targetTexture = rt; camera.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(1000,1000,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();
        Directory.CreateDirectory("tmp/compactor");File.WriteAllBytes("tmp/compactor/unity-preview.png",image.EncodeToPNG());
        RenderTexture.active = null;camera.targetTexture = null;
        Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(cameraGo);
        Debug.Log("COMPACTOR_PREVIEW_COMPLETE");
    }
}
