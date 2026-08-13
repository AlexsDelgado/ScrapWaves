using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class WeaponTestingSandboxManager : MonoBehaviour
{
    public const int WeaponSlots = 3;
    private static readonly Color PathAFeedbackColor = new(0.95f, 1f, 0.28f, 0.95f);
    private static readonly Color PathBFeedbackColor = new(0.35f, 0.9f, 1f, 0.95f);

    [Header("Asset References")]
    [SerializeField] private GameObject _playerPrefab;
    [SerializeField] private GameObject _projectilePoolPrefab;
    [SerializeField] private GameObject _dummyPrefab;
    [SerializeField] private List<WeaponData> _weaponData = new();

    [Header("Scene References")]
    [SerializeField] private Transform _playerInstance;
    [SerializeField] private ProjectilePool _projectilePoolInstance;
    [SerializeField] private HeatManager _heatManagerInstance;
    [SerializeField] private Transform _arenaRoot;
    [SerializeField] private Transform _spawnedDummiesRoot;
    [SerializeField] private bool _buildMissingSceneObjectsAtRuntime;

    [Header("Starting Loadout")]
    [SerializeField] private WeaponType _slot1 = WeaponType.AutomaticCannon;
    [SerializeField] private WeaponType _slot2 = WeaponType.RocketLauncher;
    [SerializeField] private WeaponType _slot3 = WeaponType.Flamethrower;

    private readonly WeaponInstance[] _instances = new WeaponInstance[WeaponSlots];
    private readonly IWeaponBehaviour[] _behaviours = new IWeaponBehaviour[WeaponSlots];
    private readonly float[] _lastAmmo = new float[WeaponSlots];

    private PlayerStats _playerStats;
    private PlayerMovement _playerMovement;
    private ReticleAimProvider _aimProvider;
    private WeaponPresentationController _presentationController;
    private PlayerWeaponMountController _mountController;
    private ProjectilePool _projectilePool;
    private HeatManager _heatManager;
    private IWeaponTargeting _targeting;
    private int _manualSlot;
    private Vector3 _currentAimDirection = Vector3.forward;
    private bool _bootstrapComplete;

    public Transform PlayerTransform { get; private set; }
    public Transform ProjectileSpawn { get; private set; }
    public WeaponStatOverride StatOverride { get; private set; }
    public WeaponHeatOverride HeatOverride { get; private set; }
    public WeaponTestMetrics Metrics { get; private set; }
    public WeaponDummySpawner Spawner { get; private set; }
    public WeaponDebugGizmos DebugGizmos { get; private set; }
    public Vector3 CurrentAimDirection => _currentAimDirection;
    public int CurrentManualSlot => _manualSlot;
    public WeaponInstance CurrentManualWeapon => IsValidSlot(_manualSlot) ? _instances[_manualSlot] : null;
    public IWeaponBehaviour CurrentManualBehaviour => IsValidSlot(_manualSlot) ? _behaviours[_manualSlot] : null;
    public IReadOnlyList<WeaponData> WeaponData => _weaponData;
    public WeaponPresentationController PresentationController => _presentationController;
    public ProjectilePool ProjectilePool => _projectilePool;

    private void Awake()
    {
        Bootstrap();
    }

    private void Start()
    {
        SpawnInitialLoadout();
    }

    private void Update()
    {
        if (!_bootstrapComplete)
            return;

        _currentAimDirection = ResolveAimDirection();
        TickWeapons(Time.deltaTime);
    }

    public void Bootstrap()
    {
        if (_bootstrapComplete)
            return;

        _targeting = new ConfiguredEnemyTargeting();
        EnsureArena();
        EnsureHeatManager();
        EnsureProjectilePool();
        EnsurePlayer();
        if (PlayerTransform == null)
        {
            Debug.LogError("WeaponTestingSandboxManager: scene references are missing. Run Tools/ScrapWaves/Build Weapon Testing Sandbox so the player, arena, pool, and camera exist in the hierarchy.", this);
            enabled = false;
            return;
        }

        EnsureSandboxComponents();
        EnsureCamera();
        EnsureEventSystem();
        _bootstrapComplete = true;
    }

    public WeaponData GetWeaponData(WeaponType weaponType)
    {
        for (int i = 0; i < _weaponData.Count; i++)
        {
            WeaponData data = _weaponData[i];
            if (data != null && data.WeaponType == weaponType)
                return data;
        }

        return null;
    }

    public void SetWeaponSlot(int slot, WeaponType? weaponType)
    {
        if (!IsValidSlot(slot))
            return;

        _mountController?.RemoveWeapon(_behaviours[slot]);

        if (!weaponType.HasValue)
        {
            _instances[slot] = null;
            _behaviours[slot] = null;
            if (_manualSlot == slot)
                SelectFirstAvailableManual();
            return;
        }

        WeaponData data = GetWeaponData(weaponType.Value);
        if (data == null)
        {
            Debug.LogWarning($"WeaponTestingSandbox: no WeaponData registered for {weaponType.Value}.", this);
            return;
        }

        WeaponInstance instance = new WeaponInstance
        {
            Data = data,
            Level = 1,
            SelectedPath = WeaponUpgradePath.None,
            State = WeaponState.Automatic,
            CurrentAmmo = 0f
        };

        _instances[slot] = instance;
        _behaviours[slot] = CreateBehaviour(data);
        _behaviours[slot].Setup(instance, PlayerTransform, _playerStats, _heatManager);
        _mountController?.AddWeapon(_behaviours[slot], slot == _manualSlot);

        if (CurrentManualWeapon == null || _manualSlot == slot)
            StartManualMode(slot, true);
    }

    public void ApplyWeaponLevelAndPath(int slot, int level, WeaponUpgradePath path)
    {
        if (!IsValidSlot(slot) || _instances[slot] == null)
            return;

        WeaponInstance weapon = _instances[slot];
        int requestedLevel = path == WeaponUpgradePath.None ? level : Mathf.Max(level, 6);
        weapon.Level = Mathf.Clamp(requestedLevel, 1, 10);
        weapon.SelectedPath = weapon.Level >= 6 ? path : WeaponUpgradePath.None;
        ShowUpgradePathFeedback(weapon);
        if (weapon.State == WeaponState.Manual)
            RefillAmmo(slot);
    }

    private void ShowUpgradePathFeedback(WeaponInstance weapon)
    {
        if (!Application.isPlaying || weapon == null || weapon.SelectedPath == WeaponUpgradePath.None || !weapon.HasAdvancedPath)
            return;

        Vector3 center = PlayerTransform != null ? PlayerTransform.position : transform.position;
        Color color = weapon.SelectedPath == WeaponUpgradePath.PathA ? PathAFeedbackColor : PathBFeedbackColor;
        string label = weapon.SelectedPath == WeaponUpgradePath.PathA ? "PATH A" : "PATH B";
        WeaponUpgradeVfx.SpawnRing(center, 2.2f, color, 1.2f, 2.25f, label);
    }

    public WeaponInstance GetWeaponInSlot(int slot)
    {
        return IsValidSlot(slot) ? _instances[slot] : null;
    }

    public void SelectManualSlot(int slot)
    {
        if (!IsValidSlot(slot) || _instances[slot] == null)
            return;

        StartManualMode(slot, true);
    }

    public void ForceAutomaticMode()
    {
        WeaponInstance weapon = CurrentManualWeapon;
        if (weapon != null)
            weapon.State = WeaponState.Automatic;
    }

    public void ForceManualMode()
    {
        if (CurrentManualWeapon == null)
            SelectFirstAvailableManual();
        else
            StartManualMode(_manualSlot, CurrentManualWeapon.CurrentAmmo <= 0f);
    }

    public void RefillAmmo()
    {
        RefillAmmo(_manualSlot);
    }

    public void EmptyAmmo()
    {
        WeaponInstance weapon = CurrentManualWeapon;
        if (weapon == null)
            return;

        weapon.CurrentAmmo = 0f;
        CycleToNextWeapon();
    }

    public void CycleToNextWeapon()
    {
        int next = FindNextWeaponSlot(_manualSlot + 1);
        if (next < 0)
            return;

        StartManualMode(next, true);
    }

    public void UseActiveAbility()
    {
        WeaponInstance weapon = CurrentManualWeapon;
        if (weapon == null || weapon.State != WeaponState.Manual)
            return;

        float before = weapon.CurrentAmmo;
        IWeaponBehaviour behaviour = _behaviours[_manualSlot];
        if (behaviour is IHoldActiveAbilityBehaviour holdAbility)
        {
            holdAbility.BeginActiveAbility(_currentAimDirection);
            holdAbility.ReleaseActiveAbility(_currentAimDirection);
        }
        else
        {
            behaviour?.UseActiveAbility(_currentAimDirection);
        }

        float spent = Mathf.Max(0f, before - weapon.CurrentAmmo);
        if (spent > 0f)
        {
            Metrics.RecordAmmoConsumed(spent);
            Metrics.RecordActiveAbilityUse();
        }
    }

    public void ResetWeaponCooldowns()
    {
        for (int i = 0; i < WeaponSlots; i++)
        {
            WeaponInstance instance = _instances[i];
            if (instance?.Data == null)
                continue;

            instance.AbilityCooldownTimer = 0f;
            _behaviours[i] = CreateBehaviour(instance.Data);
            _behaviours[i].Setup(instance, PlayerTransform, _playerStats, _heatManager);
        }
    }

    public bool IsSandboxWeapon(WeaponInstance weapon)
    {
        if (weapon == null)
            return false;

        for (int i = 0; i < _instances.Length; i++)
        {
            if (_instances[i] == weapon)
                return true;
        }

        return false;
    }

    public float GetMaxAmmo(WeaponInstance weapon)
    {
        return WeaponMath.GetMaxManualAmmo(weapon, _playerStats);
    }

    private void TickWeapons(float deltaTime)
    {
        for (int i = 0; i < WeaponSlots; i++)
        {
            WeaponInstance instance = _instances[i];
            IWeaponBehaviour behaviour = _behaviours[i];
            if (instance == null)
                continue;

            instance.AbilityCooldownTimer = Mathf.Max(0f, instance.AbilityCooldownTimer - deltaTime);
            _lastAmmo[i] = instance.CurrentAmmo;
            if (behaviour != null && instance.State == WeaponState.Automatic)
                behaviour.TickAutomatic(deltaTime, _currentAimDirection);
        }

        WeaponInstance manual = CurrentManualWeapon;
        if (manual != null && manual.State == WeaponState.Manual)
        {
            _lastAmmo[_manualSlot] = manual.CurrentAmmo;
            _behaviours[_manualSlot]?.TickManual(deltaTime, _currentAimDirection, IsFireHeld());
        }

        for (int i = 0; i < WeaponSlots; i++)
        {
            WeaponInstance instance = _instances[i];
            if (instance == null)
                continue;

            float spent = Mathf.Max(0f, _lastAmmo[i] - instance.CurrentAmmo);
            if (spent > 0f)
                Metrics.RecordAmmoConsumed(spent);
        }

        manual = CurrentManualWeapon;
        if (manual != null && manual.State == WeaponState.Manual && manual.CurrentAmmo <= 0f)
            CycleToNextWeapon();

        TickActiveAbilityInput(manual, deltaTime);
    }

    private void SpawnInitialLoadout()
    {
        SetWeaponSlot(0, _slot1);
        SetWeaponSlot(1, _slot2);
        SetWeaponSlot(2, _slot3);
        StartManualMode(0, true);
    }

    private IWeaponBehaviour CreateBehaviour(WeaponData data)
    {
        Transform spawn = ProjectileSpawn != null ? ProjectileSpawn : PlayerTransform;
        if (data?.PresentationProfile != null)
            _presentationController?.SetProfile(data.PresentationProfile);

        IWeaponPresentationSink presentationSink = _presentationController != null
            ? _presentationController
            : NullWeaponPresentationSink.Instance;
        return WeaponBehaviourFactory.Create(
            data,
            _targeting,
            _projectilePool,
            spawn,
            _playerMovement,
            presentationSink);
    }

    private void StartManualMode(int slot, bool refillAmmo)
    {
        if (!IsValidSlot(slot) || _instances[slot] == null)
            return;

        CancelHeldAbilities();
        for (int i = 0; i < WeaponSlots; i++)
        {
            if (_instances[i] != null)
                _instances[i].State = i == slot ? WeaponState.Manual : WeaponState.Automatic;
        }

        _manualSlot = slot;
        _mountController?.SetManualWeapon(_behaviours[slot]);
        if (refillAmmo)
            RefillAmmo(slot);
    }

    private void RefillAmmo(int slot)
    {
        if (!IsValidSlot(slot) || _instances[slot] == null)
            return;

        _instances[slot].CurrentAmmo = GetMaxAmmo(_instances[slot]);
    }

    private void SelectFirstAvailableManual()
    {
        int slot = FindNextWeaponSlot(0);
        if (slot >= 0)
            StartManualMode(slot, true);
    }

    private int FindNextWeaponSlot(int start)
    {
        for (int offset = 0; offset < WeaponSlots; offset++)
        {
            int slot = (start + offset) % WeaponSlots;
            if (_instances[slot] != null)
                return slot;
        }

        return -1;
    }

    private Vector3 ResolveAimDirection()
    {
        WeaponInstance manualWeapon = CurrentManualWeapon;
        float fallbackDistance = manualWeapon?.Data != null ? manualWeapon.Data.BaseRange : 0f;
        bool preferDamageableAimPoint = ShouldPreferDamageableAimPoint(manualWeapon);
        if (_aimProvider != null && ProjectileSpawn != null && _aimProvider.TryGetAimDirection(ProjectileSpawn.position, fallbackDistance, preferDamageableAimPoint, out Vector3 aim))
            return aim.normalized;

        if (Camera.main != null)
            return Camera.main.transform.forward;

        return PlayerTransform != null ? PlayerTransform.forward : Vector3.forward;
    }

    private static bool ShouldPreferDamageableAimPoint(WeaponInstance weapon)
    {
        if (weapon?.Data == null)
            return false;

        return weapon.Data.WeaponType == WeaponType.AutomaticCannon
            || weapon.Data.WeaponType == WeaponType.RocketLauncher;
    }

    private void EnsureHeatManager()
    {
        _heatManager = _heatManagerInstance != null ? _heatManagerInstance : HeatManager.GetInstance();
        if (_heatManager != null)
            return;

        if (!_buildMissingSceneObjectsAtRuntime)
            return;

        GameObject heatGo = new GameObject("SandboxHeatManager");
        _heatManager = heatGo.AddComponent<HeatManager>();
    }

    private void EnsureProjectilePool()
    {
        _projectilePool = _projectilePoolInstance != null ? _projectilePoolInstance : FindAnyObjectByType<ProjectilePool>();
        if (_projectilePool != null)
            return;

        if (!_buildMissingSceneObjectsAtRuntime)
            return;

        if (_projectilePoolPrefab != null)
        {
            GameObject poolGo = Instantiate(_projectilePoolPrefab, Vector3.zero, Quaternion.identity);
            poolGo.name = "SandboxProjectilePool";
            _projectilePool = poolGo.GetComponent<ProjectilePool>();
        }
    }

    private void EnsurePlayer()
    {
        GameObject playerGo = _playerInstance != null ? _playerInstance.gameObject : null;
        if (playerGo != null)
        {
            playerGo.name = "WeaponSandboxPlayer";
        }
        else if (_playerPrefab != null && _buildMissingSceneObjectsAtRuntime)
        {
            playerGo = Instantiate(_playerPrefab, new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            playerGo.name = "WeaponSandboxPlayer";
        }
        else if (_buildMissingSceneObjectsAtRuntime)
        {
            playerGo = CreateFallbackPlayer();
        }
        else
        {
            return;
        }

        PlayerTransform = playerGo.transform;
        _playerStats = playerGo.GetComponent<PlayerStats>();
        _playerMovement = playerGo.GetComponent<PlayerMovement>();
        _aimProvider = playerGo.GetComponent<ReticleAimProvider>();
        _presentationController = playerGo.GetComponent<WeaponPresentationController>();
        _mountController = playerGo.GetComponent<PlayerWeaponMountController>();
        if (_mountController == null)
            _mountController = playerGo.AddComponent<PlayerWeaponMountController>();
        if (_presentationController == null)
            _presentationController = playerGo.AddComponent<WeaponPresentationController>();
        DisableProductionRuntimeComponents(playerGo);

        ProjectileSpawn = FindChildByNameContains(playerGo.transform, "Fire");
        if (ProjectileSpawn == null)
            ProjectileSpawn = playerGo.transform;
        _mountController.Initialize(ProjectileSpawn);
    }

    private void EnsureSandboxComponents()
    {
        StatOverride = gameObject.GetComponent<WeaponStatOverride>();
        if (StatOverride == null)
            StatOverride = gameObject.AddComponent<WeaponStatOverride>();
        StatOverride.Bind(_playerStats);

        HeatOverride = gameObject.GetComponent<WeaponHeatOverride>();
        if (HeatOverride == null)
            HeatOverride = gameObject.AddComponent<WeaponHeatOverride>();
        HeatOverride.Bind(_heatManager);

        Metrics = gameObject.GetComponent<WeaponTestMetrics>();
        if (Metrics == null)
            Metrics = gameObject.AddComponent<WeaponTestMetrics>();
        Metrics.Bind(this);

        Spawner = gameObject.GetComponent<WeaponDummySpawner>();
        if (Spawner == null)
            Spawner = gameObject.AddComponent<WeaponDummySpawner>();
        Spawner.Bind(_dummyPrefab, PlayerTransform, Metrics, _spawnedDummiesRoot);
        Spawner.SetZoneCenters(new Vector3(0f, 0f, 14f), new Vector3(18f, 0f, 14f), new Vector3(-18f, 0f, 14f), new Vector3(0f, 0f, -16f), new Vector3(18f, 0f, -22f));

        DebugGizmos = gameObject.GetComponent<WeaponDebugGizmos>();
        if (DebugGizmos == null)
            DebugGizmos = gameObject.AddComponent<WeaponDebugGizmos>();
        DebugGizmos.Bind(this);

        WeaponSandboxDebugUI debugUi = gameObject.GetComponent<WeaponSandboxDebugUI>();
        if (debugUi == null)
            debugUi = gameObject.AddComponent<WeaponSandboxDebugUI>();
        debugUi.Bind(this);
    }

    private void EnsureCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
        }

        ThirdPersonCamera thirdPersonCamera = camera.GetComponent<ThirdPersonCamera>();
        if (thirdPersonCamera == null)
            thirdPersonCamera = camera.gameObject.AddComponent<ThirdPersonCamera>();

        thirdPersonCamera.ApplyMainGameOrbitDefaults();
        thirdPersonCamera.SetFollowTarget(PlayerTransform);
        camera.transform.position = PlayerTransform.position + new Vector3(0f, 1.9f, -4.2f);
        camera.transform.rotation = Quaternion.LookRotation((PlayerTransform.position + new Vector3(0f, 1.2f, 0f)) - camera.transform.position, Vector3.up);
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private void EnsureArena()
    {
        if (_arenaRoot != null || !_buildMissingSceneObjectsAtRuntime)
            return;

        _arenaRoot = CreateArena();
    }

    private Transform CreateArena()
    {
        Transform root = new GameObject("Weapon Sandbox Arena").transform;
        _arenaRoot = root;
        CreateFloorTile("Sandbox Floor", Vector3.zero, new Vector3(52f, 0.12f, 52f), new Color(0.12f, 0.13f, 0.14f, 1f), root);
        CreateZone("Zone 1 - Single Target", new Vector3(0f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.18f, 0.26f, 0.32f, 1f));
        CreateZone("Zone 2 - Group Damage", new Vector3(18f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.24f, 0.22f, 0.14f, 1f));
        CreateZone("Zone 3 - Moving Targets", new Vector3(-18f, 0.01f, 14f), new Vector3(14f, 0.08f, 12f), new Color(0.16f, 0.24f, 0.17f, 1f));
        CreateZone("Zone 4 - Elite Boss", new Vector3(0f, 0.01f, -16f), new Vector3(16f, 0.08f, 12f), new Color(0.28f, 0.18f, 0.18f, 1f));
        CreateZone("Zone 5 - Knockback Lane", new Vector3(18f, 0.01f, -16f), new Vector3(14f, 0.08f, 18f), new Color(0.18f, 0.18f, 0.29f, 1f));
        CreateZone("Zone 6/7 - Heat and Upgrade", new Vector3(-18f, 0.01f, -16f), new Vector3(14f, 0.08f, 18f), new Color(0.24f, 0.16f, 0.24f, 1f));
        CreateKnockbackLines(new Vector3(18f, 0.08f, -22f));
        return root;
    }

    private void CreateZone(string label, Vector3 center, Vector3 scale, Color color)
    {
        CreateFloorTile(label + " Tile", center, scale, color, _arenaRoot);
        CreateWorldLabel(label, center + new Vector3(0f, 0.25f, -scale.z * 0.45f));
    }

    private void CreateFloorTile(string name, Vector3 center, Vector3 scale, Color color, Transform parent)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = name;
        if (parent != null)
            tile.transform.SetParent(parent);
        tile.transform.position = center;
        tile.transform.localScale = scale;
        Renderer renderer = tile.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateRuntimeMaterial(color);
    }

    private void CreateKnockbackLines(Vector3 laneStart)
    {
        for (int i = 0; i <= 8; i++)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = $"Knockback Distance {i * 2}m";
            if (_arenaRoot != null)
                line.transform.SetParent(_arenaRoot);
            line.transform.position = laneStart + Vector3.forward * (i * 2f);
            line.transform.localScale = new Vector3(9f, 0.08f, 0.08f);
            line.GetComponent<Renderer>().sharedMaterial = CreateRuntimeMaterial(new Color(0.85f, 0.9f, 1f, 1f));
            CreateWorldLabel($"{i * 2}m", line.transform.position + Vector3.right * 5f + Vector3.up * 0.2f);
        }
    }

    private void CreateWorldLabel(string text, Vector3 position)
    {
        GameObject go = new GameObject(text + " Label");
        if (_arenaRoot != null)
            go.transform.SetParent(_arenaRoot);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
        TextMeshPro label = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = 3f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
    }

    private Material CreateRuntimeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static Transform FindChildByNameContains(Transform root, string text)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.Contains(text, System.StringComparison.OrdinalIgnoreCase))
                return child;

            Transform nested = FindChildByNameContains(child, text);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static void DisableProductionRuntimeComponents(GameObject playerGo)
    {
        WeaponManager weaponManager = playerGo.GetComponent<WeaponManager>();
        if (weaponManager != null)
            weaponManager.enabled = false;

        PlayerAutoAttack autoAttack = playerGo.GetComponent<PlayerAutoAttack>();
        if (autoAttack != null)
            autoAttack.enabled = false;

        LevelUpOrchestrator levelUpOrchestrator = playerGo.GetComponent<LevelUpOrchestrator>();
        if (levelUpOrchestrator != null)
            levelUpOrchestrator.enabled = false;

        RunStartWeaponChoice runStartWeaponChoice = playerGo.GetComponent<RunStartWeaponChoice>();
        if (runStartWeaponChoice != null)
            runStartWeaponChoice.enabled = false;

        OverheatManager overheatManager = playerGo.GetComponent<OverheatManager>();
        if (overheatManager != null)
            overheatManager.enabled = false;

        WeaponDebugMonitor monitor = playerGo.GetComponent<WeaponDebugMonitor>();
        if (monitor != null)
            monitor.enabled = false;
    }

    private GameObject CreateFallbackPlayer()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = new Vector3(0f, 1.1f, 0f);
        go.name = "WeaponSandboxPlayer";
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.freezeRotation = true;
        go.AddComponent<PlayerStats>();
        return go;
    }

    private static bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < WeaponSlots;
    }

    private static bool IsFireHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static bool IsAbilityPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    private static bool IsAbilityHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.isPressed;
#else
        return Input.GetKey(KeyCode.Q);
#endif
    }

    private static bool IsAbilityReleased()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasReleasedThisFrame;
#else
        return Input.GetKeyUp(KeyCode.Q);
#endif
    }

    private void TickActiveAbilityInput(WeaponInstance manual, float deltaTime)
    {
        if (manual == null || manual.State != WeaponState.Manual)
            return;

        IWeaponBehaviour behaviour = _behaviours[_manualSlot];
        if (behaviour is not IHoldActiveAbilityBehaviour holdAbility)
        {
            if (IsAbilityPressed())
                UseActiveAbility();
            return;
        }

        if (IsAbilityPressed())
            holdAbility.BeginActiveAbility(_currentAimDirection);
        if (IsAbilityHeld() && holdAbility.IsActiveAbilityCharging)
            holdAbility.TickActiveAbility(deltaTime, _currentAimDirection);
        if (!IsAbilityReleased() || !holdAbility.IsActiveAbilityCharging)
            return;

        float before = manual.CurrentAmmo;
        holdAbility.ReleaseActiveAbility(_currentAimDirection);
        float spent = Mathf.Max(0f, before - manual.CurrentAmmo);
        if (spent > 0f)
        {
            Metrics.RecordAmmoConsumed(spent);
            Metrics.RecordActiveAbilityUse();
        }
    }

    private void CancelHeldAbilities()
    {
        for (int i = 0; i < _behaviours.Length; i++)
        {
            if (_behaviours[i] is IHoldActiveAbilityBehaviour holdAbility)
                holdAbility.CancelActiveAbility();
        }
    }
}
