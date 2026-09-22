#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// El mesh de prefab (Larguirucho.fbx / Avispa2.fbx) es MeshRenderer sin bones.
/// El rig skineado vive en los FBX de animación; este builder los usa como visual.
/// </summary>
public static class EnemySkinnedAnimBuilder
{
    private const string ChaserVisualPath = "Assets/Arte/EnemiesAnim/LarguiruchoRigWalk_anim.fbx";
    private const string ChaserControllerPath = "Assets/Arte/EnemiesAnim/ChaserWalk.controller";
    private const string DroneVisualPath = "Assets/Arte/EnemiesAnim/Avispa_Vuelo.fbx";
    private const string DroneControllerPath = "Assets/Arte/EnemiesAnim/DroneFly.controller";

    private static readonly string[] ChaserPrefabs =
    {
        "Assets/Prefabs/Chaser.prefab",
        "Assets/Prefabs/Chaser (variant).prefab",
        "Assets/Prefabs/Chaser_Elite.prefab"
    };

    private static readonly string[] DronePrefabs =
    {
        "Assets/Prefabs/Drone.prefab",
        "Assets/Prefabs/Drone (variant).prefab",
        "Assets/Prefabs/Drone_Elite.prefab"
    };

    [MenuItem("Tools/ScrapWaves/Fix Enemy Skinned Animations (Chaser+Drone)")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Fix enemy animations in Edit Mode.");

        BuildChaser();
        BuildDrone();
        AssetDatabase.SaveAssets();
        Debug.Log("ENEMY_SKINNED_ANIM_FIX_COMPLETE");
    }

    [MenuItem("Tools/ScrapWaves/Build Chaser Walk Animation")]
    public static void BuildChaserMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build Chaser walk animation in Edit Mode.");
        BuildChaser();
        AssetDatabase.SaveAssets();
        Debug.Log("CHASER_WALK_BUILD_COMPLETE: " + ChaserControllerPath);
    }

    [MenuItem("Tools/ScrapWaves/Build Drone Fly Animation")]
    public static void BuildDroneMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build Drone fly animation in Edit Mode.");
        BuildDrone();
        AssetDatabase.SaveAssets();
        Debug.Log("DRONE_FLY_BUILD_COMPLETE: " + DroneControllerPath);
    }

    // With Y90 facing, measured height at scale 1 ≈ 1.27m. Target default ≈ player (2m).
    private const float ChaserNativeHeight = 1.27f;
    private const float ChaserTargetHeight = 2f;
    private static readonly float ChaserBaseScale = ChaserTargetHeight / ChaserNativeHeight; // ≈ 1.57

    // Y90: mesh faces +X in Blender; gameplay forward is +Z (EnemyFollow LookRotation).
    private static readonly VisualPose ChaserPoseDefault = new(
        localPosition: Vector3.zero,
        localEuler: new Vector3(0f, 90f, 0f),
        localScale: Vector3.one * ChaserBaseScale);

    // Variant + Elite: double height via root scale ×2 (visual stays at default size).
    private static readonly Vector3 ChaserLargeRootScale = new(2f, 2f, 2f);

    private static readonly VisualPose DronePoseDefault = new(
        localPosition: new Vector3(0f, 1f, 0f),
        localEuler: new Vector3(0f, 90f, 0f),
        localScale: Vector3.one);

    // Elite drone: root ×4 → ~7m, claramente “muy grande”.
    private static readonly Vector3 DroneEliteRootScale = new(4f, 4f, 4f);

    private readonly struct VisualPose
    {
        public readonly Vector3 LocalPosition;
        public readonly Vector3 LocalEuler;
        public readonly Vector3 LocalScale;

        public VisualPose(Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            LocalPosition = localPosition;
            LocalEuler = localEuler;
            LocalScale = localScale;
        }
    }

    private static void BuildChaser()
    {
        ConfigureSkinnedAnimImport(ChaserVisualPath);
        AnimatorController controller = BuildSingleStateController(ChaserControllerPath, ChaserVisualPath, "Walk");
        foreach (string prefabPath in ChaserPrefabs)
        {
            bool large = prefabPath.Contains("Elite") || prefabPath.Contains("(variant)");
            Vector3? rootScale = large ? ChaserLargeRootScale : Vector3.one;
            ReplaceVisual(
                prefabPath,
                "Larguirucho",
                ChaserVisualPath,
                "Larguirucho",
                controller,
                ChaserPoseDefault,
                hidePlaceholders: new[] { "Cylinder" },
                rootLocalScale: rootScale);
        }
    }

    private static void BuildDrone()
    {
        ConfigureSkinnedAnimImport(DroneVisualPath);
        AnimatorController controller = BuildSingleStateController(DroneControllerPath, DroneVisualPath, "Fly");
        foreach (string prefabPath in DronePrefabs)
        {
            Vector3? rootScale = prefabPath.Contains("Elite") ? DroneEliteRootScale : Vector3.one;
            ReplaceVisual(
                prefabPath,
                "Avispa2",
                DroneVisualPath,
                "Avispa2",
                controller,
                DronePoseDefault,
                hidePlaceholders: new[] { "drone" },
                rootLocalScale: rootScale);
        }
    }

    private static void ConfigureSkinnedAnimImport(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException("Missing FBX: " + path);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.optimizeBones = false;
        importer.preserveHierarchy = true;

        ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
        if (takes == null || takes.Length == 0)
            takes = importer.clipAnimations;
        if (takes == null || takes.Length == 0)
            throw new InvalidOperationException("No animation takes in " + path);

        importer.clipAnimations = takes.Select(take =>
        {
            take.loopTime = true;
            take.loopPose = true;
            take.keepOriginalPositionXZ = true;
            take.keepOriginalPositionY = true;
            take.keepOriginalOrientation = true;
            take.lockRootPositionXZ = true;
            take.lockRootHeightY = true;
            take.lockRootRotation = true;
            return take;
        }).ToArray();

        importer.SaveAndReimport();
    }

    private static AnimatorController BuildSingleStateController(string controllerPath, string clipSourcePath, string stateName)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(clipSourcePath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (clip == null)
            throw new InvalidOperationException("No AnimationClip in " + clipSourcePath);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        while (controller.layers.Length > 0)
            controller.RemoveLayer(0);

        controller.AddLayer("Base Layer");
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        sm.states = Array.Empty<ChildAnimatorState>();
        sm.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
        sm.entryTransitions = Array.Empty<AnimatorTransition>();

        AnimatorState state = sm.AddState(stateName);
        state.motion = clip;
        state.writeDefaultValues = true;
        sm.defaultState = state;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ReplaceVisual(
        string prefabPath,
        string existingVisualName,
        string skinnedModelPath,
        string newVisualName,
        RuntimeAnimatorController controller,
        VisualPose pose,
        string[] hidePlaceholders,
        Vector3? rootLocalScale = null)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(skinnedModelPath);
        if (model == null)
            throw new InvalidOperationException("Missing model " + skinnedModelPath);

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(skinnedModelPath).OfType<Avatar>().FirstOrDefault();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (rootLocalScale.HasValue)
                root.transform.localScale = rootLocalScale.Value;

            Transform existing = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == existingVisualName);

            Material[] materials = null;
            if (existing != null)
            {
                var oldSkin = existing.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var oldMr = existing.GetComponentInChildren<MeshRenderer>(true);
                if (oldSkin != null)
                    materials = oldSkin.sharedMaterials;
                else if (oldMr != null)
                    materials = oldMr.sharedMaterials;
            }

            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)
                         .Where(t => t.name == newVisualName)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = newVisualName;
            visual.transform.localPosition = pose.LocalPosition;
            visual.transform.localRotation = Quaternion.Euler(pose.LocalEuler);
            visual.transform.localScale = pose.LocalScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);

            if (materials != null && materials.Length > 0)
            {
                foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.sharedMaterials = materials;
                    skin.updateWhenOffscreen = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
            }
            else
            {
                foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.updateWhenOffscreen = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
            }

            Animator animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (avatar != null)
                animator.avatar = avatar;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

            if (hidePlaceholders != null)
            {
                foreach (string placeholder in hidePlaceholders)
                {
                    Transform p = root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name == placeholder);
                    if (p == null)
                        continue;
                    foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                        r.enabled = false;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("Replaced skinned visual on " + prefabPath + " -> " + newVisualName
                      + " scale=" + pose.LocalScale + " euler=" + pose.LocalEuler
                      + " skins=" + visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
