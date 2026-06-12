using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class WeaponClusterHud : MonoBehaviour
{
    private const int MaxWeaponSlots = WeaponManager.MaxWeaponSlots;

    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private PlayerStats _playerStats;
    [SerializeField] private PlayerMovement _playerMovement;

    private struct WeaponSlotUi
    {
        public Image Frame;
        public Image Icon;
        public TextMeshProUGUI LevelBadge;
    }

    private readonly List<WeaponSlotUi> _weaponSlots = new(MaxWeaponSlots);
    private readonly List<Image> _dashIcons = new(5);

    private TextMeshProUGUI _weaponNameText;
    private TextMeshProUGUI _weaponLevelText;
    private TextMeshProUGUI _ammoLabel;
    private TextMeshProUGUI _abilityStatusText;
    private TextMeshProUGUI _rotationCooldownText;
    private Image _ammoFill;
    private Image _abilityCooldownFill;
    private Image _rotationCooldownFill;
    private Transform _dashLayout;

    private void Awake()
    {
        ResolveRefs();
        if (!TryWireFromHierarchy())
            Debug.LogWarning($"[{nameof(WeaponClusterHud)}] Falta jerarquía WeaponSlots/WeaponPanel en el prefab. Ejecutá ScrapWaves → UI → Rebuild BottomStrip In Prefab.", this);
        else
            EnsureFillImagesReady();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (TryWireFromHierarchy())
            EnsureFillImagesReady();
    }
#endif

    private void OnEnable()
    {
        ResolveRefs();
        if (_playerMovement != null)
        {
            _playerMovement.OnDashChargesChanged += OnDashChargesChanged;
            OnDashChargesChanged(_playerMovement.CurrentDashCharges, _playerMovement.MaxDashCharges);
        }
    }

    private void OnDisable()
    {
        if (_playerMovement != null)
            _playerMovement.OnDashChargesChanged -= OnDashChargesChanged;
    }

    private void Update() => RefreshWeaponPanel();

    private void ResolveRefs()
    {
        if (_weaponManager == null)
            _weaponManager = FindAnyObjectByType<WeaponManager>();
        if (_playerStats == null)
            _playerStats = FindAnyObjectByType<PlayerStats>();
        if (_playerMovement == null)
            _playerMovement = FindAnyObjectByType<PlayerMovement>();
    }

    private bool TryWireFromHierarchy()
    {
        Transform cluster = transform.Find("WeaponCluster");
        Transform root = cluster != null ? cluster : transform;
        Transform slotsRoot = root.Find("WeaponSlots");
        Transform panelRoot = root.Find("WeaponPanel");
        _dashLayout = transform.Find("DashCharges/Layout") ?? root.Find("DashCharges/Layout");
        if (slotsRoot == null || panelRoot == null)
            return false;

        _weaponSlots.Clear();
        for (int i = 0; i < MaxWeaponSlots; i++)
        {
            Transform slotRoot = slotsRoot.Find($"WeaponSlot_{i}");
            if (slotRoot == null)
                return false;

            Image frame = HudUiWire.FindImage(slotRoot, "Frame");
            Image icon = HudUiWire.FindImage(slotRoot, "Icon");
            if (icon == null)
                return false;

            _weaponSlots.Add(new WeaponSlotUi
            {
                Frame = frame,
                Icon = icon,
                LevelBadge = HudUiWire.FindTmp(slotRoot, "Level")
            });
        }

        _weaponNameText = HudUiWire.FindTmp(panelRoot, "WeaponName");
        _weaponLevelText = HudUiWire.FindTmp(panelRoot, "WeaponLevel");
        _ammoLabel = HudUiWire.FindTmp(panelRoot, "AmmoLabel");
        _abilityStatusText = HudUiWire.FindTmp(panelRoot, "AbilityStatus");
        _rotationCooldownText = HudUiWire.FindTmp(panelRoot, "RotLabel");

        Transform ammoBar = panelRoot.Find("AmmoBar");
        if (ammoBar != null)
        {
            Transform ammoFillRoot = ammoBar.Find("AmmoFill");
            if (ammoFillRoot != null)
                _ammoFill = HudUiWire.FindImage(ammoFillRoot, "Fill");
        }

        Transform abilityRoot = panelRoot.Find("AbilityCooldown");
        if (abilityRoot != null)
        {
            Transform radial = abilityRoot.Find("AbilityCooldownFill");
            if (radial != null)
                _abilityCooldownFill = HudUiWire.FindImage(radial, "Fill");
        }

        Transform rotationRoot = panelRoot.Find("RotationCooldown");
        if (rotationRoot != null)
        {
            Transform radial = rotationRoot.Find("RotationCooldownFill");
            if (radial != null)
                _rotationCooldownFill = HudUiWire.FindImage(radial, "Fill");
        }

        WireDashIconsFromHierarchy();

        return _weaponNameText != null && _ammoFill != null;
    }

    private void EnsureFillImagesReady()
    {
        if (_ammoFill != null)
        {
            HudUiFactory.EnsureHorizontalFill(_ammoFill);
            Image track = _ammoFill.transform.parent != null
                ? _ammoFill.transform.parent.GetComponent<Image>()
                : null;
            HudUiFactory.EnsureSimpleTrack(track, HudUiFactory.EmptySlotColor);
        }

        HudUiFactory.EnsureRadial360Fill(_abilityCooldownFill);
        HudUiFactory.EnsureRadial360Fill(_rotationCooldownFill);
    }

    private void WireDashIconsFromHierarchy()
    {
        _dashIcons.Clear();
        if (_dashLayout == null)
            return;

        for (int i = 0; i < _dashLayout.childCount; i++)
        {
            Image img = _dashLayout.GetChild(i).GetComponent<Image>();
            if (img != null)
                _dashIcons.Add(img);
        }
    }

    private void OnDashChargesChanged(int current, int max)
    {
        EnsureDashIconCount(max);
        for (int i = 0; i < _dashIcons.Count && i < max; i++)
        {
            bool available = i < current;
            _dashIcons[i].color = available
                ? new Color(0.3f, 0.85f, 1f, 1f)
                : new Color(0.2f, 0.24f, 0.3f, 0.85f);
        }
    }

    private void EnsureDashIconCount(int count)
    {
        count = Mathf.Clamp(count, 1, 5);
        if (_dashIcons.Count == 0)
            WireDashIconsFromHierarchy();

        for (int i = 0; i < _dashIcons.Count; i++)
            _dashIcons[i].gameObject.SetActive(i < count);
    }

    private void RefreshWeaponPanel()
    {
        RefreshWeaponSlots();
        if (_weaponManager == null)
            return;

        WeaponInstance weapon = _weaponManager.GetCurrentManualWeapon();
        if (weapon?.Data == null)
        {
            if (_weaponNameText != null) _weaponNameText.text = "Sin arma";
            if (_weaponLevelText != null) _weaponLevelText.text = string.Empty;
            if (_ammoLabel != null) _ammoLabel.text = string.Empty;
            if (_ammoFill != null) _ammoFill.fillAmount = 0f;
            if (_abilityStatusText != null) _abilityStatusText.text = string.Empty;
            if (_abilityCooldownFill != null) _abilityCooldownFill.fillAmount = 0f;
            return;
        }

        float maxAmmo = _playerStats != null
            ? WeaponMath.GetMaxManualAmmo(weapon, _playerStats)
            : weapon.Data.BaseManualAmmo;
        float ammoNorm = maxAmmo > 0f ? weapon.CurrentAmmo / maxAmmo : 0f;

        if (_weaponNameText != null)
            _weaponNameText.text = string.IsNullOrEmpty(weapon.Data.DisplayName) ? weapon.Data.name : weapon.Data.DisplayName;
        if (_weaponLevelText != null)
            _weaponLevelText.text = $"Lv. {weapon.Level}";
        if (_ammoFill != null)
            _ammoFill.fillAmount = Mathf.Clamp01(ammoNorm);
        if (_ammoLabel != null)
            _ammoLabel.text = $"{weapon.CurrentAmmo:0}/{maxAmmo:0}";

        if (_abilityCooldownFill != null)
            _abilityCooldownFill.fillAmount = _weaponManager.GetAbilityCooldownNormalized();

        if (_abilityStatusText != null)
        {
            if (weapon.State != WeaponState.Manual)
                _abilityStatusText.text = string.Empty;
            else if (_weaponManager.CanUseAbility())
                _abilityStatusText.text = "Lista [Q]";
            else if (weapon.AbilityCooldownTimer > 0f)
                _abilityStatusText.text = $"Enfriando [Q] ({weapon.AbilityCooldownTimer:0.#}s)";
            else
                _abilityStatusText.text = "Sin munición [Q]";
        }

        if (_rotationCooldownFill != null)
        {
            float remaining = _weaponManager.GetManualCooldownRemaining();
            bool cycling = remaining > 0f && weapon.State != WeaponState.Manual;
            _rotationCooldownFill.transform.parent.gameObject.SetActive(cycling || remaining > 0f);
            _rotationCooldownFill.fillAmount = _weaponManager.GetManualCooldownNormalized();
            if (_rotationCooldownText != null)
                _rotationCooldownText.text = cycling ? $"{remaining:0.#}" : "Rot";
        }
    }

    private void RefreshWeaponSlots()
    {
        if (_weaponManager == null)
            return;

        IReadOnlyList<IWeaponBehaviour> weapons = _weaponManager.GetEquippedWeapons();

        for (int rotationSlot = 0; rotationSlot < _weaponSlots.Count; rotationSlot++)
        {
            WeaponSlotUi slot = _weaponSlots[rotationSlot];
            int equippedIndex = _weaponManager.GetEquippedIndexForRotationSlot(rotationSlot);
            if (equippedIndex < 0 || weapons[equippedIndex]?.Runtime?.Data == null)
            {
                if (slot.Frame != null) slot.Frame.color = HudUiFactory.EmptySlotColor;
                slot.Icon.sprite = HudUiFactory.WhiteSprite;
                slot.Icon.color = HudUiFactory.EmptySlotColor;
                if (slot.LevelBadge != null) slot.LevelBadge.text = string.Empty;
                continue;
            }

            WeaponInstance runtime = weapons[equippedIndex].Runtime;
            WeaponData data = runtime.Data;
            Sprite sprite = data.Icon;
            slot.Icon.sprite = sprite != null ? sprite : HudUiFactory.WhiteSprite;
            slot.Icon.color = sprite != null ? Color.white : HudUiFactory.GetPlaceholderColor(HudPlaceholderKind.Weapon);
            if (slot.LevelBadge != null)
                slot.LevelBadge.text = runtime.Level > 0 ? runtime.Level.ToString() : string.Empty;

            bool isActiveManual = rotationSlot == 0 && runtime.State == WeaponState.Manual;
            if (slot.Frame != null)
            {
                slot.Frame.color = isActiveManual
                    ? new Color(0.95f, 0.75f, 0.2f, 1f)
                    : HudUiFactory.BorderColor;
            }
        }
    }

}
