using UnityEngine;

public static class MortarTrajectory
{
    // The same spatial polyline is traversed by previews and live shells.
    // Heat changes traversal speed, never the chords used for collision.
    private const int SegmentsPerFlight = 64;
    private static readonly Collider[] InitialOverlapBuffer = new Collider[32];
    public const float MinimumFlightFailsafe = 5f;
    public const float FlightFailsafeMultiplier = 5f;

    public static Vector3 Evaluate(Vector3 start, Vector3 target, float arcHeight, float normalizedTime)
    {
        Vector3 point = Vector3.LerpUnclamped(start, target, normalizedTime);
        point.y += 4f * Mathf.Max(0f, arcHeight) * normalizedTime * (1f - normalizedTime);
        return point;
    }

    public static Vector3 EvaluateCollisionPath(Vector3 start, Vector3 target, float arcHeight, float normalizedTime)
    {
        float sample = normalizedTime * SegmentsPerFlight;
        float lower = Mathf.Floor(sample);
        return Vector3.LerpUnclamped(
            Evaluate(start, target, arcHeight, lower / SegmentsPerFlight),
            Evaluate(start, target, arcHeight, (lower + 1f) / SegmentsPerFlight),
            sample - lower);
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
        return TryPredictCollision(start, target, arcHeight, travelTime, collisionRadius,
            ignoredRoot, hitBuffer, false, out terrainHit);
    }

    public static bool TryPredictCollision(
        Vector3 start, Vector3 target, float arcHeight, float travelTime,
        float collisionRadius, Transform ignoredRoot, RaycastHit[] hitBuffer,
        bool includeDamageables, out RaycastHit hit)
    {
        return TryGetCollisionBetweenNormalizedTimes(start, target, arcHeight, collisionRadius,
            ignoredRoot, hitBuffer, 0f, GetMaximumNormalizedTime(travelTime), includeDamageables, out hit);
    }

    public static bool TryGetCollisionBetweenNormalizedTimes(
        Vector3 start, Vector3 target, float arcHeight, float collisionRadius,
        Transform ignoredRoot, RaycastHit[] hitBuffer, float fromTime, float toTime,
        bool includeDamageables, out RaycastHit hit)
    {
        return TryGetCollisionBetweenNormalizedTimes(start, target, arcHeight, collisionRadius,
            ignoredRoot, hitBuffer, fromTime, toTime, includeDamageables, out hit, out _);
    }

    private static bool TryGetCollisionBetweenNormalizedTimes(
        Vector3 start, Vector3 target, float arcHeight, float collisionRadius,
        Transform ignoredRoot, RaycastHit[] hitBuffer, float fromTime, float toTime,
        bool includeDamageables, out RaycastHit hit, out float hitTime)
    {
        hit = default;
        hitTime = toTime;
        if (hitBuffer == null || hitBuffer.Length == 0 || toTime <= fromTime)
            return false;

        if (fromTime <= 0f && TryGetInitialOverlap(start,
                EvaluateCollisionPath(start, target, arcHeight, 1f / SegmentsPerFlight) - start,
                collisionRadius, ignoredRoot, includeDamageables, out hit))
        {
            hitTime = 0f;
            return true;
        }

        float previousTime = Mathf.Max(0f, fromTime);
        Vector3 previous = EvaluateCollisionPath(start, target, arcHeight, previousTime);
        int nextSample = Mathf.FloorToInt(previousTime * SegmentsPerFlight) + 1;
        while (previousTime < toTime)
        {
            float nextTime = Mathf.Min(toTime, nextSample / (float)SegmentsPerFlight);
            Vector3 next = EvaluateCollisionPath(start, target, arcHeight, nextTime);
            if (TryGetSegmentCollision(previous, next, collisionRadius, ignoredRoot,
                    hitBuffer, includeDamageables, out hit))
            {
                float distance = Vector3.Distance(previous, next);
                hitTime = Mathf.Lerp(previousTime, nextTime,
                    distance > 0.0001f ? Mathf.Clamp01(hit.distance / distance) : 0f);
                return true;
            }
            previous = next;
            previousTime = nextTime;
            nextSample++;
        }
        return false;
    }

    public static bool TryGetSegmentCollision(
        Vector3 start,
        Vector3 end,
        float radius,
        Transform ignoredRoot,
        RaycastHit[] hitBuffer,
        bool includeDamageables,
        out RaycastHit terrainHit)
    {
        terrainHit = default;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f || hitBuffer == null || hitBuffer.Length == 0)
            return false;

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            Mathf.Max(0.01f, radius),
            displacement / distance,
            hitBuffer,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        // NonAlloc does not promise the closest hits when its buffer fills.
        // Allocate only in that exceptional dense case, then filter all hits.
        RaycastHit[] candidates = hitBuffer;
        if (hitCount == hitBuffer.Length)
        {
            candidates = Physics.SphereCastAll(start, Mathf.Max(0.01f, radius),
                displacement / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            hitCount = candidates.Length;
        }
        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = candidates[i];
            if (!IsValidCollisionTransform(hit.transform, ignoredRoot, includeDamageables))
                continue;
            Rigidbody body = hit.rigidbody;
            if (body != null && !IsValidCollisionTransform(body.transform, ignoredRoot, includeDamageables))
                continue;
            if (hit.distance >= closestDistance)
                continue;
            // A sweep that begins touching/overlapping a collider can return its
            // identity but a synthetic point at world zero and -sweep normal.
            // This can happen on any chord, not just at the launch origin.
            // Recover from that collider only; ordinary hits need no extra query.
            if (hit.distance <= 0f)
            {
                if (TryRecoverOverlapContact(hit.collider, start, displacement, radius, out RaycastHit overlapHit))
                    hit = overlapHit;
                else
                {
                    // The overlap itself is real even when a degenerate surface
                    // cannot provide a ray contact. Keep that collision at the
                    // touching shell, never PhysX's synthetic world-zero point.
                    hit.point = start;
                    hit.normal = -displacement / distance;
                }
            }
            closestDistance = hit.distance;
            terrainHit = hit;
            found = true;
        }
        return found;
    }

    private static bool TryGetInitialOverlap(
        Vector3 start, Vector3 launchDirection, float collisionRadius,
        Transform ignoredRoot, bool includeDamageables, out RaycastHit hit)
    {
        hit = default;
        float radius = Mathf.Max(0.01f, collisionRadius);
        int count = Physics.OverlapSphereNonAlloc(start, radius, InitialOverlapBuffer,
            ~0, QueryTriggerInteraction.Ignore);
        Collider[] candidates = InitialOverlapBuffer;
        if (count == InitialOverlapBuffer.Length)
        {
            candidates = Physics.OverlapSphere(start, radius, ~0, QueryTriggerInteraction.Ignore);
            count = candidates.Length;
        }

        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = candidates[i];
            if (candidate == null
                || !IsValidCollisionTransform(candidate.transform, ignoredRoot, includeDamageables)
                || (candidate.attachedRigidbody != null
                    && !IsValidCollisionTransform(candidate.attachedRigidbody.transform, ignoredRoot, includeDamageables)))
                continue;

            if (!TryRecoverOverlapContact(candidate, start, launchDirection, radius,
                    out RaycastHit candidateHit))
                continue;

            float distance = (candidateHit.point - start).sqrMagnitude;
            if (distance >= closestDistance)
                continue;
            closestDistance = distance;
            hit = candidateHit;
            found = true;
        }
        return found;
    }

    private static bool TryRecoverOverlapContact(Collider candidate, Vector3 start,
        Vector3 launchDirection, float collisionRadius, out RaycastHit hit)
    {
        hit = default;
        if (candidate == null) return false;
        float radius = Mathf.Max(0.01f, collisionRadius);
        bool requiresRayRecovery = candidate is TerrainCollider
            || (candidate is MeshCollider meshCollider && !meshCollider.convex);
        Vector3 incoming = launchDirection.sqrMagnitude > 0.000001f
            ? launchDirection.normalized : Vector3.down;
        float localReach = radius * 4f + 0.01f;
        float contactReach = radius + 0.002f;
        Vector3 outward;
        if (requiresRayRecovery)
        {
            // ClosestPoint does not support terrain or nonconvex meshes. Start
            // locally so a distant part of the same mesh cannot win the ray.
            if (candidate.Raycast(new Ray(start, incoming), out hit, localReach)
                && (hit.point - start).sqrMagnitude <= contactReach * contactReach)
            {
                hit.distance = 0f;
                return true;
            }
            // A grazing incoming ray can run almost parallel to nearby terrain.
            // Probe the same collider from the six local cardinal directions;
            // unlike a global ray this cannot select another stacked surface.
            if (TryRecoverLocalSurface(candidate, start, localReach, out hit)
                && (hit.point - start).sqrMagnitude <= contactReach * contactReach)
                return true;
            outward = -incoming;
        }
        else
        {
            Vector3 closestPoint = candidate.ClosestPoint(start);
            outward = start - closestPoint;
            if (outward.sqrMagnitude <= 0.000001f)
                outward = -incoming;
            else
                outward.Normalize();
        }
        // Start beyond this collider's bounds, then raycast toward its nearest
        // surface. Embedded centers use the entering side of the current chord.
        Bounds bounds = candidate.bounds;
        float clearance = Vector3.Distance(start, bounds.center) + bounds.extents.magnitude + radius + 0.1f;
        if (!candidate.Raycast(new Ray(start + outward * clearance, -outward),
                out hit, clearance + radius))
            return false;
        if (requiresRayRecovery && (hit.point - start).sqrMagnitude > contactReach * contactReach)
            return false;
        hit.distance = 0f;
        return true;
    }

    private static bool TryRecoverLocalSurface(Collider candidate, Vector3 start, float reach, out RaycastHit hit)
    {
        hit = default;
        float nearest = reach * reach;
        bool found = false;
        for (int axis = 0; axis < 6; axis++)
        {
            Vector3 outward = axis switch
            {
                0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.right,
                3 => Vector3.left, 4 => Vector3.forward, _ => Vector3.back
            };
            if (!candidate.Raycast(new Ray(start + outward * reach, -outward),
                    out RaycastHit sample, reach * 2f))
                continue;
            float distance = (sample.point - start).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            hit = sample;
            found = true;
        }
        if (found)
        {
            // Axis probes locate a face; its normal recovers the perpendicular
            // contact on slopes instead of a farther point along the axis ray.
            Vector3 outward = hit.normal.normalized;
            if (candidate.Raycast(new Ray(start + outward * reach, -outward),
                    out RaycastHit perpendicular, reach * 2f)
                && (perpendicular.point - start).sqrMagnitude <= nearest + 0.000001f)
                hit = perpendicular;
            hit.distance = 0f;
        }
        return found;
    }

    private static bool IsValidCollisionTransform(Transform candidate, Transform ignoredRoot, bool includeDamageables)
    {
        if (candidate == null || (ignoredRoot != null
            && (candidate == ignoredRoot || candidate.IsChildOf(ignoredRoot))))
            return false;
        return includeDamageables || ReticleAimProvider.IsValidMortarTerrainTransform(candidate, ignoredRoot);
    }

    public static float GetManualGrapeshotAirburstTime(
        Vector3 start, Vector3 target, float arcHeight, float collisionRadius,
        Transform ignoredRoot, RaycastHit[] hitBuffer)
    {
        float apex = arcHeight <= 0.0001f ? 0.5f
            : Mathf.Clamp01(0.5f + (target.y - start.y) / (8f * arcHeight));
        TryGetCollisionBetweenNormalizedTimes(start, target, arcHeight, collisionRadius,
            ignoredRoot, hitBuffer, apex, 1f, false, out _, out float impactTime);
        return Mathf.Clamp01(Mathf.Lerp(apex, impactTime, 0.5f));
    }

    public static Vector3 GetManualGrapeshotTarget(
        Vector3 selectedTarget, Vector3 surfaceNormal, float parentRadius, float childRadius)
    {
        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
        return selectedTarget + normal * (childRadius - parentRadius);
    }

    // The marker represents the center pellet of the authored scatter footprint.
    // A blocked parent bursts at the obstruction instead of passing through it.
    public static bool TryPredictManualCollision(
        Vector3 start, Vector3 target, float arcHeight, float travelTime,
        float collisionRadius, Transform ignoredRoot, RaycastHit[] hitBuffer,
        MortarUpgradePayload payload, out RaycastHit hit, Vector3 manualLandingNormal = default)
    {
        if (!payload.UseGrapeshot)
            return TryPredictCollision(start, target, arcHeight, travelTime, collisionRadius,
                ignoredRoot, hitBuffer, payload.RepeatExplosionCount <= 1, out hit);

        float airburstTime = GetManualGrapeshotAirburstTime(start, target, arcHeight,
            collisionRadius, ignoredRoot, hitBuffer);
        if (TryGetCollisionBetweenNormalizedTimes(start, target, arcHeight, collisionRadius,
                ignoredRoot, hitBuffer, 0f, airburstTime, false, out hit))
            return true;

        Vector3 airburst = EvaluateCollisionPath(start, target, arcHeight, airburstTime);
        float childRadius = Mathf.Max(0.04f, collisionRadius * 0.75f);
        Vector3 childTarget = GetManualGrapeshotTarget(target, manualLandingNormal, collisionRadius, childRadius);
        return TryPredictCollision(airburst, childTarget, 0f,
            Mathf.Clamp(travelTime * 0.45f, 0.22f, 0.6f), childRadius,
            ignoredRoot, hitBuffer, true, out hit);
    }
}
