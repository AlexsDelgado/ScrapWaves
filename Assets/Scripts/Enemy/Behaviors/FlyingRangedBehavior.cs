using UnityEngine;

/// <summary>
/// Vigilance Drone. Unidad voladora que NO persigue para golpear: mantiene altura
/// de vuelo, se acerca a distancia de tiro, se queda quieta "lockeando" al jugador
/// un tiempo fijo (telegraph) y dispara un <see cref="EnemyProjectile"/> a la ultima
/// posicion conocida del jugador. Luego entra en cooldown y repite.
///
/// Toma control total del movimiento: desactiva los followers genericos.
/// </summary>
public class FlyingRangedBehavior : EnemyBehaviorBase
{
    private enum State { Approaching, Locking, Cooldown }

    [Header("Vuelo")]
    [SerializeField, Min(0f)] private float _hoverHeight = 3.5f;
    [SerializeField, Min(0f)] private float _moveSpeed = 5f;
    [SerializeField, Min(0f)] private float _rotationSpeed = 360f;
    [SerializeField, Tooltip("Capas de suelo para mantener la altura de vuelo.")]
    private LayerMask _groundMask;
    [SerializeField, Min(1f)] private float _hoverRaycastUp = 40f;

    [Header("Distancias")]
    [SerializeField, Min(0.5f), Tooltip("Empieza a lockear cuando el jugador esta a esta distancia o menos.")]
    private float _approachUntilDistance = 9f;
    [SerializeField, Min(0.5f), Tooltip("Si el jugador queda mas cerca que esto, retrocede.")]
    private float _retreatBelowDistance = 4f;

    [Header("Disparo")]
    [SerializeField] private GameObject _bulletPrefab;
    [SerializeField, Min(0f), Tooltip("Lock in state: tiempo apuntando antes de disparar.")]
    private float _lockSeconds = 3f;
    [SerializeField, Min(0f)] private float _cooldownSeconds = 1.5f;
    [SerializeField, Min(1)] private int _bulletDamage = 8;
    [SerializeField, Min(0.1f)] private float _bulletSpeed = 16f;
    [SerializeField, Tooltip("Altura del cañon respecto al pivote del dron.")]
    private float _muzzleHeight = 0.2f;

    private State _state;
    private float _stateTimer;
    private Vector3 _lockedTargetPosition;
    private Rigidbody _rb;

    protected override void Awake()
    {
        base.Awake();
        _rb = GetComponent<Rigidbody>();
        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetGenericMovement(false);
        if (_rb != null)
            _rb.isKinematic = true;
        _state = State.Approaching;
        _stateTimer = 0f;
    }

    private void OnDisable()
    {
        SetGenericMovement(true);
    }

    private void Update()
    {
        Transform player = Player;
        if (player == null)
            return;

        MaintainHover();

        switch (_state)
        {
            case State.Approaching:
                TickApproaching();
                break;
            case State.Locking:
                TickLocking();
                break;
            case State.Cooldown:
                TickCooldown();
                break;
        }
    }

    private void MaintainHover()
    {
        Vector3 origin = transform.position + Vector3.up * _hoverRaycastUp;
        float groundY = transform.position.y - _hoverHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _hoverRaycastUp * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        float targetY = groundY + _hoverHeight;
        Vector3 pos = transform.position;
        pos.y = Mathf.Lerp(pos.y, targetY, 1f - Mathf.Exp(-6f * Time.deltaTime));
        transform.position = pos;
    }

    private void TickApproaching()
    {
        float dist = PlanarDistanceToPlayer();
        Vector3 dir = PlanarDirectionToPlayer();
        FacePlanar(dir, _rotationSpeed);

        if (dist > _approachUntilDistance)
            MovePlanar(dir);
        else if (dist < _retreatBelowDistance)
            MovePlanar(-dir);
        else
        {
            _state = State.Locking;
            _stateTimer = _lockSeconds;
        }
    }

    private void TickLocking()
    {
        Vector3 dir = PlanarDirectionToPlayer();
        FacePlanar(dir, _rotationSpeed * 1.5f);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer > 0f)
            return;

        Transform player = Player;
        _lockedTargetPosition = player != null ? player.position : transform.position + transform.forward;
        Fire();

        _state = State.Cooldown;
        _stateTimer = _cooldownSeconds;
    }

    private void TickCooldown()
    {
        Vector3 dir = PlanarDirectionToPlayer();
        FacePlanar(dir, _rotationSpeed);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
            _state = State.Approaching;
    }

    private void MovePlanar(Vector3 planarDir)
    {
        planarDir.y = 0f;
        if (planarDir.sqrMagnitude < 0.0001f)
            return;
        transform.position += planarDir.normalized * (_moveSpeed * Time.deltaTime);
    }

    private void Fire()
    {
        if (_bulletPrefab == null)
        {
            Debug.LogWarning("[FlyingRangedBehavior] _bulletPrefab is not assigned.", this);
            return;
        }

        Vector3 muzzle = transform.position + Vector3.up * _muzzleHeight;
        Vector3 dir = _lockedTargetPosition - muzzle;
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;

        EnemyProjectilePool.TryLaunch(_bulletPrefab, muzzle, Quaternion.LookRotation(dir.normalized), dir, _bulletDamage, _bulletSpeed);
    }
}
