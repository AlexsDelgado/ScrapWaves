using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD con arte <c>Player_Bars</c>: esfera = heat/overheat, barra superior = HP, barra inferior = XP.
/// Los GameObjects del Canvas viven en el prefab/escena (los movés en el editor).
/// Auto-asigna fills por nombre; en runtime asegura tipo Filled y actualiza fillAmount.
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

    [Header("Referencias de gameplay (vacías = autobúsqueda)")]
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerXP _playerXp;
    [SerializeField] private HeatManager _heatManager;
    [SerializeField] private OverheatManager _overheatManager;

    [Header("Overheat activo")]
    [SerializeField] private Color _overheatActiveColor = new Color(1f, 0.22f, 0.1f, 1f);

    private static Sprite s_whiteFillSprite;

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
        ResolveGameplayRefs();
    }

    private void Start()
    {
        ResolveGameplayRefs();
        RefreshAll();
    }

    private void AutoWireUiReferences()
    {
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
        if (_hpFill != null)
            ApplyHorizontalFillSettings(_hpFill, HpFillColor);

        if (_xpFill != null)
            ApplyHorizontalFillSettings(_xpFill, XpFillColor);

        if (_overheatFill != null)
            ApplyRadialFillSettings(_overheatFill, OverheatFillColor);
    }

    private static void ApplyHorizontalFillSettings(Image img, Color color)
    {
        if (img.sprite == null)
            img.sprite = GetWhiteFillSprite();

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillClockwise = true;
        img.color = color;
        img.raycastTarget = false;
    }

    private static void ApplyRadialFillSettings(Image img, Color color)
    {
        if (img.sprite == null)
            img.sprite = GetWhiteFillSprite();

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Vertical;
        img.fillOrigin = (int)Image.Origin360.Bottom;
        img.fillClockwise = true;
        img.color = color;
        img.raycastTarget = false;
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
            _playerHealth.OnHealthChanged += RefreshHealth;
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
            _playerHealth.OnHealthChanged -= RefreshHealth;
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
        RefreshXp();
        RefreshOverheat();
    }

    private void RefreshHealth()
    {
        if (_hpFill == null)
            return;

        if (_playerHealth == null)
        {
            _hpFill.fillAmount = 0f;
            return;
        }

        int max = Mathf.Max(1, _playerHealth.MaxHealth);
        _hpFill.fillAmount = Mathf.Clamp01((float)_playerHealth.CurrentHealth / max);
    }

    private void RefreshXp()
    {
        if (_xpFill == null)
            return;

        _xpFill.fillAmount = _playerXp != null ? _playerXp.NormalizedProgressToNextLevel : 0f;
    }

    private void RefreshOverheat()
    {
        if (_overheatFill == null)
            return;

        bool active = _overheatManager != null && _overheatManager.IsOverheating;
        _overheatFill.color = active ? _overheatActiveColor : OverheatFillColor;

        float n = _heatManager != null ? _heatManager.NormalizedHeat : 0f;
        if (active)
            n = 1f;

        _overheatFill.fillAmount = Mathf.Clamp01(n);
    }

    private static Sprite GetWhiteFillSprite()
    {
        if (s_whiteFillSprite != null)
            return s_whiteFillSprite;

        Texture2D tex = Texture2D.whiteTexture;
        s_whiteFillSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return s_whiteFillSprite;
    }
}
