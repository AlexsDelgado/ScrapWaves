using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns only the lesson and its authored world objects; hands the same player to the normal run.</summary>
[DefaultExecutionOrder(-250)]
public sealed class TutorialDirector : MonoBehaviour
{
    public static TutorialDirector Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance._active;
    public static bool AbilityAllowed => !IsActive || Instance.Step >= TutorialStep.Heat;
    public static bool ScriptedExperienceAllowed => !IsActive || Instance._grantingLevel;
    private static bool _replayRequested;
    public static void RequestReplay() => _replayRequested = true;
    public static void CancelReplayRequest() => _replayRequested = false;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; _replayRequested = false; }

    [SerializeField] private TutorialDefinition _definition;
    [SerializeField] private TutorialHudView _hud;
    [SerializeField] private GameObject _world;
    [SerializeField] private Transform _spawn;
    [SerializeField] private Transform _markerCenter;
    [SerializeField] private GameObject _marker;
    [SerializeField] private CraftingStation _station;
    [SerializeField] private TutorialEnemy[] _firstEnemies;
    [SerializeField] private TutorialEnemy[] _secondEnemies;
    [SerializeField] private TutorialEnemy[] _lastEnemies;
    [SerializeField] private MapLaunchPad _vent;
    [SerializeField] private PlayerMovement _player;
    [SerializeField] private WeaponManager _weapons;
    [SerializeField] private WeaponCraftingService _crafting;
    [SerializeField] private CraftingUI _craftingUi;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private PlayerXP _xp;
    [SerializeField] private LevelUpOrchestrator _levels;
    [SerializeField] private GuideArrow _arrow;
    private readonly List<(Behaviour component, bool enabled)> _normalSystems = new();
    private readonly HashSet<TutorialEnemy> _alive = new();
    private readonly HashSet<TutorialPickup> _pendingDrops = new();
    private readonly Dictionary<TutorialPickup, int> _amounts = new();
    private bool _active, _begun, _grantingLevel;
    private int _requestedLevel;
    private float _lastRecovery;
    public TutorialStep Step { get; private set; }
    public MaterialInventory Inventory => _inventory;
    public int RemainingEnemies => _alive.Count;
    public int RemainingPickups => _pendingDrops.Count;
    public bool HasReferences => _definition != null && _definition.StartingWeapon != null && _hud != null && _hud.HasReferences
        && _world != null && _spawn != null && _markerCenter != null && _marker != null && _station != null && _vent != null
        && _player != null && _weapons != null && _crafting != null && _craftingUi != null && _inventory != null && _xp != null && _levels != null;

    private void Awake()
    {
        Instance = this;
        bool requested = _replayRequested; _replayRequested = false;
        _active = requested || (SaveManager.Instance != null && SaveManager.Instance.ShouldAutoPlayTutorial);
        if (!_active) { if (_world != null) _world.SetActive(false); return; }
        if (!HasReferences)
        {
            Debug.LogError("Tutorial is missing authored references. Normal startup retained; tutorial eligibility is unchanged.", this);
            _active = false; if (_world != null) _world.SetActive(false); return;
        }
        HoldNormalSystems();
        _world.SetActive(false);
        PlacePlayer(_spawn.position);
        _player.transform.rotation = _spawn.rotation;
        var body = _player.GetComponent<Rigidbody>();
        if (body != null) body.rotation = _spawn.rotation;
        Physics.SyncTransforms();
    }

    public void Begin()
    {
        if (!_active || _begun) return;
        _begun = true;
        _weapons.ClearEquippedWeapons();
        _weapons.AddWeapon(_definition.StartingWeapon);
        _marker.SetActive(false);
        _world.SetActive(true);
        _vent.gameObject.SetActive(false);
        _vent.OnLaunched += Complete;
        ConfigureGrants();
        SetStep(TutorialStep.Welcome);
    }

    private void HoldNormalSystems()
    {
        Hold<OrbitalSpawner>(); Hold<SwarmSpawner>(); Hold<ZoneSpawner>();
        Hold<GuideArrowController>(); Hold<DialogueDirector>();
        foreach (var box in FindObjectsByType<DialogueBoxUI>(FindObjectsSortMode.None)) box.HideImmediate();
    }
    private void Hold<T>() where T : Behaviour
    {
        foreach (T component in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { _normalSystems.Add((component, component.enabled)); component.enabled = false; }
    }
    private void RestoreNormalSystems()
    {
        foreach (var saved in _normalSystems) if (saved.component != null) saved.component.enabled = saved.enabled;
        _normalSystems.Clear();
    }
    private void ConfigureGrants()
    {
        var upgrade = _crafting.GetUpgradeCost(_definition.StartingWeapon, WeaponUpgradePath.None, 2);
        var tinker = _crafting.GetTinkeringCost(2);
        if (upgrade.Count == 0) throw new InvalidOperationException("Tutorial requires a configured level-2 crafting recipe.");
        foreach (var actor in _firstEnemies)
            foreach (var drop in actor.Drops)
            {
                int price = FindCost(upgrade, drop.Material);
                _amounts[drop] = price > 0 ? TutorialDefinition.GrantAmount(price, drop.Material == MaterialType.MetalPipe ? _definition.FirstUpgradePipes : 0) : 0;
            }
        foreach (var actor in _secondEnemies)
            foreach (var drop in actor.Drops)
            {
                int price = drop.Material == actor.PrimaryMaterial ? FindCost(tinker, drop.Material) : 0;
                _amounts[drop] = price > 0 ? TutorialDefinition.GrantAmount(price, _definition.TinkerCommonEach) : 0;
            }
        // Preserve solvability if designers extend the slot-2 recipe with another material.
        // Every actor already has serialized pickup slots for every material type.
        foreach (var cost in tinker)
        {
            int granted = 0; TutorialPickup fallback = null;
            foreach (var actor in _secondEnemies)
                foreach (var drop in actor.Drops)
                    if (drop.Material == cost.Material) { granted += _amounts[drop]; if (fallback == null) fallback = drop; }
            if (granted < cost.Amount && fallback != null) _amounts[fallback] += cost.Amount - granted;
        }
        foreach (var actor in _lastEnemies)
            foreach (var drop in actor.Drops) _amounts[drop] = drop.Material == actor.PrimaryMaterial ? 1 : 0;
    }
    private static int FindCost(IReadOnlyList<MaterialCost> cost, MaterialType type)
    { foreach (var entry in cost) if (entry.Material == type) return entry.Amount; return 0; }

    private void Update()
    {
        if (!_active || !_begun || (GameManager.Instance != null && !GameManager.Instance.IsPlaying)) return;
        UpdateSuccessfulActions(); // Real menu purchases complete while the world is paused.
        if (GameplayPause.IsUiPaused || Time.timeScale <= 0 || (!Application.isFocused && !Application.isBatchMode)) return;
        RecoverPlayer();
        if (_definition.Page(Step)?.AdvanceWithE == true && TutorialInput.ConsumeAdvance()) AdvancePage();
        if (Step == TutorialStep.Movement && PlanarDistance(_player.transform.position, _markerCenter.position) <= _definition.MarkerRadius
            && Mathf.Abs(_player.transform.position.y - _markerCenter.position.y) < 3f)
        { _marker.SetActive(false); SetStep(TutorialStep.FirstEnemy); Activate(_firstEnemies); }
        if (Step == TutorialStep.FirstEnemy || Step == TutorialStep.Heat || Step == TutorialStep.Ability || Step == TutorialStep.AutomaticCombat)
            _hud.SetProgress($"Enemies remaining: {_alive.Count}");
        else if (Step == TutorialStep.FirstPickup || Step == TutorialStep.HordePickup || Step == TutorialStep.FinalPickup)
            _hud.SetProgress($"Collect the highlighted materials: {_pendingDrops.Count} remaining");
    }
    private void UpdateSuccessfulActions()
    {
        if (Step == TutorialStep.Upgrade && _weapons.TryGetEquippedWeapon(_definition.StartingWeapon, out var weapon) && weapon.Level >= 2)
        {
            if (!_craftingUi.CloseForTutorial()) return;
            SetStep(TutorialStep.Heat); Activate(_secondEnemies);
        }
        else if (Step == TutorialStep.Tinker && _weapons.GetEquippedWeapons().Count >= 2 && !_crafting.HasPendingTinkeringChoice)
        {
            if (!_craftingUi.CloseForTutorial()) return;
            SetStep(TutorialStep.AutomaticCombat); Activate(_lastEnemies);
        }
        else if (Step == TutorialStep.ItemChoice && _requestedLevel > 0 && _levels.LastCompletedLevel >= _requestedLevel)
        { _vent.gameObject.SetActive(true); SetStep(TutorialStep.Escape); }
    }
    public void AdvancePage()
    {
        if (Step == TutorialStep.Welcome) SetStep(TutorialStep.Movement);
        else if (Step == TutorialStep.Heat) SetStep(TutorialStep.Ability);
        else if (Step == TutorialStep.Escape) SetStep(TutorialStep.Vent);
    }
    private void Activate(TutorialEnemy[] actors)
    {
        _alive.Clear(); _pendingDrops.Clear();
        foreach (var actor in actors) { _alive.Add(actor); actor.Begin(this); }
    }
    public void NotifyKilled(TutorialEnemy actor, Vector3 position)
    {
        if (!_active || !_alive.Remove(actor)) return;
        if (PlanarDistance(position, _markerCenter.position) > _definition.RecoveryRadius - 6f)
            position = _markerCenter.position;
        int index = 0;
        foreach (var drop in actor.Drops)
        {
            if (!_amounts.TryGetValue(drop, out int amount) || amount <= 0) continue;
            _pendingDrops.Add(drop);
            Vector3 point = position + new Vector3((index++ % 3 - 1) * .55f, .65f, .25f);
            drop.Arm(this, amount, point);
        }
        EvaluateCombatCompletion();
    }
    public void NotifyCollected(TutorialPickup pickup)
    { if (_active && _pendingDrops.Remove(pickup)) EvaluateCombatCompletion(); }
    private void EvaluateCombatCompletion()
    {
        if (_alive.Count != 0) return;
        if (Step == TutorialStep.FirstEnemy) SetStep(TutorialStep.FirstPickup);
        else if (Step == TutorialStep.Heat || Step == TutorialStep.Ability) SetStep(TutorialStep.HordePickup);
        else if (Step == TutorialStep.AutomaticCombat) SetStep(TutorialStep.FinalPickup);
        if (_pendingDrops.Count != 0) return;
        if (Step == TutorialStep.FirstPickup) SetStep(TutorialStep.Upgrade);
        else if (Step == TutorialStep.HordePickup) SetStep(TutorialStep.Tinker);
        else if (Step == TutorialStep.FinalPickup)
        {
            SetStep(TutorialStep.ItemChoice);
            _requestedLevel = _xp.CurrentLevel + 1;
            _grantingLevel = true;
            try { _xp.AddExperience(_xp.XpRequiredForCurrentLevel - _xp.XpTowardsNext); }
            finally { _grantingLevel = false; }
        }
    }
    private void SetStep(TutorialStep step)
    {
        Step = step;
        _marker.SetActive(step == TutorialStep.Movement);
        if (step == TutorialStep.ItemChoice) _hud.Hide(); // Its explanation belongs to the authored three-card choice window.
        else _hud.Show(_definition.Page(step));
        _arrow?.Hide();
        if (step == TutorialStep.Upgrade || step == TutorialStep.Tinker) _arrow?.Show(_station.transform);
        if (step == TutorialStep.Movement) _arrow?.Show(_markerCenter);
        if (step == TutorialStep.Vent) _arrow?.Show(_vent.transform);
    }
    public static bool AllowsStation(CraftingStation station) => !IsActive || (Instance._station == station && (Instance.Step == TutorialStep.Upgrade || Instance.Step == TutorialStep.Tinker));
    public static bool AllowsCrafting(CraftingActionKind kind, WeaponData weapon = null, int targetLevel = 0)
    {
        if (!IsActive) return true;
        return (Instance.Step == TutorialStep.Upgrade && kind == CraftingActionKind.UpgradeLevel && weapon == Instance._definition.StartingWeapon && targetLevel == 2)
            || (Instance.Step == TutorialStep.Tinker && kind == CraftingActionKind.TinkerNewWeapon);
    }
    public static bool AllowsLaunch(MapLaunchPad pad) => !IsActive || (Instance._vent == pad && Instance.Step == TutorialStep.Vent);
    private void Complete(PlayerMovement player)
    {
        if (!_active || player != _player || Step != TutorialStep.Vent) return;
        Step = TutorialStep.Complete;
        _hud.Hide(); _arrow?.Hide();
        HeatManager.GetInstance()?.ResetHeatProgressAndEscalation();
        SaveManager.Instance?.CompleteGameplayTutorial();
        _active = false;
        RunSessionStats.Reset(); RunCombatStats.Reset(); ChallengeProgressTracker.ResetRunScratch();
        _world.SetActive(false); // Launch velocity/flight time are already owned by PlayerMovement.
        RestoreNormalSystems();
    }
    private void RecoverPlayer()
    {
        if (_player.IsLaunching || Time.unscaledTime < _lastRecovery + 1f) return;
        if (_player.transform.position.y < _spawn.position.y - 6f || PlanarDistance(_player.transform.position, _markerCenter.position) > _definition.RecoveryRadius)
        { _lastRecovery = Time.unscaledTime; PlacePlayer(_spawn.position); }
    }
    private void PlacePlayer(Vector3 position)
    {
        var body = _player.GetComponent<Rigidbody>();
        _player.transform.position = position;
        if (body != null) { body.position = position; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
    private static float PlanarDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    private void OnDisable()
    {
        if (_vent != null) _vent.OnLaunched -= Complete;
        if (_hud != null) _hud.Hide();
        RestoreNormalSystems();
        _active = false;
        if (Instance == this) Instance = null;
    }
}
