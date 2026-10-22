using UnityEngine;

/// <summary>
/// Boss Giga Worm: caza bajo tierra, telegrapha en el suelo, emerge desde el centro del circulo,
/// sube hacia un apex (Jump), cae hacia donde estaba el jugador al saltar (Fall) y escupe en el ascenso.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class GigaWormBehavior : EnemyBehaviorBase
{
    private enum State
    {
        SpawnUnderground,
        HuntUnderground,
        Telegraph,
        Jump,
        Fall,
        ReturnUnderground
    }

    [Header("Burrow")]
    [SerializeField, Min(0.5f)] private float _buryDepth = 2.5f;
    [SerializeField, Min(0.5f)] private float _undergroundMoveSpeed = 8f;
    public float AuthoredMoveSpeed => _undergroundMoveSpeed;
    [SerializeField, Min(0.5f)] private float _groundBurstIntervalMeters = 1.5f;
    [SerializeField] private LayerMask _groundMask;

    [Header("Hunt (suelo)")]
    [SerializeField, Min(0.5f)] private float _huntDurationBeforeTelegraph = 2.5f;
    [SerializeField, Min(1f)] private float _sameGroundRadius = 25f;
    [SerializeField, Min(0f)] private float _maxGroundHeightDelta = 2f;

    [Header("Telegraph (suelo)")]
    [SerializeField, Min(0.2f)] private float _telegraphDuration = 1.2f;
    [SerializeField, Min(0.5f)] private float _attackRadius = 3f;

    [Header("Jump (subida)")]
    [SerializeField, Min(0.5f)] private float _jumpRiseSpeed = 14f;
    [SerializeField, Min(0f)] private float _jumpArcHeight = 5f;
    [SerializeField, Range(0f, 1f), Tooltip("Que tan lejos hacia el punto de caida se coloca el apex en XZ (0 = encima del origen, 1 = encima del aterrizaje).")]
    private float _apexHorizontalLead = 0.65f;
    [SerializeField, Min(60f)] private float _airTiltSpeed = 540f;

    [Header("Fall (caida)")]
    [SerializeField, Min(0.5f)] private float _fallSpeed = 20f;
    [SerializeField, Min(0.05f)] private float _landingSnapDistance = 0.15f;
    [SerializeField, Min(1f), Tooltip("Distancia minima en XZ entre origen del salto y punto de caida.")]
    private float _minFallDistanceFromOrigin = 4f;
    [SerializeField, Min(0.1f)] private float _apexArrivalThreshold = 0.75f;

    [Header("Impacto aereo / aterrizaje")]
    [SerializeField, Min(1)] private int _emergeDamage = 20;
    [SerializeField, Min(0f)] private float _emergePushForce = 14f;
    [SerializeField, Min(0.2f)] private float _airborneHitRadius = 1.8f;
    [SerializeField, Min(0f)] private float _airborneHitCooldown = 0.35f;

    [Header("Spit (solo durante Jump)")]
    [SerializeField, Min(0.05f)] private float _spitInterval = 0.35f;
    [SerializeField, Min(1)] private int _spitProjectilesPerBurst = 2;
    [SerializeField, Min(5f)] private float _spitSpreadDegrees = 45f;
    [SerializeField, Min(0.2f)] private float _spitTravelTime = 0.75f;
    [SerializeField, Min(0f)] private float _spitArcHeight = 2.5f;
    [SerializeField, Min(0.05f)] private float _spitCollisionRadius = 0.25f;
    [SerializeField, Min(2f)] private float _spitLandingRadius = 6f;
    [SerializeField] private GameObject _corrosiveSlimeAreaPrefab;

    private State _state;
    private float _stateTimer;
    private float _huntTimer;
    private float _distanceSinceLastBurst;
    private float _spitTimer;
    private float _airborneHitTimer;
    private Vector3 _surfacePosition;
    private Vector3 _emergeOriginGround;
    private Vector3 _playerSnapshotGround;
    private Vector3 _apexPoint;
    private Vector3 _landingPoint;
    private bool _landingImpactApplied;
    private AttackTelegraphVfx _activeTelegraph;
    private Collider _debrisGroundSupport;
    private readonly StalkerFlightPath _flightPath = new();
    private float _flightDistance;
    private float _ascentPathSpeedMultiplier = 1f;

    private StalkerRigAnimation _rigAnimation;
    private EnemyHealth _health;
    private EnemyRegistryMember _registryMember;
    private CharacterController _characterController;
    private Collider[] _colliders;
    private Renderer[] _renderers;
    private PlayerMovement _playerMovement;
    private PlayerHealth _playerHealth;

    protected override void Awake()
    {
        base.Awake();
        _health = GetComponent<EnemyHealth>();
        _rigAnimation = GetComponent<StalkerRigAnimation>();
        _registryMember = GetComponent<EnemyRegistryMember>();
        _characterController = GetComponent<CharacterController>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);

        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        CachePlayerComponents();
        SetGenericMovement(false);
        _state = State.SpawnUnderground;
        _stateTimer = 0f;
        _huntTimer = 0f;
        _spitTimer = 0f;
        _airborneHitTimer = 0f;
        _distanceSinceLastBurst = _groundBurstIntervalMeters;
        _landingImpactApplied = false;
        _debrisGroundSupport = null;
        EnterBurrow();
    }

    private void OnDisable()
    {
        _rigAnimation?.ResetPose();
        ClearTelegraph();
        ExitBurrowVisualOnly();
        SetGenericMovement(true);
        if (_characterController != null)
            _characterController.enabled = true;
    }

    private void Update()
    {
        if (Player == null)
            return;

        switch (_state)
        {
            case State.SpawnUnderground:
                TickSpawnUnderground();
                break;
            case State.HuntUnderground:
                TickHuntUnderground();
                break;
            case State.Telegraph:
                TickTelegraph();
                break;
            case State.Jump:
                TickJump();
                break;
            case State.Fall:
                TickFall();
                break;
            case State.ReturnUnderground:
                TickReturnUnderground();
                break;
        }
    }

    private void CachePlayerComponents()
    {
        Transform player = Player;
        if (player == null)
            return;

        _playerMovement = player.GetComponentInParent<PlayerMovement>();
        _playerHealth = player.GetComponentInParent<PlayerHealth>();
    }

    private void TickSpawnUnderground()
    {
        SnapUndergroundToGround();
        _state = State.HuntUnderground;
        _huntTimer = 0f;
    }

    private void TickHuntUnderground()
    {
        MoveUndergroundTowardPlayer();
        _huntTimer += Time.deltaTime;

        if (!CanSensePlayerOnSameGround())
            return;

        if (_huntTimer >= _huntDurationBeforeTelegraph)
            BeginTelegraph();
    }

    private void TickTelegraph()
    {
        _stateTimer -= Time.deltaTime;
        if (_stateTimer > 0f)
            return;

        ClearTelegraph();
        BeginJump();
    }

    private void TickJump()
    {
        AdvanceAlongFlight(_jumpRiseSpeed * _ascentPathSpeedMultiplier, _flightPath.ApexDistance);

        TickAirborneHit();
        TickSpitDuringJump();

        if (_flightDistance >= _flightPath.ApexDistance)
        {
            _state = State.Fall;
            _rigAnimation?.BeginDive();
        }
    }

    private void TickFall()
    {
        AdvanceAlongFlight(_fallSpeed, _flightPath.Length);

        TickAirborneHit();

        if (_flightDistance >= _flightPath.Length)
        {
            transform.position = _landingPoint;
            ApplyLandingImpact();
            _rigAnimation?.BeginReburrow();
            _surfacePosition = _landingPoint;
            _state = State.ReturnUnderground;
            _stateTimer = 0.35f;
        }
    }

    private void TickReturnUnderground()
    {
        // Read the last completed visible pose before moving the head again. Hiding is
        // allowed only after a rendered frame already had the whole body below ground.
        bool bodyAboveGround = _rigAnimation != null
            ? _rigAnimation.GetHighestBodyPoint() > _landingPoint.y - 0.05f
            : transform.position.y > _landingPoint.y - _buryDepth;
        _stateTimer -= Time.deltaTime;
        // Keep feeding the entire body through the entry point; reaching the head is only
        // the start of burrowing. The impact clip finishes and holds while the tail follows.
        AdvanceAlongFlight(_fallSpeed, float.PositiveInfinity);

        if (_stateTimer > 0f || bodyAboveGround)
            return;

        EnterBurrow();
        // Once hidden, return to normal hunt depth. A deeply submerged head is beyond
        // the downward ground probe's range, so use the already resolved landing surface.
        PlaceUndergroundAt(_landingPoint);
        _state = State.HuntUnderground;
        _huntTimer = 0f;
        _distanceSinceLastBurst = _groundBurstIntervalMeters;
    }

    private void BeginTelegraph()
    {
        _state = State.Telegraph;
        _stateTimer = _telegraphDuration;

        Vector3 playerPos = Player.position;
        if (!TryGetGroundPoint(new Vector3(playerPos.x, transform.position.y, playerPos.z), out _emergeOriginGround))
            _emergeOriginGround = playerPos;

        _surfacePosition = _emergeOriginGround;
        PlaceUndergroundAt(_emergeOriginGround);

        ClearTelegraph();
        _activeTelegraph = AttackTelegraphVfx.Spawn(_emergeOriginGround, _attackRadius, _telegraphDuration);
        SpawnUndergroundDebris(_emergeOriginGround);
    }

    private void BeginJump()
    {
        SnapshotPlayerGroundAtJump(out _playerSnapshotGround);
        _landingPoint = _playerSnapshotGround;
        EnforceMinimumFallDistance();
        _apexPoint = CalculateApexPoint(_emergeOriginGround, _landingPoint);

        _surfacePosition = _emergeOriginGround;
        ExitBurrow();

        _state = State.Jump;
        _spitTimer = 0f;
        _airborneHitTimer = 0f;
        _landingImpactApplied = false;
        _flightPath.Configure(_emergeOriginGround, _apexPoint, _landingPoint,
            _rigAnimation != null ? _rigAnimation.BodyLength : 12f * Mathf.Abs(transform.lossyScale.y));
        // The rounded route is longer than the former direct ascent. Preserve its nominal
        // duration so the existing spit interval has the same firing window.
        _ascentPathSpeedMultiplier = _flightPath.ApexDistance
            / Mathf.Max(0.01f, Vector3.Distance(_emergeOriginGround, _apexPoint));
        _flightDistance = 0f;
        transform.SetPositionAndRotation(_flightPath.GetPosition(0f), _flightPath.GetRotation(0f));
        _rigAnimation?.BeginJump();
    }

    private void EnforceMinimumFallDistance()
    {
        Vector3 planar = _landingPoint - _emergeOriginGround;
        planar.y = 0f;
        float dist = planar.magnitude;

        Vector3 dir = dist > 0.0001f ? planar / dist : GetRandomPlanarDirection();

        if (dist >= _minFallDistanceFromOrigin)
            return;

        Vector3 pushed = _emergeOriginGround + dir * _minFallDistanceFromOrigin;
        if (TryGetGroundPoint(pushed, out Vector3 ground))
            _landingPoint = ground;
        else
        {
            _landingPoint = pushed;
            _landingPoint.y = _emergeOriginGround.y;
        }
    }

    private static Vector3 GetRandomPlanarDirection()
    {
        float angle = Random.Range(0f, 360f);
        return Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
    }

    private void SnapshotPlayerGroundAtJump(out Vector3 ground)
    {
        Vector3 playerPos = Player.position;
        if (!TryGetGroundPoint(new Vector3(playerPos.x, transform.position.y, playerPos.z), out ground))
            ground = playerPos;
    }

    private Vector3 CalculateApexPoint(Vector3 origin, Vector3 landing)
    {
        Vector3 planarDelta = landing - origin;
        planarDelta.y = 0f;

        Vector3 apex = origin;
        if (planarDelta.sqrMagnitude > 0.0001f)
            apex += planarDelta.normalized * (planarDelta.magnitude * _apexHorizontalLead);

        float groundY = Mathf.Max(origin.y, landing.y);
        apex.y = groundY + _jumpArcHeight;
        return apex;
    }

    private void TickSpitDuringJump()
    {
        if (_corrosiveSlimeAreaPrefab == null)
            return;

        _spitTimer -= Time.deltaTime;
        if (_spitTimer > 0f)
            return;

        _spitTimer = _spitInterval;
        FireSpitBurst();
    }

    private void FireSpitBurst()
    {
        Vector3 origin = GetHeadWorldPosition();
        Vector3 toTarget = _landingPoint - origin;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
            toTarget = transform.up;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
            toTarget = Vector3.forward;
        toTarget.Normalize();

        int count = Mathf.Max(1, _spitProjectilesPerBurst);
        float halfSpread = _spitSpreadDegrees * 0.5f;

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float angle = Mathf.Lerp(-halfSpread, halfSpread, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * toTarget;
            Vector3 landing = origin + dir * Random.Range(_spitLandingRadius * 0.35f, _spitLandingRadius);

            if (TryGetGroundPoint(new Vector3(landing.x, origin.y, landing.z), out Vector3 ground))
                landing = ground;

            CorrosiveSpitProjectile.Launch(
                _corrosiveSlimeAreaPrefab,
                origin,
                landing,
                _spitTravelTime,
                _spitArcHeight,
                _spitCollisionRadius);
        }
    }

    private void TickAirborneHit()
    {
        if (_airborneHitTimer > 0f)
        {
            _airborneHitTimer -= Time.deltaTime;
            return;
        }

        if (_playerHealth == null)
            CachePlayerComponents();

        if (_playerHealth == null)
            return;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > _airborneHitRadius * _airborneHitRadius)
            return;

        _playerHealth.TakeDamage(_emergeDamage);
        PlayerCombatHooks.TryPush(transform.position, _emergePushForce);
        _airborneHitTimer = _airborneHitCooldown;
    }

    private void ApplyLandingImpact()
    {
        if (_landingImpactApplied)
            return;

        _landingImpactApplied = true;
        ExplosionRadiusVfx.Spawn(_landingPoint, _attackRadius);
        GigaWormGroundBurstVfx.SpawnEntry(_landingPoint);

        if (_playerHealth == null)
            CachePlayerComponents();

        if (_playerHealth == null)
            return;

        Vector3 offset = Player.position - _landingPoint;
        offset.y = 0f;
        if (offset.sqrMagnitude <= _attackRadius * _attackRadius)
        {
            _playerHealth.TakeDamage(_emergeDamage);
            PlayerCombatHooks.TryPush(_landingPoint, _emergePushForce);
        }
    }

    /// <summary>Moves the head along one continuous arch; the rig samples the same path behind it.</summary>
    private void AdvanceAlongFlight(float speed, float phaseEnd)
    {
        float next = Mathf.Min(phaseEnd, _flightDistance + speed * DebugSpeedTool.EnemyScale * Time.deltaTime);
        float angle = Quaternion.Angle(transform.rotation, _flightPath.GetRotation(next));
        float allowedAngle = _airTiltSpeed * DebugSpeedTool.EnemyScale * Time.deltaTime;
        if (angle > allowedAngle && angle > 0.0001f)
            next = Mathf.Lerp(_flightDistance, next, allowedAngle / angle);
        _flightDistance = next;
        transform.SetPositionAndRotation(_flightPath.GetPosition(next), _flightPath.GetRotation(next));
    }

    private Vector3 GetHeadWorldPosition()
    {
        float halfBodyLength = Mathf.Max(0.5f, transform.localScale.y * 0.45f);
        return transform.position + transform.up * halfBodyLength;
    }

    /// <summary>Base reward position; the pickup applies its usual lift above this anchor.</summary>
    public Vector3 GetRewardDropPosition()
    {
        // The diving head can be far below the ground while the tail is still visible.
        // Its resolved entry point remains the accessible reward surface for this jump.
        if (_state == State.ReturnUnderground)
            return _landingPoint;
        if (_state == State.Jump || _state == State.Fall)
            return transform.position;

        // Hidden hunting moves away from the last landing point. Resolve the current XZ,
        // with the normal hunt depth as fallback instead of a stale cached surface.
        if (TryGetGroundPoint(transform.position, out Vector3 ground))
            return ground;
        return transform.position + Vector3.up * _buryDepth;
    }

    private void PlaceUndergroundAt(Vector3 surfaceGround)
    {
        _surfacePosition = surfaceGround;
        transform.position = surfaceGround - Vector3.up * _buryDepth;
    }

    private void MoveUndergroundTowardPlayer()
    {
        Vector3 target = Player.position;
        Vector3 pos = transform.position;
        Vector3 planarDelta = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
        float step = _undergroundMoveSpeed * DebugSpeedTool.EnemyScale * Time.deltaTime;

        if (planarDelta.sqrMagnitude > step * step)
            pos += planarDelta.normalized * step;
        else
        {
            pos.x = target.x;
            pos.z = target.z;
        }

        SnapUndergroundPosition(ref pos);
        transform.position = pos;

        _distanceSinceLastBurst += step;
        if (_distanceSinceLastBurst >= _groundBurstIntervalMeters)
        {
            _distanceSinceLastBurst = 0f;
            if (TryGetGroundPoint(pos, out Vector3 ground))
                SpawnUndergroundDebris(ground);
        }
    }

    private void SpawnUndergroundDebris(Vector3 surfacePoint)
    {
        if (_playerMovement != null && _playerMovement.TryGetGroundSupport(out Collider support))
            _debrisGroundSupport = support;
        GigaWormGroundBurstVfx.SpawnOnSurface(surfacePoint, _groundMask, _debrisGroundSupport);
    }

    private void SnapUndergroundToGround()
    {
        Vector3 pos = transform.position;
        SnapUndergroundPosition(ref pos);
        transform.position = pos;
        _surfacePosition = pos + Vector3.up * _buryDepth;
    }

    private void SnapUndergroundPosition(ref Vector3 pos)
    {
        if (TryGetGroundPoint(pos, out Vector3 ground))
        {
            _surfacePosition = ground;
            pos.y = ground.y - _buryDepth;
        }
    }

    private bool CanSensePlayerOnSameGround()
    {
        if (_playerMovement == null)
            CachePlayerComponents();

        if (_playerMovement == null || !_playerMovement.IsGroundedOnSurface)
            return false;

        if (!TryGetGroundPoint(transform.position, out Vector3 wormGround))
            return false;

        if (!TryGetGroundPoint(Player.position, out Vector3 playerGround))
            return false;

        Vector3 delta = playerGround - wormGround;
        delta.y = 0f;
        if (delta.sqrMagnitude > _sameGroundRadius * _sameGroundRadius)
            return false;

        return Mathf.Abs(playerGround.y - wormGround.y) <= _maxGroundHeightDelta;
    }

    private bool TryGetGroundPoint(Vector3 sample, out Vector3 groundPoint)
    {
        Vector3 origin = new Vector3(sample.x, sample.y + 30f, sample.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 80f, _groundMask, QueryTriggerInteraction.Ignore))
        {
            groundPoint = hit.point;
            return true;
        }

        groundPoint = sample;
        return false;
    }

    private void EnterBurrow()
    {
        if (_health != null)
            _health.SetInvincible(true);

        SetGenericMovement(false);
        SetCollidersEnabled(false);
        SetRenderersEnabled(false);
        _rigAnimation?.EnterBurrow();

        if (_registryMember != null)
            _registryMember.enabled = false;
        else
            EnemyRegistry.Unregister(transform);

        if (_characterController != null)
            _characterController.enabled = false;

        SnapUndergroundToGround();
    }

    private void ExitBurrow()
    {
        if (_health != null)
            _health.SetInvincible(false);

        SetCollidersEnabled(true);
        SetRenderersEnabled(true);

        if (_registryMember != null)
            _registryMember.enabled = true;
        else
            EnemyRegistry.Register(transform);

        if (_characterController != null)
            _characterController.enabled = false;

        transform.position = _surfacePosition;
    }

    private void ExitBurrowVisualOnly()
    {
        SetCollidersEnabled(true);
        SetRenderersEnabled(true);

        if (_health != null)
            _health.SetInvincible(false);

        if (_registryMember != null && !_registryMember.enabled)
            _registryMember.enabled = true;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (_colliders == null)
            return;

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = enabled;
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = enabled;
        }
    }

    private void ClearTelegraph()
    {
        if (_activeTelegraph != null)
        {
            Destroy(_activeTelegraph.gameObject);
            _activeTelegraph = null;
        }
    }
}
