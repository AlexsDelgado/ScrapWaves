using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A reusable achievement toast authored beneath the gameplay scene UI root.</summary>
[DisallowMultipleComponent]
public sealed class AchievementUnlockToastView : MonoBehaviour
{
    [SerializeField] private Canvas _canvas;
    [SerializeField] private RectTransform _panel;
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private GameObject _iconRoot;
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _header;
    [SerializeField] private TextMeshProUGUI _title;
    [SerializeField] private TextMeshProUGUI _scrap;
    [SerializeField] private float _textInsetWithIcon = 80f;
    [SerializeField] private float _textInsetWithoutIcon = 14f;
    [SerializeField] private Vector2 _slideOffset = new(24f, 0f);
    private Vector2 _shownPosition;
    private bool _positionCached;

    public bool IsConfigured => _canvas != null && _panel != null && _group != null
        && _iconRoot != null && _icon != null && _header != null && _title != null && _scrap != null;
    public bool CanPresent => IsConfigured && isActiveAndEnabled && _canvas.isActiveAndEnabled;

    private void Awake()
    {
        CachePosition();
        Hide();
    }

    private void CachePosition()
    {
        if (_positionCached || _panel == null) return;
        _shownPosition = _panel.anchoredPosition;
        _positionCached = true;
    }

    public void Bind(AchievementDefinition achievement)
    {
        if (!IsConfigured || achievement == null) return;
        CachePosition();
        _icon.sprite = achievement.Icon;
        _iconRoot.SetActive(achievement.Icon != null);
        _title.text = achievement.DisplayName;
        _scrap.text = achievement.ScrapReward > 0 ? $"+{achievement.ScrapReward} scrap" : string.Empty;
        float left = achievement.Icon != null ? _textInsetWithIcon : _textInsetWithoutIcon;
        SetTextInset(_header, left);
        SetTextInset(_title, left);
        SetTextInset(_scrap, left);
        _panel.gameObject.SetActive(true);
        SetAnimation(0f);
    }

    public void SetAnimation(float alpha)
    {
        if (!IsConfigured) return;
        _group.alpha = Mathf.Clamp01(alpha);
        _panel.anchoredPosition = _shownPosition + _slideOffset * (1f - _group.alpha);
    }

    public void Hide()
    {
        if (_panel == null) return;
        CachePosition();
        _panel.anchoredPosition = _shownPosition;
        if (_group != null) _group.alpha = 0f;
        _panel.gameObject.SetActive(false);
    }

    private static void SetTextInset(TMP_Text text, float left)
    {
        Vector2 offset = text.rectTransform.offsetMin;
        offset.x = left;
        text.rectTransform.offsetMin = offset;
    }

#if UNITY_EDITOR
    public static AchievementUnlockToastView AuthorUi(Transform uiRoot)
    {
        AchievementUnlockToastView existing = uiRoot.GetComponentInChildren<AchievementUnlockToastView>(true);
        if (existing != null) return existing;

        var canvasGo = new GameObject("AchievementUnlockToastCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(uiRoot, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 750;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var view = canvasGo.AddComponent<AchievementUnlockToastView>();
        view._canvas = canvas;
        Image panel = HudUiFactory.CreatePanel(canvasGo.transform, "AchievementToast", new Vector2(360f, 88f));
        panel.color = Color.clear;
        panel.raycastTarget = false;
        view._panel = panel.rectTransform;
        view._panel.anchorMin = view._panel.anchorMax = Vector2.one;
        view._panel.pivot = Vector2.one;
        view._panel.anchoredPosition = new Vector2(-16f, -16f);
        view._group = panel.gameObject.AddComponent<CanvasGroup>();
        view._group.blocksRaycasts = false;
        view._group.interactable = false;
        CreateBackground(panel.transform, "Border", 0f, HudUiFactory.BorderColor);
        CreateBackground(panel.transform, "Fill", 2f, HudUiFactory.PanelColor);

        view._icon = HudUiFactory.CreateIconSlot(panel.transform, "Icon", 56f, null, HudPlaceholderKind.None);
        view._icon.color = Color.white;
        view._iconRoot = view._icon.transform.parent.gameObject;
        var iconRect = (RectTransform)view._iconRoot.transform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(12f, 0f);
        view._header = CreateLabel(panel.transform, "Header", "Objetivo completado", 16f, 46f, -10f,
            TextAlignmentOptions.TopLeft, HudUiFactory.MutedTextColor);
        view._title = CreateLabel(panel.transform, "Title", "Achievement", 22f, 22f, -28f,
            TextAlignmentOptions.TopLeft, Color.white);
        view._title.fontStyle = FontStyles.Bold;
        view._scrap = CreateLabel(panel.transform, "Scrap", string.Empty, 16f, 8f, -52f,
            TextAlignmentOptions.BottomLeft, new Color(0.55f, 0.95f, 0.65f, 1f));
        view.Hide();
        UnityEditor.EditorUtility.SetDirty(view);
        return view;
    }

    private static void CreateBackground(Transform parent, string name, float inset, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = Vector2.one * inset;
        image.rectTransform.offsetMax = Vector2.one * -inset;
        image.sprite = HudUiFactory.WhiteSprite;
        image.color = color;
        image.raycastTarget = false;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float fontSize,
        float bottom, float top, TextAlignmentOptions alignment, Color color)
    {
        TextMeshProUGUI label = HudUiFactory.CreateLabel(parent, name, text, fontSize, alignment);
        label.color = color;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(14f, bottom);
        label.rectTransform.offsetMax = new Vector2(-12f, top);
        return label;
    }
#endif
}
