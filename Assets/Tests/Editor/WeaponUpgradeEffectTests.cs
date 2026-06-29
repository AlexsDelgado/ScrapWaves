using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class WeaponUpgradeEffectTests
{
    [Test]
    public void DamageAmplifierStatus_IncreasesDamageAppliedThroughWeaponDamageApplier()
    {
        GameObject target = new("Target");
        var damageable = target.AddComponent<TestDamageable>();
        target.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);

        bool applied = WeaponDamageApplier.TryApplyDamage(damageable, 10);

        Assert.That(applied, Is.True);
        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void RadialDamage_DamagesEachDamageableOnlyOnce()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();

        int hits = WeaponRadialDamage.Apply(
            Vector3.zero,
            2f,
            12,
            falloff: 0f,
            knockback: 0f,
            maxTargets: 32);

        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageable.TotalDamage, Is.EqualTo(12));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void FlamethrowerBurnStatus_AppliesDamageThroughWeaponDamageApplier()
    {
        GameObject target = new("Burn Target");
        var damageable = target.AddComponent<TestDamageable>();
        target.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);
        FlamethrowerBurnStatus burn = target.AddComponent<FlamethrowerBurnStatus>();
        burn.Refresh(damageable, damagePerTick: 10, duration: 3f, tickInterval: 0.5f);
        SetPrivateField(burn, "_tickTimer", 0f);

        InvokePrivate(burn, "Update");

        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void ProjectileExplosionDamageAmplifier_AffectsExplosionHitDamage()
    {
        GameObject projectileGo = new("Projectile");
        Projectile projectile = projectileGo.AddComponent<Projectile>();
        projectile.ConfigurePooled(3f, 10, 0f);
        projectile.ConfigureExplosion(2f, 0f);
        InvokePrivate(projectile, "ConfigureDamageAmplifierOnExplosion", 1.5f, 3f);

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.forward;
        var damageable = target.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        InvokePrivate(projectile, "ApplyExplosionDamage");

        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Assert.That(target.GetComponent<WeaponDamageAmplifierStatus>(), Is.Not.Null);
        Object.DestroyImmediate(projectileGo);
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void ProjectileFragmentCone_DamagesTargetsOnlyInsideForwardCone()
    {
        GameObject projectileGo = new("Projectile");
        Projectile projectile = projectileGo.AddComponent<Projectile>();
        projectile.ConfigurePooled(3f, 10, 0f);
        projectile.ConfigureExplosion(0.1f, 0f);
        InvokePrivate(projectile, "ConfigureFragmentCone", 45f, 3f, 0.5f);

        GameObject forwardTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        forwardTarget.transform.position = Vector3.forward * 2f;
        var forwardDamageable = forwardTarget.AddComponent<TestDamageable>();

        GameObject sideTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        sideTarget.transform.position = Vector3.right * 2f;
        var sideDamageable = sideTarget.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        InvokePrivate(projectile, "ApplyExplosionDamage");

        Assert.That(forwardDamageable.TotalDamage, Is.EqualTo(5));
        Assert.That(sideDamageable.TotalDamage, Is.Zero);
        Object.DestroyImmediate(projectileGo);
        Object.DestroyImmediate(forwardTarget);
        Object.DestroyImmediate(sideTarget);
        DestroyGeneratedVfx();
    }

    [Test]
    public void FlamethrowerFuelPuddle_TicksRadialDamage()
    {
        System.Type puddleType = typeof(Projectile).Assembly.GetType("FlamethrowerFuelPuddle");
        Assert.That(puddleType, Is.Not.Null, "Missing FlamethrowerFuelPuddle type.");

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        MethodInfo spawn = puddleType.GetMethod("Spawn", BindingFlags.Static | BindingFlags.Public);
        Assert.That(spawn, Is.Not.Null, "Missing FlamethrowerFuelPuddle.Spawn.");
        object puddle = spawn.Invoke(null, new object[] { Vector3.zero, 2f, 7, 1f, 0.1f });

        InvokePrivate(puddle, "Update");

        Assert.That(damageable.TotalDamage, Is.EqualTo(7));
        if (puddle is Component component)
            Object.DestroyImmediate(component.gameObject);
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void FlamethrowerLiquidNitrogenActiveBurn_AppliesMovementFreezeStatus()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathB, out WeaponData data);
        GameObject target = new("Freeze Target");
        target.AddComponent<TestDamageable>();

        InvokePrivate(
            weapon,
            "ApplyBurnToTarget",
            target.transform,
            4,
            data.Flamethrower,
            true);

        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.Not.Null);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(data);
    }

    private sealed class TestDamageable : MonoBehaviour, IDamageable
    {
        public int LastDamage { get; private set; }
        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            LastDamage = amount;
            TotalDamage += amount;
            return true;
        }
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

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, arguments);
    }

    private static void DestroyGeneratedVfx()
    {
        foreach (ExplosionRadiusVfx vfx in Object.FindObjectsByType<ExplosionRadiusVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);
    }

    private static FlamethrowerWeapon CreateFlamethrowerWeapon(WeaponUpgradePath path, out WeaponData data)
    {
        data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponId = "TestFlamethrower";
        data.DisplayName = "Test Flamethrower";
        data.WeaponType = WeaponType.Flamethrower;
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.EnsureSpecificTuningForCurrentType();
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };

        WeaponInstance instance = new()
        {
            Data = data,
            Level = 6,
            SelectedPath = path,
            State = WeaponState.Manual
        };

        FlamethrowerWeapon weapon = new(null, null, null, null);
        weapon.Setup(instance, null, null, null);
        return weapon;
    }
}
