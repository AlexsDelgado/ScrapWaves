using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyMaterialDrop : MonoBehaviour
{
    [SerializeField] private MaterialDropConfig _dropConfig;
    [SerializeField] private MaterialPool _poolOverride;
    [SerializeField, Min(1)] private int _dropAmount = 1;

    private EnemyHealth _health;
    private PlayerStats _playerStats;
    private static bool _warnedMissingPool;

    private void Awake() => _health = GetComponent<EnemyHealth>();

    private void OnEnable() => _health.OnDied += OnEnemyDied;
    private void OnDisable() => _health.OnDied -= OnEnemyDied;

    private void OnEnemyDied()
    {
        if (_dropConfig == null || _dropAmount <= 0)
            return;

        if (_playerStats == null)
            _playerStats = FindAnyObjectByType<PlayerStats>();

        float chance = _dropConfig.DropChance;
        if (_playerStats != null)
            chance = Mathf.Clamp01(chance * (1f + _playerStats.GetStat(StatType.Scavenging)));

        if (Random.value > chance)
            return;

        if (!_dropConfig.TryRoll(out MaterialType material))
            return;

        MaterialPool pool = _poolOverride != null ? _poolOverride : MaterialPool.GetInstance();
        if (pool == null)
        {
            if (!_warnedMissingPool)
            {
                _warnedMissingPool = true;
                Debug.LogWarning("EnemyMaterialDrop: no MaterialPool in scene.", this);
            }

            return;
        }

        int amount = _dropAmount;
        if (ShouldDoubleDrop())
            amount *= 2;

        pool.TrySpawn(transform.position, material, amount);
    }

    private bool ShouldDoubleDrop()
    {
        if (_playerStats == null)
            return false;
        return Random.value < Mathf.Clamp01(_playerStats.GetStat(StatType.DoubleDrop));
    }
}
