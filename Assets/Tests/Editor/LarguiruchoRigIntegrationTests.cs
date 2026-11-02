using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class LarguiruchoRigIntegrationTests
{
    private const string AnimationFolder = "Assets/Art/Enemies/Chaser/Animation/";
    private const string OriginalMaterialGuid = "964ba6a5416ac844ab22f7ff69b5ac96";
    private readonly List<GameObject> _instances = new();
    private readonly List<Scene> _previewScenes = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
            if (_instances[i] != null) Object.DestroyImmediate(_instances[i]);
        _instances.Clear();
        for (int i = _previewScenes.Count - 1; i >= 0; i--)
            if (_previewScenes[i].IsValid()) EditorSceneManager.ClosePreviewScene(_previewScenes[i]);
        _previewScenes.Clear();
    }

    [TestCase("Chaser", 2f, 30f, 12f, 25, 20f, false)]
    [TestCase("Chaser (variant)", 1.5f, 38f, 14f, 12, 30f, true)]
    [TestCase("Chaser_Elite", 1.5f, 38f, 14f, 12, 50f, true)]
    public void ProductionPrefab_BindsCompleteRigWithoutChangingCombatTuning(
        string name, float chargeSeconds, float dashSpeed, float dashDistance,
        int hitDamage, float pushForce, bool overcharged)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
        Assert.That(prefab, Is.Not.Null, name);
        AssertRigBinding(prefab);
        AssertCombatTuning(prefab, chargeSeconds, dashSpeed, dashDistance, hitDamage, pushForce, overcharged);
        SerializedObject variation = new(prefab.GetComponent<EnemyStatRandomizer>());
        Vector2 expectedRange = name == "Chaser_Elite" ? Vector2.one : new Vector2(0.8f, 1.2f);
        Assert.That(variation.FindProperty("_sizeMultiplierRange").vector2Value, Is.EqualTo(expectedRange));
        Assert.That(variation.FindProperty("_movementSpeedMultiplierRange").vector2Value, Is.EqualTo(expectedRange));
    }

    [Test]
    public void TutorialChargers_BindTheRigAndKeepStationaryLessonsAndDrops()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/GameplayScene.unity");
        try
        {
            TutorialEnemy[] chargers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TutorialEnemy>(true))
                .Where(actor => actor.GetComponent<ChargerEnemyBehavior>() != null).ToArray();
            CollectionAssert.AreEquivalent(new[] { "1 - Charger", "2 - Charger" }, chargers.Select(x => x.name));
            foreach (TutorialEnemy actor in chargers)
            {
                AssertRigBinding(actor.gameObject);
                AssertCombatTuning(actor.gameObject, 2f, 30f, 12f, 25, 20f, false);
                SerializedObject lesson = new(actor);
                Assert.That(lesson.FindProperty("_stationary").boolValue, Is.True, actor.name);
                Assert.That(lesson.FindProperty("_damageMultiplier").floatValue, Is.EqualTo(0.5f), actor.name);
                Assert.That(actor.GetComponent<ChargerEnemyBehavior>().enabled, Is.False, actor.name);
                Assert.That(actor.GetComponent<EnemyFollow>().enabled, Is.False, actor.name);
                Assert.That(actor.GetComponent<SimpleFollow>().enabled, Is.False, actor.name);
                Assert.That(actor.Drops, Has.Length.EqualTo(6), actor.name);
                Assert.That(actor.Drops.All(drop => drop != null), Is.True, actor.name);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [TestCase("Chaser")]
    [TestCase("Chaser (variant)")]
    [TestCase("Chaser_Elite")]
    [TestCase("1 - Charger")]
    [TestCase("2 - Charger")]
    public void AuthoredRenderBounds_ContainEveryBodyAndTreadVertexThroughoutAllClips(string actorName)
    {
        GameObject visual = NewAuthoredVisual(actorName);
        Animator animator = visual.GetComponentInChildren<Animator>(true);
        Assert.That(animator, Is.Not.Null, actorName);
        animator.enabled = false;
        SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(skins, Has.Length.EqualTo(2), actorName);
        foreach (SkinnedMeshRenderer skin in skins)
            Assert.That(skin.updateWhenOffscreen, Is.False,
                actorName + ": automatic bounds updates must not hide an incorrect saved bound.");

        var mesh = new Mesh();
        var vertices = new List<Vector3>();
        try
        {
            foreach (AnimatorState state in States(LoadController()).Values)
            foreach (float phase in new[] { 0f, 0.5f, 1f })
            {
                AnimationClip clip = Clip(state);
                clip.SampleAnimation(animator.gameObject, SampleTime(clip, phase));
                foreach (SkinnedMeshRenderer skin in skins)
                {
                    mesh.Clear();
                    // Compensated vertices are in mesh-transform space. Compare their
                    // world positions with Renderer.bounds: localBounds is instead
                    // interpreted in root-bone space for these imported skins.
                    skin.BakeMesh(mesh, true);
                    mesh.GetVertices(vertices);
                    Assert.That(vertices, Is.Not.Empty, actorName + "/" + skin.name);
                    Bounds bounds = skin.bounds;
                    float maximumOutside = 0f;
                    Vector3 worstVertex = Vector3.zero;
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 world = skin.transform.TransformPoint(vertex);
                        Vector3 delta = world - bounds.center;
                        float outside = Mathf.Max(Mathf.Abs(delta.x) - bounds.extents.x,
                            Mathf.Max(Mathf.Abs(delta.y) - bounds.extents.y, Mathf.Abs(delta.z) - bounds.extents.z));
                        if (outside > maximumOutside) { maximumOutside = outside; worstVertex = world; }
                    }
                    Assert.That(maximumOutside, Is.LessThanOrEqualTo(0.001f),
                        $"{actorName}/{skin.name}: {state.name} at {phase:0.##} has vertex {worstVertex} outside world bounds {bounds}.");
                }
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void Controller_HasSeparateBehaviorPhasesAndNoGameplayAnimationEvents()
    {
        AnimatorController controller = LoadController();
        Dictionary<string, AnimatorState> states = States(controller);
        CollectionAssert.AreEquivalent(new[] { "Idle", "Walk", "Charge", "Dash", "Recover" }, states.Keys);
        Assert.That(states["Walk"].speedParameterActive, Is.True);
        Assert.That(states["Walk"].speedParameter, Is.EqualTo("WalkSpeed"));
        foreach (string name in new[] { "Charge", "Dash", "Recover" })
        {
            Assert.That(states[name].timeParameterActive, Is.True, name);
            Assert.That(states[name].timeParameter, Is.EqualTo(name + "Phase"), name);
            Assert.That(AnimationUtility.GetAnimationClipSettings(Clip(states[name])).loopTime, Is.False, name);
        }
        foreach (AnimatorState state in states.Values)
            Assert.That(AnimationUtility.GetAnimationEvents(Clip(state)), Is.Empty,
                state.name + " must not add animation-driven damage or movement events.");
        Assert.That(AssetImporter.GetAtPath(AnimationFolder + "Larguirucho_Attack.fbx"), Is.TypeOf<ModelImporter>());
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(AnimationFolder + "Larguirucho_Attack.fbx");
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
    }

    [Test]
    public void AttackPhases_LeaveGameplayRootAndPlanarSkeletonRootStationary()
    {
        GameObject model = NewCanonicalRig();
        Transform skeletonRoot = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Root");
        Dictionary<string, AnimatorState> states = States(LoadController());
        Vector3 modelPosition = new(7f, 3f, -5f);
        model.transform.position = modelPosition;
        model.transform.rotation = Quaternion.Euler(0f, 31f, 0f);
        Clip(states["Charge"]).SampleAnimation(model, 0f);
        Vector3 rootStart = model.transform.InverseTransformPoint(skeletonRoot.position);
        foreach (string phase in new[] { "Charge", "Dash", "Recover" })
        {
            AnimationClip clip = Clip(states[phase]);
            for (int sample = 0; sample <= 20; sample++)
            {
                clip.SampleAnimation(model, SampleTime(clip, sample / 20f));
                Vector3 root = model.transform.InverseTransformPoint(skeletonRoot.position);
                Assert.That(Vector3.Distance(model.transform.position, modelPosition), Is.LessThan(0.0001f), phase);
                Assert.That(Mathf.Abs(root.x - rootStart.x), Is.LessThan(0.0001f), phase + " root X");
                Assert.That(Mathf.Abs(root.z - rootStart.z), Is.LessThan(0.0001f), phase + " root Z");
            }
        }
    }

    [Test]
    public void Walk_RestoresTheAttackOnlyDrillSpinBone()
    {
        GameObject model = NewCanonicalRig();
        Transform drill = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Drill_Spin");
        Quaternion rest = drill.localRotation;
        Dictionary<string, AnimatorState> states = States(LoadController());
        AnimationClip dash = Clip(states["Dash"]);
        float maximumSpin = 0f;
        for (int i = 0; i <= 17; i++)
        {
            dash.SampleAnimation(model, SampleTime(dash, i / 17f));
            maximumSpin = Mathf.Max(maximumSpin, Quaternion.Angle(rest, drill.localRotation));
        }
        Assert.That(maximumSpin, Is.GreaterThan(5f), "The canonical rig must retain the attack's spinning drill.");

        // A deliberately stale attack rotation proves that Walk writes the extra bone,
        // which is absent from the walk FBX, rather than relying on Animator defaults.
        drill.localRotation = rest * Quaternion.Euler(23f, 47f, 61f);
        AnimationClip walk = Clip(states["Walk"]);
        walk.SampleAnimation(model, walk.length * 0.37f);
        Assert.That(Quaternion.Angle(rest, drill.localRotation), Is.LessThan(0.1f));
    }

    [Test]
    public void Walk_PreservesForwardTreadMotionWithoutLoopPoseCorrection()
    {
        AnimationClip walk = Clip(States(LoadController())["Walk"]);
        var settings = AnimationUtility.GetAnimationClipSettings(walk);
        Assert.That(settings.loopTime, Is.True);
        Assert.That(settings.loopBlend, Is.False,
            "Loop pose correction pulls each tread back to its original identity along the track.");
        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(AnimationFolder + "Larguirucho_Walk.fbx")
            .OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .OrderByDescending(clip => clip.length).FirstOrDefault();
        Assert.That(source, Is.Not.Null);
        GameObject actual = NewCanonicalRig();
        GameObject reference = NewCanonicalRig();
        Transform[] actualTreads = UsedTreadBones(actual);
        Dictionary<string, Transform> referenceByPath = reference.GetComponentsInChildren<Transform>(true)
            .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, reference.transform));
        Assert.That(actualTreads.Length, Is.GreaterThanOrEqualTo(80));
        Vector3[] initial = null;
        float maximumTravel = 0f;
        // The derived clips are intentionally baked at 30 Hz with linear tangents.
        // Compare authored sample frames, not the source's different subframe interpolation.
        foreach (int frame in new[] { 0, 8, 19, 30, 41, 48 })
        {
            float phase = frame / 48f;
            walk.SampleAnimation(actual, SampleTime(walk, phase));
            source.SampleAnimation(reference, SampleTime(source, phase));
            for (int i = 0; i < actualTreads.Length; i++)
            {
                Transform bone = actualTreads[i];
                string path = AnimationUtility.CalculateTransformPath(bone, actual.transform);
                Transform expected = referenceByPath[path];
                Assert.That(Vector3.Distance(bone.localPosition, expected.localPosition), Is.LessThan(0.0002f), path);
                Assert.That(Quaternion.Angle(bone.localRotation, expected.localRotation), Is.LessThan(0.1f), path);
                if (initial != null) maximumTravel = Mathf.Max(maximumTravel, Vector3.Distance(bone.position, initial[i]));
            }
            initial ??= actualTreads.Select(t => t.position).ToArray();
        }
        Assert.That(maximumTravel, Is.GreaterThan(0.001f), "The comparison must include moving tread bones.");
    }

    [Test]
    public void Walk_TreadLoopSeamPreservesTheTrackShapeEvenWhenBoneIdentitiesCycle()
    {
        GameObject model = NewCanonicalRig();
        AnimationClip walk = Clip(States(LoadController())["Walk"]);
        Transform[] treads = UsedTreadBones(model);
        walk.SampleAnimation(model, 0f);
        Vector3[] start = treads.Select(t => model.transform.InverseTransformPoint(t.position)).ToArray();
        walk.SampleAnimation(model, SampleTime(walk, 1f));
        Vector3[] end = treads.Select(t => model.transform.InverseTransformPoint(t.position)).ToArray();
        Bounds bounds = new(start[0], Vector3.zero);
        foreach (Vector3 point in start) bounds.Encapsulate(point);
        float tolerance = Mathf.Max(0.001f, bounds.size.magnitude * 0.025f);
        Assert.That(MaxNearestDistance(start, end), Is.LessThan(tolerance), "Track shape jumps at the walk wrap.");
        Assert.That(MaxNearestDistance(end, start), Is.LessThan(tolerance), "Track shape loses tread coverage at the wrap.");
    }

    private static void AssertRigBinding(GameObject root)
    {
        string label = root.name;
        Transform cosmetic = root.transform.Find("EmergenceVisual/Larguirucho");
        Assert.That(cosmetic, Is.Not.Null, label);
        Assert.That(root.GetComponent<EnemyEmergence>().VisualPivot, Is.EqualTo(cosmetic.parent), label);
        Assert.That(cosmetic.GetComponentsInChildren<Collider>(true), Is.Empty, label);
        ChaserAnimationDriver driver = root.GetComponent<ChaserAnimationDriver>();
        Assert.That(driver, Is.Not.Null, label);
        Assert.That(driver.enabled, Is.True, label);
        Animator[] animators = cosmetic.GetComponentsInChildren<Animator>(true);
        Assert.That(animators, Has.Length.EqualTo(1), label);
        Assert.That(driver.AnimationAnimator, Is.EqualTo(animators[0]), label);
        Assert.That(animators[0].applyRootMotion, Is.False, label);
        Assert.That(animators[0].runtimeAnimatorController, Is.EqualTo(LoadController()), label);
        EnemyHitFeedback feedback = root.GetComponent<EnemyHitFeedback>();
        Assert.That(feedback, Is.Not.Null, label);
        Assert.That(new SerializedObject(feedback).FindProperty("_visualRoot").objectReferenceValue, Is.EqualTo(cosmetic), label);
        Assert.That(root.GetComponent<EnemyDeathFeedback>(), Is.Not.Null, label);
        Assert.That(root.GetComponent<EnemyStatusFeedback>(), Is.Not.Null, label);
        SkinnedMeshRenderer[] skins = cosmetic.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Assert.That(skins, Has.Length.EqualTo(2), label + " must have one body and one tread mesh, without the old visual.");
        SkinnedMeshRenderer tread = skins.Single(IsTread);
        SkinnedMeshRenderer body = skins.Single(skin => skin != tread);
        Assert.That(body.sharedMaterials, Has.Length.EqualTo(7), label);
        AssertAuthoredBodyMaterials(root.name, body.sharedMaterials);
        Assert.That(tread.sharedMaterials, Has.Length.EqualTo(1), label);
        Assert.That(tread.sharedMaterials[0], Is.SameAs(body.sharedMaterials[1]),
            label + " treads must use this actor's authored wheel material, including variant overrides.");
        foreach (SkinnedMeshRenderer skin in skins)
        {
            Assert.That(skin.sharedMesh, Is.Not.Null, label);
            Assert.That(skin.rootBone, Is.Not.Null, label);
            Assert.That(skin.bones.All(bone => bone != null && bone.IsChildOf(cosmetic)), Is.True, label);
            Assert.That(skin.sharedMaterials, Is.Not.Empty, label);
            foreach (Material material in skin.sharedMaterials)
                Assert.That(material, Is.Not.Null, label);
        }
    }

    private static void AssertAuthoredBodyMaterials(string actorName, Material[] actual)
    {
        // Verified pre-integration slots: Metal, Rueda, Oxido, Metal Brillante,
        // Verde, Amarillo, Rojo. Both stationary tutorials use the base palette.
        long[] originalIds =
        {
            -4973734387028869240L, 5625871826073799281L, 1861955341971383155L,
            -2218623673735529665L, 5258131358227053084L, -4820541742559877589L, 8261315333632255771L
        };
        string[] variantGuids =
        {
            "770767f100334604990614344d908b61", // Boss.mat
            "39c57dff6e72ed2498f3a8bd57be4e2a", // default.mat
            "17038e16f5e16174fa5345182e1c02d5", // Bullet.mat
            "17038e16f5e16174fa5345182e1c02d5"
        };
        bool variant = actorName == "Chaser (variant)" || actorName == "Chaser_Elite";
        for (int slot = 0; slot < originalIds.Length; slot++)
        {
            Assert.That(actual[slot], Is.Not.Null, actorName + " material slot " + slot);
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(actual[slot], out string guid, out long localId), Is.True);
            bool overridden = variant && slot < variantGuids.Length;
            Assert.That(guid, Is.EqualTo(overridden ? variantGuids[slot] : OriginalMaterialGuid),
                actorName + " material slot " + slot);
            Assert.That(localId, Is.EqualTo(overridden ? 2100000L : originalIds[slot]),
                actorName + " material slot " + slot);
        }
    }

    private static void AssertCombatTuning(GameObject root, float charge, float speed, float distance, int damage, float push, bool overcharged)
    {
        SerializedObject behavior = new(root.GetComponent<ChargerEnemyBehavior>());
        Assert.That(behavior.FindProperty("_chargeSeconds").floatValue, Is.EqualTo(charge), root.name);
        Assert.That(behavior.FindProperty("_dashSpeed").floatValue, Is.EqualTo(speed), root.name);
        Assert.That(behavior.FindProperty("_dashDuration").floatValue, Is.EqualTo(0.5f), root.name);
        Assert.That(behavior.FindProperty("_dashMaxDistance").floatValue, Is.EqualTo(distance), root.name);
        Assert.That(behavior.FindProperty("_hitDamage").intValue, Is.EqualTo(damage), root.name);
        Assert.That(behavior.FindProperty("_pushForce").floatValue, Is.EqualTo(push), root.name);
        Assert.That(behavior.FindProperty("_postCrashCooldown").floatValue, Is.EqualTo(1.5f), root.name);
        Assert.That(behavior.FindProperty("_overheatSeconds").floatValue, Is.EqualTo(2.5f), root.name);
        Assert.That(behavior.FindProperty("_overcharged").boolValue, Is.EqualTo(overcharged), root.name);
    }

    private static AnimatorController LoadController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimationFolder + "Larguirucho.controller");
        Assert.That(controller, Is.Not.Null);
        return controller;
    }

    private static Dictionary<string, AnimatorState> States(AnimatorController controller) =>
        controller.layers[0].stateMachine.states.ToDictionary(child => child.state.name, child => child.state);

    private static AnimationClip Clip(AnimatorState state)
    {
        Assert.That(state.motion, Is.TypeOf<AnimationClip>(), state.name);
        return (AnimationClip)state.motion;
    }

    private GameObject NewCanonicalRig()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AnimationFolder + "Larguirucho_Attack.fbx");
        Assert.That(source, Is.Not.Null);
        GameObject instance = Object.Instantiate(source);
        _instances.Add(instance);
        instance.hideFlags = HideFlags.HideAndDontSave;
        foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        return instance;
    }

    private GameObject NewAuthoredVisual(string actorName)
    {
        GameObject Clone(Transform source)
        {
            Assert.That(source, Is.Not.Null, actorName);
            // Instantiate only the cosmetic subtree, avoiding gameplay callbacks.
            GameObject instance = Object.Instantiate(source.gameObject);
            _instances.Add(instance);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(source.position, source.rotation);
            instance.transform.localScale = source.lossyScale;
            return instance;
        }

        if (!actorName.EndsWith(" - Charger", StringComparison.Ordinal))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + actorName + ".prefab");
            Assert.That(prefab, Is.Not.Null, actorName);
            return Clone(prefab.transform.Find("EmergenceVisual/Larguirucho"));
        }

        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/GameplayScene.unity");
        _previewScenes.Add(scene);
        TutorialEnemy actor = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TutorialEnemy>(true)).Single(t => t.name == actorName);
        return Clone(actor.transform.Find("EmergenceVisual/Larguirucho"));
    }

    private static bool IsTread(SkinnedMeshRenderer skin) => skin.name.Contains("Cintas_Animadas")
        || (skin.sharedMesh != null && skin.sharedMesh.name.Contains("Cintas_Animadas"));

    private static Transform[] UsedTreadBones(GameObject model)
    {
        SkinnedMeshRenderer tread = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(IsTread);
        HashSet<int> indices = new();
        foreach (BoneWeight weight in tread.sharedMesh.boneWeights)
        {
            if (weight.weight0 > 0f) indices.Add(weight.boneIndex0);
            if (weight.weight1 > 0f) indices.Add(weight.boneIndex1);
            if (weight.weight2 > 0f) indices.Add(weight.boneIndex2);
            if (weight.weight3 > 0f) indices.Add(weight.boneIndex3);
        }
        return indices.OrderBy(i => i).Select(i => tread.bones[i]).ToArray();
    }

    private static float SampleTime(AnimationClip clip, float phase) =>
        Mathf.Min(clip.length * phase, Mathf.Max(0f, clip.length - 0.00001f));

    private static float MaxNearestDistance(Vector3[] from, Vector3[] to) =>
        from.Max(a => to.Min(b => Vector3.Distance(a, b)));
}
