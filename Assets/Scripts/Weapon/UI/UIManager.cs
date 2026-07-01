using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD básico: vida, XP, nivel, Heat y estado de Overheat. Crea Canvas en runtime; referencias opcionales (autobúsqueda).
/// </summary>
[DisallowMultipleComponent]
public class UIManager : MonoBehaviour
{
    [Header("Referencias (vacías = autobúsqueda)")]
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerXP _playerXp;
    [SerializeField] private HeatManager _heatManager;
    [SerializeField] private OverheatManager _overheatManager;

    [Header("Layout")]
    [SerializeField] private Vector2 _panelPosition = new Vector2(16f, -20f);
    [SerializeField] private float _panelWidth = 340f;
    [SerializeField] private float _barHeight = 18f;
    [SerializeField] private float _barSpacing = 8f;
    [SerializeField, Min(10f)] private float _fontSize = 15f;

    [Header("Colores")]
    [SerializeField] private Color _panelBg = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color _healthBarColor = new Color(0.2f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _xpBarColor = new Color(0.25f, 0.55f, 1f, 1f);
    [SerializeField] private Color _heatFillColor = new Color(1f, 0.45f, 0.12f, 1f);
    [SerializeField] private Color _trackColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    [SerializeField] private Color _overheatActiveColor = new Color(1f, 0.35f, 0.2f, 1f);
    [SerializeField] private Color _overheatInactiveColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    private TextMeshProUGUI _overheatLabel;
    private TextMeshProUGUI _levelLabel;
    private TextMeshProUGUI _hpLabel;
    private TextMeshProUGUI _xpLabel;
    private TextMeshProUGUI _heatLabel;

    private Image _hpBarImage;
    private Image _xpBarImage;
    private Image _heatBarImage;

    private static Sprite s_whiteSprite;

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
    }

    private void ResolveRefs()
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
        if (_playerHealth != null)
            _playerHealth.OnHealthChanged += RefreshHealth;
        if (_playerXp != null)
        {
            _playerXp.OnLevelUp += OnLevelUp;
            _playerXp.OnXpProgressChanged += RefreshXp;
        }

        if (_heatManager != null)
            _heatManager.OnHeatChanged += RefreshHeat;
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += RefreshOverheat;
            _overheatManager.OnOverheatFinished += OnOverheatFinished;
        }

        RefreshAll();
    }

    private void OnDisable()
    {
        if (_playerHealth != null)
            _playerHealth.OnHealthChanged -= RefreshHealth;
        if (_playerXp != null)
        {
            _playerXp.OnLevelUp -= OnLevelUp;
            _playerXp.OnXpProgressChanged -= RefreshXp;
        }

        if (_heatManager != null)
            _heatManager.OnHeatChanged -= RefreshHeat;
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= RefreshOverheat;
            _overheatManager.OnOverheatFinished -= OnOverheatFinished;
        }
    }

    private void OnLevelUp(int _) => RefreshXpAndLevel();
    private void OnOverheatFinished(OverheatEndReason _) => RefreshOverheat();

    private void RefreshAll()
    {
        RefreshOverheat();
        RefreshXpAndLevel();
        RefreshHealth();
        RefreshXp();
        RefreshHeat();
    }

    private void RefreshHealth()
    {
        if (_hpBarImage == null || _hpLabel == null)
            return;

        if (_playerHealth == null)
        {
            _hpBarImage.fillAmount = 0f;
            _hpLabel.text = "HP — / —";
            return;
        }

        int max = Mathf.Max(1, _playerHealth.MaxHealth);
        float n = Mathf.Clamp01((float)_playerHealth.CurrentHealth / max);
        _hpBarImage.fillAmount = n;
        _hpLabel.text = $"HP {_playerHealth.CurrentHealth} / {max}";
    }

    private void RefreshXp()
    {
        if (_xpBarImage == null || _xpLabel == null)
            return;

        if (_playerXp == null)
        {
            _xpBarImage.fillAmount = 0f;
            _xpLabel.text = "XP — / —";
            return;
        }

        _xpBarImage.fillAmount = _playerXp.NormalizedProgressToNextLevel;
        _xpLabel.text = $"XP {_playerXp.XpTowardsNext} / {_playerXp.XpRequiredForCurrentLevel}";
    }

    private void RefreshXpAndLevel()
    {
        RefreshXp();
        if (_levelLabel == null)
            return;

        if (_playerXp == null)
        {
            _levelLabel.text = "Level —";
            return;
        }

        _levelLabel.text = $"Level {_playerXp.CurrentLevel}";
    }

    private void RefreshHeat()
    {
        if (_heatBarImage == null || _heatLabel == null)
            return;

        if (_heatManager == null)
        {
            _heatBarImage.fillAmount = 0f;
            _heatLabel.text = "Heat —";
            return;
        }

        _heatBarImage.fillAmount = _heatManager.NormalizedHeat;
        _heatLabel.text = $"Heat {Mathf.CeilToInt(_heatManager.CurrentHeat)} / {Mathf.CeilToInt(_heatManager.MaxHeat)}";
    }

    private void RefreshOverheat()
    {
        if (_overheatLabel == null)
            return;

        bool active = _overheatManager != null && _overheatManager.IsOverheating;
        _overheatLabel.text = active ? "Overheat: ACTIVO" : "Overheat: inactivo";
        _overheatLabel.color = active ? _overheatActiveColor : _overheatInactiveColor;
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("GameplayUI_Canvas");
        canvasGo.transform.SetParent(transform, false);
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasGo.layer = uiLayer;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.anchorMin = Vector2.zero;
        canvasRt.anchorMax = Vector2.one;
        canvasRt.offsetMin = Vector2.zero;
        canvasRt.offsetMax = Vector2.zero;

        var panel = CreatePanel(canvasGo.transform);
        float y = -8f;

        _overheatLabel = CreateLineText(panel.transform, "OverheatStatus", ref y, true);
        _levelLabel = CreateLineText(panel.transform, "LevelLine", ref y, true);

        y -= 4f;
        _hpLabel = CreateCaption(panel.transform, "HpCaption", ref y);
        CreateFilledBar(panel.transform, "HpBar", ref y, out _, out _hpBarImage, _healthBarColor, _trackColor);

        _xpLabel = CreateCaption(panel.transform, "XpCaption", ref y);
        CreateFilledBar(panel.transform, "XpBar", ref y, out _, out _xpBarImage, _xpBarColor, _trackColor);

        _heatLabel = CreateCaption(panel.transform, "HeatCaption", ref y);
        CreateFilledBar(panel.transform, "HeatBar", ref y, out _, out _heatBarImage, _heatFillColor, _trackColor);
    }

    private RectTransform CreatePanel(Transform canvas)
    {
        var go = new GameObject("HUDPanel");
        go.transform.SetParent(canvas, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = _panelPosition;
        float contentHeight = 230f;
        rt.sizeDelta = new Vector2(_panelWidth, contentHeight);

        var img = go.AddComponent<Image>();
        img.sprite = GetWhiteSprite();
        img.color = _panelBg;
        img.raycastTarget = false;

        return rt;
    }

    private TextMeshProUGUI CreateLineText(Transform panel, string name, ref float yFromTop, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(12f, yFromTop - 22f);
        rt.offsetMax = new Vector2(-12f, yFromTop);

        var text = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(text);
        text.fontSize = _fontSize + (bold ? 1f : 0f);
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = "—";

        yFromTop -= 24f;
        return text;
    }

    private TextMeshProUGUI CreateCaption(Transform panel, string name, ref float yFromTop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(12f, yFromTop - 18f);
        rt.offsetMax = new Vector2(-12f, yFromTop);

        var text = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(text);
        text.fontSize = _fontSize - 1f;
        text.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = "";

        yFromTop -= 18f;
        return text;
    }

    private void CreateFilledBar(Transform panel, string name, ref float yFromTop, out Image track, out Image fill, Color fillColor, Color trackColor)
    {
        var trackGo = new GameObject(name + "_Track");
        trackGo.transform.SetParent(panel, false);
        var trackRt = trackGo.AddComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(0f, 1f);
        trackRt.anchorMax = new Vector2(0f, 1f);
        trackRt.pivot = new Vector2(0f, 1f);
        float barWidthInset = _panelWidth - 24f;
        trackRt.anchoredPosition = new Vector2(12f, yFromTop);
        trackRt.sizeDelta = new Vector2(barWidthInset, _barHeight);

        track = trackGo.AddComponent<Image>();
        track.sprite = GetWhiteSprite();
        track.type = Image.Type.Simple;
        track.color = trackColor;
        track.raycastTarget = false;

        var fillGo = new GameObject(name + "_Fill");
        fillGo.transform.SetParent(trackGo.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        fill = fillGo.AddComponent<Image>();
        fill.sprite = GetWhiteSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;
        fill.color = fillColor;
        fill.raycastTarget = false;

        yFromTop -= _barHeight + _barSpacing;
    }

    private static Sprite GetWhiteSprite()
    {
        if (s_whiteSprite != null)
            return s_whiteSprite;

        var tex = Texture2D.whiteTexture;
        s_whiteSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return s_whiteSprite;
    }
}
