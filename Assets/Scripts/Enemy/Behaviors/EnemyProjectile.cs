using UnityEngine;

/// <summary>
/// Proyectil disparado por enemigos (Vigilance/Bomber Drone). A diferencia del
/// <c>Projectile</c> del arma del jugador (que ignora al player a proposito),
/// este SI daña al jugador via <see cref="PlayerHealth.TakeDamage"/>.
///
/// Movimiento kinematic en <see cref="FixedUpdate"/>. Se autodestruye al impactar
/// al jugador, al chocar con el terreno o al expirar su vida. Pooling = TODO futuro.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField, Min(1)] private int _damage = 8;

    [SerializeField, Min(0.1f), Tooltip("Unidades por segundo.")]
    private float _speed = 16f;

    [SerializeField, Min(0.1f), Tooltip("Segundos de vida antes de autodestruirse.")]
    private float _maxLifetime = 5f;

    private Rigidbody _rigidbody;
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

        SphereCollider sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
    }

    /// <summary>Configura y lanza el proyectil en una direccion mundial.</summary>
    public void Launch(Vector3 worldDirection, int damage, float speed)
    {
        _consumed = false;
        if (worldDirection.sqrMagnitude > 0.0001f)
            _direction = worldDirection.normalized;

        _damage = Mathf.Max(1, damage);
        _speed = Mathf.Max(0.1f, speed);
        _elapsed = 0f;

        if (_direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_direction);
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

        _rigidbody.MovePosition(_rigidbody.position + _direction * (_speed * Time.fixedDeltaTime));
    }

    private void Update()
    {
        if (_consumed)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _maxLifetime)
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
