using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Banco de pruebas de QA para enemigos y spawn. Permite spawnear cada
/// <see cref="EnemySpawnKind"/> a mano, tirar la ruleta dinámica, conmutar el
/// spawner continuo, ver conteos en vivo y validar el tope de enemigos en pantalla.
/// Deja "hooks" de spawn por tipo listos para cuando se implementen los
/// comportamientos específicos de cada enemigo.
/// </summary>
[DisallowMultipleComponent]
public class EnemiesTestingHarness : MonoBehaviour
{
    [Header("Roulette")]
    [SerializeField] private EnemySpawnRouletteConfig _config;

    [SerializeField, Tooltip("Vacío = PlayerMovement.PlayerTransform")]
    private Transform _player;

    [Header("Colocación orbital")]
    [SerializeField, Min(0f)] private float _minSpawnRadius = 8f;

    [SerializeField, Min(0f)] private float _maxSpawnRadius = 18f;

    [SerializeField] private float _spawnHeightOffset;

    [Header("Tope de enemigos en pantalla (Combat Design: 300)")]
    [SerializeField, Min(1)] private int _maxActiveEnemies = 300;

    [Header("Spawner continuo")]
    [SerializeField, Tooltip("Spawner orbital basado en ruleta (loop final). Vacío = FindAnyObjectByType.")]
    private OrbitalSpawner _orbitalSpawner;

    [SerializeField, Tooltip("Spawner de oleadas legacy (solo slimes del pool). Vacío = FindAnyObjectByType.")]
    private SwarmSpawner _swarmSpawner;

    [SerializeField, Tooltip("Pool multi-prefab (orbital). Vacío = auto en runtime.")]
    private EnemyPoolRegistry _enemyPoolRegistry;

    [SerializeField, Tooltip("HUD de contadores de pool (opcional).")]
    private EnemyPoolProfilerHud _profilerHud;

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

    [Header("Panel")]
    [SerializeField, Tooltip("Mostrar el panel IMGUI en pantalla durante el play.")]
    private bool _showPanel = true;

    private EnemySpawnRoulette _roulette;
    private PlayerHealth _playerHealth;
    private float _runStartTime;

    private int _totalRolls;
    private readonly Dictionary<EnemySpawnKind, int> _rollWinsByKind = new();
    private readonly List<SpawnedRecord> _spawned = new(512);
    private EnemySpawnRollResult _lastRoll;
    private bool _hasRolled;

    private string _maxActiveEnemiesField;

    private readonly struct SpawnedRecord
    {
        public readonly EnemySpawnKind Kind;
        public readonly GameObject Instance;

        public SpawnedRecord(EnemySpawnKind kind, GameObject instance)
        {
            Kind = kind;
            Instance = instance;
        }
    }

    private float RunTimeSeconds => Time.timeSinceLevelLoad - _runStartTime;

    private void Awake()
    {
        _runStartTime = Time.timeSinceLevelLoad;

        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");

        if (_config != null)
            _roulette = new EnemySpawnRoulette(_config);

        if (_orbitalSpawner == null)
            _orbitalSpawner = FindAnyObjectByType<OrbitalSpawner>(FindObjectsInactive.Include);
        if (_swarmSpawner == null)
            _swarmSpawner = FindAnyObjectByType<SwarmSpawner>(FindObjectsInactive.Include);
        if (_enemyPoolRegistry == null)
            _enemyPoolRegistry = FindAnyObjectByType<EnemyPoolRegistry>(FindObjectsInactive.Include);
        if (_profilerHud == null)
            _profilerHud = FindAnyObjectByType<EnemyPoolProfilerHud>(FindObjectsInactive.Include);

        EnemyPoolRegistry.EnsureExists();

        if (_orbitalSpawner != null)
            _orbitalSpawner.enabled = false;
        if (_swarmSpawner != null)
            _swarmSpawner.enabled = false;

        _maxActiveEnemiesField = _maxActiveEnemies.ToString();

        QaPanels.Active = _showPanel ? QaPanelKind.Qa : QaPanelKind.None;
    }

    private void OnValidate()
    {
        if (_maxSpawnRadius < _minSpawnRadius)
        {
            float t = _minSpawnRadius;
            _minSpawnRadius = _maxSpawnRadius;
            _maxSpawnRadius = t;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.f1Key.wasPressedThisFrame)
            QaPanels.Toggle(QaPanelKind.Qa);

        if (Keyboard.current.numpad1Key.wasPressedThisFrame)
            RollRouletteAndSpawn();

        if (Keyboard.current.numpad4Key.wasPressedThisFrame)
            LogAverageStats();

        if (Keyboard.current.numpad0Key.wasPressedThisFrame)
            ClearAll();
    }

    private Transform ResolvePlayer()
    {
        if (_player != null)
            return _player;
        return PlayerMovement.PlayerTransform;
    }

    private int CurrentOnScreenCount => EnemyRegistry.ActiveCount;

    private bool CanSpawnMore => CurrentOnScreenCount < _maxActiveEnemies;

    // ---------------------------------------------------------------------
    // Spawning
    // ---------------------------------------------------------------------

    private int SpawnKind(EnemySpawnKind kind, int amount)
    {
        if (_config == null)
            return 0;

        EnemySpawnRouletteConfig.Entry entry = _config.GetEntry(kind);
        if (entry == null || entry.Prefab == null)
        {
            Debug.LogWarning($"[EnemiesTesting] {kind}: prefab is not assigned in the config.", this);
            return 0;
        }

        Transform player = ResolvePlayer();
        if (player == null)
        {
            Debug.LogWarning("[EnemiesTesting] No hay jugador (asigna _player o añade PlayerMovement).", this);
            return 0;
        }

        int spawned = 0;
        for (int i = 0; i < amount; i++)
        {
            if (!CanSpawnMore)
            {
                Debug.Log($"[EnemiesTesting] Tope alcanzado ({_maxActiveEnemies}); se omiten spawns restantes.", this);
                break;
            }

            int dir = OrbitalSpawnPlacement.PickRandomDirectionIndex();
            if (OrbitalSpawnPlacement.TrySpawnAtOrbitalPoint(
                    player,
                    entry.Prefab,
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
                _spawned.Add(new SpawnedRecord(kind, instance));
                spawned++;
            }
        }

        return spawned;
    }

    private void RollRouletteAndSpawn()
    {
        if (_roulette == null || _config == null)
        {
            Debug.LogWarning("[EnemiesTesting] Asigna EnemySpawnRouletteConfig en el harness.", this);
            return;
        }

        EnemySpawnRollResult result = _roulette.Roll(RunTimeSeconds);
        _lastRoll = result;
        _hasRolled = true;
        _totalRolls++;

        if (!_rollWinsByKind.ContainsKey(result.SelectedKind))
            _rollWinsByKind[result.SelectedKind] = 0;
        _rollWinsByKind[result.SelectedKind]++;

        if (result.Prefab == null)
        {
            Debug.LogWarning($"[EnemiesTesting] Roulette -> {result.SelectedKind}: prefab not assigned.", this);
            return;
        }

        SpawnKind(result.SelectedKind, result.BatchSize);
    }

    private void ClearAll()
    {
        _spawned.Clear();
        EnemyLifecycleCoordinator.ClearAllForQa();

        foreach (ZoneSpawner zone in FindObjectsByType<ZoneSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            zone.Rearm();
    }

    private void PruneSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i].Instance == null)
                _spawned.RemoveAt(i);
        }
    }

    private int CountAliveOfKind(EnemySpawnKind kind)
    {
        int count = 0;
        for (int i = 0; i < _spawned.Count; i++)
        {
            SpawnedRecord rec = _spawned[i];
            if (rec.Kind == kind && rec.Instance != null && rec.Instance.activeInHierarchy)
                count++;
        }

        return count;
    }

    // ---------------------------------------------------------------------
    // Spawner continuo
    // ---------------------------------------------------------------------

    private bool ContinuousSpawnerEnabled => _orbitalSpawner != null && _orbitalSpawner.enabled;

    private void SetContinuousSpawner(bool enabled)
    {
        if (_orbitalSpawner == null)
        {
            _orbitalSpawner = FindAnyObjectByType<OrbitalSpawner>(FindObjectsInactive.Include);
            if (_orbitalSpawner == null)
            {
                Debug.LogWarning("[EnemiesTesting] No hay OrbitalSpawner en la escena.", this);
                return;
            }
        }

        if (!_orbitalSpawner.gameObject.activeSelf)
            _orbitalSpawner.gameObject.SetActive(true);
        _orbitalSpawner.enabled = enabled;
    }

    // ---------------------------------------------------------------------
    // Stats
    // ---------------------------------------------------------------------

    private void LogAverageStats()
    {
        var log = new StringBuilder(384);
        log.AppendLine($"[EnemiesTesting Stats] rolls={_totalRolls} | runTime={FormatTime(RunTimeSeconds)} | onScreen={CurrentOnScreenCount}");

        if (_totalRolls > 0)
        {
            foreach (EnemySpawnKind kind in System.Enum.GetValues(typeof(EnemySpawnKind)))
            {
                int wins = _rollWinsByKind.TryGetValue(kind, out int w) ? w : 0;
                float winPercent = wins / (float)_totalRolls * 100f;
                log.AppendLine($"  {kind,-16} wins={wins,3} ({winPercent,5:F1}% de tiradas)");
            }
        }

        Debug.Log(log.ToString());
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60:D2}:{total % 60:D2}";
    }

    // ---------------------------------------------------------------------
    // IMGUI
    // ---------------------------------------------------------------------

    private void OnGUI()
    {
        if (QaPanels.Active != QaPanelKind.Qa)
            return;

        PruneSpawned();

        const float top = 10f;
        const float gap = 6f;
        const float height = 230f;
        float x = 10f;

        x += DrawPanel(new Rect(x, top, 190f, height), "ENEMIES TESTING", DrawStatusSection) + gap;
        x += DrawPanel(new Rect(x, top, 190f, height), "PLAYER (HP)", DrawPlayerSection) + gap;
        x += DrawPanel(new Rect(x, top, 230f, height), "SPAWN BY TYPE", DrawPerKindSection) + gap;
        x += DrawPanel(new Rect(x, top, 360f, height), "DYNAMIC ROULETTE", DrawRouletteSection) + gap;
        x += DrawPanel(new Rect(x, top, 200f, height), "CONTINUOUS SPAWNER", DrawContinuousSection) + gap;
        DrawPanel(new Rect(x, top, 210f, height), "UTILITIES", DrawUtilitiesSection);
    }

    private void DrawPlayerSection()
    {
        PlayerHealth ph = ResolvePlayerHealth();
        if (ph == null)
        {
            GUILayout.Label("Sin PlayerHealth en escena.");
            return;
        }

        GUILayout.Label($"HP: {ph.CurrentHealth} / {ph.MaxHealth}");

        float frac = ph.MaxHealth > 0 ? Mathf.Clamp01((float)ph.CurrentHealth / ph.MaxHealth) : 0f;
        Rect bar = GUILayoutUtility.GetRect(150f, 16f);
        GUI.Box(bar, GUIContent.none);
        Color prev = GUI.color;
        GUI.color = Color.Lerp(Color.red, Color.green, frac);
        GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * frac, bar.height - 2f), Texture2D.whiteTexture);
        GUI.color = prev;

        GUILayout.Label(ph.IsInvulnerable ? "i-frames: SÍ" : "i-frames: no");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-10"))
            ph.TakeDamage(10);
        if (GUILayout.Button("Curar +25"))
            ph.Heal(25);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Curar al máximo"))
            ph.FullHeal();
    }

    private PlayerHealth ResolvePlayerHealth()
    {
        if (_playerHealth != null)
            return _playerHealth;

        Transform t = _player != null ? _player : PlayerMovement.PlayerTransform;
        if (t != null)
            _playerHealth = t.GetComponentInParent<PlayerHealth>() ?? t.GetComponentInChildren<PlayerHealth>();

        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>();

        return _playerHealth;
    }

    private float DrawPanel(Rect rect, string title, System.Action body)
    {
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label($"<b>{title}</b>", RichLabel());
        body();
        GUILayout.EndArea();
        return rect.width;
    }

    private void DrawStatusSection()
    {
        GUILayout.Label($"En pantalla: {CurrentOnScreenCount} / {_maxActiveEnemies}");
        GUILayout.Label($"Run time: {FormatTime(RunTimeSeconds)}");
        EnemyPoolProfiler.RefreshInactiveEnemyCount();
        GUILayout.Label($"Pool leased: {(_enemyPoolRegistry != null ? _enemyPoolRegistry.TotalLeased : EnemyPoolRegistry.Instance?.TotalLeased ?? 0)}");
        GUILayout.Label($"Inactivos escena: {EnemyPoolProfiler.InactiveEnemyObjects}");
        GUILayout.Label($"Inst/Destroy: {EnemyPoolProfiler.InstantiateCount}/{EnemyPoolProfiler.DestroyCount}");
        GUILayout.Space(4f);
        DrawCapField();
    }

    private void DrawCapField()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Tope:", GUILayout.Width(40f));
        _maxActiveEnemiesField = GUILayout.TextField(_maxActiveEnemiesField, GUILayout.Width(70f));
        if (GUILayout.Button("Set", GUILayout.Width(50f)))
        {
            if (int.TryParse(_maxActiveEnemiesField, out int parsed) && parsed > 0)
                _maxActiveEnemies = parsed;
            _maxActiveEnemiesField = _maxActiveEnemies.ToString();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawPerKindSection()
    {
        if (_config == null)
        {
            GUILayout.Label("Sin config asignada.");
            return;
        }

        foreach (EnemySpawnKind kind in System.Enum.GetValues(typeof(EnemySpawnKind)))
        {
            EnemySpawnRouletteConfig.Entry entry = _config.GetEntry(kind);
            int batch = entry != null ? Mathf.Max(1, entry.BatchSize) : 1;
            bool hasPrefab = entry != null && entry.Prefab != null;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{kind} ({CountAliveOfKind(kind)})", GUILayout.Width(118f));

            GUI.enabled = hasPrefab;
            if (GUILayout.Button("+1", GUILayout.Width(36f)))
                SpawnKind(kind, 1);
            if (GUILayout.Button($"+{batch}", GUILayout.Width(54f)))
                SpawnKind(kind, batch);
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            if (!hasPrefab)
                GUILayout.Label("  (prefab not assigned)");
        }
    }

    private void DrawRouletteSection()
    {
        if (GUILayout.Button("Tirar ruleta (Numpad 1)"))
            RollRouletteAndSpawn();

        if (!_hasRolled)
        {
            GUILayout.Label("(aún sin tiradas)");
            return;
        }

        GUILayout.Label($"Última: {_lastRoll.SelectedKind} | batch={_lastRoll.BatchSize} | variantBonus=+{_lastRoll.VariantWeightBonus}");
        if (_lastRoll.Snapshots != null)
        {
            foreach (EnemySpawnWeightSnapshot snap in _lastRoll.Snapshots)
                GUILayout.Label($"  {snap.Kind,-14} w={snap.EffectiveWeight,3} ({snap.Percent,5:F1}%)");
        }
    }

    private void DrawContinuousSection()
    {
        if (_orbitalSpawner == null)
        {
            GUILayout.Label("Sin OrbitalSpawner en escena.");
            return;
        }

        GUILayout.Label("Spawner orbital (ruleta)");
        bool enabled = ContinuousSpawnerEnabled;
        bool newEnabled = GUILayout.Toggle(enabled, " Activo (sigue al jugador)");
        if (newEnabled != enabled)
            SetContinuousSpawner(newEnabled);

        GUILayout.Label($"Activos del orbital: {_orbitalSpawner.ActiveSpawnedCount}");

        if (_enemyPoolRegistry != null)
            GUILayout.Label($"Pool registry: {_enemyPoolRegistry.TotalLeased} leased");
    }

    private void DrawUtilitiesSection()
    {
        if (GUILayout.Button("Clear all (Numpad 0)"))
            ClearAll();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"timeScale: {Time.timeScale:0.0#}", GUILayout.Width(110f));
        float ts = GUILayout.HorizontalSlider(Time.timeScale, 0f, 3f);
        if (!Mathf.Approximately(ts, Time.timeScale))
            Time.timeScale = ts;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("0x", GUILayout.Width(40f)))
            Time.timeScale = 0f;
        if (GUILayout.Button("1x", GUILayout.Width(40f)))
            Time.timeScale = 1f;
        if (GUILayout.Button("2x", GUILayout.Width(40f)))
            Time.timeScale = 2f;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Stats to console (Numpad 4)"))
            LogAverageStats();

        if (GUILayout.Button("Copy QA report (clipboard)"))
            CopyQaReport();
    }

    private void CopyQaReport()
    {
        QaPanels.Copy(QaBalanceReport.Build(_config, _orbitalSpawner, ResolvePlayerHealth()));
    }

    private static GUIStyle s_RichLabel;

    private static GUIStyle RichLabel()
    {
        if (s_RichLabel == null)
        {
            s_RichLabel = new GUIStyle(GUI.skin.label) { richText = true };
        }

        return s_RichLabel;
    }
}
