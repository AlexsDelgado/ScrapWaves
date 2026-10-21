using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class GeyserOverheatTests
{
    private GameObject _vent;
    private OverheatManager _overheat;
    private GeyserVfx _vfx;
    private ParticleSystem _updraft, _debris, _burst;
    private Renderer _model, _shimmer;
    private Collider _solid;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _overheat = new GameObject("Overheat").AddComponent<OverheatManager>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_vent != null) Object.DestroyImmediate(_vent);
        if (_overheat != null)
        {
            Set(_overheat, "_isOverheating", false);
            Object.DestroyImmediate(_overheat.gameObject);
        }
        OverheatSwarmBoost.SetIntensity(false);
    }

    [Test]
    public void OverheatClearsEffectsAndPreservesGeometryAndPreviouslyStoppedEffects()
    {
        CreateVent(GeyserVfx.Kind.Trash);
        _debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var debrisEmission = _debris.emission;
        debrisEmission.enabled = false;
        _updraft.Emit(5);
        _burst.Emit(5);
        Call(_overheat, "OnMaxHeatReached");

        AssertSuppressed();
        Assert.That(_vent.activeSelf && _vfx.enabled && _model.enabled && _solid.enabled, Is.True);
        // Inspector/runtime tuning and repeated calls must not erase the restore snapshot.
        _vfx.ApplyTuning();
        _vfx.ApplyTuning();
        _vfx.PlayEntryBurst();
        AssertSuppressed();

        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_updraft.isPlaying, Is.True);
        Assert.That(_debris.isPlaying, Is.False);
        Assert.That(_debris.emission.enabled, Is.False);
        Assert.That(_burst.isPlaying, Is.False);
        Assert.That(_burst.particleCount, Is.Zero);
        Assert.That(_shimmer.enabled, Is.False);
    }

    [Test]
    public void PermanentOverheatDoesNotRestoreEffectsWhenObjectiveCompletes()
    {
        CreateVent(GeyserVfx.Kind.HotAir);
        Assert.That(_shimmer.enabled, Is.True);
        Call(_overheat, "OnMaxHeatReached");
        _overheat.EnterPermanentOverheat();
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_overheat.IsOverheating && _overheat.IsPermanentOverheat, Is.True);
        AssertSuppressed();

        Call(_overheat, "EndOverheat", OverheatEndReason.Interrupted);
        Assert.That(_updraft.isPlaying, Is.True);
        Assert.That(_shimmer.enabled, Is.True);
        Assert.That(_burst.isPlaying, Is.False);
    }

    [Test]
    public void DisabledVentStaysDisabledAndReenableReconcilesCurrentPhase()
    {
        CreateVent(GeyserVfx.Kind.Trash);
        Call(_overheat, "OnMaxHeatReached");
        _vent.SetActive(false);
        Call(_vfx, "OnDisable");
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_vent.activeSelf, Is.False);
        Assert.That(_updraft.isPlaying, Is.False);

        _overheat.EnterPermanentOverheat();
        _vent.SetActive(true);
        Call(_vfx, "OnEnable");
        _vfx.PlayEntryBurst();
        AssertSuppressed();
        Call(_overheat, "EndOverheat", OverheatEndReason.Interrupted);
        Assert.That(_updraft.isPlaying && _debris.isPlaying, Is.True);
        Assert.That(_burst.isPlaying, Is.False);
    }

    [Test]
    public void FirstEnableDuringOverheatIsSuppressedBeforePresentation()
    {
        _overheat.EnterPermanentOverheat();
        CreateVent(GeyserVfx.Kind.HotAir);
        AssertSuppressed();
        Call(_overheat, "EndOverheat", OverheatEndReason.Interrupted);
        Assert.That(_updraft.isPlaying && _shimmer.enabled, Is.True);
    }

    [Test]
    public void LaunchChecksCurrentOverheatAndDoesNotInterruptExistingFlight()
    {
        _vent = new GameObject("Vent", typeof(BoxCollider));
        var launcher = _vent.AddComponent<MapLaunchPad>();
        var landing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var player = new GameObject("Player", typeof(CapsuleCollider), typeof(Rigidbody));
        try
        {
            landing.transform.position = new Vector3(20f, 10f, 0f);
            var movement = player.AddComponent<PlayerMovement>();
            var body = player.GetComponent<Rigidbody>();
            Set(movement, "_rb", body);
            Set(launcher, "_landing", landing.transform);
            Set(launcher, "_overheatManager", _overheat);
            Physics.SyncTransforms();
            int launches = 0;
            launcher.OnLaunched += _ => launches++;

            // No start event is needed for the point-of-use guard to reject a launch.
            Set(_overheat, "_isOverheating", true);
            Call(launcher, "TryLaunch", player.GetComponent<Collider>(), false);
            Assert.That(launches, Is.Zero);
            Assert.That(movement.IsLaunching, Is.False);

            Set(_overheat, "_isOverheating", false);
            Call(launcher, "TryLaunch", player.GetComponent<Collider>(), false);
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(movement.IsLaunching, Is.True);
            Vector3 velocity = body.linearVelocity;
            Call(_overheat, "OnMaxHeatReached");
            _overheat.EnterPermanentOverheat();
            Assert.That(movement.IsLaunching, Is.True);
            Assert.That(body.linearVelocity, Is.EqualTo(velocity));
            Call(launcher, "TryLaunch", player.GetComponent<Collider>(), false);
            Assert.That(launches, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(landing);
        }
    }

    private void CreateVent(GeyserVfx.Kind kind)
    {
        _vent = new GameObject("Vent", typeof(BoxCollider), typeof(MeshRenderer));
        _vent.SetActive(false);
        _solid = _vent.GetComponent<Collider>();
        _model = _vent.GetComponent<Renderer>();
        _updraft = Particles("Updraft");
        _debris = Particles("Debris");
        _burst = Particles("Burst");
        var shimmerObject = new GameObject("Shimmer", typeof(MeshRenderer));
        shimmerObject.transform.SetParent(_vent.transform);
        _shimmer = shimmerObject.GetComponent<Renderer>();
        _vfx = _vent.AddComponent<GeyserVfx>();
        Set(_vfx, "_overheatManager", _overheat);
        _vfx.ConfigureAuthoring(kind, _updraft, _debris, _burst, _shimmer);
        _vent.SetActive(true);
        Call(_vfx, "OnEnable");
    }

    private ParticleSystem Particles(string name)
    {
        var child = new GameObject(name, typeof(ParticleSystem));
        child.transform.SetParent(_vent.transform);
        var particles = child.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private void AssertSuppressed()
    {
        Assert.That(_vfx.ActiveParticleCount, Is.Zero);
        Assert.That(_updraft.isPlaying || _debris.isPlaying || _burst.isPlaying, Is.False);
        Assert.That(_updraft.emission.enabled || _debris.emission.enabled || _burst.emission.enabled, Is.False);
        Assert.That(_shimmer.enabled, Is.False);
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
