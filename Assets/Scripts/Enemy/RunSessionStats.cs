using System;

/// <summary>
/// Estadísticas de la partida actual: tiempo transcurrido y bosses derrotados.
/// </summary>
public static class RunSessionStats
{
    static float _runStartTime;
    static int _bossKills;

    public static int BossKills => _bossKills;

    public static float ElapsedSeconds => UnityEngine.Time.unscaledTime - _runStartTime;

    public static void Reset()
    {
        _runStartTime = UnityEngine.Time.unscaledTime;
        _bossKills = 0;
        RunCombatStats.Reset();
    }

    public static void RegisterBossKill() => _bossKills++;

    public static string FormatTime(float seconds)
    {
        if (seconds < 0f)
            seconds = 0f;

        int total = (int)seconds;
        int minutes = total / 60;
        int secs = total % 60;
        return $"{minutes:00}:{secs:00}";
    }

    public static string FormatElapsed() => FormatTime(ElapsedSeconds);
}
