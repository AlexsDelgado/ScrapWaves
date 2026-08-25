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
    public const int CurrentVersion = 2;

    public int Version = CurrentVersion;
    public int Scrap;
    public List<string> UnlockedIds = new();
    public List<string> UnlockedAchievementIds = new();

    // Player-facing presentation choices live beside progression in the same atomic JSON save.
    // SaveManager mutates this field without replacing the progression fields above or below.
    public PresentationAccessibilitySettings PresentationAccessibility = new();

    // Contadores de vida usados por AchievementConditionType.
    public int TotalBossKills;
    public int TotalRunsCompleted;
    public int TotalEnemiesKilled;
    public float BestSurvivalTimeSeconds;
    public int HighestPlayerLevel;

    public List<WeaponLevelRecord> WeaponLevels = new();
    public List<CustomProgressRecord> CustomProgress = new();

    /// <summary>Repairs data loaded from older or partially populated JSON saves.</summary>
    public void Sanitize()
    {
        if (Version < CurrentVersion)
            Version = CurrentVersion;
        UnlockedIds ??= new List<string>();
        UnlockedAchievementIds ??= new List<string>();
        WeaponLevels ??= new List<WeaponLevelRecord>();
        CustomProgress ??= new List<CustomProgressRecord>();
        PresentationAccessibility ??= new PresentationAccessibilitySettings();
        PresentationAccessibility.Sanitize();
    }
}
