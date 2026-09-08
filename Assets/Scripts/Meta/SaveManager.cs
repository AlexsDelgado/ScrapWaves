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
    public PresentationAccessibilityState PresentationAccessibility =>
        _data?.PresentationAccessibility?.ToState() ?? PresentationAccessibilityState.Default;

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
        SpecMetaBootstrap.EnsureRegistered();
    }

    private void EnsureAchievementCatalog()
    {
        if (_achievementCatalog.Count > 0)
            return;

        // Resources.LoadAll funciona igual en el Editor y en una build (a diferencia de
        // AssetDatabase, que solo existe en el Editor) — necesario para que los logros no
        // aparezcan vacíos en builds reales.
        _achievementCatalog.AddRange(Resources.LoadAll<AchievementDefinition>("Meta/Achievements"));
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

    public bool IsUnlocked(string unlockId)
    {
        if (string.IsNullOrEmpty(unlockId))
            return false;
        return _data.UnlockedIds.Contains(unlockId);
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

    /// <summary>
    /// Borra todo el progreso persistente (desbloqueos, Scrap, logros y contadores) y
    /// vuelve a guardar el archivo vacío. Pensado como herramienta de demo/QA para poder
    /// mostrar el flujo de desbloqueo desde cero sin reinstalar — no hay soft-lock posible
    /// porque el contenido existente vuelve a su default (UnlockedFromStart).
    /// </summary>
    public void ResetProgress()
    {
        PresentationAccessibilitySettings preservedAccessibility =
            _data?.PresentationAccessibility?.CloneSanitized() ?? new PresentationAccessibilitySettings();
        _data = new SaveData
        {
            PresentationAccessibility = preservedAccessibility
        };
        _data.Sanitize();
        PresentationAccessibilityRuntime.Apply(_data.PresentationAccessibility);
        Save();
        OnScrapChanged?.Invoke();
        OnUnlocksChanged?.Invoke();
    }

    /// <summary>
    /// Persists a global presentation snapshot without replacing or resetting meta progression.
    /// Sandbox-local GameFeelRuntimeOptions must not call this method.
    /// </summary>
    public void SetPresentationAccessibility(PresentationAccessibilityState state)
    {
        _data ??= new SaveData();
        _data.Sanitize();

        PresentationAccessibilityState sanitized = new(
            state.ReducedMotion,
            state.ReducedShake,
            state.ReducedFlash,
            state.CombatText,
            state.CombatTextScale);

        if (_data.PresentationAccessibility.ToState() == sanitized)
        {
            PresentationAccessibilityRuntime.Apply(sanitized);
            return;
        }

        _data.PresentationAccessibility = new PresentationAccessibilitySettings(sanitized);
        PresentationAccessibilityRuntime.Apply(sanitized);
        Save();
    }

    public void SetPresentationAccessibility(PresentationAccessibilitySettings settings)
    {
        SetPresentationAccessibility(
            settings != null ? settings.ToState() : PresentationAccessibilityState.Default);
    }

    public void AddScrap(int amount)
    {
        if (amount == 0)
            return;

        _data.Scrap += amount;
        OnScrapChanged?.Invoke();
    }

    public void SaveNow() => Save();

    public void RegisterRuntimeAchievements(IEnumerable<AchievementDefinition> achievements)
    {
        if (achievements == null)
            return;

        foreach (AchievementDefinition achievement in achievements)
        {
            if (achievement == null)
                continue;
            bool exists = false;
            for (int i = 0; i < _achievementCatalog.Count; i++)
            {
                if (_achievementCatalog[i] != null
                    && _achievementCatalog[i].AchievementId == achievement.AchievementId)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                _achievementCatalog.Add(achievement);
        }

        EvaluateAchievements();
    }

    public void ReportWeaponKill(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId))
            return;

        WeaponKillRecord record = _data.WeaponKills.Find(r => r.WeaponId == weaponId);
        if (record == null)
            _data.WeaponKills.Add(new WeaponKillRecord { WeaponId = weaponId, Kills = 1 });
        else
            record.Kills++;

        EvaluateAchievements();
        Save();
    }

    public void ReportEliteOrBossKill()
    {
        _data.TotalEliteOrBossKills++;
        EvaluateAchievements();
        Save();
    }

    public void ReportDropsLooted(int amount)
    {
        if (amount <= 0)
            return;
        _data.TotalDropsLooted += amount;
        EvaluateAchievements();
        Save();
    }

    /// <summary>
    /// Challenges no acumulativos (scratch de run) o custom keys.
    /// Si cumulative=false, solo guarda si supera el valor previo de esa run-key en CustomProgress.
    /// </summary>
    public void ReportRunChallengeProgress(string key, float value, bool cumulative)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (cumulative)
        {
            ReportCustomProgress(key, value);
            return;
        }

        // Non-cumulative: treat as max-in-run stored under key; EvaluateAchievements reads it.
        CustomProgressRecord record = _data.CustomProgress.Find(r => r.Key == key);
        if (record == null)
            _data.CustomProgress.Add(new CustomProgressRecord { Key = key, Value = value });
        else if (value > record.Value)
            record.Value = value;
        else
            return;

        EvaluateAchievements();
        Save();
    }

    public int GetMetaStatLevel(StatType statType) => _data.GetMetaStatLevel(statType);

    public int GetMetaItemUpgradeLevel(string unlockId) => _data.GetMetaItemUpgradeLevel(unlockId);

    public float GetMetaStatBaseMultiplier(StatType statType)
    {
        int level = GetMetaStatLevel(statType);
        return 1f + 0.05f * level;
    }

    public float GetMetaStatGrowthMultiplier(StatType statType)
    {
        int level = GetMetaStatLevel(statType);
        float growth = 1f;
        if (level >= 5)
            growth *= 1.15f;
        if (level >= 10)
            growth *= 1.15f;
        return growth;
    }

    public float GetMetaItemPowerMultiplier(string unlockId)
    {
        int level = GetMetaItemUpgradeLevel(unlockId);
        return Mathf.Pow(1.1f, level);
    }

    public bool TryPurchaseMetaStatUpgrade(StatType statType, MetaStatUpgradeCosts costs)
    {
        int current = GetMetaStatLevel(statType);
        if (current >= 10 || costs == null)
            return false;

        int price = costs.GetStatUpgradeCost(current + 1);
        if (_data.Scrap < price)
            return false;

        _data.Scrap -= price;
        _data.SetMetaStatLevel(statType, current + 1);
        OnScrapChanged?.Invoke();
        OnUnlocksChanged?.Invoke();
        Save();
        return true;
    }

    public bool TryPurchaseMetaItemUpgrade(string unlockId, MetaStatUpgradeCosts costs)
    {
        if (string.IsNullOrEmpty(unlockId) || costs == null)
            return false;

        int current = GetMetaItemUpgradeLevel(unlockId);
        if (current >= 3)
            return false;

        int price = costs.GetItemUpgradeCost(current + 1);
        if (_data.Scrap < price)
            return false;

        _data.Scrap -= price;
        _data.SetMetaItemUpgradeLevel(unlockId, current + 1);
        OnScrapChanged?.Invoke();
        OnUnlocksChanged?.Invoke();
        Save();
        return true;
    }

    public bool IsPathUnlocked(WeaponData weapon, WeaponUpgradePath path)
    {
        if (weapon == null || path == WeaponUpgradePath.None || path == WeaponUpgradePath.PathA)
            return true;

        string id = WeaponPathUnlockData.BuildUnlockId(weapon, path);
        return _data.UnlockedIds.Contains(id);
    }

    public void UnlockIdDirect(string unlockId)
    {
        Unlock(unlockId);
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
            case AchievementConditionType.WeaponKillsTotal:
                WeaponKillRecord kills = _data.WeaponKills.Find(r => r.WeaponId == achievement.WeaponIdFilter);
                return kills?.Kills ?? 0;
            case AchievementConditionType.EliteOrBossKillsTotal:
                return _data.TotalEliteOrBossKills;
            case AchievementConditionType.DropsLootedTotal:
                return _data.TotalDropsLooted;
            case AchievementConditionType.Custom:
            case AchievementConditionType.RunChallenge:
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

            IReadOnlyList<string> rewards = achievement.RewardUnlockIds;
            if (rewards != null)
            {
                for (int r = 0; r < rewards.Count; r++)
                    Unlock(rewards[r]);
            }

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

        _data ??= new SaveData();
        _data.Sanitize();
        PresentationAccessibilityRuntime.Apply(_data.PresentationAccessibility);
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
