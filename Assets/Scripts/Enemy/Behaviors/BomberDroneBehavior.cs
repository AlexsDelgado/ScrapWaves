using UnityEngine;

/// <summary>
/// Bomber Drone. Unidad voladora que persigue al jugador y hace 3 dashes soltando
/// 5 cargas <see cref="EnemyC4"/> en linea por dash; tras los 3 dashes se comporta
/// como dron normal (acercarse y disparar) hasta recargar las bombas, y repite.
///
/// Toma control total del movimiento: desactiva los followers genericos y mantiene
/// altura de vuelo. Engage/DronePhase usan strafe orbital; el bomb-run dash sigue recto.
/// </summary>
public class BomberDroneBehavior : EnemyBehaviorBase
{
    private enum State { Engage, Dash, BetweenDash, DronePhase }

    [Header("Vuelo")]
    [SerializeField, Min(0f)] private float _hoverHeight = 3.5f;
    [SerializeField, Min(0f), Tooltip("Offset sobre la Y del jugador al perseguir en pisos altos.")]
    private float _playerHoverOffset = 1.25f;
    [SerializeField, Min(0f)] private float _moveSpeed = 5.5f;
    [SerializeField, Min(60f)] private float _rotationSpeed = 360f;
    [SerializeField] private LayerMask _groundMask;
    [SerializeField, Min(1f)] private float _hoverRaycastUp = 40f;

    [Header("Bomb run")]
    [SerializeField, Min(0.5f), Tooltip("Distancia a la que arranca la corrida de bombas.")]
    private float _bombRunStartDistance = 10f;
    [SerializeField, Min(1)] private int _dashCount = 3;
    [SerializeField, Min(1f)] private float _dashSpeed = 18f;
    [SerializeField, Min(0.5f)] private float _dashDistance = 8f;
    [SerializeField, Min(1)] private int _bombsPerDash = 5;
    [SerializeField] private GameObject _c4Prefab;
    [SerializeField, Min(0f)] private float _betweenDashSeconds = 0.35f;

    [Header("Fase dron (recarga)")]
    [SerializeField, Min(0.1f)] private float _rechargeSeconds = 5f;
    [SerializeField] private GameObject _bulletPrefab;
    [SerializeField, Min(1)] private int _bulletDamage = 6;
    [SerializeField, Min(0.1f)] private float _bulletSpeed = 16f;
    [SerializeField, Min(0.1f)] private float _shootInterval = 1.5f;
    [SerializeField, Min(0.5f)] private float _droneStandoffDistance = 8f;

    [Header("Orbit / strafe")]
    [SerializeField, Min(0f)] private float _engageOrbitWeight = 0.32f;
    [SerializeField, Min(0f)] private float _droneOrbitWeight = 0.6f;

    private State _state;
    private float _stateTimer;
    private int _dashesDone;
    private Vector3 _dashDir;
    private Vector3 _dashStart;
    private float _dashTravelled;
    private int _bombsDropped;
    private float _shootTimer;
    private float _orbitSign = 1f;

    protected override void Awake()
    {
        base.Awake();
        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetGenericMovement(false);
        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true;
        _state = State.Engage;
        _dashesDone = 0;
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    public override void OnPoolSpawn()
    {
        base.OnPoolSpawn();
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
    }

    private void OnDisable()
    {
        SetGenericMovement(true);
    }

    private void Update()
    {
        if (Player == null)
            return;

        if (EnemyVerticalEngagement.IsDisengaged(this))
            return;

        MaintainHover();

        switch (_state)
        {
            case State.Engage:
                TickEngage();
                break;
            case State.Dash:
                TickDash();
                break;
            case State.BetweenDash:
                TickBetweenDash();
                break;
            case State.DronePhase:
                TickDronePhase();
                break;
        }
    }

    private void MaintainHover()
    {
        Vector3 origin = transform.position + Vector3.up * _hoverRaycastUp;
        float groundY = transform.position.y - _hoverHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _hoverRaycastUp * 2f, _groundMask, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;

        float minClearanceY = groundY + 1f;
        float targetY = groundY + _hoverHeight;
        Transform player = Player;
        if (player != null)
            targetY = Mathf.Max(minClearanceY, player.position.y + _playerHoverOffset);

        Vector3 pos = transform.position;
        pos.y = Mathf.Lerp(pos.y, targetY, 1f - Mathf.Exp(-6f * Time.deltaTime));
        transform.position = pos;
    }

    private void TickEngage()
    {
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);

        if (PlanarDistanceToPlayer() > _bombRunStartDistance)
        {
            EnemyMovementProfile profile = EnemyMovementProfile.BomberEngage;
            Vector3 slotDir = PlanarDirectionToPoint(
                PlanarPointAroundPlayer(SteeringSlotAngle, Mathf.Max(0.5f, _bombRunStartDistance * 0.35f)));
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
            Vector3 toPlayer = PlanarDirectionToPlayer();
            Vector3 tangent = Vector3.Cross(Vector3.up, toPlayer);
            if (tangent.sqrMagnitude > 0.0001f)
                tangent.Normalize();

            // Prioriza acercarse, con strafe lateral para no ir en línea perfecta.
            Vector3 move = toPlayer + slotDir * 0.35f + weave + sep * profile.separationWeight
                + tangent * (_orbitSign * _engageOrbitWeight);
            MovePlanar(move);
            return;
        }

        StartDash();
    }

    private void StartDash()
    {
        _dashDir = PlanarDirectionToPlayer();
        if (_dashDir.sqrMagnitude < 0.0001f)
            _dashDir = transform.forward;

        _dashStart = transform.position;
        _dashTravelled = 0f;
        _bombsDropped = 0;
        _state = State.Dash;
    }

    private void TickDash()
    {
        FacePlanar(_dashDir, _rotationSpeed);
        float step = _dashSpeed * Time.deltaTime;
        transform.position += _dashDir * step;
        _dashTravelled += step;

        float spacing = _dashDistance / Mathf.Max(1, _bombsPerDash);
        while (_bombsDropped < _bombsPerDash && _dashTravelled >= spacing * (_bombsDropped + 1))
        {
            DropBomb();
            _bombsDropped++;
        }

        if (_dashTravelled >= _dashDistance)
        {
            while (_bombsDropped < _bombsPerDash)
            {
                DropBomb();
                _bombsDropped++;
            }

            _dashesDone++;
            if (_dashesDone >= _dashCount)
            {
                _state = State.DronePhase;
                _stateTimer = _rechargeSeconds;
                _shootTimer = _shootInterval;
            }
            else
            {
                _state = State.BetweenDash;
                _stateTimer = _betweenDashSeconds;
            }
        }
    }

    private void TickBetweenDash()
    {
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
            StartDash();
    }

    private void TickDronePhase()
    {
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);

        EnemyMovementProfile profile = EnemyMovementProfile.FlyingStandoff;
        Vector3 move = ComposeOrbitStandoffDirection(
            _droneStandoffDistance,
            _orbitSign,
            _droneOrbitWeight,
            profile.weaveAmplitude,
            profile.weaveFrequency,
            profile.separationWeight,
            profile.separationRadius,
            profile.maxSeparationSamples);
        MovePlanar(move);

        _shootTimer -= Time.deltaTime;
        if (_shootTimer <= 0f)
        {
            _shootTimer = _shootInterval;
            FireBullet();
        }

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _dashesDone = 0;
            _state = State.Engage;
        }
    }

    private void MovePlanar(Vector3 planarDir)
    {
        planarDir.y = 0f;
        if (planarDir.sqrMagnitude < 0.0001f)
            return;
        transform.position += planarDir.normalized * (_moveSpeed * Time.deltaTime);
    }

    private void DropBomb()
    {
        if (_c4Prefab == null)
            return;

        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * 20f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 60f, _groundMask, QueryTriggerInteraction.Ignore))
            pos = hit.point;

        EnemyTimedAreaPool.TrySpawn(_c4Prefab, pos, Quaternion.identity);
    }

    private void FireBullet()
    {
        if (_bulletPrefab == null)
            return;

        Transform player = Player;
        Vector3 muzzle = transform.position;
        Vector3 dir = (player != null ? player.position : muzzle + transform.forward) - muzzle;
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
