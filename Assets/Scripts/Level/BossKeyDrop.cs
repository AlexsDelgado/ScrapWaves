using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class BossKeyDrop : MonoBehaviour
{
    [SerializeField, Tooltip("Prefab con KeyPickup. Vacío = Resources/Level/KeyPickup si existe.")]
    private GameObject _keyPickupPrefab;

    private EnemyHealth _health;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
    }

    private void OnEnable()
    {
        _health.OnDied += OnBossDied;
    }

    private void OnDisable()
    {
        _health.OnDied -= OnBossDied;
    }

    private void OnBossDied()
    {
        if (LevelExitObjective.Instance != null && LevelExitObjective.Instance.AllKeysCollected)
            return;

        GameObject prefab = _keyPickupPrefab;
        if (prefab == null)
            return;

        Vector3 pos = transform.position + Vector3.up * 0.75f;
        Instantiate(prefab, pos, Quaternion.identity);
    }
}
