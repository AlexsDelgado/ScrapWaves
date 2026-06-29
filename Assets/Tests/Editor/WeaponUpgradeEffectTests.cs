using NUnit.Framework;
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
}
