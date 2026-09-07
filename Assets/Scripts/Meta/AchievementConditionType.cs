/// <summary>
/// Catálogo de condiciones de logro / challenge Spec.
/// </summary>
public enum AchievementConditionType
{
    BossKillsTotal,
    RunsCompletedTotal,
    EnemiesKilledTotal,
    SurviveTimeSingleRun,
    PlayerLevelReached,
    WeaponLevelReached,
    Custom,
    /// <summary>Kills con un arma específica (WeaponIdFilter), acumulativo cross-run.</summary>
    WeaponKillsTotal,
    /// <summary>Variants/elites/bosses acumulados.</summary>
    EliteOrBossKillsTotal,
    /// <summary>Drops de materiales looteados acumulados.</summary>
    DropsLootedTotal,
    /// <summary>Progreso scratch de run reportado vía ReportRunChallengeProgress (CustomKey).</summary>
    RunChallenge
}
