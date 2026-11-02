using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>One health owner behind seven body colliders and a separately multiplied head.</summary>
public sealed class StalkerSegmentDamageTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _cleanup = new();
    private readonly HashSet<int> _existingVisuals = new();
    private static readonly Vector3 Origin = new(31000f, 1000f, 31000f);
    private GameObject _boss;
    private EnemyHealth _health;
    private EnemyDamageHitZone _head;
    private Collider _headCollider;
    private readonly List<Collider> _bodyColliders = new();
    private SaveManager _previousSaveManager;
    private ChallengeProgressTracker _previousTracker;
    private string _previousWeaponCredit;
    private Random.State _randomState;

    [SetUp]
    public void SetUp()
    {
        _randomState = Random.state;
        _previousSaveManager = SaveManager.Instance;
        typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, null);
        _previousTracker = ChallengeProgressTracker.Instance;
        if (_previousTracker == null)
        {
            var tracker = Track(new GameObject("Segment damage test tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
        _previousWeaponCredit = WeaponDamageApplier.PendingSourceWeaponId;
        WeaponDamageApplier.ClearPendingWeapon();
        foreach (var visual in Object.FindObjectsByType<ExplosionRadiusVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            _existingVisuals.Add(visual.GetInstanceID());
        foreach (var visual in Object.FindObjectsByType<EnemyStatusVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            _existingVisuals.Add(visual.GetInstanceID());

        _boss = Track(new GameObject("Segmented boss damage owner"));
        _boss.transform.position = Origin;
        _health = _boss.AddComponent<EnemyHealth>();
        _health.ApplyConfiguredMaxHealth(1000);
        var collisionRoot = new GameObject("SegmentColliders");
        collisionRoot.transform.SetParent(_boss.transform, false);
        for (int i = 0; i < 7; i++)
        {
            var piece = new GameObject(i == 6 ? "Tail" : $"Body{i + 1:00}");
            piece.transform.SetParent(collisionRoot.transform, false);
            piece.transform.localPosition = Vector3.down * (0.15f * i);
            _bodyColliders.Add(piece.AddComponent<BoxCollider>());
        }
        var head = new GameObject("Head");
        head.transform.SetParent(_boss.transform, false);
        head.transform.localPosition = Vector3.up * 0.2f;
        _headCollider = head.AddComponent<SphereCollider>();
        _head = head.AddComponent<EnemyDamageHitZone>();
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        // Edit Mode does not dispatch the status visual's Awake/OnDestroy budget pair.
        foreach (var visual in Object.FindObjectsByType<EnemyStatusVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (_existingVisuals.Contains(visual.GetInstanceID())) continue;
            Invoke(visual, "OnDestroy");
            SetField(visual, "_counted", false);
            Object.DestroyImmediate(visual.gameObject);
        }
        foreach (var visual in Object.FindObjectsByType<ExplosionRadiusVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!_existingVisuals.Contains(visual.GetInstanceID())) Object.DestroyImmediate(visual.gameObject);
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        _bodyColliders.Clear();
        _existingVisuals.Clear();
        typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, _previousSaveManager);
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _previousTracker);
        WeaponDamageApplier.PendingSourceWeaponId = _previousWeaponCredit;
        Random.state = _randomState;
    }

    [Test]
    public void DirectHits_ShareHealthButRetainHeadMultiplierAndIndependentHits()
    {
        Assert.That(WeaponDamageApplier.GetDamageIdentity(_head), Is.SameAs(_health));
        foreach (Collider collider in _bodyColliders)
            Assert.That(WeaponDamageApplier.GetDamageIdentity(collider.GetComponentInParent<IDamageable>()), Is.SameAs(_health));

        Assert.That(WeaponDamageApplier.ApplyDamage(_head, 10).AppliedDamage, Is.EqualTo(12));
        Assert.That(WeaponDamageApplier.ApplyDamage(_health, 10).AppliedDamage, Is.EqualTo(10));
        Assert.That(WeaponDamageApplier.ApplyDamage(_head, 10).AppliedDamage, Is.EqualTo(12));
        Assert.That(_health.CurrentHealth, Is.EqualTo(966));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RadialDamage_AllSegmentsAndHeadCountOncePerCall(bool useContext)
    {
        WeaponDamageContext context = CreateDamageContext(out _, out _);
        for (int tick = 1; tick <= 2; tick++)
        {
            int hits = useContext
                ? WeaponRadialDamage.Apply(Origin, 3f, context, 0f, 128, false)
                : WeaponRadialDamage.Apply(Origin, 3f, 10, 0f, 0f, showVfx: false);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(_health.CurrentHealth, Is.EqualTo(1000 - 10 * tick));
        }
    }

    [Test]
    public void MortarExplosion_EachRepeatDamagesOneOwnerOnce()
    {
        var shell = Track(new GameObject("Segment mortar test")).AddComponent<MortarShellImpact>();
        SetField(shell, "_damage", 10);
        SetField(shell, "_explosionRadius", 3f);
        SetField(shell, "_falloff", 0f);
        for (int burst = 1; burst <= 3; burst++)
        {
            Invoke(shell, "ApplyExplosionDamageAt", Origin);
            Assert.That(_health.CurrentHealth, Is.EqualTo(1000 - 10 * burst));
        }
    }

    [Test]
    public void FlameAreas_AllSegmentsAndHeadCountOnceThenAllowNextTick()
    {
        var owner = Track(new GameObject("Segment flame owner"));
        owner.transform.position = Origin + Vector3.left * 10f;
        var areas = Track(new GameObject("Segment flame areas")).AddComponent<FlamethrowerManualAreas>();
        int hits = 0;
        areas.Initialize(owner.transform, (target, origin) =>
        {
            hits++;
            Assert.That(target, Is.SameAs(_health.transform));
            return WeaponDamageApplier.ApplyDamage(target.GetComponentInParent<IDamageable>(), 10).Applied;
        }, null);
        var tuning = new FlamethrowerTuning { FlameAreaInitialRadius = 2f, FlameAreaFinalRadius = 2f };
        areas.Emit(0.1f, Origin, Vector3.forward, 0f, 1f, tuning, FlamethrowerStreamStyle.Flame, 0f);
        areas.Simulate(0.1f);
        Assert.That(hits, Is.EqualTo(1));
        Assert.That(_health.CurrentHealth, Is.EqualTo(990));
        areas.Simulate(0.4f);
        Assert.That(hits, Is.EqualTo(2));
        Assert.That(_health.CurrentHealth, Is.EqualTo(980));
    }

    [Test]
    public void PooledProjectile_OnlyFirstColliderHitsAndReusedShotCanHitAgain()
    {
        var prefab = Track(new GameObject("Segment projectile template"));
        prefab.AddComponent<Projectile>();
        prefab.SetActive(false);
        var container = Track(new GameObject("Segment projectile pool objects"));
        var pool = Track(new GameObject("Segment projectile pool")).AddComponent<ProjectilePool>();
        SetField(pool, "_projectilePrefab", prefab);
        SetField(pool, "_container", container.transform);
        SetField(pool, "_maxPoolSize", 1);

        Assert.That(pool.TrySpawnProjectile(Origin, Quaternion.identity, Vector3.forward, 10), Is.True);
        Projectile projectile = container.GetComponentInChildren<Projectile>();
        Assert.That(projectile, Is.Not.Null);
        Invoke(projectile, "OnTriggerEnter", _headCollider);
        foreach (Collider collider in _bodyColliders) Invoke(projectile, "OnTriggerEnter", collider);
        Assert.That(_health.CurrentHealth, Is.EqualTo(988));
        Assert.That(pool.ActiveLeasedCount, Is.Zero);

        Assert.That(pool.TrySpawnProjectile(Origin, Quaternion.identity, Vector3.forward, 10), Is.True);
        Assert.That(container.GetComponentInChildren<Projectile>(), Is.SameAs(projectile));
        Invoke(projectile, "OnTriggerEnter", _bodyColliders[0]);
        Invoke(projectile, "OnTriggerEnter", _headCollider);
        Assert.That(_health.CurrentHealth, Is.EqualTo(978));
        Assert.That(pool.TotalPooledInstances, Is.EqualTo(1));
        Assert.That(pool.ActiveLeasedCount, Is.Zero);
    }

    [Test]
    public void ProjectileExplosion_AlreadyTreatsHeadAndBodyAsOneAreaTarget()
    {
        var projectile = Track(new GameObject("Segment explosive projectile")).AddComponent<Projectile>();
        projectile.transform.position = Origin;
        projectile.ConfigurePooled(3f, 10);
        projectile.ConfigureExplosion(3f, 0f);
        Invoke(projectile, "ApplyExplosionDamage");
        Assert.That(_health.CurrentHealth, Is.EqualTo(990));
        Invoke(projectile, "ApplyExplosionDamage");
        Assert.That(_health.CurrentHealth, Is.EqualTo(980));
    }

    [Test]
    public void Burn_AlternatingHeadAndBodyRefreshesOneStatusOwner()
    {
        WeaponDamageContext context = CreateDamageContext(out PlayerStats stats, out WeaponInstance instance);
        instance.Data.WeaponType = WeaponType.Flamethrower;
        instance.Data.EnsureSpecificTuningForCurrentType();
        var weapon = new FlamethrowerWeapon(null, null, stats.transform, null);
        weapon.Setup(instance, stats.transform, stats, null);
        var tuning = new FlamethrowerTuning { FlameBurnDuration = 3f, FlameBurnTickInterval = 0.5f };

        Invoke(weapon, "ApplyBurnToTargetWithContext", _head.transform, 10, tuning, false, context);
        var original = _health.GetComponent<FlamethrowerBurnStatus>();
        Assert.That(original, Is.Not.Null);
        Invoke(weapon, "ApplyBurnToTargetWithContext", _bodyColliders[0].transform, 10, tuning, false, context);
        Assert.That(_boss.GetComponentsInChildren<FlamethrowerBurnStatus>(true), Has.Length.EqualTo(1));
        Assert.That(_health.GetComponent<FlamethrowerBurnStatus>(), Is.SameAs(original));
        original.Tick(0.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(990));
        original.Tick(0.5f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(980));
    }

    private WeaponDamageContext CreateDamageContext(out PlayerStats stats, out WeaponInstance instance)
    {
        stats = Track(new GameObject("Segment damage stats")).AddComponent<PlayerStats>();
        stats.transform.position = Origin;
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier,
            StatType.CriticalChance, StatType.CriticalDamage, StatType.AbilityDamageMultiplier,
            StatType.CloseRangeDamageMultiplier, StatType.LongRangeDamageMultiplier,
            StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityCooldownReduction })
        {
            StatDefinition definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField",
                type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        Invoke(stats, "Awake");
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponId = "stalker-segment-damage-test";
        data.BaseDamage = 10f;
        data.BaseKnockback = 0f;
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };
        instance = new WeaponInstance { Data = data, Level = 1, State = WeaponState.Manual };
        return new WeaponDamageContext(stats, instance, false, 1f, 1f, false, 0f, useModeDamageOverride: false);
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void SetField(object target, string field, object value) => target.GetType()
        .GetField(field, PrivateInstance).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
        .GetMethod(method, PrivateInstance).Invoke(target, arguments);
}
