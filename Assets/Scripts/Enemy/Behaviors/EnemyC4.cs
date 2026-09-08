using UnityEngine;

/// <summary>
/// Carga C4 que suelta el Bomber Drone en el suelo. Tras un breve armado, explota
/// por proximidad del jugador haciendo dano de area via
/// <see cref="PlayerHealth.TakeDamage"/>. Se autodestruye al expirar su vida.
/// </summary>
public class EnemyC4 : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Tiempo antes de poder detonar (evita explotar al instante).")]
    private float _armDelay = 0.4f;

    [SerializeField, Min(0.1f), Tooltip("Distancia al jugador que dispara la detonacion.")]
    private float _triggerRadius = 1.6f;

    [SerializeField, Min(0f), Tooltip("Si |ΔY| con el jugador supera esto, no arma por proximidad planar.")]
    private float _maxTriggerVerticalDelta = 40f;

    [SerializeField, Min(0.1f), Tooltip("Radio del dano de area al explotar.")]
    private float _explosionRadius = 3f;

    [SerializeField, Min(1)] private int _damage = 18;

    [SerializeField, Min(0f), Tooltip("Knockback pequeño al detonar cerca del jugador.")]
    private float _explosionPushForce = 3f;

    [SerializeField, Min(0.5f), Tooltip("Vida maxima; si nadie la pisa, se limpia.")]
    private float _maxLifetime = 12f;

    private float _armedAt;
    private float _expiresAt;
    private bool _exploded;
    private Transform _player;
    private int _runtimeDamage;
    private bool _damageConfigured;

    private void OnEnable()
    {
        _exploded = false;
        _armedAt = Time.time + _armDelay;
        _expiresAt = Time.time + _maxLifetime;
        _player = PlayerMovement.PlayerTransform;
        _damageConfigured = false;
        _runtimeDamage = _damage;
    }

    /// <summary>Opcional: daño ya escalado por dificultad del enemigo que la soltó.</summary>
    public void ConfigureDamage(int damage)
    {
        _runtimeDamage = Mathf.Max(1, damage);
        _damageConfigured = true;
    }

    private void Update()
    {
        if (_exploded)
            return;

        if (Time.time >= _expiresAt)
        {
            ReturnToPool();
            return;
        }

        if (Time.time < _armedAt)
            return;

        if (_player == null)
            _player = PlayerMovement.PlayerTransform;
        if (_player == null)
            return;

        Vector3 toPlayer = _player.position - transform.position;
        if (Mathf.Abs(toPlayer.y) > _maxTriggerVerticalDelta)
            return;

        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= _triggerRadius * _triggerRadius)
            Explode();
    }

    private void Explode()
    {
        if (_exploded)
            return;

        _exploded = true;
        ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius);

        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerHealth player = hits[i].GetComponentInParent<PlayerHealth>();
            if (player != null)
            {
                int damage = _runtimeDamage;
                if (!_damageConfigured)
                {
                    DifficultyManager difficulty = DifficultyManager.Instance;
                    if (difficulty != null)
                        damage = Mathf.Max(1, Mathf.RoundToInt(_damage * difficulty.GetEnemyDamageMultiplier()));
                }

                player.TakeDamage(damage);
                if (_explosionPushForce > 0f)
                    PlayerCombatHooks.TryPush(transform.position, _explosionPushForce);
                break;
            }
        }

        ReturnToPool();
    }

    private void ReturnToPool()
    {
        if (TryGetComponent(out PooledTimedAreaMember member))
            member.ReturnToPool();
        else
        {
            EnemyPoolProfiler.RegisterDestroy();
            Destroy(gameObject);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.05f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, _explosionRadius);
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _triggerRadius);
    }
}
