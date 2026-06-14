using UnityEngine;

public static class AutomaticCannonFireLogic
{
    // Converts cannon volley rate into an interval while preserving normal attack-rate scaling.
    public static float GetManualBurstInterval(
        float baseBurstsPerSecond,
        float attackSpeedMultiplier,
        float weaponRateMultiplier)
    {
        float burstRate = Mathf.Max(0.01f, baseBurstsPerSecond);
        float attackSpeed = Mathf.Max(0.01f, attackSpeedMultiplier);
        float weaponRate = Mathf.Max(0.01f, weaponRateMultiplier);
        return 1f / Mathf.Max(0.05f, burstRate * attackSpeed * weaponRate);
    }

    // Applies deterministic two-axis scatter from a sample inside the unit circle.
    public static Vector3 ApplyProjectileScatter(
        Vector3 direction,
        float spreadDegrees,
        Vector2 unitCircleSample)
    {
        Vector3 baseDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;

        if (spreadDegrees <= 0f)
            return baseDirection;

        Vector2 sample = Vector2.ClampMagnitude(unitCircleSample, 1f);
        Vector2 spread = sample * spreadDegrees;
        Quaternion aimRotation = Quaternion.LookRotation(baseDirection, GetStableUp(baseDirection));
        return (aimRotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward).normalized;
    }

    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f
            ? Vector3.forward
            : Vector3.up;
    }
}
