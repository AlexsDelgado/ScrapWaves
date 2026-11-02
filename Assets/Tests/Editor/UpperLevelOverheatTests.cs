using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Real collider/support queries with deterministic hazard ticks. Native-loop integration is separate.
public sealed class UpperLevelOverheatTests
{
    private readonly List<Object> _cleanup = new();
    private GameObject _player;
    private Rigidbody _body;
    private PlayerMovement _movement;
    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerPlatformState _state;
    private PlayerUpperLevelOverheat _hazard;
    private OverheatManager _overheat;
    private UpperLevelVolume _volume;
    private BoxCollider _box;
    private BoxCollider _base;
    private ParticleSystem _effect;
    private ChallengeProgressTracker _previousChallengeTracker;
    private float _previousScale;
    private bool _previousStartupHeld;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _previousScale = Time.timeScale;
        _previousStartupHeld = EntranceCinematic.StartupHeld;
        SetStartupHeld(false);
        Time.timeScale = 1f;
        GameplayPause.Reset();
        _previousChallengeTracker = ChallengeProgressTracker.Instance;
        // EditMode AddComponent does not execute the gameplay singleton's Awake.
        var tracker = Track(new GameObject("Upper hazard challenge tracker")).AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);

        _base = Box("Authored Base", new Vector3(0f, -.5f, 0f), new Vector3(100f, 1f, 100f));
        _box = Box("One upper volume", new Vector3(0f, 20f, 0f), new Vector3(40f, 20f, 40f));
        _box.isTrigger = true;
        _volume = _box.gameObject.AddComponent<UpperLevelVolume>();
        Set(_volume, "_baseSurfaceRoots", new[] { _base.transform });

        _player = Track(new GameObject("Upper hazard player"));
        var capsule = _player.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = .5f;
        _body = _player.AddComponent<Rigidbody>();
        _body.useGravity = false;
        _stats = _player.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (string name in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed", "MaxHealth", "DamageResistance" })
        {
            var definition = AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{name}.asset");
            Assert.That(definition, Is.Not.Null, name);
            definitions.Add(definition);
        }
        Set(_stats, "_statDefinitions", definitions);
        Call(_stats, "Awake");
        _movement = _player.AddComponent<PlayerMovement>();
        Call(_movement, "Awake");
        _health = _player.AddComponent<PlayerHealth>();
        Call(_health, "Awake");
        _health.FullHeal();
        Set(_health, "_hitInvulnerabilitySeconds", 0f);

        _state = _player.AddComponent<PlayerPlatformState>();
        Set(_state, "_upperLevelVolume", _volume);
        Call(_state, "Awake");
        _overheat = Track(new GameObject("Overheat phase")).AddComponent<OverheatManager>();
        Set(_overheat, "_isOverheating", true);

        var effectObject = new GameObject("Authored hazard fire");
        effectObject.transform.SetParent(_player.transform, false);
        _effect = effectObject.AddComponent<ParticleSystem>();
        _effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _effect.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 12;
        _hazard = _player.AddComponent<PlayerUpperLevelOverheat>();
        // Explicit lifecycle setup is needed for edit-mode components without ExecuteAlways.
        Call(_hazard, "OnDisable");
        Set(_hazard, "_platformState", _state);
        Set(_hazard, "_upperLevelVolume", _volume);
        Set(_hazard, "_health", _health);
        Set(_hazard, "_overheatManager", _overheat);
        Set(_hazard, "_effects", new[] { _effect });
        Call(_hazard, "Awake");
        Call(_hazard, "OnEnable");
        Move(new Vector3(0f, 12f, 0f));
        Assert.That(_state.IsInsideUpper, Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        if (_hazard != null) Call(_hazard, "OnDisable");
        if (_overheat != null) Set(_overheat, "_isOverheating", false);
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _previousChallengeTracker);
        GameplayPause.Reset();
        SetStartupHeld(_previousStartupHeld);
        Time.timeScale = _previousScale;
    }

    [Test]
    public void Region_UsesRotatedScaledBoxSpaceAndHonorsDisabledVolume()
    {
        _volume.transform.SetPositionAndRotation(new Vector3(-7f, 30f, 9f), Quaternion.Euler(22f, 43f, 11f));
        _volume.transform.localScale = new Vector3(2f, .5f, 3f);
        _box.center = new Vector3(1f, 2f, 3f);
        _box.size = new Vector3(4f, 6f, 8f);
        Vector3 inside = _volume.transform.TransformPoint(_box.center + new Vector3(1.99f, 2.99f, 3.99f));
        Vector3 outside = _volume.transform.TransformPoint(_box.center + new Vector3(2.01f, 0f, 0f));
        Assert.That(_volume.Contains(inside), Is.True);
        Assert.That(_volume.Contains(outside), Is.False);
        _box.enabled = false;
        Assert.That(_volume.Contains(inside), Is.False);
        _box.enabled = true;
        _volume.enabled = false;
        Assert.That(_volume.Contains(inside), Is.False);
    }

    [Test]
    public void PlayerChildTrigger_CannotExpandUpperOccupancyOrMoveFeet()
    {
        var child = new GameObject("Large pickup trigger");
        child.transform.SetParent(_player.transform, false);
        var trigger = child.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 100f;
        Move(new Vector3(30f, 12f, 0f));
        Assert.That(_state.FeetPosition, Is.EqualTo(new Vector3(30f, 11f, 0f)));
        Assert.That(_state.IsInsideUpper, Is.False);
        child.SetActive(false);
        Assert.That(_state.IsInsideUpper, Is.False);
    }

    [Test]
    public void BaseGate_RequiresCurrentPositiveSupportAndRejectsTeleportAndJump()
    {
        Move(new Vector3(0f, 1.01f, 0f));
        Assert.That(_movement.TryGetGroundSupport(out Collider support), Is.True);
        Assert.That(support, Is.SameAs(_base));
        Assert.That(_state.CanSpawnObjectives, Is.True);

        // No FixedUpdate between teleports: stale support must never unlock a batch.
        Move(new Vector3(0f, 6f, 0f));
        Assert.That(_state.IsInsideUpper, Is.False);
        Assert.That(_state.CanSpawnObjectives, Is.False);
        Move(new Vector3(0f, 1.01f, 0f));
        Call(_movement, "PerformJump", false);
        Assert.That(_body.linearVelocity.y, Is.GreaterThan(0f));
        Assert.That(_state.CanSpawnObjectives, Is.False, "Consumed jump support must not release the queue.");
    }

    [Test]
    public void GroundOutsideVolume_IsNotBaseWithoutExplicitSupportReference()
    {
        var upperEdge = Box("Unmarked elevated ledge", new Vector3(60f, 19.5f, 0f), new Vector3(10f, 1f, 10f));
        Move(new Vector3(60f, 21.01f, 0f));
        Assert.That(_movement.TryGetGroundSupport(out Collider support), Is.True);
        Assert.That(support, Is.SameAs(upperEdge));
        Assert.That(_state.IsInsideUpper, Is.False);
        Assert.That(_state.CanSpawnObjectives, Is.False);
    }

    [Test]
    public void JustLaunchedPlayer_CannotReuseNearbyBaseSupport()
    {
        Move(new Vector3(0f, 1.01f, 0f));
        Assert.That(_state.CanSpawnObjectives, Is.True);
        _movement.LaunchWithVelocity(Vector3.down, 1f);
        Assert.That(_movement.IsLaunching, Is.True);
        Assert.That(_state.CanSpawnObjectives, Is.False);
        Assert.That(_body.linearVelocity, Is.EqualTo(Vector3.down));
    }

    [Test]
    public void Damage_DefaultGraceAndCadenceRetainFractionalGrowth()
    {
        Tick(1.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Tick(.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(98));
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(96), "The third hit rounds 1.69 to 2 instead of multiplying a rounded 1 forever.");
        Tick(2f);
        Tick(2f);
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(87), "Default unmitigated hits are 1, 1, 2, 2, 3, 4.");
    }

    [Test]
    public void GroundReentry_RestartsGraceButRetainsEventDamageEscalation()
    {
        Tick(2f);
        Tick(2f);
        Move(new Vector3(0f, 1.01f, 0f));
        Tick(100f);
        Move(new Vector3(0f, 12f, 0f));
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(98));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(96), "Landing must not reset the next hit to starting damage.");
        Call(_overheat, "EndOverheat", OverheatEndReason.Interrupted);
        Call(_overheat, "OnMaxHeatReached");
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(95), "A new phase starts at 1 again.");
    }

    [Test]
    public void AirborneUpperJump_DoesNotRestartGrace()
    {
        Tick(1.25f);
        Move(new Vector3(0f, 17f, 0f));
        Assert.That(_state.IsBaseSupported, Is.False);
        Tick(.75f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void AirborneExit_StopsDamageAndClearsEffectsButRetainsGraceProgress()
    {
        Tick(1.25f);
        _effect.Emit(6);
        Assert.That(_effect.particleCount, Is.GreaterThan(0));
        Move(new Vector3(0f, 6f, 0f));
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_effect.particleCount, Is.Zero);
        Assert.That(_effect.isPlaying, Is.False);
        Assert.That(_state.CanSpawnObjectives, Is.False);
        Move(new Vector3(0f, 12f, 0f));
        Tick(.75f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void ExitJustBeforeDueHit_SuspendsCadenceWithoutAccumulatingDamage()
    {
        Tick(2f);
        Tick(1.9f);
        Move(new Vector3(30f, 12f, 0f));
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
        Move(new Vector3(0f, 12f, 0f));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(98));
    }

    [Test]
    public void ConfirmedBaseLanding_ResetsExposureForNextVisit()
    {
        Tick(1.5f);
        Move(new Vector3(0f, 1.01f, 0f));
        Assert.That(_state.CanSpawnObjectives, Is.True);
        Tick(100f);
        Move(new Vector3(0f, 12f, 0f));
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [TestCase("ui")]
    [TestCase("time")]
    [TestCase("cinematic")]
    public void PauseOrCinematic_FreezesExposureAndReconcilesSpatialExit(string kind)
    {
        Tick(1.5f);
        if (kind == "ui") GameplayPause.Push();
        if (kind == "time") Time.timeScale = 0f;
        if (kind == "cinematic") SetStartupHeld(true);
        Assert.That(_state.IsRunActive, Is.True, "Pause must retain pending objectives.");
        Assert.That(_state.IsSimulationActive, Is.False);
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_effect.isPaused, Is.True);
        Move(new Vector3(0f, 6f, 0f));
        Tick(100f);
        Assert.That(_effect.isPlaying, Is.False);
        GameplayPause.Reset();
        Time.timeScale = 1f;
        SetStartupHeld(false);
        Move(new Vector3(0f, 12f, 0f));
        Tick(.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void LongFrame_DealsAtMostOneHitAndDoesNotCarryCatchupDebt()
    {
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
        Tick(0f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(98));
    }

    [Test]
    public void OverheatEnd_ImmediatelyClearsEffectsAndResetsNextPhaseGrace()
    {
        Tick(1.5f);
        _effect.Emit(6);
        Call(_overheat, "EndOverheat", OverheatEndReason.Interrupted);
        Assert.That(_effect.particleCount, Is.Zero);
        Assert.That(_effect.isPlaying, Is.False);
        Set(_overheat, "_isOverheating", true);
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void PermanentOverheat_ContinuesHazardWhenObjectiveCompletionIsIgnored()
    {
        Tick(1f);
        _overheat.EnterPermanentOverheat();
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_overheat.IsPermanentOverheat, Is.True);
        Assert.That(_overheat.IsOverheating, Is.True);
        Tick(1f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void Damage_UsesShieldThenOrdinaryResistance()
    {
        Set(_volume, "_startingDamage", 10f);
        Set(_volume, "_damageMultiplier", 1.3f);
        Call(_hazard, "ResetHazard");
        Set(_health, "_shieldCharges", 1);
        Tick(2f);
        Assert.That(_health.ShieldCharges, Is.Zero);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        _stats.AddModifier(new StatModifier(StatType.DamageResistance, .4f, StatUpgradeSource.LevelUp));
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(92), "Shield absorbs the first hit; the next raw 13 hit retains ordinary 40% resistance rounding.");
        Assert.That(_health.IsBurning, Is.False);
    }

    [Test]
    public void Damage_RespectsExistingGlobalHitInvulnerability()
    {
        _health.GrantInvulnerability(10f);
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Set(_health, "_invulnerableUntil", -1f);
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void Damage_RespectsTemporaryPowerupInvulnerability()
    {
        var powerup = _player.AddComponent<TemporaryPowerupController>();
        Call(powerup, "Awake");
        Set(powerup, "_invulnEndsAt", Time.time + 10f);
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Set(powerup, "_invulnEndsAt", -1f);
        Tick(2f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void DisabledVolume_StopsHazardWithoutResettingAirborneExposure()
    {
        Tick(1f);
        _box.enabled = false;
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_effect.isPlaying, Is.False);
        _box.enabled = true;
        Tick(1f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void DeathAndExplicitHealthReset_ClearEffectsAndRestartGrace()
    {
        Set(_health, "_currentHealth", 1);
        Tick(2f);
        Assert.That(_health.IsAlive, Is.False);
        Assert.That(_effect.isPlaying, Is.False);
        Assert.That(_effect.particleCount, Is.Zero);
        Assert.That(_state.CanSpawnObjectives, Is.False);
        _health.FullHeal(resetPhase: true);
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        _health.FullHeal(resetPhase: true);
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void OrdinaryFullHeal_DoesNotRestartUpperExposure()
    {
        Tick(1.5f);
        _health.FullHeal();
        Tick(.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99), "Healing or debug infinite health must not reset the phase clock.");
    }

    [Test]
    public void ComponentDisableAndReenable_ClearsEffectsAndRestartsGrace()
    {
        Tick(1.5f);
        _effect.Emit(6);
        Call(_hazard, "OnDisable");
        Assert.That(_effect.isPlaying, Is.False);
        Assert.That(_effect.particleCount, Is.Zero);
        Call(_hazard, "OnEnable");
        Tick(1.9f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Tick(.11f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(99));
    }

    [Test]
    public void EndedRun_ClearsHazardEvenWhenOverheatFlagRemainsSet()
    {
        Tick(1.5f);
        _effect.Emit(6);
        var game = Track(new GameObject("Ended run")).AddComponent<GameManager>();
        Call(game, "OnEnable");
        Set(game, "_state", GameManager.GameState.GameOver);
        Assert.That(_overheat.IsOverheating, Is.True);
        Assert.That(_state.IsRunActive, Is.False);
        Tick(100f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_effect.isPlaying, Is.False);
        Assert.That(_effect.particleCount, Is.Zero);
    }

    private void Move(Vector3 position)
    {
        _body.position = position;
        _player.transform.position = position;
        _body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    private void Tick(float seconds) => Call(_hazard, "Tick", seconds);

    private BoxCollider Box(string name, Vector3 position, Vector3 size)
    {
        var go = Track(new GameObject(name));
        go.transform.position = position;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }

    private static void SetStartupHeld(bool value) => typeof(EntranceCinematic)
        .GetField("<StartupHeld>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    private static void Set(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, field);
        info.SetValue(target, value);
    }

    private static void Call(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, method);
        info.Invoke(target, arguments);
    }
}
