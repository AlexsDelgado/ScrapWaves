using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ManualWeaponScreenShakeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _cleanup = new();
    private readonly List<CombatFeedbackDirector> _directors = new();

    [SetUp]
    public void InitializeDamageTracking()
    {
        if (ChallengeProgressTracker.Instance != null) return;
        GameObject trackerObject = Track(new GameObject("Weapon shake test damage tracker"));
        ChallengeProgressTracker tracker = trackerObject.AddComponent<ChallengeProgressTracker>();
        // EditMode does not run the Awake that normally registers this dependency.
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (CombatFeedbackDirector director in _directors) director.StopAll();
        _directors.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    [TestCase(WeaponType.RotatingBlade)]
    public void SemanticShots_PreserveVfxButOnlyManualAndQAddCameraImpulses(WeaponType type)
    {
        Harness h = CreateHarness(type);
        h.Weapon.State = WeaponState.Automatic;
        WeaponFeedbackContext automatic = Context(h.Weapon, WeaponFeedbackMode.Automatic);
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in automatic, 1f, 1f);
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
        AssertQuiet(h.Camera);

        h.Weapon.State = WeaponState.Manual;
        WeaponFeedbackContext manual = Context(h.Weapon, WeaponFeedbackMode.Manual);
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in manual, 1f, 2f);
        AssertShakes(h.Camera);
        h.Camera.ClearPresentationImpulses();
        WeaponFeedbackContext active = Context(h.Weapon, WeaponFeedbackMode.Active);
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in active, 1f, 3f);
        AssertShakes(h.Camera);
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    [TestCase(WeaponFeedbackMode.Active)]
    public void SustainedFireLoops_KeepRunningWithShakeRestrictedToManualFire(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness(WeaponType.Flamethrower, loop: true);
        WeaponFeedbackContext context = Context(h.Weapon, mode);
        h.Director.BeginSemanticLoop(WeaponFeedbackEvent.SustainedFireStarted, in context, 1f, 1f);
        Assert.That(h.Director.ActiveLoopCount, Is.EqualTo(1));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
        h.Director.EndSemanticLoop(WeaponFeedbackEvent.SustainedFireStarted, in context);
        Assert.That(h.Director.ActiveLoopCount, Is.Zero);
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    [TestCase(WeaponFeedbackMode.Active)]
    public void LegacyCues_UseTheSameManualOnlyCameraRule(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness();
        WeaponPresentationContext context = new(WeaponPresentationCue.AutomaticCannonManualShot,
            h.Weapon, Vector3.zero, Vector3.forward, mode: mode);
        Assert.That(h.Director.EmitLegacy(in context, 1f, 1f), Is.True);
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    [Test]
    public void AutomaticFeedback_DoesNotUseImpulseBudgetOrClearOutstandingManualShake()
    {
        Harness h = CreateHarness();
        CameraFeedbackController controller = new();
        controller.Bind(h.Camera);
        WeaponFeedbackContext automatic = Context(h.Weapon, WeaponFeedbackMode.Automatic);
        WeaponFeedbackContext manual = Context(h.Weapon, WeaponFeedbackMode.Manual);
        Assert.That(controller.Request(h.Cue, in automatic, null, true, false, 1f), Is.False);
        Assert.That(controller.Request(h.Cue, in manual, null, true, false, 1f), Is.True);
        Vector3 previous = h.Camera.CurrentPresentationRotationImpulse;
        Assert.That(controller.Request(h.Cue, in automatic, null, true, false, 2f), Is.False);
        Assert.That(h.Camera.CurrentPresentationRotationImpulse, Is.EqualTo(previous));
    }

    [TestCase(true, false, 1f)]
    [TestCase(true, true, .25f)]
    [TestCase(false, false, 0f)]
    public void ManualShake_StillRespectsEnabledAndReducedShakeSettings(bool enabled, bool reducedShake, float scale)
    {
        Harness h = CreateHarness();
        CameraFeedbackController controller = new();
        controller.Bind(h.Camera);
        WeaponFeedbackContext manual = Context(h.Weapon, WeaponFeedbackMode.Manual);
        bool accepted = controller.Request(h.Cue, in manual, null, enabled, reducedShake, 1f);
        Assert.That(accepted, Is.EqualTo(enabled));
        Assert.That(h.Camera.CurrentPresentationPositionImpulse.x, Is.EqualTo(.1f * scale).Within(.0001f));
        Assert.That(h.Camera.CurrentPresentationRotationImpulse.y, Is.EqualTo(.2f * scale).Within(.0001f));
    }

    [Test]
    public void ManualShake_RespectsRuntimeScreenShakeToggleAndCameraPreference()
    {
        Harness h = CreateHarness();
        WeaponFeedbackContext manual = Context(h.Weapon, WeaponFeedbackMode.Manual);
        h.Options.ScreenShakeEnabled = false;
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in manual, 1f, 1f);
        AssertQuiet(h.Camera);
        h.Options.ScreenShakeEnabled = true;
        h.Camera.ScreenShakeEnabled = false;
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in manual, 1f, 2f);
        AssertQuiet(h.Camera);
        h.Camera.ScreenShakeEnabled = true;
        h.Director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in manual, 1f, 3f);
        AssertShakes(h.Camera);
    }

    [Test]
    public void NonWeaponAndDirectCameraFeedback_KeepTheirExistingBehavior()
    {
        Harness h = CreateHarness();
        CameraFeedbackController controller = new();
        controller.Bind(h.Camera);
        WeaponFeedbackContext unrelated = Context(null, WeaponFeedbackMode.Automatic);
        Assert.That(controller.Request(h.Cue, in unrelated, null, true, false, 1f), Is.True);
        AssertShakes(h.Camera);
        h.Camera.ClearPresentationImpulses();
        Assert.That(h.Camera.AddPresentationImpulse(new Vector3(.1f, 0f, 0f), Vector3.up), Is.True);
        Assert.That(h.Camera.CurrentPresentationRotationImpulse.y, Is.GreaterThan(0f));
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    public void SemanticProjectileImpact_UsesLaunchModeAfterSwitch(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness();
        Projectile projectile = CreateProjectile();
        WeaponFeedbackContext launch = Context(h.Weapon, mode);
        projectile.ConfigureFeedback(h.Sink, in launch);
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Manual : WeaponState.Automatic;
        Call(projectile, "EmitFeedbackImpact", null, null, true, Vector3.zero, 0, false, false);
        Assert.That(h.Sink.ImpactModes, Is.EqualTo(new[] { mode }));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    public void LegacyProjectileImpact_UsesLaunchModeAfterSwitch(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness();
        Projectile projectile = CreateProjectile();
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        projectile.ConfigurePresentation(h.Sink, h.Weapon, h.Cue.Cue,
            WeaponPresentationCue.None, WeaponPresentationCue.None, false, false);
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Manual : WeaponState.Automatic;
        Call(projectile, "EmitPresentationImpact", null, null, true, Vector3.zero);
        Assert.That(h.Sink.LegacyModes, Is.EqualTo(new[] { mode }));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    [Test]
    public void ReusedLegacyProjectile_RefreshesModeForEachLaunch()
    {
        Harness h = CreateHarness();
        Projectile projectile = CreateProjectile();
        for (int cycle = 0; cycle < 6; cycle++)
        {
            projectile.ClearPresentation();
            h.Camera.ClearPresentationImpulses();
            WeaponFeedbackMode mode = cycle % 2 == 0 ? WeaponFeedbackMode.Automatic : WeaponFeedbackMode.Manual;
            h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
            projectile.ConfigurePresentation(h.Sink, h.Weapon, h.Cue.Cue,
                WeaponPresentationCue.None, WeaponPresentationCue.None, false, false);
            Call(projectile, "EmitPresentationImpact", null, null, true, Vector3.zero);
            Assert.That(h.Sink.LegacyModes[cycle], Is.EqualTo(mode));
            if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
            else AssertShakes(h.Camera);
        }
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    public void DelayedHeadHunterWorldImpact_RetainsFiringModeAcrossSwitch(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness();
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        AutomaticCannonWeapon cannon = CreateCannon(h, null);
        Call(cannon, "QueueHeadHunterWorldImpact", default(RaycastHit), Vector3.forward, false, 1f);
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Manual : WeaponState.Automatic;
        Call(cannon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(h.Sink.ImpactModes, Is.EqualTo(new[] { mode }));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    [TestCase(WeaponFeedbackMode.Automatic)]
    [TestCase(WeaponFeedbackMode.Manual)]
    public void DelayedHeadHunterEnemyImpact_RetainsModeWithoutChangingDamage(WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness();
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        AutomaticCannonWeapon cannon = CreateCannon(h, null);
        GameObject target = Track(new GameObject("Delayed Head Hunter target"));
        ShakeTestDamageable damageable = target.AddComponent<ShakeTestDamageable>();
        Call(cannon, "QueueHeadHunterImpact", target.transform, 10, false, false, false,
            Vector3.zero, Vector3.zero, Vector3.forward, 0, 1f, 0, 10f);
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Manual : WeaponState.Automatic;
        Call(cannon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(damageable.TotalDamage, Is.EqualTo(10));
        Assert.That(h.Sink.ImpactModes, Is.EqualTo(new[] { mode }));
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    [TestCase(WeaponFeedbackMode.Automatic, false)]
    [TestCase(WeaponFeedbackMode.Manual, false)]
    [TestCase(WeaponFeedbackMode.Automatic, true)]
    [TestCase(WeaponFeedbackMode.Manual, true)]
    public void PendingCannonBurst_RetainsTriggerModeForEveryRoundAndLegacyCue(WeaponFeedbackMode mode, bool legacyOnly)
    {
        Harness h = CreateHarness();
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        ProjectilePool pool = CreatePool();
        AutomaticCannonWeapon cannon = CreateCannon(h, pool);
        if (legacyOnly) cannon.SetPresentationSink(new LegacyOnlySink(h.Sink));
        Call(cannon, "BeginPresentationLineBurst", Vector3.forward, 3, 1f, .2f, 0f, 0f, false,
            h.Cue.Cue, h.Cue.Cue, null, false);
        h.Camera.ClearPresentationImpulses();
        h.Weapon.State = mode == WeaponFeedbackMode.Automatic ? WeaponState.Manual : WeaponState.Automatic;
        Call(cannon, "TickPendingLineBurst", 1f);
        Assert.That(pool.ActiveLeasedCount, Is.EqualTo(3), "All rounds still spawn.");
        Assert.That(h.Sink.LegacyModes, Is.Not.Empty);
        Assert.That(h.Sink.LegacyModes, Has.All.EqualTo(mode));
        if (!legacyOnly)
        {
            Assert.That(h.Sink.ShotModes, Has.Count.EqualTo(3));
            Assert.That(h.Sink.ShotModes, Has.All.EqualTo(mode));
        }
        if (mode == WeaponFeedbackMode.Automatic) AssertQuiet(h.Camera);
        else AssertShakes(h.Camera);
    }

    private Harness CreateHarness(WeaponType type = WeaponType.AutomaticCannon, bool loop = false)
    {
        GameObject cameraObject = Track(new GameObject("Weapon shake test camera"));
        cameraObject.AddComponent<Camera>();
        ThirdPersonCamera camera = cameraObject.AddComponent<ThirdPersonCamera>();
        camera.CameraFeedbackScale = 1f;
        camera.ScreenShakeEnabled = true;
        GameObject runtime = Track(new GameObject("Weapon shake feedback runtime"));
        GameObject vfx = Track(new GameObject("Weapon shake VFX prefab"));
        vfx.SetActive(false);
        WeaponPresentationProfile profile = Track(ScriptableObject.CreateInstance<WeaponPresentationProfile>());
        var cue = new WeaponPresentationCueData
        {
            Cue = loop ? WeaponPresentationCue.FlamethrowerManualLoop : WeaponPresentationCue.AutomaticCannonManualShot,
            VfxPrefab = vfx, Duration = 1f, PrewarmCount = 1, MaxSimultaneous = 8, Loop = loop,
            CameraPositionImpulse = new Vector3(.1f, 0f, 0f),
            CameraRotationImpulse = new Vector3(0f, .2f, 0f), CameraFovKick = 1f
        };
        Get<List<WeaponPresentationCueData>>(profile, "_cues").Add(cue);
        foreach (WeaponFeedbackEvent feedbackEvent in new[] { WeaponFeedbackEvent.ShotFired,
            WeaponFeedbackEvent.ProjectileImpact, WeaponFeedbackEvent.SustainedFireStarted })
            Get<List<WeaponFeedbackBinding>>(profile, "_feedbackBindings").Add(new WeaponFeedbackBinding { Event = feedbackEvent, Cue = cue.Cue });
        profile.RebuildCache();
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.BaseDamage = 10f;
        data.BaseManualAmmo = 100f;
        data.PresentationProfile = profile;
        var options = new GameFeelRuntimeOptions { HitStopEnabled = false, EnemyReactionEnabled = false, AudioEnabled = false };
        var director = new CombatFeedbackDirector(profile, runtime.transform, camera, null, options,
            new CameraFeedbackController(), new HitStopController(), 1, 0f);
        _directors.Add(director);
        return new Harness
        {
            Camera = camera, Director = director, Options = options, Cue = cue,
            Weapon = new WeaponInstance { Data = data, State = WeaponState.Manual, CurrentAmmo = 100f },
            Sink = new ForwardingSink(director)
        };
    }

    private AutomaticCannonWeapon CreateCannon(Harness h, ProjectilePool pool)
    {
        GameObject owner = Track(new GameObject("Weapon shake cannon owner"));
        var cannon = new AutomaticCannonWeapon(null, pool, owner.transform);
        cannon.Setup(h.Weapon, owner.transform, null, null);
        cannon.SetPresentationSink(h.Sink);
        return cannon;
    }

    private Projectile CreateProjectile() => Track(new GameObject("Weapon shake projectile")).AddComponent<Projectile>();

    private ProjectilePool CreatePool()
    {
        Projectile prefab = CreateProjectile();
        prefab.gameObject.SetActive(false);
        GameObject container = Track(new GameObject("Weapon shake projectile container"));
        GameObject poolObject = Track(new GameObject("Weapon shake projectile pool"));
        poolObject.SetActive(false);
        ProjectilePool pool = poolObject.AddComponent<ProjectilePool>();
        Set(pool, "_projectilePrefab", prefab.gameObject);
        Set(pool, "_container", container.transform);
        Set(pool, "_initialPoolSize", 3);
        Set(pool, "_maxPoolSize", 3);
        Call(pool, "Awake");
        return pool;
    }

    private static WeaponFeedbackContext Context(WeaponInstance weapon, WeaponFeedbackMode mode) =>
        new(weapon, mode, 0f, Vector3.zero, Vector3.forward);
    private static void AssertQuiet(ThirdPersonCamera camera)
    {
        Assert.That(camera.CurrentPresentationPositionImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(camera.CurrentPresentationRotationImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(camera.CurrentPresentationFovKick, Is.Zero);
    }
    private static void AssertShakes(ThirdPersonCamera camera)
    {
        Assert.That(camera.CurrentPresentationPositionImpulse.sqrMagnitude, Is.GreaterThan(0f));
        Assert.That(camera.CurrentPresentationRotationImpulse.sqrMagnitude, Is.GreaterThan(0f));
    }
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);

    private sealed class Harness
    {
        public ThirdPersonCamera Camera;
        public CombatFeedbackDirector Director;
        public GameFeelRuntimeOptions Options;
        public WeaponPresentationCueData Cue;
        public WeaponInstance Weapon;
        public ForwardingSink Sink;
    }

    public sealed class ShakeTestDamageable : MonoBehaviour, IDamageable
    {
        public int TotalDamage;
        public bool ApplyDamage(int amount) { TotalDamage += amount; return true; }
    }

    private sealed class ForwardingSink : IWeaponFeedbackSink
    {
        private readonly CombatFeedbackDirector _director;
        private float _now;
        public readonly List<WeaponFeedbackMode> ShotModes = new();
        public readonly List<WeaponFeedbackMode> ImpactModes = new();
        public readonly List<WeaponFeedbackMode> LegacyModes = new();
        public ForwardingSink(CombatFeedbackDirector director) { _director = director; }
        public void Emit(in WeaponPresentationContext context) { LegacyModes.Add(context.Mode); _director.EmitLegacy(in context, 1f, ++_now); }
        public void OnShotFired(in WeaponFeedbackContext context) { ShotModes.Add(context.Mode); _director.EmitSemantic(WeaponFeedbackEvent.ShotFired, in context, 1f, ++_now); }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { ImpactModes.Add(context.Mode); _director.EmitSemantic(WeaponFeedbackEvent.ProjectileImpact, in context, 1f, ++_now); }
        public void OnDamageConfirmed(in WeaponFeedbackContext context) { }
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context, float progress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnStatusApplied(in WeaponFeedbackContext context) { }
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float threshold) { }
        public void ConfigureProjectile(Projectile projectile, ProjectilePresentationArchetypeId archetype, in WeaponFeedbackContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    private sealed class LegacyOnlySink : IWeaponPresentationSink
    {
        private readonly ForwardingSink _sink;
        public LegacyOnlySink(ForwardingSink sink) { _sink = sink; }
        public void Emit(in WeaponPresentationContext context) => _sink.Emit(in context);
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }
}
