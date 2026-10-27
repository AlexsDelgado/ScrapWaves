using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawner de zona (emboscada). Se activa cuando el jugador entra al trigger:
/// spawnea una cantidad fija de un tipo de enemigo en un area propia (no ligada al
/// jugador) y se desactiva hasta el proximo ciclo de Overheat. Pensado para 3
/// variantes en prefab, una por tipo de enemigo, con <see cref="_spawnCount"/>
/// editable.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ZoneSpawner : MonoBehaviour
{
    [Header("Emboscada")]
    [SerializeField, Tooltip("Prefab del enemigo a spawnear (un tipo por zona).")]
    private GameObject _enemyPrefab;

    [SerializeField, Min(1), Tooltip("Cantidad de enemigos a spawnear en la zona al activarse.")]
    private int _spawnCount = 8;

    [SerializeField, Min(0.5f), Tooltip("Radio del area (alrededor de esta zona) donde aparecen los enemigos.")]
    private float _spawnAreaRadius = 6f;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType. Aplica stats de dificultad al spawnear.")]
    private DifficultyManager _difficultyManager;

    [Header("Re-arme")]
    [SerializeField, Tooltip("Se vuelve a armar al iniciar el siguiente ciclo de Overheat.")]
    private bool _rearmOnOverheat = true;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType.")]
    private OverheatManager _overheatManager;

    [Header("Spawn en suelo")]
    [SerializeField] private float _spawnHeightOffset = 2f;

    [SerializeField] private LayerMask _groundRaycastMask;

    [SerializeField] private LayerMask _fallbackGroundRaycastMask;

    [SerializeField] private LayerMask _overlapSolidMask;

    [SerializeField, Min(1f)] private float _raycastStartHeight = 48f;

    [SerializeField, Min(1f)] private float _raycastMaxDistance = 220f;

    [SerializeField, Min(0f)] private float _maxAbsSpawnSurfaceDeltaY = 6f;

    [SerializeField, Min(0f)] private float _surfaceSeparation = 0.02f;

    [SerializeField, Min(0)] private int _maxProjectionIterations = 14;

    [SerializeField, Min(0f)] private float _resolveStepUp = 0.08f;

    [SerializeField, Min(0f)] private float _resolveStepOut = 0.06f;

    private bool _armed = true;
    private readonly HashSet<Collider> _preparationEntries = new();
    private readonly List<GameObject> _spawned = new(16);

    public bool IsArmed => _armed;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

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
            if (_rearmOnOverheat) _overheatManager.OnOverheatStarted += Rearm;
            _overheatManager.OnOverheatFinished += ClearPreparationEntries;
        }
    }

    private void OnDisable()
    {
        _preparationEntries.Clear();
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= Rearm;
            _overheatManager.OnOverheatFinished -= ClearPreparationEntries;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (EntranceCinematic.StartupHeld || TutorialDirector.IsActive) return;
        if (!_armed)
            return;

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) return;
        if (!health.IsAlive || (GameManager.Instance != null && !GameManager.Instance.IsPlaying)) return;
        if (_overheatManager != null && _overheatManager.IsPreparing)
        {
            _preparationEntries.Add(other);
            return;
        }
        if (Time.timeScale <= 0f || GameplayPause.IsUiPaused) return;
        _preparationEntries.Clear();
        _armed = false;
        SpawnAmbush();
    }

    private void ClearPreparationEntries(OverheatEndReason reason) => _preparationEntries.Clear();

    private void OnTriggerStay(Collider other)
    {
        // Only retry entries held by preparation; ordinary rearming behavior is unchanged.
        if (_preparationEntries.Contains(other)) OnTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other) => _preparationEntries.Remove(other);

    private void SpawnAmbush()
    {
        if (_enemyPrefab == null)
        {
            Debug.LogWarning("[ZoneSpawner] _enemyPrefab is not assigned.", this);
            return;
        }

        for (int i = 0; i < _spawnCount; i++)
        {
            Vector3 desired = transform.position;
            bool clear = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 disc = Random.insideUnitCircle * _spawnAreaRadius;
                desired = transform.position + new Vector3(disc.x, _spawnHeightOffset, disc.y);
                if (EnemySpawnBlockVolume.Blocks(desired))
                    continue;

                clear = true;
                break;
            }

            if (!clear)
                continue;

            if (OrbitalSpawnPlacement.TrySpawnGrounded(
                    _enemyPrefab,
                    desired,
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
                    out _))
            {
                _difficultyManager?.ApplySpawnModifiers(instance);
                // In-world ambushes stay independent of the orbital cycle cap, even when
                // this pooled instance previously belonged to an orbital or objective spawn.
                EnemySpawnPopulationMember.RecordSpawn(instance, EnemySpawnSource.InWorld);
                _spawned.Add(instance);
            }
        }
    }

    /// <summary>Re-arma la emboscada (al iniciar el siguiente Overheat).</summary>
    public void Rearm()
    {
        _armed = true;
    }

    /// <summary>Devuelve al pool o destruye los enemigos que spawneo esta zona (QA / Clear all).</summary>
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _armed ? new Color(0.2f, 0.9f, 0.3f, 0.5f) : new Color(0.6f, 0.6f, 0.6f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, _spawnAreaRadius);
    }
}
