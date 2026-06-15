using System;
using UnityEngine;

/// <summary>
/// Motivo por el que termina la fase de Overheat (buff + ventana de boss).
/// </summary>
public enum OverheatEndReason
{
    TimeExpired,
    BossDefeated,
    Interrupted
}

/// <summary>
/// Al llenar el Heat (<see cref="HeatManager.OnOverheat"/>), entra en Overheat: aplica un multiplicador de cadencia al <see cref="PlayerStats"/>
/// y luego resetea el Heat a <see cref="_heatAfterOverheat"/>.
/// NO hay temporizador: el Overheat se mantiene hasta que el jugador completa el objetivo
/// (derrotar al/los boss en ciclos pares, o todos los elites en impares), momento en el que
/// <see cref="BossManager"/> / <see cref="OverheatEliteWaveSpawner"/> llaman a
/// <see cref="NotifyOverheatObjectiveCleared"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-32)]
public class OverheatManager : MonoBehaviour
{
    [SerializeField, Tooltip("Si está vacío, se usa HeatManager.GetInstance().")]
    private HeatManager _heatManager;

    [SerializeField, Tooltip("Stats del jugador (mismo GameObject o referencia explícita).")]
    private PlayerStats _playerStats;

    [SerializeField, Min(0.1f), Tooltip("EN DESUSO: el Overheat ya no termina por tiempo, dura hasta limpiar el objetivo. Se conserva por compatibilidad.")]
    private float _overheatDuration = 5f;

    [SerializeField, Min(0.01f), Tooltip("Multiplicador de cadencia de disparo durante Overheat (2 = el doble de rápido, intervalo ~mitad).")]
    private float _fireRateMultiplier = 1.5f;

    [SerializeField, Min(0f), Tooltip("Heat tras terminar Overheat (0 = vacío; sube de nuevo con kills).")]
    private float _heatAfterOverheat = 0f;

    [SerializeField, Tooltip("Loguear inicio y fin de Overheat.")]
    private bool _logState;

    [SerializeField, Tooltip("Pool de enemigos comunes; vacío = FindAnyObjectByType al terminar Overheat.")]
    private SwarmEnemyPool _swarmEnemyPool;

    private bool _isOverheating;
    private bool _permanentOverheat;

    public bool IsOverheating => _isOverheating;
    public bool IsPermanentOverheat => _permanentOverheat;

    /// <summary>Sin temporizador: 0 mientras el Overheat depende del objetivo.</summary>
    public float OverheatTimeRemaining => 0f;

    /// <summary>Sin temporizador: 1 mientras está activo el Overheat, 0 si no.</summary>
    public float NormalizedOverheatTimeRemaining => _isOverheating ? 1f : 0f;

    /// <summary>Duración configurada (en desuso: ya no hay temporizador).</summary>
    public float ConfiguredOverheatDuration => _overheatDuration;

    /// <summary>Al entrar en Overheat (buff activo y temporizador iniciado).</summary>
    public event Action OnOverheatStarted;

    /// <summary>Al salir de Overheat; incluye éxito por boss, tiempo agotado o interrupción.</summary>
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

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(_fireRateMultiplier);
        else if (_logState)
            Debug.LogWarning("OverheatManager: no hay PlayerStats; no se aplica buff de cadencia.", this);

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

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(_fireRateMultiplier);
        else if (_logState)
            Debug.LogWarning("OverheatManager: no hay PlayerStats; no se aplica buff de cadencia.", this);

        if (_logState)
            Debug.Log($"Overheat iniciado (sin timer, x{_fireRateMultiplier:0.##} fire rate; dura hasta limpiar el objetivo)", this);

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
        _swarmEnemyPool?.ReleaseAllActive();

        if (_playerStats != null)
            _playerStats.SetRuntimeFireRateMultiplier(1f);

        if (_heatManager != null)
        {
            _heatManager.ApplyEscalationAfterOverheat();
            float cap = _heatManager.MaxHeat;
            _heatManager.SetHeat(Mathf.Clamp(_heatAfterOverheat, 0f, cap));
        }

        if (_logState)
            Debug.Log($"Overheat terminado ({reason}); escalado de heat aplicado; enemigos del pool devueltos.", this);

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
