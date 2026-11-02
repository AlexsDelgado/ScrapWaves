using NUnit.Framework;
using UnityEngine;

public class MortarTrajectoryTests
{
    [TestCase(0.05f)]
    [TestCase(0.6666667f)]
    [TestCase(1f)]
    public void Prediction_HeatTravelTimeDoesNotMoveCollision(float travelTime)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            floor.transform.position = new Vector3(0f, -0.5f, 10f);
            floor.transform.localScale = new Vector3(10f, 1f, 40f);
            Physics.SyncTransforms();
            var buffer = new RaycastHit[16];
            Vector3 start = Vector3.up * 2f;
            Vector3 target = new Vector3(0f, 0.18f, 18f);
            Assert.That(MortarTrajectory.TryPredictTerrainCollision(start, target, 8f, 1f,
                0.18f, null, buffer, out RaycastHit baseline), Is.True);
            Assert.That(MortarTrajectory.TryPredictTerrainCollision(start, target, 8f, travelTime,
                0.18f, null, buffer, out RaycastHit heated), Is.True);
            Assert.That(Vector3.Distance(baseline.point, heated.point), Is.LessThan(0.0001f));
        }
        finally { Object.DestroyImmediate(floor); }
    }

    [TestCase(0.013f)]
    [TestCase(0.37f)]
    [TestCase(1.3f)]
    public void Prediction_MatchesLiveSweepsAcrossFramePartitions(float frameStep)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Vector3 start = Vector3.up * 2f;
            Vector3 target = new Vector3(0f, 0.18f, 18f);
            obstacle.transform.position = MortarTrajectory.Evaluate(start, target, 8f, 0.3f);
            obstacle.transform.localScale = Vector3.one * 0.4f;
            Physics.SyncTransforms();
            var buffer = new RaycastHit[16];
            Assert.That(MortarTrajectory.TryPredictTerrainCollision(start, target, 8f, 1f,
                0.18f, null, buffer, out RaycastHit prediction), Is.True);
            RaycastHit live = default;
            bool found = false;
            for (float from = 0f; from < 1.5f && !found; from += frameStep)
                found = MortarTrajectory.TryGetCollisionBetweenNormalizedTimes(start, target, 8f,
                    0.18f, null, buffer, from, from + frameStep, false, out live);
            Assert.That(found, Is.True);
            Assert.That(live.collider, Is.EqualTo(prediction.collider));
            Assert.That(Vector3.Distance(live.point, prediction.point), Is.LessThan(0.002f));
        }
        finally { Object.DestroyImmediate(obstacle); }
    }

    [TestCase(0f)]
    [TestCase(0.55f)]
    public void Prediction_ReportsMuzzleAlreadyInsideOrOverlappingObstacle(float centerOffset)
    {
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Vector3 start = Vector3.up * 2f;
            obstacle.transform.position = start + Vector3.forward * centerOffset;
            Physics.SyncTransforms();
            var buffer = new RaycastHit[16];
            Assert.That(MortarTrajectory.TryPredictTerrainCollision(start, start + Vector3.forward * 18f,
                8f, 1f, 0.18f, null, buffer, out RaycastHit prediction), Is.True);
            Assert.That(prediction.collider, Is.EqualTo(obstacle.GetComponent<Collider>()));
            Assert.That(prediction.distance, Is.Zero);
            Assert.That(MortarTrajectory.TryGetCollisionBetweenNormalizedTimes(start,
                start + Vector3.forward * 18f, 8f, 0.18f, null, buffer, 0f, 0.01f,
                false, out RaycastHit live), Is.True);
            Assert.That(Vector3.Distance(live.point, prediction.point), Is.LessThan(0.0001f));
        }
        finally { Object.DestroyImmediate(obstacle); }
    }

    [Test]
    public void ManualGrapeshotPreview_CentersOnSelectedLandingInsteadOfAirburstPosition()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            floor.transform.position = new Vector3(0f, -0.5f, 10f);
            floor.transform.localScale = new Vector3(10f, 1f, 40f);
            Physics.SyncTransforms();
            var payload = new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f);
            Assert.That(MortarTrajectory.TryPredictManualCollision(Vector3.up * 2f,
                new Vector3(0f, 0.18f, 18f), 8f, 1f, 0.18f, null, new RaycastHit[16],
                payload, out RaycastHit hit, Vector3.up), Is.True);
            Assert.That(hit.point.z, Is.EqualTo(18f).Within(0.02f));
            Assert.That(hit.point.y, Is.EqualTo(0f).Within(0.001f));
        }
        finally { Object.DestroyImmediate(floor); }
    }

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
