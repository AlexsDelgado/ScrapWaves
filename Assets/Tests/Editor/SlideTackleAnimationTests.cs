using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public sealed class SlideTackleAnimationTests
{
    private readonly List<Object> _cleanup = new();
    [TearDown] public void Cleanup() { foreach (var item in _cleanup) if (item != null) Object.DestroyImmediate(item); _cleanup.Clear(); }
    private T Track<T>(T item) where T : Object { _cleanup.Add(item); return item; }
    private AnimationClip Tackle => AssetDatabase.LoadAssetAtPath<AnimationClip>(SlideTackleAnimationAuthoring.ClipPath);
    private GameObject Rig()
    {
        var rig = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPlayerAnimationBuilder.ModelPath)));
        rig.transform.localScale = Vector3.one * 1.2f;
        var animator = rig.GetComponent<Animator>();
        animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return rig;
    }
    private static Transform Bone(GameObject rig, string name) => rig.GetComponentsInChildren<Transform>().Single(b => b.name == name);
    private static float Knee(GameObject rig, string side) => Vector3.Angle(
        Bone(rig, "thigh." + side).position - Bone(rig, "shin." + side).position,
        Bone(rig, "foot." + side).position - Bone(rig, "shin." + side).position);
    private static float Lowest(SkinnedMeshRenderer skin, Mesh mesh)
    {
        skin.BakeMesh(mesh);
        return mesh.vertices.Min(v => skin.transform.TransformPoint(v).y);
    }
    private static int[] BootVertices(SkinnedMeshRenderer skin)
    {
        using var counts = skin.sharedMesh.GetBonesPerVertex(); using var weights = skin.sharedMesh.GetAllBoneWeights();
        var vertices = new List<int>(); int offset = 0;
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            float weight = 0f;
            for (int i = 0; i < counts[vertex]; i++)
            {
                var influence = weights[offset++]; string name = skin.bones[influence.boneIndex].name;
                if (name.StartsWith("foot.", StringComparison.Ordinal) || name.StartsWith("toe.", StringComparison.Ordinal)) weight += influence.weight;
            }
            if (weight > .5f) vertices.Add(vertex);
        }
        Assert.That(vertices.Count, Is.GreaterThan(30)); return vertices.ToArray();
    }
    private static float LowestBoot(SkinnedMeshRenderer skin, Mesh mesh, int[] boots)
    {
        skin.BakeMesh(mesh); var vertices = mesh.vertices;
        return boots.Min(i => skin.transform.TransformPoint(vertices[i]).y);
    }

    [Test]
    public void AuthoredSlide_IsAnInPlaceHeldTackleWithoutSocketKeysOrGameplayEvents()
    {
        Assert.That(Tackle, Is.Not.Null);
        Assert.That(Tackle.length, Is.EqualTo(.6f).Within(.001f));
        Assert.That(Tackle.isLooping, Is.False, "Entry plays once; an indefinite slide holds the final pose.");
        Assert.That(AnimationUtility.GetAnimationEvents(Tackle), Is.Empty);
        foreach (var binding in AnimationUtility.GetCurveBindings(Tackle))
        {
            Assert.That(binding.path, Does.Not.Contain("Socket").And.Not.Contain("Fire Point"));
            Assert.That(binding.path, Does.StartWith("metarig/"), "Only presentation bones may be keyed.");
        }
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlaceholderPlayerAnimationBuilder.ControllerPath);
        Assert.That(controller.layers[0].stateMachine.states.Single(s => s.state.name == "Slide").state.motion, Is.SameAs(Tackle));
    }

    [Test]
    public void FullClip_StaysAboveStandingContactPlaneAndHoldsAnAsymmetricBackwardLean()
    {
        var rig = Rig(); var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>(); var mesh = Track(new Mesh());
        PlaceholderPlayerAnimationBuilder.Clips().Single(c => c.name == "Idle").SampleAnimation(rig, 0f);
        float floor = Lowest(skin, mesh), standingHip = Bone(rig, "spine").position.y;
        Vector3 rootPosition = rig.transform.position;
        for (int frame = 0; frame <= 90; frame++)
        {
            float time = frame / 120f;
            Tackle.SampleAnimation(rig, time);
            Assert.That(Lowest(skin, mesh), Is.GreaterThanOrEqualTo(floor - .018f), "Body/floor penetration at " + time);
            Assert.That(rig.transform.position, Is.EqualTo(rootPosition));
            if (time < SlideTackleAnimationAuthoring.EntryTime) continue;
            Assert.That(standingHip - Bone(rig, "spine").position.y, Is.GreaterThan(.6f));
            Assert.That(Knee(rig, "R"), Is.GreaterThan(150f), "Leading knee must not fold or invert.");
            Assert.That(Knee(rig, "L"), Is.InRange(20f, 95f), "Tucked knee must remain anatomically folded.");
            Assert.That(Bone(rig, "foot.R").position.z - Bone(rig, "spine").position.z, Is.GreaterThan(.8f));
            Assert.That(Bone(rig, "spine.003").position.z - Bone(rig, "spine").position.z, Is.LessThan(-.06f));
        }
        Tackle.SampleAnimation(rig, .6f);
        Quaternion[] held = rig.GetComponentsInChildren<Transform>().Select(t => t.localRotation).ToArray();
        Tackle.SampleAnimation(rig, 8f);
        Assert.That(rig.GetComponentsInChildren<Transform>().Select((t, i) => Quaternion.Angle(t.localRotation, held[i])).Max(), Is.LessThan(.01f));
    }

    [TestCase("Locomotion", "", 0f)]
    [TestCase("Locomotion", "Aim", .25f)]
    [TestCase("Locomotion", "Fire", .5f)]
    [TestCase("Locomotion", "Slash", .75f)]
    [TestCase("Locomotion", "Flame", 0f)]
    [TestCase("Crouch", "", .5f)]
    [TestCase("Crouch", "Aim", .75f)]
    [TestCase("Crouch", "Fire", 0f)]
    [TestCase("Crouch", "Slash", .25f)]
    [TestCase("Crouch", "Flame", .5f)]
    public void ActualController_EntryAndExitDoNotInvertJointsOrPushTheSkinBelowTheFloor(string exit, string action, float phase)
    {
        var rig = Rig(); var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>(); var mesh = Track(new Mesh());
        PlaceholderPlayerAnimationBuilder.Clips().Single(c => c.name == "Idle").SampleAnimation(rig, 0f);
        int[] boots = BootVertices(skin); float floor = LowestBoot(skin, mesh, boots);
        var graph = PlayableGraph.Create("Slide entry/exit geometry regression");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var controller = AnimatorControllerPlayable.Create(graph, AssetDatabase.LoadAssetAtPath<AnimatorController>(PlaceholderPlayerAnimationBuilder.ControllerPath));
            AnimationPlayableOutput.Create(graph, "Rig", rig.GetComponent<Animator>()).SetSourcePlayable(controller);
            graph.Play(); controller.SetLayerWeight(1, action == "" ? 0f : 1f); controller.SetLayerWeight(2, 0f);
            controller.SetFloat("MoveY", 1f); controller.SetFloat("Speed", 1f); controller.SetFloat("LocomotionRate", 1f);
            controller.Play(Animator.StringToHash("Base Layer.Locomotion"), 0, phase);
            if (action != "") controller.Play(Animator.StringToHash("Upper Body." + action), 1, .25f);
            graph.Evaluate(0f);
            Transform[] joints = new[] { "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R" }.Select(n => Bone(rig, n)).ToArray();
            Quaternion[] previous = joints.Select(t => t.localRotation).ToArray();
            controller.CrossFadeInFixedTime(Animator.StringToHash("Base Layer.Slide"), .09f, 0, 0f);
            for (int frame = 0; frame < 120; frame++)
            {
                if (frame == 60)
                {
                    if (exit == "Crouch") { controller.SetFloat("Speed", .3f); controller.SetFloat("LocomotionRate", .35f); }
                    controller.CrossFadeInFixedTime(Animator.StringToHash("Base Layer.SlideTo" + (exit == "Crouch" ? "Crouch" : "Locomotion")), .015f, 0, 0f);
                }
                if (frame == 84) controller.CrossFadeInFixedTime(Animator.StringToHash("Base Layer." + exit), .035f, 0, 0f);
                graph.Evaluate(1f / 120f);
                Assert.That(LowestBoot(skin, mesh, boots), Is.GreaterThanOrEqualTo(floor - .035f), "Boot/floor clearance at frame " + frame);
                for (int joint = 0; joint < joints.Length; joint++)
                {
                    Assert.That(Quaternion.Angle(previous[joint], joints[joint].localRotation), Is.LessThan(65f), "Joint discontinuity: " + joints[joint].name);
                    previous[joint] = joints[joint].localRotation;
                }
                if (frame == 36 || frame == 54)
                    TestContext.WriteLine($"Slide frame={frame} knee={Knee(rig, "R"):F2} state={controller.GetCurrentAnimatorStateInfo(0).fullPathHash} phase={controller.GetCurrentAnimatorStateInfo(0).normalizedTime:F3} transition={controller.IsInTransition(0)}");
                if (frame is > 53 and < 60)
                {
                    Assert.That(Knee(rig, "R"), Is.GreaterThan(150f));
                    Assert.That(Knee(rig, "L"), Is.LessThan(95f));
                }
            }
        }
        finally { graph.Destroy(); }
    }
}
