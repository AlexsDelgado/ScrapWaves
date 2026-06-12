using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public readonly struct LevelUpChoiceOption
{
    public LevelUpChoiceOption(string displayName, string description = null, Sprite icon = null, HudPlaceholderKind placeholder = HudPlaceholderKind.None)
    {
        DisplayName = displayName;
        Description = description;
        Icon = icon;
        Placeholder = placeholder;
    }

    public string DisplayName { get; }
    public string Description { get; }
    public Sprite Icon { get; }
    public HudPlaceholderKind Placeholder { get; }
}

[DisallowMultipleComponent]
public class LevelUpChoiceUI : MonoBehaviour
{
    [SerializeField] private bool _pauseWhileChoosing = true;
    [SerializeField, Min(180)] private float _cardWidth = 240f;
    [SerializeField, Min(120)] private float _cardHeight = 160f;
    [SerializeField] private Canvas _canvasOverride;
    [SerializeField] private ThirdPersonCamera _thirdPersonCamera;

    private Canvas _canvas;
    private RectTransform _cardsRow;
    private TextMeshProUGUI _titleText;
    private readonly List<Button> _spawnedButtons = new();
    private ThirdPersonCamera _resolvedCamera;
    private float _previousTimeScale = 1f;
    private Action<int> _onSelected;
    private IReadOnlyList<LevelUpChoiceOption> _currentOptions;
    private bool _isVisible;

    public bool IsVisible => _isVisible;

    public IEnumerator PresentCoroutine(string title, IReadOnlyList<LevelUpChoiceOption> options, Action<int> onComplete)
    {
        if (options == null || options.Count == 0)
        {
            onComplete?.Invoke(-1);
            yield break;
        }

        bool done = false;
        int selectedIndex = -1;

        Show(title, options, index =>
        {
            selectedIndex = index;
            done = true;
        });

        while (!done)
            yield return null;

        onComplete?.Invoke(selectedIndex);
    }

    public void Show(string title, IReadOnlyList<LevelUpChoiceOption> options, Action<int> onSelected)
    {
        if (options == null || options.Count == 0)
        {
            onSelected?.Invoke(-1);
            return;
        }

        _currentOptions = options;
        _onSelected = onSelected;
        _isVisible = true;

        SetCameraBlocked(true);

        if (_pauseWhileChoosing)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        EnsureUiExists();
        if (_titleText != null)
            _titleText.text = title;

        RefreshCards();
        _canvas.gameObject.SetActive(true);
    }

    private void OnOptionClicked(int index)
    {
        if (!_isVisible || _currentOptions == null || index < 0 || index >= _currentOptions.Count)
            return;

        Hide();
        _onSelected?.Invoke(index);
        _onSelected = null;
        _currentOptions = null;
    }

    private void Hide()
    {
        _isVisible = false;

        if (_canvas != null && _canvasOverride == null)
            _canvas.gameObject.SetActive(false);
        else if (_canvasOverride != null)
            _canvasOverride.gameObject.SetActive(false);

        SetCameraBlocked(false);

        if (_pauseWhileChoosing)
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
    }

    private void EnsureUiExists()
    {
        if (_canvasOverride != null)
        {
            _canvas = _canvasOverride;
            CacheRowIfNeeded();
            return;
        }

        if (_canvas != null)
            return;

        EnsureEventSystemWithInputSystemUi();

        var canvasGo = new GameObject("LevelUpChoiceCanvas", typeof(RectTransform));
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = panel.AddComponent<Image>();
        panelImg.sprite = HudUiFactory.WhiteSprite;
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panel.transform, false);
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.65f);
        titleRt.anchorMax = new Vector2(0.5f, 0.65f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(800f, 56f);
        titleRt.anchoredPosition = Vector2.zero;
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_titleText);
        _titleText.fontSize = 28f;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = Color.white;

        var rowGo = new GameObject("CardsRow", typeof(RectTransform));
        rowGo.transform.SetParent(panel.transform, false);
        _cardsRow = rowGo.GetComponent<RectTransform>();
        _cardsRow.anchorMin = new Vector2(0.5f, 0.42f);
        _cardsRow.anchorMax = new Vector2(0.5f, 0.42f);
        _cardsRow.pivot = new Vector2(0.5f, 0.5f);
        _cardsRow.sizeDelta = new Vector2(1200f, _cardHeight + 16f);
        _cardsRow.anchoredPosition = Vector2.zero;

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 20f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        _canvas.gameObject.SetActive(false);
    }

    private void CacheRowIfNeeded()
    {
        if (_cardsRow != null || _canvas == null)
            return;

        Transform row = _canvas.transform.Find("Panel/CardsRow");
        if (row == null)
            row = _canvas.transform.Find("Panel/ButtonsRow");
        if (row != null)
            _cardsRow = row as RectTransform;

        Transform title = _canvas.transform.Find("Panel/Title");
        if (title != null)
            _titleText = title.GetComponent<TextMeshProUGUI>();
    }

    private void RefreshCards()
    {
        if (_currentOptions == null || _cardsRow == null)
            return;

        foreach (Button button in _spawnedButtons)
        {
            if (button != null)
                Destroy(button.gameObject);
        }

        _spawnedButtons.Clear();

        for (int i = 0; i < _currentOptions.Count; i++)
        {
            LevelUpChoiceOption option = _currentOptions[i];
            int captured = i;
            Button btn = CreateChoiceCard(option, captured);
            _spawnedButtons.Add(btn);
        }
    }

    private Button CreateChoiceCard(LevelUpChoiceOption option, int index)
    {
        var cardGo = new GameObject($"ChoiceCard_{index}", typeof(RectTransform));
        cardGo.transform.SetParent(_cardsRow, false);
        var cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.sizeDelta = new Vector2(_cardWidth, _cardHeight);

        var bg = cardGo.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = HudUiFactory.BorderColor;

        var btn = cardGo.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f);
        colors.pressedColor = new Color(0.2f, 0.22f, 0.28f);
        btn.colors = colors;
        btn.onClick.AddListener(() => OnOptionClicked(index));

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(cardGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = new Vector2(0f, -12f);
        iconRt.sizeDelta = new Vector2(64f, 64f);
        var icon = iconGo.AddComponent<Image>();
        icon.sprite = option.Icon != null ? option.Icon : HudUiFactory.WhiteSprite;
        icon.color = option.Icon != null ? Color.white : HudUiFactory.GetPlaceholderColor(option.Placeholder);
        icon.raycastTarget = false;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(cardGo.transform, false);
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0.42f);
        titleRt.anchorMax = new Vector2(1f, 0.42f);
        titleRt.offsetMin = new Vector2(10f, 0f);
        titleRt.offsetMax = new Vector2(-10f, 28f);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(title);
        title.fontSize = 18f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        title.text = option.DisplayName;
        title.raycastTarget = false;

        var descGo = new GameObject("Description", typeof(RectTransform));
        descGo.transform.SetParent(cardGo.transform, false);
        var descRt = descGo.GetComponent<RectTransform>();
        descRt.anchorMin = new Vector2(0f, 0f);
        descRt.anchorMax = new Vector2(1f, 0.42f);
        descRt.offsetMin = new Vector2(10f, 10f);
        descRt.offsetMax = new Vector2(-10f, 0f);
        var desc = descGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(desc);
        desc.fontSize = 14f;
        desc.alignment = TextAlignmentOptions.Top;
        desc.color = HudUiFactory.MutedTextColor;
        desc.text = string.IsNullOrEmpty(option.Description) ? " " : option.Description;
        desc.enableWordWrapping = true;
        desc.raycastTarget = false;

        return btn;
    }

    private static void EnsureEventSystemWithInputSystemUi()
    {
        EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (existing != null)
        {
            StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null)
                UnityEngine.Object.Destroy(legacy);

            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void SetCameraBlocked(bool blocked)
    {
        if (_resolvedCamera == null)
        {
            _resolvedCamera = _thirdPersonCamera != null
                ? _thirdPersonCamera
                : UnityEngine.Object.FindFirstObjectByType<ThirdPersonCamera>();
        }

        _resolvedCamera?.SetLookBlockedByUi(blocked);
    }
}
