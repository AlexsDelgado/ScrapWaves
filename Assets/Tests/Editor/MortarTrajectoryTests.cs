using NUnit.Framework;
using UnityEngine;

public class MortarTrajectoryTests
{
    [Test]
    public void Evaluate_ReachesConfiguredTargetAtOne()
    {
        Vector3 start = new Vector3(0f, 1f, 0f);
        Vector3 target = new Vector3(0f, 1f, 10f);

        Assert.That(
            MortarTrajectory.Evaluate(start, target, 7f, 1f),
            Is.EqualTo(target));
    }

    [Test]
    public void Evaluate_ContinuesPastTargetAfterOne()
    {
        Vector3 point = MortarTrajectory.Evaluate(
            Vector3.zero,
            Vector3.forward * 10f,
            7f,
            2f);

        Assert.That(point.z, Is.EqualTo(20f).Within(0.0001f));
        Assert.That(point.y, Is.LessThan(0f));
    }

    [Test]
    public void GetMaximumNormalizedTime_MatchesShellFailsafe()
    {
        Assert.That(
            MortarTrajectory.GetMaximumNormalizedTime(0.5f),
            Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void PredictTerrainCollision_ReturnsFalseWhenTrajectoryNeverHitsMap()
    {
        RaycastHit[] hits = new RaycastHit[16];
        bool found = MortarTrajectory.TryPredictTerrainCollision(
            Vector3.up * 4f,
            new Vector3(0f, 8f, 6f),
            3f,
            1f,
            0.1f,
            null,
            hits,
            out _);

        Assert.That(found, Is.False);
    }

    [Test]
    public void PredictTerrainCollision_ReportsActualMapSurfaceInsteadOfAirTarget()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            floor.transform.position = new Vector3(0f, -0.05f, 4f);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Physics.SyncTransforms();
            RaycastHit[] hits = new RaycastHit[16];

            bool found = MortarTrajectory.TryPredictTerrainCollision(
                Vector3.up * 4f,
                new Vector3(0f, 8f, 6f),
                3f,
                1f,
                0.1f,
                null,
                hits,
                out RaycastHit terrainHit);

            Assert.That(found, Is.True);
            Assert.That(terrainHit.collider, Is.EqualTo(floor.GetComponent<Collider>()));
            Assert.That(terrainHit.point.y, Is.LessThan(0.15f));
            Assert.That(terrainHit.point.y, Is.Not.EqualTo(8f).Within(0.1f));
        }
        finally
        {
            Object.DestroyImmediate(floor);
        }
    }
}
