using UnityEngine;

/// <summary>
/// Resolves a readable surface for mortar-only presentation without changing the
/// collision point used by damage. A slope is used only when it supports most of
/// the effect footprint; small props fall back to the dominant supporting ground.
/// </summary>
public static class MortarPresentationSurface
{
    private const float BoundsPadding = 0.25f;
    private const int CoverageSampleCount = 16;
    private const float MinimumSlopeAngle = 7f;
    private const float MatchingSlopeAngle = 12f;
    private const float RequiredSlopeCoverage = 0.5f;
    private const float GoldenAngle = 2.39996323f;

    public static void Resolve(
        RaycastHit sourceHit,
        float effectRadius,
        Transform ignoredRoot,
        RaycastHit[] supportHits,
        out Vector3 position,
        out Vector3 normal)
    {
        position = sourceHit.point;
        normal = GetSafeNormal(sourceHit.normal);

        Collider sourceCollider = sourceHit.collider;
        if (sourceCollider == null || supportHits == null || supportHits.Length == 0)
            return;

        if (IsMinorObstacle(sourceCollider, effectRadius)
            && TryFindSupportingGround(
                sourceHit,
                effectRadius,
                ignoredRoot,
                supportHits,
                out RaycastHit supportingHit))
        {
            position = supportingHit.point;
            normal = GetSafeNormal(supportingHit.normal);
        }

        float radius = Mathf.Max(0f, effectRadius);
        if (radius <= 0.01f || Vector3.Angle(normal, Vector3.up) < MinimumSlopeAngle)
            return;

        ResolveSlopeCoverage(
            radius,
            ignoredRoot,
            supportHits,
            ref position,
            ref normal);
    }

    private static bool TryFindSupportingGround(
        RaycastHit sourceHit,
        float effectRadius,
        Transform ignoredRoot,
        RaycastHit[] supportHits,
        out RaycastHit supportingHit)
    {
        supportingHit = default;
        Bounds bounds = sourceHit.collider.bounds;
        Vector3 origin = new(sourceHit.point.x, bounds.max.y + BoundsPadding, sourceHit.point.z);
        float distance = Mathf.Max(
            2f,
            origin.y - bounds.min.y + Mathf.Max(2f, Mathf.Max(0f, effectRadius) + 1f));
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            supportHits,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = supportHits[i];
            if (candidate.collider == null
                || candidate.collider == sourceHit.collider
                || candidate.distance >= closestDistance
                || !IsValidSupportingSurface(candidate, ignoredRoot)
                || IsMinorObstacle(candidate.collider, effectRadius))
            {
                continue;
            }

            closestDistance = candidate.distance;
            supportingHit = candidate;
            found = true;
        }

        return found;
    }

    private static void ResolveSlopeCoverage(
        float effectRadius,
        Transform ignoredRoot,
        RaycastHit[] supportHits,
        ref Vector3 position,
        ref Vector3 normal)
    {
        Vector3 slopeNormal = normal;
        int validSamples = 0;
        int matchingSlopeSamples = 0;
        int alternateSamples = 0;
        float alternateHeightSum = 0f;

        for (int i = 0; i < CoverageSampleCount; i++)
        {
            float sampleRadius = effectRadius * Mathf.Sqrt((i + 0.5f) / CoverageSampleCount);
            float angle = i * GoldenAngle;
            Vector3 samplePosition = position + new Vector3(
                Mathf.Cos(angle) * sampleRadius,
                0f,
                Mathf.Sin(angle) * sampleRadius);
            if (!TrySampleSurface(
                    samplePosition,
                    position.y,
                    effectRadius,
                    ignoredRoot,
                    supportHits,
                    out RaycastHit sampleHit))
            {
                continue;
            }

            validSamples++;
            Vector3 sampleNormal = GetSafeNormal(sampleHit.normal);
            if (Vector3.Angle(sampleNormal, slopeNormal) <= MatchingSlopeAngle)
            {
                matchingSlopeSamples++;
            }
            else
            {
                alternateSamples++;
                alternateHeightSum += sampleHit.point.y;
            }
        }

        bool slopeHasMajority = validSamples >= CoverageSampleCount / 2
            && matchingSlopeSamples > validSamples * RequiredSlopeCoverage;
        if (slopeHasMajority)
            return;

        normal = Vector3.up;
        if (alternateSamples > 0)
            position.y = alternateHeightSum / alternateSamples;
    }

    private static bool TrySampleSurface(
        Vector3 samplePosition,
        float referenceHeight,
        float effectRadius,
        Transform ignoredRoot,
        RaycastHit[] supportHits,
        out RaycastHit surfaceHit)
    {
        surfaceHit = default;
        float clearance = Mathf.Max(4f, effectRadius * 2f + 1f);
        Vector3 origin = new(samplePosition.x, referenceHeight + clearance, samplePosition.z);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            supportHits,
            clearance * 2f + effectRadius,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = supportHits[i];
            if (candidate.collider == null
                || candidate.distance >= closestDistance
                || !IsValidSupportingSurface(candidate, ignoredRoot)
                || IsMinorObstacle(candidate.collider, effectRadius))
            {
                continue;
            }

            closestDistance = candidate.distance;
            surfaceHit = candidate;
            found = true;
        }

        return found;
    }

    private static bool IsValidSupportingSurface(RaycastHit hit, Transform ignoredRoot)
    {
        if (!ReticleAimProvider.IsValidMortarTerrainTransform(hit.transform, ignoredRoot))
            return false;

        Rigidbody body = hit.rigidbody;
        return body == null
            || ReticleAimProvider.IsValidMortarTerrainTransform(body.transform, ignoredRoot);
    }

    private static bool IsMinorObstacle(Collider collider, float effectRadius)
    {
        if (collider == null)
            return false;

        Bounds bounds = collider.bounds;
        float radius = Mathf.Max(0f, effectRadius);
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        float maximumFootprint = Mathf.Max(0.8f, radius * 0.55f);
        float maximumHeight = Mathf.Max(0.75f, radius * 0.5f);
        return footprint <= maximumFootprint && bounds.size.y <= maximumHeight;
    }

    private static Vector3 GetSafeNormal(Vector3 normal)
    {
        return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
    }
}
