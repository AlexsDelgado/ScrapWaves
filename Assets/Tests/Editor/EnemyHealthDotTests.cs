using NUnit.Framework;
using UnityEngine;

public class EnemyHealthDotTests
{
    [Test]
    public void ApplyDotDamage_ReducesHealthWhileInvincible()
    {
        GameObject go = new GameObject("EnemyHealthDotTest");
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(100);
        health.SetInvincible(true);

        Assert.IsFalse(health.ApplyDamage(10));
        Assert.IsTrue(health.ApplyDotDamage(10));
        Assert.That(health.CurrentHealth, Is.EqualTo(90));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void ApplyDotDamage_CanKillWhileInvincible()
    {
        GameObject go = new GameObject("EnemyHealthDotTest");
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(5);
        health.SetInvincible(true);

        Assert.IsTrue(health.ApplyDotDamage(5));
        Assert.That(health.CurrentHealth, Is.EqualTo(0));

        Object.DestroyImmediate(go);
    }
}
