using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PlayerDirectionalDashTests
{
    private const string SetupKey = "PlayerDirectionalDashTests.SceneSetup";
    private const float FrameTime = 1f / 60f;
    private readonly List<Object> _cleanup = new();
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    private sealed class DashPosePump : MonoBehaviour
    {
        public PlayerAnimationDriver Driver;
        public PlayerMovement Movement;
        public Rigidbody Body;
        public Transform Hand;
        public Vector3 Velocity;
        public bool Dashing, Paused;
        private void Update() => Evaluate();
        public void Evaluate()
        {
            Body.linearVelocity = Velocity;
            SetField(Movement, "_isDashing", Dashing);
            Driver.EvaluatePoseForWeapons(Paused ? 0f : FrameTime, Hand.position + Vector3.forward * 30f);
        }
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
        string saved = SessionState.GetString(SetupKey, "");
        if (string.IsNullOrEmpty(saved)) yield break;
        SessionState.EraseString(SetupKey);
        SavedSetup setup = JsonUtility.FromJson<SavedSetup>(saved);
        if (setup?.Scenes?.Length > 0 && setup.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(scene => new SceneSetup
            { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
    }

    [TestCase(0f, 1f)]
    [TestCase(0f, -1f)]
    [TestCase(-1f, 0f)]
    [TestCase(1f, 0f)]
    [TestCase(-1f, 1f)]
    [TestCase(1f, 1f)]
    [TestCase(-1f, -1f)]
    [TestCase(1f, -1f)]
    public void DashDirections_PreserveAuthoredBackstepAndRedirectOnlyForwardOrSide(float x, float z)
    {
        GameObject actor = Track(Object.Instantiate(PlayerPrefab()));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        actor.transform.SetPositionAndRotation(new Vector3(1.2f, .3f, -2.5f), Quaternion.Euler(0f, 73f, 0f));
        PlayerAnimationDriver driver = actor.GetComponent<PlayerAnimationDriver>();
        typeof(PlayerAnimationDriver).GetMethod("ResolveReferences", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(driver, null);
        GameObject visual = driver.RigAnimator.gameObject;
        driver.RigAnimator.runtimeAnimatorController = null;
        bool backward = z < 0f;
        AnimationClip dash = PlaceholderPlayerAnimationBuilder.Clips().Single(clip => clip.name == (backward ? "DashBackward" : "Dash"));
        dash.SampleAnimation(visual, dash.length * .25f);
        Transform hips = Bone(visual, "spine"), chest = Bone(visual, "spine.003");
        Transform leftThigh = Bone(visual, "thigh.L"), leftShin = Bone(visual, "shin.L");
        Transform rightThigh = Bone(visual, "thigh.R"), rightShin = Bone(visual, "shin.R");
        Transform[] bones = visual.GetComponentsInChildren<Transform>();
        var positions = bones.ToDictionary(bone => bone, bone => bone.position);
        var rotations = bones.ToDictionary(bone => bone, bone => bone.localRotation);
        var lengths = bones.Where(bone => bone.parent != null).ToDictionary(bone => bone,
            bone => Vector3.Distance(bone.position, bone.parent.position));
        Quaternion rootRotation = actor.transform.rotation;
        Vector3 rootPosition = actor.transform.position;
        Vector3 direction = new Vector3(x, 0f, z).normalized;
        MethodInfo redirect = typeof(PlayerAnimationDriver).GetMethod("RedirectDashPose", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(redirect, Is.Not.Null);
        redirect.Invoke(driver, new object[] { direction, 1f });

        Vector3 torso = actor.transform.InverseTransformDirection(chest.position - hips.position);
        Vector3 planarLean = new Vector3(torso.x, 0f, torso.z);
        if (!backward)
            Assert.That(Vector3.Dot(planarLean.normalized, direction), Is.GreaterThan(.99f), "The forward/side chest lean must follow dash travel.");
        else
            Assert.That(Vector3.Angle(torso, Vector3.up), Is.LessThan(20f), "The backstep keeps an upright counterbalanced torso.");
        if (z > 0f)
        {
            float leftReach = actor.transform.InverseTransformDirection(leftShin.position - leftThigh.position).z;
            float rightReach = actor.transform.InverseTransformDirection(rightShin.position - rightThigh.position).z;
            Assert.That(leftReach * z, Is.GreaterThan(.05f), "Forward dash retains its authored leading leg.");
            Assert.That(rightReach * z, Is.LessThan(-.05f));
        }
        if (x != 0f && !backward)
        {
            Assert.That(actor.transform.InverseTransformDirection(leftShin.position - leftThigh.position).x, Is.LessThan(-.05f));
            Assert.That(actor.transform.InverseTransformDirection(rightShin.position - rightThigh.position).x, Is.GreaterThan(.05f));
        }
        foreach (string name in new[] { "shin.L", "shin.R", "foot.L", "foot.R" })
        {
            Transform joint = Bone(visual, name);
            Assert.That(Quaternion.Angle(joint.localRotation, rotations[joint]), Is.LessThan(.05f), name + " authored joint bend");
        }
        foreach (var pair in lengths)
            Assert.That(Vector3.Distance(pair.Key.position, pair.Key.parent.position), Is.EqualTo(pair.Value).Within(.00001f), pair.Key.name);
        Assert.That(actor.transform.position, Is.EqualTo(rootPosition));
        Assert.That(Quaternion.Angle(actor.transform.rotation, rootRotation), Is.LessThan(.001f));
        if (backward || x == 0f && z == 1f)
            foreach (Transform bone in bones)
            {
                Assert.That(Vector3.Distance(bone.position, positions[bone]), Is.LessThan(.00001f), "Retain the authored directional pose: " + bone.name);
                Assert.That(Quaternion.Angle(bone.localRotation, rotations[bone]), Is.LessThan(.05f), "Retain authored directional joint rotation: " + bone.name);
            }
    }

    [Test]
    public void AuthoredBackstep_HasLowFeetAndUprightTorsoThroughoutItsShortNonLoopingClip()
    {
        GameObject actor = Track(Object.Instantiate(PlayerPrefab()));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        GameObject visual = actor.GetComponent<PlayerAnimationDriver>().RigAnimator.gameObject;
        visual.GetComponent<Animator>().runtimeAnimatorController = null;
        var clips = PlaceholderPlayerAnimationBuilder.Clips().ToDictionary(clip => clip.name);
        AnimationClip backward = clips["DashBackward"];
        Assert.That(backward.length, Is.EqualTo(.3f).Within(.01f));
        Assert.That(backward.isLooping, Is.False);
        clips["Idle"].SampleAnimation(visual, 0f);
        Transform hips = Bone(visual, "spine"), chest = Bone(visual, "spine.003");
        Transform[] feet = { Bone(visual, "foot.L"), Bone(visual, "foot.R") };
        float[] standing = feet.Select(foot => actor.transform.InverseTransformPoint(foot.position).y).ToArray();
        for (int frame = 0; frame <= 9; frame++)
        {
            backward.SampleAnimation(visual, backward.length * frame / 9f);
            Assert.That(Vector3.Angle(chest.position - hips.position, actor.transform.up), Is.LessThan(20f), "Backstep torso at frame " + frame);
            for (int side = 0; side < feet.Length; side++)
                Assert.That(actor.transform.InverseTransformPoint(feet[side].position).y - standing[side], Is.LessThan(.16f),
                    "Backstep must keep feet low instead of folding into a reversed forward lunge: " + feet[side].name + " frame " + frame);
        }
    }

    [UnityTest]
    public IEnumerator RuntimeDash_TracksTravelAndRedirectsThenHoldsOnPauseAndStopAndBlendsOut()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled scene before running animation tests.");
        Assert.That(Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
            .All(index => !UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty), Is.True,
            "Save scene changes before running animation tests.");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return RunDashScenario();
    }

    private IEnumerator RunDashScenario()
    {
        GameObject actor = Track(Object.Instantiate(PlayerPrefab()));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        PlayerMovement movement = actor.GetComponent<PlayerMovement>();
        Rigidbody body = actor.GetComponent<Rigidbody>();
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        foreach (Collider collider in actor.GetComponentsInChildren<Collider>()) collider.enabled = false;
        PlayerAnimationDriver driver = actor.GetComponent<PlayerAnimationDriver>();
        Transform hips = Bone(driver.RigAnimator.gameObject, "spine"), chest = Bone(driver.RigAnimator.gameObject, "spine.003");
        Transform hand = Bone(driver.RigAnimator.gameObject, "hand.R");
        SetField(movement, "_isGrounded", true);
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        driver.SetManualWeaponOverride(new WeaponInstance { Data = data, State = WeaponState.Manual, CurrentAmmo = 100f });
        driver.enabled = true;
        DashPosePump pump = actor.AddComponent<DashPosePump>();
        pump.Driver = driver; pump.Movement = movement; pump.Body = body; pump.Hand = hand;
        for (int frame = 0; frame < 8; frame++) { yield return null; pump.Evaluate(); }
        yield return CheckBackstepRecovery(actor, driver, pump);
        Quaternion facing = actor.transform.rotation;
        Transform muzzle = actor.GetComponent<PlayerWeaponMountController>().MainFirePoint;
        Vector3 muzzleLocal = muzzle.localPosition;
        Quaternion muzzleRotation = muzzle.localRotation;

        pump.Dashing = true;
        pump.Velocity = Vector3.right * 12f;
        for (int frame = 0; frame < 9; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(movement.CurrentDashDirectionWorld, Is.EqualTo(Vector3.right));
        Assert.That(chest.position.x - hips.position.x, Is.GreaterThan(.04f), "The live graph must apply lateral dash lean.");
        Assert.That(Mathf.Abs(chest.position.z - hips.position.z), Is.LessThan(.025f), "A sideways dash must stop lunging forward.");
        AssertSocketsFinal(driver, actor.GetComponent<PlayerWearableSurfaceFollower>());

        pump.Velocity = Vector3.back * 12f;
        for (int frame = 0; frame < 9; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.DashBackward")),
            "Rear travel must select the authored backstep instead of reversing a forward lunge.");
        Assert.That(Vector3.Angle(chest.position - hips.position, actor.transform.up), Is.LessThan(20f));
        Assert.That(Quaternion.Angle(actor.transform.rotation, facing), Is.LessThan(.001f));
        Assert.That(driver.CurrentAimYaw, Is.EqualTo(0f).Within(1f), "The manual aim remains forward while the dash moves backward.");
        Assert.That(muzzle.localPosition, Is.EqualTo(muzzleLocal));
        Assert.That(Quaternion.Angle(muzzle.localRotation, muzzleRotation), Is.LessThan(.001f));

        pump.Paused = true;
        Time.timeScale = 0f;
        pump.Velocity = Vector3.left * 12f;
        Vector3 beforePause = chest.position - hips.position;
        float phaseBeforePause = BaseState(driver).normalizedTime;
        for (int frame = 0; frame < 4; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(Vector3.Distance(chest.position - hips.position, beforePause), Is.LessThan(.00002f), "Zero scaled time must freeze dash direction and pose.");
        Assert.That(BaseState(driver).normalizedTime, Is.EqualTo(phaseBeforePause).Within(.00001f));
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.DashBackward")));

        Time.timeScale = 1f;
        pump.Paused = false;
        pump.Velocity = Vector3.zero;
        for (int frame = 0; frame < 3; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.DashBackward")), "A collision stop keeps the selected backstep.");
        Assert.That(BaseState(driver).normalizedTime, Is.GreaterThan(phaseBeforePause), "A stop does not restart or freeze the playing backstep.");
        pump.Velocity = Vector3.right * 12f;
        for (int frame = 0; frame < 9; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.Dash")), "A mid-dash lateral redirect leaves the backstep state.");
        Assert.That(chest.position.x - hips.position.x, Is.GreaterThan(.04f));
        pump.Velocity = new Vector3(-1f, 0f, -1f).normalized * 12f;
        for (int frame = 0; frame < 9; frame++) { yield return null; pump.Evaluate(); }
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.DashBackward")), "Rear diagonals also use the low backstep.");
        pump.Dashing = false;
        pump.Velocity = Vector3.forward * 5f;
        for (int frame = 0; frame < 14; frame++) { yield return null; pump.Evaluate(); }
        Assert.That((bool)GetField(driver, "_hasDashDirection"), Is.False, "The outgoing dash pose must release after its controller transition.");
        Assert.That(movement.CurrentDashDirectionWorld, Is.EqualTo(Vector3.zero));
        Assert.That(chest.position.z - hips.position.z, Is.GreaterThan(0f), "Normal forward running recovers after dash.");
        AssertSocketsFinal(driver, actor.GetComponent<PlayerWearableSurfaceFollower>());

        movement.ApplyWeaponDash(Vector3.left, 13f, .2f);
        Assert.That(movement.CurrentDashDirectionWorld, Is.EqualTo(Vector3.left), "A weapon dash exposes its new direction before physics consumes the impulse.");

        SetField(movement, "_isDashing", false);
        PlayerStats stats = actor.GetComponent<PlayerStats>();
        stats.AddModifier(new StatModifier(StatType.DashCharges, 1f - stats.GetStat(StatType.DashCharges),
            StatUpgradeSource.TemporaryEffect, this));
        movement.RefreshPassiveResources();
        Assert.That(movement.MaxDashCharges, Is.EqualTo(1), "The input-dash scenario needs one unlocked charge.");
        SetField(movement, "_moveDirectionWorld", Vector3.forward);
        body.linearVelocity = Vector3.right * 9f;
        float boost = Mathf.Max(.1f, stats.GetStat(StatType.DashSpeed));
        typeof(PlayerMovement).GetMethod("TryDash", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        Assert.That(movement.IsDashing, Is.True);
        Assert.That(Vector3.Distance(movement.CurrentDashDirectionWorld, (Vector3.right * 9f + Vector3.forward * boost).normalized),
            Is.LessThan(.00001f), "An additive input dash follows the resulting momentum instead of only the input direction.");
    }

    private static IEnumerator CheckBackstepRecovery(GameObject actor, PlayerAnimationDriver driver, DashPosePump pump)
    {
        Transform visual = driver.RigAnimator.transform;
        Transform[] bones = { Bone(visual.gameObject, "spine"), Bone(visual.gameObject, "foot.L"), Bone(visual.gameObject, "foot.R") };
        Vector3[] previous = bones.Select(bone => actor.transform.InverseTransformPoint(bone.position)).ToArray();
        var report = new List<string> { "frame,dashing,phase,hipY,leftFootX,leftFootY,leftFootZ,rightFootX,rightFootY,rightFootZ,hipStep,maxFootStep" };
        for (int frame = 0; frame < 25; frame++)
        {
            pump.Dashing = frame < 15;
            pump.Velocity = pump.Dashing ? Vector3.back * 12f : Vector3.zero;
            yield return null;
            pump.Evaluate();
            Vector3[] current = bones.Select(bone => actor.transform.InverseTransformPoint(bone.position)).ToArray();
            float hipStep = Vector3.Distance(previous[0], current[0]);
            float footStep = Mathf.Max(Vector3.Distance(previous[1], current[1]), Vector3.Distance(previous[2], current[2]));
            Assert.That(hipStep, Is.LessThan(.08f), "Backstep hip must not snap at frame " + frame);
            Assert.That(footStep, Is.LessThan(.14f), "Backstep catch/recovery must blend without a foot snap at frame " + frame);
            AnimatorStateInfo state = BaseState(driver);
            if (frame == 14)
            {
                Assert.That(state.fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.DashBackward")));
                Assert.That(state.normalizedTime, Is.GreaterThan(.6f), "A normal 0.25-second dash reaches the authored catch phase.");
            }
            report.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4}",
                frame, pump.Dashing, state.normalizedTime, current[0].y, current[1].x, current[1].y, current[1].z,
                current[2].x, current[2].y, current[2].z, hipStep, footStep));
            previous = current;
        }
        Directory.CreateDirectory(".utmp/player-animation/backstep");
        File.WriteAllLines(".utmp/player-animation/backstep/runtime-motion.csv", report);
        Assert.That(BaseState(driver).fullPathHash, Is.EqualTo(Animator.StringToHash("Base Layer.Locomotion")));
        Assert.That((bool)GetField(driver, "_hasDashDirection"), Is.False, "Backstep releases after ten recovery frames.");
    }

    private static void AssertSocketsFinal(PlayerAnimationDriver driver, PlayerWearableSurfaceFollower follower)
    {
        Vector3[] positions = follower.Attachments.Select(attachment => attachment.Socket.position).ToArray();
        Quaternion[] rotations = follower.Attachments.Select(attachment => attachment.Socket.rotation).ToArray();
        int evaluations = driver.GraphEvaluationCount;
        driver.EvaluatePoseForWeapons(FrameTime, Vector3.forward * 30f);
        Assert.That(driver.GraphEvaluationCount, Is.EqualTo(evaluations), "Weapon reads cannot advance the pose twice.");
        follower.EvaluateAttachments();
        for (int i = 0; i < positions.Length; i++)
        {
            Assert.That(Vector3.Distance(follower.Attachments[i].Socket.position, positions[i]), Is.LessThan(.00002f));
            Assert.That(Quaternion.Angle(follower.Attachments[i].Socket.rotation, rotations[i]), Is.LessThan(.05f));
        }
    }

    private static GameObject PlayerPrefab() => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
    private static AnimatorStateInfo BaseState(PlayerAnimationDriver driver) =>
        ((AnimatorControllerPlayable)GetField(driver, "_controller")).GetCurrentAnimatorStateInfo(0);
    private static Transform Bone(GameObject visual, string name) => visual.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == name);
    private static object GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
}
