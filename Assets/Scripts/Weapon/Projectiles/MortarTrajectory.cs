using UnityEngine;

public static class MortarTrajectory
{
    private const int PredictionSegments = 64;
    public const float MinimumFlightFailsafe = 5f;
    public const float FlightFailsafeMultiplier = 5f;

    public static Vector3 Evaluate(Vector3 start, Vector3 target, float arcHeight, float normalizedTime)
    {
        Vector3 point = Vector3.LerpUnclamped(start, target, normalizedTime);
        point.y += 4f * Mathf.Max(0f, arcHeight) * normalizedTime * (1f - normalizedTime);
        return point;
    }

    public static float GetMaximumNormalizedTime(float travelTime)
    {
        float safeTravelTime = Mathf.Max(0.05f, travelTime);
        float failsafeSeconds = Mathf.Max(
            MinimumFlightFailsafe,
            safeTravelTime * FlightFailsafeMultiplier);
        return failsafeSeconds / safeTravelTime;
    }

    public static bool TryPredictTerrainCollision(
        Vector3 start,
        Vector3 target,
        float arcHeight,
        float travelTime,
        float collisionRadius,
        Transform ignoredRoot,
        RaycastHit[] hitBuffer,
        out RaycastHit terrainHit)
    {
        terrainHit = default;
        if (hitBuffer == null || hitBuffer.Length == 0)
            return false;

        float maximumTime = GetMaximumNormalizedTime(travelTime);
        float step = maximumTime / PredictionSegments;
        float radius = Mathf.Max(0.01f, collisionRadius);
        Vector3 previous = start;
        for (int i = 1; i <= PredictionSegments; i++)
        {
            Vector3 next = Evaluate(start, target, arcHeight, step * i);
            if (TryGetTerrainSegmentHit(previous, next, radius, ignoredRoot, hitBuffer, out terrainHit))
                return true;
            previous = next;
        }
        return false;
    }

    private static bool TryGetTerrainSegmentHit(
        Vector3 start,
        Vector3 end,
        float radius,
        Transform ignoredRoot,
        RaycastHit[] hitBuffer,
        out RaycastHit terrainHit)
    {
        terrainHit = default;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return false;

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            radius,
            displacement / distance,
            hitBuffer,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (!ReticleAimProvider.IsValidMortarTerrainTransform(hit.transform, ignoredRoot))
                continue;
            Rigidbody body = hit.rigidbody;
            if (body != null && !ReticleAimProvider.IsValidMortarTerrainTransform(body.transform, ignoredRoot))
                continue;
            if (hit.distance >= closestDistance)
                continue;
            closestDistance = hit.distance;
            terrainHit = hit;
            found = true;
        }
        return found;
    }
}
