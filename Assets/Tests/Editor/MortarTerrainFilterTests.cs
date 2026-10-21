using NUnit.Framework;
using UnityEngine;

public class MortarTerrainFilterTests
{
    [TestCase(1)]
    [TestCase(16)]
    public void GroundProjection_SkipsStackedModelsAndUsesNearestFloorBelowContact(int bufferSize)
    {
        var objects = new System.Collections.Generic.List<GameObject>();
        Vector3 anchor = new(3500f, 100f, 3500f);
        try
        {
            GameObject lower = Box(0f, false);
            GameObject platform = Box(4f, false);
            Box(10f, false); // A higher storey must never become the ground under this impact.
            GameObject source = Box(7f, true);
            for (int i = 0; i < 5; i++) Box(5f + i * .25f, true);
            Physics.SyncTransforms();
            Assert.That(source.GetComponent<Collider>().Raycast(
                new Ray(anchor + Vector3.up * 9f, Vector3.down), out RaycastHit hit, 4f), Is.True);
            var hits = new RaycastHit[bufferSize];
            Assert.That(MortarPresentationSurface.TryResolveGround(hit, null, hits, out var ground), Is.True);
            Assert.That(ground.collider, Is.SameAs(platform.GetComponent<Collider>()));
            Assert.That(ground.point.y, Is.EqualTo(anchor.y + 4f).Within(.001f));
            platform.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(MortarPresentationSurface.TryResolveGround(hit, null, hits, out ground), Is.True);
            Assert.That(ground.collider, Is.SameAs(lower.GetComponent<Collider>()));
            lower.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(MortarPresentationSurface.TryResolveGround(hit, null, hits, out _), Is.False,
                "Models and an overhead platform cannot replace missing ground beneath the contact.");
        }
        finally
        {
            foreach (GameObject value in objects) Object.DestroyImmediate(value);
        }

        GameObject Box(float top, bool actor)
        {
            GameObject value = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(value);
            value.transform.position = anchor + Vector3.up * (top - .1f);
            value.transform.localScale = new Vector3(8f, .2f, 8f);
            if (actor) value.layer = LayerMask.NameToLayer("Enemy");
            return value;
        }
    }

    [Test]
    public void GroundProjection_ThinCeilingUndersideNeverSelectsItsTop()
    {
        Vector3 anchor = new(3600f, 100f, 3600f);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            floor.transform.position = anchor - Vector3.up * .25f;
            floor.transform.localScale = new Vector3(8f, .5f, 8f);
            ceiling.transform.position = anchor + Vector3.up * 4.05f;
            ceiling.transform.localScale = new Vector3(8f, .1f, 8f);
            Physics.SyncTransforms();
            Collider ceilingCollider = ceiling.GetComponent<Collider>();
            Assert.That(ceilingCollider.Raycast(new Ray(anchor + Vector3.up * 3f, Vector3.up),
                out RaycastHit underside, 2f), Is.True);
            Assert.That(underside.normal.y, Is.LessThan(-.99f));
            var hits = new RaycastHit[8];
            Assert.That(MortarPresentationSurface.TryResolveGround(underside, null, hits, out var ground), Is.True);
            Assert.That(ground.collider, Is.SameAs(floor.GetComponent<Collider>()),
                "The .25m probe clearance must not move the footprint onto the .1m ceiling's upper face.");
            Assert.That(ground.point.y, Is.EqualTo(anchor.y).Within(.001f));

            floor.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(MortarPresentationSurface.TryResolveGround(underside, null, hits, out _), Is.False,
                "Without a floor below the underside, its overhead top must not become a fallback.");
            Assert.That(ceilingCollider.Raycast(new Ray(anchor + Vector3.up * 6f, Vector3.down),
                out RaycastHit top, 3f), Is.True);
            Assert.That(MortarPresentationSurface.TryResolveGround(top, null, hits, out ground), Is.True,
                "A direct valid hit on an elevated walkable top remains valid.");
            Assert.That(ground.collider, Is.SameAs(ceilingCollider));
            Assert.That(ground.point, Is.EqualTo(top.point));
        }
        finally
        {
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(ceiling);
        }
    }

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
