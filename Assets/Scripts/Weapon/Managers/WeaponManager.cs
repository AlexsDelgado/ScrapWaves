using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class WeaponManager : MonoBehaviour
{
    public const int MaxWeaponSlots = 3;

    [SerializeField] private List<WeaponData> _startingWeapons = new();
    [SerializeField] private Transform _projectileSpawn;
    [SerializeField] private ProjectilePool _projectilePool;
    [SerializeField] private float _manualCycleCooldown = 1.25f;
    [SerializeField] private float _singleWeaponCycleCooldown = 2.5f;

    [SerializeField, Tooltip("Cámara de apuntado. Si está vacío, se busca en la escena.")]
    private camera_Aim _aimCamera;

    private readonly List<IWeaponBehaviour> _equipped = new();
    private int _currentManualIndex;
    private float _manualCooldownTimer;

    private PlayerStats _stats;
    private HeatManager _heat;
    private IWeaponTargeting _targeting;

    // Initializes dependencies and equips configured starter weapons.
    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _heat = HeatManager.GetInstance();
        _targeting = new ClosestEnemyTargeting();

        if (_projectilePool == null)
            _projectilePool = FindFirstObjectByType<ProjectilePool>();

        if (_aimCamera == null)
            _aimCamera = FindFirstObjectByType<camera_Aim>();

        AddStartingWeapons();
    }

    // Updates automatic fire, manual input, and cycle cooldown.
    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        UpdateAutomaticWeapons(Time.deltaTime);
        UpdateManualWeapon(Time.deltaTime);
        UpdateManualCycle(Time.deltaTime);
    }

    // Returns equipped weapon behaviors in immutable list form.
    public IReadOnlyList<IWeaponBehaviour> GetEquippedWeapons() => _equipped;

    // Returns currently manual weapon runtime, or null.
    public WeaponInstance GetCurrentManualWeapon()
    {
        if (_equipped.Count == 0)
            return null;
        return _equipped[_currentManualIndex].Runtime;
    }

    // Adds weapon instance and creates behavior via factory method.
    public bool AddWeapon(WeaponData data)
    {
        if (!CanAddWeapon() || data == null)
            return false;

        WeaponInstance instance = new() { Data = data, State = WeaponState.Automatic };
        IWeaponBehaviour behaviour = CreateBehaviour(data);
        behaviour.Setup(instance, transform, _stats, _heat);
        _equipped.Add(behaviour);

        if (_equipped.Count == 1)
            StartManualMode(0);

        return true;
    }

    // Returns true if inventory still has a free slot.
    public bool CanAddWeapon() => _equipped.Count < MaxWeaponSlots;

    // Increases weapon level by one within level cap.
    public void UpgradeWeapon(WeaponInstance weapon)
    {
        if (weapon == null)
            return;
        weapon.Level = Mathf.Clamp(weapon.Level + 1, 1, 10);
    }

    // Applies selected advanced path when level requirement is met.
    public void ApplyUpgradePath(WeaponInstance weapon, WeaponUpgradePath path)
    {
        if (weapon == null || weapon.Level < 6 || path == WeaponUpgradePath.None)
            return;
        weapon.SelectedPath = path;
    }

    // Returns current manual index for debug and UI usage.
    public int GetCurrentManualWeaponIndex() => _currentManualIndex;

    // Returns manual cycle cooldown remaining for debug and UI usage.
    public float GetManualCooldownRemaining() => Mathf.Max(0f, _manualCooldownTimer);

    // Creates starter inventory from configured weapon assets.
    private void AddStartingWeapons()
    {
        for (int i = 0; i < _startingWeapons.Count && i < MaxWeaponSlots; i++)
            AddWeapon(_startingWeapons[i]);
    }

    // Creates concrete behavior for each weapon type.
    private IWeaponBehaviour CreateBehaviour(WeaponData data)
    {
        Transform spawn = _projectileSpawn != null ? _projectileSpawn : transform;
        return data.WeaponType switch
        {
            WeaponType.AutomaticCannon => new AutomaticCannonWeapon(_targeting, _projectilePool, spawn),
            WeaponType.RocketLauncher => new RocketLauncherWeapon(_targeting, _projectilePool, spawn),
            _ => new BasicProjectileWeapon(_targeting, _projectilePool, spawn)
        };
    }

    // Ticks automatic mode for every non-manual equipped weapon.
    private void UpdateAutomaticWeapons(float deltaTime)
    {
        for (int i = 0; i < _equipped.Count; i++)
        {
            if (i == _currentManualIndex)
                continue;
            _equipped[i].TickAutomatic(deltaTime);
        }
    }

    // Routes input and active ability usage to manual weapon.
    private void UpdateManualWeapon(float deltaTime)
    {
        if (_equipped.Count == 0)
            return;

        Vector3 aimDirection = GetManualAimDirection();
        bool fireHeld = IsFireHeld();
        bool abilityPressed = IsAbilityPressed();

        IWeaponBehaviour manual = _equipped[_currentManualIndex];
        manual.TickManual(deltaTime, aimDirection, fireHeld);

        if (abilityPressed)
            manual.UseActiveAbility(aimDirection);

        if (manual.Runtime.State == WeaponState.Manual && manual.Runtime.CurrentAmmo <= 0f)
            EndManualMode();
    }


    private Vector3 GetManualAimDirection()
    {
        Transform spawn = _projectileSpawn != null ? _projectileSpawn : transform;

        if (_aimCamera != null)
            return _aimCamera.GetAimDirectionFrom(spawn.position);

        if (Camera.main != null)
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 toTarget = ray.GetPoint(300f) - spawn.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                return toTarget.normalized;
            return ray.direction;
        }

        return transform.forward;
    }

    // Reads primary fire state from active input backend.
    private bool IsFireHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    // Reads ability press state from active input backend.
    private bool IsAbilityPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    // Moves manual index after cooldown expires.
    private void UpdateManualCycle(float deltaTime)
    {
        if (_manualCooldownTimer <= 0f)
            return;

        _manualCooldownTimer -= deltaTime;
        if (_manualCooldownTimer > 0f)
            return;

        int next = _equipped.Count == 0 ? 0 : (_currentManualIndex + 1) % _equipped.Count;
        StartManualMode(next);
    }

    // Activates manual state and refills ammo from runtime formulas.
    private void StartManualMode(int index)
    {
        if (_equipped.Count == 0)
            return;

        _currentManualIndex = Mathf.Clamp(index, 0, _equipped.Count - 1);
        WeaponInstance runtime = _equipped[_currentManualIndex].Runtime;
        runtime.State = WeaponState.Manual;
        runtime.CurrentAmmo = WeaponMath.GetMaxManualAmmo(runtime, _stats);
    }

    // Returns current manual weapon to automatic and starts cooldown.
    private void EndManualMode()
    {
        if (_equipped.Count == 0)
            return;

        WeaponInstance runtime = _equipped[_currentManualIndex].Runtime;
        if (runtime.State != WeaponState.Manual)
            return;

        runtime.State = WeaponState.Automatic;
        _manualCooldownTimer = _equipped.Count == 1 ? _singleWeaponCycleCooldown : _manualCycleCooldown;
    }
}