using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class BossKeyDrop : MonoBehaviour
{
    [SerializeField, Tooltip("Prefab de batería/llave (CellBattery). Debe tener KeyPickup + WorldPickup.")]
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

        // Spawnea en la muerte; WorldPickup cae al suelo con PickupGroundFall.
        // No snappeamos con raycast aquí: el fallback ~0 podía golpear al boss (layer Enemy)
        // o dejar la llave flotando si el suelo no está en Terrain/Default.
        Vector3 pos = transform.position + Vector3.up * 0.75f;
        Instantiate(prefab, pos, Quaternion.identity);
    }
}
