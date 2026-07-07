using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TitleScreenSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string PlayScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string WeaponSandboxScenePath = "Assets/Scenes/WeaponTestingSandbox.unity";
    private const string EnemiesTestingScenePath = "Assets/Scenes/enemiesTesting.unity";

    [MenuItem("Tools/Scenes/Rebuild Title Screen")]
    public static void Rebuild()
    {
        SceneSetup();
        ApplyBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void Verify()
    {
        RunVerification("SceneNavigationTests", VerifySceneNavigation);
        RunVerification("TitleScreenControllerTests", VerifyTitleScreenController);
        RunVerification("PauseMenuUITests", VerifyPauseMenu);
        Debug.Log("Title screen verification passed.");
    }

    [MenuItem("Tools/Scenes/Install Debug Scene Pause Menus")]
    public static void InstallDebugScenePauseMenus()
    {
        EnsureDebugScenePauseMenu(WeaponSandboxScenePath);
        EnsureDebugScenePauseMenu(EnemiesTestingScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void SceneSetup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject root = new("TitleScreenRoot");
        TitleScreenController controller = root.AddComponent<TitleScreenController>();

        CreateMainCamera(root.transform);
        GameObject canvas = CreateCanvas(root.transform);
        CreateBackground(canvas.transform);
        Transform menuPanel = CreateMenuPanel(canvas.transform);
        CreateTitle(menuPanel);
        CreateSubtitle(menuPanel);
        CreateDivider(menuPanel);
        Transform menuRoot = CreateMenuRoot(menuPanel);
        Button playButton = CreateMenuButton(menuRoot, "PlayButton", "Play");
        Button weaponSandboxButton = CreateMenuButton(menuRoot, "WeaponSandboxButton", "Weapon Sandbox");
        Button enemiesTestingButton = CreateMenuButton(menuRoot, "EnemiesTestingButton", "Enemies Testing");
        CreateEventSystem(root.transform);
        AssignButtons(controller, playButton, weaponSandboxButton, enemiesTestingButton);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene, ScenePath);
    }

    private static void ApplyBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(PlayScenePath, true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", false),
            new EditorBuildSettingsScene(WeaponSandboxScenePath, true),
            new EditorBuildSettingsScene(EnemiesTestingScenePath, true)
        };
    }

    private static void EnsureDebugScenePauseMenu(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        bool changed = false;

        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
        {
            CreateEventSystem(null);
            changed = true;
        }

        PauseMenuUI[] pauseMenus = UnityEngine.Object.FindObjectsByType<PauseMenuUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (pauseMenus.Length == 0)
        {
            CreateScenePauseMenuCanvas();
            changed = true;
        }

        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void CreateScenePauseMenuCanvas()
    {
        GameObject canvasGo = new("PauseMenuCanvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 650;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        Stretch(canvasGo.GetComponent<RectTransform>());

        GameObject pauseMenuGo = new("PauseMenuUI", typeof(RectTransform));
        pauseMenuGo.transform.SetParent(canvasGo.transform, false);
        Stretch(pauseMenuGo.GetComponent<RectTransform>());
        pauseMenuGo.AddComponent<PauseMenuUI>();
    }

    private static GameObject CreateCanvas(Transform parent)
    {
        GameObject canvasGo = new("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(parent, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        Stretch(canvasGo.GetComponent<RectTransform>());
        return canvasGo;
    }

    private static void CreateMainCamera(Transform parent)
    {
        GameObject cameraGo = new("Main Camera");
        cameraGo.tag = "MainCamera";
        cameraGo.transform.SetParent(parent, false);
        cameraGo.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.043f, 0.04f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 100f;

        cameraGo.AddComponent<AudioListener>();
    }

    private static void CreateBackground(Transform parent)
    {
        GameObject backgroundGo = new("Background", typeof(RectTransform));
        backgroundGo.transform.SetParent(parent, false);
        Stretch(backgroundGo.GetComponent<RectTransform>());

        Image background = backgroundGo.AddComponent<Image>();
        background.color = new Color(0.035f, 0.043f, 0.04f, 1f);
        background.raycastTarget = true;
    }

    private static Transform CreateMenuPanel(Transform parent)
    {
        GameObject panelGo = new("MenuPanel", typeof(RectTransform));
        panelGo.transform.SetParent(parent, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(720f, 560f);

        Image panel = panelGo.AddComponent<Image>();
        panel.color = new Color(0.065f, 0.078f, 0.074f, 0.94f);
        panel.raycastTarget = false;
        return panelGo.transform;
    }

    private static void CreateTitle(Transform parent)
    {
        TextMeshProUGUI title = CreateText(parent, "Title", "SCRAP WAVES", 64f, TextAlignmentOptions.Center);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 180f);
        titleRect.sizeDelta = new Vector2(640f, 78f);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 8f;
        title.color = new Color(0.95f, 0.96f, 0.92f, 1f);
    }

    private static void CreateSubtitle(Transform parent)
    {
        TextMeshProUGUI subtitle = CreateText(parent, "Subtitle", "SELECT DESTINATION", 18f, TextAlignmentOptions.Center);
        RectTransform subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
        subtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
        subtitleRect.pivot = new Vector2(0.5f, 0.5f);
        subtitleRect.anchoredPosition = new Vector2(0f, 122f);
        subtitleRect.sizeDelta = new Vector2(420f, 28f);
        subtitle.characterSpacing = 3f;
        subtitle.color = new Color(0.68f, 0.74f, 0.69f, 1f);
    }

    private static void CreateDivider(Transform parent)
    {
        GameObject dividerGo = new("Divider", typeof(RectTransform));
        dividerGo.transform.SetParent(parent, false);
        RectTransform dividerRect = dividerGo.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0.5f, 0.5f);
        dividerRect.anchorMax = new Vector2(0.5f, 0.5f);
        dividerRect.pivot = new Vector2(0.5f, 0.5f);
        dividerRect.anchoredPosition = new Vector2(0f, 86f);
        dividerRect.sizeDelta = new Vector2(440f, 2f);

        Image divider = dividerGo.AddComponent<Image>();
        divider.color = new Color(0.66f, 0.78f, 0.56f, 0.8f);
        divider.raycastTarget = false;
    }

    private static Transform CreateMenuRoot(Transform parent)
    {
        GameObject menuRoot = new("MenuEntries", typeof(RectTransform));
        menuRoot.transform.SetParent(parent, false);
        RectTransform menuRect = menuRoot.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(0.5f, 0.5f);
        menuRect.anchorMax = new Vector2(0.5f, 0.5f);
        menuRect.pivot = new Vector2(0.5f, 0.5f);
        menuRect.anchoredPosition = new Vector2(0f, -48f);
        menuRect.sizeDelta = new Vector2(460f, 240f);

        VerticalLayoutGroup layout = menuRoot.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 14f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = menuRoot.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return menuRoot.transform;
    }

    private static Button CreateMenuButton(Transform parent, string name, string label)
    {
        GameObject buttonGo = new(name, typeof(RectTransform));
        buttonGo.transform.SetParent(parent, false);
        RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(440f, 62f);

        Image background = buttonGo.AddComponent<Image>();
        background.color = new Color(0.12f, 0.145f, 0.135f, 1f);

        Button button = buttonGo.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = background.color;
        colors.highlightedColor = new Color(0.19f, 0.24f, 0.22f, 1f);
        colors.pressedColor = new Color(0.08f, 0.1f, 0.095f, 1f);
        colors.selectedColor = new Color(0.16f, 0.2f, 0.18f, 1f);
        button.colors = colors;

        CreateButtonAccent(buttonGo.transform);

        TextMeshProUGUI labelText = CreateText(buttonGo.transform, "Label", label, 23f, TextAlignmentOptions.Center);
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color(0.93f, 0.95f, 0.9f, 1f);
        return button;
    }

    private static void CreateButtonAccent(Transform parent)
    {
        GameObject accentGo = new("Accent", typeof(RectTransform));
        accentGo.transform.SetParent(parent, false);
        RectTransform accentRect = accentGo.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.offsetMin = Vector2.zero;
        accentRect.offsetMax = new Vector2(6f, 0f);

        Image accent = accentGo.AddComponent<Image>();
        accent.color = new Color(0.66f, 0.78f, 0.56f, 1f);
        accent.raycastTarget = false;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textGo = new(name, typeof(RectTransform));
        textGo.transform.SetParent(parent, false);
        Stretch(textGo.GetComponent<RectTransform>());

        TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static void CreateEventSystem(Transform parent)
    {
        GameObject eventSystem = new("EventSystem");
        if (parent != null)
            eventSystem.transform.SetParent(parent, false);
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private static void AssignButtons(TitleScreenController controller, Button playButton, Button weaponSandboxButton, Button enemiesTestingButton)
    {
        SerializedObject serializedController = new(controller);
        serializedController.FindProperty("_playButton").objectReferenceValue = playButton;
        serializedController.FindProperty("_weaponSandboxButton").objectReferenceValue = weaponSandboxButton;
        serializedController.FindProperty("_enemiesTestingButton").objectReferenceValue = enemiesTestingButton;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static void Stretch(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static void VerifySceneNavigation()
    {
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.Title, "TitleScreen"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.Play, "GameplayScene"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.WeaponSandbox, "WeaponTestingSandbox"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.EnemiesTesting, "enemiesTesting"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.PrepareForSceneChange_ResetsPausedTimeScale());
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.Title, "Assets/Scenes/TitleScreen.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.Play, "Assets/Scenes/GameplayScene.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.WeaponSandbox, "Assets/Scenes/WeaponTestingSandbox.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.EnemiesTesting, "Assets/Scenes/enemiesTesting.unity"));
    }

    private static void VerifyTitleScreenController()
    {
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_CreatesExactlyThreeMenuButtonsWithExpectedLabels());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_CreatesEventSystemWhenMissing());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_UnlocksAndShowsCursorForMenu());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_WiresButtonsToSharedSceneNavigationMethods());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.EnabledBuildSettingsOrder_MatchesTitleScreenBootFlow());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.TitleScreenScene_HasEditableCanvasWithControllerAndButtons());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.WeaponSandboxScene_HasPauseMenuUiForEscapeMenu());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.EnemiesTestingScene_HasPauseMenuUiForEscapeMenu());
    }

    private static void VerifyPauseMenu()
    {
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.Awake_CreatesResumeAndReturnToTitleButtons());
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.Awake_WiresPauseButtonsToExpectedHandlers());
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.Awake_CreatesTopInteractiveCanvasAboveDebugUi());
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.ShowPause_UnlocksAndShowsCursorWithoutCamera());
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.ShowPause_ForcesSandboxDebugUiBackToUnlockedMouseMode());
    }

    private static void RunVerification(string name, Action action)
    {
        try
        {
            action();
            Debug.Log($"{name}: PASS");
        }
        catch (Exception exception)
        {
            Debug.LogError($"{name}: FAIL\n{exception}");
            throw;
        }
    }

    private static void RunFixture<T>(Func<T> createFixture, Action<T> runTest) where T : class
    {
        T fixture = createFixture();

        fixture.GetType().GetMethod("SetUp")?.Invoke(fixture, null);
        try
        {
            runTest(fixture);
        }
        finally
        {
            fixture.GetType().GetMethod("TearDown")?.Invoke(fixture, null);
        }
    }
}
