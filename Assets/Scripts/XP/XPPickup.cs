using UnityEngine;

/// <summary>
/// Coloca este componente en el jugador. Las bolitas (<see cref="XPDrop"/>) lo localizan para imán y recogida.
/// Acumula la experiencia total recogida y reenvía cada cantidad a <see cref="PlayerXP"/> si existe en el mismo GameObject.
/// </summary>
[DisallowMultipleComponent]
public class XPPickup : MonoBehaviour
{
    public static XPPickup Instance { get; private set; }

    [SerializeField, Tooltip("Punto usado para distancia de recogida e imán (típicamente el mismo transform del jugador o un hijo al centro).")]
    private Transform _pickupPoint;

    [SerializeField, Tooltip("Log each pickup to the console.")]
    private bool _logGrants;

    private int _totalExperience;
    private PlayerXP _playerXp;

    public Transform PickupPointTransform => _pickupPoint != null ? _pickupPoint : transform;

    /// <summary>Posición mundial usada por <see cref="XPDrop"/>.</summary>
    public Vector3 PickupPoint => PickupPointTransform.position;

    public int TotalExperience => _totalExperience;

    public event System.Action<int, int> OnExperienceChanged;

    private void Awake()
    {
        _playerXp = GetComponent<PlayerXP>();
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Llamado por las bolitas al entrar en radio de recogida.</summary>
    public void GrantExperience(int amount)
    {
        if (amount <= 0)
            return;

        _totalExperience += amount;
        if (_logGrants)
            Debug.Log($"XP +{amount} (total {_totalExperience})", this);

        _playerXp?.AddExperience(amount);

        OnExperienceChanged?.Invoke(amount, _totalExperience);
    }
}
