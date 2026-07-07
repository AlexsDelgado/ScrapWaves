using UnityEngine;

[DisallowMultipleComponent]
public class MaterialPickupReceiver : MonoBehaviour
{
    public static MaterialPickupReceiver Instance { get; private set; }

    [SerializeField] private Transform _pickupPoint;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private PlayerXP _playerXp;

    private PlayerStats _stats;

    private void Awake()
    {
        if (_inventory == null)
            _inventory = GetComponent<MaterialInventory>();
        if (_playerXp == null)
            _playerXp = GetComponent<PlayerXP>();
        _stats = GetComponent<PlayerStats>();
    }

    private void OnEnable() => Instance = this;
    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    public Vector3 PickupPoint => (_pickupPoint != null ? _pickupPoint : transform).position;

    public void GrantMaterial(MaterialType type, int amount)
    {
        if (amount <= 0)
            return;

        _inventory?.Add(type, amount);

        int xp = MaterialCatalog.GetPickupXpValue(type);
        float scavenging = _stats != null ? _stats.GetStat(StatType.Scavenging) : 0f;
        int xpAmount = Mathf.Max(1, Mathf.RoundToInt(xp * amount * (1f + scavenging)));
        _playerXp?.AddExperience(xpAmount);
    }
}
