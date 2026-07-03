using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class SimpleFollow : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField, Min(0f)] private float _speed = 3.5f;

    private float _baseSpeed;
    private float _difficultySpeedMultiplier = 1f;
    private Rigidbody _rb;
    private EnemyKnockbackReceiver _knockback;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _knockback = GetComponent<EnemyKnockbackReceiver>();
        _baseSpeed = _speed;
        if (_target == null)
            _target = PlayerMovement.PlayerTransform;
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
    }

    public void ConfigureDifficultyForSpawn(float speedMultiplier)
    {
        _difficultySpeedMultiplier = Mathf.Max(0.1f, speedMultiplier);
    }

    public void OnDespawned()
    {
    }

    public void BrainFixedUpdate(float fixedDeltaTime)
    {
        if (_rb == null)
            return;

        Vector3 knockbackDisplacement = ConsumeKnockback(fixedDeltaTime);
        if (_target == null)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _rb.MovePosition(transform.position + knockbackDisplacement);
            return;
        }

        Vector3 dir = _target.position - transform.position;
        dir.y = 0f;
        float sqr = dir.sqrMagnitude;
        if (sqr < 0.0001f)
        {
            if (knockbackDisplacement.sqrMagnitude > 0.0001f)
                _rb.MovePosition(transform.position + knockbackDisplacement);
            return;
        }

        dir /= Mathf.Sqrt(sqr);

        float speed = _baseSpeed
            * _difficultySpeedMultiplier
            * WeaponMovementSlowStatus.GetSpeedMultiplier(transform);
        Vector3 nextPos = transform.position + dir * speed * fixedDeltaTime + knockbackDisplacement;
        _rb.MovePosition(nextPos);

        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    private Vector3 ConsumeKnockback(float fixedDeltaTime)
    {
        if (_knockback == null)
            _knockback = GetComponent<EnemyKnockbackReceiver>();

        return _knockback != null ? _knockback.ConsumeDisplacement(fixedDeltaTime) : Vector3.zero;
    }
}

