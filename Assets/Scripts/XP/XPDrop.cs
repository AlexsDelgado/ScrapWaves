using UnityEngine;

/// <summary>
/// Lógica de una bolita de XP en el mundo: imán opcional y recogida por proximidad al jugador (<see cref="XPPickup"/>).
/// </summary>
public class XPDrop : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("Valor por defecto si el pool no pasa cantidad (no debería ocurrir en uso normal).")]
    private int _defaultExperience = 1;

    [SerializeField, Min(0.01f), Tooltip("Distancia al jugador para consumir la bolita.")]
    private float _pickupRadius = 0.75f;

    [SerializeField, Min(0f), Tooltip("Si el jugador está dentro de este radio, la bolita se mueve hacia él. 0 = sin imán.")]
    private float _magnetRadius = 7f;

    [SerializeField, Min(0f), Tooltip("Velocidad de movimiento cuando el imán está activo.")]
    private float _magnetSpeed = 14f;

    [SerializeField, Min(0f), Tooltip("Offset vertical sobre el suelo al aterrizar (evita enterrar el modelo).")]
    private float _spawnHeightOffset = 0.5f;

    [SerializeField, Tooltip("Layer(s) contra las que cae el drop. Vacío = Terrain + Default.")]
    private LayerMask _groundMask;

    private int _experience;
    private XPPool _pool;
    private XPPoolMember _member;

    private bool _isFalling;
    private float _fallVelocity;

    private void Awake()
    {
        _member = GetComponent<XPPoolMember>();

        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    private void OnValidate()
    {
        if (_magnetRadius > 0f && _magnetRadius < _pickupRadius)
            _magnetRadius = _pickupRadius;
    }

    /// <summary>Llamado por <see cref="XPPool"/> al sacar la instancia del pool.</summary>
    public void ActivateFromPool(XPPool pool, int experienceAmount)
    {
        _pool = pool;
        _experience = experienceAmount > 0 ? experienceAmount : _defaultExperience;

        // El enemigo puede haber muerto en el aire (p. ej. voladores): cae por gravedad simple
        // hasta tocar el suelo en vez de quedar flotando en el punto exacto de la muerte.
        _isFalling = true;
        _fallVelocity = 0f;
    }

    private void Update()
    {
        if (_isFalling)
        {
            Vector3 fallPos = transform.position;
            if (PickupGroundFall.Tick(ref fallPos, ref _fallVelocity, Time.deltaTime, _spawnHeightOffset, _groundMask))
                _isFalling = false;
            transform.position = fallPos;
            return;
        }

        XPPickup pickup = XPPickup.Instance;
        if (pickup == null)
            return;

        Vector3 target = pickup.PickupPoint;
        float dist = Vector3.Distance(transform.position, target);
        float pickupRadius = Mathf.Max(_pickupRadius, pickup.PickupRadius);

        if (dist <= pickupRadius)
        {
            pickup.GrantExperience(_experience);
            if (_member != null)
                _member.Despawn();
            else if (_pool != null)
                _pool.Release(gameObject);
            return;
        }

        float magnetRadius = Mathf.Max(_magnetRadius, pickupRadius);
        if (magnetRadius <= 0f || _magnetSpeed <= 0f)
            return;

        if (dist <= magnetRadius && dist > pickupRadius)
            transform.position = Vector3.MoveTowards(transform.position, target, _magnetSpeed * Time.deltaTime);
    }
}
