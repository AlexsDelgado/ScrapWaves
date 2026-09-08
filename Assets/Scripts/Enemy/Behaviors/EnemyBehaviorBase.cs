using UnityEngine;

/// <summary>
/// Base comun para los comportamientos por tipo de enemigo. Resuelve el target
/// (jugador) sin busquedas por frame y permite activar/desactivar los followers
/// genericos (<see cref="SimpleFollow"/> / <see cref="EnemyFollow"/>) para que el
/// comportamiento pueda tomar control del movimiento durante telegraphs/dash/etc.
/// </summary>
[DisallowMultipleComponent]
public abstract class EnemyBehaviorBase : MonoBehaviour, IEnemySpawnLifecycle
{
    private Transform _player;
    private SimpleFollow _simpleFollow;
    private EnemyFollow _enemyFollow;
    private bool _simpleFollowInitiallyEnabled;
    private bool _enemyFollowInitiallyEnabled;
    private bool _cachedFollowers;

    protected float SteeringSlotAngle { get; private set; }
    protected float SteeringWeavePhase { get; private set; }

    /// <summary>Si es true, tras el spawn se aplica <see cref="ApproachMovementProfile"/> a los followers.</summary>
    protected virtual bool HasApproachMovementProfile => false;

    /// <summary>Perfil usado en fases de persecución con follower genérico.</summary>
    protected virtual EnemyMovementProfile ApproachMovementProfile => EnemyMovementProfile.MeleeSwarm;

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
        RandomizeSteeringIdentity();
    }

    protected virtual void OnEnable()
    {
        _player = PlayerMovement.PlayerTransform;
    }

    public virtual void OnPoolSpawn()
    {
        RandomizeSteeringIdentity();
        ApplyApproachMovementProfile();
    }

    public virtual void OnPoolDespawn()
    {
    }

    protected void RandomizeSteeringIdentity()
    {
        EnemyMovementSteering.RandomizeIdentity(out float slot, out float phase);
        SteeringSlotAngle = slot;
        SteeringWeavePhase = phase;
    }

    protected void ApplyApproachMovementProfile()
    {
        if (!HasApproachMovementProfile)
            return;
        ApplyFollowerMovementProfile(ApproachMovementProfile);
    }

    protected void ApplyFollowerMovementProfile(EnemyMovementProfile profile)
    {
        CacheFollowers();
        if (_simpleFollow != null)
            _simpleFollow.ConfigureMovementProfile(profile);
        if (_enemyFollow != null)
            _enemyFollow.ConfigureMovementProfile(profile);
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

        if (enabled)
            ApplyApproachMovementProfile();
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

    /// <summary>Punto en anillo alrededor del jugador (plano XZ).</summary>
    protected Vector3 PlanarPointAroundPlayer(float angleDeg, float radius)
    {
        Transform player = Player;
        if (player == null)
            return transform.position;
        return EnemyMovementSteering.GetPursuitPoint(player.position, angleDeg, radius);
    }

    /// <summary>Dirección plana hacia un punto del mundo.</summary>
    protected Vector3 PlanarDirectionToPoint(Vector3 worldPoint)
    {
        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        return d.normalized;
    }

    /// <summary>
    /// Dirección de standoff: acerarse/alejarse del radio + strafe orbital + weave + separación ligera.
    /// </summary>
    protected Vector3 ComposeOrbitStandoffDirection(
        float standoffRadius,
        float orbitSign,
        float orbitWeight,
        float weaveAmplitude,
        float weaveFrequency,
        float separationWeight,
        float separationRadius,
        int maxSeparationSamples)
    {
        Transform player = Player;
        if (player == null)
            return Vector3.zero;

        EnemyMovementSteering.RefreshPositionCacheIfNeeded();

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        Vector3 radial = dist > 0.0001f ? toPlayer / dist : Vector3.forward;

        Vector3 radialCorrection = Vector3.zero;
        float slack = 0.6f;
        if (dist > standoffRadius + slack)
            radialCorrection = radial;
        else if (dist < standoffRadius - slack)
            radialCorrection = -radial;

        Vector3 tangent = Vector3.Cross(Vector3.up, radial);
        if (tangent.sqrMagnitude > 0.0001f)
            tangent.Normalize();
        else
            tangent = Vector3.right;

        Vector3 weave = EnemyMovementSteering.GetWeaveOffset(
            radial,
            Time.time,
            SteeringWeavePhase,
            weaveAmplitude,
            weaveFrequency);

        Vector3 sep = EnemyMovementSteering.SampleSeparation(
            transform.position,
            separationRadius,
            maxSeparationSamples,
            GetInstanceID());

        Vector3 move = radialCorrection
            + tangent * (orbitSign * orbitWeight)
            + weave
            + sep * separationWeight;
        move.y = 0f;
        if (move.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        return move.normalized;
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
