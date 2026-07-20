using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD mínimo: stats arriba-izquierda (nivel, EXP, enemigos eliminados) y barra superior de overheat (roja, 0–100%).
/// Crea Canvas e hijos en runtime; asigna <see cref="PlayerXP"/> y <see cref="HeatManager"/> o déjalos vacíos para autobúsqueda.
/// </summary>
[DisallowMultipleComponent]
public class SurvivorHud : MonoBehaviour
{
    [SerializeField, Tooltip("Si está vacío, se usa FindAnyObjectByType.")]
    private PlayerXP _playerXp;

    [SerializeField, Tooltip("Si está vacío, se usa HeatManager.GetInstance().")]
    private HeatManager _heatManager;

    [SerializeField, Min(4f)] private float _heatBarHeight = 14f;

    [SerializeField, Min(8f)] private float _statsFontSize = 17f;

    [SerializeField] private Vector2 _statsPadding = new Vector2(14f, 10f);

    [SerializeField] private Color _heatBarColor = new Color(0.9f, 0.15f, 0.12f, 1f);

    [SerializeField] private Color _heatBarTrackColor = new Color(0.12f, 0.04f, 0.04f, 0.85f);

    [SerializeField] private Color _statsPanelColor = new Color(0f, 0f, 0f, 0.45f);

    private TextMeshProUGUI _levelText;
    private TextMeshProUGUI _expText;
    private TextMeshProUGUI _killsText;
    private Image _heatFill;

    private static Sprite s_whiteSprite;

    private void Awake()
    {
        if (_playerXp == null)
            _playerXp = FindAnyObjectByType<PlayerXP>();
        if (_heatManager == null)
            _heatManager = HeatManager.GetInstance();

        BuildUi();
    }

    private void OnEnable()
    {
        if (_playerXp != null)
        {
            _playerXp.OnLevelUp += OnXpOrLevelChanged;
            _playerXp.OnXpProgressChanged += OnXpOrLevelChanged;
        }

        if (_heatManager != null)
            _heatManager.OnHeatChanged += RefreshHeatBar;

        RunCombatStats.OnEnemiesEliminatedChanged += RefreshKills;

        RefreshAll();
    }

    private void OnDisable()
    {
        if (_playerXp != null)
        {
            _playerXp.OnLevelUp -= OnXpOrLevelChanged;
            _playerXp.OnXpProgressChanged -= OnXpOrLevelChanged;
        }

        if (_heatManager != null)
            _heatManager.OnHeatChanged -= RefreshHeatBar;

        RunCombatStats.OnEnemiesEliminatedChanged -= RefreshKills;
    }

    private void OnXpOrLevelChanged(int _) => RefreshLevelAndExp();
    private void OnXpOrLevelChanged() => RefreshLevelAndExp();

    private void BuildUi()
    {
        var canvasGo = new GameObject("SurvivorHUD_Canvas");
        canvasGo.transform.SetParent(transform, false);
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasGo.layer = uiLayer;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

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

        CreateHeatBar(canvasGo.transform);
        CreateStatsBlock(canvasGo.transform);
    }

    private void CreateHeatBar(Transform canvas)
    {
        var trackGo = new GameObject("HeatBarTrack");
        trackGo.transform.SetParent(canvas, false);
        var trackRt = trackGo.AddComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(0f, 1f);
        trackRt.anchorMax = new Vector2(1f, 1f);
        trackRt.pivot = new Vector2(0.5f, 1f);
        trackRt.offsetMin = new Vector2(0f, -_heatBarHeight);
        trackRt.offsetMax = new Vector2(0f, 0f);

        var trackImg = trackGo.AddComponent<Image>();
        trackImg.sprite = GetWhiteSprite();
        trackImg.type = Image.Type.Simple;
        trackImg.color = _heatBarTrackColor;
        trackImg.raycastTarget = false;

        var fillGo = new GameObject("HeatBarFill");
        fillGo.transform.SetParent(trackGo.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        _heatFill = fillGo.AddComponent<Image>();
        _heatFill.sprite = GetWhiteSprite();
        _heatFill.type = Image.Type.Filled;
        _heatFill.fillMethod = Image.FillMethod.Horizontal;
        _heatFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _heatFill.fillAmount = 0f;
        _heatFill.color = _heatBarColor;
        _heatFill.raycastTarget = false;
    }

    private void CreateStatsBlock(Transform canvas)
    {
        float topOffset = _heatBarHeight + _statsPadding.y;

        var panelGo = new GameObject("StatsPanel");
        panelGo.transform.SetParent(canvas, false);
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.anchoredPosition = new Vector2(_statsPadding.x, -topOffset);
        panelRt.sizeDelta = new Vector2(420f, 86f);

        var panelBg = panelGo.AddComponent<Image>();
        panelBg.sprite = GetWhiteSprite();
        panelBg.color = _statsPanelColor;
        panelBg.raycastTarget = false;

        _levelText = CreateHudText(panelGo.transform, "LevelText", new Vector2(10f, -8f));
        _expText = CreateHudText(panelGo.transform, "ExpText", new Vector2(10f, -34f));
        _killsText = CreateHudText(panelGo.transform, "KillsText", new Vector2(10f, -60f));
    }

    private TextMeshProUGUI CreateHudText(Transform parent, string name, Vector2 anchoredPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(-20f, 24f);

        var text = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(text);
        text.fontSize = _statsFontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = "—";
        return text;
    }

    private void RefreshAll()
    {
        RefreshLevelAndExp();
        RefreshKills();
        RefreshHeatBar();
    }

    private void RefreshLevelAndExp()
    {
        if (_levelText == null || _expText == null)
            return;

        if (_playerXp == null)
        {
            _levelText.text = "Level: —";
            _expText.text = "EXP: — / —";
            return;
        }

        _levelText.text = $"Level: {_playerXp.CurrentLevel}";
        _expText.text = _playerXp.IsAtLevelCap
            ? "EXP: MAX"
            : $"EXP: {_playerXp.XpTowardsNext} / {_playerXp.XpRequiredForCurrentLevel}";
    }

    private void RefreshKills()
    {
        if (_killsText == null)
            return;
        _killsText.text = $"Enemies eliminated: {RunCombatStats.EnemiesEliminated}";
    }

    private void RefreshHeatBar()
    {
        if (_heatFill == null)
            return;

        float n = _heatManager != null ? _heatManager.NormalizedHeat : 0f;
        _heatFill.fillAmount = Mathf.Clamp01(n);
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
