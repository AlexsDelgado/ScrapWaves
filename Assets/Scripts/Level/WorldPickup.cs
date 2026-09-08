using UnityEngine;

/// <summary>
/// Pickup genérico en el mundo: bobbing, imán y recogida por proximidad al jugador.
/// El GameObject debe tener un componente que implemente <see cref="IPickable"/>;
/// ese componente recibe la llamada <see cref="IPickable.OnPickedUp"/> y puede destruir
/// o devolver el objeto al pool según su lógica.
/// </summary>
[DisallowMultipleComponent]
public class WorldPickup : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Radio de recogida automática.")]
    public float PickupRadius = 1.5f;

    [SerializeField, Min(0f), Tooltip("Radio a partir del cual el item se mueve hacia el jugador. 0 = sin imán.")]
    public float MagnetRadius = 5f;

    [SerializeField, Min(0f), Tooltip("Velocidad del imán.")]
    public float MagnetSpeed = 10f;

    [SerializeField, Min(0f), Tooltip("Amplitud del bobbing vertical.")]
    public float BobAmplitude = 0.15f;

    [SerializeField, Min(0f), Tooltip("Velocidad del bobbing.")]
    public float BobSpeed = 3f;

    [SerializeField, Tooltip("Si false, ignora PickupRange del jugador y usa solo PickupRadius.")]
    private bool _usePlayerPickupRange = true;

    private IPickable _pickable;
    private Vector3 _basePosition;
    private float _bobPhase;
    private bool _pickedUp;

    private void Awake()
    {
        TryResolvePickable();
    }

    private void OnEnable()
    {
        TryResolvePickable();
        _basePosition = transform.position;
        _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        _pickedUp = false;
    }

    /// <summary>Recogida al acercarse, sin imán ni PickupRange de stats (escenas de prueba).</summary>
    public void ConfigureForManualCollection(float pickupRadius = 1f)
    {
        PickupRadius = Mathf.Max(0.01f, pickupRadius);
        MagnetRadius = 0f;
        MagnetSpeed = 0f;
        _usePlayerPickupRange = false;
    }

    /// <summary>Recogida de gameplay: radio base + imán + PickupRange del jugador.</summary>
    public void ConfigureForGameplayCollection(float pickupRadius = 1.5f, float magnetRadius = 6f, float magnetSpeed = 12f)
    {
        PickupRadius = Mathf.Max(0.01f, pickupRadius);
        MagnetRadius = Mathf.Max(0f, magnetRadius);
        MagnetSpeed = Mathf.Max(0f, magnetSpeed);
        _usePlayerPickupRange = true;
    }

    private void OnValidate()
    {
        if (_usePlayerPickupRange && MagnetRadius > 0f && MagnetRadius < PickupRadius)
            MagnetRadius = PickupRadius;
    }

    private void Update()
    {
        if (_pickedUp)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return;

        if (!TryResolvePickable())
            return;

        Vector3 playerPos = player.position;
        float dist = Vector3.Distance(_basePosition, playerPos);
        float pickupRadius = GetEffectivePickupRadius(player);

        if (dist <= pickupRadius)
        {
            _pickedUp = true;
            _pickable.OnPickedUp();
            return;
        }

        float magnetRadius = Mathf.Max(MagnetRadius, pickupRadius);
        if (magnetRadius > 0f && MagnetSpeed > 0f && dist <= magnetRadius)
            _basePosition = Vector3.MoveTowards(_basePosition, playerPos, MagnetSpeed * Time.deltaTime);

        _bobPhase += BobSpeed * Time.deltaTime;
        transform.position = _basePosition + Vector3.up * (Mathf.Sin(_bobPhase) * BobAmplitude);
    }

    private bool TryResolvePickable()
    {
        if (_pickable != null)
            return true;

        _pickable = GetComponent<IPickable>();
        return _pickable != null;
    }

    private float GetEffectivePickupRadius(Transform player)
    {
        if (!_usePlayerPickupRange)
            return PickupRadius;

        PlayerStats stats = player != null ? player.GetComponentInParent<PlayerStats>() : null;
        return PlayerStatMath.GetPickupRange(stats, PickupRadius);
    }
}
