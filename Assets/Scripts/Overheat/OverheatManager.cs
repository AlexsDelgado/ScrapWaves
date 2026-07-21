using System;
using UnityEngine;

/// <summary>
/// Reason the Overheat phase ends (buff + boss window).
/// </summary>
public enum OverheatEndReason
{
    TimeExpired,
    BossDefeated,
    Interrupted
}

/// <summary>
/// When Heat is filled (<see cref="HeatManager.OnOverheat"/>), enters Overheat: applies a fire-rate multiplier to <see cref="PlayerStats"/>
/// and then resets Heat to <see cref="_heatAfterOverheat"/>.
/// There is no timer: Overheat stays active until the player completes the objective
/// (defeat the boss or bosses on even cycles, or all elites on odd cycles), then
/// <see cref="BossManager"/> / <see cref="OverheatEliteWaveSpawner"/> call
/// <see cref="NotifyOverheatObjectiveCleared"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-32)]
public class OverheatManager : MonoBehaviour
{
    [SerializeField, Tooltip("If empty, HeatManager.GetInstance() is used.")]
    private HeatManager _heatManager;

    [SerializeField, Tooltip("Player stats (same GameObject or explicit reference).")]
    private PlayerStats _playerStats;

    [SerializeField, Min(0.1f), Tooltip("DEPRECATED: Overheat no longer ends by time; it lasts until the objective is cleared. Kept for compatibility.")]
    private float _overheatDuration = 5f;

    [SerializeField, Min(0.01f), Tooltip("Fire-rate multiplier during Overheat (2 = double speed, roughly half interval).")]
    private float _fireRateMultiplier = 1.5f;

    [SerializeField, Min(0f), Tooltip("Heat mínimo residual al terminar Overheat. Si es 0, se usa el primer tramo (80% visual) para pausar spawns.")]
    private float _heatAfterOverheat = 0f;

    [SerializeField, Tooltip("Si true, limpia todo el swarm al terminar Overheat (legacy). Por defecto false: los enemigos comunes quedan.")]
    private bool _clearSwarmOnOverheatEnd;

    [SerializeField, Tooltip("Log Overheat start and end.")]
    private bool _logState;

    [SerializeField, Tooltip("Common enemy pool; empty = FindAnyObjectByType when Overheat ends.")]
    private SwarmEnemyPool _swarmEnemyPool;

    private bool _isOverheating;
    private bool _permanentOverheat;

    public bool IsOverheating => _isOverheating;
    public bool IsPermanentOverheat => _permanentOverheat;

    /// <summary>No timer: 0 while Overheat depends on the objective.</summary>
    public float OverheatTimeRemaining => 0f;

    /// <summary>No timer: 1 while Overheat is active, otherwise 0.</summary>
    public float NormalizedOverheatTimeRemaining => _isOverheating ? 1f : 0f;

    /// <summary>Configured duration (deprecated: there is no timer anymore).</summary>
    public float ConfiguredOverheatDuration => _overheatDuration;

    /// <summary>When entering Overheat (buff active and timer started).</summary>
    public event Action OnOverheatStarted;

    /// <summary>When leaving Overheat; includes boss success, time expired, or interruption.</summary>
    public event Action<OverheatEndReason> OnOverheatFinished;

    private void Awake()
    {
        if (_heatManager == null)
            _heatManager = HeatManager.GetInstance();
        if (_playerStats == null)
            _playerStats = FindAnyObjectByType<PlayerStats>();
        if (_swarmEnemyPool == null)
            _swarmEnemyPool = FindAnyObjectByType<SwarmEnemyPool>();
    }

    private void OnEnable()
    {
        if (_heatManager != null)
            _heatManager.OnOverheat += OnMaxHeatReached;
    }

    private void OnDisable()
    {
        if (_heatManager != null)
            _heatManager.OnOverheat -= OnMaxHeatReached;

        if (_isOverheating)
            EndOverheat(OverheatEndReason.Interrupted);
    }

    /// <summary>Si el boss muere, termina Overheat como éxito.</summary>
    public void NotifyBossDefeatedEarly()
    {
        NotifyOverheatObjectiveCleared();
    }

    /// <summary>
    /// El objetivo de la fase de Overheat se completó (boss derrotado en ciclos pares,
    /// o todos los elites derrotados en ciclos impares): termina el Overheat como éxito.
    /// </summary>
    public void NotifyOverheatObjectiveCleared()
    {
        if (!_isOverheating || _permanentOverheat)
            return;

        EndOverheat(OverheatEndReason.BossDefeated);
    }

    /// <summary>Tras reunir todas las llaves: overheat que no termina hasta victoria o game over.</summary>
    public void EnterPermanentOverheat()
    {
        _permanentOverheat = true;

        if (_isOverheating)
            return;

        _isOverheating = true;
        OverheatSwarmBoost.SetIntensity(false);
        _heatManager?.StopPostOverheatDecay();

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(_fireRateMultiplier);
        else if (_logState)
            Debug.LogWarning("OverheatManager: no PlayerStats found; fire-rate buff was not applied.", this);

        if (_logState)
            Debug.Log($"Overheat permanente (salida) x{_fireRateMultiplier:0.##} fire rate.", this);

        OnOverheatStarted?.Invoke();
    }

    private void OnMaxHeatReached()
    {
        if (_isOverheating)
            return;

        _isOverheating = true;
        OverheatSwarmBoost.SetIntensity(false);
        _heatManager?.StopPostOverheatDecay();

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(_fireRateMultiplier);
        else if (_logState)
            Debug.LogWarning("OverheatManager: no hay PlayerStats; no se aplica buff de cadencia.", this);

        if (_logState)
            Debug.Log($"Overheat started (no timer, x{_fireRateMultiplier:0.##} fire rate; lasts until the objective is cleared)", this);

        OnOverheatStarted?.Invoke();
    }

    private void EndOverheat(OverheatEndReason reason)
    {
        if (_permanentOverheat && reason != OverheatEndReason.Interrupted)
            return;

        _isOverheating = false;
        _permanentOverheat = false;
        OverheatSwarmBoost.SetIntensity(false);

        if (_swarmEnemyPool == null)
            _swarmEnemyPool = FindAnyObjectByType<SwarmEnemyPool>();

        if (_clearSwarmOnOverheatEnd)
            EnemyLifecycleCoordinator.ClearAllForQa();
        else
            EnemyLifecycleCoordinator.OnOverheatEnded();

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(1f);

        if (_heatManager != null)
        {
            _heatManager.ApplyEscalationAfterOverheat();
            // Residual por encima del primer tramo: OrbitalSpawner pausa hasta que decay baje del umbral.
            // Si _heatAfterOverheat > 0 se respeta; si no, ~90% de la barra (mitad del 2.º tramo).
            float residual = _heatAfterOverheat > 0f
                ? _heatAfterOverheat
                : _heatManager.PointsFirstSegment + _heatManager.PointsSecondSegment * 0.5f;
            residual = Mathf.Clamp(residual, 0f, _heatManager.MaxHeat);
            _heatManager.BeginPostOverheatCooldown(residual);
        }

        if (_logState)
            Debug.Log($"Overheat terminado ({reason}); escalado aplicado; swarm no limpio; heat residual con decay.", this);

        OnOverheatFinished?.Invoke(reason);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_overheatDuration < 0.1f)
            _overheatDuration = 0.1f;
        if (_fireRateMultiplier < 0.01f)
            _fireRateMultiplier = 0.01f;
        if (_heatAfterOverheat < 0f)
            _heatAfterOverheat = 0f;
    }
#endif
}
