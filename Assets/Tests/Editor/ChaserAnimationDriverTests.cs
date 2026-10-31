using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class ChaserAnimationDriverTests
{
    private readonly List<Object> _created = new();
    private Random.State _randomState;
    private int _pauseLocks;

    [SetUp]
    public void SetUp()
    {
        _randomState = Random.state;
        _pauseLocks = GameplayPause.LockCount;
        GameplayPause.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
        Random.state = _randomState;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void Charge_PlaysAnticipationThenHoldsWithoutAdvancingGameplayTimer()
    {
        Rig rig = CreateRig();
        Set(rig.Charger, "_chargeSeconds", 2f);
        SetPhase(rig.Charger, "Charging", 2f);
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Charge");
        Assert.That(rig.Driver.PhaseNormalized, Is.Zero);

        Set(rig.Charger, "_stateTimer", 1.8f);
        rig.Driver.TickPresentation(0.02f);
        Assert.That(rig.Animator.GetFloat("ChargePhase"), Is.EqualTo(0.6f).Within(0.0001f));
        Set(rig.Charger, "_stateTimer", 0.9f);
        rig.Driver.TickPresentation(5f);
        Assert.That(rig.Animator.GetFloat("ChargePhase"), Is.EqualTo(1f));
        Assert.That(Get<float>(rig.Charger, "_stateTimer"), Is.EqualTo(0.9f));
        Assert.That(rig.Charger.CurrentPresentationPhase, Is.EqualTo(ChargerEnemyBehavior.PresentationPhase.Charging));
        Assert.That(rig.Root.transform.position, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Dash_FollowsEitherDistanceOrTimeLimitWithoutMovingTheRoot()
    {
        Rig rig = CreateRig();
        Set(rig.Charger, "_dashDuration", 0.5f);
        Set(rig.Charger, "_dashMaxDistance", 12f);
        SetPhase(rig.Charger, "Dashing", 0.4f);
        Set(rig.Charger, "_dashTravelled", 6f);
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Dash");
        Assert.That(rig.Animator.GetFloat("DashPhase"), Is.EqualTo(0.5f).Within(0.0001f));
        Set(rig.Charger, "_stateTimer", 0.05f);
        rig.Driver.TickPresentation(0.02f);
        Assert.That(rig.Animator.GetFloat("DashPhase"), Is.EqualTo(0.9f).Within(0.0001f));
        Assert.That(rig.Root.transform.position, Is.EqualTo(Vector3.zero));
        Assert.That(Get<float>(rig.Charger, "_dashTravelled"), Is.EqualTo(6f));
    }

    [Test]
    public void EarlyCrash_ImmediatelyEntersRecoveryWithoutRewindingOutgoingDash()
    {
        Rig rig = CreateRig();
        Set(rig.Driver, "_blendSeconds", 0.06f);
        SetPhase(rig.Charger, "Dashing", 0.4f);
        Set(rig.Charger, "_dashTravelled", 3f);
        Set(rig.Charger, "_pushForce", 0f);
        GameObject target = Track(new GameObject("Inert crash target"));
        // An already-dead target keeps this presentation test independent of global player effects.
        PlayerHealth playerHealth = target.AddComponent<PlayerHealth>();
        Set(playerHealth, "_currentHealth", 0);
        Set(rig.Charger, "_playerHealth", playerHealth);
        rig.Driver.TickPresentation(0.02f);
        float outgoingPhase = rig.Animator.GetFloat("DashPhase");

        Invoke(rig.Charger, "OnCrash");
        rig.Driver.TickPresentation(0.02f);

        Assert.That(rig.Driver.CurrentStateName, Is.EqualTo("Recover"));
        Assert.That(rig.Animator.GetFloat("RecoverPhase"), Is.Zero);
        Assert.That(rig.Animator.GetFloat("DashPhase"), Is.EqualTo(outgoingPhase));
        Assert.That(Get<float>(rig.Charger, "_stateTimer"), Is.EqualTo(0.6f));
        Set(rig.Charger, "_stateTimer", 0.3f);
        rig.Driver.TickPresentation(0.02f);
        Assert.That(rig.Animator.GetFloat("RecoverPhase"), Is.EqualTo(0.5f).Within(0.0001f));
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void Walk_UsesActualPlanarMovementAndInstanceScale(float scale)
    {
        Rig rig = CreateRig();
        rig.Root.transform.localScale = Vector3.one * scale;
        Set(rig.Driver, "_speedSmoothingSeconds", 0f);
        rig.Driver.OnPoolSpawn();
        rig.Driver.TickPresentation(0f);
        rig.Root.transform.position = new Vector3(2.125f * scale * 0.1f, 20f, 0f);
        rig.Driver.TickPresentation(0.1f);
        AssertState(rig, "Walk");
        Assert.That(rig.Animator.GetFloat("WalkSpeed"), Is.EqualTo(1f).Within(0.0001f));
        rig.Driver.TickPresentation(0.1f);
        AssertState(rig, "Idle");
        Assert.That(rig.Animator.GetFloat("WalkSpeed"), Is.Zero);
    }

    [Test]
    public void Overheat_UsesMeasuredSlowWalkEvenThoughGenericFollowerIsDisabled()
    {
        Rig rig = CreateRig();
        Set(rig.Driver, "_speedSmoothingSeconds", 0f);
        SetPhase(rig.Charger, "Overheat", 2f);
        rig.Follow.enabled = false;
        rig.Root.transform.position = Vector3.forward * 0.1f;
        rig.Driver.TickPresentation(0.1f);
        AssertState(rig, "Walk");
        Assert.That(rig.Driver.WalkPlaybackSpeed, Is.EqualTo(1f / 2.125f).Within(0.0001f));
        Assert.That(rig.Follow.enabled, Is.False);
    }

    [Test]
    public void DisabledTutorialBehavior_RemainsIdleAndNeverEnablesMovement()
    {
        Rig rig = CreateRig();
        SetPhase(rig.Charger, "Dashing", 0.25f);
        rig.Charger.enabled = false;
        rig.Follow.enabled = false;
        rig.Body.isKinematic = true;
        rig.Root.transform.position = Vector3.right * 5f;
        rig.Driver.TickPresentation(0.1f);
        AssertState(rig, "Idle");
        Assert.That(rig.Charger.enabled, Is.False);
        Assert.That(rig.Follow.enabled, Is.False);
        Assert.That(rig.Body.isKinematic, Is.True);
        Assert.That(rig.Root.transform.position, Is.EqualTo(Vector3.right * 5f));
    }

    [Test]
    public void EmergenceGate_HoldsNeutralUntilGameplayIsReady()
    {
        Rig rig = CreateRig();
        EnemyEmergence emergence = rig.Root.AddComponent<EnemyEmergence>();
        rig.Driver.OnPoolSpawn();
        SetPhase(rig.Charger, "Dashing", 0.25f);
        SetEnum(emergence, "_phase", "Emerging");
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Idle");
        SetEnum(emergence, "_phase", "Ready");
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Dash");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Dormancy_SuspendsEvaluationAndRestoresOnlyThePreviousAnimatorEnableState(bool originallyEnabled)
    {
        Rig rig = CreateRig();
        EnemyVerticalEngagement engagement = rig.Root.AddComponent<EnemyVerticalEngagement>();
        rig.Driver.OnPoolSpawn();
        rig.Animator.enabled = originallyEnabled;
        Set(engagement, "_dormant", true);
        rig.Driver.TickPresentation(0.1f);
        Assert.That(rig.Animator.enabled, Is.False);
        rig.Root.transform.position = Vector3.right * 100f;
        rig.Driver.TickPresentation(0.1f);
        Set(engagement, "_dormant", false);
        rig.Driver.TickPresentation(0.1f);
        Assert.That(rig.Animator.enabled, Is.EqualTo(originallyEnabled));
        Assert.That(rig.Driver.WalkPlaybackSpeed, Is.Zero, "Sleeping displacement must not produce a fast walk on wake.");
        Assert.That(rig.Charger.enabled, Is.True);
    }

    [Test]
    public void PoolReuse_ClearsAttackParametersAndVisualOffsetWithoutCountingSpawnTeleportAsWalking()
    {
        Rig rig = CreateRig();
        SetPhase(rig.Charger, "Recover", 0.3f);
        rig.Driver.TickPresentation(0.02f);
        rig.Animator.transform.localPosition += Vector3.right * 10f;
        rig.Animator.speed = 0.2f;
        rig.Driver.OnPoolDespawn();
        rig.Root.transform.position = Vector3.forward * 300f;
        SetPhase(rig.Charger, "Chase", 0f);
        rig.Driver.OnPoolSpawn();
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Idle");
        Assert.That(rig.Animator.transform.localPosition, Is.EqualTo(Vector3.up * 0.25f));
        Assert.That(rig.Animator.speed, Is.EqualTo(1f));
        Assert.That(rig.Animator.GetFloat("ChargePhase"), Is.Zero);
        Assert.That(rig.Animator.GetFloat("DashPhase"), Is.Zero);
        Assert.That(rig.Animator.GetFloat("RecoverPhase"), Is.Zero);
        Assert.That(rig.Animator.GetFloat("WalkSpeed"), Is.Zero);
        Assert.That(rig.Animator.applyRootMotion, Is.False);
    }

    [Test]
    public void Death_HoldsTheLastPoseForSnapshotInsteadOfStartingAnotherState()
    {
        Rig rig = CreateRig();
        SetPhase(rig.Charger, "Dashing", 0.25f);
        rig.Driver.TickPresentation(0.02f);
        AssertState(rig, "Dash");
        float phase = rig.Animator.GetFloat("DashPhase");
        Set(rig.Health, "_currentHealth", 0);
        SetPhase(rig.Charger, "Recover", 0.3f);
        rig.Driver.TickPresentation(0.02f);
        Assert.That(rig.Driver.CurrentStateName, Is.EqualTo("Dash"));
        Assert.That(rig.Animator.GetFloat("DashPhase"), Is.EqualTo(phase));
        Assert.That(rig.Animator.speed, Is.Zero);
        Assert.That(rig.Root.activeSelf, Is.True, "Presentation must not own gameplay despawn.");
    }

    private Rig CreateRig()
    {
        GameObject root = Track(new GameObject("Chaser presentation fixture"));
        root.SetActive(false);
        Rigidbody body = root.AddComponent<Rigidbody>();
        SimpleFollow follow = root.AddComponent<SimpleFollow>();
        ChargerEnemyBehavior charger = root.AddComponent<ChargerEnemyBehavior>();
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(100);
        GameObject visual = new("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * 0.25f;
        GameObject bone = new("Bone");
        bone.transform.SetParent(visual.transform, false);
        Animator animator = visual.AddComponent<Animator>();
        AnimatorController controller = Track(new AnimatorController());
        foreach (string parameter in new[] { "WalkSpeed", "ChargePhase", "DashPhase", "RecoverPhase" })
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
        AnimatorStateMachine machine = Track(new AnimatorStateMachine());
        controller.AddLayer(new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine });
        foreach (string name in new[] { "Idle", "Walk", "Charge", "Dash", "Recover" })
        {
            AnimationClip clip = Track(new AnimationClip { name = name, frameRate = 30f });
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, name == "Idle" ? 0f : 1f));
            AnimatorState state = Track(machine.AddState(name));
            state.motion = clip;
            if (name == "Idle") machine.defaultState = state;
            else if (name == "Walk")
            {
                state.speedParameter = "WalkSpeed";
                state.speedParameterActive = true;
            }
            else
            {
                state.speed = 0f;
                state.timeParameter = name + "Phase";
                state.timeParameterActive = true;
            }
        }
        animator.runtimeAnimatorController = controller;
        ChaserAnimationDriver driver = root.AddComponent<ChaserAnimationDriver>();
        Set(driver, "_blendSeconds", 0f);
        root.SetActive(true);
        animator.Rebind();
        animator.Update(0f);
        Invoke(charger, "Awake");
        Invoke(charger, "OnEnable");
        driver.Configure(animator);
        driver.TickPresentation(0f);
        return new Rig { Root = root, Body = body, Follow = follow, Charger = charger, Health = health, Animator = animator, Driver = driver };
    }

    private static void AssertState(Rig rig, string expected)
    {
        rig.Animator.Update(0f);
        Assert.That(rig.Driver.CurrentStateName, Is.EqualTo(expected));
        Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash(expected)));
    }

    private static void SetPhase(ChargerEnemyBehavior charger, string phase, float timer)
    {
        SetEnum(charger, "_state", phase);
        Set(charger, "_stateTimer", timer);
    }

    private static void SetEnum(object target, string field, string value)
    {
        FieldInfo info = Field(target, field);
        info.SetValue(target, Enum.Parse(info.FieldType, value));
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private T Track<T>(T value) where T : Object { _created.Add(value); return value; }

    private sealed class Rig
    {
        public GameObject Root;
        public Rigidbody Body;
        public SimpleFollow Follow;
        public ChargerEnemyBehavior Charger;
        public EnemyHealth Health;
        public Animator Animator;
        public ChaserAnimationDriver Driver;
    }
}
