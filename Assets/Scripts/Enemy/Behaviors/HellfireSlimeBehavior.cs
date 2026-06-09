using UnityEngine;

/// <summary>
/// Hellfire Slime. Persigue como el slime normal (follower generico) y, al llegar a
/// <see cref="_triggerRange"/>, se vuelve invencible/intangible al dano, se lanza
/// hacia el jugador y EXPLOTA: dano de area via <see cref="PlayerHealth.TakeDamage"/>,
/// deja un <see cref="FireArea"/> en el suelo y muere.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class HellfireSlimeBehavior : EnemyBehaviorBase
{
    private enum State { Seek, Launching, Done }

    [Header("Disparador")]
    [SerializeField, Min(0.3f)] private float _triggerRange = 3f;
    [SerializeField, Min(60f)] private float _rotationSpeed = 540f;

    [Header("Lanzamiento")]
    [SerializeField, Min(1f)] private float _launchSpeed = 18f;
    [SerializeField, Min(0.05f)] private float _launchDuration = 0.5f;
    [SerializeField, Min(0.5f)] private float _launchMaxDistance = 6f;
    [SerializeField, Min(0.2f)] private float _contactRadius = 1.3f;

    [Header("Explosion")]
    [SerializeField, Min(0.5f)] private float _explosionRadius = 3.5f;
    [SerializeField, Min(1)] private int _explosionDamage = 25;
    [SerializeField, Min(0f), Tooltip("Duración de la quemadura aplicada al jugador al impactar (doc: 3 s).")]
    private float _burnSeconds = 3f;
    [SerializeField, Min(0), Tooltip("Daño por segundo de la quemadura (doc: bajo).")]
    private int _burnDps = 2;
    [SerializeField] private GameObject _fireAreaPrefab;
    [SerializeField, Tooltip("Capas de suelo para colocar el area de fuego.")]
    private LayerMask _groundMask;

    private State _state;
    private float _stateTimer;
    private float _travelled;
    private Vector3 _launchDir;
    private EnemyHealth _health;

    protected override void Awake()
    {
        base.Awake();
        _health = GetComponent<EnemyHealth>();
        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetGenericMovement(true);
        _state = State.Seek;
        _stateTimer = 0f;
        _travelled = 0f;
        if (_health != null)
            _health.SetInvincible(false);
    }

    private void OnDisable()
    {
        SetGenericMovement(true);
    }

    private void Update()
    {
        if (Player == null)
            return;

        switch (_state)
        {
            case State.Seek:
                TickSeek();
                break;
            case State.Launching:
                TickLaunching();
                break;
        }
    }

    private void TickSeek()
    {
        if (PlanarDistanceToPlayer() > _triggerRange)
            return;

        SetGenericMovement(false);
        if (_health != null)
            _health.SetInvincible(true);

        _launchDir = PlanarDirectionToPlayer();
        if (_launchDir.sqrMagnitude < 0.0001f)
            _launchDir = transform.forward;

        _state = State.Launching;
        _stateTimer = _launchDuration;
        _travelled = 0f;
    }

    private void TickLaunching()
    {
        FacePlanar(_launchDir, _rotationSpeed);
        float step = _launchSpeed * Time.deltaTime;
        transform.position += _launchDir * step;
        _travelled += step;

        _stateTimer -= Time.deltaTime;
        if (PlanarDistanceToPlayer() <= _contactRadius || _stateTimer <= 0f || _travelled >= _launchMaxDistance)
            Explode();
    }

    private void Explode()
    {
        if (_state == State.Done)
            return;
        _state = State.Done;

        ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius);

        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerHealth player = hits[i].GetComponentInParent<PlayerHealth>();
            if (player != null)
            {
                player.TakeDamage(_explosionDamage);
                PlayerCombatHooks.TryBurn(_burnSeconds, _burnDps);
                break;
            }
        }

        SpawnFireArea();
        Die();
    }

    private void SpawnFireArea()
    {
        if (_fireAreaPrefab == null)
            return;

        Vector3 pos = transform.position;
        Vector3 origin = pos + Vector3.up * 20f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 60f, _groundMask, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y;

        Instantiate(_fireAreaPrefab, pos, Quaternion.identity);
    }

    private void Die()
    {
        if (_health != null)
        {
            _health.SetInvincible(false);
            _health.ApplyDamage(_health.CurrentHealth);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
