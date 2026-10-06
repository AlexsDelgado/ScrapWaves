using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public sealed class PlayerMovementReliabilityTests
{
    private readonly List<Object> _cleanup = new();
    private PlayerMovement _movement;
    private PlayerStats _stats;
    private Rigidbody _body;
    private Keyboard _keyboard;
    private float _oldStep;
    private SimulationMode _oldSimulation;
    private InputSettings.BackgroundBehavior _oldBackground;
    private bool _oldPlayerUpdates;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameplayPause.Reset();
        _oldStep = Time.fixedDeltaTime;
        _oldSimulation = Physics.simulationMode;
        Time.fixedDeltaTime = .02f;
        Time.timeScale = 1f;
        Physics.simulationMode = SimulationMode.Script;
        _oldBackground = InputSystem.settings.backgroundBehavior;
        _oldPlayerUpdates = (bool)typeof(InputSettings).GetMethod("IsFeatureEnabled", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(InputSystem.settings, new object[] { "RUN_PLAYER_UPDATES_IN_EDIT_MODE" });
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
        GameObject actor = Track(new GameObject("Movement fixture"));
        actor.transform.position = new Vector3(0f, 1.2f, 0f);
        actor.transform.localScale = Vector3.one * 1.2f;
        CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
        capsule.radius = .5f; capsule.height = 2f;
        _body = actor.AddComponent<Rigidbody>();
        _body.useGravity = false;
        _stats = actor.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (string stat in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed" })
        {
            StatDefinition definition = AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{stat}.asset");
            Assert.That(definition, Is.Not.Null, stat);
            definitions.Add(definition);
        }
        Set(_stats, "_statDefinitions", definitions);
        Call(_stats, "Awake");
        _movement = actor.AddComponent<PlayerMovement>();
        Call(_movement, "Awake");
        GameObject camera = Track(new GameObject("Movement camera"));
        Set(_movement, "_cameraTransform", camera.transform);
        Set(_movement, "_baseMoveAcceleration", 75f);
        Set(_movement, "_baseFriction", 10f);
        Set(_movement, "_airFrictionMultiplier", .4f);
        Set(_movement, "_slideFrictionMultiplier", .4f);
        Set(_movement, "_postDashFrictionMultiplier", 3.5f);
        _movement.RefreshPassiveResources();
        Box("Floor", new Vector3(0f, -.5f, 0f), new Vector3(100f, 1f, 100f));
        Physics.SyncTransforms();
        Set(_movement, "_isGrounded", true);
    }

    [TearDown]
    public void Cleanup()
    {
        if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        Time.fixedDeltaTime = _oldStep;
        Physics.simulationMode = _oldSimulation;
        Time.timeScale = 1f;
        GameplayPause.Reset();
        InputSystem.settings.backgroundBehavior = _oldBackground;
        InputSystem.settings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", _oldPlayerUpdates);
    }

    [TestCase(.01f)] [TestCase(.02f)] [TestCase(1f / 30f)]
    public void NormalMotor_ReachesExactCapAndStopsWithoutReversal(float step)
    {
        Time.fixedDeltaTime = step;
        Set(_movement, "_moveDirectionWorld", Vector3.forward);
        for (int i = 0; i < 100; i++) Call(_movement, "HandleMovement");
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(6.8f).Within(.0001f));
        Set(_movement, "_moveDirectionWorld", Vector3.zero);
        float previous = _body.linearVelocity.z;
        for (int i = 0; i < 100; i++)
        {
            Call(_movement, "HandleMovement");
            Assert.That(_body.linearVelocity.z, Is.InRange(0f, previous + .0001f));
            previous = _body.linearVelocity.z;
        }
        Assert.That(_movement.PlanarSpeed, Is.Zero);
    }

    [TestCase(.005f)] [TestCase(.05f)] [TestCase(.1f)] [TestCase(.19f)]
    public void ReleaseBrake_StopsSmallResidualInsteadOfChangingSign(float residual)
    {
        _body.linearVelocity = Vector3.forward * residual;
        Call(_movement, "HandleMovement");
        Assert.That(_body.linearVelocity.z, Is.Zero);
    }

    [TestCase(25f)] [TestCase(35f)] [TestCase(45f)]
    public void ReleaseBrake_RecordsCandidateStoppingDistance(float braking)
    {
        Set(_movement, "_stoppingDeceleration", braking);
        _body.linearVelocity = Vector3.forward * 6.8f;
        float distance = 0f; int ticks = 0;
        while (_movement.PlanarSpeed > .0001f && ticks++ < 100)
        {
            Call(_movement, "HandleMovement");
            distance += _movement.PlanarSpeed * Time.fixedDeltaTime;
        }
        TestContext.WriteLine($"braking={braking}; stopSeconds={ticks * Time.fixedDeltaTime:F3}; distance={distance:F4}");
        Assert.That(distance, Is.LessThan(1f));
        Assert.That(_movement.PlanarSpeed, Is.Zero);
    }

    [Test]
    public void Reversal_HasIndependentRateAndDiagonalHasSameCap()
    {
        _body.linearVelocity = Vector3.forward * 6.8f;
        Set(_movement, "_moveDirectionWorld", Vector3.back);
        Call(_movement, "HandleMovement");
        Assert.That(_body.linearVelocity.z, Is.EqualTo(5f).Within(.0001f));
        Set(_movement, "_moveDirectionWorld", new Vector3(1f, 0f, 1f).normalized);
        for (int i = 0; i < 100; i++) Call(_movement, "HandleMovement");
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(6.8f).Within(.0001f));
    }

    public static IEnumerable<TestCaseData> DashCases()
    {
        foreach (float speed in new[] { 6.8f, 13.6f, 27.2f })
            foreach (Vector2 input in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                yield return new TestCaseData(speed, input.x, input.y);
    }

    [TestCaseSource(nameof(DashCases))]
    public void Dash_IsIntentAlignedAtEveryBuffSpeed(float moveSpeed, float x, float z)
    {
        _stats.AddModifier(new StatModifier(StatType.MovementSpeed, moveSpeed - 6.8f, StatUpgradeSource.TemporaryEffect, this));
        Vector3 direction = new Vector3(x, 0f, z).normalized;
        Set(_movement, "_moveDirectionWorld", direction);
        _body.linearVelocity = new Vector3(0f, 3f, moveSpeed + .325f);
        Call(_movement, "TryDash");
        Vector3 planar = Vector3.ProjectOnPlane(_body.linearVelocity, Vector3.up);
        Assert.That(Vector3.Angle(planar, direction), Is.LessThan(.01f));
        Assert.That(planar.magnitude, Is.EqualTo(moveSpeed + 10f).Within(.0001f));
        Assert.That(_body.linearVelocity.y, Is.EqualTo(3f));
        Assert.That(_movement.CurrentDashCharges, Is.Zero);
    }

    [Test]
    public void DashAndExpiredKnockback_RecoveryIsBounded()
    {
        _body.linearVelocity = Vector3.forward * 16.8f;
        Set(_movement, "_isDashing", true);
        Set(_movement, "_dashTimer", .02f);
        for (int i = 0; i < 50; i++)
        {
            float before = _movement.PlanarSpeed;
            Call(_movement, "FixedUpdate");
            Assert.That(before - _movement.PlanarSpeed, Is.InRange(-.0001f, .7001f));
        }
        _body.linearVelocity = Vector3.forward * 20f;
        Set(_movement, "_knockbackTimer", Time.fixedDeltaTime);
        Call(_movement, "FixedUpdate");
        Assert.That(_movement.PlanarSpeed, Is.GreaterThan(19f));
        float last = _movement.PlanarSpeed;
        Call(_movement, "FixedUpdate");
        Assert.That(last - _movement.PlanarSpeed, Is.EqualTo(.7f).Within(.0001f));
    }

    [Test]
    public void WeaponDashAndSlow_RetainResourceAndSpeedSemantics()
    {
        _movement.ApplySlow(.5f, 2f);
        Set(_movement, "_moveDirectionWorld", Vector3.forward);
        for (int i = 0; i < 30; i++) Call(_movement, "HandleMovement");
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(3.4f).Within(.0001f));
        int charges = _movement.CurrentDashCharges;
        _movement.ApplyWeaponDash(Vector3.left, 13f, .2f);
        Assert.That(_movement.CurrentDashCharges, Is.EqualTo(charges));
        Assert.That(_body.linearVelocity.x, Is.EqualTo(-13f).Within(.0001f));
        Set(_movement, "_isDashing", false);
        Call(_movement, "TryDash");
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(16.8f).Within(.0001f), "Input dash still bypasses slow.");
    }

    [Test]
    public void NonFreezingWeaponCharge_RetainsAuthoredCoastingFriction()
    {
        _body.linearVelocity = Vector3.forward * 6.8f;
        int events = 0; _movement.OnStunned += () => events++;
        _movement.ApplyMomentumPreservingStun(1f, triggerStunFeedback: false, freezePlanarVelocity: false);
        Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity.z, Is.EqualTo(6.6f).Within(.0001f));
        Assert.That(events, Is.Zero);
        Assert.That(_movement.IsStunned, Is.False);
    }

    [TestCase(false)] [TestCase(true)]
    public void CtrlRelease_ExitsCrouchDuringStunOrLaunch(bool launch)
    {
        Call(_movement, "StartCrouch");
        Set(_movement, "_crouchHeld", true);
        if (launch) _movement.LaunchWithVelocity(Vector3.forward * 10f, 2f);
        else _movement.ApplyStun(.2f);
        _keyboard = InputSystem.AddDevice<Keyboard>();
        Keys(); Call(_movement, "Update");
        Assert.That(_movement.IsCrouching, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
    }

    [Test]
    public void HitStop_BuffersJumpButMenuPauseDiscardsIt()
    {
        _keyboard = InputSystem.AddDevice<Keyboard>();
        Time.timeScale = 0f;
        Keys(Key.Space); Call(_movement, "Update");
        Assert.That(_body.linearVelocity.y, Is.Zero);
        Keys(); Call(_movement, "Update");
        Time.timeScale = 1f; Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity.y, Is.EqualTo(Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * 3f)).Within(.0001f));
        _body.linearVelocity = Vector3.zero;
        Time.timeScale = 0f; GameplayPause.Push();
        Keys(Key.Space); Call(_movement, "Update");
        Assert.That(Get<float>(_movement, "_jumpRequestTime"), Is.EqualTo(float.NegativeInfinity));
    }

    [Test]
    public void Jump_BufferExpiresAndCoyoteConsumesOnlyOnce()
    {
        Set(_movement, "_jumpRequestTime", Time.unscaledTime - .2f);
        Call(_movement, "ConsumeBufferedActions");
        Assert.That(_body.linearVelocity.y, Is.Zero);
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_lastGroundedTime", Time.time - .08f);
        Assert.That((bool)Call(_movement, "TryJump"), Is.True);
        Assert.That((bool)Call(_movement, "TryJump"), Is.False);
        Assert.That((bool)Call(_movement, "IsGrounded"), Is.False, "Ascending jump cannot refresh ground eligibility.");
    }

    [Test]
    public void HitStopDash_RetainsPressDirectionAndExpiredRequestsDoNothing()
    {
        _keyboard = InputSystem.AddDevice<Keyboard>();
        Time.timeScale = 0f;
        Keys(Key.S, Key.LeftShift); Call(_movement, "Update");
        Assert.That(_movement.CurrentDashCharges, Is.EqualTo(1));
        Keys(Key.W); Call(_movement, "Update");
        Time.timeScale = 1f; Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity.z, Is.EqualTo(-16.8f).Within(.0001f));
        _movement.RefreshPassiveResources(); Set(_movement, "_isDashing", false);
        Set(_movement, "_dashRequestTime", Time.unscaledTime - .2f);
        Call(_movement, "ConsumeBufferedActions");
        Assert.That(_movement.CurrentDashCharges, Is.EqualTo(1));
    }

    [Test]
    public void PrelandingBuffer_ConsumesAtSupportAndAirJumpSetsConsistentVerticalSpeed()
    {
        Set(_movement, "_isGrounded", false);
        Set(_movement, "_jumpRequestTime", Time.unscaledTime - .05f);
        Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity.y, Is.GreaterThan(10f));
        Set(_movement, "_remainingAirJumps", 1);
        _body.linearVelocity = new Vector3(2f, 6f, 3f);
        Assert.That((bool)Call(_movement, "TryJump"), Is.True);
        Assert.That(_body.linearVelocity.y, Is.EqualTo(Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * 3f)).Within(.0001f));
        Assert.That(_movement.RemainingAirJumps, Is.Zero);
    }

    [TestCase(0f, true)] [TestCase(30f, true)] [TestCase(45f, true)] [TestCase(70f, false)]
    public void SupportProbe_UsesCapsuleAndWalkableNormals(float angle, bool expected)
    {
        Object.DestroyImmediate(GameObject.Find("Floor"));
        Vector3 normal = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
        GameObject slope = Box("Slope", -normal * .1f, new Vector3(40f, .2f, 40f));
        slope.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        _movement.transform.position = Vector3.up * (.6f + .6f / Mathf.Cos(angle * Mathf.Deg2Rad) + .01f);
        Physics.SyncTransforms();
        Assert.That((bool)Call(_movement, "IsGrounded"), Is.EqualTo(expected));
        if (expected) Assert.That(Vector3.Angle(_movement.GroundNormal, normal), Is.LessThan(.1f));
        if (expected && angle > 0f)
        {
            _body.linearVelocity = Vector3.up * 3f;
            Assert.That((bool)Call(_movement, "IsGrounded"), Is.True, "Walking uphill is not a jump.");
        }
    }

    [Test]
    public void SupportProbe_RetainsOffCentreEdgeAndRejectsEnemyEvenOnDefaultLayer()
    {
        Object.DestroyImmediate(GameObject.Find("Floor"));
        GameObject edge = Box("Edge", new Vector3(0f, -.5f, 0f), new Vector3(2f, 1f, 20f));
        _movement.transform.position = new Vector3(1.3f, 1.12f, 0f);
        Physics.SyncTransforms();
        Assert.That((bool)Call(_movement, "IsGrounded"), Is.True);
        edge.AddComponent<EnemyHealth>();
        Assert.That((bool)Call(_movement, "IsGrounded"), Is.False);
    }

    [Test]
    public void PadLanding_PreservesBallisticLockAndDeadStopWithoutZeroSpeedSlide()
    {
        Set(_movement, "_crouchHeld", true);
        _movement.LaunchWithVelocity(new Vector3(12f, 5f, 0f), 2f);
        Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity.x, Is.EqualTo(12f));
        Set(_movement, "_launchGroundGrace", 0f);
        _body.linearVelocity = new Vector3(5f, -.1f, 0f);
        Call(_movement, "FixedUpdate");
        Assert.That(_body.linearVelocity, Is.EqualTo(Vector3.zero));
        Assert.That(_movement.IsLaunching, Is.False);
        Assert.That(_movement.IsSliding, Is.False);
    }

    private void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        InputSystem.Update();
        _keyboard.MakeCurrent();
        foreach (Key key in keys) Assert.That(_keyboard[key].isPressed, Is.True, "Synthetic input must reach the device.");
    }

    [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(240)]
    public void Destroyer_PullsOncePerPhysicsTickRegardlessOfRenderCalls(int renderCalls)
    {
        GameObject boss = Track(new GameObject("Destroyer"));
        boss.transform.position = new Vector3(30f, 1.2f, 0f);
        boss.AddComponent<EnemyHealth>();
        DestroyerBehavior behavior = boss.AddComponent<DestroyerBehavior>();
        Call(behavior, "Awake");
        FieldInfo state = typeof(DestroyerBehavior).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
        state.SetValue(behavior, Enum.Parse(state.FieldType, "Suction"));
        Set(behavior, "_playerPullAcceleration", 10f);
        for (int i = 0; i < renderCalls; i++) Call(behavior, "TickSuction");
        Physics.Simulate(.02f);
        Assert.That(_movement.PlanarSpeed, Is.Zero, "Render updates must not submit physics acceleration.");
        for (int i = 0; i < 50; i++) { Call(behavior, "FixedUpdate"); Physics.Simulate(.02f); }
        Assert.That(_movement.PlanarSpeed, Is.EqualTo(10f).Within(.001f));
    }
    private GameObject Box(string name, Vector3 position, Vector3 scale)
    {
        GameObject box = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        box.name = name; box.transform.position = position; box.transform.localScale = scale;
        return box;
    }
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
