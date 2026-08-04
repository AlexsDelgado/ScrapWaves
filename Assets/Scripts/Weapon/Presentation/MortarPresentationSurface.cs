using UnityEngine;

/// <summary>
/// Resolves a readable surface for mortar-only presentation without changing the
/// collision point used by damage. Large terrain keeps its real slope while a
/// small prop (for example, a loose stone) falls back to the supporting ground.
/// </summary>
public static class MortarPresentationSurface
{
    private const float BoundsPadding = 0.25f;

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
        if (sourceCollider == null
            || supportHits == null
            || supportHits.Length == 0
            || !IsMinorObstacle(sourceCollider, effectRadius))
        {
            return;
        }

        Bounds bounds = sourceCollider.bounds;
        Vector3 origin = new(position.x, bounds.max.y + BoundsPadding, position.z);
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
        RaycastHit supportingHit = default;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = supportHits[i];
            if (candidate.collider == null
                || candidate.collider == sourceCollider
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

        if (!found)
            return;

        position = supportingHit.point;
        normal = GetSafeNormal(supportingHit.normal);
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
