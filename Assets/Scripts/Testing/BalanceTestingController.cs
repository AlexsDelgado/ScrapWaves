using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Escena mínima de balance: spawn enemigos 1×1, materiales, 1 arma, crafting y power-ups temporales.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public class BalanceTestingController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private EnemySpawnRouletteConfig _rouletteConfig;
    [SerializeField] private Transform _player;
    [SerializeField] private WeaponData _startingWeapon;
    [SerializeField] private CraftingStation _craftingStation;
    [SerializeField] private GameObject _destroyerBossPrefab;
    [SerializeField] private GameObject _stalkerBossPrefab;
    [SerializeField] private Transform[] _powerupSpawnPoints = Array.Empty<Transform>();

    [Header("Spawn")]
    [SerializeField, Min(0f)] private float _minSpawnRadius = 8f;
    [SerializeField, Min(0f)] private float _maxSpawnRadius = 14f;
    [SerializeField] private float _spawnHeightOffset;
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

    [Header("UI")]
    [SerializeField] private bool _showPanel = true;
    [SerializeField, Tooltip("Si true, el mouse controla la UI (cursor libre). Si false, mira con la cámara.")]
    private bool _uiMouseMode = true;

    private WeaponManager _weapons;
    private MaterialInventory _inventory;
    private TemporaryPowerupController _powerups;
    private ThirdPersonCamera _camera;
    private Vector2 _scroll;
    private string _status = "Balance test ready.";

    private void Awake()
    {
        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");

        if (_rouletteConfig == null)
            _rouletteConfig = Resources.Load<EnemySpawnRouletteConfig>("Spawning/DefaultEnemySpawnRoulette");
        // Fallback: scene builder assigns DefaultEnemySpawnRoulette from ScriptableObjects.

        EnsureBossPrefabs();
        EnsurePowerupSpawnPoints();
        ResolvePlayer();
        ConfigurePlayerForBalance();
        EnsureEnemyPools();
        TemporaryPowerupPool.GetInstance();
    }

    private void Start()
    {
        EquipStartingWeapon();
        PlaceCraftingStationNearPlayer();
        CacheCamera();
        ApplyUiMouseMode();
        EnablePowerupStatLogs();
        SetStatus("F1 panel · F2 mouse UI/cámara · craft E / botón");
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            _showPanel = !_showPanel;

        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            SetUiMouseMode(!_uiMouseMode);
    }

    private void LateUpdate()
    {
        // Re-assert while UI owns the mouse so gameplay systems can't re-lock it.
        if (_uiMouseMode)
            ApplyUiMouseMode();
    }

    private void OnGUI()
    {
        if (!_showPanel)
            return;

        const float width = 420f;
        Rect area = new(12f, 12f, width, Mathf.Min(Screen.height - 24f, 720f));
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f));
        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.Label("BALANCE TEST — test_balance");
        GUILayout.Label(_status);
        GUILayout.Space(6f);

        DrawCursorSection();
        GUILayout.Space(8f);
        DrawEnemySection();
        GUILayout.Space(8f);
        DrawBossSection();
        GUILayout.Space(8f);
        DrawMaterialsSection();
        GUILayout.Space(8f);
        DrawWeaponsCraftingSection();
        GUILayout.Space(8f);
        DrawPowerupsSection();
        GUILayout.Space(8f);
        if (GUILayout.Button("Clear enemies"))
            ClearEnemies();
        if (GUILayout.Button("Heal player full"))
            HealPlayer();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawCursorSection()
    {
        GUILayout.Label(_uiMouseMode ? "Mouse: UI (libre)" : "Mouse: Cámara (lockeado)");
        if (GUILayout.Button(_uiMouseMode ? "Lock mouse → Camera (F2)" : "Unlock mouse → UI (F2)"))
            SetUiMouseMode(!_uiMouseMode);
    }

    private void DrawEnemySection()
    {
        GUILayout.Label("Enemies");
        if (_rouletteConfig == null)
        {
            GUILayout.Label("Missing EnemySpawnRouletteConfig");
            return;
        }

        foreach (EnemySpawnKind kind in Enum.GetValues(typeof(EnemySpawnKind)))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(kind.ToString(), GUILayout.Width(140f));
            if (GUILayout.Button("+1", GUILayout.Width(40f)))
                SpawnEnemy(kind, 1);
            if (GUILayout.Button("+10", GUILayout.Width(50f)))
                SpawnEnemy(kind, 10);
            GUILayout.EndHorizontal();
        }
    }

    private void DrawBossSection()
    {
        GUILayout.Label("Bosses");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Destroyer"))
            SpawnBoss(_destroyerBossPrefab, "Destroyer");
        if (GUILayout.Button("+ Stalker"))
            SpawnBoss(_stalkerBossPrefab, "Stalker");
        GUILayout.EndHorizontal();
    }

    private void DrawMaterialsSection()
    {
        GUILayout.Label("Materials");
        if (GUILayout.Button("Grant 999 of ALL materials"))
            GrantAllMaterials(999);

        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            int amount = _inventory != null ? _inventory.GetAmount(type) : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{type}: {amount}", GUILayout.Width(180f));
            if (GUILayout.Button("+50", GUILayout.Width(50f)))
                GrantMaterial(type, 50);
            GUILayout.EndHorizontal();
        }
    }

    private void DrawWeaponsCraftingSection()
    {
        GUILayout.Label("Weapons / Crafting");
        string equipped = _weapons != null && _weapons.GetEquippedWeapons().Count > 0
            ? _weapons.GetEquippedWeapons()[0]?.Runtime?.Data?.DisplayName ?? "?"
            : "(none)";
        GUILayout.Label($"Equipped: {equipped}");

        if (_startingWeapon != null && GUILayout.Button($"Reset to 1× {_startingWeapon.DisplayName}"))
            EquipStartingWeapon();

        if (GUILayout.Button("Open crafting UI"))
            OpenCrafting();
    }

    private void DrawPowerupsSection()
    {
        GUILayout.Label("Temporary power-ups (spawnpoints fijos)");
        foreach (TemporaryPowerupType type in Enum.GetValues(typeof(TemporaryPowerupType)))
        {
            if (GUILayout.Button($"Spawn {type}"))
                SpawnPowerup(type);
        }
    }

    public void SpawnEnemy(EnemySpawnKind kind, int count = 1)
    {
        if (_rouletteConfig == null)
        {
            SetStatus("No roulette config.");
            return;
        }

        EnemySpawnRouletteConfig.Entry entry = _rouletteConfig.GetEntry(kind);
        if (entry?.Prefab == null)
        {
            SetStatus($"{kind}: prefab missing in roulette.");
            return;
        }

        Transform player = ResolvePlayer();
        if (player == null)
        {
            SetStatus("No player.");
            return;
        }

        count = Mathf.Max(1, count);
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
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
                spawned++;
            }
        }

        SetStatus(spawned > 0 ? $"Spawned {kind} ×{spawned}" : $"Failed spawn {kind}");
    }

    public void ClearEnemies()
    {
        EnemyLifecycleCoordinator.ClearAllForQa();
        SetStatus("Enemies cleared.");
    }

    public void GrantAllMaterials(int amount)
    {
        EnsureInventory();
        if (_inventory == null)
        {
            SetStatus("No MaterialInventory.");
            return;
        }

        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
            _inventory.Add(type, amount);
        SetStatus($"Granted {amount} of each material.");
    }

    public void GrantMaterial(MaterialType type, int amount)
    {
        EnsureInventory();
        if (_inventory == null)
            return;
        _inventory.Add(type, amount);
        SetStatus($"+{amount} {type}");
    }

    public void EquipStartingWeapon()
    {
        ResolvePlayer();
        if (_weapons == null || _startingWeapon == null)
        {
            SetStatus("Missing WeaponManager or starting weapon.");
            return;
        }

        RunStartWeaponChoice choice = _player != null
            ? _player.GetComponent<RunStartWeaponChoice>()
            : null;
        if (choice != null)
            choice.enabled = false;

        _weapons.ClearEquippedWeapons();
        _weapons.AddWeapon(_startingWeapon);
        SetStatus($"Equipped {_startingWeapon.DisplayName}");
    }

    public void OpenCrafting()
    {
        if (_craftingStation == null)
            _craftingStation = FindAnyObjectByType<CraftingStation>();
        if (_craftingStation == null)
        {
            SetStatus("No CraftingStation.");
            return;
        }

        _craftingStation.OpenCrafting();
        SetStatus("Crafting opened.");
    }

    public void SpawnPowerup(TemporaryPowerupType type)
    {
        Vector3 pos = GetPowerupSpawnPosition(type);
        bool ok = TemporaryPowerupPool.GetInstance().TrySpawn(pos, type);
        SetStatus(ok ? $"Spawned {type} at {pos}" : $"Failed power-up {type}");
    }

    public void SpawnBoss(GameObject prefab, string label)
    {
        if (prefab == null)
        {
            SetStatus($"{label}: prefab missing.");
            return;
        }

        Transform player = ResolvePlayer();
        if (player == null)
        {
            SetStatus("No player.");
            return;
        }

        Vector3 ringPos = player.position + player.forward * 10f;
        GameObject go = Instantiate(prefab, ringPos, Quaternion.identity);
        EnemyHealth health = go.GetComponent<EnemyHealth>();
        if (health == null)
        {
            Destroy(go);
            SetStatus($"{label}: prefab without EnemyHealth.");
            return;
        }

        CharacterController cc = go.GetComponent<CharacterController>();
        if (cc != null && SpawnGroundUtility.TryResolveFootPosition(
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
            go.transform.position = foot;
        }
        else
            go.transform.position = ringPos;

        if (go.GetComponent<EnemyScrapDrop>() == null)
            go.AddComponent<EnemyScrapDrop>();
        if (go.GetComponent<EnemyTemporaryPowerupDrop>() == null)
            go.AddComponent<EnemyTemporaryPowerupDrop>();

        Vector3 toPlayer = player.position - go.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            go.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

        if (!go.activeSelf)
            go.SetActive(true);

        SetStatus($"Spawned {label}");
    }

    public void HealPlayer()
    {
        ResolvePlayer();
        PlayerHealth health = _player != null ? _player.GetComponent<PlayerHealth>() : null;
        if (health == null)
            return;
        health.HealToFull();
        SetStatus("Player healed.");
    }

    private void ConfigurePlayerForBalance()
    {
        if (_player == null)
            return;

        RunStartWeaponChoice choice = _player.GetComponent<RunStartWeaponChoice>();
        if (choice != null)
            choice.enabled = false;

        OverheatManager overheat = _player.GetComponent<OverheatManager>();
        if (overheat != null)
            overheat.enabled = false;

        EnsurePowerups();
        EnablePowerupStatLogs();
        EnsureInventory();
        _weapons = _player.GetComponent<WeaponManager>();
    }

    private void EnsureEnemyPools()
    {
        EnemyPoolRegistry.EnsureExists();
        if (_rouletteConfig != null && EnemyPoolRegistry.Instance != null)
            EnemyPoolRegistry.Instance.RegisterFromRoulette(_rouletteConfig);
    }

    private void EnsurePowerups()
    {
        if (_player == null)
            return;
        _powerups = _player.GetComponent<TemporaryPowerupController>();
        if (_powerups == null)
            _powerups = _player.gameObject.AddComponent<TemporaryPowerupController>();
        if (_player.GetComponent<MetaProgressionApplier>() == null)
            _player.gameObject.AddComponent<MetaProgressionApplier>();
    }

    private void EnsureInventory()
    {
        if (_inventory != null)
            return;
        _inventory = MaterialInventory.Instance != null
            ? MaterialInventory.Instance
            : FindAnyObjectByType<MaterialInventory>();
    }

    private void EnablePowerupStatLogs()
    {
        EnsurePowerups();
        if (_powerups == null)
            return;

        _powerups.LogStatDeltas = true;
        _powerups.OnPowerupLogged -= HandlePowerupLogged;
        _powerups.OnPowerupLogged += HandlePowerupLogged;
    }

    private void HandlePowerupLogged(string message)
    {
        SetStatus(message);
    }

    private void EnsureBossPrefabs()
    {
#if UNITY_EDITOR
        if (_destroyerBossPrefab == null)
            _destroyerBossPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Destroyer_Boss.prefab");
        if (_stalkerBossPrefab == null)
            _stalkerBossPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Stalker.prefab");
#endif
    }

    private void EnsurePowerupSpawnPoints()
    {
        if (HasAssignedSpawnPoints())
            return;

        TemporaryPowerupType[] types = (TemporaryPowerupType[])Enum.GetValues(typeof(TemporaryPowerupType));
        _powerupSpawnPoints = new Transform[types.Length];
        GameObject root = new("PowerupSpawnPoints");
        const float radius = 11f;
        for (int i = 0; i < types.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / types.Length;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"PowerupSpawn_{types[i]}";
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = new Vector3(Mathf.Cos(angle) * radius, 0.15f, Mathf.Sin(angle) * radius);
            marker.transform.localScale = new Vector3(1.2f, 0.08f, 1.2f);
            Collider col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            _powerupSpawnPoints[i] = marker.transform;
        }
    }

    private bool HasAssignedSpawnPoints()
    {
        if (_powerupSpawnPoints == null || _powerupSpawnPoints.Length == 0)
            return false;
        for (int i = 0; i < _powerupSpawnPoints.Length; i++)
        {
            if (_powerupSpawnPoints[i] == null)
                return false;
        }

        return true;
    }

    private Vector3 GetPowerupSpawnPosition(TemporaryPowerupType type)
    {
        int index = (int)type;
        if (_powerupSpawnPoints != null
            && index >= 0
            && index < _powerupSpawnPoints.Length
            && _powerupSpawnPoints[index] != null)
        {
            return _powerupSpawnPoints[index].position;
        }

        float angle = index * Mathf.PI * 2f / Mathf.Max(1, Enum.GetValues(typeof(TemporaryPowerupType)).Length);
        const float radius = 11f;
        return new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
    }

    private void OnDestroy()
    {
        if (_powerups != null)
            _powerups.OnPowerupLogged -= HandlePowerupLogged;
    }

    private void PlaceCraftingStationNearPlayer()
    {
        if (_craftingStation == null)
            _craftingStation = FindAnyObjectByType<CraftingStation>();
        if (_craftingStation == null || _player == null)
            return;

        Vector3 pos = _player.position + _player.forward * 4f;
        pos.y = _player.position.y;
        _craftingStation.transform.position = pos;
    }

    private Transform ResolvePlayer()
    {
        if (_player != null)
            return _player;

        if (PlayerMovement.PlayerTransform != null)
        {
            _player = PlayerMovement.PlayerTransform;
            return _player;
        }

        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
            _player = movement.transform;
        return _player;
    }

    private void SetStatus(string message) => _status = message ?? string.Empty;

    private void SetUiMouseMode(bool uiMode)
    {
        _uiMouseMode = uiMode;
        ApplyUiMouseMode();
        SetStatus(_uiMouseMode ? "Mouse libre para UI (F2 = cámara)" : "Mouse en cámara (F2 = UI)");
    }

    private void CacheCamera()
    {
        if (_camera != null)
            return;
        Camera main = Camera.main;
        if (main != null)
            _camera = main.GetComponent<ThirdPersonCamera>();
        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
    }

    private void ApplyUiMouseMode()
    {
        CacheCamera();
        if (_camera != null)
        {
            _camera.SetLookBlockedByUi(_uiMouseMode);
            return;
        }

        Cursor.lockState = _uiMouseMode ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _uiMouseMode;
    }
}
