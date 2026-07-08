using UnityEngine;

[DisallowMultipleComponent]
public class MaterialPickupReceiver : MonoBehaviour
{
    public static MaterialPickupReceiver Instance { get; private set; }

    [SerializeField] private Transform _pickupPoint;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private PlayerXP _playerXp;

    [SerializeField, Min(0), Tooltip("XP fija otorgada por cada drop recogido, sin importar tipo ni cantidad.")]
    private int _xpPerDrop = 1;

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

        if (_xpPerDrop > 0)
            _playerXp?.AddExperience(_xpPerDrop);
    }
}
