using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

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
    private float _previousTimeScale = 1f;
    private int _selectedSlot;
    private WeaponUpgradePath _offeredPath;
    private bool _isVisible;
    private bool _holdsUiPause;
    private bool _buttonsBound;
    private bool _applyingAction;
    public bool IsVisible => _isVisible;

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
        _buttonsBound = true;
        return true;
    }

    private void OnDisable()
    {
        if (_isVisible) Hide();
        GameplayPause.SetHeld(ref _holdsUiPause, false);
    }
    private void OnDestroy()
    {
        if (_inventory != null) _inventory.OnInventoryChanged -= OnInventoryChanged;
        if (!_buttonsBound || _view == null) return;
        if (_view.CloseButton != null) _view.CloseButton.onClick.RemoveListener(Hide);
        if (_view.UpgradeButton != null) _view.UpgradeButton.onClick.RemoveListener(UpgradeSelected);
        if (_view.TinkerButton != null) _view.TinkerButton.onClick.RemoveListener(Tinker);
        if (_view.AcceptButton != null) _view.AcceptButton.onClick.RemoveListener(AcceptAdvanced);
        if (_view.DeclineButton != null) _view.DeclineButton.onClick.RemoveListener(DeclineAdvanced);
        for (int i = 0; i < _view.Slots.Length; i++)
            if (_view.Slots[i] != null && _view.Slots[i].Button != null) _view.Slots[i].Button.onClick.RemoveListener(_slotActions[i]);
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
        SetStatus(string.Empty);
        _view.gameObject.SetActive(true);
        Refresh();
        return true;
    }

    private void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        if (_inventory != null) _inventory.OnInventoryChanged -= OnInventoryChanged;
        if (_view != null) _view.gameObject.SetActive(false);
        Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
        GameplayPause.SetHeld(ref _holdsUiPause, false);
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
        if (!_isVisible) return;
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
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        foreach (CraftingMaterialField field in _view.Materials)
            field.AmountText.text = _inventory.GetAmount(field.Type).ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < _view.Slots.Length; i++)
            _view.Slots[i].Bind(i < equipped.Count ? equipped[i]?.Runtime : null, i == _selectedSlot, i <= equipped.Count);
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
        string[] ids = { "Damage", weapon.Data.WeaponType == WeaponType.RotatingBlade ? "Auto blade length (m)" : "Auto mode range (m)", "Manual ammo" };
        string[] labels = { "Damage", weapon.Data.WeaponType == WeaponType.RotatingBlade ? "Auto blade length" : "Auto range", "Manual ammo" };
        if (weapon.Data.WeaponType == WeaponType.RotatingBlade
            && float.IsNaN(weapon.Data.TryGetBalanceStat(ids[1], weapon.Level, weapon.SelectedPath, float.NaN)))
        {
            ids[1] = "Configured orbit radius";
            labels[1] = "Auto orbit radius";
        }
        for (int i = 0; i < 3; i++)
        {
            _view.UpgradeStatLabels[i].text = labels[i];
            string current = FormatTuning(weapon.Data, ids[i], weapon.Level, weapon.SelectedPath);
            string future = FormatTuning(weapon.Data, ids[i], next, weapon.SelectedPath);
            _view.UpgradeStatValues[i].text = (maximum ? current : $"{current} → {future}") + (i == 1 ? " m" : string.Empty);
        }
        IReadOnlyList<MaterialCost> cost = _crafting.GetUpgradeCost(weapon.Data, weapon.SelectedPath, next);
        _view.UpgradeCostText.text = maximum ? "No further upgrades" : BuildCostText(cost);
        _view.UpgradeButton.interactable = !maximum && _inventory.CanAfford(cost);
        _view.UpgradeButtonText.text = maximum ? "MAX LEVEL" : "UPGRADE";
    }
    // Configured weapon tuning only: no player modifiers, heat or combat rolls.
    public static string FormatTuning(WeaponData data, string statId, int level, WeaponUpgradePath path)
    {
        float value = data.TryGetBalanceStat(statId, level, path, float.NaN);
        if (float.IsNaN(value))
        {
            // Legacy assets can have level/path tuning without imported balance rows.
            var preview = new WeaponInstance { Data = data, Level = level, SelectedPath = path };
            if (statId == "Damage")
                value = Mathf.Max(0f, data.BaseDamage) * WeaponDamageResolver.GetLevelDamageMultiplier(preview)
                    * WeaponDamageResolver.GetPathDamageMultiplier(preview);
            else if (statId == "Auto mode range (m)") value = data.BaseRange;
            else if (statId == "Configured orbit radius") value = data.RotatingBlade.BladeOrbitRadius;
            else if (statId == "Manual ammo")
            {
                WeaponLevelData tuning = WeaponMath.GetLevelData(preview);
                WeaponUpgradePathData pathTuning = WeaponMath.GetPathData(preview);
                value = Mathf.Max(0f, data.BaseManualAmmo) * (tuning != null ? Mathf.Max(0.01f, tuning.ManualAmmoMultiplier) : 1f);
                if (pathTuning != null && pathTuning.ManualAmmoOverride >= 0f) value = pathTuning.ManualAmmoOverride;
            }
        }
        return float.IsNaN(value) ? "—" : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
    private void ShowTinker(int slot)
    {
        _view.ShowPanel(_view.TinkerPanel);
        List<WeaponData> candidates = _crafting.BuildUnequippedWeapons();
        for (int i = 0; i < _view.Candidates.Length; i++)
        {
            CraftingCandidateField field = _view.Candidates[i];
            bool visible = i < candidates.Count;
            field.Root.SetActive(visible);
            if (!visible) continue;
            field.NameText.text = candidates[i].DisplayName;
            field.Icon.sprite = WeaponUiIcons.Resolve(candidates[i], selected: false);
            field.Icon.gameObject.SetActive(field.Icon.sprite != null);
            field.Icon.color = Color.white;
        }
        IReadOnlyList<MaterialCost> cost = _crafting.GetTinkeringCost(slot);
        _view.TinkerCostLabel.text = $"COST · SLOT {slot}";
        _view.TinkerCostText.text = candidates.Count == 0 ? "No new weapons available" : BuildCostText(cost);
        _view.TinkerButton.interactable = candidates.Count > 0 && _weaponManager.CanAddWeapon() && _inventory.CanAfford(cost);
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
        _view.AdvancedCostText.text = "Tinker cost: " + BuildCostText(cost);
        _view.AcceptButton.interactable = available && _inventory.CanAfford(cost);
        _view.DeclineButton.interactable = canDecline && _inventory.CanAfford(cost);
    }
    private void UpgradeSelected()
    {
        WeaponInstance weapon = SelectedWeapon();
        if (!_isVisible || weapon?.Data == null || weapon.Level >= 10) return;
        ApplyAction(() => _crafting.TryUpgradeWeapon(weapon.Data, weapon.Level + 1), "Weapon upgraded.");
    }
    private void Tinker()
    {
        if (!_isVisible) return;
        ApplyAction(_crafting.TryTinkerRandomWeapon, "New weapon crafted.");
    }
    private void AcceptAdvanced() => ResolveAdvanced(true);
    private void DeclineAdvanced() => ResolveAdvanced(false);
    private void ResolveAdvanced(bool accept)
    {
        WeaponInstance weapon = SelectedWeapon();
        if (!_isVisible || weapon?.Data == null || _offeredPath == WeaponUpgradePath.None) return;
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
