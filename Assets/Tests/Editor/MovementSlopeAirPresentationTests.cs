using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public sealed class MovementSlopeAirPresentationTests
{
    private static readonly Vector3 FixtureOrigin = Vector3.up * 1000f;
    private GameObject _root, _actor, _support;
    private PlayerMovement _movement;
    private PlayerAnimationDriver _driver;
    private PlayerStats _stats;
    private Rigidbody _body;
    private float _previousStep, _previousTimeScale;
    private SimulationMode _previousSimulation;
    private int _previousPauseLocks;
    private float WalkSpeed => _stats.GetMoveSpeed() * DebugSpeedTool.LocomotionScale;

    [SetUp]
    public void Setup()
    {
        // The isolated editor test runner owns this scene, as in the existing presentation fixtures.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _previousStep = Time.fixedDeltaTime;
        _previousTimeScale = Time.timeScale;
        _previousSimulation = Physics.simulationMode;
        _previousPauseLocks = GameplayPause.LockCount;
        Time.fixedDeltaTime = .02f;
        Time.timeScale = 1f;
        Physics.simulationMode = SimulationMode.Script;
        GameplayPause.Reset();

        _root = new GameObject("Slope/air prefab presentation fixture");
        _actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"), _root.transform);
        foreach (MonoBehaviour script in _actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        _actor.transform.localScale = Vector3.one * 1.2f;
        _actor.transform.rotation = Quaternion.identity;
        _stats = _actor.GetComponent<PlayerStats>();
        Call(_stats, "Awake");
        _movement = _actor.GetComponent<PlayerMovement>();
        _body = _actor.GetComponent<Rigidbody>();
        _body.useGravity = false;
        Call(_movement, "Awake");
        var cameraBasis = new GameObject("Movement basis");
        cameraBasis.transform.SetParent(_root.transform);
        Set(_movement, "_cameraTransform", cameraBasis.transform);
        _movement.RefreshPassiveResources();

        _driver = _actor.GetComponent<PlayerAnimationDriver>();
        Call(_driver, "ResolveReferences");
        Call(_driver, "Subscribe");
        Assert.That((bool)Call(_driver, "EnsureGraph"), Is.True, "Use the actual prefab controller and rig.");
        Get<PlayableGraph>(_driver, "_graph").Evaluate(0f);

        _support = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _support.name = "Presentation fixture support";
        _support.transform.SetParent(_root.transform);
        PlaceOnSupport(30f);
    }

    [TearDown]
    public void Cleanup()
    {
        if (_driver != null)
        {
            Call(_driver, "Unsubscribe");
            Call(_driver, "DestroyGraph");
        }
        if (_root != null) Object.DestroyImmediate(_root);
        Time.fixedDeltaTime = _previousStep;
        Time.timeScale = _previousTimeScale;
        Physics.simulationMode = _previousSimulation;
        GameplayPause.Reset();
        for (int i = 0; i < _previousPauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void DownhillWalkingEntry_SelectsAuthoredSlideAtReducedThreshold()
    {
        BeginDownhillSlide();
        Assert.That(_movement.PlanarSpeed, Is.LessThan((float)Call(_movement, "GetSlideStartSpeed")));
        AssertPose("Slide");
    }

    [Test]
    public void SlideLosesSupport_SelectsFallInsteadOfGroundRecoveryWhileCrouchHeld()
    {
        BeginDownhillSlide();
        AssertPose("Slide");
        _body.position = FixtureOrigin + Vector3.up * 10f;
        _body.linearVelocity = Vector3.right * WalkSpeed + Vector3.down * 2f;
        Physics.SyncTransforms();
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(_movement.PlanarSpeed, Is.GreaterThan(WalkSpeed - 1f));
        Assert.That(_body.linearVelocity.y, Is.EqualTo(-2f));
        AssertPose("Fall");
        Assert.That(Get<float>(_driver, "_slideRecoveryRemaining"), Is.Zero);
    }

    [Test]
    public void BufferedJumpFromSlide_SelectsJumpDespiteHeldCrouch()
    {
        BeginDownhillSlide();
        AssertPose("Slide");
        Set(_movement, "_jumpRequestTime", Time.unscaledTime);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(_body.linearVelocity.y, Is.GreaterThan(0f));
        Assert.That(_movement.PlanarSpeed, Is.GreaterThan(0f));
        AssertPose("Jump");
        Assert.That(Get<float>(_driver, "_slideRecoveryRemaining"), Is.Zero);
    }

    [TestCase(0f, "Land")]
    [TestCase(2f, "Crouch")]
    public void LowSpeedLandingWithHeldCrouch_UsesLandingOrCrouchWithoutSlidePulse(float speed, string initialPose)
    {
        Set(_movement, "_isGrounded", false);
        _body.linearVelocity = Vector3.down;
        AssertPose("Fall");
        PlaceOnSupport(0f);
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_crouchHeld", true);
        _body.linearVelocity = Vector3.right * speed + Vector3.down;
        int slideStarts = 0;
        _movement.OnSlideStarted += () => slideStarts++;
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.True);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(slideStarts, Is.Zero);
        AssertPose(initialPose);
        // Let the cosmetic landing timer expire; held crouch becomes the settled pose.
        Call(_driver, "UpdateLocomotion", .3f);
        AssertPose("Crouch");
    }

    private void BeginDownhillSlide()
    {
        _body.linearVelocity = Vector3.right * WalkSpeed;
        Set(_movement, "_crouchHeld", true);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.True);
        Assert.That(_movement.IsSliding, Is.True);
    }

    private void PlaceOnSupport(float angle)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, -angle);
        _support.transform.SetPositionAndRotation(FixtureOrigin + rotation * Vector3.down * .1f, rotation);
        _support.transform.localScale = new Vector3(40f, .2f, 40f);
        _body.position = FixtureOrigin + Vector3.up * (.6f + .6f / Mathf.Cos(angle * Mathf.Deg2Rad) + .01f);
        Physics.SyncTransforms();
        _movement.SynchronizeGroundedPlacement();
    }

    private void AssertPose(string name)
    {
        Vector3 velocity = _body.linearVelocity;
        int expected = Animator.StringToHash("Base Layer." + name);
        AnimatorControllerPlayable controller = Get<AnimatorControllerPlayable>(_driver, "_controller");
        Assert.That(controller.HasState(0, expected), Is.True, "The prefab must provide the selected pose.");
        Call(_driver, "UpdateLocomotion", .02f);
        Get<PlayableGraph>(_driver, "_graph").Evaluate(.02f);
        Assert.That(Get<int>(_driver, "_baseState"), Is.EqualTo(expected));
        bool selectedInGraph = controller.GetCurrentAnimatorStateInfo(0).fullPathHash == expected ||
            controller.GetNextAnimatorStateInfo(0).fullPathHash == expected;
        Assert.That(selectedInGraph, Is.True, "The prefab graph must enter or blend toward " + name + ".");
        Assert.That(_body.linearVelocity, Is.EqualTo(velocity), "Presentation must not change motor momentum.");
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
