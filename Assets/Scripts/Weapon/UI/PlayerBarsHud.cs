using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD con arte <c>Player_Bars</c>: esfera = heat/overheat, barra superior = HP, barra inferior = XP.
/// Los GameObjects del Canvas viven en el prefab/escena (los movés en el editor).
/// Auto-asigna fills por nombre y conserva los valores reales también con efectos animados.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class PlayerBarsHud : MonoBehaviour
{
    private const string HpFillName = "HpFill";
    private const string XpFillName = "XpFill";
    private const string OverheatFillName = "OverheatFill";

    private static readonly Color HpFillColor = new Color(0.18f, 0.82f, 0.28f, 0.95f);
    private static readonly Color XpFillColor = new Color(0.25f, 0.55f, 1f, 0.95f);
    private static readonly Color OverheatFillColor = new Color(1f, 0.42f, 0.08f, 0.95f);

    [Header("UI (auto por nombre de hijo: HpFill / XpFill / OverheatFill)")]
    [SerializeField] private Image _overheatFill;
    [SerializeField] private Image _hpFill;
    [SerializeField] private Image _xpFill;
    [SerializeField] private ShieldChargesGraphic _shieldMeter;

    [Header("Referencias de gameplay (vacías = autobúsqueda)")]
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerXP _playerXp;
    [SerializeField] private HeatManager _heatManager;
    [SerializeField] private OverheatManager _overheatManager;

    [Header("Overheat activo")]
    [SerializeField] private Color _overheatActiveColor = new Color(1f, 0.22f, 0.1f, 1f);

    [Header("Animated fills")]
    [SerializeField] private bool _useAnimatedFills = true;
    private LiquidHealthBarVfx _liquidHealth;
    private EnergyMeterVfx _experienceEnergy;
    private EnergyMeterVfx _heatFire;

    private void Reset()
    {
        AutoWireUiReferences();
        EnsureFillImagesReady();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoWireUiReferences();
        EnsureFillImagesReady();
    }
#endif

    private void Awake()
    {
        AutoWireUiReferences();
        EnsureFillImagesReady();
        EnsureAnimatedFills();
        ResolveGameplayRefs();
    }

    private void Start()
    {
        UnbindEvents();
        ResolveGameplayRefs();
        BindEvents();
        RefreshAll();
    }

    private void AutoWireUiReferences()
    {
        if (_shieldMeter == null)
            _shieldMeter = GetComponentInChildren<ShieldChargesGraphic>(true);
        if (_hpFill != null && _xpFill != null && _overheatFill != null)
            return;

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null || img.gameObject.name == "Frame")
                continue;

            switch (img.gameObject.name)
            {
                case HpFillName:
                    _hpFill = img;
                    break;
                case XpFillName:
                    _xpFill = img;
                    break;
                case OverheatFillName:
                    _overheatFill = img;
                    break;
            }
        }
    }

    private void EnsureFillImagesReady()
    {
        if (_hpFill != null && _hpFill.GetComponent<LiquidHealthBarVfx>()?.IsRenderingEffect != true)
            HudUiFactory.EnsureHorizontalFill(_hpFill, HpFillColor);
        if (_xpFill != null && _xpFill.GetComponent<EnergyMeterVfx>()?.IsRenderingEffect != true)
            HudUiFactory.EnsureHorizontalFill(_xpFill, XpFillColor);
        if (_overheatFill != null && _overheatFill.GetComponent<EnergyMeterVfx>()?.IsRenderingEffect != true)
            HudUiFactory.EnsureVerticalFill(_overheatFill, OverheatFillColor);
    }

    private void EnsureAnimatedFills()
    {
        if (!_useAnimatedFills) return;
        if (_hpFill != null)
            _liquidHealth = _hpFill.GetComponent<LiquidHealthBarVfx>() ?? _hpFill.gameObject.AddComponent<LiquidHealthBarVfx>();
        if (_xpFill != null)
            _experienceEnergy = _xpFill.GetComponent<EnergyMeterVfx>() ?? _xpFill.gameObject.AddComponent<EnergyMeterVfx>();
        if (_overheatFill != null)
            _heatFire = _overheatFill.GetComponent<EnergyMeterVfx>() ?? _overheatFill.gameObject.AddComponent<EnergyMeterVfx>();
    }

    private void ResolveGameplayRefs()
    {
        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (_playerXp == null)
            _playerXp = FindAnyObjectByType<PlayerXP>();
        if (_heatManager == null)
            _heatManager = HeatManager.GetInstance();
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
    }

    private void OnEnable()
    {
        ResolveGameplayRefs();
        BindEvents();
        RefreshAll();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void BindEvents()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged += RefreshHealth;
            _playerHealth.OnShieldChanged += RefreshShields;
        }
        if (_playerXp != null)
        {
            _playerXp.OnXpProgressChanged += RefreshXp;
            _playerXp.OnLevelUp += OnLevelUp;
        }
        if (_heatManager != null)
            _heatManager.OnHeatChanged += RefreshOverheat;
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += OnOverheatStarted;
            _overheatManager.OnOverheatFinished += OnOverheatFinished;
        }
    }

    private void UnbindEvents()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= RefreshHealth;
            _playerHealth.OnShieldChanged -= RefreshShields;
        }
        if (_playerXp != null)
        {
            _playerXp.OnXpProgressChanged -= RefreshXp;
            _playerXp.OnLevelUp -= OnLevelUp;
        }
        if (_heatManager != null)
            _heatManager.OnHeatChanged -= RefreshOverheat;
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= OnOverheatStarted;
            _overheatManager.OnOverheatFinished -= OnOverheatFinished;
        }
    }

    private void OnLevelUp(int _) => RefreshXp();
    private void OnOverheatStarted() => RefreshOverheat();
    private void OnOverheatFinished(OverheatEndReason _) => RefreshOverheat();

    private void RefreshAll()
    {
        RefreshHealth();
        RefreshShields();
        RefreshXp();
        RefreshOverheat();
    }

    private void RefreshShields()
    {
        if (_shieldMeter != null)
            _shieldMeter.SetCharges(_playerHealth != null ? _playerHealth.ShieldCharges : 0,
                _playerHealth != null ? _playerHealth.MaxShieldCharges : 0);
    }

    private void RefreshHealth()
    {
        if (_hpFill == null)
            return;

        float value = _playerHealth != null
            ? Mathf.Clamp01((float)_playerHealth.CurrentHealth / Mathf.Max(1, _playerHealth.MaxHealth)) : 0f;
        _hpFill.fillAmount = value;
        if (_liquidHealth != null) _liquidHealth.SetFill(value);
    }

    private void RefreshXp()
    {
        if (_xpFill == null)
            return;

        float value = _playerXp != null ? _playerXp.NormalizedProgressToNextLevel : 0f;
        _xpFill.fillAmount = value;
        if (_experienceEnergy != null) _experienceEnergy.SetExperience(value, _playerXp != null ? _playerXp.CurrentLevel : 1);
    }

    private void RefreshOverheat()
    {
        if (_overheatFill == null)
            return;

        bool active = _overheatManager != null && _overheatManager.IsOverheating;

        float n = _heatManager != null ? _heatManager.NormalizedHeat : 0f;
        if (active)
            n = 1f;

        bool warning = n >= HeatPresentationState.WarningThreshold &&
            (_heatManager == null || !_heatManager.IsPostOverheatDecayActive);
        _overheatFill.color = active ? _overheatActiveColor
            : warning ? new Color(1f, .68f, .12f, .95f) : OverheatFillColor;
        _overheatFill.fillAmount = Mathf.Clamp01(n);
        if (_heatFire != null) _heatFire.SetHeat(n, active);
    }
}
