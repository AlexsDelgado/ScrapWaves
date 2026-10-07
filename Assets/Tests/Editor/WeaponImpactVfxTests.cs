using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WeaponImpactVfxTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new(10000f, 10000f, 10000f);
    private readonly List<Object> _cleanup = new();
    private readonly List<CombatFeedbackDirector> _directors = new();
    private readonly List<CombatTextDirector> _combatText = new();
    private readonly List<int> _deathIds = new();
    private readonly List<EnemyDeathReactionVfx> _deathEffects = new();
    private Transform _previousDeathRoot;
    private GameFeelRuntimeOptions _previousReactions;
    private bool _previousScreenFlash;

    [SetUp]
    public void SetUp()
    {
        _previousDeathRoot = GetStatic<Transform>("s_root");
        _previousReactions = new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = EnemyReactionRuntime.Enabled,
            ReducedMotion = EnemyReactionRuntime.ReducedMotion,
            ReducedFlash = EnemyReactionRuntime.ReducedFlash,
            Quality = EnemyReactionRuntime.Quality
        };
        _previousScreenFlash = EnemyReactionRuntime.ScreenFlashEnabled;
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions { EnemyReactionEnabled = true });
        EnemyReactionRuntime.ApplyUserPreferences(false, true);
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Impact VFX damage tracker")).AddComponent<ChallengeProgressTracker>();
            // EditMode does not invoke the Awake that normally registers this dependency.
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (CombatFeedbackDirector director in _directors) director.StopAll();
        foreach (CombatTextDirector text in _combatText) text.Dispose();
        IDictionary pending = GetStatic<IDictionary>("s_pending");
        foreach (int id in _deathIds)
        {
            if (!pending.Contains(id)) continue;
            object snapshot = pending[id].GetType().GetField("Snapshot").GetValue(pending[id]);
            snapshot?.GetType().GetMethod("Dispose").Invoke(snapshot, null);
            pending.Remove(id);
        }
        foreach (EnemyDeathReactionVfx effect in _deathEffects)
        {
            Call(effect, "Release");
            Object.DestroyImmediate(effect.gameObject);
        }
        Stack<EnemyDeathReactionVfx> pool = GetStatic<Stack<EnemyDeathReactionVfx>>("s_pool");
        EnemyDeathReactionVfx[] pooled = pool.ToArray();
        pool.Clear();
        for (int i = pooled.Length - 1; i >= 0; i--)
            if (pooled[i] != null) pool.Push(pooled[i]);
        Transform root = GetStatic<Transform>("s_root");
        if (_previousDeathRoot == null && root != null) Object.DestroyImmediate(root.gameObject);
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        _directors.Clear();
        _combatText.Clear();
        _deathIds.Clear();
        _deathEffects.Clear();
        EnemyReactionRuntime.Apply(_previousReactions);
        EnemyReactionRuntime.ApplyUserPreferences(_previousReactions.ReducedMotion, _previousScreenFlash);
    }

    [TestCase(1)]
    [TestCase(3)]
    public void GroundExplosion_KillsKeepOneWeaponImpactAndOneDeathEffectPerEnemy(int enemyCount)
    {
        Harness h = CreateHarness(WeaponType.RocketLauncher);
        var targets = new List<WeaponDummyEnemy>();
        for (int i = 0; i < enemyCount; i++)
        {
            WeaponDummyEnemy target = CreateEnemy(Origin + new Vector3(1f + i * .7f, 0f, .5f), 10);
            // Multiple colliders on a target must still yield one authoritative damage result.
            target.gameObject.AddComponent<SphereCollider>().radius = .4f;
            targets.Add(target);
        }
        Collider ground = CreateGround();
        Projectile projectile = Launch(h, explosion: true);
        Collide(projectile, ground);
        Collide(projectile, ground);
        Assert.That(h.Sink.Impacts.Count, Is.EqualTo(1));
        Assert.That(h.Sink.Damage.Count, Is.EqualTo(enemyCount));
        Assert.That(h.Sink.Damage.TrueForAll(c => c.IsKill && c.DamageAmount == 10), Is.True);
        foreach (WeaponDummyEnemy target in targets)
        {
            Assert.That(target.CurrentHealth, Is.Zero);
            Assert.That(target.gameObject.activeSelf, Is.False);
        }
        FlushOwnDeaths();
        Assert.That(_deathEffects.Count, Is.EqualTo(enemyCount), "Enemy deaths remain independent of weapon impact VFX.");
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1), "A lethal AOE must not add weapon explosions for its kills.");
    }

    [Test]
    public void GroundExplosion_NonlethalDamageKeepsOneImpactAndExactHealthDelta()
    {
        Harness h = CreateHarness(WeaponType.RocketLauncher);
        WeaponDummyEnemy target = CreateEnemy(Origin + Vector3.right, 100);
        Projectile projectile = Launch(h, explosion: true);
        Collide(projectile, CreateGround());
        Assert.That(target.CurrentHealth, Is.EqualTo(80));
        Assert.That(h.Sink.Damage.Count, Is.EqualTo(1));
        Assert.That(h.Sink.Damage[0].IsKill, Is.False);
        Assert.That(h.Sink.Damage[0].DamageAmount, Is.EqualTo(20));
        FlushOwnDeaths();
        Assert.That(_deathEffects, Is.Empty);
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
    }

    [TestCase(WeaponType.RocketLauncher, true)]
    [TestCase(WeaponType.AutomaticCannon, false)]
    public void DirectEnemyCollision_LethalDamageKeepsOnePhysicalImpact(WeaponType type, bool explosion)
    {
        Harness h = CreateHarness(type);
        WeaponDummyEnemy target = CreateEnemy(Origin + Vector3.right, 10);
        Projectile projectile = Launch(h, explosion);
        Collide(projectile, target.GetComponent<Collider>());
        Assert.That(h.Sink.Impacts.Count, Is.EqualTo(1));
        Assert.That(h.Sink.Damage.Count, Is.EqualTo(1));
        Assert.That(h.Sink.Damage[0].IsKill, Is.True);
        Assert.That(target.CurrentHealth, Is.Zero);
        FlushOwnDeaths();
        Assert.That(_deathEffects.Count, Is.EqualTo(1));
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
    }

    [Test]
    public void ReusedProjectile_EmitsEachDistinctImpactWithoutRepeatingConsumedImpact()
    {
        Harness h = CreateHarness(WeaponType.RocketLauncher, poolSize: 1);
        WeaponDummyEnemy target = CreateEnemy(Origin + Vector3.right, 10);
        Collider ground = CreateGround();
        Projectile projectile = Launch(h, explosion: true);
        int instanceId = projectile.GetInstanceID();
        Collide(projectile, ground);
        Collide(projectile, ground);
        Assert.That(h.Pool.ActiveLeasedCount, Is.Zero);
        target.Configure(100, 0f, WeaponEnemyKind.Normal, false, WeaponSandboxMovementPattern.None, null, null);
        h.Sink.AdvanceTime();
        Projectile reused = Launch(h, explosion: true, damage: 7);
        Assert.That(reused.GetInstanceID(), Is.EqualTo(instanceId));
        Collide(reused, ground);
        Collide(reused, ground);
        Assert.That(target.CurrentHealth, Is.EqualTo(93));
        Assert.That(h.Sink.Impacts.Count, Is.EqualTo(2));
        Assert.That(h.Sink.Damage.Count, Is.EqualTo(2));
        Assert.That(h.Sink.Damage[0].IsKill, Is.True);
        Assert.That(h.Sink.Damage[1].IsKill, Is.False);
        Assert.That(h.Pool.ActiveLeasedCount, Is.Zero);
        FlushOwnDeaths();
        Assert.That(_deathEffects.Count, Is.EqualTo(1));
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(2), "Pool reuse must preserve the next genuine impact.");
    }

    [Test]
    public void ClusterChildren_KeepTheirOwnAuthoredDetonationsAndDamage()
    {
        Harness h = CreateHarness(WeaponType.RocketLauncher, poolSize: 4);
        WeaponDummyEnemy first = CreateEnemy(Origin + Vector3.right, 10);
        Collider ground = CreateGround();
        Projectile parent = Launch(h, explosion: true);
        parent.ConfigureExplosionCluster(h.Pool, 3, 5, 2f, 0f, 0f, 1f, 3f, 0f, 0f, 0f);
        Collide(parent, ground);
        Projectile[] children = h.Container.GetComponentsInChildren<Projectile>();
        Assert.That(children.Length, Is.EqualTo(3));
        Assert.That(h.Pool.ActiveLeasedCount, Is.EqualTo(3));
        first.Configure(100, 0f, WeaponEnemyKind.Normal, false, WeaponSandboxMovementPattern.None, null, null);
        foreach (Projectile child in children)
        {
            child.transform.position = Origin;
            Collide(child, ground);
        }
        Assert.That(first.CurrentHealth, Is.EqualTo(85));
        Assert.That(h.Sink.Impacts.Count, Is.EqualTo(1));
        Assert.That(h.Sink.LegacyCues, Is.EqualTo(new[]
        {
            WeaponPresentationCue.RocketFragmentChildImpact,
            WeaponPresentationCue.RocketFragmentChildImpact,
            WeaponPresentationCue.RocketFragmentChildImpact
        }));
        Assert.That(h.Sink.Damage.Count, Is.EqualTo(4));
        Assert.That(h.Pool.ActiveLeasedCount, Is.Zero);
        FlushOwnDeaths();
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(4), "One parent blast and three intentional child blasts remain visible.");
    }

    private static IEnumerable<TestCaseData> WeaponModes()
    {
        foreach (WeaponType type in System.Enum.GetValues(typeof(WeaponType)))
            foreach (WeaponFeedbackMode mode in System.Enum.GetValues(typeof(WeaponFeedbackMode)))
                yield return new TestCaseData(type, mode);
    }

    [Test]
    public void SharedCue_ThrottlesImpactAndKillFeedbackIndependentlyAndResetsBoth()
    {
        Harness h = CreateHarness(WeaponType.RocketLauncher, customProfile: true, preserveDamageChannels: true);
        GameObject target = Track(new GameObject("Shared impact and kill cue target"));
        WeaponFeedbackContext kill = new(h.Weapon, WeaponFeedbackMode.Manual, 0f, Vector3.zero, Vector3.forward,
            damageAmount: 10, isKill: true, target: target.transform);
        h.Sink.OnProjectileImpact(in kill);
        h.Sink.OnDamageConfirmed(in kill);
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
        Assert.That(h.Director.ActiveAudioVoiceCount, Is.EqualTo(2), "An impact must not consume the kill feedback cooldown.");
        h.Sink.OnDamageConfirmed(in kill);
        h.Sink.OnProjectileImpact(in kill);
        Assert.That(h.Director.ActiveAudioVoiceCount, Is.EqualTo(2), "Both channels retain their authored replay interval.");
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1));
        h.Director.StopAll();
        h.Sink.OnDamageConfirmed(in kill);
        h.Sink.OnProjectileImpact(in kill);
        Assert.That(h.Director.ActiveAudioVoiceCount, Is.EqualTo(2));
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1), "A reset clears both cooldowns without losing the physical impact.");
    }

    [TestCaseSource(nameof(WeaponModes))]
    public void SharedDamageConfirmation_DoesNotSpawnWeaponVfxForAnyWeaponOrMode(WeaponType type, WeaponFeedbackMode mode)
    {
        Harness h = CreateHarness(type, customProfile: true, preserveDamageChannels: true);
        GameObject target = Track(new GameObject("Confirmed kill feedback target"));
        EnemyHitFeedback hit = target.AddComponent<EnemyHitFeedback>();
        WeaponFeedbackContext kill = new(h.Weapon, mode, 0f, Vector3.zero, Vector3.forward,
            damageAmount: 10, isKill: true, target: target.transform, referenceDamage: 10);
        h.Sink.OnDamageConfirmed(in kill);
        Assert.That(hit.IsPlaying, Is.True);
        Assert.That(hit.CurrentTier, Is.EqualTo(EnemyReactionTier.Kill));
        Assert.That(h.HitStop.IsActive, Is.True);
        Assert.That(h.Text.ActiveAggregateCount, Is.EqualTo(1));
        Assert.That(h.Director.ActiveAudioVoiceCount, Is.EqualTo(1));
        Assert.That(h.Director.ActiveVfxCount, Is.Zero, "Kills use enemy reactions; weapon VFX belong to physical presentation events.");
        h.Director.EmitSemantic(WeaponFeedbackEvent.ProjectileImpact, in kill, 1f, 1f);
        Assert.That(h.Director.ActiveVfxCount, Is.EqualTo(1), "The physical impact must remain available after confirming a kill.");
    }

    private Harness CreateHarness(WeaponType type, int poolSize = 1, bool customProfile = false, bool preserveDamageChannels = false)
    {
        WeaponPresentationProfile profile;
        if (customProfile)
        {
            profile = Track(ScriptableObject.CreateInstance<WeaponPresentationProfile>());
            GameObject prefab = Track(new GameObject("Impact regression VFX prefab"));
            prefab.SetActive(false);
            var cue = new WeaponPresentationCueData
            {
                Cue = WeaponPresentationCue.RocketImpact, VfxPrefab = prefab, Duration = 2f,
                MaxSimultaneous = 16, MinReplayInterval = .12f, HitStopDuration = .04f,
                AudioClips = new List<AudioClip> { Track(AudioClip.Create("Kill feedback audio", 4410, 1, 44100, false)) }
            };
            Get<List<WeaponPresentationCueData>>(profile, "_cues").Add(cue);
            Get<List<WeaponFeedbackBinding>>(profile, "_feedbackBindings").Add(new WeaponFeedbackBinding
            { Event = WeaponFeedbackEvent.DamageConfirmed, Kill = FeedbackFilter.Required, Cue = cue.Cue });
            Get<List<WeaponFeedbackBinding>>(profile, "_feedbackBindings").Add(new WeaponFeedbackBinding
            { Event = WeaponFeedbackEvent.ProjectileImpact, Cue = cue.Cue });
        }
        else
        {
            profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(
                $"Assets/ScriptableObjects/WeaponPresentation/{type}Presentation.asset");
            Assert.That(profile, Is.Not.Null);
        }
        profile.RebuildCache();
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.BaseDamage = 20f;
        data.PresentationProfile = profile;
        GameObject runtime = Track(new GameObject("Impact regression feedback runtime"));
        var options = new GameFeelRuntimeOptions
        {
            CameraFeedbackEnabled = false, AudioEnabled = preserveDamageChannels,
            HitStopEnabled = preserveDamageChannels, EnemyReactionEnabled = true
        };
        CombatTextDirector text = null;
        if (preserveDamageChannels)
        {
            Camera camera = Track(new GameObject("Impact combat text camera")).AddComponent<Camera>();
            camera.transform.position = Vector3.back * 10f;
            var textProfile = Track(ScriptableObject.CreateInstance<CombatTextProfile>());
            textProfile.ViewPrefab = null;
            text = new CombatTextDirector(runtime.transform, camera, textProfile, options);
            _combatText.Add(text);
        }
        var hitStop = new HitStopController();
        var director = new CombatFeedbackDirector(profile, runtime.transform, null, null, options,
            new CameraFeedbackController(), hitStop, 4, 0f, text);
        _directors.Add(director);
        GameObject prefabObject = Track(new GameObject("Impact regression projectile prefab"));
        prefabObject.SetActive(false);
        prefabObject.AddComponent<Projectile>();
        GameObject container = Track(new GameObject("Impact regression pool container"));
        GameObject poolObject = Track(new GameObject("Impact regression pool"));
        poolObject.SetActive(false);
        ProjectilePool pool = poolObject.AddComponent<ProjectilePool>();
        Set(pool, "_projectilePrefab", prefabObject);
        Set(pool, "_container", container.transform);
        Set(pool, "_initialPoolSize", poolSize);
        Set(pool, "_maxPoolSize", poolSize);
        Call(pool, "Awake");
        return new Harness
        {
            Director = director, HitStop = hitStop, Text = text, Pool = pool, Container = container.transform,
            Weapon = new WeaponInstance { Data = data, State = WeaponState.Manual }, Sink = new ForwardingSink(director)
        };
    }

    private Projectile Launch(Harness h, bool explosion, int damage = 20)
    {
        GameObject instance = h.Pool.TryGet();
        Assert.That(instance, Is.Not.Null);
        instance.transform.position = Origin;
        Projectile projectile = instance.GetComponent<Projectile>();
        projectile.ConfigurePooled(3f, damage, 0f);
        projectile.Launch(Vector3.forward);
        if (explosion) projectile.ConfigureExplosion(4f, 0f);
        WeaponFeedbackContext launch = new(h.Weapon, WeaponFeedbackMode.Manual, 0f, Origin, Vector3.forward,
            explosionRadius: explosion ? 4f : 0f);
        projectile.ConfigureFeedback(h.Sink, in launch, replaceExplosionVfx: true);
        return projectile;
    }

    private WeaponDummyEnemy CreateEnemy(Vector3 position, int health)
    {
        GameObject target = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        target.SetActive(false);
        target.transform.position = position;
        WeaponDummyEnemy dummy = target.AddComponent<WeaponDummyEnemy>();
        EnemyDeathFeedback death = target.AddComponent<EnemyDeathFeedback>();
        Call(death, "Awake");
        dummy.Configure(health, 0f, WeaponEnemyKind.Normal, false, WeaponSandboxMovementPattern.None, null, null);
        Call(death, "OnDisable");
        Call(death, "OnEnable");
        _deathIds.Add(target.GetInstanceID());
        return dummy;
    }

    private Collider CreateGround()
    {
        GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        ground.name = "Impact regression ground";
        ground.transform.position = Origin + Vector3.down * .1f;
        ground.transform.localScale = new Vector3(10f, .1f, 10f);
        return ground.GetComponent<Collider>();
    }

    private static void Collide(Projectile projectile, Collider collider)
    {
        Physics.SyncTransforms();
        Call(projectile, "OnTriggerEnter", collider);
    }

    private void FlushOwnDeaths()
    {
        IDictionary pending = GetStatic<IDictionary>("s_pending");
        var active = GetStatic<HashSet<EnemyDeathReactionVfx>>("s_active");
        var before = new HashSet<EnemyDeathReactionVfx>(active);
        foreach (int id in _deathIds)
        {
            if (!pending.Contains(id)) continue;
            object death = pending[id];
            // Advancing the deferred death cue explicitly keeps this a synchronous EditMode test.
            death.GetType().GetField("Frame").SetValue(death, -1);
            pending[id] = death;
        }
        typeof(EnemyDeathReactionVfx).GetMethod("FlushPending", StaticPrivate).Invoke(null, null);
        foreach (EnemyDeathReactionVfx effect in active)
            if (!before.Contains(effect)) _deathEffects.Add(effect);
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static T GetStatic<T>(string field) => (T)typeof(EnemyDeathReactionVfx).GetField(field, StaticPrivate).GetValue(null);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);

    private sealed class Harness
    {
        public WeaponInstance Weapon;
        public CombatFeedbackDirector Director;
        public ForwardingSink Sink;
        public HitStopController HitStop;
        public CombatTextDirector Text;
        public ProjectilePool Pool;
        public Transform Container;
    }

    private sealed class ForwardingSink : IWeaponFeedbackSink
    {
        private readonly CombatFeedbackDirector _director;
        private float _now = 1f;
        public readonly List<WeaponFeedbackContext> Impacts = new();
        public readonly List<WeaponFeedbackContext> Damage = new();
        public readonly List<WeaponPresentationCue> LegacyCues = new();
        public ForwardingSink(CombatFeedbackDirector director) { _director = director; }
        // An explosion and all its damage confirmations occur in the same frame in production.
        public void AdvanceTime() => _now += 1f;
        public void Emit(in WeaponPresentationContext context) { LegacyCues.Add(context.Cue); _director.EmitLegacy(in context, 1f, _now); }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { Impacts.Add(context); _director.EmitSemantic(WeaponFeedbackEvent.ProjectileImpact, in context, 1f, _now); }
        public void OnDamageConfirmed(in WeaponFeedbackContext context) { Damage.Add(context); _director.EmitSemantic(WeaponFeedbackEvent.DamageConfirmed, in context, 1f, _now); }
        public void OnShotFired(in WeaponFeedbackContext context) { }
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
}
