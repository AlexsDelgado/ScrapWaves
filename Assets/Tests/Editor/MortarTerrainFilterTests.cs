using NUnit.Framework;
using UnityEngine;

public class MortarTerrainFilterTests
{
    [Test]
    public void IsValidMortarTerrainTransform_RejectsDamageableTargets()
    {
        GameObject enemy = new GameObject("Enemy");
        enemy.AddComponent<ReticleTestDamageable>();
        try
        {
            Assert.That(
                ReticleAimProvider.IsValidMortarTerrainTransform(enemy.transform, null),
                Is.False);
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void IsValidMortarTerrainTransform_AcceptsMapGeometry()
    {
        GameObject map = new GameObject("Map");
        try
        {
            Assert.That(
                ReticleAimProvider.IsValidMortarTerrainTransform(map.transform, null),
                Is.True);
        }
        finally
        {
            Object.DestroyImmediate(map);
        }
    }
}

public sealed class ReticleTestDamageable : MonoBehaviour, IDamageable
{
    public bool ApplyDamage(int amount)
    {
        return amount > 0;
    }
}
