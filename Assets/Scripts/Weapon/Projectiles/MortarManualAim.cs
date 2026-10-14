using UnityEngine;

public readonly struct MortarManualAimSolution
{
    public readonly Vector3 Origin, Target, SurfacePoint, SurfaceNormal;
    public readonly float ArcHeight, Range;

    public MortarManualAimSolution(Vector3 origin, Vector3 target, RaycastHit surface, float arcHeight, float range)
    {
        Origin = origin;
        Target = target;
        SurfacePoint = surface.point;
        SurfaceNormal = surface.normal;
        ArcHeight = arcHeight;
        Range = range;
    }
}

/// <summary>Camera pitch controls horizontal range; the shell keeps its independently authored arc.</summary>
public static class MortarManualAim
{
    public static float EvaluateDistance(float pitchDegrees, MortarTuning tuning)
    {
        // Monotone cubic Hermite interpolation through the three designer control points.
        // Zero endpoint slopes also make the transition into either clamp smooth.
        float a = tuning.MortarManualMinimumPitch;
        float b = Mathf.Max(a + 0.01f, tuning.MortarManualLevelPitch);
        float c = Mathf.Max(b + 0.01f, tuning.MortarManualMaximumPitch);
        float x = Mathf.Max(0f, tuning.MortarManualMinimumRange);
        float y = Mathf.Max(x, tuning.MortarManualLevelRange);
        float z = Mathf.Max(y, tuning.MortarManualMaximumRange);
        if (pitchDegrees <= a) return x;
        if (pitchDegrees >= c) return z;
        float firstSlope = (y - x) / (b - a);
        float secondSlope = (z - y) / (c - b);
        float middleSlope = firstSlope > 0f && secondSlope > 0f
            ? 2f * firstSlope * secondSlope / (firstSlope + secondSlope) : 0f;
        return pitchDegrees <= b
            ? Hermite(x, y, 0f, middleSlope * (b - a), (pitchDegrees - a) / (b - a))
            : Hermite(y, z, middleSlope * (c - b), 0f, (pitchDegrees - b) / (c - b));
    }

    private static float Hermite(float a, float b, float ma, float mb, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * ma
            + (-2f * t3 + 3f * t2) * b + (t3 - t2) * mb;
    }

    public static bool TryResolve(Vector3 origin, Vector3 viewDirection, Vector3 fallbackForward,
        MortarTuning tuning, float collisionRadius, Transform ignoredRoot, RaycastHit[] hits,
        out MortarManualAimSolution solution)
    {
        solution = default;
        if (tuning == null || hits == null || hits.Length == 0 || viewDirection.sqrMagnitude < 0.0001f)
            return false;
        viewDirection.Normalize();
        float pitch = Mathf.Asin(Mathf.Clamp(viewDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 horizontal = Vector3.ProjectOnPlane(viewDirection, Vector3.up);
        if (horizontal.sqrMagnitude < 0.000001f)
            horizontal = Vector3.ProjectOnPlane(fallbackForward, Vector3.up);
        if (horizontal.sqrMagnitude < 0.000001f) horizontal = Vector3.forward;
        float range = EvaluateDistance(pitch, tuning);
        Vector3 column = origin + horizontal.normalized * range;
        float above = Mathf.Max(1f, tuning.MortarManualSurfaceProbeAbove);
        float below = Mathf.Max(1f, tuning.MortarManualSurfaceProbeBelow);
        int count = Physics.RaycastNonAlloc(column + Vector3.up * above, Vector3.down,
            hits, above + below, ~0, QueryTriggerInteraction.Ignore);
        // Dense stacked geometry must not silently pick an arbitrary surface from a full buffer.
        RaycastHit[] candidates = count == hits.Length
            ? Physics.RaycastAll(column + Vector3.up * above, Vector3.down, above + below,
                ~0, QueryTriggerInteraction.Ignore) : hits;
        if (candidates != hits) count = candidates.Length;
        float nearest = float.PositiveInfinity;
        RaycastHit surface = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = candidates[i];
            if (hit.normal.y < 0.2f || hit.distance >= nearest
                || !ReticleAimProvider.IsValidMortarTerrainTransform(hit.transform, ignoredRoot)
                || (hit.rigidbody != null && !ReticleAimProvider.IsValidMortarTerrainTransform(hit.rigidbody.transform, ignoredRoot)))
                continue;
            nearest = hit.distance;
            surface = hit;
        }
        if (float.IsPositiveInfinity(nearest)) return false;
        Vector3 target = surface.point + surface.normal * Mathf.Max(0.01f, collisionRadius);
        // Keep a rising launch and descending arrival even across steep elevation changes.
        float arc = Mathf.Max(tuning.MortarArcHeight, Mathf.Abs(target.y - origin.y) * 0.25f + 0.25f);
        solution = new MortarManualAimSolution(origin, target, surface, arc, range);
        return true;
    }
}
