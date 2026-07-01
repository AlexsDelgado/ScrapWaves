using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one or more bosses on each Overheat. From the configured cycle onward it can spawn several at once.
/// When the last boss in the phase dies, notifies <see cref="OverheatManager"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-31)]
public class BossManager : MonoBehaviour
{
    [SerializeField, Tooltip("Overheat manager (same phase duration as the player's timer).")]
    private OverheatManager _overheatManager;

    [SerializeField, Tooltip("Root boss prefab: EnemyHealth + EnemyFollow; no SwarmPooledEnemy.")]
    private GameObject _bossPrefab;

    [SerializeField, Tooltip("Second boss (Boss_2). Alternates with the first on each even Overheat. Empty = always use the first.")]
    private GameObject _secondBossPrefab;

    [SerializeField, Tooltip("Bosses only appear on even Overheats (2nd, 4th...). Odd Overheats spawn the elite wave.")]
    private bool _spawnOnlyOnEvenCycles = true;

    [SerializeField, Min(1), Tooltip("Max health applied on spawn (replaces the prefab value).")]
    private int _bossMaxHealth = 400;

    [SerializeField, Min(2f), Tooltip("XZ distance from the player where each boss appears.")]
    private float _spawnDistance = 12f;

    [SerializeField, Min(1), Tooltip("Overheat number (1 = first, 2 = second...). Several bosses spawn at once on this cycle.")]
    private int _multiBossOverheatCycle = 3;

    [SerializeField, Min(2), Tooltip("How many bosses to spawn on the multi-boss cycle (for example, 2 on the 3rd Overheat).")]
    private int _bossCountOnMultiCycle = 2;

    [SerializeField, Tooltip("Log spawn, success, and failure.")]
    private bool _logState;

    [Header("Ground Spawn")]
    [SerializeField, Tooltip("Primary downward raycast (0 in Awake = Terrain only).")]
    private LayerMask _groundRaycastMask;

    [SerializeField, Tooltip("If the primary raycast fails (0 in Awake = Terrain + Default).")]
    private LayerMask _fallbackGroundRaycastMask;

    [SerializeField, Tooltip("Solid colliders for overlap (0 in Awake = Terrain + Default).")]
    private LayerMask _overlapSolidMask;

    [SerializeField, Min(1f), Tooltip("Height above the reference Y for the downward raycast.")]
    private float _raycastStartHeight = 48f;

    [SerializeField, Min(1f), Tooltip("Maximum length of the downward raycast.")]
    private float _raycastMaxDistance = 220f;

    [SerializeField, Min(0f), Tooltip("Prefer surfaces with |Y - reference| <= this value. 0 = no preference.")]
    private float _maxAbsSpawnSurfaceDeltaY = 3.5f;

    [SerializeField, Min(0f), Tooltip("Separation from the hit along the normal.")]
    private float _surfaceSeparation = 0.02f;

    [SerializeField, Min(0), Tooltip("Maximum anti-interior projection steps.")]
    private int _maxProjectionIterations = 14;

    [SerializeField, Min(0f), Tooltip("Vertical step per iteration.")]
    private float _resolveStepUp = 0.08f;

    [SerializeField, Min(0f), Tooltip("Horizontal step per iteration.")]
    private float _resolveStepOut = 0.06f;

    private readonly List<EnemyHealth> _activeBosses = new List<EnemyHealth>(4);
    private readonly Dictionary<EnemyHealth, Action> _onBossDiedHandlers = new Dictionary<EnemyHealth, Action>();

    private int _overheatCycleIndex;
    private bool _exitPhaseActive;

    /// <summary>Every time a boss is defeated (for global victory in <see cref="GameManager"/>).</summary>
    public event Action OnBossDefeated;

    /// <summary>Active boss spawn or death (bar and objective HUD).</summary>
    public event Action OnActiveBossesChanged;

    /// <summary>Living bosses in the current Overheat phase.</summary>
    public IReadOnlyList<EnemyHealth> ActiveBosses => _activeBosses;

    public bool HasActiveBosses => _activeBosses.Count > 0;

    /// <summary>First living boss (health bar and offscreen arrow).</summary>
    public EnemyHealth PrimaryBoss
    {
        get
        {
            for (int i = 0; i < _activeBosses.Count; i++)
            {
                EnemyHealth h = _activeBosses[i];
                if (h != null && h.CurrentHealth > 0)
                    return h;
            }

            return null;
        }
    }

    /// <summary>Started Overheat cycles (1-based during the current phase after incrementing).</summary>
    public int CurrentOverheatCycle => _overheatCycleIndex;

    public void SetExitPhaseActive(bool active) => _exitPhaseActive = active;

    private void Awake()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();

        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");
    }

    private void OnEnable()
    {
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

        DespawnAllBossesImmediate();
    }

    private void OnOverheatStarted()
    {
        _overheatCycleIndex++;
        DespawnAllBossesImmediate();

        if (_exitPhaseActive)
        {
            if (_logState)
                Debug.Log("BossManager: fase de salida activa; sin spawn de boss.", this);
            return;
        }

        // Los bosses solo en ciclos pares; los impares los cubre la oleada de elites.
        if (_spawnOnlyOnEvenCycles && (_overheatCycleIndex % 2 != 0))
        {
            if (_logState)
                Debug.Log($"BossManager: Overheat impar #{_overheatCycleIndex}; sin boss (turno de elites).", this);
            return;
        }

        GameObject bossPrefab = SelectBossPrefabForCurrentCycle();
        if (bossPrefab == null)
        {
            if (_logState)
                Debug.LogWarning("BossManager: asigna prefab de boss.", this);
            EndOverheatIfNoObjective();
            return;
        }

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
        {
            if (_logState)
                Debug.LogWarning("BossManager: no hay jugador; no se spawnea boss.", this);
            EndOverheatIfNoObjective();
            return;
        }

        int count = GetBossSpawnCountForCurrentCycle();
        float ringOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        for (int i = 0; i < count; i++)
        {
            float angle = ringOffset + (Mathf.PI * 2f * i) / Mathf.Max(1, count);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * _spawnDistance, 0f, Mathf.Sin(angle) * _spawnDistance);
            Vector3 ringPos = player.position + offset;

            GameObject go = Instantiate(bossPrefab, ringPos, Quaternion.identity);
            EnemyHealth health = go.GetComponent<EnemyHealth>();
            if (health == null)
            {
                if (_logState)
                    Debug.LogError("BossManager: el prefab debe tener EnemyHealth.", this);
                Destroy(go);
                continue;
            }

            CharacterController cc = go.GetComponent<CharacterController>();
            if (cc == null)
            {
                if (_logState)
                    Debug.LogError("BossManager: el prefab del boss debe tener CharacterController (EnemyFollow lo requiere).", this);
                Destroy(go);
                continue;
            }

            if (!SpawnGroundUtility.TryResolveFootPosition(
                    new Vector3(ringPos.x, 0f, ringPos.z),
                    go.transform,
                    cc,
                    ringPos.y,
                    _maxAbsSpawnSurfaceDeltaY,
                    _groundRaycastMask,
                    _fallbackGroundRaycastMask,
                    _overlapSolidMask,
                    _raycastStartHeight,
                    _raycastMaxDistance,
                    _surfaceSeparation,
                    _maxProjectionIterations,
                    _resolveStepUp,
                    _resolveStepOut,
                    out Vector3 foot))
            {
                if (_logState)
                    Debug.LogWarning("BossManager: no se encontró suelo bajo el punto de spawn; se spawnea igual (sin snap a suelo).", this);

                // Fallback al comportamiento anterior: no bloquear el spawn del boss si el suelo no está debajo
                // (por ejemplo, anillo cae fuera del terreno o hay vacío bajo ese XZ).
                go.transform.position = ringPos;
            }
            else
            {
                go.transform.position = foot;
            }

            health.ApplyConfiguredMaxHealth(_bossMaxHealth);

            EnemyHealth captured = health;
            Action handler = () => OnBossInstanceDied(captured);
            _onBossDiedHandlers[health] = handler;
            health.OnDied += handler;

            if (!go.activeSelf)
                go.SetActive(true);

            _activeBosses.Add(health);

            Vector3 toPlayer = player.position - go.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        }

        if (_logState && _activeBosses.Count > 0)
            Debug.Log($"Boss spawn x{_activeBosses.Count} (ciclo Overheat #{_overheatCycleIndex})", this);

        NotifyActiveBossesChanged();

        // Sin temporizador: si no se pudo spawnear ningún boss, no dejar el Overheat colgado.
        if (_activeBosses.Count == 0)
            EndOverheatIfNoObjective();
    }

    /// <summary>
    /// Anti-softlock: como el Overheat ya no termina por tiempo, si este ciclo no logró
    /// generar objetivo (sin boss), lo cierra para no bloquear el loop.
    /// </summary>
    private void EndOverheatIfNoObjective()
    {
        if (_overheatManager != null && _overheatManager.IsOverheating)
            _overheatManager.NotifyOverheatObjectiveCleared();
    }

    private int GetBossSpawnCountForCurrentCycle()
    {
        if (_overheatCycleIndex == _multiBossOverheatCycle && _bossCountOnMultiCycle > 1)
            return _bossCountOnMultiCycle;
        return 1;
    }

    /// <summary>
    /// Elige el boss del ciclo actual. Si hay segundo prefab, alterna entre ambos en
    /// cada aparición par: 2.º Overheat -> boss 1, 4.º -> boss 2, 6.º -> boss 1…
    /// </summary>
    private GameObject SelectBossPrefabForCurrentCycle()
    {
        if (_secondBossPrefab == null)
            return _bossPrefab;
        if (_bossPrefab == null)
            return _secondBossPrefab;

        // appearanceIndex 1-based para los ciclos pares (2->1, 4->2, 6->3…).
        int appearanceIndex = _spawnOnlyOnEvenCycles
            ? _overheatCycleIndex / 2
            : _overheatCycleIndex;

        return (appearanceIndex % 2 == 1) ? _bossPrefab : _secondBossPrefab;
    }

    private void OnBossInstanceDied(EnemyHealth health)
    {
        if (health == null)
            return;

        if (_onBossDiedHandlers.TryGetValue(health, out Action handler))
        {
            health.OnDied -= handler;
            _onBossDiedHandlers.Remove(health);
        }

        _activeBosses.Remove(health);
        OnBossDefeated?.Invoke();
        NotifyActiveBossesChanged();

        if (health.gameObject != null)
            Destroy(health.gameObject, 0.05f);

        if (_activeBosses.Count == 0 && _overheatManager != null && _overheatManager.IsOverheating)
            _overheatManager.NotifyBossDefeatedEarly();

        if (_logState)
            Debug.Log("Boss derrotado.", this);
    }

    private void OnOverheatFinished(OverheatEndReason reason)
    {
        if (reason == OverheatEndReason.TimeExpired && _activeBosses.Count > 0 && _logState)
            Debug.Log("Tiempo de Overheat agotado: fallo (boss(es) sigue(n) vivo(s)).", this);

        DespawnAllBossesImmediate();
    }

    private void DespawnAllBossesImmediate()
    {
        for (int i = _activeBosses.Count - 1; i >= 0; i--)
        {
            EnemyHealth h = _activeBosses[i];
            if (h == null)
                continue;

            if (_onBossDiedHandlers.TryGetValue(h, out Action handler))
            {
                h.OnDied -= handler;
                _onBossDiedHandlers.Remove(h);
            }

            if (h.gameObject != null)
                Destroy(h.gameObject);
        }

        _activeBosses.Clear();
        NotifyActiveBossesChanged();
    }

    private void NotifyActiveBossesChanged() => OnActiveBossesChanged?.Invoke();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_bossMaxHealth < 1)
            _bossMaxHealth = 1;
        if (_spawnDistance < 2f)
            _spawnDistance = 2f;
        if (_multiBossOverheatCycle < 1)
            _multiBossOverheatCycle = 1;
        if (_bossCountOnMultiCycle < 2)
            _bossCountOnMultiCycle = 2;
    }
#endif
}
