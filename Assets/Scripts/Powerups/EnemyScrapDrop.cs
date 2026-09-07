using UnityEngine;

/// <summary>Drop de Scrap mid-run (0.1% base, escala con Scavenging) + scrap de boss.</summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyScrapDrop : MonoBehaviour
{
    private const float BaseChance = 0.001f;
    private const float BaseScavenging = 50f;

    private EnemyHealth _health;
    private PlayerStats _playerStats;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _playerStats = FindAnyObjectByType<PlayerStats>();
    }

    private void OnEnable() => _health.OnDied += OnEnemyDied;
    private void OnDisable() => _health.OnDied -= OnEnemyDied;

    private void OnEnemyDied()
    {
        if (WeaponEnemyClassifier.GetKind(transform) == WeaponEnemyKind.Boss)
        {
            int amount = Random.Range(5, 11);
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.AddScrap(amount);
                SaveManager.Instance.SaveNow();
            }

            return;
        }

        float scavenging = _playerStats != null && _playerStats.GetDefinition(StatType.Scavenging) != null
            ? _playerStats.GetStat(StatType.Scavenging)
            : BaseScavenging;
        float chance = BaseChance * (scavenging / BaseScavenging);
        if (Random.value > Mathf.Clamp01(chance))
            return;

        ScrapPickupPool.GetInstance()?.TrySpawn(transform.position);
    }
}
