using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Continuous orbital spawner for the final loop. On each tick it rolls the roulette
/// (<see cref="EnemySpawnRoulette"/>) to decide which enemy type and how many enemies
/// appear, then places them on random orbital points around the player
/// (<see cref="OrbitalSpawnPlacement"/>), always following the player. Cadence scales
/// with <see cref="DifficultyManager"/> and Overheat state.
///
/// Replaces the old <c>SwarmSpawner</c> (which only pulled slimes from the pool) as
/// the constant spawner around the player.
/// </summary>
public class OrbitalSpawner : MonoBehaviour
{
    [Header("Roulette")]
    [SerializeField] private EnemySpawnRouletteConfig _config;

    [SerializeField, Tooltip("Vacío = PlayerMovement.PlayerTransform.")]
    private Transform _player;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType. Escala intervalo y cantidad.")]
    private DifficultyManager _difficultyManager;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType. Al terminar el Overheat limpia los enemigos del orbital (reset del ciclo).")]
    private OverheatManager _overheatManager;

    [Header("Cadencia (SpawnCooldown * dificultad * overheat)")]
    [SerializeField, Min(0.05f)] private float _spawnInterval = 1.5f;

    [SerializeField, Tooltip("Empieza a spawnear apenas arranca la escena.")]
    private bool _spawnOnStart = true;

    [SerializeField, Min(1)] private int _maxActiveEnemies = 300;

    [Header("Colocación orbital")]
    [SerializeField, Min(0f)] private float _minSpawnRadius = 10f;

    [SerializeField, Min(0f)] private float _maxSpawnRadius = 20f;

    [SerializeField] private float _spawnHeightOffset;

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

    private EnemySpawnRoulette _roulette;
    private float _nextSpawnTime;
    private float _runStartTime;
    private readonly List<GameObject> _spawned = new(256);

    public int ActiveSpawnedCount
    {
        get
        {
            PruneSpawned();
            return _spawned.Count;
        }
    }

    private void Awake()
    {
        if (_difficultyManager == null)
            _difficultyManager = FindAnyObjectByType<DifficultyManager>();

        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");

        if (_config != null)
        {
            _roulette = new EnemySpawnRoulette(_config);
            EnemyPoolRegistry registry = FindAnyObjectByType<EnemyPoolRegistry>();
            if (registry != null)
                registry.RegisterFromRoulette(_config);
            else
                EnemyPoolRegistry.EnsureExists();
        }
    }

    private void OnEnable()
    {
        _runStartTime = Time.timeSinceLevelLoad;
        _nextSpawnTime = _spawnOnStart ? 0f : Time.time + EffectiveSpawnInterval();

        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_overheatManager != null)
            _overheatManager.OnOverheatFinished += OnOverheatFinished;
    }

    private void OnDisable()
    {
        if (_overheatManager != null)
            _overheatManager.OnOverheatFinished -= OnOverheatFinished;
    }

    private void OnOverheatFinished(OverheatEndReason reason)
    {
        ClearSpawned();
    }

    private void OnValidate()
    {
        if (_maxSpawnRadius < _minSpawnRadius)
        {
            (_minSpawnRadius, _maxSpawnRadius) = (_maxSpawnRadius, _minSpawnRadius);
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        if (_roulette == null || _config == null)
            return;

        if (_player == null)
            _player = PlayerMovement.PlayerTransform;
        if (_player == null)
            return;

        if (Time.time < _nextSpawnTime)
            return;

        _nextSpawnTime = Time.time + EffectiveSpawnInterval();
        SpawnRouletteWave();
    }

    private float EffectiveSpawnInterval()
    {
        float scale = _difficultyManager != null ? _difficultyManager.GetSpawnIntervalScale() : 1f;
        return Mathf.Max(0.05f, _spawnInterval * scale);
    }

    private float RunTimeSeconds => Time.timeSinceLevelLoad - _runStartTime;

    private void SpawnRouletteWave()
    {
        EnemySpawnRollResult roll = _roulette.Roll(RunTimeSeconds);
        if (roll.Prefab == null)
            return;

        float diffCount = _difficultyManager != null ? _difficultyManager.GetSpawnCountMultiplier() : 1f;
        int batch = Mathf.Max(1, Mathf.RoundToInt(roll.BatchSize * diffCount * OverheatSwarmBoost.SpawnWaveMultiplier));

        for (int i = 0; i < batch; i++)
        {
            if (EnemyRegistry.ActiveCount >= _maxActiveEnemies)
                break;

            int dir = OrbitalSpawnPlacement.PickRandomDirectionIndex();
            if (OrbitalSpawnPlacement.TrySpawnAtOrbitalPoint(
                    _player,
                    roll.Prefab,
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
                _difficultyManager?.ApplySpawnModifiers(instance);
                _spawned.Add(instance);
            }
        }
    }

    /// <summary>Devuelve al pool o destruye enemigos rastreados (QA / fin de Overheat).</summary>
    public void ClearSpawned()
    {
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
    }

    private void PruneSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] == null)
                _spawned.RemoveAt(i);
        }
    }
}
