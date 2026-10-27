using UnityEngine;

/// <summary>
/// Vigilance Drone. Unidad voladora que NO persigue para golpear: mantiene altura
/// de vuelo, se acerca a distancia de tiro, se queda "lockeando" al jugador
/// un tiempo fijo (telegraph) y dispara un <see cref="EnemyProjectile"/> a la ultima
/// posicion conocida del jugador. Luego entra en cooldown y repite.
///
/// Toma control total del movimiento: desactiva los followers genericos.
/// Approach/standoff usan slot orbital + strafe para no apilarse en el mismo punto.
/// </summary>
public class FlyingRangedBehavior : EnemyBehaviorBase
{
    private enum State { Approaching, Locking, Cooldown }

    [Header("Vuelo")]
    [SerializeField, Min(0f)] private float _hoverHeight = 3.5f;
    [SerializeField, Min(0f), Tooltip("Vertical pursuit speed as a fraction of ordinary movement speed.")]
    private float _verticalFollowSpeedRatio = 1.5f;
    [SerializeField, Min(0f), Tooltip("Height error beyond the individual hover offset required to start following. Once started, settles at that offset.")]
    private float _verticalDeadZone = 5f;
    [SerializeField, Min(0f)] private float _moveSpeed = 5f;
    public float AuthoredMoveSpeed => _moveSpeed;
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

    [Header("Orbit / strafe")]
    [SerializeField, Min(0f)] private float _approachOrbitWeight = 0.28f;
    [SerializeField, Min(0f)] private float _standoffOrbitWeight = 0.65f;
    [SerializeField, Min(0f)] private float _lockMoveSpeedScale = 0.35f;
    [SerializeField, Min(0f)] private float _cooldownMoveSpeedScale = 0.5f;

    private State _state;
    private float _stateTimer;
    private Vector3 _lockedTargetPosition;
    private Rigidbody _rb;
    private EnemyStatRandomizer _statRandomizer;
    private DroneHoverMotion _hoverMotion;
    private float HoverHeight => _hoverHeight
        * (_statRandomizer != null ? _statRandomizer.DroneHoverHeightMultiplier : 1f);
    private float _orbitSign = 1f;

    protected override void Awake()
    {
        base.Awake();
        _statRandomizer = GetComponent<EnemyStatRandomizer>();
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
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    public override void OnPoolSpawn()
    {
        base.OnPoolSpawn();
        _hoverMotion = default;
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    private void OnDisable()
    {
        SetGenericMovement(true);
    }

    private void Update()
    {
        if (EmergenceBlocksGameplay)
            return;

        Transform player = Player;
        if (player == null)
            return;

        if (EnemyVerticalEngagement.IsDisengaged(this))
            return;

        MaintainHover(Time.deltaTime);

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

    private void MaintainHover(float deltaTime)
    {
        Transform player = Player;
        if (player == null)
            return;

        float hoverHeight = HoverHeight;
        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * _hoverRaycastUp;
        // A missed probe must not become a floor at the current altitude: island drops
        // can put the ground outside the probe's range while the drone descends.
        float minimumY = float.NegativeInfinity;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _hoverRaycastUp * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            minimumY = hit.point.y + hoverHeight;

        float verticalSpeed = _moveSpeed * _verticalFollowSpeedRatio
            * (_statRandomizer != null ? _statRandomizer.MovementSpeedMultiplier : 1f)
            * DebugSpeedTool.EnemyScale;
        pos.y = _hoverMotion.Step(pos.y, player.position.y + hoverHeight, minimumY,
            _verticalDeadZone, verticalSpeed, deltaTime);
        transform.position = pos;
    }

    public override Vector3 ResolveEmergenceRestPosition(Vector3 groundPoint)
    {
        Vector3 restPosition = transform.position;
        restPosition.y = groundPoint.y + HoverHeight;
        _hoverMotion.SetHeight(restPosition.y);
        return restPosition;
    }

    private void TickApproaching()
    {
        float dist = PlanarDistanceToPlayer();
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);

        EnemyMovementProfile profile = EnemyMovementProfile.FlyingStandoff;
        float standoff = _approachUntilDistance;

        if (dist > _approachUntilDistance)
        {
            Vector3 slotDir = PlanarDirectionToPoint(PlanarPointAroundPlayer(SteeringSlotAngle, standoff));
            EnemyMovementSteering.RefreshPositionCacheIfNeeded();
            Vector3 weave = EnemyMovementSteering.GetWeaveOffset(
                slotDir,
                Time.time,
                SteeringWeavePhase,
                profile.weaveAmplitude,
                profile.weaveFrequency);
            Vector3 sep = EnemyMovementSteering.SampleSeparation(
                transform.position,
                profile.separationRadius,
                profile.maxSeparationSamples,
                GetInstanceID());
            Vector3 tangent = Vector3.Cross(Vector3.up, PlanarDirectionToPlayer());
            if (tangent.sqrMagnitude > 0.0001f)
                tangent.Normalize();
            Vector3 move = slotDir + weave + sep * profile.separationWeight + tangent * (_orbitSign * _approachOrbitWeight);
            MovePlanar(move);
            return;
        }

        if (dist < _retreatBelowDistance)
        {
            MovePlanar(ComposeOrbitStandoffDirection(
                standoff,
                _orbitSign,
                _standoffOrbitWeight,
                profile.weaveAmplitude,
                profile.weaveFrequency,
                profile.separationWeight,
                profile.separationRadius,
                profile.maxSeparationSamples));
            return;
        }

        _state = State.Locking;
        _stateTimer = _lockSeconds;
    }

    private void TickLocking()
    {
        Vector3 dir = PlanarDirectionToPlayer();
        FacePlanar(dir, _rotationSpeed * 1.5f);

        EnemyMovementProfile profile = EnemyMovementProfile.FlyingStandoff;
        Vector3 move = ComposeOrbitStandoffDirection(
            _approachUntilDistance,
            _orbitSign,
            _standoffOrbitWeight,
            profile.weaveAmplitude,
            profile.weaveFrequency,
            profile.separationWeight,
            profile.separationRadius,
            profile.maxSeparationSamples);
        MovePlanar(move, _lockMoveSpeedScale);

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
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);

        EnemyMovementProfile profile = EnemyMovementProfile.FlyingStandoff;
        Vector3 move = ComposeOrbitStandoffDirection(
            _approachUntilDistance,
            _orbitSign,
            _standoffOrbitWeight * 0.85f,
            profile.weaveAmplitude,
            profile.weaveFrequency,
            profile.separationWeight,
            profile.separationRadius,
            profile.maxSeparationSamples);
        MovePlanar(move, _cooldownMoveSpeedScale);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
            _state = State.Approaching;
    }

    private void MovePlanar(Vector3 planarDir, float speedScale = 1f)
    {
        planarDir.y = 0f;
        if (planarDir.sqrMagnitude < 0.0001f || speedScale <= 0f)
            return;
        float speed = _moveSpeed
            * speedScale
            * (_statRandomizer != null ? _statRandomizer.MovementSpeedMultiplier : 1f)
            * DebugSpeedTool.EnemyScale;
        transform.position += planarDir.normalized * (speed * Time.deltaTime);
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

        EnemyProjectilePool.TryLaunch(
            _bulletPrefab,
            muzzle,
            Quaternion.LookRotation(dir.normalized),
            dir,
            EnemyOutgoingDamageScale.ScaleFrom(this, _bulletDamage),
            _bulletSpeed);
    }
}
