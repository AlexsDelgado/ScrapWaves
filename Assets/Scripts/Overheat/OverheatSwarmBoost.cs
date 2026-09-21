using UnityEngine;

/// <summary>
/// Global multipliers during the intense Overheat phase (last timer segment)
/// and during exit pressure (completed keys).
/// Only affects enemies from <see cref="SwarmEnemyPool"/> (not the boss).
/// </summary>
public static class OverheatSwarmBoost
{
    private static bool s_intensityActive;
    private static float s_exitPressureMultiplier = 1f;

    public static float SpeedMultiplier
    {
        get
        {
            float mult = 1f;
            if (s_intensityActive)
                mult = Mathf.Max(mult, 2f);
            if (s_exitPressureMultiplier > 1f)
                mult = Mathf.Max(mult, s_exitPressureMultiplier);
            return mult;
        }
    }

    public static int SpawnWaveMultiplier => Mathf.Max(1, Mathf.RoundToInt(SpeedMultiplier));

    /// <summary>
    /// Solo la parte de exit pressure, sin el boost de la fase intermedia. La cantidad de enemigos por
    /// oleada la maneja ahora la curva de <see cref="HeatManager"/>; esto evita contar el boost dos veces.
    /// Conserva el mismo redondeo que <see cref="SpawnWaveMultiplier"/> para no cambiar la fase de escape.
    /// </summary>
    public static int ExitPressureSpawnMultiplier => Mathf.Max(1, Mathf.RoundToInt(s_exitPressureMultiplier));

    public static bool IsIntensityActive => s_intensityActive;

    public static void SetIntensity(bool active) => s_intensityActive = active;

    public static void SetExitPressureMultiplier(float multiplier)
    {
        s_exitPressureMultiplier = multiplier < 1f ? 1f : multiplier;
    }

    public static void ClearExitPressure() => s_exitPressureMultiplier = 1f;
}
