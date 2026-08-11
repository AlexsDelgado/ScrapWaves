using System;
using System.Collections.Generic;

/// <summary>
/// Récord con el nivel más alto alcanzado por un arma específica (para AchievementConditionType.WeaponLevelReached).
/// </summary>
[Serializable]
public class WeaponLevelRecord
{
    public string WeaponId;
    public int HighestLevel;
}

/// <summary>
/// Progreso acumulado de una condición Custom (para AchievementConditionType.Custom).
/// </summary>
[Serializable]
public class CustomProgressRecord
{
    public string Key;
    public float Value;
}

/// <summary>
/// Todo el estado persistente de meta-progresión. Serializado a JSON vía <see cref="SaveManager"/>.
/// JsonUtility no soporta Dictionary, por eso los contadores por-clave usan listas de records.
/// </summary>
[Serializable]
public class SaveData
{
    public int Version = 1;
    public int Scrap;
    public List<string> UnlockedIds = new();
    public List<string> UnlockedAchievementIds = new();

    // Contadores de vida usados por AchievementConditionType.
    public int TotalBossKills;
    public int TotalRunsCompleted;
    public int TotalEnemiesKilled;
    public float BestSurvivalTimeSeconds;
    public int HighestPlayerLevel;

    public List<WeaponLevelRecord> WeaponLevels = new();
    public List<CustomProgressRecord> CustomProgress = new();
}
