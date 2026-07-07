using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// En los Overheat IMPARES (1.º, 3.º, 5.º…) spawnea una oleada de elites alrededor
/// del jugador. En los Overheat PARES no hace nada (esos los cubren los bosses del
/// <see cref="BossManager"/>).
///
/// Mantiene su propio contador de ciclos (se sincroniza con BossManager porque ambos
/// se suscriben a <see cref="OverheatManager.OnOverheatStarted"/> e incrementan una
/// vez por evento). Limpia los elites al terminar el Overheat.
/// </summary>
[DisallowMultipleComponent]
public class OverheatEliteWaveSpawner : MonoBehaviour
{
    [Serializable]
    public class EliteEntry
    {
        [Tooltip("Prefab elite a spawnear (Slime_Elite, Drone_Elite, Chaser_Elite…).")]
        public GameObject Prefab;

        [Min(0), Tooltip("Cantidad de este elite por oleada.")]
        public int Count = 3;
    }

    [Header("Oleada de elites (Overheat impar)")]
    [SerializeField, Tooltip("Variantes elite y cuántas spawnear de cada una.")]
    private EliteEntry[] _elites = Array.Empty<EliteEntry>();

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType.")]
    private OverheatManager _overheatManager;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType. Aplica stats de dificultad al spawnear.")]
    private DifficultyManager _difficultyManager;

    [SerializeField, Tooltip("Loguear la oleada.")]
    private bool _logState;

    [Header("Colocación (anillo alrededor del jugador)")]
    [SerializeField, Min(1f)] private float _minSpawnRadius = 8f;
    [SerializeField, Min(1f)] private float _maxSpawnRadius = 16f;
    [SerializeField] private float _spawnHeightOffset = 0f;

    [SerializeField, Min(1), Tooltip("Intentos de colocación con snap a suelo por elite antes del fallback.")]
    private int _placementAttemptsPerElite = 8;

    [SerializeField, Tooltip("Si todos los intentos con snap fallan, instanciar igual sin snap (garantiza la oleada).")]
    private bool _guaranteeSpawn = true;

    [Header("Spawn en suelo")]
    [SerializeField] private LayerMask _groundRaycastMask;
    [SerializeField] private LayerMask _fallbackGroundRaycastMask;
    [SerializeField] private LayerMask _overlapSolidMask;
    [SerializeField, Min(1f)] private float _raycastStartHeight = 48f;
    [SerializeField, Min(1f)] private float _raycastMaxDistance = 220f;
    [SerializeField, Min(0f)] private float _maxAbsSpawnSurfaceDeltaY = 3.5f;
    [SerializeField, Min(0f)] private float _surfaceSeparation = 0.02f;
    [SerializeField, Min(0)] private int _maxProjectionIterations = 14;
    [SerializeField, Min(0f)] private float _resolveStepUp = 0.08f;
    [SerializeField, Min(0f)] private float _resolveStepOut = 0.06f;

    private readonly List<GameObject> _spawned = new(32);
    private readonly List<Transform> _aliveEliteTransformBuffer = new(32);
    private readonly Dictionary<EnemyHealth, Action> _onEliteDiedHandlers = new(32);
    private int _aliveEliteCount;
    private bool _waveActive;
    private int _cycleIndex;
    private bool _exitPhaseDisabled;

    public int EliteWaveTotal { get; private set; }
    public int ElitesRemaining => _aliveEliteCount;
    public bool IsEliteWaveActive => _waveActive;

    public event Action OnEliteWaveProgressChanged;

    public void SetExitPhaseDisabled(bool disabled) => _exitPhaseDisabled = disabled;

    private void Awake()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_difficultyManager == null)
            _difficultyManager = FindAnyObjectByType<DifficultyManager>();

        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");
    }

    private void OnEnable()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += OnOverheatStarted;
            _overheatManager.OnOverheatFinished += OnOverheatFinished;
        }
    }

    private void OnDisable()
    {
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= OnOverheatStarted;
            _overheatManager.OnOverheatFinished -= OnOverheatFinished;
        }

        ClearSpawned();
    }

    private void OnOverheatStarted()
    {
        _cycleIndex++;

        if (_exitPhaseDisabled)
        {
            if (_logState)
                Debug.Log("[EliteWave] Fase de salida activa; sin oleada de elites.", this);
            return;
        }

        // Pares -> bosses (BossManager). Impares -> oleada de elites.
        if (_cycleIndex % 2 == 0)
            return;

        SpawnEliteWave();
    }

    private void OnOverheatFinished(OverheatEndReason reason)
    {
        ClearSpawned();
    }

    private void SpawnEliteWave()
    {
        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
        {
            if (_logState)
                Debug.LogWarning("[EliteWave] No hay jugador; no se spawnean elites.", this);
            EndOverheatIfNoObjective();
            return;
        }

        if (_elites == null || _elites.Length == 0)
        {
            if (_logState)
                Debug.LogWarning("[EliteWave] No hay prefabs elite asignados.", this);
            EndOverheatIfNoObjective();
            return;
        }

        int totalPlanned = 0;
        foreach (EliteEntry entry in _elites)
        {
            if (entry == null || entry.Prefab == null || entry.Count <= 0)
                continue;
            totalPlanned += entry.Count;
        }

        EliteWaveTotal = totalPlanned;

        int spawned = 0;
        foreach (EliteEntry entry in _elites)
        {
            if (entry == null || entry.Prefab == null || entry.Count <= 0)
                continue;

            for (int i = 0; i < entry.Count; i++)
            {
                if (SpawnOneElite(player, entry.Prefab))
                    spawned++;
            }
        }

        // Solo gestionamos la condición de "derrotar a todos" si hay elites con vida.
        _waveActive = _aliveEliteCount > 0;

        if (_logState)
            Debug.Log($"[EliteWave] Overheat impar #{_cycleIndex}: spawneados {spawned} elites (vivos rastreados: {_aliveEliteCount}).", this);

        NotifyEliteWaveProgressChanged();

        // Sin temporizador: si no quedó ningún elite rastreable, no dejar el Overheat colgado.
        if (!_waveActive)
            EndOverheatIfNoObjective();
    }

    public IReadOnlyList<Transform> GetAliveEliteTransforms()
    {
        _aliveEliteTransformBuffer.Clear();
        for (int i = 0; i < _spawned.Count; i++)
        {
            GameObject go = _spawned[i];
            if (go == null)
                continue;

            EnemyHealth health = go.GetComponent<EnemyHealth>();
            if (health != null && health.CurrentHealth <= 0)
                continue;

            _aliveEliteTransformBuffer.Add(go.transform);
        }

        return _aliveEliteTransformBuffer;
    }

    /// <summary>
    /// Spawnea un elite garantizado: reintenta varias direcciones con snap a suelo y,
    /// si todas fallan, lo instancia igual sin snap (como hace <see cref="BossManager"/>)
    /// para que la oleada nunca quede vacía.
    /// </summary>
    private bool SpawnOneElite(Transform player, GameObject prefab)
    {
        int attempts = Mathf.Max(1, _placementAttemptsPerElite);
        for (int a = 0; a < attempts; a++)
        {
            int dir = OrbitalSpawnPlacement.PickRandomDirectionIndex();
            if (OrbitalSpawnPlacement.TrySpawnAtOrbitalPoint(
                    player,
                    prefab,
                    dir,
                    _minSpawnRadius,
                    _maxSpawnRadius,
                    _spawnHeightOffset,
                    _groundRaycastMask,
                    _fallbackGroundRaycastMask,
                    _overlapSolidMask,
                    _raycastStartHeight,
                    _raycastMaxDistance,
                    _maxAbsSpawnSurfaceDeltaY,
                    _surfaceSeparation,
                    _maxProjectionIterations,
                    _resolveStepUp,
                    _resolveStepOut,
                    out GameObject instance,
                    out _,
                    out _))
            {
                RegisterSpawned(instance);
                return true;
            }
        }

        if (!_guaranteeSpawn)
            return false;

        // Fallback sin snap a suelo: instanciar en un punto del anillo a la altura del jugador.
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float radius = UnityEngine.Random.Range(_minSpawnRadius, _maxSpawnRadius);
        Vector3 pos = player.position + new Vector3(
            Mathf.Cos(angle) * radius,
            _spawnHeightOffset,
            Mathf.Sin(angle) * radius);

        GameObject fallback;
        if (EnemyPoolRegistry.UseEnemyPool
            && EnemyPoolRegistry.Instance != null
            && EnemyPoolRegistry.Instance.TryGet(prefab, out fallback))
        {
            fallback.transform.SetPositionAndRotation(pos, Quaternion.identity);
        }
        else
        {
            fallback = Instantiate(prefab, pos, Quaternion.identity);
            EnemyPoolProfiler.RegisterInstantiate();
        }

        Vector3 toPlayer = player.position - fallback.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            fallback.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

        if (!fallback.activeSelf)
            fallback.SetActive(true);

        RegisterSpawned(fallback);
        return true;
    }

    private void RegisterSpawned(GameObject instance)
    {
        _difficultyManager?.ApplySpawnModifiers(instance);
        _spawned.Add(instance);
        TrackElite(instance);
    }

    /// <summary>
    /// Anti-softlock: como el Overheat ya no termina por tiempo, si la oleada no generó
    /// elites rastreables, cierra el Overheat para no bloquear el loop.
    /// </summary>
    private void EndOverheatIfNoObjective()
    {
        if (_overheatManager != null && _overheatManager.IsOverheating)
            _overheatManager.NotifyOverheatObjectiveCleared();
    }

    private void TrackElite(GameObject instance)
    {
        EnemyHealth health = instance.GetComponent<EnemyHealth>();
        if (health == null)
            return;

        EnemyHealth captured = health;
        Action handler = () => OnEliteDied(captured);
        _onEliteDiedHandlers[health] = handler;
        health.OnDied += handler;
        _aliveEliteCount++;
    }

    private void OnEliteDied(EnemyHealth health)
    {
        if (health != null && _onEliteDiedHandlers.TryGetValue(health, out Action handler))
        {
            health.OnDied -= handler;
            _onEliteDiedHandlers.Remove(health);
        }

        _aliveEliteCount = Mathf.Max(0, _aliveEliteCount - 1);
        NotifyEliteWaveProgressChanged();

        if (!_waveActive || _aliveEliteCount > 0)
            return;

        // Último elite derrotado: el Overheat termina como éxito (igual que con el boss).
        _waveActive = false;
        if (_logState)
            Debug.Log("[EliteWave] Todos los elites derrotados; fin de Overheat.", this);

        if (_overheatManager != null && _overheatManager.IsOverheating)
            _overheatManager.NotifyOverheatObjectiveCleared();
    }

    /// <summary>Devuelve al pool o destruye los elites que spawneó esta oleada.</summary>
    public void ClearSpawned()
    {
        foreach (KeyValuePair<EnemyHealth, Action> kv in _onEliteDiedHandlers)
        {
            if (kv.Key != null)
                kv.Key.OnDied -= kv.Value;
        }
        _onEliteDiedHandlers.Clear();
        _aliveEliteCount = 0;
        _waveActive = false;

        if (EnemyPoolRegistry.UseEnemyPool && EnemyPoolRegistry.Instance != null)
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                GameObject go = _spawned[i];
                if (go != null && go.activeSelf)
                    EnemyPoolRegistry.Instance.Release(go);
            }
        }
        else
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i]);
                    EnemyPoolProfiler.RegisterDestroy();
                }
            }
        }

        _spawned.Clear();
        EliteWaveTotal = 0;
        NotifyEliteWaveProgressChanged();
    }

    private void NotifyEliteWaveProgressChanged() => OnEliteWaveProgressChanged?.Invoke();
}
