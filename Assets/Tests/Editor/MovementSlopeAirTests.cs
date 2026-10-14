using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MovementSlopeAirTests
{
    private readonly List<Object> _cleanup = new();
    private PlayerMovement _movement;
    private PlayerStats _stats;
    private Rigidbody _body;
    private GameObject _floor;
    private float _oldStep;
    private SimulationMode _oldSimulation;
    private float WalkSpeed => _stats.GetMoveSpeed() * DebugSpeedTool.LocomotionScale;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameplayPause.Reset();
        _oldStep = Time.fixedDeltaTime;
        _oldSimulation = Physics.simulationMode;
        Time.fixedDeltaTime = .02f;
        Time.timeScale = 1f;
        Physics.simulationMode = SimulationMode.Script;
        var actor = Track(new GameObject("Slope/air motor fixture"));
        actor.transform.localScale = Vector3.one * 1.2f;
        actor.transform.position = Vector3.up * 1.21f;
        var capsule = actor.AddComponent<CapsuleCollider>();
        capsule.radius = .5f;
        capsule.height = 2f;
        _body = actor.AddComponent<Rigidbody>();
        _body.useGravity = false;
        _stats = actor.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (string stat in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed" })
            definitions.Add(AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{stat}.asset"));
        Set(_stats, "_statDefinitions", definitions);
        Call(_stats, "Awake");
        _movement = actor.AddComponent<PlayerMovement>();
        Call(_movement, "Awake");
        Set(_movement, "_cameraTransform", Track(new GameObject("Movement basis")).transform);
        Set(_movement, "_airFrictionMultiplier", .4f);
        Set(_movement, "_slideFrictionMultiplier", .4f);
        _movement.RefreshPassiveResources();
        _floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        _floor.name = "Support";
        SetSlope(0f);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        Time.fixedDeltaTime = _oldStep;
        Physics.simulationMode = _oldSimulation;
        Time.timeScale = 1f;
        GameplayPause.Reset();
    }

    [TestCase(10f, 1f, 0f, true)]
    [TestCase(30f, 1f, 0f, true)]
    [TestCase(45f, 1f, 0f, true)]
    [TestCase(30f, -1f, 0f, false)]
    [TestCase(30f, 0f, 1f, false)]
    [TestCase(0f, 1f, 0f, false)]
    [TestCase(1f, 1f, 0f, false)]
    public void WalkingEntry_UsesActualDownhillTravelOnWalkableSupport(float slope, float x, float z, bool slide)
    {
        SetSlope(slope);
        _body.linearVelocity = new Vector3(x, 0f, z) * WalkSpeed;
        // Opposing input must not decide the slope direction; actual momentum does.
        Set(_movement, "_moveDirectionWorld", new Vector3(-x, 0f, -z));
        Set(_movement, "_crouchHeld", true);
        int starts = 0;
        _movement.OnSlideStarted += () => starts++;
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.True);
        Assert.That(_movement.IsSliding, Is.EqualTo(slide));
        Assert.That(_movement.IsCrouching, Is.EqualTo(!slide));
        Assert.That(starts, Is.EqualTo(slide ? 1 : 0));
    }

    [TestCase(0f)] [TestCase(-30f)]
    public void FlatAndUphill_RetainExistingHighSpeedEntry(float slope)
    {
        SetSlope(slope);
        _body.linearVelocity = Vector3.right * WalkSpeed * 1.5f;
        Set(_movement, "_crouchHeld", true);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsSliding, Is.True);
    }

    [TestCase(1f)] [TestCase(.5f)]
    public void DownhillEntry_UsesCurrentWalkingTargetWithOnlyRoundingTolerance(float slow)
    {
        SetSlope(30f);
        _movement.ApplySlow(slow, 2f);
        _body.linearVelocity = Vector3.right * (WalkSpeed * slow - .02f);
        Assert.That((bool)Call(_movement, "CanStartSlide"), Is.False);
        _body.linearVelocity = Vector3.right * (WalkSpeed * slow);
        Assert.That((bool)Call(_movement, "CanStartSlide"), Is.True);
    }

    [Test]
    public void FinishedShallowSlide_HeldCrouchDoesNotRepeatedlyRestartAtExitSpeed()
    {
        SetSlope(6f);
        _body.linearVelocity = Vector3.right * WalkSpeed;
        Set(_movement, "_crouchHeld", true);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsSliding, Is.True);
        int starts = 0;
        _movement.OnSlideStarted += () => starts++;
        _body.linearVelocity = Vector3.right * 1.99f;
        Set(_movement, "_moveDirectionWorld", Vector3.right);
        for (int i = 0; i < 200; i++) Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(starts, Is.Zero);
    }

    [TestCase(0f)] [TestCase(.1f)] [TestCase(1.99f)]
    public void DownhillAtRestOrBelowExitSpeed_DoesNotPulseSlide(float speed)
    {
        SetSlope(30f);
        _body.linearVelocity = Vector3.right * speed;
        Set(_movement, "_crouchHeld", true);
        int starts = 0;
        _movement.OnSlideStarted += () => starts++;
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(starts, Is.Zero);
    }

    [TestCase(0f)] [TestCase(-30f)]
    public void FallingVelocity_CannotMakeFlatOrUphillTravelEligible(float slope)
    {
        SetSlope(slope);
        _body.linearVelocity = Vector3.right * WalkSpeed + Vector3.down * 20f;
        Set(_movement, "_crouchHeld", true);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.True);
        Assert.That(_movement.IsSliding, Is.False);
    }

    [TestCase(-7f)] [TestCase(7f)]
    public void AirborneHeldCrouch_NeverStartsSlideEvenWithStaleSupport(float verticalSpeed)
    {
        _body.position = Vector3.up * 10f;
        Physics.SyncTransforms();
        Set(_movement, "_isGrounded", true);
        Set(_movement, "_groundNormal", Quaternion.Euler(0f, 0f, -30f) * Vector3.up);
        Set(_movement, "_crouchHeld", true);
        _body.linearVelocity = Vector3.right * WalkSpeed * 2f + Vector3.up * verticalSpeed;
        int starts = 0;
        _movement.OnSlideStarted += () => starts++;
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(starts, Is.Zero);
        Assert.That(_body.linearVelocity.y, Is.EqualTo(verticalSpeed));
    }

    public static IEnumerable<TestCaseData> AirMotorCases()
    {
        foreach (string grace in new[] { "ordinary", "postDash", "knockback", "coastingCharge" })
            foreach (int input in new[] { 0, 1, -1, 2 })
                foreach (float vertical in new[] { -5f, 5f })
                    yield return new TestCaseData(grace, input, vertical);
    }

    [TestCaseSource(nameof(AirMotorCases))]
    public void AirCrouch_HasExactlyTheNormalAirMotorIncludingDragAndControl(string grace, int input, float vertical)
    {
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_moveDirectionWorld", input == 2 ? Vector3.forward : Vector3.right * input);
        if (grace == "postDash") Set(_movement, "_postDashFrictionTimer", .15f);
        if (grace == "knockback") Set(_movement, "_knockbackTimer", .2f);
        if (grace == "coastingCharge") Set(_movement, "_momentumPreservingStunTimer", .5f);
        Vector3 initial = Vector3.right * WalkSpeed + Vector3.up * vertical;
        _body.linearVelocity = initial;
        Set(_movement, "_isCrouching", false);
        for (int i = 0; i < 10; i++) Call(_movement, "HandlePlanarMotor", grace != "coastingCharge");
        Vector3 normal = _body.linearVelocity;
        _body.linearVelocity = initial;
        Set(_movement, "_isCrouching", true);
        for (int i = 0; i < 10; i++) Call(_movement, "HandlePlanarMotor", grace != "coastingCharge");
        Assert.That(Vector3.Distance(_body.linearVelocity, normal), Is.LessThan(.00001f));
        Assert.That(_body.linearVelocity.y, Is.EqualTo(vertical));
        if (grace == "ordinary" && input == 0)
            Assert.That(_movement.PlanarSpeed, Is.EqualTo(WalkSpeed - .8f).Within(.0001f), "Existing air drag still applies.");
    }

    [Test]
    public void SlideLeavingSupport_EndsOnceAndDoesNotChangeVelocityDuringTransition()
    {
        Set(_movement, "_isGrounded", true);
        Set(_movement, "_crouchHeld", true);
        _body.linearVelocity = Vector3.right * WalkSpeed * 1.5f;
        Call(_movement, "TryStartCrouchOrSlide");
        Assert.That(_movement.IsSliding, Is.True);
        _body.position = Vector3.up * 10f;
        Physics.SyncTransforms();
        Vector3 departure = new Vector3(WalkSpeed, -2f, 1f);
        _body.linearVelocity = departure;
        int ends = 0;
        _movement.OnSlideEnded += () => ends++;
        Call(_movement, "UpdateGroundedState");
        Call(_movement, "UpdateGroundedState");
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(_movement.IsCrouching, Is.True);
        Assert.That(_body.linearVelocity, Is.EqualTo(departure));
        Assert.That(ends, Is.EqualTo(1));
    }

    [TestCase(0f, 0f, false)] [TestCase(0f, .5f, false)]
    [TestCase(0f, 1f, false)] [TestCase(0f, 1.6f, true)]
    [TestCase(30f, 1f, true)] [TestCase(-30f, 1f, false)]
    public void LandingWithCrouchHeld_RechecksEntryAndOtherwiseCrouches(float slope, float speedFactor, bool slide)
    {
        SetSlope(slope);
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_crouchHeld", true);
        _body.linearVelocity = Vector3.right * WalkSpeed * speedFactor + Vector3.down;
        int lands = 0, starts = 0;
        _movement.OnLanded += () => lands++;
        _movement.OnSlideStarted += () => starts++;
        Call(_movement, "FixedUpdate");
        Assert.That(lands, Is.EqualTo(1));
        Assert.That(starts, Is.EqualTo(slide ? 1 : 0));
        Assert.That(_movement.IsSliding, Is.EqualTo(slide));
        Assert.That(_movement.IsCrouching, Is.EqualTo(!slide));
        Assert.That(_body.linearVelocity.y, Is.EqualTo(-1f));
        if (speedFactor > 0f) Assert.That(_movement.PlanarSpeed, Is.GreaterThan(0f));
    }

    [Test]
    public void BufferedJumpOnLanding_DoesNotPulseGroundSlide()
    {
        SetSlope(30f);
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_crouchHeld", true);
        Set(_movement, "_jumpRequestTime", Time.unscaledTime);
        _body.linearVelocity = Vector3.right * WalkSpeed + Vector3.down;
        int starts = 0;
        _movement.OnSlideStarted += () => starts++;
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.IsGroundedOnSurface, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
        Assert.That(starts, Is.Zero);
        Assert.That(_body.linearVelocity.y, Is.GreaterThan(0f));
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(WalkSpeed - .08f).Within(.0001f));
    }

    [Test]
    public void GroundedCrouch_StillUsesItsAuthoredSpeedAndAcceleration()
    {
        Set(_movement, "_isCrouching", true);
        Set(_movement, "_moveDirectionWorld", Vector3.forward);
        Call(_movement, "HandlePlanarMotor", true);
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(.3f).Within(.0001f));
        for (int i = 0; i < 100; i++) Call(_movement, "HandlePlanarMotor", true);
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(WalkSpeed * .5f).Within(.0001f));
    }

    private void SetSlope(float angle)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, -angle);
        _floor.transform.rotation = rotation;
        _floor.transform.localScale = new Vector3(100f, .2f, 100f);
        _floor.transform.position = rotation * Vector3.down * .1f;
        _body.position = Vector3.up * (.6f + .6f / Mathf.Cos(angle * Mathf.Deg2Rad) + .01f);
        Physics.SyncTransforms();
        _movement.SynchronizeGroundedPlacement();
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
