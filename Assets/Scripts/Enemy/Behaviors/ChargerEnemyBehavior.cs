using UnityEngine;

/// <summary>
/// Comportamiento de embestida parametrizable, compartido por Chaser Bot y Shocker
/// Bot. Persigue (usando el follower generico) y, al entrar en distancia, se frena,
/// carga (telegraph) y hace un dash recto super rapido. Si impacta: dano + empuje
/// (hook). Si no logra alcanzar al jugador en un tiempo dado, "overheatea" y se
/// ralentiza. Con <see cref="_overcharged"/> (Shocker) añade dano de descarga +
/// stun (hook) al chocar y un cooldown post-choque mayor.
/// </summary>
public class ChargerEnemyBehavior : EnemyBehaviorBase
{
    private enum State { Chase, Charging, Dashing, Recover, Overheat }

    [Header("Enganche / carga")]
    [SerializeField, Min(0.5f), Tooltip("Distancia a la que se frena y empieza a cargar el dash.")]
    private float _lockInDistance = 5f;
    [SerializeField, Min(0f), Tooltip("Telegraph: tiempo cargando antes de lanzar el dash.")]
    private float _chargeSeconds = 1f;
    [SerializeField, Min(60f)] private float _rotationSpeed = 540f;

    [Header("Dash")]
    [SerializeField, Min(1f)] private float _dashSpeed = 30f;
    [SerializeField, Min(0.05f)] private float _dashDuration = 0.5f;
    [SerializeField, Min(0.5f)] private float _dashMaxDistance = 12f;
    [SerializeField, Min(0.2f), Tooltip("Distancia al jugador que cuenta como impacto del dash.")]
    private float _contactRadius = 1.5f;
    [SerializeField, Min(1)] private int _hitDamage = 12;
    [SerializeField, Min(0f), Tooltip("Fuerza de empuje al jugador (hook, ~10m).")]
    private float _pushForce = 12f;

    [Header("Overheat (no alcanza al jugador)")]
    [SerializeField, Min(0.5f), Tooltip("Si no impacta en este tiempo desde que engancha, overheatea.")]
    private float _maxEngageSeconds = 5f;
    [SerializeField, Min(0.1f)] private float _overheatSeconds = 2.5f;
    [SerializeField, Min(0f), Tooltip("Velocidad reducida al moverse durante el overheat.")]
    private float _overheatSpeed = 1f;

    [Header("Shocker (overcargado)")]
    [SerializeField, Tooltip("Activa descarga (dano extra) + stun al chocar y cooldown mayor.")]
    private bool _overcharged;
    [SerializeField, Min(0)] private int _dischargeDamage = 8;
    [SerializeField, Min(0f)] private float _stunSeconds = 1f;
    [SerializeField, Min(0f)] private float _postCrashCooldown = 1.5f;

    private State _state;
    private float _stateTimer;
    private Vector3 _dashDir;
    private float _dashTravelled;
    private bool _engaged;
    private float _engageStartTime;
    private PlayerHealth _playerHealth;

    protected override void OnEnable()
    {
        base.OnEnable();
        SetGenericMovement(true);
        _state = State.Chase;
        _engaged = false;
        _stateTimer = 0f;
        CachePlayerHealth();
    }

    private void OnDisable()
    {
        SetGenericMovement(true);
    }

    private void CachePlayerHealth()
    {
        Transform player = Player;
        if (player != null)
            _playerHealth = player.GetComponentInParent<PlayerHealth>();
    }

    private void Update()
    {
        if (Player == null)
            return;

        switch (_state)
        {
            case State.Chase:
                TickChase();
                break;
            case State.Charging:
                TickCharging();
                break;
            case State.Dashing:
                TickDashing();
                break;
            case State.Recover:
                TickRecover();
                break;
            case State.Overheat:
                TickOverheat();
                break;
        }
    }

    private void TickChase()
    {
        if (_engaged && Time.time - _engageStartTime >= _maxEngageSeconds)
        {
            EnterOverheat();
            return;
        }

        if (PlanarDistanceToPlayer() <= _lockInDistance)
        {
            if (!_engaged)
            {
                _engaged = true;
                _engageStartTime = Time.time;
            }

            SetGenericMovement(false);
            _state = State.Charging;
            _stateTimer = _chargeSeconds;
        }
    }

    private void TickCharging()
    {
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer > 0f)
            return;

        _dashDir = PlanarDirectionToPlayer();
        if (_dashDir.sqrMagnitude < 0.0001f)
            _dashDir = transform.forward;

        _dashTravelled = 0f;
        _state = State.Dashing;
        _stateTimer = _dashDuration;
    }

    private void TickDashing()
    {
        float step = _dashSpeed * Time.deltaTime;
        transform.position += _dashDir * step;
        _dashTravelled += step;

        if (PlanarDistanceToPlayer() <= _contactRadius)
        {
            OnCrash();
            return;
        }

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f || _dashTravelled >= _dashMaxDistance)
        {
            if (Time.time - _engageStartTime >= _maxEngageSeconds)
                EnterOverheat();
            else
            {
                SetGenericMovement(true);
                _state = State.Chase;
            }
        }
    }

    private void OnCrash()
    {
        if (_playerHealth == null)
            CachePlayerHealth();

        int damage = _hitDamage + (_overcharged ? _dischargeDamage : 0);
        if (_playerHealth != null)
            _playerHealth.TakeDamage(damage);

        PlayerCombatHooks.TryPush(transform.position, _pushForce);
        if (_overcharged)
            PlayerCombatHooks.TryStun(_stunSeconds);

        _engaged = false;
        SetGenericMovement(false);
        _state = State.Recover;
        _stateTimer = _overcharged ? _postCrashCooldown : Mathf.Min(0.6f, _postCrashCooldown);
    }

    private void TickRecover()
    {
        FacePlanar(PlanarDirectionToPlayer(), _rotationSpeed * 0.5f);
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            SetGenericMovement(true);
            _state = State.Chase;
        }
    }

    private void EnterOverheat()
    {
        _engaged = false;
        SetGenericMovement(false);
        _state = State.Overheat;
        _stateTimer = _overheatSeconds;
    }

    private void TickOverheat()
    {
        Vector3 dir = PlanarDirectionToPlayer();
        FacePlanar(dir, _rotationSpeed * 0.4f);
        if (dir.sqrMagnitude > 0.0001f)
            transform.position += dir * (_overheatSpeed * Time.deltaTime);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            SetGenericMovement(true);
            _state = State.Chase;
        }
    }
}
