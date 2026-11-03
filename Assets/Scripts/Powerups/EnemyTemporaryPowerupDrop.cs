using UnityEngine;

/// <summary>
/// Al morir el enemigo, roll Spec de Temporary Power-up (por defecto 1% normal / 5% elite-boss, escala con Scavenging; valores en PowerupBalanceProfile).
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyTemporaryPowerupDrop : MonoBehaviour
{
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
        PowerupBalanceProfile balance = PowerupBalanceProfile.Current;
        bool elite = WeaponEnemyClassifier.CountsAsEliteOrBoss(transform);
        float scavenging = _playerStats != null && _playerStats.GetDefinition(StatType.Scavenging) != null
            ? _playerStats.GetStat(StatType.Scavenging)
            : balance.ReferenceScavenging;
        if (Random.value > balance.DropChance(elite, scavenging))
            return;

        TemporaryPowerupType type = balance.RollDropType();
        TemporaryPowerupPool.GetInstance()?.TrySpawn(transform.position, type);
    }
}
