using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ManualProjectileTargetDamageTests
{
    private readonly List<Object> _cleanup = new();

    [SetUp]
    public void InitializeDamageTracking()
    {
        if (ChallengeProgressTracker.Instance != null)
            return;
        GameObject trackerObject = new("Test challenge tracker");
        _cleanup.Add(trackerObject);
        ChallengeProgressTracker tracker = trackerObject.AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance))
            .SetValue(null, tracker);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _cleanup.Count; i++)
            Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 24)]
    public void ManualProjectile_AppliesEliteDamageMultiplierToHitTarget(bool trigger, int interveningTriggers)
    {
        GameObject owner = new("Manual Projectile Owner");
        GameObject spawn = new("Manual Projectile Spawn");
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        GameObject poolGo = new("Manual Projectile Pool");
        GameObject poolContainer = new("Manual Projectile Pool Container");
        GameObject prefab = new("Manual Projectile Prefab");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(owner);
        _cleanup.Add(spawn);
        _cleanup.Add(target);
        _cleanup.Add(poolGo);
        _cleanup.Add(poolContainer);
        _cleanup.Add(prefab);
        _cleanup.Add(data);

        target.GetComponent<Collider>().isTrigger = trigger;
        for (int i = 0; i < interveningTriggers; i++)
        {
            GameObject volume = new("Non-damageable trigger");
            _cleanup.Add(volume);
            volume.transform.position = Vector3.forward * (0.1f + i * 0.01f);
            BoxCollider collider = volume.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.01f;
            collider.isTrigger = true;
        }
        target.name = "Elite Target";
        target.transform.position = Vector3.forward;
        TestDamageable damageable = target.AddComponent<TestDamageable>();

        prefab.AddComponent<Rigidbody>();
        prefab.AddComponent<SphereCollider>();
        prefab.AddComponent<Projectile>();
        prefab.SetActive(false);

        poolGo.SetActive(false);
        ProjectilePool pool = poolGo.AddComponent<ProjectilePool>();
        SetPrivateField(pool, "_projectilePrefab", prefab);
        SetPrivateField(pool, "_container", poolContainer.transform);
        SetPrivateField(pool, "_initialPoolSize", 1);
        SetPrivateField(pool, "_maxPoolSize", 2);
        SetPrivateField(pool, "_allowPoolGrowth", true);
        poolGo.SetActive(true);
        InvokePrivate(pool, "Awake");
        foreach (Projectile pooledProjectile in Resources.FindObjectsOfTypeAll<Projectile>())
        {
            if (pooledProjectile.gameObject != prefab)
                InvokePrivate(pooledProjectile, "Awake");
        }

        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateDefaultStatDefinitions());
        InvokePrivate(stats, "Awake");

        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseRange = 12f;
        data.BaseManualAmmo = 5f;

        WeaponInstance instance = new()
        {
            Data = data,
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };

        BasicProjectileWeapon weapon = new(null, pool, spawn.transform);
        weapon.Setup(instance, owner.transform, stats, null);
        weapon.TickManual(1f, Vector3.forward, true);

        Projectile projectile = null;
        foreach (Projectile candidate in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
        {
            if (candidate.gameObject != prefab && candidate.gameObject.activeSelf)
            {
                projectile = candidate;
                break;
            }
        }

        Assert.That(projectile, Is.Not.Null);
        Physics.SyncTransforms();

        InvokePrivate<bool>(projectile, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 2f);

        Assert.That(damageable.TotalDamage, Is.EqualTo(20));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Projectile_LethalHitReportsFullResolvedDamageAndKillColor(bool critical)
    {
        GameObject owner = new("Overkill Projectile Owner");
        GameObject target = new("Overkill Target");
        GameObject shot = new("Overkill Projectile");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        CombatTextProfile profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        _cleanup.AddRange(new Object[] { owner, target, shot, data, profile });

        PlayerStats stats = owner.AddComponent<PlayerStats>();
        List<StatDefinition> definitions = CreateDefaultStatDefinitions();
        foreach (StatDefinition definition in definitions)
        {
            _cleanup.Add(definition);
            if (definition.StatType == StatType.CriticalChance)
                SetPrivateField(definition, "<BaseValue>k__BackingField", 1f);
        }
        SetPrivateField(stats, "_statDefinitions", definitions);
        InvokePrivate(stats, "Awake");
        data.BaseDamage = 200f;
        WeaponInstance weapon = new() { Data = data, Level = 1, State = WeaponState.Manual };
        WeaponDamageContext damageContext = new(stats, weapon, canCrit: critical,
            critMultiplierOverride: 1f, damageScale: 1f, isAbilityDamage: false, knockbackScale: 0f);

        EnemyHealth health = target.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(5);
        SetPrivateField(health, "_damageTakenMultiplier", 0.5f);
        Collider targetCollider = target.AddComponent<BoxCollider>();
        Projectile projectile = shot.AddComponent<Projectile>();
        InvokePrivate(projectile, "Awake");
        projectile.ConfigureWeaponDamage(damageContext);
        DamageFeedbackRecorder sink = new();
        WeaponFeedbackContext template = new(weapon, WeaponFeedbackMode.Manual, 0f,
            Vector3.zero, Vector3.forward);
        projectile.ConfigureFeedback(sink, in template);

        typeof(Projectile).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(projectile, new object[] { targetCollider });

        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(sink.DamageEvents.Count, Is.EqualTo(1));
        WeaponFeedbackContext feedback = sink.DamageEvents[0];
        Assert.That(feedback.DamageAmount, Is.EqualTo(critical ? 200 : 100),
            "Display the full post-mitigation hit, not the five HP removed.");
        Assert.That(feedback.IsCritical, Is.EqualTo(critical));
        Assert.That(feedback.IsKill, Is.True);
        CombatTextEvent text = CombatTextEvent.FromFeedback(in feedback);
        Assert.That(text.AppliedDamage, Is.EqualTo(feedback.DamageAmount));
        CombatTextStyleId style = CombatTextStyleResolver.ResolveStyle(in text);
        Assert.That(style, Is.EqualTo(CombatTextStyleId.Kill));
        Assert.That(CombatTextStyleResolver.ResolveColor(style), Is.EqualTo(Color.red));
    }

    private sealed class DamageFeedbackRecorder : IWeaponFeedbackSink
    {
        public readonly List<WeaponFeedbackContext> DamageEvents = new();
        public void OnDamageConfirmed(in WeaponFeedbackContext context) => DamageEvents.Add(context);
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnShotFired(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { }
        public void OnStatusApplied(in WeaponFeedbackContext context) { }
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold) { }
        public void ConfigureProjectile(Projectile projectile, ProjectilePresentationArchetypeId archetype,
            in WeaponFeedbackContext context) { }
        public void Emit(in WeaponPresentationContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    private sealed class TestDamageable : MonoBehaviour, IDamageable
    {
        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            TotalDamage += amount;
            return true;
        }
    }

    private static List<StatDefinition> CreateDefaultStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 2f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, 1f),
            CreateDefinition(StatType.AbilityDamageMultiplier, 1f),
            CreateDefinition(StatType.AbilityCooldownReduction, 0f)
        };
    }

    private static StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", false);
        return definition;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        return (T)method.Invoke(target, arguments);
    }
}
