using UnityEngine;

[DisallowMultipleComponent]
public class MaterialPickupReceiver : MonoBehaviour
{
    public static MaterialPickupReceiver Instance { get; private set; }

    [SerializeField] private Transform _pickupPoint;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private PlayerXP _playerXp;

    [SerializeField, Min(0), Tooltip("Si > 0, XP fija por pickup (ignora rareza). 0 = usar MaterialCatalog por tipo.")]
    private int _xpPerDropOverride;

    private void Awake()
    {
        if (_inventory == null)
            _inventory = GetComponent<MaterialInventory>();
        if (_playerXp == null)
            _playerXp = GetComponent<PlayerXP>();
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

        int xp = _xpPerDropOverride > 0
            ? _xpPerDropOverride
            : MaterialCatalog.GetPickupXpValue(type) * Mathf.Max(1, amount);
        if (xp > 0)
            _playerXp?.AddExperience(xp);
    }
}
