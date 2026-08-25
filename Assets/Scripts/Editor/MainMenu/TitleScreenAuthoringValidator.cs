using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TitleScreenAuthoringValidator
{
    public const string ScenePath = "Assets/Scenes/TitleScreen.unity";

    public sealed class Result
    {
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public bool IsValid => _errors.Count == 0;

        internal void Error(string message) => _errors.Add(message);
        internal void Warning(string message) => _warnings.Add(message);
    }

    [MenuItem("Tools/Scrap Waves/Validate Authored Title Screen")]
    public static void ValidateMenuItem()
    {
        Result result = ValidateAsset();
        Log(result);
        if (!result.IsValid)
            throw new InvalidOperationException($"Title screen authoring validation failed with {result.Errors.Count} error(s).");
    }

    public static Result ValidateAsset()
    {
        Result result = new();
        if (!File.Exists(ScenePath))
        {
            result.Error($"Missing production scene: {ScenePath}");
            return result;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ValidateScene(scene, result);
        }
        finally
        {
            if (setup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        return result;
    }

    public static Result ValidateScene(Scene scene)
    {
        Result result = new();
        ValidateScene(scene, result);
        return result;
    }

    public static void Log(Result result)
    {
        for (int i = 0; i < result.Errors.Count; i++)
            Debug.LogError($"TitleScreen authoring: {result.Errors[i]}");
        for (int i = 0; i < result.Warnings.Count; i++)
            Debug.LogWarning($"TitleScreen authoring: {result.Warnings[i]}");
        if (result.IsValid)
            Debug.Log($"TitleScreen authoring validation passed with {result.Warnings.Count} warning(s).");
    }

    private static void ValidateScene(Scene scene, Result result)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            result.Error("The TitleScreen scene is not loaded.");
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        TitleScreenController[] controllers = FindInScene<TitleScreenController>(roots);
        EventSystem[] eventSystems = FindInScene<EventSystem>(roots);
        RequireExactlyOne(controllers, nameof(TitleScreenController), result);
        RequireExactlyOne(eventSystems, nameof(EventSystem), result);
        RequireExactlyOne(FindInScene<UserSettingsService>(roots), nameof(UserSettingsService), result);
        UserSettingsApplier[] settingsAppliers = FindInScene<UserSettingsApplier>(roots);
        RequireExactlyOne(settingsAppliers, nameof(UserSettingsApplier), result);
        RequireExactlyOne(FindInScene<ScrapSceneTransition>(roots), nameof(ScrapSceneTransition), result);
        TitleScreenScreenStack[] screenStacks = FindInScene<TitleScreenScreenStack>(roots);
        RequireExactlyOne(screenStacks, nameof(TitleScreenScreenStack), result);
        MainMenuPresentationController[] presentations = FindInScene<MainMenuPresentationController>(roots);
        MenuScreenPunch[] screenPunches = FindInScene<MenuScreenPunch>(roots);
        ScrapMenuBackgroundController[] backgrounds = FindInScene<ScrapMenuBackgroundController>(roots);
        MenuAudioFeedback[] audioFeedback = FindInScene<MenuAudioFeedback>(roots);
        RequireExactlyOne(presentations, nameof(MainMenuPresentationController), result);
        RequireExactlyOne(screenPunches, nameof(MenuScreenPunch), result);
        RequireExactlyOne(backgrounds, nameof(ScrapMenuBackgroundController), result);
        RequireExactlyOne(audioFeedback, nameof(MenuAudioFeedback), result);
        ObjectivesMenuUI[] objectivesScreens = FindInScene<ObjectivesMenuUI>(roots);
        SettingsScreenUI[] settingsScreens = FindInScene<SettingsScreenUI>(roots);
        RequireExactlyOne(objectivesScreens, nameof(ObjectivesMenuUI), result);
        RequireExactlyOne(settingsScreens, nameof(SettingsScreenUI), result);
        if (screenStacks.Length == 1 && !screenStacks[0].HasValidBindings)
            result.Error("TitleScreenScreenStack has missing authored local-screen bindings.");
        if (objectivesScreens.Length == 1)
        {
            if (!objectivesScreens[0].HasRequiredReferences)
                result.Error("ObjectivesMenuUI has missing authored shell, scroll viewport, detail, or row/card prefab references.");
            RequireSerializedReference(objectivesScreens[0], "_objectivesScrollRect", result);
            RequireSerializedReference(objectivesScreens[0], "_unlocksScrollRect", result);
        }
        if (settingsScreens.Length == 1 && !settingsScreens[0].HasRequiredReferences)
            result.Error("SettingsScreenUI has missing authored category or setting-row references.");
        if (settingsAppliers.Length == 1)
            RequireSerializedReference(settingsAppliers[0], "_settingsService", result);
        if (screenStacks.Length == 1)
        {
            RequireSerializedReference(screenStacks[0], "_profile", result);
            RequireSerializedReference(screenStacks[0], "_objectivesPresenter", result);
            RequireSerializedReference(screenStacks[0], "_cancelAction", result);
            InputSystemUIInputModule inputModule = eventSystems.Length == 1
                ? eventSystems[0].GetComponent<InputSystemUIInputModule>()
                : null;
            if (inputModule == null)
            {
                result.Error("The authored EventSystem is missing InputSystemUIInputModule.");
            }
            else
            {
                SerializedProperty cancelAction = new SerializedObject(screenStacks[0]).FindProperty("_cancelAction");
                if (cancelAction == null || cancelAction.objectReferenceValue != inputModule.cancel)
                    result.Error("TitleScreenScreenStack._cancelAction must share the EventSystem UI/Cancel action.");
            }
        }
        if (presentations.Length == 1)
        {
            RequireSerializedReference(presentations[0], "_profile", result);
            RequireSerializedReference(presentations[0], "_titleRoot", result);
            RequireSerializedReference(presentations[0], "_mainMenuRoot", result);
        }
        if (screenPunches.Length == 1)
            RequireSerializedReference(screenPunches[0], "_profile", result);
        if (backgrounds.Length == 1)
        {
            RequireSerializedReference(backgrounds[0], "_proceduralBackground", result);
            RequireSerializedReference(backgrounds[0], "_proceduralBackgroundMaterial", result);
            ValidateBackgroundMaterial(backgrounds[0], result);
        }
        if (audioFeedback.Length == 1)
        {
            RequireSerializedReference(audioFeedback[0], "_source", result);
            SerializedProperty navigationClips = new SerializedObject(audioFeedback[0]).FindProperty("_navigationClips");
            if (navigationClips == null || navigationClips.arraySize == 0)
                result.Error("MenuAudioFeedback has no authored navigation clips.");
        }

        Canvas[] canvases = FindInScene<Canvas>(roots);
        if (!canvases.Any(canvas => canvas.sortingOrder == 1000))
            result.Error("MainMenuCanvas with sorting order 1000 is missing.");
        if (!canvases.Any(canvas => canvas.sortingOrder == 1500))
            result.Error("FeedbackCanvas with sorting order 1500 is missing.");
        if (!canvases.Any(canvas => canvas.sortingOrder >= 5000))
            result.Error("Persistent transition Canvas with sorting order at least 5000 is missing.");

        RequireNamedObject(roots, "MainMenuScreen", result);
        RequireNamedObject(roots, "ObjectivesScreen", result);
        RequireNamedObject(roots, "SettingsScreen", result);
        RequireNamedObject(roots, "QuitConfirmation", result);
        RequireNamedObject(roots, "SafeArea", result);
        RequireNamedObject(roots, "TitleRoot", result);
        RequireNamedObject(roots, "MenuRoot", result);
        RequireNamedObject(roots, "ProceduralScrapyardBackground", result);
        RequireNamedObject(roots, "FeedbackCanvas", result);
        RequireNamedObject(roots, "PersistentSystemsRoot", result);

        Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        ValidateMainMenuComposition(transforms, backgrounds, result);
        if (transforms.Any(transform => transform.name.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        transform.name.IndexOf("BuildLabel", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            result.Error("A player-facing Version/BuildLabel object exists in the production title scene.");
        }

        MainMenuItemView[] items = FindInScene<MainMenuItemView>(roots);
        if (items.Length < 6)
            result.Error("Expected four production and two developer MainMenuItemView instances.");
        for (int i = 0; i < items.Length; i++)
        {
            if (!items[i].HasRequiredReferences)
                result.Error($"MainMenuItemView '{items[i].name}' has missing authored references.");
            if (items[i].VisualRoot == null || items[i].VisualRoot.parent != items[i].transform)
                result.Error($"MainMenuItemView '{items[i].name}' must animate a direct VisualRoot child beneath a stable slot.");
        }

        if (FindInScene<VerticalLayoutGroup>(roots).Any(group => IsUnderNamedParent(group.transform, "MenuRoot")))
            result.Error("MenuRoot uses VerticalLayoutGroup; production slot positions must be authored independently.");

        if (controllers.Length == 1)
        {
            ValidateControllerReferences(controllers[0], result);
            ValidateProductionOrder(controllers[0], items, result);
            if (eventSystems.Length == 1)
                ValidateEventSystem(eventSystems[0], controllers[0], result);
        }

        if (typeof(TitleScreenController).GetMethod(
                "BuildUiIfNeeded",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public) != null)
        {
            result.Error("TitleScreenController still contains the forbidden BuildUiIfNeeded runtime path.");
        }

        ValidateBuildSettings(result);
    }

    private static void ValidateMainMenuComposition(
        Transform[] transforms,
        ScrapMenuBackgroundController[] backgrounds,
        Result result)
    {
        string[] forbiddenNames =
        {
            "ShowcaseRoot",
            "ShowcaseSubject",
            "ShowcaseLabel",
            "TitleStencil"
        };
        for (int index = 0; index < forbiddenNames.Length; index++)
        {
            if (transforms.Any(transform => transform.name == forbiddenNames[index]))
                result.Error($"Legacy main-menu presentation object '{forbiddenNames[index]}' must be removed.");
        }

        RectTransform mainMenuScreen = FindNamedRectTransform(transforms, "MainMenuScreen");
        if (mainMenuScreen == null)
        {
            result.Error("MainMenuScreen must be authored as a RectTransform.");
        }
        else
        {
            if (mainMenuScreen.GetComponentsInChildren<Transform>(true)
                .Any(transform => transform.name == "InputHints"))
            {
                result.Error("MainMenuScreen must not contain InputHints; local-screen hints may remain on their own screens.");
            }

            if (mainMenuScreen.GetComponentsInChildren<TMP_Text>(true)
                .Any(text => string.Equals(text.text?.Trim(), "CREDITS", StringComparison.OrdinalIgnoreCase)))
            {
                result.Error("MainMenuScreen still contains player-facing CREDITS text; the production destination is OBJECTIVES.");
            }
        }

        RectTransform safeArea = FindNamedRectTransform(transforms, "SafeArea");
        RectTransform titleRoot = FindNamedRectTransform(transforms, "TitleRoot");
        RectTransform menuRoot = FindNamedRectTransform(transforms, "MenuRoot");
        if (safeArea == null || titleRoot == null || menuRoot == null)
        {
            result.Error("SafeArea, TitleRoot, and MenuRoot must all be authored as RectTransforms.");
        }
        else
        {
            Canvas.ForceUpdateCanvases();
            float safeCenterX = safeArea.rect.center.x;
            float titleCenterX = CenterXRelativeTo(safeArea, titleRoot);
            float menuCenterX = CenterXRelativeTo(safeArea, menuRoot);
            if (titleCenterX >= safeCenterX)
                result.Error("TitleRoot must occupy the left side of SafeArea.");
            if (menuCenterX <= safeCenterX)
                result.Error("MenuRoot must occupy the right side of SafeArea.");
        }

        if (backgrounds.Length != 1)
            return;

        SerializedProperty graphicProperty = new SerializedObject(backgrounds[0])
            .FindProperty("_proceduralBackground");
        Graphic graphic = graphicProperty?.objectReferenceValue as Graphic;
        if (graphic == null)
            return;

        RectTransform rect = graphic.rectTransform;
        const float tolerance = 0.01f;
        if (Vector2.Distance(rect.anchorMin, Vector2.zero) > tolerance ||
            Vector2.Distance(rect.anchorMax, Vector2.one) > tolerance ||
            rect.offsetMin.sqrMagnitude > tolerance * tolerance ||
            rect.offsetMax.sqrMagnitude > tolerance * tolerance)
        {
            result.Error("The authored procedural background must stretch edge-to-edge with zero offsets.");
        }
    }

    private static void ValidateBackgroundMaterial(ScrapMenuBackgroundController background, Result result)
    {
        SerializedProperty materialProperty = new SerializedObject(background)
            .FindProperty("_proceduralBackgroundMaterial");
        Material material = materialProperty?.objectReferenceValue as Material;
        if (material == null)
            return;

        Shader shader = material.shader;
        if (shader == null)
        {
            result.Error("The authored procedural background material has no shader.");
            return;
        }

        if (!shader.isSupported || ShaderUtil.ShaderHasError(shader))
            result.Error($"The authored procedural background shader '{shader.name}' is unsupported or has compiler errors.");
        if (!material.HasProperty("_MainTex"))
            result.Error("The authored procedural background material is missing the UI _MainTex property.");
    }

    private static void ValidateControllerReferences(TitleScreenController controller, Result result)
    {
        SerializedObject serialized = new(controller);
        string[] requiredFields =
        {
            "_playButton", "_objectivesButton", "_settingsButton", "_quitButton", "_quitConfirmButton",
            "_developerRoot", "_weaponSandboxButton", "_enemiesTestingButton", "_eventSystem", "_screenStack",
            "_presentation", "_objectivesScreen", "_settingsScreen", "_sceneTransition", "_settingsService",
            "_playItem", "_objectivesItem", "_settingsItem", "_quitItem", "_weaponSandboxItem", "_enemiesTestingItem"
        };
        for (int i = 0; i < requiredFields.Length; i++)
        {
            SerializedProperty property = serialized.FindProperty(requiredFields[i]);
            if (property == null || property.objectReferenceValue == null)
                result.Error($"TitleScreenController.{requiredFields[i]} is not assigned.");
        }
    }

    private static void ValidateProductionOrder(
        TitleScreenController controller,
        MainMenuItemView[] allItems,
        Result result)
    {
        string[] expected = { "PLAY", "SETTINGS", "OBJECTIVES", "QUIT" };
        Button[] controllerButtons =
        {
            controller.PlayButton,
            controller.SettingsButton,
            controller.ObjectivesButton,
            controller.QuitButton
        };
        string[] labels =
        {
            ReadButtonLabel(controller.PlayButton),
            ReadButtonLabel(controller.SettingsButton),
            ReadButtonLabel(controller.ObjectivesButton),
            ReadButtonLabel(controller.QuitButton)
        };
        if (!labels.SequenceEqual(expected))
            result.Error($"Production menu controller labels are '{string.Join(", ", labels)}'; expected PLAY, SETTINGS, OBJECTIVES, QUIT.");

        MainMenuItemView[] productionItems = allItems
            .Where(item => item != null && !item.IsDeveloperEntry)
            .OrderBy(item => item.transform.GetSiblingIndex())
            .ToArray();
        if (productionItems.Length != expected.Length)
        {
            result.Error($"Expected exactly four non-developer production items; found {productionItems.Length}.");
        }
        else
        {
            string[] authoredLabels = productionItems
                .Select(item => ReadButtonLabel(item.Button))
                .ToArray();
            if (!authoredLabels.SequenceEqual(expected))
                result.Error($"Authored production item order/labels are '{string.Join(", ", authoredLabels)}'; expected PLAY, SETTINGS, OBJECTIVES, QUIT.");

            MainMenuItemView[] controllerItems = controllerButtons
                .Select(button => button != null ? button.GetComponent<MainMenuItemView>() : null)
                .ToArray();
            if (!controllerItems.SequenceEqual(productionItems))
                result.Error("The four production controller references must match the authored MenuRoot item order.");
        }

        if (productionItems.Any(item => !IsUnderNamedParent(item.transform, "MenuRoot")))
            result.Error("Every non-developer production item must be authored beneath MenuRoot.");

        if (allItems.Any(item => item != null &&
                                string.Equals(
                                    ReadButtonLabel(item.Button),
                                    "CREDITS",
                                    StringComparison.OrdinalIgnoreCase)))
        {
            result.Error("A CREDITS MainMenuItemView remains in the authored title scene.");
        }
    }

    private static void ValidateEventSystem(
        EventSystem eventSystem,
        TitleScreenController controller,
        Result result)
    {
        if (eventSystem.firstSelectedGameObject != controller.PlayButton?.gameObject)
            result.Error("EventSystem.firstSelectedGameObject must reference the authored Play button.");

        BaseInputModule inputModule = eventSystem.GetComponent<BaseInputModule>();
        if (inputModule == null)
        {
            result.Error("EventSystem is missing an authored input module.");
            return;
        }

        SerializedProperty deselectOnBackgroundClick = new SerializedObject(inputModule)
            .FindProperty("m_DeselectOnBackgroundClick");
        if (deselectOnBackgroundClick != null && deselectOnBackgroundClick.boolValue)
            result.Error("Input module must keep selection when decorative background is clicked.");
    }

    private static void ValidateBuildSettings(Result result)
    {
        EditorBuildSettingsScene[] enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
        if (enabled.Length == 0 || enabled[0].path != ScenePath)
            result.Error("TitleScreen is not the first enabled Build Settings scene.");
        if (!enabled.Any(scene => scene.path == "Assets/Scenes/GameplayScene.unity"))
            result.Error("GameplayScene is not enabled in Build Settings.");
    }

    private static string ReadButtonLabel(Button button)
    {
        if (button == null)
            return "<missing>";
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null ? label.text?.Trim() ?? string.Empty : "<missing label>";
    }

    private static T[] FindInScene<T>(IEnumerable<GameObject> roots) where T : Component
    {
        return roots.SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }

    private static void RequireExactlyOne<T>(T[] values, string label, Result result)
    {
        if (values.Length != 1)
            result.Error($"Expected exactly one {label}; found {values.Length}.");
    }

    private static void RequireNamedObject(IEnumerable<GameObject> roots, string name, Result result)
    {
        bool found = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Any(transform => transform.name == name);
        if (!found)
            result.Error($"Required authored object '{name}' is missing.");
    }

    private static RectTransform FindNamedRectTransform(IEnumerable<Transform> transforms, string name)
    {
        return transforms.FirstOrDefault(transform => transform.name == name) as RectTransform;
    }

    private static float CenterXRelativeTo(RectTransform reference, RectTransform target)
    {
        Vector3 worldCenter = target.TransformPoint(target.rect.center);
        return reference.InverseTransformPoint(worldCenter).x;
    }

    private static void RequireSerializedReference(Component component, string fieldName, Result result)
    {
        SerializedProperty property = new SerializedObject(component).FindProperty(fieldName);
        if (property == null || property.objectReferenceValue == null)
            result.Error($"{component.GetType().Name}.{fieldName} is not assigned.");
    }

    private static bool IsUnderNamedParent(Transform transform, string parentName)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == parentName)
                return true;
        }
        return false;
    }
}
