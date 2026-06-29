using NUnit.Framework;
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
}
