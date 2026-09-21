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

    [SerializeField, Min(0f), Tooltip("Offset vertical sobre el suelo al aterrizar.")]
    private float _groundOffset = 0.35f;

    [SerializeField, Tooltip("Layer(s) contra las que cae el pickup. Vacío = Terrain + Default.")]
    private LayerMask _groundMask;

    [SerializeField, Tooltip("Si true, cae por gravedad hasta el suelo al spawnear (bosses/enemigos en el aire).")]
    private bool _fallToGround = true;

    private IPickable _pickable;
    private Vector3 _basePosition;
    private float _bobPhase;
    private bool _pickedUp;
    private bool _isFalling;
    private float _fallVelocity;

    private void Awake()
    {
        TryResolvePickable();
        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    private void OnEnable()
    {
        TryResolvePickable();
        _basePosition = transform.position;
        _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        _pickedUp = false;
        _isFalling = _fallToGround;
        _fallVelocity = 0f;
    }

    /// <summary>Recogida al acercarse, sin imán ni PickupRange de stats (escenas de prueba).</summary>
    public void ConfigureForManualCollection(float pickupRadius = 1f)
    {
        PickupRadius = Mathf.Max(0.01f, pickupRadius);
        MagnetRadius = 0f;
        MagnetSpeed = 0f;
        _usePlayerPickupRange = false;
        _fallToGround = false;
        _isFalling = false;
        _basePosition = transform.position;
    }

    /// <summary>Recogida de gameplay: radio base + imán + PickupRange del jugador.</summary>
    public void ConfigureForGameplayCollection(float pickupRadius = 1.5f, float magnetRadius = 6f, float magnetSpeed = 12f)
    {
        PickupRadius = Mathf.Max(0.01f, pickupRadius);
        MagnetRadius = Mathf.Max(0f, magnetRadius);
        MagnetSpeed = Mathf.Max(0f, magnetSpeed);
        _usePlayerPickupRange = true;
        _fallToGround = true;
        if (isActiveAndEnabled)
        {
            _isFalling = true;
            _fallVelocity = 0f;
            _basePosition = transform.position;
        }
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

        if (_isFalling)
        {
            Vector3 fallPos = transform.position;
            if (PickupGroundFall.Tick(ref fallPos, ref _fallVelocity, Time.deltaTime, _groundOffset, _groundMask))
            {
                _isFalling = false;
                _basePosition = fallPos;
            }

            transform.position = fallPos;
            return;
        }

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
