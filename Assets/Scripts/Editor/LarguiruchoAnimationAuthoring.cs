#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Rebuilds only Larguirucho presentation. Gameplay components and authored outer poses are preserved.</summary>
public static class LarguiruchoAnimationAuthoring
{
    public const string Folder = "Assets/Art/Enemies/Chaser/Animation";
    public const string AttackModelPath = Folder + "/Larguirucho_Attack.fbx";
    public const string WalkModelPath = Folder + "/Larguirucho_Walk.fbx";
    public const string ControllerPath = Folder + "/Larguirucho.controller";
    public const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    public const string LegacyModelPath = "Assets/Arte/EnemiesAnim/LarguiruchoRigWalk_anim.fbx";
    public const float AnticipationEnd = 10f / 30f;
    public const float ThrustEnd = 44f / 30f;
    private const int SampleRate = 30;
    private static readonly string[] StateNames = { "Idle", "Walk", "Charge", "Dash", "Recover" };
    public static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/Chaser.prefab", "Assets/Prefabs/Chaser (variant).prefab", "Assets/Prefabs/Chaser_Elite.prefab"
    };
    private static readonly string[] TutorialNames = { "1 - Charger", "2 - Charger" };
    private static readonly string[] Properties =
    {
        "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
        "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z"
    };

    [MenuItem("Tools/ScrapWaves/Enemies/Build Larguirucho Animations")]
    private static void ApplyMenu() => Debug.Log(Apply());

    public static void ApplyBatch()
    {
        string report = Apply() + Environment.NewLine + Validate();
        Directory.CreateDirectory(".utmp/larguirucho-2026-10-31");
        File.WriteAllText(".utmp/larguirucho-2026-10-31/authoring-report.txt", report);
        Debug.Log(report);
        if (report.Contains("FAIL")) throw new InvalidOperationException(report);
    }

    public static string Apply()
    {
        RequireSafeEditor();
        ConfigureImport(AttackModelPath);
        ConfigureImport(WalkModelPath);
        AnimationClip attack = SourceClip(AttackModelPath), walk = SourceClip(WalkModelPath);
        if (Mathf.Abs(attack.length - 2.5f) > .05f || Mathf.Abs(walk.length - 1.6f) > .05f)
            throw new InvalidOperationException($"Unexpected source lengths: Attack={attack.length}, Walk={walk.length}. Reinspect phase cuts.");

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(AttackModelPath);
        var clips = BuildClips(model, attack, walk);
        AnimatorController controller = BuildController(clips);
        var report = new StringBuilder();
        report.AppendLine($"Sources: {AttackModelPath} ({attack.length:0.###}s), {WalkModelPath} ({walk.length:0.###}s).");
        using (var sceneScope = new SceneScope())
        {
            TutorialEnemy[] actors = TutorialActors(sceneScope.Scene);
            // Complete structural checks before saving any prefab or scene.
            foreach (string path in PrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                CheckActor(prefab);
            }
            foreach (TutorialEnemy actor in actors) CheckActor(actor.gameObject);

            foreach (string path in PrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureActor(root, model, controller, clips, report);
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                    if (!saved) throw new InvalidOperationException("Failed to save " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (TutorialEnemy actor in actors) ConfigureActor(actor.gameObject, model, controller, clips, report);
            EditorSceneManager.MarkSceneDirty(sceneScope.Scene);
            if (!EditorSceneManager.SaveScene(sceneScope.Scene, ScenePath))
                throw new InvalidOperationException("Failed to save the two tutorial visual updates.");
        }
        report.AppendLine("LARGUIRUCHO_AUTHORING_COMPLETE: three prefabs and two stationary tutorial visuals; gameplay tuning preserved.");
        return report.ToString();
    }

    public static string Validate()
    {
        RequireSafeEditor();
        var report = new StringBuilder();
        foreach (string path in PrefabPaths) ValidateActor(path, AssetDatabase.LoadAssetAtPath<GameObject>(path), report);
        using (var scope = new SceneScope())
            foreach (TutorialEnemy actor in TutorialActors(scope.Scene)) ValidateActor("Tutorial/" + actor.name, actor.gameObject, report);
        return report.ToString();
    }

    private static void ConfigureImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Missing source FBX: " + path);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.preserveHierarchy = true;
        importer.optimizeGameObjects = false;
        importer.optimizeBones = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importConstraints = false;
        importer.motionNodeName = "";
        var takes = importer.defaultClipAnimations;
        if (takes.Length != 1) throw new InvalidOperationException("Expected exactly one animation take in " + path);
        foreach (var take in takes)
        {
            take.loopTime = false;
            take.loopPose = false;
            take.keepOriginalPositionXZ = true;
            take.keepOriginalPositionY = true;
            take.keepOriginalOrientation = true;
            take.lockRootPositionXZ = true;
            take.lockRootHeightY = true;
            take.lockRootRotation = true;
            take.events = Array.Empty<AnimationEvent>();
        }
        importer.clipAnimations = takes;
        importer.SaveAndReimport();
    }

    private static AnimationClip SourceClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

    private static Dictionary<string, AnimationClip> BuildClips(GameObject model, AnimationClip attack, AnimationClip walk)
    {
        GameObject rig = Object.Instantiate(model);
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Animator animator = rig.GetComponent<Animator>();
            if (animator != null) { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; }
            Transform[] transforms = rig.GetComponentsInChildren<Transform>(true).Where(t => t != rig.transform).ToArray();
            var rest = transforms.ToDictionary(t => t, t => new Pose(t));
            Transform root = transforms.Single(t => t.name == "Root");
            Transform spin = transforms.Single(t => t.name == "Drill_Spin");
            Vector3 rootOrigin = rig.transform.InverseTransformPoint(root.position);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(walk))
                if (binding.type == typeof(Transform) && binding.path.Length != 0 && rig.transform.Find(binding.path) == null)
                    throw new InvalidOperationException("Walk has no canonical binding: " + binding.path);

            AnimationClip Make(string name, AnimationClip source, float start, float end, bool looping, bool held)
            {
                float duration = held ? 1f : end - start;
                int frames = held ? 1 : Mathf.RoundToInt(duration * SampleRate);
                var tracks = transforms.ToDictionary(t => t, _ => Enumerable.Range(0, Properties.Length).Select(__ => new List<Keyframe>()).ToArray());
                for (int frame = 0; frame <= frames; frame++)
                {
                    foreach (var entry in rest) entry.Value.Restore(entry.Key);
                    source.SampleAnimation(rig, held ? start : Mathf.Lerp(start, end, frame / (float)frames));
                    // FBX Root is below the Animator. Remove its travel in rig space,
                    // rather than leaving a drifting child with applyRootMotion=false.
                    Vector3 localRoot = rig.transform.InverseTransformPoint(root.position);
                    root.position = rig.transform.TransformPoint(new Vector3(rootOrigin.x, localRoot.y, rootOrigin.z));
                    if (source == walk) rest[spin].Restore(spin);
                    float time = duration * frame / frames;
                    foreach (Transform bone in transforms)
                    {
                        Vector3 p = bone.localPosition, s = bone.localScale; Quaternion q = bone.localRotation;
                        float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                        for (int c = 0; c < Properties.Length; c++) tracks[bone][c].Add(new Keyframe(time, values[c]));
                    }
                }
                string path = Folder + "/" + name + ".anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.name = name; clip.frameRate = SampleRate; clip.legacy = false;
                foreach (Transform bone in transforms)
                for (int c = 0; c < Properties.Length; c++)
                {
                    List<Keyframe> samples = tracks[bone][c];
                    bool constant = samples.All(k => Mathf.Abs(k.value - samples[0].value) < .0000001f);
                    var curve = new AnimationCurve(constant ? new[] { samples[0], samples[samples.Count - 1] } : samples.ToArray());
                    for (int k = 0; k < curve.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(bone, rig.transform), typeof(Transform), Properties[c]), curve);
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = looping; settings.loopBlend = false;
                settings.loopBlendOrientation = settings.loopBlendPositionXZ = settings.loopBlendPositionY = false;
                settings.keepOriginalOrientation = settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
                return clip;
            }

            return new Dictionary<string, AnimationClip>
            {
                ["Idle"] = Make("Idle", walk, 0f, 0f, false, true),
                ["Walk"] = Make("Walk", walk, 0f, walk.length, true, false),
                ["Charge"] = Make("Charge", attack, 0f, AnticipationEnd, false, false),
                ["Dash"] = Make("Dash", attack, AnticipationEnd, ThrustEnd, false, false),
                ["Recover"] = Make("Recover", attack, ThrustEnd, attack.length, false, false)
            };
        }
        finally { Object.DestroyImmediate(rig); }
    }

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
        if (controller.layers.Length != 1) throw new InvalidOperationException("Unexpected Larguirucho controller layers; refusing to discard authored layers.");
        foreach (string parameter in new[] { "WalkSpeed", "ChargePhase", "DashPhase", "RecoverPhase" })
        {
            var found = controller.parameters.SingleOrDefault(p => p.name == parameter);
            if (found == null) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            else if (found.type != AnimatorControllerParameterType.Float) throw new InvalidOperationException("Unexpected parameter type: " + parameter);
        }
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        if (machine.states.Any(s => !StateNames.Contains(s.state.name)) || machine.stateMachines.Length != 0)
            throw new InvalidOperationException("Unexpected Larguirucho states; refusing to discard authored state machines.");
        foreach (string name in StateNames)
        {
            AnimatorState state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clips[name]; state.writeDefaultValues = false;
            state.speed = name == "Walk" ? 1f : 0f;
            state.speedParameterActive = name == "Walk"; state.speedParameter = name == "Walk" ? "WalkSpeed" : "";
            state.timeParameterActive = name == "Charge" || name == "Dash" || name == "Recover";
            state.timeParameter = state.timeParameterActive ? name + "Phase" : "";
            if (state.transitions.Length != 0) throw new InvalidOperationException("State transitions are owned by the gameplay presentation driver: " + name);
            EditorUtility.SetDirty(state);
            if (name == "Idle") machine.defaultState = state;
        }
        EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    private static void ConfigureActor(GameObject actor, GameObject model, AnimatorController controller,
        Dictionary<string, AnimationClip> clips, StringBuilder report)
    {
        Transform oldVisual = CheckActor(actor);
        var preservation = new Preservation(actor, oldVisual);
        Transform wrapper = oldVisual;
        Transform existingRig = oldVisual.Find("Rig");
        // Slots keep the source material's meaning even when a variant overrides it
        // with an unrelated material name such as Boss or Bullet.
        GameObject materialSource = existingRig != null ? model : AssetDatabase.LoadAssetAtPath<GameObject>(LegacyModelPath);
        Dictionary<string, Material> palette = CaptureMaterialPalette(oldVisual, materialSource);
        var canonicalSkins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
        GameObject rig;
        if (existingRig != null && PrefabUtility.GetCorrespondingObjectFromSource(existingRig.gameObject) == model)
            rig = existingRig.gameObject;
        else
        {
            if (existingRig != null) throw new InvalidOperationException("Unexpected existing Rig on " + actor.name);
            Bounds referenceBounds = SampleLocalBounds(oldVisual.gameObject, SourceClip(LegacyModelPath));
            var outerPose = new Pose(oldVisual); Transform parent = oldVisual.parent;
            int sibling = oldVisual.GetSiblingIndex(); int layer = oldVisual.gameObject.layer; bool active = oldVisual.gameObject.activeSelf;
            Object.DestroyImmediate(oldVisual.gameObject);
            wrapper = new GameObject("Larguirucho").transform; wrapper.gameObject.layer = layer;
            wrapper.SetParent(parent, false); outerPose.Restore(wrapper); wrapper.SetSiblingIndex(sibling); wrapper.gameObject.SetActive(active);
            rig = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper);
            rig.name = "Rig";
            rig.transform.localPosition = Vector3.zero; rig.transform.localRotation = Quaternion.identity; rig.transform.localScale = Vector3.one;
            Bounds currentBounds = SampleLocalBounds(rig, clips["Idle"]);
            float scale = referenceBounds.size.y / currentBounds.size.y;
            if (!float.IsFinite(scale) || scale < .1f || scale > 10f) throw new InvalidOperationException("Unexpected visual size conversion on " + actor.name);
            rig.transform.localScale = Vector3.one * scale;
            rig.transform.localPosition = new Vector3(referenceBounds.center.x - currentBounds.center.x * scale,
                referenceBounds.min.y - currentBounds.min.y * scale, referenceBounds.center.z - currentBounds.center.z * scale);
            Record(rig.transform); Record(rig);
        }
        foreach (Transform transform in rig.GetComponentsInChildren<Transform>(true))
        {
            transform.gameObject.layer = wrapper.gameObject.layer;
            Record(transform.gameObject);
        }
        Animator animator = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AttackModelPath).OfType<Avatar>().Single();
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        animator.updateMode = AnimatorUpdateMode.Normal; animator.enabled = true; Record(animator);
        foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] sourceSlots = canonicalSkins[skin.name].sharedMaterials;
            Material[] expected = sourceSlots.Select(material =>
            {
                if (material != null && palette.TryGetValue(MaterialKey(material.name), out Material mapped)) return mapped;
                if (skin.name == "Cintas_Animadas" && palette.TryGetValue(MaterialKey("Rueda"), out mapped)) return mapped;
                throw new InvalidOperationException($"No authored material for {skin.name}/{material?.name} on {actor.name}");
            }).ToArray();
            skin.sharedMaterials = expected;
            if (!skin.sharedMaterials.SequenceEqual(expected))
                throw new InvalidOperationException("Authored material references changed on " + actor.name + "/" + skin.name);
            if (skin.name == "Circle.001")
                foreach (var original in palette.Where(p => p.Key != MaterialKey("Goma_Cintas")))
                {
                    int slot = Array.FindIndex(sourceSlots, m => m != null && MaterialKey(m.name) == original.Key);
                    if (slot < 0 || skin.sharedMaterials[slot] != original.Value)
                        throw new InvalidOperationException("Body material override was not preserved: " + actor.name + "/" + original.Key);
                }
            skin.updateWhenOffscreen = false; Record(skin);
        }
        FitAnimationBounds(rig, clips.Values);
        if (animator.isActiveAndEnabled)
        {
            // Configure resets the driver's state immediately. Initialize this newly
            // instantiated edit-mode Animator before that first Play/SetFloat call.
            var normalizedPose = new Pose(rig.transform);
            animator.Rebind();
            animator.Update(0f);
            normalizedPose.Restore(rig.transform);
        }
        clips["Idle"].SampleAnimation(rig, 0f);
        ChaserAnimationDriver driver = actor.GetComponent<ChaserAnimationDriver>();
        float walkUnitsPerSecond = driver != null
            ? new SerializedObject(driver).FindProperty("_walkUnitsPerSecond").floatValue : 2.125f;
        if (driver == null) driver = actor.AddComponent<ChaserAnimationDriver>();
        driver.Configure(animator, walkUnitsPerSecond); EditorUtility.SetDirty(driver); Record(driver);
        EnemyHitFeedback hit = actor.GetComponent<EnemyHitFeedback>();
        if (hit != null)
        {
            var serialized = new SerializedObject(hit);
            serialized.FindProperty("_visualRoot").objectReferenceValue = wrapper;
            SerializedProperty renderers = serialized.FindProperty("_renderers");
            Renderer[] skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            renderers.arraySize = skins.Length;
            for (int i = 0; i < skins.Length; i++) renderers.GetArrayElementAtIndex(i).objectReferenceValue = skins[i];
            serialized.ApplyModifiedPropertiesWithoutUndo(); Record(hit);
        }
        preservation.Verify(wrapper);
        report.AppendLine($"Updated {actor.name}: {rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} skins, inner scale={rig.transform.localScale}, offset={rig.transform.localPosition}; original outer pose and gameplay components intact.");
    }

    private static string MaterialKey(string name) => Regex.Replace(
        Regex.Replace(name.Trim(), @"(?:\.\d+)+$", ""), @"[\s_-]+", "").ToLowerInvariant();

    private static Dictionary<string, Material> CaptureMaterialPalette(Transform visual, GameObject sourceModel)
    {
        if (sourceModel == null) throw new InvalidOperationException("Missing material source for " + visual.name);
        var sourceSkins = sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
        var result = new Dictionary<string, Material>();
        foreach (SkinnedMeshRenderer skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!sourceSkins.TryGetValue(skin.name, out SkinnedMeshRenderer source))
                throw new InvalidOperationException("Unexpected authored skin for material preservation: " + skin.name);
            Material[] semantics = source.sharedMaterials, actual = skin.sharedMaterials;
            if (semantics.Length != actual.Length) throw new InvalidOperationException("Material slot count differs on " + skin.name);
            for (int i = 0; i < actual.Length; i++)
            {
                if (semantics[i] == null) throw new InvalidOperationException("Missing source material meaning on " + skin.name + "/" + i);
                string key = MaterialKey(semantics[i].name);
                if (result.TryGetValue(key, out Material previous) && previous != actual[i])
                    throw new InvalidOperationException("Conflicting authored material overrides for " + key);
                result[key] = actual[i];
            }
        }
        return result;
    }

    private static Bounds SampleLocalBounds(GameObject rig, AnimationClip clip)
    {
        var poses = rig.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => new Pose(t));
        try
        {
            clip.SampleAnimation(rig, 0f);
            bool found = false; Bounds bounds = default;
            foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh();
                try
                {
                    // Explicit scale handling matches weighted skinning for both FBX export conventions.
                    skin.BakeMesh(mesh, true);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = rig.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        if (found) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); found = true; }
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            if (!found || bounds.size.y < .001f) throw new InvalidOperationException("Cannot measure skinned visual " + rig.name);
            return bounds;
        }
        finally { foreach (var pose in poses) if (pose.Key != null) pose.Value.Restore(pose.Key); }
    }

    private static void FitAnimationBounds(GameObject rig, IEnumerable<AnimationClip> clips)
    {
        var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var bounds = new Bounds[skins.Length];
        var found = new bool[skins.Length];
        AnimationClip[] motions = clips.ToArray();
        var mesh = new Mesh();
        try
        {
            foreach (AnimationClip clip in motions)
            for (int frame = 0; frame <= Mathf.CeilToInt(clip.length * SampleRate); frame++)
            {
                clip.SampleAnimation(rig, Mathf.Min(clip.length, frame / (float)SampleRate));
                for (int i = 0; i < skins.Length; i++)
                {
                    mesh.Clear(); skins[i].BakeMesh(mesh, true);
                    // Skinned localBounds is rooted at rootBone, which has a different
                    // export scale than either new FBX renderer transform.
                    Transform boundsSpace = skins[i].rootBone != null ? skins[i].rootBone : skins[i].transform;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = boundsSpace.InverseTransformPoint(skins[i].transform.TransformPoint(vertex));
                        if (found[i]) bounds[i].Encapsulate(point);
                        else { bounds[i] = new Bounds(point, Vector3.zero); found[i] = true; }
                    }
                }
            }
            for (int i = 0; i < skins.Length; i++)
            {
                if (!found[i]) throw new InvalidOperationException("Cannot fit empty skin " + skins[i].name);
                bounds[i].Expand(.2f); skins[i].localBounds = bounds[i]; Record(skins[i]);
            }
            // Check the renderer's actual world-space culling result, not only the
            // local-space calculation, before saving the migrated actor.
            foreach (AnimationClip clip in motions)
            for (int frame = 0; frame <= Mathf.CeilToInt(clip.length * SampleRate); frame++)
            {
                clip.SampleAnimation(rig, Mathf.Min(clip.length, frame / (float)SampleRate));
                foreach (SkinnedMeshRenderer skin in skins)
                {
                    mesh.Clear(); skin.BakeMesh(mesh, true);
                    Bounds worldBounds = skin.bounds;
                    foreach (Vector3 vertex in mesh.vertices)
                        if (!worldBounds.Contains(skin.transform.TransformPoint(vertex)))
                            throw new InvalidOperationException($"Animated culling bounds miss {skin.name} in {clip.name} frame {frame}.");
                }
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    private static Transform CheckActor(GameObject actor)
    {
        if (actor == null || actor.GetComponent<ChargerEnemyBehavior>() == null) throw new InvalidOperationException("Missing target Charger actor.");
        Transform pivot = actor.transform.Find("EmergenceVisual");
        Transform visual = pivot != null ? pivot.Find("Larguirucho") : null;
        if (visual == null || pivot.childCount != 1 || actor.GetComponent<EnemyEmergence>()?.VisualPivot != pivot)
            throw new InvalidOperationException("Unexpected emergence hierarchy on " + actor.name);
        if (visual.GetComponentsInChildren<Collider>(true).Length != 0 || visual.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            throw new InvalidOperationException("Visual contains gameplay physics on " + actor.name);
        return visual;
    }

    private static void ValidateActor(string label, GameObject actor, StringBuilder report)
    {
        try
        {
            Transform wrapper = CheckActor(actor);
            Animator animator = wrapper.Find("Rig")?.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController != AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) || animator.applyRootMotion)
                throw new InvalidOperationException("Missing in-place animation controller.");
            if (actor.GetComponent<ChaserAnimationDriver>() == null || wrapper.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 2)
                throw new InvalidOperationException("Missing root driver or body/treads skin.");
            if (new SerializedObject(actor.GetComponent<EnemyHitFeedback>()).FindProperty("_visualRoot").objectReferenceValue != wrapper)
                throw new InvalidOperationException("Hit response is not bound to the whole cosmetic rig.");
            TutorialEnemy tutorial = actor.GetComponent<TutorialEnemy>();
            if (tutorial != null && (actor.GetComponent<ChargerEnemyBehavior>().enabled || actor.GetComponent<SimpleFollow>().enabled ||
                !new SerializedObject(tutorial).FindProperty("_stationary").boolValue)) throw new InvalidOperationException("Stationary tutorial settings changed.");
            report.AppendLine("PASS " + label);
        }
        catch (Exception error) { report.AppendLine("FAIL " + label + ": " + error.Message); }
    }

    private static TutorialEnemy[] TutorialActors(Scene scene)
    {
        TutorialEnemy[] actors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialEnemy>(true))
            .Where(t => TutorialNames.Contains(t.name) && t.transform.parent != null && t.transform.parent.name == "TutorialWorld").ToArray();
        if (actors.Length != 2 || !actors.Select(a => a.name).OrderBy(n => n).SequenceEqual(TutorialNames.OrderBy(n => n)))
            throw new InvalidOperationException("Expected the two named stationary tutorial Chargers.");
        return actors;
    }

    private static void RequireSafeEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Run Larguirucho authoring in an idle Edit Mode editor.");
        if (PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Close Prefab Mode before authoring.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("An existing open scene has unsaved changes: " + SceneManager.GetSceneAt(i).name);
    }

    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private readonly struct Pose
    {
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        public Pose(Transform transform) { _position = transform.localPosition; _rotation = transform.localRotation; _scale = transform.localScale; }
        public void Restore(Transform transform) { transform.localPosition = _position; transform.localRotation = _rotation; transform.localScale = _scale; }
        public bool EqualsPose(Transform transform) => transform.localPosition == _position && transform.localScale == _scale && transform.localRotation == _rotation;
    }

    private sealed class Preservation
    {
        private readonly Dictionary<Transform, Pose> _poses;
        private readonly Dictionary<Component, string> _components;
        private readonly Pose _outerPose;
        private readonly Transform _parent;
        private readonly bool _active;
        private readonly int _layer, _sibling;
        public Preservation(GameObject actor, Transform visual)
        {
            bool Outside(Component component) => component.transform != visual && !component.transform.IsChildOf(visual);
            _poses = actor.GetComponentsInChildren<Transform>(true).Where(Outside).ToDictionary(t => t, t => new Pose(t));
            _components = actor.GetComponentsInChildren<Component>(true).Where(c => c != null && Outside(c) && !(c is Transform) &&
                !(c is EnemyHitFeedback) && !(c is ChaserAnimationDriver)).ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            _outerPose = new Pose(visual); _parent = visual.parent; _active = visual.gameObject.activeSelf; _layer = visual.gameObject.layer; _sibling = visual.GetSiblingIndex();
        }
        public void Verify(Transform visual)
        {
            if (!_outerPose.EqualsPose(visual) || visual.parent != _parent || visual.gameObject.activeSelf != _active || visual.gameObject.layer != _layer || visual.GetSiblingIndex() != _sibling)
                throw new InvalidOperationException("The authored visual placement changed.");
            foreach (var pair in _poses)
                if (pair.Key == null || !pair.Value.EqualsPose(pair.Key)) throw new InvalidOperationException("An existing gameplay transform changed: " + pair.Key);
            foreach (var pair in _components)
                if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value) throw new InvalidOperationException("An existing gameplay component/reference changed: " + pair.Key);
        }
    }

    private sealed class SceneScope : IDisposable
    {
        private readonly Scene _previous = SceneManager.GetActiveScene();
        private readonly bool _opened;
        private readonly bool _alreadyListed;
        public Scene Scene { get; }
        public SceneScope()
        {
            _alreadyListed = EditorSceneManager.GetSceneManagerSetup().Any(s => s.path == ScenePath);
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            _opened = !scene.IsValid() || !scene.isLoaded;
            Scene = _opened ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : scene;
        }
        public void Dispose()
        {
            if (_opened && Scene.IsValid()) EditorSceneManager.CloseScene(Scene, !_alreadyListed);
            if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
        }
    }
}
#endif
