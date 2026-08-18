using UnityEngine;

/// <summary>
/// Misil lanzado por el Destroyer: a diferencia de <see cref="EnemyProjectile"/> (que va recto),
/// gira hacia el jugador cada <see cref="FixedUpdate"/>. Fire-and-forget sin pool: el Destroyer
/// dispara con cadencia baja (cooldown), no en volumen, así que instanciar/destruir es suficiente.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class EnemySeekingMissile : MonoBehaviour
{
    [SerializeField, Min(1)] private int _damage = 12;

    [SerializeField, Min(0.1f), Tooltip("Unidades por segundo.")]
    private float _speed = 10f;

    [SerializeField, Min(0f), Tooltip("Grados por segundo al corregir rumbo hacia el jugador.")]
    private float _turnRateDegPerSec = 90f;

    [SerializeField, Min(0.1f), Tooltip("Segundos de vida antes de autodestruirse.")]
    private float _maxLifetime = 6f;

    private Rigidbody _rigidbody;
    private Vector3 _direction = Vector3.forward;
    private Transform _target;
    private float _elapsed;
    private bool _consumed;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        SphereCollider sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
    }

    /// <summary>Configura y lanza el misil; sigue a <paramref name="target"/> mientras exista.</summary>
    public void Launch(Vector3 worldDirection, Transform target, int damage, float speed, float turnRateDegPerSec)
    {
        _consumed = false;
        _target = target;
        _damage = Mathf.Max(1, damage);
        _speed = Mathf.Max(0.1f, speed);
        _turnRateDegPerSec = Mathf.Max(0f, turnRateDegPerSec);
        _elapsed = 0f;

        _direction = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>Instancia y lanza un misil desde <paramref name="prefab"/> (sin pool).</summary>
    public static void Launch(GameObject prefab, Vector3 position, Vector3 worldDirection, Transform target, int damage, float speed, float turnRateDegPerSec)
    {
        if (prefab == null)
            return;

        Quaternion rotation = worldDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(worldDirection.normalized) : Quaternion.identity;
        GameObject go = Instantiate(prefab, position, rotation);
        if (go.TryGetComponent(out EnemySeekingMissile missile))
            missile.Launch(worldDirection, target, damage, speed, turnRateDegPerSec);
    }

    private void FixedUpdate()
    {
        if (_consumed)
            return;

        if (_target != null)
        {
            Vector3 toTarget = _target.position - _rigidbody.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion currentRotation = Quaternion.LookRotation(_direction);
                Quaternion desiredRotation = Quaternion.LookRotation(toTarget.normalized);
                Quaternion newRotation = Quaternion.RotateTowards(currentRotation, desiredRotation, _turnRateDegPerSec * Time.fixedDeltaTime);
                _direction = newRotation * Vector3.forward;
            }
        }

        transform.rotation = Quaternion.LookRotation(_direction);
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
        Destroy(gameObject);
    }
}
