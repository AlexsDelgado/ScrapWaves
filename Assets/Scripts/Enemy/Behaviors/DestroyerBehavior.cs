using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss Destroyer: Hunt (camina lento, dispara misiles seeking) y Suction (al cruzar 75%, 50% y
/// 25% de vida, una vez cada umbral; inmune total, succiona jugador y swarm hacia la boca, come
/// enemigos y se cura). Ver docs/boss-destroyer.md para el diseño completo.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class DestroyerBehavior : EnemyBehaviorBase
{
    private enum State
    {
        Hunt,
        Suction
    }

    [Header("Fase de succión")]
    [SerializeField, Tooltip("Umbrales de vida (CurrentHealth/MaxHealth) que disparan succión, en orden descendente. Cada uno se consume una sola vez.")]
    private float[] _suctionHealthThresholds = { 0.75f, 0.50f, 0.25f };

    [Header("Hunt - Misiles")]
    [SerializeField] private Transform _missileMuzzle;
    [SerializeField] private GameObject _missilePrefab;
    [SerializeField, Min(0.1f)] private float _missileInterval = 1.2f;
    [SerializeField, Min(1)] private int _missileDamage = 12;
    [SerializeField, Min(0.1f)] private float _missileSpeed = 10f;
    [SerializeField, Min(0f)] private float _missileTurnRateDegPerSec = 90f;

    [Header("Succión - Boca y pull")]
    [SerializeField] private Transform _mouth;
    [SerializeField] private DestroyerMouthWeakPoint _weakPoint;
    [SerializeField, Min(0f), Tooltip("Aceleración continua de succión aplicada al jugador (PlayerCombatHooks.TryPull).")]
    private float _playerPullAcceleration = 14f;
    [SerializeField, Min(0f), Tooltip("Unidades por segundo a las que se arrastra al swarm hacia la boca.")]
    private float _swarmPullSpeed = 6f;

    [Header("Succión - Comer swarm")]
    [SerializeField, Min(0f)] private float _eatRadius = 2.5f;
    [SerializeField, Range(0f, 1f), Tooltip("Porcentaje de MaxHealth curado por cada enemigo comido.")]
    private float _healPercentPerEnemyEaten = 0.02f;

    [Header("Succión - Tragar al jugador")]
    [SerializeField, Min(0f)] private float _swallowRadius = 2.2f;
    [SerializeField, Min(1)] private int _swallowDamage = 40;
    [SerializeField, Min(0f)] private float _swallowKnockback = 35f;
    [SerializeField, Range(0f, 1f), Tooltip("Porcentaje de MaxHealth curado al tragar al jugador.")]
    private float _healPercentOnSwallow = 0.15f;

    [Header("Succión - Weak point")]
    [SerializeField, Range(0f, 1f), Tooltip("Porcentaje de MaxHealth de daño al cuerpo cuando se destruye el weak point.")]
    private float _weakPointDestroyedDamagePercent = 0.20f;

    [Header("Succión - Seguridad")]
    [SerializeField, Min(1f), Tooltip("Si nadie llega a la boca ni se rompe el weak point, corta la succión igual (evita softlock).")]
    private float _maxSuctionDuration = 15f;

    private readonly List<Transform> _swarmScratch = new List<Transform>(64);

    private EnemyHealth _health;
    private State _state;
    private int _nextSuctionThresholdIndex;
    private float _missileTimer;
    private float _suctionTimer;

    protected override void Awake()
    {
        base.Awake();
        _health = GetComponent<EnemyHealth>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _state = State.Hunt;
        _nextSuctionThresholdIndex = 0;
        _missileTimer = _missileInterval;
        _suctionTimer = 0f;

        if (_weakPoint != null)
        {
            _weakPoint.Deactivate();
            _weakPoint.OnWeakPointDestroyed += HandleWeakPointDestroyed;
        }
    }

    private void OnDisable()
    {
        if (_weakPoint != null)
            _weakPoint.OnWeakPointDestroyed -= HandleWeakPointDestroyed;
    }

    private void Update()
    {
        if (Player == null || _health == null)
            return;

        switch (_state)
        {
            case State.Hunt:
                TickHunt();
                break;
            case State.Suction:
                TickSuction();
                break;
        }
    }

    private void TickHunt()
    {
        _missileTimer -= Time.deltaTime;
        if (_missileTimer <= 0f)
        {
            FireMissile();
            _missileTimer = _missileInterval;
        }

        if (ShouldBeginSuction())
            BeginSuction();
    }

    private void FireMissile()
    {
        if (_missilePrefab == null || _missileMuzzle == null)
            return;

        Vector3 dir = Player.position - _missileMuzzle.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;
        dir.Normalize();

        EnemySeekingMissile.Launch(_missilePrefab, _missileMuzzle.position, dir, Player, _missileDamage, _missileSpeed, _missileTurnRateDegPerSec);
    }

    private bool ShouldBeginSuction()
    {
        if (_suctionHealthThresholds == null || _suctionHealthThresholds.Length == 0)
            return false;

        if (_nextSuctionThresholdIndex >= _suctionHealthThresholds.Length || _health.MaxHealth <= 0)
            return false;

        float ratio = (float)_health.CurrentHealth / _health.MaxHealth;
        return ratio <= _suctionHealthThresholds[_nextSuctionThresholdIndex];
    }

    private void BeginSuction()
    {
        _nextSuctionThresholdIndex++;
        _state = State.Suction;
        _suctionTimer = 0f;

        SetGenericMovement(false);
        _health.SetInvincible(true, blockDot: true);

        if (_weakPoint != null)
            _weakPoint.Reactivate();
    }

    private void TickSuction()
    {
        _suctionTimer += Time.deltaTime;
        if (_suctionTimer >= _maxSuctionDuration)
        {
            EndSuction();
            return;
        }

        Vector3 mouthPos = _mouth != null ? _mouth.position : transform.position;

        PlayerCombatHooks.TryPull(mouthPos, _playerPullAcceleration);
        PullAndEatSwarm(mouthPos);
        CheckPlayerSwallowed(mouthPos);
    }

    private void PullAndEatSwarm(Vector3 mouthPos)
    {
        int count = EnemyRegistry.CollectActive(_swarmScratch);
        for (int i = 0; i < count; i++)
        {
            Transform t = _swarmScratch[i];
            if (t == null || t == transform || IsBossTransform(t))
                continue;

            Vector3 toMouth = mouthPos - t.position;
            float dist = toMouth.magnitude;
            if (dist <= _eatRadius)
            {
                TryEatEnemy(t);
                continue;
            }

            Vector3 dir = toMouth / Mathf.Max(dist, 0.0001f);
            float step = _swarmPullSpeed * Time.deltaTime;
            if (t.TryGetComponent(out CharacterController cc) && cc.enabled)
                cc.Move(dir * step);
            else
                t.position += dir * step;
        }
    }

    private void TryEatEnemy(Transform t)
    {
        EnemyHealth enemyHealth = t.GetComponentInParent<EnemyHealth>();
        if (enemyHealth == null || enemyHealth.CurrentHealth <= 0)
            return;

        if (!enemyHealth.ApplyDamage(enemyHealth.CurrentHealth))
            return;

        _health.Heal(Mathf.RoundToInt(_health.MaxHealth * _healPercentPerEnemyEaten));
    }

    private void CheckPlayerSwallowed(Vector3 mouthPos)
    {
        Vector3 toPlayer = Player.position - mouthPos;
        if (toPlayer.magnitude > _swallowRadius)
            return;

        PlayerHealth playerHealth = Player.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.TakeDamage(_swallowDamage);

        PlayerCombatHooks.TryPush(mouthPos, _swallowKnockback);
        _health.Heal(Mathf.RoundToInt(_health.MaxHealth * _healPercentOnSwallow));
        EndSuction();
    }

    private void HandleWeakPointDestroyed()
    {
        _health.SetInvincible(false);
        int damage = Mathf.Max(1, Mathf.RoundToInt(_health.MaxHealth * _weakPointDestroyedDamagePercent));
        _health.ApplyDamage(damage);
        EndSuction();
    }

    private void EndSuction()
    {
        if (_state != State.Suction)
            return;

        _state = State.Hunt;
        _missileTimer = _missileInterval;

        if (_weakPoint != null)
            _weakPoint.Deactivate();

        _health.SetInvincible(false);
        SetGenericMovement(true);
    }

    private static bool IsBossTransform(Transform t)
    {
        Transform root = t.root;
        return root != null && root.name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
