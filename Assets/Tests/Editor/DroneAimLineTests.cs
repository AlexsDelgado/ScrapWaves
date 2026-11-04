using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class DroneAimLineTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene _scene;
    private GameObject _root, _target, _bullet;
    private FlyingRangedBehavior _drone;
    private EnemyHealth _health;
    private EnemyProjectilePool _pool, _previousPool;
    private float _timeScale;
    private int _pauseLocks;
    private bool _startupHeld;
    private UnityEngine.Random.State _random;
    private PresentationAccessibilityState _preferences;

    [SetUp]
    public void SetUp()
    {
        _timeScale = Time.timeScale; _pauseLocks = GameplayPause.LockCount;
        _startupHeld = EntranceCinematic.StartupHeld; _random = UnityEngine.Random.state;
        _preferences = PresentationAccessibilityRuntime.Current;
        Time.timeScale = 1f; GameplayPause.Reset();
        typeof(EntranceCinematic).GetField("<StartupHeld>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
        _scene = EditorSceneManager.NewPreviewScene();
        _root = Make("Aim test drone");
        _root.transform.position = new Vector3(9000, 9003, 9000);
        _health = _root.AddComponent<EnemyHealth>();
        _health.ApplyConfiguredMaxHealth(12);
        _target = Make("Aim test player");
        _target.transform.position = new Vector3(9000, 9000, 9007);
        _bullet = Make("Aim test projectile stand-in"); _bullet.SetActive(false);
        _drone = _root.AddComponent<FlyingRangedBehavior>();
        Call(_drone, "Awake"); Call(_drone, "OnEnable");
        typeof(EnemyBehaviorBase).GetField("_player", Hidden).SetValue(_drone, _target.transform);
        Set(_drone, "_bulletPrefab", _bullet); Set(_drone, "_moveSpeed", 0f);
        _previousPool = EnemyProjectilePool.Instance;
        _pool = Make("Aim test pool").AddComponent<EnemyProjectilePool>();
        Set(_pool, "_container", _pool.transform); Set(_pool, "_initialPoolSize", 1);
        PoolInstance().SetValue(null, _pool);
    }

    [TearDown]
    public void TearDown()
    {
        if (_drone != null) { Call(_drone, "OnDisable"); Call(_drone, "OnDestroy"); }
        if (_root != null && _root.TryGetComponent(out EnemyVerticalEngagement engagement)) Call(engagement, "OnDisable");
        foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
        EditorSceneManager.ClosePreviewScene(_scene);
        PoolInstance().SetValue(null, _previousPool);
        Time.timeScale = _timeScale; GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
        typeof(EntranceCinematic).GetField("<StartupHeld>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _startupHeld);
        PresentationAccessibilityRuntime.Apply(_preferences); UnityEngine.Random.state = _random;
    }

    [Test]
    public void Locking_TracksTheActualMuzzleAndTarget_WithoutFiringOrChangingTimer()
    {
        Call(_drone, "LateUpdate");
        Assert.That(_root.GetComponentsInChildren<LineRenderer>(true), Is.Empty);
        Call(_drone, "TickApproaching");
        Set(_drone, "_stateTimer", .5f);
        float timer = Get<float>(_drone, "_stateTimer");
        Call(_drone, "LateUpdate");
        LineRenderer line = Line();
        Assert.That(line.enabled, Is.True);
        Assert.That(line.GetPosition(0), Is.EqualTo(_root.transform.position + Vector3.up * .2f));
        AssertExtendsPastTarget(line);
        _target.transform.position += new Vector3(2, 4, -1);
        Call(_drone, "LateUpdate");
        AssertExtendsPastTarget(line);
        Assert.That(Get<float>(_drone, "_stateTimer"), Is.EqualTo(timer));
        Assert.That(_pool.transform.childCount, Is.Zero);
        Assert.That(line.GetComponent<Collider>(), Is.Null);
    }

    [Test]
    public void Firing_UsesTheTelegraphedDirection_AndHidesDuringCooldown()
    {
        Lock();
        Vector3 origin = Line().GetPosition(0), target = Line().GetPosition(1);
        Set(_drone, "_stateTimer", 0f);
        Call(_drone, "TickLocking");
        Call(_drone, "LateUpdate");
        Assert.That(Get<object>(_drone, "_state").ToString(), Is.EqualTo("Cooldown"));
        Assert.That(Line().enabled, Is.False);
        Assert.That(_pool.transform.childCount, Is.EqualTo(1));
        Transform shot = _pool.transform.GetChild(0);
        Assert.That(shot.position, Is.EqualTo(origin));
        Assert.That(Vector3.Dot(shot.forward, (target-origin).normalized), Is.GreaterThan(.9999f));
        Assert.That(Get<int>(_drone, "_bulletDamage"), Is.EqualTo(8));
        Assert.That(Get<float>(_drone, "_bulletSpeed"), Is.EqualTo(16f));
    }

    [TestCase("death")]
    [TestCase("inactive-target")]
    [TestCase("disabled")]
    [TestCase("pause")]
    [TestCase("time-scale")]
    [TestCase("disengaged")]
    [TestCase("emergence")]
    [TestCase("option-off")]
    public void InterruptedAttack_CannotLeaveAnOrphanLine(string reason)
    {
        Lock(); Assert.That(Line().enabled, Is.True);
        switch (reason)
        {
            case "death": Set(_health, "_currentHealth", 0); break;
            case "inactive-target": _target.SetActive(false); break;
            case "disabled": _drone.enabled = false; Call(_drone, "OnDisable"); break;
            case "pause": GameplayPause.Push(); break;
            case "time-scale": Time.timeScale = 0f; break;
            case "disengaged":
                var engagement = _root.AddComponent<EnemyVerticalEngagement>();
                Call(engagement, "Awake");
                typeof(EnemyVerticalEngagement).GetMethod("SetDisengaged", Hidden).Invoke(engagement, new object[] { true });
                break;
            case "emergence":
                var emergence = _root.AddComponent<EnemyEmergence>();
                Call(emergence, "Awake"); Call(emergence, "OnEnable");
                break;
            case "option-off": Set(_drone, "_showLockingLine", false); break;
        }
        Call(_drone, "LateUpdate");
        Assert.That(Line().enabled, Is.False);
    }

    [Test]
    public void RepeatedAttacksAndPoolReuse_KeepOneRendererAndSharedMaterial()
    {
        Lock(); var first = Line(); var material = first.sharedMaterial;
        for (int i = 0; i < 100; i++)
        {
            _drone.OnPoolDespawn(); Assert.That(first.enabled, Is.False);
            Call(_drone, "OnDisable"); Call(_drone, "OnEnable"); _drone.OnPoolSpawn();
            Call(_drone, "LateUpdate"); Assert.That(first.enabled, Is.False);
            Lock();
            Assert.That(Line(), Is.SameAs(first));
            Assert.That(Line().sharedMaterial, Is.SameAs(material));
        }
        Assert.That(_root.GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(1));
        Call(_drone, "OnDestroy");
        Assert.That(first == null, Is.True);
    }

    [Test]
    public void Accessibility_KeepsAReadableSteadyWarningWithoutMaterialPulses()
    {
        Lock(); var line = Line();
        Assert.That(line.sharedMaterial.GetFloat("_Pulse"), Is.Zero);
        foreach (var state in new[] { _preferences.WithReducedMotion(true), _preferences.WithReducedFlash(true) })
        {
            PresentationAccessibilityRuntime.Apply(state);
            Set(_drone, "_stateTimer", .5f); Call(_drone, "LateUpdate");
            Color color = line.startColor; float width = line.widthMultiplier;
            Set(_drone, "_stateTimer", .01f); Call(_drone, "LateUpdate");
            Assert.That(line.startColor, Is.EqualTo(color));
            Assert.That(line.widthMultiplier, Is.EqualTo(width));
            Assert.That(line.enabled, Is.True);
        }
    }

    [Test]
    public void AuthoredDroneAndBuildResource_AreReadyWithoutChangingAttackBalance()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Drone.prefab");
        var serialized = new SerializedObject(prefab.GetComponent<FlyingRangedBehavior>());
        Assert.That(serialized.FindProperty("_showLockingLine").boolValue, Is.True);
        Assert.That(serialized.FindProperty("_lockSeconds").floatValue, Is.EqualTo(2f));
        var material = Resources.Load<Material>("VFX/DroneAimLine");
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.isSupported, Is.True);
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
    }

    [Test]
    public void ChargeBlink_AcceleratesTowardsTheShot_AndRestartsForTheNextAttack()
    {
        Lock();
        int early = 0, late = 0;
        float previous = 1f;
        for (int sample = 0; sample < 300; sample++)
        {
            float elapsed = sample * (.5f / 300f);
            Set(_drone, "_stateTimer", .5f - elapsed);
            Call(_drone, "LateUpdate");
            float alpha = Line().startColor.a;
            Assert.That(alpha, Is.InRange(.05f, 1f));
            if (previous < .4f && alpha >= .4f)
            {
                if (elapsed < .25f) early++; else late++;
            }
            previous = alpha;
        }
        Assert.That(late, Is.GreaterThan(early), "More flashes in the last half of the same charge.");
        Lock();
        Assert.That(Line().startColor.a, Is.EqualTo(.6f).Within(.01f));
    }

    [TestCase(.8f)]
    [TestCase(1.2f)]
    public void ProductionModel_LineAndProjectileStartInsideTheVisibleDrone(float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Drone.prefab");
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
        var behavior = actor.GetComponent<FlyingRangedBehavior>();
        try
        {
            actor.transform.position = new Vector3(9000, 9003, 9000);
            actor.transform.localScale = Vector3.one * scale;
            actor.transform.rotation = Quaternion.Euler(0, 40, 0);
            actor.GetComponent<EnemyHealth>().ApplyConfiguredMaxHealth(12);
            Set(actor.GetComponent<EnemyEmergence>(), "_excluded", true);
            Call(behavior, "Awake"); Call(behavior, "OnEnable");
            typeof(EnemyBehaviorBase).GetField("_player", Hidden).SetValue(behavior, _target.transform);
            Set(behavior, "_bulletPrefab", _bullet); Set(behavior, "_moveSpeed", 0f);
            Call(behavior, "TickApproaching"); Set(behavior, "_stateTimer", .5f); Call(behavior, "LateUpdate");
            var line = actor.GetComponentInChildren<LineRenderer>();
            var body = actor.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(line, Is.Not.Null);
            Assert.That(line.enabled, Is.True);
            Assert.That(body.bounds.Contains(line.GetPosition(0)), Is.True);
            Assert.That(line.GetPosition(0).y, Is.GreaterThan(actor.transform.position.y + .5f),
                "Do not fall back to the empty root-space muzzle below the mesh.");
            Vector3 before = line.GetPosition(0);
            actor.transform.position += new Vector3(1,2,0);
            Call(behavior, "LateUpdate");
            Assert.That(Vector3.Distance(line.GetPosition(0),before+new Vector3(1,2,0)), Is.LessThan(.01f));
            Vector3 origin = line.GetPosition(0), target = line.GetPosition(1);
            // TickLocking turns the drone toward the player with Time.deltaTime, which moves the muzzle slightly;
            // freeze the turn so the test compares the line origin and the shot origin of the same pose.
            Set(behavior, "_rotationSpeed", 0f);
            Set(behavior, "_stateTimer", 0f); Call(behavior, "TickLocking"); Call(behavior, "LateUpdate");
            var shot = _pool.transform.GetChild(0);
            Assert.That(shot.position, Is.EqualTo(origin));
            Assert.That(Vector3.Dot(shot.forward,(target-origin).normalized), Is.GreaterThan(.9999f));
            Assert.That(line.enabled, Is.False);
        }
        finally
        {
            Call(behavior, "OnDisable"); Call(behavior, "OnDestroy"); Object.DestroyImmediate(actor);
        }
    }

    [TestCase(.501f, false)]
    [TestCase(.5f, true)]
    [TestCase(.25f, true)]
    [TestCase(.001f, true)]
    [TestCase(0f, false)]
    public void NormalAndElite_OnlyWarnDuringTheFinalHalfSecond(float remaining, bool visible)
    {
        Call(_drone, "TickApproaching");
        Set(_drone, "_stateTimer", remaining);
        Call(_drone, "LateUpdate");
        Assert.That(Line() != null && Line().enabled, Is.EqualTo(visible));
        var elite = Elite();
        Set(elite, "_shootTimer", remaining);
        Call(elite, "LateUpdate");
        var line = elite.GetComponentInChildren<LineRenderer>(true);
        Assert.That(line != null && line.enabled, Is.EqualTo(visible));
        Assert.That(Get<float>(elite, "_shootTimer"), Is.EqualTo(remaining));
        Assert.That(_pool.transform.childCount, Is.Zero);
        if (visible) { AssertExtendsPastTarget(Line()); AssertExtendsPastTarget(line); }
    }

    [Test]
    public void Elite_ShootsFromItsVisibleBodyAlongTheLine_ThenReusesIt()
    {
        var elite = Elite();
        Call(elite, "LateUpdate");
        var line = elite.GetComponentInChildren<LineRenderer>(true);
        var body = elite.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.That(line.enabled, Is.True);
        Assert.That(body.bounds.Contains(line.GetPosition(0)), Is.True);
        Vector3 origin = line.GetPosition(0), direction = (line.GetPosition(1) - origin).normalized;
        Set(elite, "_shootTimer", 0f);
        Call(elite, "TickDronePhase"); Call(elite, "LateUpdate");
        Assert.That(line.enabled, Is.False);
        var shot = _pool.transform.GetChild(0);
        Assert.That(shot.position, Is.EqualTo(origin));
        Assert.That(Vector3.Dot(shot.forward, direction), Is.GreaterThan(.9999f));
        Assert.That(Get<float>(elite, "_shootTimer"), Is.EqualTo(1.5f));
        Assert.That(Get<int>(elite, "_bulletDamage"), Is.EqualTo(35));
        Set(elite, "_shootTimer", .5f); Call(elite, "LateUpdate");
        Assert.That(elite.GetComponentInChildren<LineRenderer>(true), Is.SameAs(line));
        Assert.That(line.enabled, Is.True);
    }

    [TestCase("Engage")]
    [TestCase("Dash")]
    [TestCase("BetweenDash")]
    [TestCase("phase-ending")]
    [TestCase("pool")]
    [TestCase("death")]
    [TestCase("pause")]
    [TestCase("disabled")]
    public void Elite_HidesWarningsWhenNoBulletIsAboutToFire(string reason)
    {
        var elite = Elite(); Call(elite, "LateUpdate");
        var line = elite.GetComponentInChildren<LineRenderer>(true);
        Assert.That(line.enabled, Is.True);
        switch (reason)
        {
            case "phase-ending": Set(elite, "_stateTimer", .2f); break;
            case "pool": elite.OnPoolDespawn(); break;
            case "death": Set(elite.GetComponent<EnemyHealth>(), "_currentHealth", 0); break;
            case "pause": GameplayPause.Push(); break;
            case "disabled": elite.enabled = false; Call(elite, "OnDisable"); break;
            default:
                var field = typeof(BomberDroneBehavior).GetField("_state", Hidden);
                field.SetValue(elite, Enum.Parse(field.FieldType, reason)); break;
        }
        if (reason != "pool") Call(elite, "LateUpdate");
        Assert.That(line.enabled, Is.False);
        Assert.That(_pool.transform.childCount, Is.Zero);
    }

    private BomberDroneBehavior Elite()
    {
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Drone_Elite.prefab"), _scene);
        foreach (var script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        actor.transform.position = _root.transform.position;
        var behavior = actor.GetComponent<BomberDroneBehavior>(); behavior.enabled = true;
        actor.GetComponent<EnemyHealth>().ApplyConfiguredMaxHealth(30);
        Set(actor.GetComponent<EnemyEmergence>(), "_excluded", true);
        Call(behavior, "Awake"); Call(behavior, "OnEnable");
        typeof(EnemyBehaviorBase).GetField("_player", Hidden).SetValue(behavior, _target.transform);
        Set(behavior, "_bulletPrefab", _bullet); Set(behavior, "_moveSpeed", 0f);
        var state = typeof(BomberDroneBehavior).GetField("_state", Hidden);
        state.SetValue(behavior, Enum.Parse(state.FieldType, "DronePhase"));
        Set(behavior, "_shootTimer", .5f); Set(behavior, "_stateTimer", 5f);
        return behavior;
    }

    private void Lock() { Call(_drone, "TickApproaching"); Set(_drone, "_stateTimer", .5f); Call(_drone, "LateUpdate"); }
    private void AssertExtendsPastTarget(LineRenderer line)
    {
        Vector3 direction = (_target.transform.position - line.GetPosition(0)).normalized;
        Vector3 beyond = line.GetPosition(1) - _target.transform.position;
        Assert.That(Vector3.Dot(beyond.normalized, direction), Is.GreaterThan(.9999f));
        Assert.That(beyond.magnitude, Is.EqualTo(12f).Within(.01f));
    }
    private LineRenderer Line() => _root.GetComponentInChildren<LineRenderer>(true);
    private GameObject Make(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, _scene); return go; }
    private static FieldInfo PoolInstance() => typeof(EnemyProjectilePool).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static);
    private static void Set(object value, string name, object data) => value.GetType().GetField(name, Hidden).SetValue(value, data);
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
    private static void Call(object value, string name) => value.GetType().GetMethod(name, Hidden).Invoke(value, null);
}
