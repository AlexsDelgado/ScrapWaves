using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistencia de meta-progresión (desbloqueos, Scrap, logros) entre runs y entre cierres del
/// juego. Se auto-crea antes de cargar la primera escena (mismo patrón que <see cref="EconomyBootstrap"/>)
/// y sobrevive a cambios de escena con DontDestroyOnLoad. Guarda a JSON en Application.persistentDataPath.
/// </summary>
[DefaultExecutionOrder(-150)]
public class SaveManager : MonoBehaviour
{
    private const string SaveFileName = "scrapwaves_save.json";

    public static SaveManager Instance { get; private set; }

    [SerializeField, Tooltip("Todos los logros del juego. Se evalúan automáticamente contra los contadores acumulados.")]
    private List<AchievementDefinition> _achievementCatalog = new();

    private SaveData _data = new();
    private string _path;

    public int Scrap => _data.Scrap;
    public IReadOnlyList<AchievementDefinition> AchievementCatalog => _achievementCatalog;

    public event Action OnUnlocksChanged;
    public event Action OnScrapChanged;
    public event Action<AchievementDefinition> OnAchievementUnlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreateBootstrap()
    {
        if (Instance != null || FindAnyObjectByType<SaveManager>() != null)
            return;

        var go = new GameObject(nameof(SaveManager));
        go.AddComponent<SaveManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _path = Path.Combine(Application.persistentDataPath, SaveFileName);
        EnsureAchievementCatalog();
        Load();
    }

    private void EnsureAchievementCatalog()
    {
        if (_achievementCatalog.Count > 0)
            return;

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets(
            "t:AchievementDefinition", new[] { "Assets/ScriptableObjects/Meta/Achievements" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            AchievementDefinition achievement = UnityEditor.AssetDatabase.LoadAssetAtPath<AchievementDefinition>(path);
            if (achievement != null)
                _achievementCatalog.Add(achievement);
        }
#endif
    }

    private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => HookGameplayEvents();

    private void HookGameplayEvents()
    {
        PlayerXP xp = FindAnyObjectByType<PlayerXP>();
        if (xp == null)
            return;

        xp.OnLevelUp -= HandlePlayerLevelUp;
        xp.OnLevelUp += HandlePlayerLevelUp;
    }

    private void HandlePlayerLevelUp(int newLevel)
    {
        if (newLevel <= _data.HighestPlayerLevel)
            return;

        _data.HighestPlayerLevel = newLevel;
        EvaluateAchievements();
        Save();
    }

    /// <summary>Llamado por GameManager.EnterEndState al terminar cualquier run (victoria o derrota).</summary>
    public void ReportRunEnded(bool victory, int bossKillsThisRun, int enemiesKilledThisRun, float survivalSeconds, int scrapEarned)
    {
        if (victory)
            _data.TotalRunsCompleted++;

        _data.TotalBossKills += Mathf.Max(0, bossKillsThisRun);
        _data.TotalEnemiesKilled += Mathf.Max(0, enemiesKilledThisRun);
        if (survivalSeconds > _data.BestSurvivalTimeSeconds)
            _data.BestSurvivalTimeSeconds = survivalSeconds;

        AddScrap(Mathf.Max(0, scrapEarned));
        EvaluateAchievements();
        Save();
    }

    /// <summary>Hook opcional para logros de tipo WeaponLevelReached (sin call site todavía, ver doc).</summary>
    public void ReportWeaponLevelReached(string weaponId, int level)
    {
        if (string.IsNullOrEmpty(weaponId))
            return;

        WeaponLevelRecord record = _data.WeaponLevels.Find(r => r.WeaponId == weaponId);
        if (record == null)
        {
            _data.WeaponLevels.Add(new WeaponLevelRecord { WeaponId = weaponId, HighestLevel = level });
        }
        else if (level > record.HighestLevel)
        {
            record.HighestLevel = level;
        }
        else
        {
            return;
        }

        EvaluateAchievements();
        Save();
    }

    /// <summary>Escape hatch para logros AchievementConditionType.Custom que no entran en un contador genérico.</summary>
    public void ReportCustomProgress(string key, float value)
    {
        if (string.IsNullOrEmpty(key))
            return;

        CustomProgressRecord record = _data.CustomProgress.Find(r => r.Key == key);
        if (record == null)
        {
            _data.CustomProgress.Add(new CustomProgressRecord { Key = key, Value = value });
        }
        else if (value > record.Value)
        {
            record.Value = value;
        }
        else
        {
            return;
        }

        EvaluateAchievements();
        Save();
    }

    public bool IsUnlocked(IUnlockable item)
    {
        if (item == null)
            return false;
        if (item.UnlockedFromStart)
            return true;
        return _data.UnlockedIds.Contains(item.UnlockId);
    }

    public bool IsAchievementUnlocked(AchievementDefinition achievement) =>
        achievement != null && _data.UnlockedAchievementIds.Contains(achievement.AchievementId);

    /// <summary>Intenta comprar/desbloquear un ítem. Devuelve false si ya estaba desbloqueado, si falta el logro
    /// requerido, si no hay Requirement configurado, o si no alcanza el Scrap.</summary>
    public bool TryPurchase(IUnlockable item)
    {
        if (item == null || IsUnlocked(item))
            return false;

        UnlockRequirement requirement = item.Requirement;
        if (requirement == null)
            return false;

        if (requirement.RequiredAchievement != null && !IsAchievementUnlocked(requirement.RequiredAchievement))
            return false;

        if (_data.Scrap < requirement.ScrapPrice)
            return false;

        _data.Scrap -= requirement.ScrapPrice;
        Unlock(item.UnlockId);
        OnScrapChanged?.Invoke();
        return true;
    }

    public void AddScrap(int amount)
    {
        if (amount == 0)
            return;

        _data.Scrap += amount;
        OnScrapChanged?.Invoke();
    }

    public float GetProgress(AchievementDefinition achievement)
    {
        if (achievement == null)
            return 0f;

        switch (achievement.ConditionType)
        {
            case AchievementConditionType.BossKillsTotal: return _data.TotalBossKills;
            case AchievementConditionType.RunsCompletedTotal: return _data.TotalRunsCompleted;
            case AchievementConditionType.EnemiesKilledTotal: return _data.TotalEnemiesKilled;
            case AchievementConditionType.SurviveTimeSingleRun: return _data.BestSurvivalTimeSeconds;
            case AchievementConditionType.PlayerLevelReached: return _data.HighestPlayerLevel;
            case AchievementConditionType.WeaponLevelReached:
                WeaponLevelRecord record = _data.WeaponLevels.Find(r => r.WeaponId == achievement.WeaponIdFilter);
                return record?.HighestLevel ?? 0;
            case AchievementConditionType.Custom:
                CustomProgressRecord custom = _data.CustomProgress.Find(r => r.Key == achievement.CustomKey);
                return custom?.Value ?? 0f;
            default:
                return 0f;
        }
    }

    private void Unlock(string id)
    {
        if (string.IsNullOrEmpty(id) || _data.UnlockedIds.Contains(id))
            return;

        _data.UnlockedIds.Add(id);
        OnUnlocksChanged?.Invoke();
        Save();
    }

    private void EvaluateAchievements()
    {
        for (int i = 0; i < _achievementCatalog.Count; i++)
        {
            AchievementDefinition achievement = _achievementCatalog[i];
            if (achievement == null || IsAchievementUnlocked(achievement))
                continue;

            if (GetProgress(achievement) + 0.0001f < achievement.TargetValue)
                continue;

            _data.UnlockedAchievementIds.Add(achievement.AchievementId);
            if (achievement.ScrapReward > 0)
                AddScrap(achievement.ScrapReward);
            OnAchievementUnlocked?.Invoke(achievement);
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path)) ?? new SaveData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveManager: no se pudo leer el save ({e.Message}). Se arranca desde cero.");
            _data = new SaveData();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonUtility.ToJson(_data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveManager: no se pudo guardar el save ({e.Message}).");
        }
    }
}
