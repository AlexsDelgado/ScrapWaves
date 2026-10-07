using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>Presentation controller for the authored crafting menu on the player prefab.</summary>
[DisallowMultipleComponent]
public class CraftingUI : MonoBehaviour
{
    [SerializeField] private CraftingMenuView _view;
    [SerializeField] private RunMenuContent _content;
    private WeaponCraftingService _crafting;
    private MaterialInventory _inventory;
    private WeaponManager _weaponManager;
    private ThirdPersonCamera _resolvedCamera;
    private Action _onClosed;
    private UnityAction[] _slotActions;
    private UnityAction[] _candidateActions;
    private float _previousTimeScale = 1f;
    private int _selectedSlot;
    private WeaponUpgradePath _offeredPath;
    private bool _isVisible;
    private bool _holdsUiPause;
    private bool _buttonsBound;
    private bool _applyingAction;
    public bool IsVisible => _isVisible;
    public bool IsChoosingWeapon => _crafting != null && _crafting.HasPendingTinkeringChoice;

    private void Awake()
    {
        BindButtons();
        if (_view != null) _view.gameObject.SetActive(false);
    }

    private bool BindButtons()
    {
        if (_buttonsBound) return true;
        if (_view == null || !_view.IsConfigured) return false;
        _view.CloseButton.onClick.AddListener(Hide);
        _view.UpgradeButton.onClick.AddListener(UpgradeSelected);
        _view.TinkerButton.onClick.AddListener(Tinker);
        _view.AcceptButton.onClick.AddListener(AcceptAdvanced);
        _view.DeclineButton.onClick.AddListener(DeclineAdvanced);
        _slotActions = new UnityAction[_view.Slots.Length];
        for (int i = 0; i < _view.Slots.Length; i++)
        {
            int slot = i;
            _slotActions[i] = () => SelectSlot(slot);
            _view.Slots[i].Button.onClick.AddListener(_slotActions[i]);
        }
        _candidateActions = new UnityAction[_view.Candidates.Length];
        for (int i = 0; i < _view.Candidates.Length; i++)
        {
            int index = i;
            _candidateActions[i] = () => SelectCandidate(index);
            _view.Candidates[i].Button.onClick.AddListener(_candidateActions[i]);
        }
        _buttonsBound = true;
        return true;
    }

    private void OnDisable()
    {
        // Forced lifecycle interruption releases presentation, but the service retains the paid choice.
        ClosePresentation();
        GameplayPause.SetHeld(ref _holdsUiPause, false);
    }
    private void OnEnable()
    {
        if (!_isVisible && IsChoosingWeapon && _inventory != null) Show(_crafting, _inventory);
    }
    private void OnDestroy()
    {
        ClosePresentation();
        if (_inventory != null) _inventory.OnInventoryChanged -= OnInventoryChanged;
        if (!_buttonsBound || _view == null) return;
        if (_view.CloseButton != null) _view.CloseButton.onClick.RemoveListener(Hide);
        if (_view.UpgradeButton != null) _view.UpgradeButton.onClick.RemoveListener(UpgradeSelected);
        if (_view.TinkerButton != null) _view.TinkerButton.onClick.RemoveListener(Tinker);
        if (_view.AcceptButton != null) _view.AcceptButton.onClick.RemoveListener(AcceptAdvanced);
        if (_view.DeclineButton != null) _view.DeclineButton.onClick.RemoveListener(DeclineAdvanced);
        for (int i = 0; i < _view.Slots.Length; i++)
            if (_view.Slots[i] != null && _view.Slots[i].Button != null) _view.Slots[i].Button.onClick.RemoveListener(_slotActions[i]);
        for (int i = 0; i < _view.Candidates.Length; i++)
            if (_view.Candidates[i]?.Button != null) _view.Candidates[i].Button.onClick.RemoveListener(_candidateActions[i]);
    }

    public IEnumerator PresentCoroutine(WeaponCraftingService crafting, MaterialInventory inventory, Action onClosed)
    {
        if (_isVisible) { onClosed?.Invoke(); yield break; }
        bool done = false;
        _onClosed = () => done = true;
        if (!Show(crafting, inventory))
        {
            _onClosed = null;
            onClosed?.Invoke();
            yield break;
        }
        while (!done) yield return null;
        onClosed?.Invoke();
    }

    private bool Show(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        if (!BindButtons() || crafting == null || inventory == null)
        {
            Debug.LogError("CraftingUI requires its authored CraftingMenu view, crafting service, and inventory.", this);
            return false;
        }
        _crafting = crafting;
        _inventory = inventory;
        _weaponManager = crafting.GetComponent<WeaponManager>();
        if (_weaponManager == null)
        {
            Debug.LogError("CraftingUI requires a WeaponManager on the crafting service owner.", this);
            return false;
        }
        _inventory.OnInventoryChanged -= OnInventoryChanged;
        _inventory.OnInventoryChanged += OnInventoryChanged;
        _isVisible = true;
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        GameplayPause.SetHeld(ref _holdsUiPause, true);
        SetCameraBlocked(true);
        _selectedSlot = Mathf.Clamp(_selectedSlot, 0, Mathf.Min(2, _weaponManager.GetEquippedWeapons().Count));
        if (IsChoosingWeapon) _selectedSlot = Mathf.Min(2, _weaponManager.GetEquippedWeapons().Count);
        SetStatus(string.Empty);
        _view.gameObject.SetActive(true);
        Refresh();
        return true;
    }

    private void Hide()
    {
        if (IsChoosingWeapon || _applyingAction) return;
        ClosePresentation();
    }
    private void ClosePresentation()
    {
        if (!_isVisible) return;
        _isVisible = false;
        if (_inventory != null) _inventory.OnInventoryChanged -= OnInventoryChanged;
        if (_view != null) _view.gameObject.SetActive(false);
        GameplayPause.SetHeld(ref _holdsUiPause, false);
        Time.timeScale = GameplayPause.IsUiPaused ? 0f : _previousTimeScale > 0f ? _previousTimeScale : 1f;
        SetCameraBlocked(false);
        Action closed = _onClosed;
        _onClosed = null;
        closed?.Invoke();
    }
    private void SetCameraBlocked(bool blocked)
    {
        if (_resolvedCamera == null) _resolvedCamera = FindFirstObjectByType<ThirdPersonCamera>();
        _resolvedCamera?.SetLookBlockedByUi(blocked);
    }
    private void OnInventoryChanged()
    {
        if (_isVisible && !_applyingAction) Refresh();
    }
    private void SelectSlot(int slot)
    {
        if (!_isVisible || IsChoosingWeapon || _applyingAction) return;
        // Crafting fills the next free equipment slot; later empty slots are not actionable yet.
        _selectedSlot = Mathf.Clamp(slot, 0, Mathf.Min(2, _weaponManager.GetEquippedWeapons().Count));
        SetStatus(string.Empty);
        Refresh();
    }
    private WeaponInstance SelectedWeapon()
    {
        if (_weaponManager == null) return null;
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        return _selectedSlot < equipped.Count ? equipped[_selectedSlot]?.Runtime : null;
    }
    private void Refresh()
    {
        if (!_isVisible) return;
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        foreach (CraftingMaterialField field in _view.Materials)
            field.AmountText.text = _inventory.GetAmount(field.Type).ToString(CultureInfo.InvariantCulture);
        _view.BalanceReadout?.Bind(_inventory, null);
        for (int i = 0; i < _view.Slots.Length; i++)
        {
            WeaponInstance runtime = i < equipped.Count ? equipped[i]?.Runtime : null;
            _view.Slots[i].Bind(runtime, i == _selectedSlot, i <= equipped.Count);
            _view.Slots[i].Availability?.Bind(runtime != null ? _crafting.GetAvailableAction(runtime)
                : i == equipped.Count && _crafting.CanTinkerNewWeapon() ? CraftingActionKind.TinkerNewWeapon : null);
            if (IsChoosingWeapon) _view.Slots[i].Button.interactable = false;
        }
        _view.CloseButton.interactable = !IsChoosingWeapon;
        _view.TinkerChoicePanel.SetActive(IsChoosingWeapon);
        if (IsChoosingWeapon)
        {
            _view.UpgradeButton.interactable = _view.TinkerButton.interactable = false;
            _view.AcceptButton.interactable = _view.DeclineButton.interactable = false;
            ShowTinkerChoice();
            return;
        }
        foreach (CraftingCandidateField candidate in _view.Candidates) candidate.Root.SetActive(false);
        WeaponInstance weapon = SelectedWeapon();
        _offeredPath = WeaponUpgradePath.None;
        if (weapon?.Data == null) ShowTinker(equipped.Count + 1);
        else if (weapon.Level == 5 && weapon.SelectedPath == WeaponUpgradePath.None) ShowAdvanced(weapon);
        else ShowUpgrade(weapon);
    }
    private void ShowUpgrade(WeaponInstance weapon)
    {
        _view.ShowPanel(_view.UpgradePanel);
        bool maximum = weapon.Level >= 10;
        int next = Mathf.Min(10, weapon.Level + 1);
        _view.UpgradeNameText.text = weapon.Data.DisplayName;
        _view.UpgradeLevelText.text = maximum ? $"LV {weapon.Level} · Maximum level" : $"LV {weapon.Level} → {next}";
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        IWeaponBehaviour behaviour = _selectedSlot < equipped.Count ? equipped[_selectedSlot] : null;
        BindUpgradePreview(_view, behaviour, maximum);
        IReadOnlyList<MaterialCost> cost = _crafting.GetUpgradeCost(weapon.Data, weapon.SelectedPath, next);
        _view.UpgradeReadout?.Bind(_inventory, cost, maximum ? "No further upgrades" : null);
        _view.UpgradeCostText.text = maximum ? "No further upgrades" : BuildCostText(cost);
        _view.UpgradeButton.interactable = !maximum && _inventory.CanAfford(cost);
        _view.UpgradeButtonText.text = maximum ? "MAX LEVEL" : "UPGRADE";
    }
    public static void BindUpgradePreview(CraftingMenuView view, IWeaponBehaviour behaviour, bool maximum = false)
    {
        CraftingUpgradePreview preview = CraftingUpgradePreview.Build(behaviour, maximum);
        for (int i = 0; i < 3; i++)
        {
            view.UpgradeStatLabels[i].text = preview.Rows[i].Label;
            view.UpgradeStatValues[i].text = preview.Rows[i].Value;
        }
        if (view.UpgradePreviewNotice != null) view.UpgradePreviewNotice.text = preview.Notice;
    }

    // Neutral configured tuning, shared with gameplay; CSV metadata is never the authority here.
    public static string FormatTuning(WeaponData data, string statId, int level, WeaponUpgradePath path)
    {
        var preview = new WeaponInstance { Data = data, Level = level, SelectedPath = path };
        float value = float.NaN;
        if (statId == "Damage") value = Mathf.Max(0f, data.BaseDamage)
            * WeaponDamageResolver.GetLevelDamageMultiplier(preview) * WeaponDamageResolver.GetPathDamageMultiplier(preview);
        else if (statId == "Auto mode range (m)") value = data.BaseRange;
        else if (statId == "Configured orbit radius") value = data.RotatingBlade.BladeOrbitRadius;
        else if (statId == "Manual ammo") value = WeaponMath.GetManualAmmoCapacity(preview);
        return float.IsNaN(value) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
    private void ShowTinker(int slot)
    {
        _view.ShowPanel(_view.TinkerPanel);
        bool eligible = _weaponManager.CanAddWeapon() && _crafting.BuildUnequippedWeapons().Count >= 2;
        IReadOnlyList<MaterialCost> cost = _crafting.GetTinkeringCost(slot);
        _view.TinkerCostLabel.text = $"COST · SLOT {slot}";
        _view.TinkerReadout?.Bind(_inventory, cost, eligible ? null : "Not enough eligible weapons for a choice");
        _view.TinkerCostText.text = eligible ? BuildCostText(cost) : "Not enough eligible weapons for a choice";
        _view.TinkerButton.interactable = _crafting.CanTinkerNewWeapon();
    }
    private void ShowTinkerChoice()
    {
        IReadOnlyList<WeaponData> candidates = _crafting.GetTinkeringOffer();
        for (int i = 0; i < _view.Candidates.Length; i++)
        {
            CraftingCandidateField field = _view.Candidates[i];
            bool visible = i < candidates.Count;
            field.Root.SetActive(visible);
            if (!visible) continue;
            field.NameText.text = candidates[i].DisplayName;
            field.Background.color = field.NormalColor;
            field.Border.gameObject.SetActive(false);
            field.Button.interactable = true;
            field.Icon.sprite = WeaponUiIcons.Resolve(candidates[i], false);
            field.Icon.gameObject.SetActive(field.Icon.sprite != null);
            field.Icon.color = Color.white;
        }
        _view.TinkerChoiceNotice.text = "Cost paid. Choose a weapon to continue. The other is excluded for this run.";
    }
    private void SelectCandidate(int index)
    {
        if (!_isVisible || !IsChoosingWeapon || !_view.TinkerChoicePanel.activeSelf || _applyingAction) return;
        IReadOnlyList<WeaponData> offer = _crafting.GetTinkeringOffer();
        if (index < 0 || index >= offer.Count) return;
        ApplyAction(() => _crafting.TryTinkerWeapon(offer[index]), "New weapon crafted. The other offer is excluded for this run.");
        if (_isVisible && !IsChoosingWeapon && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_view.Slots[_selectedSlot].Button.gameObject);
    }
    private void ShowAdvanced(WeaponInstance weapon)
    {
        _view.ShowPanel(_view.AdvancedPanel);
        _view.AdvancedNameText.text = weapon.Data.DisplayName;
        _view.AdvancedLevelText.text = "LV 5 → 6";
        bool available = _crafting.TryGetAdvancedOffer(weapon.Data, out _offeredPath);
        bool guaranteed = _crafting.TryGetGuaranteedPath(weapon.Data, out _);
        bool canDecline = available && _crafting.CanRejectAdvancedOffer(weapon.Data);
        WeaponMenuCopy copy = _content != null ? _content.Find(weapon.Data) : null;
        _view.AdvancedPathText.text = !available ? "No upgrade available"
            : _offeredPath == WeaponUpgradePath.PathA ? weapon.Data.PathA?.PathName : weapon.Data.PathB?.PathName;
        _view.AdvancedDescriptionText.text = copy == null ? string.Empty
            : _offeredPath == WeaponUpgradePath.PathA ? copy.PathADescription : copy.PathBDescription;
        _view.AdvancedNoticeText.text = guaranteed ? "Other path guaranteed after your previous decline."
            : canDecline ? "Decline: pay this attempt's cost. Next try costs +50% with the other path guaranteed."
            : "Only one path is unlocked.";
        IReadOnlyList<MaterialCost> cost = _crafting.GetAdvancedTinkeringCost(weapon.Data);
        _view.AdvancedReadout?.Bind(_inventory, cost, available ? null : "No upgrade available");
        _view.AdvancedCostText.text = "Tinker cost: " + BuildCostText(cost);
        _view.AcceptButton.interactable = available && _inventory.CanAfford(cost);
        _view.DeclineButton.interactable = canDecline && _inventory.CanAfford(cost);
    }
    private void UpgradeSelected()
    {
        WeaponInstance weapon = SelectedWeapon();
        if (!_isVisible || IsChoosingWeapon || weapon?.Data == null || weapon.Level >= 10) return;
        ApplyAction(() => _crafting.TryUpgradeWeapon(weapon.Data, weapon.Level + 1), "Weapon upgraded.");
    }
    private void Tinker()
    {
        if (!_isVisible || IsChoosingWeapon || !_view.TinkerPanel.activeSelf || SelectedWeapon() != null) return;
        ApplyAction(() => _crafting.TryBeginTinkering(), string.Empty);
        if (_isVisible && IsChoosingWeapon && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_view.Candidates[0].Button.gameObject);
    }
    private void AcceptAdvanced() => ResolveAdvanced(true);
    private void DeclineAdvanced() => ResolveAdvanced(false);
    private void ResolveAdvanced(bool accept)
    {
        WeaponInstance weapon = SelectedWeapon();
        if (!_isVisible || IsChoosingWeapon || weapon?.Data == null || _offeredPath == WeaponUpgradePath.None) return;
        ApplyAction(() => _crafting.TryAdvancedTinkering(weapon.Data, _offeredPath, accept),
            accept ? "Advanced path applied." : "Offer declined. The other path is guaranteed next time.");
    }
    private void ApplyAction(Func<CraftingActionResult> action, string successMessage)
    {
        if (_applyingAction) return;
        _applyingAction = true;
        try
        {
            CraftingActionResult result = action();
            SetStatus(result.Success ? successMessage : "Crafting unavailable. Check your materials and the selected offer.");
        }
        finally { _applyingAction = false; }
        Refresh();
    }
    private void SetStatus(string message)
    {
        if (_view != null && _view.StatusText != null) _view.StatusText.text = message;
    }
    private static string BuildCostText(IReadOnlyList<MaterialCost> costs)
    {
        if (costs == null || costs.Count == 0) return "Free";
        var text = new StringBuilder();
        for (int i = 0; i < costs.Count; i++)
        {
            if (i > 0) text.Append(" + ");
            text.Append(costs[i].Amount).Append(' ').Append(MaterialCatalog.GetDisplayName(costs[i].Material));
        }
        return text.ToString();
    }
}
