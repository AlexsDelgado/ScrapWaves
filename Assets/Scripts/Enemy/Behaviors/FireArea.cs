using UnityEngine;

/// <summary>
/// Area de fuego que deja el Hellfire Slime al explotar. Vive un tiempo fijo y
/// hace dano por tick al jugador que este dentro del trigger (via
/// <see cref="PlayerHealth.TakeDamage"/>). La quemadura (DoT) de 3 s se aplica en el
/// impacto de la explosion (ver <see cref="HellfireSlimeBehavior"/>), no aqui, para
/// evitar daño doble.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FireArea : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float _lifetime = 4f;

    [SerializeField, Min(0.1f), Tooltip("Cada cuantos segundos aplica dano al jugador dentro.")]
    private float _tickInterval = 0.5f;

    [SerializeField, Min(1)] private int _damagePerTick = 4;

    private float _tickTimer;
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
        _playerInside = false;
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
