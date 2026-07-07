using NUnit.Framework;
using UnityEngine;

public class EnemyPoolLifecycleTests
{
    [Test]
    public void PrepareForPoolSpawn_ClearsInvincibilityAndRefillsHealth()
    {
        GameObject go = new GameObject("EnemyPoolTest");
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(20);
        health.SetInvincible(true);
        health.ApplyDotDamage(5);

        health.PrepareForPoolSpawn();

        Assert.IsFalse(health.IsInvincible);
        Assert.That(health.CurrentHealth, Is.EqualTo(20));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void SwarmPooledEnemy_DespawnRoutesToRegistryWhenBound()
    {
        GameObject registryGo = new GameObject("Registry");
        EnemyPoolRegistry registry = registryGo.AddComponent<EnemyPoolRegistry>();

        GameObject prefab = new GameObject("EnemyPrefab");
        prefab.AddComponent<EnemyHealth>();
        prefab.AddComponent<SwarmPooledEnemy>();

        registry.RegisterPrefab(new EnemyPoolRegistry.Entry
        {
            Prefab = prefab,
            InitialSize = 1,
            MaxSize = 4
        });

        Assert.IsTrue(registry.TryGet(prefab, out GameObject instance));
        Assert.IsTrue(instance.activeSelf);

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        Assert.IsTrue(pooled.IsBoundToRegistry);

        pooled.Despawn();
        Assert.IsFalse(instance.activeSelf);

        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(registryGo);
    }
}
