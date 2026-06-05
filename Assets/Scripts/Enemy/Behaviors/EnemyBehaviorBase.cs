using UnityEngine;

/// <summary>
/// Base comun para los comportamientos por tipo de enemigo. Resuelve el target
/// (jugador) sin busquedas por frame y permite activar/desactivar los followers
/// genericos (<see cref="SimpleFollow"/> / <see cref="EnemyFollow"/>) para que el
/// comportamiento pueda tomar control del movimiento durante telegraphs/dash/etc.
/// </summary>
[DisallowMultipleComponent]
public abstract class EnemyBehaviorBase : MonoBehaviour
{
    private Transform _player;
    private SimpleFollow _simpleFollow;
    private EnemyFollow _enemyFollow;
    private bool _simpleFollowInitiallyEnabled;
    private bool _enemyFollowInitiallyEnabled;
    private bool _cachedFollowers;

    /// <summary>Transform del jugador (cacheado; se reintenta si aun no existia).</summary>
    protected Transform Player
    {
        get
        {
            if (_player == null)
                _player = PlayerMovement.PlayerTransform;
            return _player;
        }
    }

    protected virtual void Awake()
    {
        CacheFollowers();
    }

    protected virtual void OnEnable()
    {
        _player = PlayerMovement.PlayerTransform;
    }

    private void CacheFollowers()
    {
        if (_cachedFollowers)
            return;

        _simpleFollow = GetComponent<SimpleFollow>();
        _enemyFollow = GetComponent<EnemyFollow>();
        _simpleFollowInitiallyEnabled = _simpleFollow != null && _simpleFollow.enabled;
        _enemyFollowInitiallyEnabled = _enemyFollow != null && _enemyFollow.enabled;
        _cachedFollowers = true;
    }

    /// <summary>
    /// Activa o desactiva los followers genericos de este enemigo. Al reactivar solo
    /// se vuelven a habilitar los que estaban habilitados originalmente, para no
    /// encender un mover deshabilitado a proposito (p. ej. EnemyFollow con su
    /// CharacterController inactivo).
    /// </summary>
    protected void SetGenericMovement(bool enabled)
    {
        CacheFollowers();
        if (_simpleFollow != null)
            _simpleFollow.enabled = enabled && _simpleFollowInitiallyEnabled;
        if (_enemyFollow != null)
            _enemyFollow.enabled = enabled && _enemyFollowInitiallyEnabled;
    }

    /// <summary>Distancia plana (XZ) al jugador, o float.MaxValue si no hay jugador.</summary>
    protected float PlanarDistanceToPlayer()
    {
        Transform player = Player;
        if (player == null)
            return float.MaxValue;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>Direccion plana (XZ) normalizada hacia el jugador, o Vector3.zero.</summary>
    protected Vector3 PlanarDirectionToPlayer()
    {
        Transform player = Player;
        if (player == null)
            return Vector3.zero;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        return d.normalized;
    }

    protected void FacePlanar(Vector3 planarDirection, float rotationSpeedDegPerSec)
    {
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeedDegPerSec * Time.deltaTime);
    }
}
