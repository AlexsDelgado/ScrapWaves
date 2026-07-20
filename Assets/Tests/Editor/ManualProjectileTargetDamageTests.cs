using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ManualProjectileTargetDamageTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _cleanup.Count; i++)
            Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void ManualProjectile_AppliesEliteDamageMultiplierToHitTarget()
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
