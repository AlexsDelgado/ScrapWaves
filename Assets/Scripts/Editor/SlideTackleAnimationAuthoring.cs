using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Presentation-only, in-place soccer tackle on the existing Generic rig.</summary>
public static class SlideTackleAnimationAuthoring
{
    public const string ClipPath = PlaceholderPlayerAnimationBuilder.Folder + "/SlideTackle.anim";
    public const float Duration = .6f;
    public const float EntryTime = .16f;
    public const float RecoveryTime = .18f;
    public const string RunRecoveryPath = PlaceholderPlayerAnimationBuilder.Folder + "/SlideToLocomotion.anim";
    public const string CrouchRecoveryPath = PlaceholderPlayerAnimationBuilder.Folder + "/SlideToCrouch.anim";
    private const int SampleRate = 60;
    private const float HipHeight = .11f;
    private static Quaternion SideHipRotation(float blend) => Quaternion.AngleAxis(55f * blend, Vector3.up) *
        Quaternion.AngleAxis(-48f * blend, Vector3.forward) * Quaternion.AngleAxis(-10f * blend, Vector3.right);

    [MenuItem("Tools/ScrapWaves/Author Soccer Slide Tackle")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author the slide in Edit Mode.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPlayerAnimationBuilder.ModelPath);
        var rig = Object.Instantiate(model);
        try
        {
            var animator = rig.GetComponent<Animator>();
            if (animator != null) animator.runtimeAnimatorController = null;
            var idle = AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath)
                .OfType<AnimationClip>().Single(c => c.name == "Idle");
            idle.SampleAnimation(rig, 0f);
            var bones = rig.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(s => s.bones).Distinct().ToArray();
            var rest = bones.ToDictionary(b => b, b => new Rest(b));
            Transform Bone(string name) => bones.Single(b => b.name == name);
            var hip = Bone("spine");
            Vector3 standingHip = hip.position;
            var feet = new[] { Bone("foot.R"), Bone("foot.L") };
            Vector3[] standingFeet = feet.Select(b => b.position).ToArray();
            Quaternion[] soleRotation = feet.Select(b => b.rotation).ToArray();
            var wrists = new[] { Bone("hand.R"), Bone("hand.L") };
            Vector3[] standingHands = wrists.Select(b => b.position).ToArray();
            Quaternion[] handRotation = wrists.Select(b => b.rotation).ToArray();
            Vector3 Bend(string first, string middle, Vector3 end) => Vector3.ProjectOnPlane(
                Bone(middle).position - Bone(first).position, end - Bone(first).position).normalized;
            Vector3 rightKneePole = Bend("thigh.R", "shin.R", standingFeet[0]);
            Vector3 leftKneePole = Bend("thigh.L", "shin.L", standingFeet[1]);
            Vector3[] elbowPoles = { Bend("upper_arm.R", "forearm.R", standingHands[0]), Bend("upper_arm.L", "forearm.L", standingHands[1]) };
            var tracks = bones.ToDictionary(b => b, _ => Enumerable.Range(0, 10).Select(__ => new List<Keyframe>()).ToArray());

            for (int frame = 0; frame <= SampleRate * Duration; frame++)
            {
                float time = frame / (float)SampleRate;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / EntryTime));
                foreach (var pair in rest) pair.Value.Restore(pair.Key);
                hip.position = Vector3.Lerp(standingHip, new Vector3(standingHip.x + .06f, HipHeight, standingHip.z + .055f), blend);
                hip.rotation = SideHipRotation(blend) * rest[hip].WorldRotation;
                Vector3 bodyForward = Quaternion.AngleAxis(55f * blend, Vector3.up) * Vector3.forward;
                // Counterlean keeps the original upper-body action clips clear of
                // the floor while the pelvis remains loaded onto the right hip.
                Bone("spine.001").rotation = Quaternion.AngleAxis(22f * blend, bodyForward) * Bone("spine.001").rotation;
                Bone("spine.002").rotation = Quaternion.AngleAxis(-6f * blend, bodyForward) * Bone("spine.002").rotation;

                var rightThigh = Bone("thigh.R");
                float reach = (Vector3.Distance(rightThigh.position, Bone("shin.R").position) +
                    Vector3.Distance(Bone("shin.R").position, feet[0].position)) * .985f;
                Vector3 settledHip = new(standingHip.x + .06f, HipHeight, standingHip.z + .055f);
                // Solve against the final thigh socket, not the moving entry height.
                Vector3 finalThigh = settledHip + SideHipRotation(1f) *
                    (rest[rightThigh].WorldPosition - standingHip);
                float vertical = .14f - finalThigh.y;
                float forward = Mathf.Sqrt(Mathf.Max(.01f, reach * reach - vertical * vertical - .04f * .04f));
                Vector3 lead = finalThigh + new Vector3(.04f, vertical, forward);
                Vector3 tucked = new(standingFeet[1].x - .10f, standingFeet[1].y + .002f, standingHip.z - .04f);
                // Clear the ground as the running feet sweep into the tackle. This
                // also leaves room for the phase-dependent locomotion crossfade.
                Vector3 entryLift = Vector3.up * (.14f * Mathf.Sin(Mathf.Clamp01(time / EntryTime) * Mathf.PI));
                Solve(Bone("thigh.R"), Bone("shin.R"), feet[0], Vector3.Lerp(standingFeet[0], lead, blend) + entryLift, Vector3.Slerp(rightKneePole, SideHipRotation(1f) * Vector3.up, blend));
                Solve(Bone("thigh.L"), Bone("shin.L"), feet[1], Vector3.Lerp(standingFeet[1], tucked, blend) + entryLift, Vector3.Slerp(leftKneePole, new Vector3(-.4f, .4f, 1f), blend));
                feet[0].rotation = Quaternion.AngleAxis(-48f * blend, Vector3.forward) * Quaternion.AngleAxis(-10f * blend, Vector3.right) * soleRotation[0];
                feet[1].rotation = soleRotation[1];

                // Open balancing arms remain available when the weapon layer is off.
                // The existing chest/arm mask and procedural aim replace these when armed.
                for (int side = 0; side < 2; side++)
                {
                    string suffix = side == 0 ? "R" : "L";
                    float sign = side == 0 ? 1f : -1f;
                    Vector3 hand = side == 0 ? new Vector3(standingHip.x + .64f, .16f, standingHip.z - .11f) :
                        new Vector3(standingHip.x + .15f, .68f, standingHip.z - .30f);
                    Solve(Bone("upper_arm." + suffix), Bone("forearm." + suffix), wrists[side],
                        Vector3.Lerp(standingHands[side], hand, blend), Vector3.Slerp(elbowPoles[side], new Vector3(sign, .2f, -.3f), blend));
                    wrists[side].rotation = handRotation[side];
                }
                foreach (var bone in bones)
                {
                    Vector3 p = bone.localPosition, s = bone.localScale; Quaternion q = bone.localRotation;
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                    for (int component = 0; component < values.Length; component++)
                        tracks[bone][component].Add(new Keyframe(time, values[component]));
                }
            }
            var clip = WriteClip(rig, bones, tracks, ClipPath, "Slide");
            var runRecovery = Recovery(rig, bones, clip, "Idle", RunRecoveryPath, "SlideToLocomotion");
            var crouchRecovery = Recovery(rig, bones, clip, "CrouchIdle", CrouchRecoveryPath, "SlideToCrouch");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlaceholderPlayerAnimationBuilder.ControllerPath);
            var machine = controller.layers[0].stateMachine;
            void Bind(string name, AnimationClip motion)
            {
                var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? machine.AddState(name);
                state.motion = motion; EditorUtility.SetDirty(state);
            }
            Bind("Slide", clip); Bind("SlideToLocomotion", runRecovery); Bind("SlideToCrouch", crouchRecovery);
            EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log("SIDE_SLIDE_AUTHORED: rotated pelvis, weight on right hip; .16s entry and .18s recoveries; original motor.");
        }
        finally { Object.DestroyImmediate(rig); }
    }

    private static AnimationClip Recovery(GameObject rig, Transform[] bones, AnimationClip tackle, string targetName, string path, string name)
    {
        tackle.SampleAnimation(rig, Duration);
        var start = bones.ToDictionary(b => b, b => new Rest(b));
        Transform Bone(string n) => bones.Single(b => b.name == n);
        Vector3 Pole(string side) => Vector3.ProjectOnPlane(Bone("shin." + side).position - Bone("thigh." + side).position,
            Bone("foot." + side).position - Bone("thigh." + side).position).normalized;
        Vector3[] poles = { Pole("R"), Pole("L") };
        var target = AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath).OfType<AnimationClip>().Single(c => c.name == targetName);
        target.SampleAnimation(rig, 0f);
        var end = bones.ToDictionary(b => b, b => new Rest(b));
        Vector3[] endPoles = { Pole("R"), Pole("L") };
        var tracks = bones.ToDictionary(b => b, _ => Enumerable.Range(0, 10).Select(__ => new List<Keyframe>()).ToArray());
        int frames = Mathf.CeilToInt(RecoveryTime * SampleRate);
        for (int frame = 0; frame <= frames; frame++)
        {
            float phase = frame / (float)frames, blend = Mathf.SmoothStep(0f, 1f, phase), time = phase * RecoveryTime;
            foreach (var bone in bones) start[bone].BlendTo(bone, end[bone], blend);
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "R" : "L";
                var foot = Bone("foot." + suffix);
                Vector3 point = Vector3.Lerp(start[foot].WorldPosition, end[foot].WorldPosition, blend);
                // Bring the extended boot under the pelvis above the contact plane.
                if (side == 0) point.y += .045f * Mathf.Sin(phase * Mathf.PI);
                Solve(Bone("thigh." + suffix), Bone("shin." + suffix), foot, point, Vector3.Slerp(poles[side], endPoles[side], blend));
                foot.rotation = Quaternion.Slerp(start[foot].WorldRotation, end[foot].WorldRotation, blend);
            }
            foreach (var bone in bones)
            {
                Vector3 p = bone.localPosition, s = bone.localScale; Quaternion q = bone.localRotation;
                float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                for (int component = 0; component < values.Length; component++) tracks[bone][component].Add(new Keyframe(time, values[component]));
            }
        }
        return WriteClip(rig, bones, tracks, path, name);
    }

    private static AnimationClip WriteClip(GameObject rig, Transform[] bones, Dictionary<Transform, List<Keyframe>[]> tracks, string path, string name)
    {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.name = name; clip.frameRate = 30f;
            string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            foreach (var bone in bones)
            for (int component = 0; component < properties.Length; component++)
            {
                var samples = tracks[bone][component];
                // Constant bind tracks and the held tail need endpoints, not repeated keys.
                // Quaternion components must retain identical sample times for continuity.
                var keys = component >= 3 && component <= 6 ? samples.ToArray() :
                    samples.Where((key, index) => index == 0 || index == samples.Count - 1 ||
                        key.value != samples[index - 1].value || key.value != samples[index + 1].value).ToArray();
                var curve = new AnimationCurve(keys);
                for (int key = 0; key < curve.length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                    AnimationUtility.CalculateTransformPath(bone, rig.transform), typeof(Transform), properties[component]), curve);
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; settings.loopBlend = false;
            settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
    }

    private static void Rotate(Transform bone, float degrees) =>
        bone.rotation = Quaternion.AngleAxis(degrees, Vector3.right) * bone.rotation;

    private static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        float first = Vector3.Distance(upper.position, lower.position), second = Vector3.Distance(lower.position, end.position);
        Vector3 delta = target - upper.position;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(first - second) + .001f, first + second - .001f);
        Vector3 direction = delta.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
        float along = (first * first - second * second + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, first * first - along * along));
        Vector3 knee = upper.position + direction * along + bend * height;
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, knee - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
    }

    private readonly struct Rest
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;
        public readonly Vector3 WorldPosition;
        public readonly Quaternion WorldRotation;
        public Rest(Transform bone) { _position = bone.localPosition; _rotation = bone.localRotation; _scale = bone.localScale; WorldPosition = bone.position; WorldRotation = bone.rotation; }
        public void Restore(Transform bone) { bone.localPosition = _position; bone.localRotation = _rotation; bone.localScale = _scale; }
        public void BlendTo(Transform bone, Rest end, float blend) { bone.localPosition = Vector3.Lerp(_position, end._position, blend); bone.localRotation = Quaternion.Slerp(_rotation, end._rotation, blend); bone.localScale = Vector3.Lerp(_scale, end._scale, blend); }
    }
}
