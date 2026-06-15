using UnityEngine;

/// <summary>
/// Multiplicadores globales durante la fase intensa del Overheat (último tramo del temporizador)
/// y durante la presión de salida (llaves completas).
/// Solo afecta a enemigos del <see cref="SwarmEnemyPool"/> (no al boss).
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

    public static bool IsIntensityActive => s_intensityActive;

    public static void SetIntensity(bool active) => s_intensityActive = active;

    public static void SetExitPressureMultiplier(float multiplier)
    {
        s_exitPressureMultiplier = multiplier < 1f ? 1f : multiplier;
    }

    public static void ClearExitPressure() => s_exitPressureMultiplier = 1f;
}
