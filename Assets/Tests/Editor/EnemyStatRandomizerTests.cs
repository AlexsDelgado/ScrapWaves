using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public sealed class EnemyStatRandomizerTests
{
    private readonly List<GameObject> _created = new();
    private readonly EnemyEmergenceTestLifecycle _lifecycle = new();
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
            if (_created[i] != null) _lifecycle.Destroy(_created[i]);
        _created.Clear();
        Random.state = _randomState;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void DefaultRanges_PreserveAuthoredHealthAndNonUnitScaleWithoutManagers()
    {
        EnemyStatRandomizer randomizer = NewEnemy(37, new Vector3(2f, 3f, 4f));
        randomizer.BeginSpawn();
        Assert.That(randomizer.HasSampledForSpawn, Is.True);
        Assert.That(Samples(randomizer), Is.EqualTo(Vector3.one));
        Assert.That(randomizer.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        Assert.That(randomizer.GetComponent<EnemyHealth>().MaxHealth, Is.EqualTo(37));
        Assert.That(randomizer.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(37));
    }

    [Test]
    public void RepeatedBeginSpawn_KeepsSamplesScaleAndDamageTaken()
    {
        EnemyStatRandomizer randomizer = NewEnemy(100);
        Configure(randomizer, new Vector2(.7f, 1.3f), new Vector2(.8f, 1.2f), new Vector2(.9f, 1.4f));
        Random.InitState(206);
        randomizer.BeginSpawn();
        Vector3 sampled = Samples(randomizer);
        Vector3 scale = randomizer.transform.localScale;
        EnemyHealth health = randomizer.GetComponent<EnemyHealth>();
        health.ApplyDotDamage(1);
        int remaining = health.CurrentHealth;
        randomizer.BeginSpawn();
        EnemyStatRandomizer.BeginSpawn(randomizer.gameObject);
        ((IEnemySpawnLifecycle)randomizer).OnPoolSpawn();
        Assert.That(Samples(randomizer), Is.EqualTo(sampled));
        Assert.That(randomizer.transform.localScale, Is.EqualTo(scale));
        Assert.That(health.CurrentHealth, Is.EqualTo(remaining), "Repeated preparation must not heal a living enemy.");
    }

    [Test]
    public void EachInstanceAndNewLifetime_SamplesIndependentStatsWithinItsRanges()
    {
        Random.InitState(206);
        EnemyStatRandomizer first = NewEnemy(100);
        EnemyStatRandomizer second = NewEnemy(100);
        Vector2 range = new(.5f, 1.5f);
        Configure(first, range, range, range);
        Configure(second, range, range, range);
        first.BeginSpawn();
        second.BeginSpawn();
        Vector3 initial = Samples(first);
        Assert.That(Samples(second), Is.Not.EqualTo(initial), "Instances must not share a wave-level roll.");
        Assert.That(initial.x, Is.Not.EqualTo(initial.y), "HP and size need separate draws.");
        Assert.That(initial.y, Is.Not.EqualTo(initial.z), "Size and movement speed need separate draws.");
        foreach (EnemyStatRandomizer enemy in new[] { first, second })
        {
            Assert.That(enemy.HealthMultiplier, Is.InRange(range.x, range.y));
            Assert.That(enemy.SizeMultiplier, Is.InRange(range.x, range.y));
            Assert.That(enemy.MovementSpeedMultiplier, Is.InRange(range.x, range.y));
        }
        first.EndSpawn();
        first.GetComponent<EnemyHealth>().PrepareForPoolSpawn();
        first.BeginSpawn();
        Assert.That(Samples(first), Is.Not.EqualTo(initial), "A new lease represents a new enemy lifetime.");
    }

    [TestCase("_healthMultiplierRange")]
    [TestCase("_sizeMultiplierRange")]
    [TestCase("_movementSpeedMultiplierRange")]
    public void InvalidRanges_AreNeutralAndReversedPositiveEndpointsAreSorted(string fieldName)
    {
        EnemyStatRandomizer randomizer = NewEnemy(100);
        foreach (Vector2 invalid in new[] { new Vector2(float.NaN, float.PositiveInfinity), new Vector2(0f, -2f) })
        {
            SetField(randomizer, fieldName, invalid);
            randomizer.BeginSpawn();
            Assert.That(Samples(randomizer), Is.EqualTo(Vector3.one), "Runtime validation must work without OnValidate.");
            randomizer.EndSpawn();
            randomizer.GetComponent<EnemyHealth>().PrepareForPoolSpawn();
        }
        SetField(randomizer, fieldName, new Vector2(1.4f, .8f));
        Invoke(randomizer, "OnValidate");
        Assert.That(GetField<Vector2>(randomizer, fieldName), Is.EqualTo(new Vector2(.8f, 1.4f)));
        randomizer.BeginSpawn();
        Vector3 samples = Samples(randomizer);
        Assert.That(samples.x, Is.InRange(.8f, 1.4f));
        Assert.That(samples.y, Is.InRange(.8f, 1.4f));
        Assert.That(samples.z, Is.InRange(.8f, 1.4f));
    }

    [Test]
    public void PoolReturn_RestoresAuthoredScaleAndDoesNotHealOrCompoundNextLease()
    {
        Vector3 authored = new(2f, 3f, 4f);
        EnemyStatRandomizer randomizer = NewEnemy(100, authored);
        SetFixed(randomizer, 1.5f, 1.25f, .8f);
        EnemyHealth health = randomizer.GetComponent<EnemyHealth>();
        for (int lease = 0; lease < 20; lease++)
        {
            health.PrepareForPoolSpawn();
            ((IEnemySpawnLifecycle)randomizer).OnPoolSpawn();
            Assert.That(randomizer.HasSampledForSpawn, Is.False, "Pool reset must not sample before placement.");
            randomizer.BeginSpawn();
            Assert.That(health.MaxHealth, Is.EqualTo(150));
            Assert.That(randomizer.transform.localScale, Is.EqualTo(authored * 1.25f));
            health.ApplyDotDamage(1);
            int remaining = health.CurrentHealth;
            ((IEnemySpawnLifecycle)randomizer).OnPoolDespawn();
            Assert.That(randomizer.HasSampledForSpawn, Is.False);
            Assert.That(randomizer.transform.localScale, Is.EqualTo(authored));
            Assert.That(health.CurrentHealth, Is.EqualTo(remaining), "Despawn cleanup must not resurrect or heal enemies.");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HealthMultiplier_ComposesWithDifficultyAndRoundsOnce(bool difficultyFirst)
    {
        EnemyStatRandomizer randomizer = NewEnemy(13);
        SetFixed(randomizer, 1.2f, 1f, 1f);
        EnemyHealth health = randomizer.GetComponent<EnemyHealth>();
        if (difficultyFirst) health.ConfigureDifficultyForSpawn(1.2f);
        randomizer.BeginSpawn();
        health.ConfigureDifficultyForSpawn(1.2f);
        health.ConfigureDifficultyForSpawn(1.2f);
        Assert.That(health.MaxHealth, Is.EqualTo(19), "Round 13 * 1.2 * 1.2 once, without compounding current HP.");
        Assert.That(health.CurrentHealth, Is.EqualTo(19));
        randomizer.EndSpawn();
        health.PrepareForPoolSpawn();
        SetFixed(randomizer, 1f, 1f, 1f);
        randomizer.BeginSpawn();
        Assert.That(health.MaxHealth, Is.EqualTo(13), "A new spawn must clear the previous difficulty multiplier.");
    }

    [Test]
    public void DifficultyApplication_PreservesRandomizationAndCompletedCycleScaling()
    {
        HeatManager heat = NewObject("Randomizer cycle manager").AddComponent<HeatManager>();
        SetField(heat, "_completedCycleHealthStep", .1f);
        SetField(heat, "_completedCycleHealthCeiling", 2f);
        _lifecycle.SetActive(heat.gameObject, true);
        heat.ApplyEscalationAfterOverheat();
        heat.ApplyEscalationAfterOverheat();
        DifficultyManager difficulty = NewObject("Randomizer difficulty manager").AddComponent<DifficultyManager>();
        SetField(difficulty, "_scalingStartDelaySeconds", 0f);
        SetField(difficulty, "_intensityOverMinutesAfterStart", AnimationCurve.Constant(0f, 100f, 1f));
        SetField(difficulty, "_maxEnemyHealthMultiplier", 2f);
        SetField(difficulty, "_scaleEnemyHealth", true);
        EnemyStatRandomizer randomizer = NewEnemy(100);
        SetFixed(randomizer, 1.5f, 1f, 1f);
        randomizer.BeginSpawn();
        difficulty.ApplySpawnModifiers(randomizer.gameObject);
        Assert.That(difficulty.GetEnemyHealthMultiplier(), Is.EqualTo(2f));
        Assert.That(heat.GetCompletedCycleHealthScale(), Is.EqualTo(1.2f).Within(.0001f));
        Assert.That(randomizer.GetComponent<EnemyHealth>().MaxHealth, Is.EqualTo(360));
    }

    [Test]
    public void DormancyAndComponentEnable_DoNotRerollOrResetTheCurrentLifetime()
    {
        EnemyStatRandomizer randomizer = NewEnemy(100);
        Configure(randomizer, new Vector2(.8f, 1.2f), new Vector2(.8f, 1.2f), new Vector2(.8f, 1.2f));
        EnemyVerticalEngagement engagement = randomizer.gameObject.AddComponent<EnemyVerticalEngagement>();
        randomizer.BeginSpawn();
        Vector3 samples = Samples(randomizer);
        Vector3 scale = randomizer.transform.localScale;
        engagement.EnterDormancy();
        engagement.ExitDormancy();
        randomizer.enabled = false;
        randomizer.enabled = true;
        randomizer.BeginSpawn();
        Assert.That(randomizer.HasSampledForSpawn, Is.True);
        Assert.That(Samples(randomizer), Is.EqualTo(samples));
        Assert.That(randomizer.transform.localScale, Is.EqualTo(scale));
    }

    [Test]
    public void SameLifetimeGameObjectReenable_PreservesRandomizedHealthAndDamageTaken()
    {
        EnemyStatRandomizer randomizer = NewEnemy(100);
        _lifecycle.SetActive(randomizer.gameObject, true);
        SetFixed(randomizer, 1.5f, 1.25f, .8f);
        randomizer.BeginSpawn();
        EnemyHealth health = randomizer.GetComponent<EnemyHealth>();
        health.ApplyDotDamage(10);
        _lifecycle.SetActive(randomizer.gameObject, false);
        _lifecycle.SetActive(randomizer.gameObject, true);
        randomizer.BeginSpawn();
        Assert.That(health.MaxHealth, Is.EqualTo(150));
        Assert.That(health.CurrentHealth, Is.EqualTo(140));
        Assert.That(Samples(randomizer), Is.EqualTo(new Vector3(1.5f, 1.25f, .8f)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BossAndTutorialActors_AreExcludedEvenIfComponentIsAccidentallyAttached(bool tutorial)
    {
        EnemyStatRandomizer randomizer = NewEnemy(100);
        if (tutorial) randomizer.gameObject.AddComponent<TutorialEnemy>();
        else randomizer.gameObject.AddComponent<BossKeyDrop>();
        SetFixed(randomizer, 2f, 2f, 2f);
        randomizer.BeginSpawn();
        Assert.That(randomizer.HasSampledForSpawn, Is.False);
        Assert.That(randomizer.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(randomizer.GetComponent<EnemyHealth>().MaxHealth, Is.EqualTo(100));
    }

    [Test]
    public void RealPoolReuse_DoesNotSampleDuringPrewarmAndRestoresTheBaseline()
    {
        Transform container = NewObject("Randomizer test pool").transform;
        EnemyStatRandomizer source = NewEnemy(100, new Vector3(2f, 3f, 4f));
        SetFixed(source, 1.5f, 1.25f, .8f);
        EnemyPrefabPool pool = new(source.gameObject, container, 1, false, 1);
        EnemyStatRandomizer prewarmed = container.GetComponentInChildren<EnemyStatRandomizer>(true);
        Assert.That(prewarmed.HasSampledForSpawn, Is.False);
        GameObject first = pool.TryGet();
        EnemyHealth health = first.GetComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(100);
        EnemyStatRandomizer.BeginSpawn(first);
        Assert.That(health.MaxHealth, Is.EqualTo(150));
        Assert.That(first.transform.localScale, Is.EqualTo(new Vector3(2.5f, 3.75f, 5f)));
        health.ApplyDotDamage(10);
        first.GetComponent<SwarmPooledEnemy>().NotifySpawned();
        EnemyStatRandomizer.BeginSpawn(first);
        Assert.That(health.CurrentHealth, Is.EqualTo(140), "A duplicate notification must not reset this living lease.");
        Assert.That(first.transform.localScale, Is.EqualTo(new Vector3(2.5f, 3.75f, 5f)));
        pool.Release(first);
        GameObject second = pool.TryGet();
        Assert.That(second, Is.SameAs(first));
        Assert.That(second.GetComponent<EnemyStatRandomizer>().HasSampledForSpawn, Is.False);
        EnemyStatRandomizer.BeginSpawn(second);
        Assert.That(health.MaxHealth, Is.EqualTo(150));
        Assert.That(second.transform.localScale, Is.EqualTo(new Vector3(2.5f, 3.75f, 5f)));
        pool.Release(second);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedPlacement_AppliesSizeBeforeSeatingThePhysicalHitbox(bool usePool)
    {
        const int groundLayer = 30;
        GameObject ground = NewObject("Randomizer ground");
        ground.layer = groundLayer;
        ground.transform.position = new Vector3(20000f, -.5f, 20000f);
        ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
        EnemyStatRandomizer source = NewEnemy(100);
        source.gameObject.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        SetFixed(source, 1f, 2f, 1f);
        Physics.SyncTransforms();
        EnemyPoolRegistry previousRegistry = EnemyPoolRegistry.Instance;
        EnemyPoolRegistry registry = NewPoolRegistry(usePool);
        try
        {
            registry.RegisterPrefab(new EnemyPoolRegistry.Entry { Prefab = source.gameObject, InitialSize = 1, MaxSize = 1 });
            bool placed = OrbitalSpawnPlacement.TrySpawnGrounded(source.gameObject,
                new Vector3(20000f, 0f, 20000f), 1 << groundLayer, 0, 1 << groundLayer,
                10f, 30f, 0f, .05f, 3, .05f, .2f, out GameObject instance, out _);
            if (instance != null && !usePool) _created.Add(instance);
            Assert.That(placed, Is.True);
            Physics.SyncTransforms();
            Assert.That(instance.GetComponent<EnemyStatRandomizer>().HasSampledForSpawn, Is.True);
            Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one * 2f));
            Bounds hitbox = instance.GetComponent<BoxCollider>().bounds;
            Assert.That(hitbox.size.y, Is.EqualTo(4f).Within(.0001f));
            Assert.That(hitbox.min.y, Is.InRange(.049f, .101f), "The enlarged collider must sit above the ground.");
            if (usePool)
            {
                registry.Release(instance);
                Assert.That(instance.GetComponent<EnemyStatRandomizer>().HasSampledForSpawn, Is.False);
                Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
            }
        }
        finally
        {
            SetPoolRegistry(previousRegistry);
        }
    }

    [Test]
    public void FailedPooledPlacement_ReturnsTheLeaseAndClearsItsRandomizedScale()
    {
        EnemyStatRandomizer source = NewEnemy(100, new Vector3(2f, 3f, 4f));
        source.gameObject.AddComponent<BoxCollider>();
        SetFixed(source, 1.2f, 1.4f, .8f);
        EnemyPoolRegistry previousRegistry = EnemyPoolRegistry.Instance;
        EnemyPoolRegistry registry = NewPoolRegistry(true);
        try
        {
            registry.RegisterPrefab(new EnemyPoolRegistry.Entry { Prefab = source.gameObject, InitialSize = 1, MaxSize = 1 });
            EnemyStatRandomizer pooled = registry.GetComponentInChildren<EnemyStatRandomizer>(true);
            bool placed = OrbitalSpawnPlacement.TrySpawnGrounded(source.gameObject,
                new Vector3(50000f, 0f, 50000f), 1 << 30, 0, 1 << 30,
                10f, 30f, 0f, .05f, 3, .05f, .2f, out GameObject instance, out _);
            Assert.That(placed, Is.False);
            Assert.That(instance, Is.Null);
            Assert.That(registry.TotalLeased, Is.Zero);
            Assert.That(pooled.HasSampledForSpawn, Is.False);
            Assert.That(pooled.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(registry.TryGet(source.gameObject, out GameObject nextLease), Is.True);
            Assert.That(nextLease, Is.SameAs(pooled.gameObject));
            EnemyStatRandomizer.BeginSpawn(nextLease);
            Assert.That(nextLease.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f) * 1.4f));
            registry.Release(nextLease);
        }
        finally
        {
            SetPoolRegistry(previousRegistry);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneFallback_SeatsEnlargedBodiesAndPreservesTutorialPlacement(bool tutorial)
    {
        GameObject ground = NewObject("Scene randomizer ground");
        ground.transform.position = new Vector3(20000f, -.5f, 20000f);
        ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
        EnemyStatRandomizer randomizer = NewEnemy(100);
        randomizer.gameObject.SetActive(false);
        randomizer.transform.position = new Vector3(20000f, 1f, 20000f);
        BoxCollider hitbox = randomizer.gameObject.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(1f, 2f, 1f);
        if (tutorial) randomizer.gameObject.AddComponent<TutorialEnemy>();
        SetFixed(randomizer, 1f, 2f, 1f);
        Transform pivot = NewObject("Scene randomizer visual").transform;
        pivot.SetParent(randomizer.transform, false);
        pivot.localScale = new Vector3(.8f, .9f, 1.1f);
        EnemyEmergence emergence = randomizer.gameObject.AddComponent<EnemyEmergence>();
        emergence.Configure(pivot, .1f, .1f);
        _lifecycle.SetActive(randomizer.gameObject, true);
        Physics.SyncTransforms();

        emergence.BeginAtCurrentPlacement();
        emergence.Simulate(1f);
        Physics.SyncTransforms();

        Assert.That(emergence.BlocksGameplayNow, Is.False);
        Assert.That(randomizer.HasSampledForSpawn, Is.EqualTo(!tutorial));
        Assert.That(hitbox.enabled, Is.True);
        Assert.That(hitbox.bounds.size.y, Is.EqualTo(tutorial ? 2f : 4f).Within(.0001f));
        Assert.That(hitbox.bounds.min.y, Is.EqualTo(0f).Within(.0001f), "The final hitbox must rest on the floor.");
        Assert.That(randomizer.transform.position.y, Is.EqualTo(tutorial ? 1f : 2f).Within(.0001f));
        Assert.That(pivot.localScale, Is.EqualTo(new Vector3(.8f, .9f, 1.1f)), "Emergence must preserve authored art scale.");
    }

    [TestCase("Enemy")]
    [TestCase("EnemyPro")]
    [TestCase("Chaser")]
    [TestCase("Chaser (variant)")]
    [TestCase("Chaser_Elite")]
    [TestCase("Slime (variant)")]
    [TestCase("Slime_Elite")]
    [TestCase("Drone")]
    [TestCase("Drone (variant)")]
    [TestCase("Drone_Elite")]
    public void NormalAndElitePrefabs_HaveOneEnabledRandomizerWithValidDesignerRanges(string prefabName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefabName + ".prefab");
        Assert.That(prefab, Is.Not.Null);
        EnemyStatRandomizer[] components = prefab.GetComponentsInChildren<EnemyStatRandomizer>(true);
        Assert.That(components, Has.Length.EqualTo(1));
        Assert.That(components[0].gameObject, Is.SameAs(prefab));
        Assert.That(components[0].enabled, Is.True);
        foreach (string field in new[] { "_healthMultiplierRange", "_sizeMultiplierRange", "_movementSpeedMultiplierRange" })
        {
            Vector2 range = GetField<Vector2>(components[0], field);
            Assert.That(float.IsNaN(range.x) || float.IsInfinity(range.x), Is.False, field);
            Assert.That(float.IsNaN(range.y) || float.IsInfinity(range.y), Is.False, field);
            Assert.That(range.x, Is.GreaterThan(0f), field);
            Assert.That(range.y, Is.GreaterThanOrEqualTo(range.x), field);
        }
    }

    [TestCase("Boss")]
    [TestCase("Boss_2")]
    [TestCase("Stalker")]
    [TestCase("Destroyer_Boss")]
    public void BossPrefabs_DoNotHaveRandomizers(string prefabName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefabName + ".prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<EnemyStatRandomizer>(true), Is.Empty);
    }

    private EnemyPoolRegistry NewPoolRegistry(bool enabled)
    {
        GameObject holder = NewObject("Randomizer placement pool");
        holder.SetActive(false);
        EnemyPoolRegistry registry = holder.AddComponent<EnemyPoolRegistry>();
        SetField(registry, "_container", holder.transform);
        registry.SetUseEnemyPool(enabled);
        SetPoolRegistry(registry);
        _lifecycle.SetActive(holder, true);
        return registry;
    }

    private static void SetPoolRegistry(EnemyPoolRegistry registry) =>
        typeof(EnemyPoolRegistry).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, registry);

    private GameObject NewObject(string name)
    {
        GameObject go = new(name);
        _created.Add(go);
        return go;
    }

    private EnemyStatRandomizer NewEnemy(int health, Vector3? scale = null)
    {
        GameObject go = NewObject("Randomizer enemy");
        go.transform.localScale = scale ?? Vector3.one;
        go.AddComponent<EnemyHealth>().ApplyConfiguredMaxHealth(health);
        return go.AddComponent<EnemyStatRandomizer>();
    }

    private static Vector3 Samples(EnemyStatRandomizer randomizer) =>
        new(randomizer.HealthMultiplier, randomizer.SizeMultiplier, randomizer.MovementSpeedMultiplier);

    private static void SetFixed(EnemyStatRandomizer randomizer, float health, float size, float speed) =>
        Configure(randomizer, Vector2.one * health, Vector2.one * size, Vector2.one * speed);

    private static void Configure(EnemyStatRandomizer randomizer, Vector2 health, Vector2 size, Vector2 speed)
    {
        SetField(randomizer, "_healthMultiplierRange", health);
        SetField(randomizer, "_sizeMultiplierRange", size);
        SetField(randomizer, "_movementSpeedMultiplierRange", speed);
    }

    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T GetField<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return field;
    }

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
}
