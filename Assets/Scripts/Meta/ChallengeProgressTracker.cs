using System;
using UnityEngine;

/// <summary>
/// Progreso de challenges Spec: kill credit por último hit, acumulativos cross-run, scratch por run.
/// </summary>
[DefaultExecutionOrder(-140)]
public class ChallengeProgressTracker : MonoBehaviour
{
    public static ChallengeProgressTracker Instance { get; private set; }

    private float _runDamageFreeSeconds;
    private float _lastPlayerDamageTime = -999f;
    private float _runMaxDamageInstance;
    private float _healthFullStamp = -1f;
    private bool _wasAtFullHealth;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (SaveManager.Instance == null)
            return;

        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health == null || !health.IsAlive)
        {
            _runDamageFreeSeconds = 0f;
            return;
        }

        float now = Time.time;
        if (now - _lastPlayerDamageTime >= 0f)
            _runDamageFreeSeconds = Mathf.Max(0f, now - _lastPlayerDamageTime);
        else
            _runDamageFreeSeconds += Time.deltaTime;

        // Survival adept: 120s without damage in a single run.
        if (_runDamageFreeSeconds >= 120f)
            SaveManager.Instance.ReportRunChallengeProgress("survival_adept", 120f, cumulative: false);

        bool atFull = health.CurrentHealth >= health.MaxHealth && health.CurrentHealth > 0;
        if (atFull && !_wasAtFullHealth)
            _healthFullStamp = now;
        _wasAtFullHealth = atFull;
    }

    public static void NotifyPlayerDamaged(PlayerHealth health, int healthBefore, int healthAfter)
    {
        if (Instance == null)
            EnsureExists();

        Instance._lastPlayerDamageTime = Time.time;
        Instance._runDamageFreeSeconds = 0f;

        // Not how you heal yourself: 100% -> 0% in under 5 seconds.
        if (healthBefore >= health.MaxHealth && healthAfter <= 0
            && Instance._healthFullStamp > 0f
            && Time.time - Instance._healthFullStamp <= 5f)
        {
            SaveManager.Instance?.ReportRunChallengeProgress("not_how_you_heal", 1f, cumulative: false);
        }
    }

    public static void NotifyDamageInstance(int appliedDamage)
    {
        if (appliedDamage <= 0)
            return;
        if (Instance == null)
            EnsureExists();

        if (appliedDamage > Instance._runMaxDamageInstance)
        {
            Instance._runMaxDamageInstance = appliedDamage;
            if (appliedDamage >= 1000)
                SaveManager.Instance?.ReportRunChallengeProgress("one_shot_one_kill", appliedDamage, cumulative: false);
        }
    }

    public static void NotifyEnemyKilled(EnemyHealth enemy)
    {
        if (enemy == null || SaveManager.Instance == null)
            return;
        if (Instance == null)
            EnsureExists();

        string weaponId = enemy.LastDamagingWeaponId;
        if (!string.IsNullOrEmpty(weaponId))
            SaveManager.Instance.ReportWeaponKill(weaponId);

        if (WeaponEnemyClassifier.CountsAsEliteOrBoss(enemy.transform))
            SaveManager.Instance.ReportEliteOrBossKill();
    }

    public static void NotifyDropLooted(int amount = 1)
    {
        if (amount <= 0 || SaveManager.Instance == null)
            return;
        SaveManager.Instance.ReportDropsLooted(amount);
    }

    public static void ResetRunScratch()
    {
        if (Instance == null)
            return;
        Instance._runDamageFreeSeconds = 0f;
        Instance._lastPlayerDamageTime = Time.time;
        Instance._runMaxDamageInstance = 0f;
        Instance._healthFullStamp = -1f;
        Instance._wasAtFullHealth = false;
    }

    private static void EnsureExists()
    {
        if (Instance != null)
            return;
        var go = new GameObject(nameof(ChallengeProgressTracker));
        go.AddComponent<ChallengeProgressTracker>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureExists();
}
