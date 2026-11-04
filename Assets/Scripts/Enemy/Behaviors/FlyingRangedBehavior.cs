using UnityEngine;
using UnityEngine.Serialization;

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
    [FormerlySerializedAs("_verticalDeadZone")]
    [SerializeField, Min(0f), Tooltip("Height above the current flight plane that the desired hover altitude must exceed before ascending. Once started, follows until settled.")]
    private float _upwardDeadZone = 5f;
    [SerializeField, Min(0.1f), Tooltip("Height above the desired reachable hover altitude allowed before descending. Minimum 0.1 matches the settling tolerance and prevents jitter.")]
    private float _downwardDeadZone = 0.1f;
    [SerializeField, Min(0f)] private float _moveSpeed = 5f;
    public float AuthoredMoveSpeed => _moveSpeed;
    [SerializeField, Min(0f)] private float _rotationSpeed = 360f;
    [SerializeField, Tooltip("Capas de suelo para mantener la altura de vuelo.")]
    private LayerMask _groundMask;
    [SerializeField, Min(1f)] private float _hoverRaycastUp = 40f;
    [SerializeField, Tooltip("Capas que cuentan como suelo o plataforma al comprobar si el disparo sale desde debajo de ellas. Vacío = Default + Terrain.")]
    private LayerMask _shotBlockMask;

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
    [SerializeField, Tooltip("Optional muzzle socket. Without one, the animated drone body supplies the origin.")]
    private Transform _muzzle;
    private SkinnedMeshRenderer _muzzleBody;

    [Header("Locking telegraph (visual only)")]
    [SerializeField] private bool _showLockingLine = true;
    [SerializeField, Min(.005f)] private float _lockingLineWidth = .045f;
    private DroneAimLine _aimLine;
    // Mientras el disparo saldría desde debajo del suelo, el dron sube sin esperar la zona muerta y no apunta.
    private bool _climbForClearShot;
    private EnemyHealth _aimHealth;
    // Share the same model-relative origin between the warning and the actual shot.
    // Bounds follow the animated model and spawn scale; the root pivot is below its body.
    private Vector3 MuzzlePosition => _muzzle != null ? _muzzle.position
        : _muzzleBody != null ? _muzzleBody.bounds.center
        : transform.position + Vector3.up * _muzzleHeight;

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
        _aimHealth = GetComponent<EnemyHealth>();
        _muzzleBody = GetComponentInChildren<SkinnedMeshRenderer>(true);
        _statRandomizer = GetComponent<EnemyStatRandomizer>();
        _rb = GetComponent<Rigidbody>();
        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        HideAimLine();
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
        HideAimLine();
        _hoverMotion = default;
        _climbForClearShot = false;
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    private void OnDisable()
    {
        HideAimLine();
        SetGenericMovement(true);
    }

    public override void OnPoolDespawn()
    {
        HideAimLine();
        base.OnPoolDespawn();
    }

    private void OnDestroy() => _aimLine?.Dispose();
    private void HideAimLine() => _aimLine?.Hide();

    private static readonly RaycastHit[] s_ShotHits = new RaycastHit[8];
    private static readonly Collider[] s_ShotOverlaps = new Collider[8];

    private int ShotBlockMask => _shotBlockMask.value != 0 ? _shotBlockMask.value : LayerMask.GetMask("Default", "Terrain");

    /// <summary>
    /// True si el disparo saldría desde debajo de un suelo o plataforma: la línea cañón-jugador cruza su cara inferior,
    /// o el cañón quedó dentro de la geometría. Paredes y saltos del jugador no cuentan.
    /// </summary>
    private bool ShotComesFromBelowGround(Transform player)
    {
        if (player == null)
            return false;

        Vector3 muzzle = MuzzlePosition;
        int mask = ShotBlockMask;
        int overlaps = Physics.OverlapSphereNonAlloc(muzzle, 0.1f, s_ShotOverlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!s_ShotOverlaps[i].transform.IsChildOf(transform))
                return true;
        }

        Vector3 delta = player.position - muzzle;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return false;

        // Primer impacto que no sea del propio dron: solo cuenta si es la cara inferior de un suelo o plataforma.
        int hits = Physics.RaycastNonAlloc(muzzle, delta / distance, s_ShotHits, distance, mask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool underside = false;
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = s_ShotHits[i];
            if (hit.distance >= nearest || hit.collider.transform.IsChildOf(transform))
                continue;
            nearest = hit.distance;
            underside = hit.normal.y < -0.3f;
        }

        return underside;
    }

    private void LateUpdate()
    {
        // Read the final attack state after Update. Never advance or retarget the AI here.
        Transform player = Player;
        if (!_showLockingLine || !isActiveAndEnabled || _state != State.Locking ||
            player == null || !player.gameObject.activeInHierarchy || _bulletPrefab == null ||
            (_aimHealth != null && _aimHealth.CurrentHealth <= 0) || EmergenceBlocksGameplay ||
            EnemyVerticalEngagement.IsDisengaged(this) || GameplayPause.IsUiPaused ||
            Time.timeScale <= 0f || EntranceCinematic.StartupHeld)
        {
            HideAimLine();
            return;
        }

        if (!DroneAimLine.TryGetWarningProgress(_stateTimer, _lockSeconds, out float progress))
        {
            HideAimLine();
            return;
        }
        _aimLine ??= new DroneAimLine(transform);
        _aimLine.Show(MuzzlePosition, player.position, progress, _lockingLineWidth,
            Mathf.Min(_lockSeconds, DroneAimLine.WarningSeconds));
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
            _climbForClearShot ? 0f : _upwardDeadZone, _downwardDeadZone, verticalSpeed, deltaTime);
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
            _climbForClearShot = false;
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

        // A distancia de tiro pero por debajo del suelo del jugador: sube (MaintainHover) antes de apuntar.
        _climbForClearShot = ShotComesFromBelowGround(Player);
        if (_climbForClearShot)
            return;

        _state = State.Locking;
        _stateTimer = _lockSeconds;
    }

    private void TickLocking()
    {
        _climbForClearShot = ShotComesFromBelowGround(Player);
        if (_climbForClearShot)
        {
            // El jugador quedó sobre una plataforma respecto al dron: cancela el apuntado, no dispara.
            _state = State.Approaching;
            _stateTimer = 0f;
            HideAimLine();
            return;
        }

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

        if (ShotComesFromBelowGround(Player))
            return;

        Vector3 muzzle = MuzzlePosition;
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
