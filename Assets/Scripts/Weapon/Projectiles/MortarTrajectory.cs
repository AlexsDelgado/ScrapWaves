using UnityEngine;

public static class MortarTrajectory
{
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
}
