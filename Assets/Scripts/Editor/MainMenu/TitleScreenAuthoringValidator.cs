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
        RequireNamedObject(roots, "ShowcaseRoot", result);
        RequireNamedObject(roots, "FeedbackCanvas", result);
        RequireNamedObject(roots, "PersistentSystemsRoot", result);

        Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
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
            ValidateProductionOrder(controllers[0], result);
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

    private static void ValidateProductionOrder(TitleScreenController controller, Result result)
    {
        string[] labels =
        {
            ReadButtonLabel(controller.PlayButton),
            ReadButtonLabel(controller.ObjectivesButton),
            ReadButtonLabel(controller.SettingsButton),
            ReadButtonLabel(controller.QuitButton)
        };
        string[] expected = { "PLAY", "OBJECTIVES", "SETTINGS", "QUIT" };
        if (!labels.SequenceEqual(expected))
            result.Error($"Production menu order/labels are '{string.Join(", ", labels)}'; expected PLAY, OBJECTIVES, SETTINGS, QUIT.");
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
        return label != null ? label.text : "<missing label>";
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
