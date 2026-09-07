using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyFollow : MonoBehaviour
{
    [SerializeField, Tooltip("Si está vacío, se usa el transform del jugador registrado por PlayerMovement (sin búsquedas en runtime).")]
    private Transform _target;

    [SerializeField, Tooltip("Unidades por segundo en el plano XZ (base; el pool puede multiplicar en fase intensa de Overheat).")]
    private float _moveSpeed = 3.5f;

    private float _baseMoveSpeed;
    private float _difficultySpeedMultiplier = 1f;
    private SwarmPooledEnemy _pooled;

    [SerializeField, Tooltip("Distancia mínima al jugador en XZ. Debe ser menor que el radio del hurtbox + collider del enemigo si quieres daño por contacto.")]
    private float _minFollowDistance = 0.55f;

    [SerializeField, Tooltip("Grados por segundo al girar hacia la dirección de desplazamiento.")]
    private float _rotationSpeed = 540f;

    [SerializeField, Tooltip("Aceleración vertical cuando no está trepando (CharacterController).")]
    private float _gravity = -25f;

    [Header("Steering")]
    [SerializeField, Min(0f)] private float _orbitRadius = 1.8f;
    [SerializeField, Min(0f)] private float _weaveAmplitude = 0.35f;
    [SerializeField, Min(0f)] private float _weaveFrequency = 1.6f;
    [SerializeField, Min(0f), Tooltip("Peso del empuje lateral respecto a perseguir al jugador. 0 = sin separación.")]
    private float _separationWeight = 0.55f;
    [SerializeField, Min(0.05f)] private float _separationRadius = 1.1f;
    [SerializeField, Min(1)] private int _maxSeparationSamples = 8;

    [Header("Climb (trepar)")]
    [SerializeField, Min(0.01f), Tooltip("Distancia del raycast frontal para detectar pared u otro enemigo.")]
    private float _forwardRayDistance = 0.45f;

    [SerializeField, Min(0f), Tooltip("Altura desde el suelo (local) para lanzar el raycast frontal.")]
    private float _forwardRayHeight = 0.6f;

    [SerializeField, Min(0f), Tooltip("Velocidad de subida (unidades/segundo) cuando hay obstáculo delante.")]
    private float _climbSpeed = 4.5f;

    [SerializeField, Tooltip("Capas consideradas obstáculo para trepar (por defecto: todo).")]
    private LayerMask _climbObstacleMask = ~0;

    private float _minFollowDistanceSqr;
    private CharacterController _characterController;
    private EnemyKnockbackReceiver _knockback;
    private float _verticalVelocity;
    private float _slotAngleDeg;
    private float _weavePhase;
    private int _sampleSeed;

    private void Awake()
    {
        _baseMoveSpeed = _moveSpeed;
        _pooled = GetComponent<SwarmPooledEnemy>();
        _sampleSeed = GetInstanceID();
        CacheDerived();
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
        _characterController = GetComponent<CharacterController>();
        _knockback = GetComponent<EnemyKnockbackReceiver>();
        EnemyMovementSteering.RandomizeIdentity(out _slotAngleDeg, out _weavePhase);
    }

    private void OnValidate()
    {
        CacheDerived();
    }

    private void CacheDerived()
    {
        _minFollowDistanceSqr = _minFollowDistance * _minFollowDistance;
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }

    public void PrepareForSpawn()
    {
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
        if (_pooled == null)
            _pooled = GetComponent<SwarmPooledEnemy>();
        if (_knockback == null)
            _knockback = GetComponent<EnemyKnockbackReceiver>();
        _difficultySpeedMultiplier = 1f;
        RandomizeSteeringIdentity();
        ConfigureMovementProfile(EnemyMovementProfile.MeleeSwarm);
        _orbitRadius *= Random.Range(0.75f, 1.35f);
        CacheDerived();
    }

    public void RandomizeSteeringIdentity()
    {
        EnemyMovementSteering.RandomizeIdentity(out _slotAngleDeg, out _weavePhase);
    }

    /// <summary>Tras <see cref="PrepareForSpawn"/>; <see cref="DifficultyManager"/> sobrescribe el multiplicador.</summary>
    public void ConfigureDifficultyForSpawn(float speedMultiplier)
    {
        _difficultySpeedMultiplier = Mathf.Max(0.1f, speedMultiplier);
    }

    public void ConfigureMovementProfile(EnemyMovementProfile profile)
    {
        _orbitRadius = Mathf.Max(0f, profile.orbitRadius);
        _weaveAmplitude = Mathf.Max(0f, profile.weaveAmplitude);
        _weaveFrequency = Mathf.Max(0f, profile.weaveFrequency);
        _separationWeight = Mathf.Max(0f, profile.separationWeight);
        _separationRadius = Mathf.Max(0.05f, profile.separationRadius);
        _maxSeparationSamples = Mathf.Max(1, profile.maxSeparationSamples);
    }

    public void OnDespawned()
    {
    }

    private void Update()
    {
        if (_characterController == null)
            return;

        Vector3 knockbackDisplacement = ConsumeKnockback(Time.deltaTime);
        if (_target == null)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _characterController.Move(knockbackDisplacement);
            return;
        }

        Vector3 toTarget = _target.position - transform.position;
        toTarget.y = 0f;
        float sqrToTarget = toTarget.sqrMagnitude;

        Vector3 moveDir = Vector3.zero;
        if (sqrToTarget > 0.0001f && sqrToTarget > _minFollowDistanceSqr)
        {
            EnemyMovementSteering.RefreshPositionCacheIfNeeded();
            moveDir = EnemyMovementSteering.ComposePlanarMoveDirection(
                transform.position,
                _target.position,
                _slotAngleDeg,
                _orbitRadius,
                Time.time,
                _weavePhase,
                _weaveAmplitude,
                _weaveFrequency,
                _separationWeight,
                _separationRadius,
                _maxSeparationSamples,
                _sampleSeed);
        }

        if (moveDir.sqrMagnitude < 0.0001f)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _characterController.Move(knockbackDisplacement);
            return;
        }

        float speed = _baseMoveSpeed
            * _difficultySpeedMultiplier
            * WeaponMovementSlowStatus.GetSpeedMultiplier(transform);
        if (OverheatSwarmBoost.SpeedMultiplier > 1f)
            speed *= OverheatSwarmBoost.SpeedMultiplier;

        bool isClimbing = ShouldClimb(moveDir);
        if (isClimbing)
        {
            _verticalVelocity = _climbSpeed;
        }
        else
        {
            if (_characterController.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            else
                _verticalVelocity += _gravity * Time.deltaTime;
        }

        Vector3 velocity = moveDir * speed;
        velocity.y = _verticalVelocity;
        _characterController.Move(velocity * Time.deltaTime + knockbackDisplacement);

        Quaternion targetRotation = Quaternion.LookRotation(moveDir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            _rotationSpeed * Time.deltaTime);
    }

    private bool ShouldClimb(Vector3 moveDir)
    {
        if (_forwardRayDistance <= 0f || _climbSpeed <= 0f)
            return false;

        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0f, _forwardRayHeight);
        if (Physics.Raycast(origin, moveDir, out RaycastHit hit, _forwardRayDistance, _climbObstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == null)
                return false;
            if (hit.transform.root == transform.root)
                return false;
            return true;
        }

        return false;
    }

    private Vector3 ConsumeKnockback(float deltaTime)
    {
        if (_knockback == null)
            _knockback = GetComponent<EnemyKnockbackReceiver>();

        return _knockback != null ? _knockback.ConsumeDisplacement(deltaTime) : Vector3.zero;
    }
}
