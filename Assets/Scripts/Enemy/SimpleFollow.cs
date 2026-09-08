using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class SimpleFollow : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField, Min(0f)] private float _speed = 3.5f;

    [Header("Steering")]
    [SerializeField, Min(0f)] private float _orbitRadius = 1.8f;
    [SerializeField, Min(0f)] private float _weaveAmplitude = 0.35f;
    [SerializeField, Min(0f)] private float _weaveFrequency = 1.6f;
    [SerializeField, Min(0f)] private float _separationWeight = 0.7f;
    [SerializeField, Min(0.05f)] private float _separationRadius = 1.25f;
    [SerializeField, Min(1)] private int _maxSeparationSamples = 8;

    private float _baseSpeed;
    private float _difficultySpeedMultiplier = 1f;
    private Rigidbody _rb;
    private EnemyKnockbackReceiver _knockback;
    private float _slotAngleDeg;
    private float _weavePhase;
    private int _sampleSeed;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _knockback = GetComponent<EnemyKnockbackReceiver>();
        _baseSpeed = _speed;
        _sampleSeed = GetInstanceID();
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
        EnemyMovementSteering.RandomizeIdentity(out _slotAngleDeg, out _weavePhase);
    }

    private void OnEnable()
    {
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
        EnemyFollowBrain.EnsureExistsAndRegister(this);
    }

    private void OnDisable()
    {
        EnemyFollowBrain.UnregisterIfExists(this);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }

    public void PrepareForSpawn()
    {
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
        _difficultySpeedMultiplier = 1f;
        if (_knockback == null)
            _knockback = GetComponent<EnemyKnockbackReceiver>();
        RandomizeSteeringIdentity();
        ConfigureMovementProfile(EnemyMovementProfile.MeleeSwarm);
        // Variedad ligera de radio por instancia (sigue leyendo el perfil base).
        _orbitRadius *= Random.Range(0.75f, 1.35f);
    }

    public void RandomizeSteeringIdentity()
    {
        EnemyMovementSteering.RandomizeIdentity(out _slotAngleDeg, out _weavePhase);
    }

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

    public void BrainFixedUpdate(float fixedDeltaTime)
    {
        if (_rb == null)
            return;

        if (EnemyVerticalEngagement.IsDisengaged(this))
        {
            Vector3 kbOnly = ConsumeKnockback(fixedDeltaTime);
            if (kbOnly.sqrMagnitude > 0.0001f)
                _rb.MovePosition(transform.position + kbOnly);
            return;
        }

        Vector3 knockbackDisplacement = ConsumeKnockback(fixedDeltaTime);
        if (_target == null)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _rb.MovePosition(transform.position + knockbackDisplacement);
            return;
        }

        EnemyMovementSteering.RefreshPositionCacheIfNeeded();

        Vector3 dir = EnemyMovementSteering.ComposePlanarMoveDirection(
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

        if (dir.sqrMagnitude < 0.0001f)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _rb.MovePosition(transform.position + knockbackDisplacement);
            return;
        }

        float speed = _baseSpeed
            * _difficultySpeedMultiplier
            * WeaponMovementSlowStatus.GetSpeedMultiplier(transform);
        Vector3 nextPos = transform.position + dir * speed * fixedDeltaTime + knockbackDisplacement;
        _rb.MovePosition(nextPos);

        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    private Vector3 ConsumeKnockback(float fixedDeltaTime)
    {
        if (_knockback == null)
            _knockback = GetComponent<EnemyKnockbackReceiver>();

        return _knockback != null ? _knockback.ConsumeDisplacement(fixedDeltaTime) : Vector3.zero;
    }
}
