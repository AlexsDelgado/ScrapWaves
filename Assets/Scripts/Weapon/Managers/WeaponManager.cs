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
    [SerializeField, Tooltip("Center-screen aim resolver. Empty uses a ReticleAimProvider on the same player if one exists.")]
    private ReticleAimProvider _reticleAimProvider;
    [SerializeField] private WeaponPresentationController _presentationController;

    [SerializeField, Min(0f), Tooltip("How long the body briefly faces reticle aim when manual fire starts.")]
    private float _aimFacingHoldTime = 0.08f;

    [SerializeField] private float _manualCycleCooldown = 1.25f;
    [SerializeField] private float _singleWeaponCycleCooldown = 2.5f;

    private readonly List<IWeaponBehaviour> _equipped = new();
    private int _currentManualIndex;
    private float _manualCooldownTimer;

    private PlayerStats _stats;
    private PlayerMovement _movement;
    private HeatManager _heat;
    private IWeaponTargeting _targeting;

    // Initializes dependencies and equips configured starter weapons.
    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _movement = GetComponent<PlayerMovement>();
        if (_reticleAimProvider == null)
            _reticleAimProvider = GetComponent<ReticleAimProvider>();
        if (_presentationController == null)
            _presentationController = GetComponent<WeaponPresentationController>();

        _heat = HeatManager.GetInstance();
        _targeting = new ConfiguredEnemyTargeting();
        AddStartingWeapons();
    }

    // Updates automatic fire, manual input, and cycle cooldown.
    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        Vector3 aimDirection = GetAimDirection();
        UpdateAutomaticWeapons(Time.deltaTime, aimDirection);
        UpdateManualWeapon(Time.deltaTime, aimDirection);
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

    // Returns the active manual behavior so presentation can read weapon-specific status.
    public IWeaponBehaviour GetCurrentManualBehaviour()
    {
        if (_equipped.Count == 0)
            return null;
        return _equipped[_currentManualIndex];
    }

    public Transform GetProjectileSpawn()
    {
        return _projectileSpawn != null ? _projectileSpawn : transform;
    }

    // Adds weapon instance and creates behavior via factory method.
    public bool AddWeapon(WeaponData data)
    {
        if (!CanAddWeapon() || data == null)
            return false;

        WeaponInstance instance = new() { Data = data, State = WeaponState.Automatic };
        if (data.PresentationProfile != null)
            _presentationController?.SetProfile(data.PresentationProfile);
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

    // Returns equipped instance matching weapon data, if any.
    public bool TryGetEquippedWeapon(WeaponData data, out WeaponInstance instance)
    {
        instance = null;
        if (data == null)
            return false;

        for (int i = 0; i < _equipped.Count; i++)
        {
            WeaponInstance runtime = _equipped[i].Runtime;
            if (runtime?.Data == data)
            {
                instance = runtime;
                return true;
            }
        }

        return false;
    }

    // Adds a new weapon or upgrades an existing copy.
    public bool TryAddOrUpgradeWeapon(WeaponData data)
    {
        if (data == null)
            return false;

        if (TryGetEquippedWeapon(data, out WeaponInstance existing))
        {
            UpgradeWeapon(existing);
            return true;
        }

        return AddWeapon(data);
    }

    // Applies selected advanced path when level requirement is met (Lv6+),
    // or when advancing from Lv5 via Advanced Tinkering (caller upgrades first).
    public void ApplyUpgradePath(WeaponInstance weapon, WeaponUpgradePath path)
    {
        if (weapon == null || path == WeaponUpgradePath.None)
            return;
        if (weapon.Level < 6)
            return;
        weapon.SelectedPath = path;
    }

    // Returns current manual index for debug and UI usage.
    public int GetCurrentManualWeaponIndex() => _currentManualIndex;

    /// <summary>
    /// Maps a HUD rotation slot to equipped list index: 0 = current manual, 1 = next in cycle, etc.
    /// Returns -1 when the slot has no weapon (rotation offset beyond equipped count).
    /// </summary>
    public int GetEquippedIndexForRotationSlot(int rotationSlot)
    {
        if (_equipped.Count == 0 || rotationSlot < 0)
            return -1;
        if (rotationSlot >= _equipped.Count)
            return -1;
        return (_currentManualIndex + rotationSlot) % _equipped.Count;
    }

    // Returns manual cycle cooldown remaining for debug and UI usage.
    public float GetManualCooldownRemaining() => Mathf.Max(0f, _manualCooldownTimer);

    public float GetManualCycleCooldownDuration()
    {
        if (_equipped.Count <= 1)
            return _singleWeaponCycleCooldown;
        return _manualCycleCooldown;
    }

    public float GetManualCooldownNormalized()
    {
        float duration = GetManualCycleCooldownDuration();
        if (duration <= 0f || _manualCooldownTimer <= 0f)
            return 1f;
        return 1f - Mathf.Clamp01(_manualCooldownTimer / duration);
    }

    public float GetAbilityCooldownNormalized()
    {
        WeaponInstance weapon = GetCurrentManualWeapon();
        if (weapon?.Data == null)
            return 1f;

        float duration = Mathf.Max(0.01f, WeaponMath.GetAbilityCooldownDuration(weapon, _stats));
        if (weapon.AbilityCooldownTimer <= 0f)
            return 1f;

        return 1f - Mathf.Clamp01(weapon.AbilityCooldownTimer / duration);
    }

    public bool CanUseAbility()
    {
        WeaponInstance weapon = GetCurrentManualWeapon();
        if (weapon?.Data == null || weapon.State != WeaponState.Manual)
            return false;
        if (weapon.AbilityCooldownTimer > 0f)
            return false;
        float ammoCost = WeaponMath.GetActiveAbilityAmmoCost(weapon);
        return ammoCost <= 0f || weapon.CurrentAmmo > 0f;
    }

    private static void TickAbilityCooldown(WeaponInstance weapon, float deltaTime)
    {
        if (weapon == null || weapon.AbilityCooldownTimer <= 0f)
            return;
        weapon.AbilityCooldownTimer = Mathf.Max(0f, weapon.AbilityCooldownTimer - deltaTime);
    }

    // Creates starter inventory from configured weapon assets.
    // When RunStartWeaponChoice is present, the first weapon comes only from that menu.
    private void AddStartingWeapons()
    {
        if (HasRunStartWeaponChoice())
            return;

        for (int i = 0; i < _startingWeapons.Count && i < MaxWeaponSlots; i++)
            AddWeapon(_startingWeapons[i]);
    }

    private bool HasRunStartWeaponChoice()
    {
        RunStartWeaponChoice choice = GetComponent<RunStartWeaponChoice>();
        return choice != null && choice.isActiveAndEnabled;
    }

    // Removes every equipped weapon so a run can start from an empty loadout.
    public void ClearEquippedWeapons()
    {
        _equipped.Clear();
        _currentManualIndex = 0;
        _manualCooldownTimer = 0f;
    }

    // Creates concrete behavior for each weapon type.
    private IWeaponBehaviour CreateBehaviour(WeaponData data)
    {
        Transform spawn = _projectileSpawn != null ? _projectileSpawn : transform;
        IWeaponPresentationSink presentationSink = _presentationController != null
            ? _presentationController
            : NullWeaponPresentationSink.Instance;
        return WeaponBehaviourFactory.Create(
            data,
            _targeting,
            _projectilePool,
            spawn,
            _movement,
            presentationSink);
    }

    // Ticks automatic mode for every non-manual equipped weapon.
    private void UpdateAutomaticWeapons(float deltaTime, Vector3 aimDirection)
    {
        for (int i = 0; i < _equipped.Count; i++)
        {
            if (_equipped[i].Runtime == null || _equipped[i].Runtime.State != WeaponState.Automatic)
                continue;

            _equipped[i].TickAutomatic(deltaTime, aimDirection);
        }
    }

    // Routes input and active ability usage to manual weapon.
    private void UpdateManualWeapon(float deltaTime, Vector3 aimDirection)
    {
        if (_equipped.Count == 0)
            return;

        bool fireHeld = IsFireHeld();
        bool firePressed = IsFirePressed();
        bool abilityPressed = IsAbilityPressed();
        bool abilityHeld = IsAbilityHeld();
        bool abilityReleased = IsAbilityReleased();

        IWeaponBehaviour manual = _equipped[_currentManualIndex];
        if (manual.Runtime.State == WeaponState.Manual && (firePressed || abilityPressed))
            _movement?.RequestAimFacing(aimDirection, _aimFacingHoldTime);

        TickAbilityCooldown(manual.Runtime, deltaTime);
        manual.TickManual(deltaTime, aimDirection, fireHeld);

        if (manual is IHoldActiveAbilityBehaviour holdAbility)
        {
            if (abilityPressed && CanUseAbility())
                holdAbility.BeginActiveAbility(aimDirection);
            if (abilityHeld && holdAbility.IsActiveAbilityCharging)
                holdAbility.TickActiveAbility(deltaTime, aimDirection);
            if (abilityReleased && holdAbility.IsActiveAbilityCharging)
                holdAbility.ReleaseActiveAbility(aimDirection);
        }
        else if (abilityPressed && CanUseAbility())
        {
            manual.UseActiveAbility(aimDirection);
        }

        if (manual.Runtime.State == WeaponState.Manual && manual.Runtime.CurrentAmmo <= 0f)
            EndManualMode();
    }

    private Vector3 GetAimDirection()
    {
        Transform spawn = _projectileSpawn != null ? _projectileSpawn : transform;
        WeaponInstance manualWeapon = GetCurrentManualWeapon();
        float fallbackDistance = manualWeapon?.Data != null ? manualWeapon.Data.BaseRange : 0f;
        bool preferDamageableAimPoint = ShouldPreferDamageableAimPoint(manualWeapon);
        if (_reticleAimProvider != null && _reticleAimProvider.TryGetAimDirection(spawn.position, fallbackDistance, preferDamageableAimPoint, out Vector3 aimDirection))
            return aimDirection.normalized;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera.transform.forward;

        return transform.forward;
    }

    private static bool ShouldPreferDamageableAimPoint(WeaponInstance weapon)
    {
        if (weapon?.Data == null)
            return false;

        return weapon.Data.WeaponType == WeaponType.AutomaticCannon
            || weapon.Data.WeaponType == WeaponType.RocketLauncher;
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

    private bool IsFirePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
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

    private bool IsAbilityHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.isPressed;
#else
        return Input.GetKey(KeyCode.Q);
#endif
    }

    private bool IsAbilityReleased()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasReleasedThisFrame;
#else
        return Input.GetKeyUp(KeyCode.Q);
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

        CancelHeldAbilities();
        _currentManualIndex = Mathf.Clamp(index, 0, _equipped.Count - 1);
        _manualCooldownTimer = 0f;

        for (int i = 0; i < _equipped.Count; i++)
        {
            WeaponInstance equippedRuntime = _equipped[i].Runtime;
            if (equippedRuntime != null)
                equippedRuntime.State = i == _currentManualIndex ? WeaponState.Manual : WeaponState.Automatic;
        }

        WeaponInstance runtime = _equipped[_currentManualIndex].Runtime;
        if (runtime != null)
            runtime.CurrentAmmo = WeaponMath.GetMaxManualAmmo(runtime, _stats);
    }

    // Returns current manual weapon to automatic and immediately selects the next slot.
    private void EndManualMode()
    {
        if (_equipped.Count == 0)
            return;

        WeaponInstance runtime = _equipped[_currentManualIndex].Runtime;
        if (runtime == null || runtime.State != WeaponState.Manual)
            return;

        runtime.State = WeaponState.Automatic;
        int next = (_currentManualIndex + 1) % _equipped.Count;
        StartManualMode(next);
    }

    private void CancelHeldAbilities()
    {
        for (int i = 0; i < _equipped.Count; i++)
        {
            if (_equipped[i] is IHoldActiveAbilityBehaviour holdAbility)
                holdAbility.CancelActiveAbility();
        }
    }
}
