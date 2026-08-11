using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TitleScreenController : MonoBehaviour
{
    [SerializeField] private Button _playButton;
    [SerializeField] private Button _weaponSandboxButton;
    [SerializeField] private Button _enemiesTestingButton;
    [SerializeField] private Button _objectivesButton;
    [SerializeField] private Button _quitButton;
    [SerializeField] private ObjectivesMenuUI _objectivesMenu;

    [SerializeField, Tooltip("Destildar para builds: oculta Weapon Sandbox y Enemies Testing del menú principal sin borrar su funcionalidad.")]
    private bool _includeTestingButtons = false;

    private GameObject _canvasRoot;

    private void Awake()
    {
        ShowCursorForMenu();
        EnsureEventSystemWithInputSystemUi();
        CacheSceneButtonsIfNeeded();
        BuildUiIfNeeded();
        WireButtons();
        ApplyTestingButtonVisibility();
        FocusFirstButton();
    }

    private void ApplyTestingButtonVisibility()
    {
        if (_weaponSandboxButton != null)
            _weaponSandboxButton.gameObject.SetActive(_includeTestingButtons);
        if (_enemiesTestingButton != null)
            _enemiesTestingButton.gameObject.SetActive(_includeTestingButtons);
    }

    private void BuildUiIfNeeded()
    {
        if (HasSceneButtons())
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

        _playButton = CreateMenuButton(menuRoot.transform, "Play");
        _objectivesButton = CreateMenuButton(menuRoot.transform, "Objetivos");
        _weaponSandboxButton = CreateMenuButton(menuRoot.transform, "Weapon Sandbox");
        _enemiesTestingButton = CreateMenuButton(menuRoot.transform, "Enemies Testing");
    }

    private Button CreateMenuButton(Transform parent, string label)
    {
        Button button = HudUiFactory.CreateButton(parent, label, new Vector2(360f, 56f));
        button.name = $"{label.Replace(" ", string.Empty)}Button";

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

    private void CacheSceneButtonsIfNeeded()
    {
        _playButton ??= FindSceneButton("PlayButton", "Play");
        _weaponSandboxButton ??= FindSceneButton("WeaponSandboxButton", "Weapon Sandbox");
        _enemiesTestingButton ??= FindSceneButton("EnemiesTestingButton", "Enemies Testing");
        _objectivesButton ??= FindSceneButton("ObjectivesButton", "Objetivos");
        _quitButton ??= FindSceneButton("Quit", "Quit");
    }

    private Button FindSceneButton(string buttonName, string label)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        Button namedButton = buttons.FirstOrDefault(button => button.name == buttonName);
        if (namedButton != null)
            return namedButton;

        return buttons.FirstOrDefault(button => GetButtonLabel(button) == label);
    }

    private static string GetButtonLabel(Button button)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        return label != null ? label.text : string.Empty;
    }

    private bool HasSceneButtons()
    {
        return _playButton != null && _weaponSandboxButton != null && _enemiesTestingButton != null;
    }

    private void WireButtons()
    {
        WireButton(_playButton, LoadPlay);
        WireButton(_weaponSandboxButton, LoadWeaponSandbox);
        WireButton(_enemiesTestingButton, LoadEnemiesTesting);
        WireButton(_objectivesButton, OpenObjectives);
        WireButton(_quitButton, QuitGame);
    }

    private void OpenObjectives()
    {
        if (_objectivesMenu == null)
            _objectivesMenu = FindAnyObjectByType<ObjectivesMenuUI>();
        if (_objectivesMenu == null)
            _objectivesMenu = gameObject.AddComponent<ObjectivesMenuUI>();

        _objectivesMenu.Show();
    }

    private static void WireButton(Button button, UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void FocusFirstButton()
    {
        if (_playButton == null)
            return;

        EventSystem eventSystem = EventSystem.current ?? UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(_playButton.gameObject);
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

    private static void QuitGame()
    {
        SceneNavigation.QuitApplication();
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

    private static void ShowCursorForMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
