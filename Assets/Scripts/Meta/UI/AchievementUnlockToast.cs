using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toast no bloqueante (esquina superior derecha) cuando se completa un challenge meta.
/// Escucha <see cref="SaveManager.OnAchievementUnlocked"/>.
/// </summary>
[DefaultExecutionOrder(-130)]
[DisallowMultipleComponent]
public sealed class AchievementUnlockToast : MonoBehaviour
{
    private const float PanelWidth = 360f;
    private const float PanelHeight = 88f;
    private const int SortingOrder = 750;

    [SerializeField, Min(0.5f)] private float _visibleDuration = 3f;
    [SerializeField, Min(0.05f)] private float _fadeDuration = 0.25f;
    [SerializeField] private Vector2 _anchoredOffset = new(-16f, -16f);

    public static AchievementUnlockToast Instance { get; private set; }

    private readonly Queue<AchievementDefinition> _pending = new();
    private Canvas _canvas;
    private RectTransform _slot;
    private bool _isShowing;
    private SaveManager _subscribedSaveManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (_subscribedSaveManager == null)
            TrySubscribe();
    }

    private void TrySubscribe()
    {
        SaveManager save = SaveManager.Instance;
        if (save == null || save == _subscribedSaveManager)
            return;

        Unsubscribe();
        _subscribedSaveManager = save;
        _subscribedSaveManager.OnAchievementUnlocked += HandleAchievementUnlocked;
    }

    private void Unsubscribe()
    {
        if (_subscribedSaveManager == null)
            return;

        _subscribedSaveManager.OnAchievementUnlocked -= HandleAchievementUnlocked;
        _subscribedSaveManager = null;
    }

    private void HandleAchievementUnlocked(AchievementDefinition achievement)
    {
        if (achievement == null)
            return;

        if (ShouldSkipToast())
            return;

        _pending.Enqueue(achievement);
        if (!_isShowing)
            StartCoroutine(ShowQueueCoroutine());
    }

    private static bool ShouldSkipToast()
    {
        ObjectivesMenuUI menu = FindAnyObjectByType<ObjectivesMenuUI>();
        return menu != null && menu.IsVisible;
    }

    private IEnumerator ShowQueueCoroutine()
    {
        _isShowing = true;
        EnsureUiExists();

        while (_pending.Count > 0)
        {
            AchievementDefinition achievement = _pending.Dequeue();
            yield return ShowToastCoroutine(achievement);
        }

        _isShowing = false;
    }

    private IEnumerator ShowToastCoroutine(AchievementDefinition achievement)
    {
        GameObject toastGo = BuildToast(achievement);
        CanvasGroup group = toastGo.GetComponent<CanvasGroup>();
        RectTransform rt = toastGo.GetComponent<RectTransform>();

        group.alpha = 0f;
        Vector2 shown = _anchoredOffset;
        Vector2 hidden = shown + new Vector2(24f, 0f);
        rt.anchoredPosition = hidden;

        float fadeIn = 0f;
        while (fadeIn < _fadeDuration)
        {
            fadeIn += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeIn / _fadeDuration);
            group.alpha = t;
            rt.anchoredPosition = Vector2.Lerp(hidden, shown, t);
            yield return null;
        }

        group.alpha = 1f;
        rt.anchoredPosition = shown;

        float hold = 0f;
        while (hold < _visibleDuration)
        {
            hold += Time.unscaledDeltaTime;
            yield return null;
        }

        float fadeOut = 0f;
        while (fadeOut < _fadeDuration)
        {
            fadeOut += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeOut / _fadeDuration);
            group.alpha = 1f - t;
            rt.anchoredPosition = Vector2.Lerp(shown, hidden, t);
            yield return null;
        }

        Destroy(toastGo);
    }

    private GameObject BuildToast(AchievementDefinition achievement)
    {
        Image panel = HudUiFactory.CreatePanel(_slot, "AchievementToast", new Vector2(PanelWidth, PanelHeight));
        RectTransform panelRt = panel.rectTransform;
        panelRt.anchorMin = new Vector2(1f, 1f);
        panelRt.anchorMax = new Vector2(1f, 1f);
        panelRt.pivot = new Vector2(1f, 1f);
        panelRt.anchoredPosition = _anchoredOffset;

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        Image borderImg = new GameObject("Border", typeof(RectTransform)).AddComponent<Image>();
        borderImg.transform.SetParent(panel.transform, false);
        borderImg.transform.SetAsFirstSibling();
        RectTransform borderRt = borderImg.rectTransform;
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = Vector2.zero;
        borderRt.offsetMax = Vector2.zero;
        borderImg.sprite = HudUiFactory.WhiteSprite;
        borderImg.color = HudUiFactory.BorderColor;
        borderImg.raycastTarget = false;

        Image fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
        fill.transform.SetParent(panel.transform, false);
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);
        fill.sprite = HudUiFactory.WhiteSprite;
        fill.color = HudUiFactory.PanelColor;
        fill.raycastTarget = false;

        // Recolor outer panel transparent so border shows
        panel.color = new Color(0f, 0f, 0f, 0f);

        float leftPad = 14f;
        if (achievement.Icon != null)
        {
            Image icon = HudUiFactory.CreateIconSlot(panel.transform, "Icon", 56f, achievement.Icon, HudPlaceholderKind.None);
            RectTransform iconRt = icon.transform.parent as RectTransform;
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(12f, 0f);
            leftPad = 80f;
        }

        TextMeshProUGUI header = HudUiFactory.CreateLabel(
            panel.transform,
            "Header",
            "Objetivo completado",
            16f,
            TextAlignmentOptions.TopLeft);
        header.color = HudUiFactory.MutedTextColor;
        RectTransform headerRt = header.rectTransform;
        headerRt.anchorMin = new Vector2(0f, 0f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.offsetMin = new Vector2(leftPad, 46f);
        headerRt.offsetMax = new Vector2(-12f, -10f);

        TextMeshProUGUI title = HudUiFactory.CreateLabel(
            panel.transform,
            "Title",
            achievement.DisplayName,
            22f,
            TextAlignmentOptions.TopLeft);
        title.fontStyle = FontStyles.Bold;
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 0f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(leftPad, 22f);
        titleRt.offsetMax = new Vector2(-12f, -28f);

        string scrapLine = achievement.ScrapReward > 0
            ? $"+{achievement.ScrapReward} scrap"
            : string.Empty;
        TextMeshProUGUI scrap = HudUiFactory.CreateLabel(
            panel.transform,
            "Scrap",
            scrapLine,
            16f,
            TextAlignmentOptions.BottomLeft);
        scrap.color = new Color(0.55f, 0.95f, 0.65f, 1f);
        RectTransform scrapRt = scrap.rectTransform;
        scrapRt.anchorMin = new Vector2(0f, 0f);
        scrapRt.anchorMax = new Vector2(1f, 1f);
        scrapRt.offsetMin = new Vector2(leftPad, 8f);
        scrapRt.offsetMax = new Vector2(-12f, -52f);

        return panel.gameObject;
    }

    private void EnsureUiExists()
    {
        if (_canvas != null)
            return;

        var canvasGo = new GameObject("AchievementUnlockToastCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var slotGo = new GameObject("ToastSlot", typeof(RectTransform));
        slotGo.transform.SetParent(canvasGo.transform, false);
        _slot = slotGo.GetComponent<RectTransform>();
        _slot.anchorMin = Vector2.zero;
        _slot.anchorMax = Vector2.one;
        _slot.offsetMin = Vector2.zero;
        _slot.offsetMax = Vector2.zero;
    }

    private static void EnsureExists()
    {
        if (Instance != null)
            return;

        var go = new GameObject(nameof(AchievementUnlockToast));
        go.AddComponent<AchievementUnlockToast>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => EnsureExists();
}
