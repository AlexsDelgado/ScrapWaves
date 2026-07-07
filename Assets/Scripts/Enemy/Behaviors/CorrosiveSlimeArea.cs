using UnityEngine;

/// <summary>
/// Charco corrosivo dejado por el Giga Worm. Ralentiza al jugador y puede aplicar dano por tick.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CorrosiveSlimeArea : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float _lifetime = 5f;
    [SerializeField, Min(0.1f)] private float _tickInterval = 0.5f;
    [SerializeField, Min(0)] private int _damagePerTick = 2;
    [SerializeField, Range(0.05f, 1f)] private float _slowMultiplier = 0.45f;
    [SerializeField, Min(0.1f)] private float _slowRefreshSeconds = 0.35f;

    private float _tickTimer;
    private float _slowRefreshTimer;
    private bool _playerInside;
    private PlayerHealth _player;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnEnable()
    {
        _tickTimer = 0f;
        _slowRefreshTimer = 0f;
        _playerInside = false;
        _player = null;
        Invoke(nameof(SelfDestroy), _lifetime);
    }

    private void OnDisable()
    {
        CancelInvoke();
    }

    private void Update()
    {
        if (!_playerInside || _player == null)
            return;

        _slowRefreshTimer -= Time.deltaTime;
        if (_slowRefreshTimer <= 0f)
        {
            _slowRefreshTimer = _slowRefreshSeconds;
            PlayerCombatHooks.TrySlow(_slowMultiplier, _slowRefreshSeconds + 0.05f);
        }

        if (_damagePerTick <= 0)
            return;

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f)
            return;

        _tickTimer = _tickInterval;
        _player.TakeDamage(_damagePerTick);
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null)
            return;

        _player = player;
        _playerInside = true;
        _tickTimer = 0f;
        _slowRefreshTimer = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerHealth>() == _player && _player != null)
            _playerInside = false;
    }

    private void SelfDestroy()
    {
        if (TryGetComponent(out PooledTimedAreaMember member))
            member.ReturnToPool();
        else
        {
            EnemyPoolProfiler.RegisterDestroy();
            Destroy(gameObject);
        }
    }
}
