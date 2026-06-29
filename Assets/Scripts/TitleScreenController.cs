using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TitleScreenController : MonoBehaviour
{
    private GameObject _canvasRoot;
    private Button _firstButton;

    private void Awake()
    {
        EnsureEventSystemWithInputSystemUi();
        BuildUiIfNeeded();
        FocusFirstButton();
    }

    private void BuildUiIfNeeded()
    {
        if (_canvasRoot != null)
            return;

        _canvasRoot = new GameObject("Canvas", typeof(RectTransform));
        _canvasRoot.transform.SetParent(transform, false);

        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = _canvasRoot.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        Image background = HudUiFactory.CreatePanel(_canvasRoot.transform, "Background", Vector2.zero);
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        background.color = new Color(0.02f, 0.02f, 0.02f, 1f);
        background.raycastTarget = true;

        TextMeshProUGUI title = HudUiFactory.CreateLabel(_canvasRoot.transform, "Title", "SCRAP WAVES", 56f, TextAlignmentOptions.Center);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.72f);
        titleRect.anchorMax = new Vector2(0.5f, 0.72f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(640f, 72f);
        title.fontStyle = FontStyles.Bold;

        GameObject menuRoot = new("MenuEntries", typeof(RectTransform));
        menuRoot.transform.SetParent(_canvasRoot.transform, false);
        RectTransform menuRect = menuRoot.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(0.5f, 0.5f);
        menuRect.anchorMax = new Vector2(0.5f, 0.5f);
        menuRect.pivot = new Vector2(0.5f, 0.5f);
        menuRect.anchoredPosition = new Vector2(0f, -24f);
        menuRect.sizeDelta = new Vector2(360f, 220f);

        VerticalLayoutGroup layout = menuRoot.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 18f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = menuRoot.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        _firstButton = CreateMenuButton(menuRoot.transform, "Play", LoadPlay);
        CreateMenuButton(menuRoot.transform, "Weapon Sandbox", LoadWeaponSandbox);
        CreateMenuButton(menuRoot.transform, "Enemies Testing", LoadEnemiesTesting);
    }

    private Button CreateMenuButton(Transform parent, string label, UnityAction onClick)
    {
        Button button = HudUiFactory.CreateButton(parent, label, new Vector2(360f, 56f));
        button.name = $"{label.Replace(" ", string.Empty)}Button";
        button.onClick.AddListener(onClick);

        if (button.TryGetComponent(out Image background))
            background.color = new Color(1f, 1f, 1f, 0.12f);

        TextMeshProUGUI labelText = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (labelText != null)
        {
            labelText.fontSize = 24f;
            labelText.fontStyle = FontStyles.Bold;
        }

        return button;
    }

    private void FocusFirstButton()
    {
        if (_firstButton == null)
            return;

        EventSystem eventSystem = EventSystem.current ?? UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(_firstButton.gameObject);
    }

    private void LoadPlay()
    {
        SceneNavigation.LoadPlay();
    }

    private void LoadWeaponSandbox()
    {
        SceneNavigation.LoadWeaponSandbox();
    }

    private void LoadEnemiesTesting()
    {
        SceneNavigation.LoadEnemiesTesting();
    }

    private static void EnsureEventSystemWithInputSystemUi()
    {
        EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (existing != null)
        {
            StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null)
                DestroyComponent(legacy);

            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        GameObject eventSystem = new("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private static void DestroyComponent(Object component)
    {
        if (component == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(component);
        else
            UnityEngine.Object.DestroyImmediate(component);
    }
}
