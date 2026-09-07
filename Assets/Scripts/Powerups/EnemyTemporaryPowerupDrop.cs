using UnityEngine;

/// <summary>
/// Al morir el enemigo, roll Spec de Temporary Power-up (0.1% normal / 1% elite, escala con Scavenging).
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyTemporaryPowerupDrop : MonoBehaviour
{
    private const float BaseNormalChance = 0.001f;
    private const float BaseEliteChance = 0.01f;
    private const float BaseScavenging = 50f;

    private EnemyHealth _health;
    private PlayerStats _playerStats;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _playerStats = FindAnyObjectByType<PlayerStats>();
    }

    private void OnEnable()
    {
        _health.OnDied += OnEnemyDied;
    }

    private void OnDisable()
    {
        _health.OnDied -= OnEnemyDied;
    }

    private void OnEnemyDied()
    {
        bool elite = WeaponEnemyClassifier.CountsAsEliteOrBoss(transform);
        float baseChance = elite ? BaseEliteChance : BaseNormalChance;
        float scavenging = _playerStats != null && _playerStats.GetDefinition(StatType.Scavenging) != null
            ? _playerStats.GetStat(StatType.Scavenging)
            : BaseScavenging;
        float chance = baseChance * (scavenging / BaseScavenging);
        if (Random.value > Mathf.Clamp01(chance))
            return;

        TemporaryPowerupType type = (TemporaryPowerupType)Random.Range(0, 6);
        TemporaryPowerupPool.GetInstance()?.TrySpawn(transform.position, type);
    }
}
