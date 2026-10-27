using UnityEngine;

/// <summary>
/// Proyectil disparado por enemigos (Vigilance/Bomber Drone). A diferencia del
/// <c>Projectile</c> del arma del jugador (que ignora al player a proposito),
/// este SI daña al jugador via <see cref="PlayerHealth.TakeDamage"/>.
///
/// Movimiento kinematic en <see cref="FixedUpdate"/>. Se autodestruye al impactar
/// al jugador, al chocar con el terreno o al expirar su vida. Retorna al pool si esta vinculado.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField, Min(1)] private int _damage = 8;

    [SerializeField, Min(0f), Tooltip("Knockback pequeño al impactar al jugador.")]
    private float _hitPushForce = 2.5f;

    [SerializeField, Min(0.1f), Tooltip("Unidades por segundo.")]
    private float _speed = 16f;

    [SerializeField, Min(0.1f), Tooltip("Segundos de vida antes de autodestruirse.")]
    private float _maxLifetime = 5f;

    private Rigidbody _rigidbody;
    private CapsuleCollider _capsule;
    private readonly RaycastHit[] _sweepHits = new RaycastHit[32];
    private readonly Collider[] _overlapHits = new Collider[32];
    private int _collisionMask;
    private bool _checkInitialOverlap;
    private Vector3 _direction = Vector3.forward;
    private float _elapsed;
    private bool _consumed;
    private EnemyProjectilePool _pool;
    private GameObject _sourcePrefab;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        _capsule = GetComponent<CapsuleCollider>();
        _capsule.isTrigger = true;
    }

    /// <summary>Configura y lanza el proyectil en una direccion mundial.</summary>
    public void Launch(Vector3 worldDirection, int damage, float speed)
    {
        _consumed = false;
        _checkInitialOverlap = true;
        _collisionMask = 0;
        for (int layer = 0; layer < 32; layer++)
            if (!Physics.GetIgnoreLayerCollision(gameObject.layer, layer))
                _collisionMask |= 1 << layer;
        if (worldDirection.sqrMagnitude > 0.0001f)
            _direction = worldDirection.normalized;

        _damage = Mathf.Max(1, damage);
        _speed = Mathf.Max(0.1f, speed);
        _elapsed = 0f;

        if (_direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_direction);

        // Reuse can teleport an interpolated body; the next sweep must start at the new physics pose.
        _rigidbody.position = transform.position;
        _rigidbody.rotation = transform.rotation;
    }

    public void BindPool(EnemyProjectilePool pool, GameObject sourcePrefab)
    {
        _pool = pool;
        _sourcePrefab = sourcePrefab;
    }

    private void FixedUpdate()
    {
        if (_consumed)
            return;

        Vector3 displacement = _direction * (_speed * DebugSpeedTool.ProjectileScale * Time.fixedDeltaTime);
        if (!TryConsumeSweptCollision(displacement))
            _rigidbody.MovePosition(_rigidbody.position + displacement);
    }

    // A kinematic trigger can pass completely through a thin target between physics ticks.
    // Sweep the authored capsule, including its long nose, before moving the pooled bolt.
    private bool TryConsumeSweptCollision(Vector3 displacement)
    {
        if (!_capsule.enabled)
            return false;

        Vector3 scale = transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        int axisIndex = _capsule.direction;
        Vector3 localAxis = axisIndex == 0 ? Vector3.right : axisIndex == 1 ? Vector3.up : Vector3.forward;
        float radiusScale = axisIndex == 0 ? Mathf.Max(scale.y, scale.z)
            : axisIndex == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
        float radius = _capsule.radius * radiusScale;
        float halfSegment = Mathf.Max(0f, _capsule.height * scale[axisIndex] * 0.5f - radius);
        Vector3 offset = (_rigidbody.rotation * localAxis) * halfSegment;
        Vector3 center = _rigidbody.position + _rigidbody.rotation * Vector3.Scale(_capsule.center, transform.lossyScale);
        Vector3 point1 = center - offset;
        Vector3 point2 = center + offset;

        if (_checkInitialOverlap)
        {
            _checkInitialOverlap = false;
            int count = Physics.OverlapCapsuleNonAlloc(point1, point2, radius, _overlapHits,
                _collisionMask, QueryTriggerInteraction.Collide);
            Collider[] overlaps = _overlapHits;
            // NonAlloc results are unordered. A full buffer must not hide a valid target.
            if (count == overlaps.Length)
            {
                overlaps = Physics.OverlapCapsule(point1, point2, radius, _collisionMask, QueryTriggerInteraction.Collide);
                count = overlaps.Length;
            }
            Collider nearestOverlap = null;
            float nearestOverlapDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (IsCollisionTarget(overlaps[i]))
                {
                    float gap = (overlaps[i].ClosestPoint(center) - center).sqrMagnitude;
                    if (gap < nearestOverlapDistance)
                    {
                        nearestOverlap = overlaps[i];
                        nearestOverlapDistance = gap;
                    }
                }
            if (nearestOverlap != null)
            {
                OnTriggerEnter(nearestOverlap);
                return _consumed;
            }
        }

        float distance = displacement.magnitude;
        if (distance <= 0.00001f)
            return false;

        Vector3 direction = displacement / distance;
        int hitCount = Physics.CapsuleCastNonAlloc(point1, point2, radius, direction, _sweepHits,
            distance, _collisionMask, QueryTriggerInteraction.Collide);
        RaycastHit[] hits = _sweepHits;
        if (hitCount == hits.Length)
        {
            hits = Physics.CapsuleCastAll(point1, point2, radius, direction, distance,
                _collisionMask, QueryTriggerInteraction.Collide);
            hitCount = hits.Length;
        }

        int nearest = -1;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
            if (hits[i].distance < nearestDistance && IsCollisionTarget(hits[i].collider))
            {
                nearest = i;
                nearestDistance = hits[i].distance;
            }
        if (nearest < 0)
            return false;

        _rigidbody.position += direction * nearestDistance;
        OnTriggerEnter(hits[nearest].collider);
        return _consumed;
    }

    private bool IsCollisionTarget(Collider other)
    {
        if (other == null || other == _capsule || other.attachedRigidbody == _rigidbody ||
            Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) ||
            Physics.GetIgnoreCollision(_capsule, other))
            return false;
        return other.GetComponentInParent<PlayerHealth>() != null ||
               other.gameObject.layer == LayerMask.NameToLayer("Terrain");
    }

    private void Update()
    {
        if (_consumed)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed * Mathf.Max(0.05f, DebugSpeedTool.ProjectileScale) >= _maxLifetime)
            Consume();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed)
            return;

        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player != null)
        {
            player.TakeDamage(_damage);
            if (_hitPushForce > 0f)
                PlayerCombatHooks.TryPush(transform.position, _hitPushForce);
            Consume();
            return;
        }

        int terrainLayer = LayerMask.NameToLayer("Terrain");
        if (terrainLayer >= 0 && other.gameObject.layer == terrainLayer)
            Consume();
    }

    private void Consume()
    {
        if (_consumed)
            return;

        _consumed = true;
        if (_pool != null && _sourcePrefab != null)
            _pool.Release(gameObject, _sourcePrefab);
        else
        {
            EnemyPoolProfiler.RegisterDestroy();
            Destroy(gameObject);
        }
    }
}
