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

    private IPickable _pickable;
    private Vector3 _basePosition;
    private float _bobPhase;
    private bool _pickedUp;

    private void Awake()
    {
        _pickable = GetComponent<IPickable>();
        if (_pickable == null)
            Debug.LogWarning($"[WorldPickup] {name}: no hay componente IPickable en el GameObject.", this);
    }

    private void OnEnable()
    {
        _basePosition = transform.position;
        _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        _pickedUp = false;
    }

    private void OnValidate()
    {
        if (MagnetRadius > 0f && MagnetRadius < PickupRadius)
            MagnetRadius = PickupRadius;
    }

    private void Update()
    {
        if (_pickedUp)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return;

        Vector3 playerPos = player.position;
        float dist = Vector3.Distance(_basePosition, playerPos);
        float pickupRadius = GetEffectivePickupRadius(player);

        if (dist <= pickupRadius)
        {
            _pickedUp = true;
            _pickable?.OnPickedUp();
            return;
        }

        float magnetRadius = Mathf.Max(MagnetRadius, pickupRadius);
        if (magnetRadius > 0f && MagnetSpeed > 0f && dist <= magnetRadius)
            _basePosition = Vector3.MoveTowards(_basePosition, playerPos, MagnetSpeed * Time.deltaTime);

        _bobPhase += BobSpeed * Time.deltaTime;
        transform.position = _basePosition + Vector3.up * (Mathf.Sin(_bobPhase) * BobAmplitude);
    }

    private float GetEffectivePickupRadius(Transform player)
    {
        PlayerStats stats = player != null ? player.GetComponentInParent<PlayerStats>() : null;
        return PlayerStatMath.GetPickupRange(stats, PickupRadius);
    }
}
