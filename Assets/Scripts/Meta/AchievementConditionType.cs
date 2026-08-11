/// <summary>
/// Catálogo estandarizado de condiciones de logro. La mayoría se evalúan contra contadores
/// de vida acumulados en <see cref="SaveData"/> sin necesidad de código nuevo por logro;
/// <see cref="Custom"/> es el escape hatch para condiciones que no entran en una fórmula genérica.
/// </summary>
public enum AchievementConditionType
{
    /// <summary>Bosses derrotados acumulados entre todas las runs.</summary>
    BossKillsTotal,
    /// <summary>Runs ganadas (llegar a la salida) acumuladas.</summary>
    RunsCompletedTotal,
    /// <summary>Enemigos eliminados acumulados entre todas las runs.</summary>
    EnemiesKilledTotal,
    /// <summary>Mejor tiempo de supervivencia en una sola run (segundos).</summary>
    SurviveTimeSingleRun,
    /// <summary>Nivel de jugador más alto alcanzado en cualquier run.</summary>
    PlayerLevelReached,
    /// <summary>Nivel más alto alcanzado por un arma específica (usa <see cref="AchievementDefinition.WeaponIdFilter"/>).</summary>
    WeaponLevelReached,
    /// <summary>Condición a medida reportada por código vía <see cref="SaveManager.ReportCustomProgress"/> (usa <see cref="AchievementDefinition.CustomKey"/>).</summary>
    Custom
}
