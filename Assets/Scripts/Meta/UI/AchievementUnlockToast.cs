using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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
    private AchievementUnlockToastView _sceneView;
    private bool _sceneViewBound;
    private GameObject _legacyToast;
    private CanvasGroup _legacyGroup;
    private RectTransform _legacyRect;

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
        FinishPresentation();
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
        while (_pending.Count > 0)
        {
            AchievementDefinition achievement = _pending.Dequeue();
            yield return ShowToastCoroutine(achievement);
        }

        _isShowing = false;
    }

    private IEnumerator ShowToastCoroutine(AchievementDefinition achievement)
    {
        float fadeIn = 0f;
        while (fadeIn < _fadeDuration)
        {
            if (!TryPreparePresentation(achievement)) { yield return null; continue; }
            fadeIn += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeIn / _fadeDuration);
            SetPresentationAlpha(t);
            yield return null;
        }

        float hold = 0f;
        while (hold < _visibleDuration)
        {
            if (!TryPreparePresentation(achievement)) { yield return null; continue; }
            SetPresentationAlpha(1f);
            hold += Time.unscaledDeltaTime;
            yield return null;
        }

        float fadeOut = 0f;
        while (fadeOut < _fadeDuration)
        {
            if (!TryPreparePresentation(achievement)) { yield return null; continue; }
            fadeOut += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeOut / _fadeDuration);
            SetPresentationAlpha(1f - t);
            yield return null;
        }

        FinishPresentation();
    }

    // The title screen retains its existing toast construction. Gameplay uses only
    // the saved scene view and waits through scene transitions without losing queue order.
    private bool TryPreparePresentation(AchievementDefinition achievement)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name == "TitleScreen")
        {
            if (_sceneView != null) _sceneView.Hide();
            _sceneView = null;
            _sceneViewBound = false;
            EnsureUiExists();
            _canvas.gameObject.SetActive(true);
            if (_legacyToast == null)
            {
                _legacyToast = BuildToast(achievement);
                _legacyGroup = _legacyToast.GetComponent<CanvasGroup>();
                _legacyRect = _legacyToast.GetComponent<RectTransform>();
            }
            return true;
        }

        if (_legacyToast != null) Destroy(_legacyToast);
        _legacyToast = null;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
        if (_sceneView == null || !_sceneView.IsConfigured || _sceneView.gameObject.scene != scene)
        {
            if (_sceneView != null) _sceneView.Hide();
            _sceneView = null;
            _sceneViewBound = false;
            foreach (AchievementUnlockToastView candidate in FindObjectsByType<AchievementUnlockToastView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != scene || !candidate.IsConfigured) continue;
                _sceneView = candidate;
                break;
            }
        }
        if (_sceneView == null) return false;
        if (!_sceneView.CanPresent)
        {
            _sceneView.Hide();
            _sceneViewBound = false;
            return false;
        }
        if (!_sceneViewBound)
        {
            _sceneView.Bind(achievement);
            _sceneViewBound = true;
        }
        return true;
    }

    private void SetPresentationAlpha(float alpha)
    {
        if (_sceneView != null)
            _sceneView.SetAnimation(alpha);
        else if (_legacyGroup != null && _legacyRect != null)
        {
            _legacyGroup.alpha = alpha;
            _legacyRect.anchoredPosition = _anchoredOffset + new Vector2(24f * (1f - alpha), 0f);
        }
    }

    private void FinishPresentation()
    {
        if (_sceneView != null) _sceneView.Hide();
        _sceneViewBound = false;
        if (_legacyToast != null) Destroy(_legacyToast);
        _legacyToast = null;
        _legacyGroup = null;
        _legacyRect = null;
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
