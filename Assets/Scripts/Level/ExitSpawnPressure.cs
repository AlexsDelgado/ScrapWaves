using UnityEngine;

/// <summary>
/// Multiplicador global de presión de spawn tras reunir todas las llaves (×2, ×3, ×4…).
/// </summary>
public static class ExitSpawnPressure
{
    public static bool IsActive { get; private set; }
    public static float SpawnRateMultiplier { get; private set; } = 1f;

    public static void SetActive(bool active, float multiplier)
    {
        IsActive = active;
        SpawnRateMultiplier = active ? Mathf.Max(1f, multiplier) : 1f;
        if (active)
            OverheatSwarmBoost.SetExitPressureMultiplier(SpawnRateMultiplier);
        else
            OverheatSwarmBoost.ClearExitPressure();
    }

    public static void SetMultiplier(float multiplier)
    {
        if (!IsActive)
            return;

        SpawnRateMultiplier = Mathf.Max(1f, multiplier);
        OverheatSwarmBoost.SetExitPressureMultiplier(SpawnRateMultiplier);
    }
}
