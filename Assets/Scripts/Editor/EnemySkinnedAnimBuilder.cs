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
    private const string ChaserControllerPath = LarguiruchoAnimationAuthoring.ControllerPath;
    private const string DroneVisualPath = "Assets/Arte/EnemiesAnim/Avispa_Vuelo.fbx";
    private const string DroneControllerPath = "Assets/Arte/EnemiesAnim/DroneFly.controller";
    private const string SlimeVisualPath = "Assets/Arte/EnemiesAnim/Basurita_MiniSalto.fbx";
    private const string SlimeControllerPath = "Assets/Arte/EnemiesAnim/SlimeHop.controller";

    private static readonly string[] DronePrefabs =
    {
        "Assets/Prefabs/Drone.prefab",
        "Assets/Prefabs/Drone (variant).prefab",
        "Assets/Prefabs/Drone_Elite.prefab"
    };

    private static readonly string[] SlimePrefabs =
    {
        "Assets/Prefabs/EnemyPro.prefab",
        "Assets/Prefabs/Slime (variant).prefab",
        "Assets/Prefabs/Slime_Elite.prefab"
    };

    [MenuItem("Tools/ScrapWaves/Fix Enemy Skinned Animations (Chaser+Drone)")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Fix enemy animations in Edit Mode.");

        BuildChaser();
        BuildDrone();
        BuildSlime();
        AssetDatabase.SaveAssets();
        Debug.Log("ENEMY_SKINNED_ANIM_FIX_COMPLETE");
    }

    [MenuItem("Tools/ScrapWaves/Build Chaser Animations")]
    public static void BuildChaserMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build Chaser animations in Edit Mode.");
        BuildChaser();
        Debug.Log("CHASER_ANIMATIONS_BUILD_COMPLETE: " + ChaserControllerPath);
    }

    [MenuItem("Tools/ScrapWaves/Build Slime Hop Animation")]
    public static void BuildSlimeMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build Slime hop animation in Edit Mode.");
        BuildSlime();
        AssetDatabase.SaveAssets();
        Debug.Log("SLIME_HOP_BUILD_COMPLETE: " + SlimeControllerPath);
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

    private static void BuildChaser() => Debug.Log(LarguiruchoAnimationAuthoring.Apply());

    private static void BuildSlime()
    {
        ConfigureSkinnedAnimImport(SlimeVisualPath);
        AnimatorController controller = BuildSingleStateController(SlimeControllerPath, SlimeVisualPath, "Hop");
        foreach (string prefabPath in SlimePrefabs)
        {
            ReplaceVisual(
                prefabPath,
                "Bola de Tierra",
                SlimeVisualPath,
                "Basurita",
                controller,
                defaultPose: new VisualPose(Vector3.zero, Vector3.zero, Vector3.one),
                hidePlaceholders: null,
                rootLocalScale: null,
                preserveExistingPose: true,
                forceLocalEuler: Vector3.zero,
                offsetAnimPhase: true);
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
                rootLocalScale: rootScale,
                preserveExistingPose: false);
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
        VisualPose defaultPose,
        string[] hidePlaceholders,
        Vector3? rootLocalScale = null,
        bool preserveExistingPose = false,
        Vector3? forceLocalEuler = null,
        bool offsetAnimPhase = false)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(skinnedModelPath);
        if (model == null)
            throw new InvalidOperationException("Missing model " + skinnedModelPath);

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(skinnedModelPath).OfType<Avatar>().FirstOrDefault();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            // Emergence owns an independent, neutral parent. Keep current art tuning when
            // rebuilding a migrated prefab instead of restoring the builder's old defaults.
            Transform emergencePivot = root.transform.Find(EnemyEmergenceAuthoring.PivotName);
            bool preserveEmergencePose = root.GetComponent<EnemyEmergence>() != null || emergencePivot != null;
            if (rootLocalScale.HasValue && !preserveEmergencePose)
                root.transform.localScale = rootLocalScale.Value;

            Transform visualParent = emergencePivot != null ? emergencePivot : root.transform;
            // A skinned model can contain a mesh with the legacy visual's name.
            // Replace only a top-level model, never a mesh or bone inside its rig.
            Transform existing = visualParent.Cast<Transform>().FirstOrDefault(t => t.name == existingVisualName)
                ?? visualParent.Cast<Transform>().FirstOrDefault(t => t.name == newVisualName);
            if (preserveEmergencePose && existing == null)
                throw new InvalidOperationException("Missing top-level emergence visual in " + prefabPath);
            Quaternion? preservedRotation = preserveEmergencePose && existing != null ? existing.localRotation : null;

            VisualPose pose = defaultPose;
            if ((preserveExistingPose || preserveEmergencePose) && existing != null)
            {
                pose = new VisualPose(
                    existing.localPosition,
                    preserveEmergencePose ? existing.localEulerAngles : forceLocalEuler ?? existing.localEulerAngles,
                    existing.localScale);
            }
            else if (forceLocalEuler.HasValue)
            {
                pose = new VisualPose(pose.LocalPosition, forceLocalEuler.Value, pose.LocalScale);
            }

            Material[] materials = null;
            var materialsByName = new System.Collections.Generic.Dictionary<string, Material[]>(StringComparer.Ordinal);
            if (existing != null)
            {
                foreach (Renderer renderer in existing.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                        materialsByName[renderer.gameObject.name] = renderer.sharedMaterials;
                }

                var oldSkin = existing.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var oldMr = existing.GetComponentInChildren<MeshRenderer>(true);
                if (oldSkin != null)
                    materials = oldSkin.sharedMaterials;
                else if (oldMr != null)
                    materials = oldMr.sharedMaterials;
            }

            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            foreach (Transform t in visualParent.Cast<Transform>()
                         .Where(t => t.name == newVisualName)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, visualParent);
            visual.name = newVisualName;
            visual.transform.localPosition = pose.LocalPosition;
            visual.transform.localRotation = preservedRotation ?? Quaternion.Euler(pose.LocalEuler);
            visual.transform.localScale = pose.LocalScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);

            if (preserveExistingPose || preserveEmergencePose)
            {
                foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (materialsByName.TryGetValue(skin.gameObject.name, out Material[] named) && named.Length > 0)
                        skin.sharedMaterials = named;
                    skin.updateWhenOffscreen = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
            }
            else if (materials != null && materials.Length > 0)
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

            if (offsetAnimPhase && visual.GetComponent<EnemyAnimPhaseOffset>() == null)
                visual.AddComponent<EnemyAnimPhaseOffset>();

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

            EnemyEmergenceAuthoring.ConfigureActor(root);
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

    /// <summary>
    /// El mesh animado mira en +Z. El yaw 90° del mesh estático los dejaba de costado.
    /// </summary>
    public static string FixSlimeFacing()
    {
        var report = new System.Text.StringBuilder();
        foreach (string prefabPath in SlimePrefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform visual = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "Basurita");
                if (visual == null)
                {
                    report.AppendLine("missing Basurita: " + prefabPath);
                    continue;
                }

                Vector3 scale = visual.localScale;
                visual.localRotation = Quaternion.identity;
                visual.localScale = scale;

                if (visual.GetComponent<EnemyAnimPhaseOffset>() == null)
                    visual.gameObject.AddComponent<EnemyAnimPhaseOffset>();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.AppendLine(prefabPath + " yaw=0 scale=" + scale + " root=" + root.transform.localScale);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        return report.ToString();
    }
}
#endif
