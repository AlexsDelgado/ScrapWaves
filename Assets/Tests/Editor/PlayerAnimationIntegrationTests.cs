using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Checks the generated artifacts and real skeleton/controller boundary, not just authoring constants.</summary>
public sealed class PlayerAnimationIntegrationTests
{
    private const string ModelPath = "Assets/Art/Player/PlaceholderAnimation/PlaceholderPlayer.fbx";
    private const string PrefabPath = "Assets/Prefabs/player.prefab";
    private const string SavedSceneKey = "PlayerAnimationIntegrationTests.SceneSetup";
    private readonly List<Object> _cleanup = new();
    private static readonly string[] ClipNames =
    {
        "Idle", "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "CrouchIdle", "CrouchMove",
        "Jump", "AirJump", "Fall", "Land", "Slide", "Dash", "DashBackward", "Stun", "Aim", "Fire", "Slash", "Flame", "Hit", "Death"
    };
    private static readonly HashSet<string> LoopNames = new()
    {
        "Idle", "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "CrouchIdle", "CrouchMove",
        "Fall", "Slide", "Stun", "Aim", "Flame"
    };
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTearDown]
    public IEnumerator RestoreRuntimeScene()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
        string saved = SessionState.GetString(SavedSceneKey, "");
        if (string.IsNullOrEmpty(saved)) yield break;
        SessionState.EraseString(SavedSceneKey);
        SavedSetup setup = JsonUtility.FromJson<SavedSetup>(saved);
        if (setup?.Scenes?.Length > 0 && setup.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(scene => new SceneSetup
            { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void ImportedModel_HasEveryNamedGenericClipWithCorrectLoopAndNoAnimationEvents()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        Assert.That(importer, Is.Not.Null, "Run the placeholder animation builder before validation.");
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.importAnimation, Is.True);
        AnimationClip[] clips = ImportedClips();
        Assert.That(clips.Select(clip => clip.name), Is.EquivalentTo(ClipNames));
        foreach (AnimationClip clip in clips)
        {
            Assert.That(clip.length, Is.GreaterThan(0f), clip.name);
            Assert.That(clip.frameRate, Is.EqualTo(30f).Within(0.01f), clip.name);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                Is.EqualTo(LoopNames.Contains(clip.name)), clip.name);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty,
                "Gameplay owns damage/projectile timing: " + clip.name);
        }
    }

    [Test]
    public void ImportedSkin_HasNormalizedWeightsAndResolvableBoneBindings()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Assert.That(model, Is.Not.Null);
        SkinnedMeshRenderer[] skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(skins, Is.Not.Empty, "The preview must animate the mesh as well as the skeleton.");
        foreach (SkinnedMeshRenderer skin in skins)
        {
            Mesh mesh = skin.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.bindposes.Length, Is.EqualTo(skin.bones.Length));
            Assert.That(skin.bones.All(bone => bone != null && bone.IsChildOf(model.transform)), Is.True);
            using var counts = mesh.GetBonesPerVertex();
            using var weights = mesh.GetAllBoneWeights();
            Assert.That(counts.Length, Is.EqualTo(mesh.vertexCount));
            int offset = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                Assert.That(counts[vertex], Is.InRange(1, 4), "Unusable weighting at vertex " + vertex);
                float sum = 0f;
                for (int influence = 0; influence < counts[vertex]; influence++)
                {
                    BoneWeight1 weight = weights[offset++];
                    Assert.That(weight.boneIndex, Is.InRange(0, skin.bones.Length - 1));
                    Assert.That(weight.weight, Is.GreaterThan(0f));
                    sum += weight.weight;
                }
                Assert.That(sum, Is.EqualTo(1f).Within(0.001f), "Weights at vertex " + vertex);
            }
            Assert.That(offset, Is.EqualTo(weights.Length));
        }
    }

    [Test]
    public void GameplayPrefab_RetainsMainPointAndMapsAllWearablesToAnimatedBones()
    {
        GameObject prefab = PlayerPrefab();
        PlayerAnimationDriver driver = prefab.GetComponent<PlayerAnimationDriver>();
        Assert.That(driver, Is.Not.Null);
        Animator animator = driver.RigAnimator;
        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.avatar, Is.Not.Null);
        Assert.That(animator.avatar.isHuman, Is.False);
        Assert.That(animator.applyRootMotion, Is.False);
        Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
        PlayerWeaponMountController mounts = prefab.GetComponent<PlayerWeaponMountController>();
        WeaponManager weapons = prefab.GetComponent<WeaponManager>();
        Assert.That(mounts, Is.Not.Null);
        Assert.That(weapons, Is.Not.Null);
        Transform main = mounts.MainFirePoint;
        Assert.That(main, Is.Not.Null);
        Assert.That(main.name, Is.EqualTo("Main Weapon Fire Point"));
        Assert.That(weapons.GetProjectileSpawn(), Is.SameAs(main));
        var legacy = new SerializedObject(prefab.GetComponent<PlayerAutoAttack>());
        Assert.That(legacy.FindProperty("_firePoint").objectReferenceValue, Is.SameAs(main));
        Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main, out string _, out long localId), Is.True);
        Assert.That(localId, Is.EqualTo(123637993059655878L), "Retain serialized references to the original manual point.");
        Assert.That(main.IsChildOf(FindBone(animator.transform, "hand.R")), Is.True);

        AssertSocket(mounts, animator, WeaponType.AutomaticCannon, "shoulder.R", "spine.003");
        AssertSocket(mounts, animator, WeaponType.Flamethrower, "forearm.L");
        AssertSocket(mounts, animator, WeaponType.RocketLauncher, "spine.003", "spine.002");
        AssertSocket(mounts, animator, WeaponType.Mortar, "spine.003", "spine.002");
        AssertSocket(mounts, animator, WeaponType.RotatingBlade, "spine");
        foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
        {
            Transform socket = mounts.GetAnimatedSocket(type);
            if (socket != null) Assert.That(socket, Is.Not.SameAs(main), type.ToString());
        }
    }

    [Test]
    public void ManualMuzzle_RemainsJustBeyondRoboticHandGeometryAcrossPoses()
    {
        GameObject player = Track(Object.Instantiate(PlayerPrefab()));
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(1.25f, .3f, -2f), Quaternion.Euler(0f, 37f, 0f));
        Animator animator = player.GetComponent<PlayerAnimationDriver>().RigAnimator;
        animator.runtimeAnimatorController = null;
        Transform hand = FindBone(animator.transform, "hand.R");
        Transform muzzle = player.GetComponent<PlayerWeaponMountController>().MainFirePoint;
        Assert.That(muzzle.parent, Is.SameAs(hand));
        Vector3 socketPosition = muzzle.localPosition;
        Quaternion socketRotation = muzzle.localRotation;
        SkinnedMeshRenderer skin = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Single(renderer => renderer.bones.Contains(hand));
        int handIndex = Array.IndexOf(skin.bones, hand);
        var handVertices = new List<int>();
        using (var counts = skin.sharedMesh.GetBonesPerVertex())
        using (var weights = skin.sharedMesh.GetAllBoneWeights())
        {
            int offset = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                float handWeight = 0f;
                for (int influence = 0; influence < counts[vertex]; influence++)
                {
                    BoneWeight1 weight = weights[offset++];
                    if (weight.boneIndex == handIndex) handWeight += weight.weight;
                }
                if (handWeight >= .99f) handVertices.Add(vertex);
            }
        }
        Assert.That(handVertices.Count, Is.GreaterThan(5), "The check needs actual rigid robotic hand geometry.");
        Mesh baked = Track(new Mesh());
        var clips = ImportedClips().ToDictionary(clip => clip.name);
        foreach (string clipName in new[] { "Idle", "Slide", "Fire", "Slash" })
        {
            AnimationClip clip = clips[clipName];
            clip.SampleAnimation(animator.gameObject, clip.length * (clipName == "Slash" ? .5f : .25f));
            skin.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;
            float nearestTipGap = float.PositiveInfinity;
            foreach (int vertex in handVertices)
            {
                Vector3 worldVertex = skin.transform.TransformPoint(vertices[vertex]);
                float gap = Vector3.Dot(muzzle.position - worldVertex, muzzle.forward);
                nearestTipGap = Mathf.Min(nearestTipGap, gap);
            }
            Assert.That(nearestTipGap, Is.InRange(.005f, .025f),
                clipName + ": manual shots must start just beyond the outermost robotic hand surface.");
            Assert.That(Vector3.Distance(muzzle.localPosition, socketPosition), Is.LessThan(.00001f),
                clipName + ": animation must preserve the calibrated socket position.");
            Assert.That(Quaternion.Angle(muzzle.localRotation, socketRotation), Is.LessThan(.001f),
                clipName + ": animation must preserve the outlet axis.");
        }
    }

    [Test]
    public void UpperBodyMask_ExplicitlyExcludesHipsLegsAndSocketOffsets()
    {
        Animator animator = PlayerPrefab().GetComponent<PlayerAnimationDriver>().RigAnimator;
        var controller = animator.runtimeAnimatorController as AnimatorController;
        Assert.That(controller, Is.Not.Null);
        AnimatorControllerLayer[] maskedLayers = controller.layers.Where(layer => layer.avatarMask != null).ToArray();
        Assert.That(maskedLayers, Is.Not.Empty, "An upper-body layer needs a rig-specific mask.");
        foreach (AnimatorControllerLayer layer in maskedLayers)
        {
            Assert.That(layer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override),
                "These authored Actions contain absolute poses, not additive deltas: " + layer.name);
            AvatarMask mask = layer.avatarMask;
            string[] excluded = { "spine", "spine.001", "spine.002", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R" };
            foreach (string bone in excluded)
                AssertMaskBone(mask, bone, false);
            AssertMaskBone(mask, "spine.003", true);
            AssertMaskBone(mask, "shoulder.L", true);
            AssertMaskBone(mask, "shoulder.R", true);
            AssertMaskBone(mask, "upper_arm.R", true);
            AssertMaskBone(mask, "forearm.L", true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                if (string.IsNullOrEmpty(path) || path.EndsWith("metarig", StringComparison.Ordinal))
                    Assert.That(mask.GetTransformActive(i), Is.False, layer.name + ": " + path);
            }
        }
        foreach (AnimationClip clip in ImportedClips())
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            Assert.That(binding.path.Contains("Socket") || binding.path.Contains("Fire Point"), Is.False,
                "Authored clips must not overwrite socket offsets: " + clip.name + "/" + binding.path);
    }

    [Test]
    public void LoopClips_JoinWithoutBonePoseDiscontinuity()
    {
        GameObject model = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)));
        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        foreach (AnimationClip clip in ImportedClips().Where(clip => LoopNames.Contains(clip.name)))
        {
            clip.SampleAnimation(model, 0f);
            Vector3[] startPositions = bones.Select(bone => bone.localPosition).ToArray();
            Quaternion[] startRotations = bones.Select(bone => bone.localRotation).ToArray();
            clip.SampleAnimation(model, clip.length);
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.That(Vector3.Distance(startPositions[i], bones[i].localPosition), Is.LessThan(0.002f),
                    clip.name + ": " + bones[i].name);
                Assert.That(Quaternion.Angle(startRotations[i], bones[i].localRotation), Is.LessThan(0.2f),
                    clip.name + ": " + bones[i].name);
            }
        }
    }

    [Test]
    public void Controller_UsesFourDirectionalClipsAndAnUnmaskedDeathOverride()
    {
        AnimatorController controller = Controller();
        Assert.That(controller.layers.Select(layer => layer.name),
            Is.EqualTo(new[] { "Base Layer", "Upper Body", "Full Body" }));
        BlendTree tree = controller.layers[0].stateMachine.states.Single(child => child.state.name == "Locomotion").state.motion as BlendTree;
        Assert.That(tree, Is.Not.Null);
        Assert.That(tree.blendParameter, Is.EqualTo("MoveX"));
        Assert.That(tree.blendParameterY, Is.EqualTo("MoveY"));
        var expected = new Dictionary<string, Vector2>
        {
            ["Idle"] = Vector2.zero, ["MoveForward"] = Vector2.up, ["MoveBackward"] = Vector2.down,
            ["MoveLeft"] = Vector2.left, ["MoveRight"] = Vector2.right
        };
        Assert.That(tree.children.Length, Is.EqualTo(expected.Count));
        foreach (ChildMotion child in tree.children)
        {
            Assert.That(expected.ContainsKey(child.motion.name), Is.True);
            Assert.That(child.position, Is.EqualTo(expected[child.motion.name]));
        }
        AnimatorControllerLayer death = controller.layers[2];
        Assert.That(death.avatarMask, Is.Null);
        Assert.That(death.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
        Assert.That(death.defaultWeight, Is.Zero);
        Assert.That(death.stateMachine.states.Single(child => child.state.name == "Death").state.motion.name, Is.EqualTo("Death"));
    }

    [Test]
    public void Controller_UsesEveryImportedPlayerClip()
    {
        AnimatorController controller = Controller();
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.animationClips.Distinct(), Is.EquivalentTo(ImportedClips()),
            "Keep the imported player animation set limited to clips used by the controller.");
    }

    [Test]
    public void Backstep_BootsClearTheFloorDuringForefootPushAndRearFootCatch()
    {
        var clips = ImportedClips().ToDictionary(clip => clip.name);
        GameObject model = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)));
        model.GetComponent<Animator>().runtimeAnimatorController = null;
        SkinnedMeshRenderer skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        int[][] feet = { FootVertexIndices(skin, "L"), FootVertexIndices(skin, "R") };
        Mesh baked = Track(new Mesh());
        clips["Idle"].SampleAnimation(model, 0f);
        skin.BakeMesh(baked);
        Vector3[] vertices = baked.vertices;
        float floor = feet.Min(indices => LowestWorldVertex(skin, vertices, indices));
        for (int frame = 0; frame <= 18; frame++)
        {
            clips["DashBackward"].SampleAnimation(model, frame / 60f);
            skin.BakeMesh(baked);
            vertices = baked.vertices;
            float[] heights = feet.Select(indices => LowestWorldVertex(skin, vertices, indices) - floor).ToArray();
            Assert.That(heights.Min(), Is.GreaterThanOrEqualTo(-.003f),
                "The boot toe must clear the floor during push-off, frame " + frame);
            Assert.That(heights.Min(), Is.LessThan(.04f),
                "The backstep should stay close to the floor, frame " + frame);
            if (frame >= 11)
                Assert.That(heights[1], Is.LessThan(.012f), "The rear foot must hold its landing through recovery.");
        }
    }

    [TestCase("MoveForward")]
    [TestCase("MoveBackward")]
    [TestCase("MoveLeft")]
    [TestCase("MoveRight")]
    public void DirectionalRun_HasFlatGroundContactsAndBriefFlightBetweenSteps(string clipName)
    {
        var clips = ImportedClips().ToDictionary(clip => clip.name);
        AnimationClip run = clips[clipName];
        Assert.That(run.length, Is.LessThanOrEqualTo(.8f), "The movement cycle should retain the reference's running cadence.");

        GameObject model = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)));
        Animator animator = model.GetComponent<Animator>();
        if (animator != null) animator.runtimeAnimatorController = null;
        Transform[] feet = new[] { "foot.L", "foot.R" }.Select(name => FindBone(model.transform, name)).ToArray();
        SkinnedMeshRenderer skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Single(renderer => feet.All(foot => renderer.bones.Contains(foot)));
        int[][] footVertices = { FootVertexIndices(skin, "L"), FootVertexIndices(skin, "R") };
        Mesh baked = Track(new Mesh());

        clips["Idle"].SampleAnimation(model, 0f);
        skin.BakeMesh(baked);
        Vector3[] vertices = baked.vertices;
        float[] standingSoles = footVertices.Select(indices => LowestWorldVertex(skin, vertices, indices)).ToArray();
        Quaternion[] standingRotations = feet.Select(foot => foot.rotation).ToArray();
        int[] groundedSamples = new int[feet.Length];
        int[] flatLoadedSamples = new int[feet.Length];
        int flightSamples = 0;
        const int samples = 20;
        const float groundTolerance = .02f;
        for (int sample = 0; sample < samples; sample++)
        {
            run.SampleAnimation(model, run.length * sample / samples);
            skin.BakeMesh(baked);
            vertices = baked.vertices;
            bool bothAirborne = true;
            for (int side = 0; side < feet.Length; side++)
            {
                float height = LowestWorldVertex(skin, vertices, footVertices[side]) - standingSoles[side];
                string context = $"{clipName}, {feet[side].name}, run phase {sample / (float)samples:F2}, sole height {height:F3} m";
                Assert.That(height, Is.GreaterThanOrEqualTo(-groundTolerance), "The sole must not sink through the floor: " + context);
                if (Mathf.Abs(height) <= groundTolerance)
                {
                    groundedSamples[side]++;
                    // A real run rolls over its toes at take-off. Require a
                    // sustained flat loading interval, not a locked ankle
                    // throughout the entire contact and push-off sequence.
                    if (Quaternion.Angle(standingRotations[side], feet[side].rotation) <= 8f)
                        flatLoadedSamples[side]++;
                }
                bothAirborne &= height > groundTolerance;
            }
            if (bothAirborne) flightSamples++;
        }
        for (int side = 0; side < feet.Length; side++)
        {
            Assert.That(groundedSamples[side], Is.GreaterThanOrEqualTo(2), "Each foot needs a visible loaded stance: " + feet[side].name);
            Assert.That(flatLoadedSamples[side], Is.GreaterThanOrEqualTo(2),
                "Each foot needs a flat loaded stance before rolling off the toes: " + feet[side].name);
        }
        Assert.That(flightSamples, Is.InRange(1, samples / 2),
            "A run needs brief flight between alternating contacts, without floating for most of its cycle.");
    }

    [UnityTest]
    public IEnumerator RuntimeUpperActions_ChangeUpperPoseWhileKeepingMovingLegs()
    {
        PrepareRuntimeScene();
        yield return new EnterPlayMode();
        yield return RunUpperActionScenarios();
    }

    private IEnumerator RunUpperActionScenarios()
    {
        foreach (string action in new[] { "Fire", "Slash", "Flame", "Hit" })
            yield return SampleUpperAction(action);
        yield return SampleArmedCrouchSlide();
    }

    private IEnumerator SampleUpperAction(string action)
    {
        Assert.That(Application.isPlaying, Is.True, "Playable skin evaluation must run in the actual animation player loop.");
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;
        yield return null; // Let the imported Animator bind its skeleton before creating the test graph.
        PlayableGraph graph = PlayableGraph.Create("Animation mask integration test");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimatorControllerPlayable player = AnimatorControllerPlayable.Create(graph, Controller());
            AnimationPlayableOutput.Create(graph, "Rig", animator).SetSourcePlayable(player);
            graph.Play();
            // The first positive evaluation binds the playable to the imported skeleton.
            // A zero-time evaluation immediately after graph creation can leave an Animator
            // at its bind pose and falsely pass every lower-body equality assertion.
            graph.Evaluate(1f / 60f);
            player.SetFloat("MoveX", 0f);
            player.SetFloat("MoveY", 1f);
            player.SetFloat("LocomotionRate", 1f);
            player.SetLayerWeight(1, 0f);
            player.SetLayerWeight(2, 0f);
            Transform thigh = FindBone(model.transform, "thigh.L");
            player.Play(Animator.StringToHash("Base Layer.Locomotion"), 0, 0f);
            graph.Evaluate(0.001f);
            Quaternion phaseZeroThigh = thigh.localRotation;
            player.Play(Animator.StringToHash("Base Layer.Locomotion"), 0, 0.25f);
            graph.Evaluate(0.001f);
            Assert.That(Quaternion.Angle(phaseZeroThigh, thigh.localRotation), Is.GreaterThan(5f),
                "The test must sample moving legs, not compare two unevaluated bind poses.");
            Transform[] lower = new[] { "spine", "spine.001", "spine.002", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R" }
                .Select(name => FindBone(model.transform, name)).ToArray();
            Vector3[] position = lower.Select(bone => bone.localPosition).ToArray();
            Quaternion[] rotation = lower.Select(bone => bone.localRotation).ToArray();
            Transform arm = FindBone(model.transform, "upper_arm.R");
            Transform chest = FindBone(model.transform, "spine.003");
            Quaternion beforeArm = arm.localRotation;
            Quaternion beforeChest = chest.localRotation;
            player.SetLayerWeight(1, 1f);
            // Reset the base to the same phase and evaluate the same positive delta so
            // any leg difference comes from layer masking, not a later point in the walk.
            player.Play(Animator.StringToHash("Base Layer.Locomotion"), 0, 0.25f);
            player.Play(Animator.StringToHash("Upper Body." + action), 1, 0.5f);
            graph.Evaluate(0.001f);
            for (int i = 0; i < lower.Length; i++)
            {
                Assert.That(Vector3.Distance(position[i], lower[i].localPosition), Is.LessThan(0.001f), lower[i].name);
                Assert.That(Quaternion.Angle(rotation[i], lower[i].localRotation), Is.LessThan(0.1f), lower[i].name);
            }
            // Flame intentionally reuses the braced idle pose; all action states must still sample a finite pose.
            Assert.That(float.IsNaN(arm.localRotation.x) || float.IsNaN(chest.localRotation.x), Is.False);
            if (action == "Slash")
                Assert.That(Quaternion.Angle(beforeArm, arm.localRotation) + Quaternion.Angle(beforeChest, chest.localRotation),
                    Is.GreaterThan(5f), "The upper layer must actually affect the rig.");
            Assert.That(model.transform.position, Is.EqualTo(Vector3.zero), "In-place animation must not move the gameplay actor.");
        }
        finally
        {
            graph.Destroy();
            Object.Destroy(model);
        }
        yield return null;
    }

    private IEnumerator SampleArmedCrouchSlide()
    {
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;
        yield return null;
        PlayableGraph graph = PlayableGraph.Create("Armed crouch-slide layer validation");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimatorControllerPlayable player = AnimatorControllerPlayable.Create(graph, Controller());
            AnimationPlayableOutput.Create(graph, "Rig", animator).SetSourcePlayable(player);
            graph.Play();
            graph.Evaluate(1f / 60f);
            player.SetLayerWeight(1, 0f);
            player.SetLayerWeight(2, 0f);
            player.SetFloat("MoveX", 0f);
            player.SetFloat("MoveY", 0f);
            player.SetFloat("LocomotionRate", 1f);
            player.Play(Animator.StringToHash("Base Layer.Locomotion"), 0, .25f);
            graph.Evaluate(.001f);
            Transform hips = FindBone(model.transform, "spine");
            Transform[] knees = new[] { "shin.L", "shin.R" }.Select(name => FindBone(model.transform, name)).ToArray();
            Transform lowerSpine = FindBone(model.transform, "spine.002");
            float standingHeight = hips.position.y;
            Quaternion standingSpine = lowerSpine.localRotation;
            Quaternion[] standingKnees = knees.Select(knee => knee.localRotation).ToArray();

            int slide = Animator.StringToHash("Base Layer.Slide");
            player.Play(slide, 0, .25f);
            graph.Evaluate(.001f);
            Assert.That(standingHeight - hips.position.y, Is.GreaterThan(.25f), "The slide must visibly lower the pelvis.");
            Assert.That(Quaternion.Angle(standingSpine, lowerSpine.localRotation), Is.GreaterThan(5f),
                "The test must exercise an authored torso lean that a weapon layer could otherwise erase.");
            for (int i = 0; i < knees.Length; i++)
                Assert.That(Quaternion.Angle(standingKnees[i], knees[i].localRotation), Is.GreaterThan(35f),
                    "Both knees must remain folded for a crouch slide: " + knees[i].name);
            Transform[] protectedBones = new[] { "spine", "spine.001", "spine.002", "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R" }
                .Select(name => FindBone(model.transform, name)).ToArray();
            Vector3[] slidePositions = protectedBones.Select(bone => bone.localPosition).ToArray();
            Quaternion[] slideRotations = protectedBones.Select(bone => bone.localRotation).ToArray();
            Transform chest = FindBone(model.transform, "spine.003");
            Quaternion unaimedChest = chest.localRotation;
            foreach (string action in new[] { "Aim", "Fire" })
            {
                player.SetLayerWeight(1, 1f);
                player.Play(slide, 0, .25f);
                player.Play(Animator.StringToHash("Upper Body." + action), 1, .25f);
                graph.Evaluate(.001f);
                for (int i = 0; i < protectedBones.Length; i++)
                {
                    Assert.That(Vector3.Distance(slidePositions[i], protectedBones[i].localPosition), Is.LessThan(.001f), action + ": " + protectedBones[i].name);
                    Assert.That(Quaternion.Angle(slideRotations[i], protectedBones[i].localRotation), Is.LessThan(.1f), action + ": " + protectedBones[i].name);
                }
                if (action == "Fire")
                    Assert.That(Quaternion.Angle(unaimedChest, chest.localRotation), Is.GreaterThan(1f),
                        "The chest action must still play above the protected crouch-slide torso.");
            }
            Assert.That(model.transform.position, Is.EqualTo(Vector3.zero), "The crouch slide is cosmetic and in place.");
        }
        finally
        {
            graph.Destroy();
            Object.Destroy(model);
        }
        yield return null;
    }

    [TestCase(179f, -179f, 95f)]
    [TestCase(-179f, 179f, -95f)]
    public void BehindAim_CrossesAngleSeamWithoutFlippingTorsoSide(float before, float after, float expected)
    {
        float first = PlayerAnimationDriver.ResolveLimitedYaw(before, 0f, 95f);
        float second = PlayerAnimationDriver.ResolveLimitedYaw(after, first, 95f);
        Assert.That(first, Is.EqualTo(expected));
        Assert.That(second, Is.EqualTo(first), "A two-degree rear crossing must not create a 190-degree arm reversal.");
        Assert.That(PlayerAnimationDriver.ResolveLimitedYaw(35f, second, 95f), Is.EqualTo(35f),
            "The aim must release the rear-side latch once the target returns in front.");
    }

    [UnityTest]
    public IEnumerator RuntimeGraph_KeepsShotBatchSocketsStableAndHandlesPausedDeathRevival()
    {
        PrepareRuntimeScene();
        yield return new EnterPlayMode();
        yield return RunDeathScenario();
    }

    private static void PrepareRuntimeScene()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled Editor scene before running the runtime integration test.");
        SessionState.SetString(SavedSceneKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private IEnumerator RunDeathScenario()
    {
        // Create locals after the EnterPlayMode domain reload, following the existing project's test pattern.
        GameObject owner = new GameObject("Isolated animation validation actor");
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), owner.transform);
        Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.runtimeAnimatorController = Controller();
        animator.applyRootMotion = false;
        PlayerHealth health = owner.AddComponent<PlayerHealth>();
        WeaponPresentationController presentation = owner.AddComponent<WeaponPresentationController>();
        PlayerAnimationDriver driver = owner.AddComponent<PlayerAnimationDriver>();
        driver.Configure(animator, FindBone(model.transform, "spine.003"),
            FindBone(model.transform, "upper_arm.R"), FindBone(model.transform, "forearm.R"), FindBone(model.transform, "hand.R"),
            FindBone(model.transform, "upper_arm.L"), FindBone(model.transform, "forearm.L"), FindBone(model.transform, "hand.L"));
        yield return null;
        // Both logical emission points are descendants of the actual imported animated limbs.
        Transform main = new GameObject("Validation Main Muzzle").transform;
        main.SetParent(FindBone(model.transform, "hand.R"), false);
        main.localPosition = Vector3.forward * 0.2f;
        Transform flame = new GameObject("Validation Automatic Flame Muzzle").transform;
        flame.SetParent(FindBone(model.transform, "forearm.L"), false);
        flame.localPosition = Vector3.up * 0.15f;
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        WeaponInstance manual = new() { Data = data, State = WeaponState.Manual, CurrentAmmo = 100f };
        var shot = new WeaponFeedbackContext(manual, WeaponFeedbackMode.Manual, 0f, main.position, Vector3.forward);
        presentation.OnShotFired(in shot);
        yield return null;
        driver.EvaluatePoseForWeapons(Time.deltaTime, Vector3.forward * 20f);
        Vector3 mainBefore = main.position;
        Vector3 flameBefore = flame.position;
        int graphBefore = driver.GraphEvaluationCount;
        presentation.OnShotFired(in shot);
        Assert.That(Vector3.Distance(main.position, mainBefore), Is.LessThan(0.00001f),
            "Confirmed manual fire must not move a muzzle after another weapon already sampled this frame's pose.");
        Assert.That(Vector3.Distance(flame.position, flameBefore), Is.LessThan(0.00001f),
            "A manual recoil event cannot displace the automatic flame stream after its tick.");
        Assert.That(driver.GraphEvaluationCount, Is.EqualTo(graphBefore));
        yield return null;
        driver.EvaluatePoseForWeapons(Time.deltaTime, Vector3.forward * 20f);
        Assert.That(driver.GraphEvaluationCount, Is.GreaterThan(graphBefore), "Queued recoil is consumed at the next scheduled pose.");
        Assert.That(manual.CurrentAmmo, Is.EqualTo(100f), "Presentation feedback cannot consume gameplay ammo.");
        Object.Destroy(data);
        Transform hips = FindBone(model.transform, "spine");
        Vector3 aliveHips = hips.localPosition;
        Vector3 actorPosition = owner.transform.position;
        int before = driver.GraphEvaluationCount;
        driver.EvaluatePoseForWeapons(0.1f, Vector3.forward * 20f);
        driver.EvaluatePoseForWeapons(0.1f, Vector3.forward * 20f);
        Assert.That(driver.GraphEvaluationCount - before, Is.LessThanOrEqualTo(1), "Multiple consumers cannot advance the graph twice per frame.");
        Time.timeScale = 0f;
        health.TakeDamage(health.MaxHealth + 1);
        Assert.That(driver.IsDeathPlaying, Is.True);
        float end = Time.realtimeSinceStartup + 0.7f;
        while (Time.realtimeSinceStartup < end) yield return null;
        Assert.That(Time.timeScale, Is.Zero, "Presentation must leave game-over pause intact.");
        Assert.That(Vector3.Distance(hips.localPosition, aliveHips), Is.GreaterThan(0.1f),
            "Death must progress using unscaled time after immediate game-over pause.");
        Assert.That(owner.transform.position, Is.EqualTo(actorPosition), "Death pose cannot move the gameplay root.");
        health.FullHeal();
        yield return null;
        Assert.That(driver.IsDeathPlaying, Is.False);
        Assert.That(health.IsAlive, Is.True);
        Assert.That(Vector3.Distance(hips.localPosition, aliveHips), Is.LessThan(0.04f), "QA revive must clear the collapse pose.");
        Time.timeScale = 1f;
        Object.Destroy(owner);
    }

    private static GameObject PlayerPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        return prefab;
    }

    private static AnimationClip[] ImportedClips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
        .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();

    private static AnimatorController Controller() => AssetDatabase.LoadAssetAtPath<AnimatorController>(
        PlaceholderPlayerAnimationBuilder.ControllerPath);

    private static Transform FindBone(Transform root, string name)
    {
        Transform bone = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(child => child.name == name);
        Assert.That(bone, Is.Not.Null, "Missing original skeleton bone: " + name);
        return bone;
    }

    private static int[] FootVertexIndices(SkinnedMeshRenderer skin, string side)
    {
        var footBones = new HashSet<int>(skin.bones.Select((bone, index) => (bone, index))
            .Where(entry => entry.bone.name == "foot." + side || entry.bone.name == "toe." + side)
            .Select(entry => entry.index));
        var vertices = new List<int>();
        using var counts = skin.sharedMesh.GetBonesPerVertex();
        using var weights = skin.sharedMesh.GetAllBoneWeights();
        int offset = 0;
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            float footWeight = 0f;
            for (int influence = 0; influence < counts[vertex]; influence++)
            {
                BoneWeight1 weight = weights[offset++];
                if (footBones.Contains(weight.boneIndex)) footWeight += weight.weight;
            }
            if (footWeight >= .5f) vertices.Add(vertex);
        }
        Assert.That(vertices.Count, Is.GreaterThan(3), "The contact check needs weighted mesh geometry for foot " + side);
        return vertices.ToArray();
    }

    private static float LowestWorldVertex(SkinnedMeshRenderer skin, Vector3[] vertices, int[] indices)
    {
        float lowest = float.PositiveInfinity;
        foreach (int index in indices) lowest = Mathf.Min(lowest, skin.transform.TransformPoint(vertices[index]).y);
        return lowest;
    }

    private static void AssertSocket(PlayerWeaponMountController mounts, Animator animator, WeaponType type,
        params string[] carrierBones)
    {
        Transform socket = mounts.GetAnimatedSocket(type);
        Assert.That(socket, Is.Not.Null, type.ToString());
        Assert.That(socket.IsChildOf(animator.transform), Is.True, type.ToString());
        Assert.That(socket.parent, Is.Not.Null, type.ToString());
        Assert.That(carrierBones, Does.Contain(socket.parent.name), type + " carrier");
        Assert.That(socket.localScale.x, Is.GreaterThan(0f), type.ToString());
    }

    private static void AssertMaskBone(AvatarMask mask, string bone, bool expected)
    {
        int index = -1;
        for (int i = 0; i < mask.transformCount; i++)
            if (mask.GetTransformPath(i).Split('/').Last() == bone) { index = i; break; }
        Assert.That(index, Is.GreaterThanOrEqualTo(0), "Mask is missing an explicit entry for " + bone);
        Assert.That(mask.GetTransformActive(index), Is.EqualTo(expected), "Mask boundary: " + bone);
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
}
