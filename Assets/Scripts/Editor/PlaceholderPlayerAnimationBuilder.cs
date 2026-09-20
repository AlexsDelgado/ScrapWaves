using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable authoring of the placeholder rig, controller and sockets.</summary>
public static class PlaceholderPlayerAnimationBuilder
{
    public const string Folder = "Assets/Art/Player/PlaceholderAnimation";
    public const string ModelPath = Folder + "/PlaceholderPlayer.fbx";
    public const string ControllerPath = Folder + "/PlaceholderPlayer.controller";
    public const string MaskPath = Folder + "/UpperBody.mask";
    public const string PlayerPath = "Assets/Prefabs/player.prefab";
    public const string Output = ".utmp/player-animation";
    // Measured on the original mesh: wrist ring vertices 1074..1081 to the
    // three prong terminal edges 1140..1145. Unity model bind coordinates;
    // the outlet is centred between the prongs, 12 mm beyond their furthest tip.
    private static readonly Vector3 ManualMuzzleBindPosition = new(.8153204f, .8932381f, -.0066977f);
    private static readonly Vector3 ManualMuzzleBindDirection = new(.7473143f, -.6625556f, -.0504122f);
    private static readonly string[] Names = { "Idle", "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "CrouchIdle", "CrouchMove", "Jump", "AirJump", "Fall", "Land", "Slide", "Dash", "DashBackward", "Stun", "Aim", "Fire", "Slash", "Flame", "Hit", "Death" };
    private static readonly string[] Loops = { "Idle", "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "CrouchIdle", "CrouchMove", "Fall", "Slide", "Stun", "Aim", "Flame" };

    [MenuItem("Tools/ScrapWaves/Build Placeholder Player Animation")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build animations in Edit Mode.");
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null) throw new InvalidOperationException("Generate PlaceholderPlayer.fbx using Tools/Blender/generate_player_animations.py first.");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.optimizeBones = false;
        importer.preserveHierarchy = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.SaveAndReimport();
        ConfigureClipImports(importer);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var mask = BuildMask(model);
        var controller = BuildController(mask);
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            // Preserve the existing muzzle object and all serialized consumers.
            Transform main = player.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Main Weapon Fire Point");
            main.SetParent(player.transform, true);
            Transform previous = player.transform.Find("PlaceholderPlayerVisual");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            // Keep the old source FBX in the prefab for easy restoration; hide its renderer only.
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                if (sourcePath == "Assets/Art/Player (1).fbx" || sourcePath == "Assets/Art/PlayerModel.fbx") renderer.enabled = false;
            }
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, player.scene);
            visual.name = "PlaceholderPlayerVisual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, -0.7347f, 0f);
            RestoreBindPose(visual);
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = player.layer;
            ConvertMaterials(visual);
            var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.updateWhenOffscreen = true;
                skin.localBounds = new Bounds(new Vector3(0f, .9f, 0f), new Vector3(4f, 4f, 4f));
            }
            Transform Bone(string name) => visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
            var driver = player.GetComponent<PlayerAnimationDriver>() ?? player.AddComponent<PlayerAnimationDriver>();
            driver.Configure(animator, Bone("spine.003"), Bone("upper_arm.R"), Bone("forearm.R"), Bone("hand.R"), Bone("upper_arm.L"), Bone("forearm.L"), Bone("hand.L"));
            MigrateDefaultPoseTimes(driver);

            ConfigureWearableSockets(player, visual);
            // Calibrate against the robotic hand geometry, then keep the original
            // serialized muzzle object attached to hand.R through every pose.
            main.SetPositionAndRotation(visual.transform.TransformPoint(ManualMuzzleBindPosition),
                Quaternion.LookRotation(visual.transform.TransformDirection(ManualMuzzleBindDirection), player.transform.up));
            main.localScale = Vector3.one;
            main.SetParent(Bone("hand.R"), true);
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            var report = new StringBuilder();
            report.AppendLine("Unity " + Application.unityVersion + "; Generic, in-place, scale 1, full hierarchy retained.");
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                report.AppendLine(AnimationUtility.CalculateTransformPath(t, visual.transform) + " world=" + t.position.ToString("F4"));
            foreach (var clip in Clips()) report.AppendLine($"{clip.name}: {clip.length:F4}s, {clip.frameRate} fps, loop={clip.isLooping}, events={clip.events.Length}");
            File.WriteAllText(Output + "/unity-import.txt", report.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        Debug.Log("PLACEHOLDER_PLAYER_BUILD_COMPLETE");
        RenderPreviews();
    }

    /// <summary>Refresh clip takes and the generated controller without rebuilding the player or its attachments.</summary>
    [MenuItem("Tools/ScrapWaves/Refresh Player Animation Controller")]
    public static void RefreshAnimationController()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refresh animations in Edit Mode.");
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Missing model " + ModelPath);
        ConfigureClipImports(importer);
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) throw new InvalidOperationException("Build the player animation mask before refreshing its controller.");
        var controller = BuildController(mask);
        AssetDatabase.SaveAssetIfDirty(controller);
        Debug.Log("PLAYER_ANIMATION_CONTROLLER_REFRESH_COMPLETE");
    }

    private static void ConfigureClipImports(ModelImporter importer)
    {
        var takes = importer.defaultClipAnimations;
        importer.clipAnimations = Names.Select(name =>
        {
            var take = takes.SingleOrDefault(t => t.name == name || t.name.EndsWith("|" + name, StringComparison.Ordinal));
            if (take == null) throw new InvalidOperationException("Missing FBX take " + name + "; found: " + string.Join(",", takes.Select(t => t.name)));
            take.name = name;
            take.loopTime = Loops.Contains(name);
            take.loopPose = take.loopTime;
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

    [MenuItem("Tools/ScrapWaves/Refit Animated Wearable Sockets")]
    public static void RefitWearableSockets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refit sockets in Edit Mode.");
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var visual = player.transform.Find("PlaceholderPlayerVisual").gameObject;
            RestoreBindPose(visual);
            ConfigureWearableSockets(player, visual);
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureWearableSockets(GameObject player, GameObject visual)
    {
        // Preserve the approved catalog layout exactly in the bind pose.
        // Convert once; animations never key the sockets themselves.
        var catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>("Assets/Resources/WearableWeaponMounts.asset");
        var types = new[] { WeaponType.RotatingBlade, WeaponType.AutomaticCannon, WeaponType.Flamethrower, WeaponType.RocketLauncher, WeaponType.Mortar };
        var bones = new[] { "spine", "shoulder.R", "forearm.L", "spine.003", "spine.003" };
        var mounts = player.GetComponent<PlayerWeaponMountController>();
        var sockets = new AnimatedWeaponSocket[types.Length];
        var transforms = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < types.Length; i++)
        {
            if (!catalog.TryGet(types[i], out var definition)) throw new InvalidOperationException("Missing catalog weapon " + types[i]);
            Transform socket = mounts.GetAnimatedSocket(types[i]);
            if (socket == null) socket = new GameObject(types[i] + " Animation Socket").transform;
            socket.SetParent(player.transform, false);
            socket.localPosition = definition.LocalPosition;
            socket.localRotation = Quaternion.Euler(definition.LocalEulerAngles);
            socket.localScale = definition.LocalScale;
            socket.SetParent(transforms.Single(t => t.name == bones[i]), true);
            sockets[i] = new AnimatedWeaponSocket { Type = types[i], Socket = socket };
        }
        mounts.ConfigureAnimatedSockets(sockets);
        PlayerWearableSurfaceBuilder.Configure(player, visual, sockets);
    }

    private static AvatarMask BuildMask(GameObject model)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
        mask.name = "UpperBody";
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        var transforms = model.GetComponentsInChildren<Transform>(true);
        // Keep the lower-spine movement posture when an armed upper layer is at
        // full weight. Chest and shoulders still own aiming and action accents.
        Transform boundary = transforms.Single(t => t.name == "spine.003");
        mask.transformCount = transforms.Length;
        for (int i = 0; i < transforms.Length; i++)
        {
            mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(transforms[i], model.transform));
            mask.SetTransformActive(i, transforms[i] == boundary || transforms[i].IsChildOf(boundary));
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    public static AnimationClip[] Clips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();

    private static void MigrateDefaultPoseTimes(PlayerAnimationDriver driver)
    {
        // Existing prefabs retain serialized values when code defaults change.
        // Upgrade only the original defaults, preserving any custom tuning.
        var serialized = new SerializedObject(driver);
        void Migrate(string field, float previous, float replacement)
        {
            var property = serialized.FindProperty(field);
            if (property != null && property.floatValue == previous) property.floatValue = replacement;
        }
        Migrate("_firePoseTime", .2f, .28f);
        Migrate("_slashPoseTime", .35f, .55f);
        Migrate("_hitPoseTime", .2f, .34f);
        Migrate("_landingPoseTime", .12f, .22f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RestoreBindPose(GameObject visual)
    {
        // Imported model defaults may contain a sampled Action. Bind matrices are
        // authoritative for converting the legacy root-space attachment layout.
        var matrices = new System.Collections.Generic.Dictionary<Transform, Matrix4x4>();
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var bindposes = skin.sharedMesh.bindposes;
            for (int i = 0; i < skin.bones.Length; i++)
                matrices[skin.bones[i]] = skin.transform.localToWorldMatrix * bindposes[i].inverse;
        }
        int Depth(Transform t) { int n = 0; while (t.parent != null) { n++; t = t.parent; } return n; }
        foreach (var pair in matrices.OrderBy(p => Depth(p.Key)))
        {
            pair.Key.SetPositionAndRotation(pair.Value.GetColumn(3), pair.Value.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Key);
        }
    }

    private static AnimatorController BuildController(AvatarMask mask)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        // Own only this generated controller. Keep its GUID while replacing subassets.
        controller.layers = Array.Empty<AnimatorControllerLayer>();
        foreach (Object child in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (child != controller) Object.DestroyImmediate(child, true);
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        foreach (string p in new[] { "MoveX", "MoveY", "Speed", "LocomotionRate" }) controller.AddParameter(p, AnimatorControllerParameterType.Float);
        var clips = Clips().ToDictionary(c => c.name);
        AnimatorStateMachine Layer(string name, AvatarMask m, float weight)
        {
            var sm = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(sm, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = sm, defaultWeight = weight, avatarMask = m, blendingMode = AnimatorLayerBlendingMode.Override });
            return sm;
        }
        AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
        {
            var state = sm.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }
        var baseLayer = Layer("Base Layer", null, 1);
        var move = new BlendTree { name = "Directional Locomotion", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveY", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(move, controller);
        move.AddChild(clips["Idle"], Vector2.zero);
        move.AddChild(clips["MoveForward"], Vector2.up); move.AddChild(clips["MoveBackward"], Vector2.down);
        move.AddChild(clips["MoveLeft"], Vector2.left); move.AddChild(clips["MoveRight"], Vector2.right);
        var locomotion = State(baseLayer, "Locomotion", move);
        locomotion.speedParameter = "LocomotionRate"; locomotion.speedParameterActive = true;
        baseLayer.defaultState = locomotion;
        var crouch = new BlendTree { name = "Crouch Speed", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(crouch, controller);
        crouch.AddChild(clips["CrouchIdle"],0); crouch.AddChild(clips["CrouchMove"],1);
        var crouchState = State(baseLayer, "Crouch", crouch);
        crouchState.speedParameter = "LocomotionRate"; crouchState.speedParameterActive = true;
        foreach (string n in new[] { "Jump", "AirJump", "Fall", "Land", "Slide", "Dash", "DashBackward", "Stun" }) State(baseLayer,n,clips[n]);
        var upper = Layer("Upper Body", mask, 1);
        foreach (string n in new[] { "Aim", "Fire", "Slash", "Flame", "Hit" }) State(upper,n,clips[n]);
        upper.defaultState = upper.states[0].state;
        var full = Layer("Full Body", null, 0);
        full.defaultState = State(full,"Empty",null);
        State(full,"Death",clips["Death"]);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ConvertMaterials(GameObject visual)
    {
        Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh();
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                if (source == null) return source;
                string path = Folder + "/Materials/" + source.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = source.name };
                    material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.color : Color.gray);
                    material.SetFloat("_Smoothness", .25f);
                    AssetDatabase.CreateAsset(material,path);
                }
                return material;
            }).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }

    [MenuItem("Tools/ScrapWaves/Render Placeholder Player Animation")]
    public static void RenderPreviews()
    {
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.NewPreviewScene();
        var previousAmbient = RenderSettings.ambientLight;
        var previousFog = RenderSettings.fog;
        try
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath),scene);
            var visual = player.transform.Find("PlaceholderPlayerVisual").gameObject;
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            visual.GetComponent<Animator>().runtimeAnimatorController = null;
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bakedMeshes = new Mesh[skins.Length];
            for (int i = 0; i < skins.Length; i++)
            {
                // Immediate CPU baking prevents edit-mode Camera.Render from showing
                // a cached GPU skin pose from a previously sampled Action.
                bakedMeshes[i] = new Mesh { name = "Disposable sampled player mesh" };
                var meshObject = new GameObject("Sampled animated mesh");
                meshObject.transform.SetParent(skins[i].transform, false);
                meshObject.AddComponent<MeshFilter>().sharedMesh = bakedMeshes[i];
                meshObject.AddComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
                skins[i].enabled = false;
            }
            var catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>("Assets/Resources/WearableWeaponMounts.asset");
            var mounts = player.GetComponent<PlayerWeaponMountController>();
            foreach (WeaponType type in new[] { WeaponType.RotatingBlade, WeaponType.AutomaticCannon, WeaponType.Flamethrower, WeaponType.RocketLauncher, WeaponType.Mortar })
            {
                catalog.TryGet(type, out var definition);
                Object.Instantiate(definition.Prefab, mounts.GetAnimatedSocket(type), false);
            }
            var cameraObject = new GameObject("Animation Preview Camera"); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f,.12f,.15f);
            camera.orthographic = true; camera.orthographicSize = 1.4f; camera.nearClipPlane = .01f;
            var lightObject = new GameObject("Animation Preview Light"); SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(40,-25,0);
            RenderSettings.ambientLight = new Color(.65f,.65f,.65f);
            RenderSettings.fog = false;
            var samples = new StringBuilder();
            var arm = visual.GetComponentsInChildren<Transform>().Single(t => t.name == "upper_arm.R");
            var clips = Clips().ToDictionary(clip => clip.name);
            foreach (var clip in clips.Values)
            {
                clip.SampleAnimation(visual,0f);
                samples.Append(clip.name + " arm0=" + arm.localRotation.ToString("F3"));
                float sampleTime = clip.length * PreviewPhase(clip.name);
                clip.SampleAnimation(visual, sampleTime);
                samples.AppendLine(" sampleTime=" + sampleTime.ToString("F4") + " armSample=" + arm.localRotation.ToString("F3"));
                player.GetComponent<PlayerWearableSurfaceFollower>()?.EvaluateAttachments();
                for (int i = 0; i < skins.Length; i++) skins[i].BakeMesh(bakedMeshes[i]);
                foreach (string view in new[] { "front", "back" })
                {
                    camera.transform.position = new Vector3(view == "front" ? 3f : -3f,1.7f,view == "front" ? 6f : -6f);
                    camera.transform.LookAt(new Vector3(0,.15f,0));
                    Render(camera,Output + "/unity-" + clip.name + "-" + view + ".png");
                }
            }
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            foreach (string action in new[] { "Aim", "Fire" })
            {
                // Explicit full-weight composition of the same absolute clips and
                // mask used by the controller. Runtime tests independently verify
                // the live layer result; these stills omit procedural target aim.
                SampleLayeredPose(visual, clips["Slide"], clips[action], mask);
                player.GetComponent<PlayerWearableSurfaceFollower>()?.EvaluateAttachments();
                for (int i = 0; i < skins.Length; i++) skins[i].BakeMesh(bakedMeshes[i]);
                foreach (string view in new[] { "side", "back" })
                {
                    camera.transform.position = view == "side" ? new Vector3(6f, .9f, 0f) : new Vector3(-3f, 1.7f, -6f);
                    camera.transform.LookAt(new Vector3(0f, -.05f, 0f));
                    Render(camera, Output + "/unity-Slide-" + action + "-" + view + ".png");
                }
                samples.AppendLine("Slide + " + action + ": full-weight UpperBody mask composition; procedural target aim omitted.");
            }
            Directory.CreateDirectory(Output + "/motion");
            // A visible contact plane makes loaded soles and flight readable.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Disposable motion-preview ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(0f, -.748f, 0f);
            ground.transform.localScale = Vector3.one * .45f;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(.15f, .17f, .20f));
            groundMaterial.SetFloat("_Smoothness", 0f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            light.shadows = LightShadows.Soft;
            camera.orthographicSize = 1.4f;
            camera.transform.position = new Vector3(3f, 1.7f, 6f);
            camera.transform.LookAt(new Vector3(0f, .15f, 0f));
            foreach (string pose in new[] { "Idle", "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "Slide" })
            {
                var lower = clips[pose];
                var upper = clips["Aim"];
                for (int frame = 0; frame < 120; frame++)
                {
                    float elapsed = frame / 30f;
                    SampleLayeredPose(visual, lower, upper, mask,
                        Mathf.Repeat(elapsed, lower.length) / lower.length,
                        Mathf.Repeat(elapsed, upper.length) / upper.length);
                    player.GetComponent<PlayerWearableSurfaceFollower>()?.EvaluateAttachments();
                    for (int i = 0; i < skins.Length; i++) skins[i].BakeMesh(bakedMeshes[i]);
                    Render(camera, Output + $"/motion/{pose}-{frame:D3}.png", 480);
                }
                samples.AppendLine(pose + " motion: 120 frames at 30 fps; full-weight Aim mask composition; procedural target aim omitted.");
            }
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            // Disposable close-ups make the otherwise invisible emission point
            // reviewable on the actual skinned mesh. Green sphere = manual origin.
            var muzzleMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            muzzleMarker.name = "Manual muzzle preview marker";
            muzzleMarker.transform.SetParent(mounts.MainFirePoint, false);
            muzzleMarker.transform.localScale = Vector3.one * .015f;
            var markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            markerMaterial.SetColor("_BaseColor", new Color(.1f, 1f, .3f));
            muzzleMarker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            foreach (string pose in new[] { "Idle", "Slide", "Fire" })
            {
                clips[pose].SampleAnimation(visual, clips[pose].length * PreviewPhase(pose));
                player.GetComponent<PlayerWearableSurfaceFollower>()?.EvaluateAttachments();
                for (int i = 0; i < skins.Length; i++) skins[i].BakeMesh(bakedMeshes[i]);
                Transform muzzle = mounts.MainFirePoint;
                camera.orthographicSize = .25f;
                camera.transform.position = muzzle.position + muzzle.forward * .65f + player.transform.right * .55f + Vector3.up * .45f;
                camera.transform.LookAt(muzzle.position - muzzle.forward * .065f);
                Render(camera, Output + "/unity-ManualMuzzle-" + pose + ".png");
                samples.AppendLine("Manual muzzle " + pose + ": green marker at robotic prong outlet, position=" + muzzle.position.ToString("F4"));
            }
            Object.DestroyImmediate(markerMaterial);
            File.WriteAllText(Output + "/unity-samples.txt",samples.ToString());
            foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
        }
        finally { RenderSettings.ambientLight = previousAmbient; RenderSettings.fog = previousFog; EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static float PreviewPhase(string clipName) => clipName switch
    {
        "Fire" => .25f,
        "Jump" or "AirJump" => .4f,
        "Land" or "Dash" or "DashBackward" or "Slash" or "Hit" => .5f,
        "Death" => 1f,
        _ => .25f
    };

    private static void SampleLayeredPose(GameObject visual, AnimationClip lower, AnimationClip upper, AvatarMask mask,
        float? lowerPhase = null, float? upperPhase = null)
    {
        lower.SampleAnimation(visual, lower.length * (lowerPhase ?? PreviewPhase(lower.name)));
        var activePaths = Enumerable.Range(0, mask.transformCount)
            .Where(mask.GetTransformActive).Select(mask.GetTransformPath).ToHashSet();
        var lowerTransforms = visual.GetComponentsInChildren<Transform>(true)
            .Where(t => !activePaths.Contains(AnimationUtility.CalculateTransformPath(t, visual.transform))).ToArray();
        var positions = lowerTransforms.Select(t => t.localPosition).ToArray();
        var rotations = lowerTransforms.Select(t => t.localRotation).ToArray();
        var scales = lowerTransforms.Select(t => t.localScale).ToArray();
        upper.SampleAnimation(visual, upper.length * (upperPhase ?? PreviewPhase(upper.name)));
        for (int i = 0; i < lowerTransforms.Length; i++)
        {
            lowerTransforms[i].localPosition = positions[i];
            lowerTransforms[i].localRotation = rotations[i];
            lowerTransforms[i].localScale = scales[i];
        }
    }

    private static void Render(Camera camera, string path, int size = 720)
    {
        var rt = RenderTexture.GetTemporary(size,size,24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(size,size,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,size,size),0,0); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());
        }
        finally { camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
    }
}
