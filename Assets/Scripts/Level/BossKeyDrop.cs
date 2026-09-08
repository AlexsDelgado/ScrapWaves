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

        Vector3 preferred = transform.position + Vector3.up * 0.75f;
        Vector3 pos = ResolveGroundDropPosition(preferred);
        Instantiate(prefab, pos, Quaternion.identity);
    }

    private static Vector3 ResolveGroundDropPosition(Vector3 preferred)
    {
        int mask = LayerMask.GetMask("Terrain", "Default");
        if (Physics.Raycast(preferred + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 40f, mask,
                QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * 0.35f;

        if (Physics.Raycast(preferred + Vector3.up * 20f, Vector3.down, out hit, 80f, ~0,
                QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * 0.35f;

        return preferred;
    }
}
