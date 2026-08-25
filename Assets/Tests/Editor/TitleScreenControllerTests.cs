using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScreenControllerTests
{
    private const string TitleScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string WeaponSandboxScenePath = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";
    private const string EnemiesTestingScenePath = "Assets/Scenes/Testing/enemiesTesting.unity";

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [Test]
    public void EnabledBuildSettingsOrder_MatchesProductionAndTestingDestinations()
    {
        string[] enabledScenePaths = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                TitleScenePath,
                "Assets/Scenes/GameplayScene.unity",
                WeaponSandboxScenePath,
                EnemiesTestingScenePath
            },
            enabledScenePaths);
    }

    [Test]
    public void TitleScreenScene_HasAuthoredProductionMenuInRequiredOrder()
    {
        Scene scene = OpenTitleScene();
        TitleScreenController controller = FindExactlyOneInScene<TitleScreenController>(scene);

        Button[] productionButtons =
        {
            controller.PlayButton,
            controller.SettingsButton,
            controller.ObjectivesButton,
            controller.QuitButton
        };

        Assert.That(productionButtons, Has.All.Not.Null, "Every production destination must be assigned in the scene.");
        CollectionAssert.AreEqual(
            new[] { "PLAY", "SETTINGS", "OBJECTIVES", "QUIT" },
            productionButtons.Select(ReadButtonLabel).ToArray());

        MainMenuItemView[] productionItems = FindAllInScene<MainMenuItemView>(scene)
            .Where(item => !item.IsDeveloperEntry)
            .OrderBy(item => item.transform.GetSiblingIndex())
            .ToArray();
        Assert.That(productionItems, Has.Length.EqualTo(4),
            "The production menu must not retain an unreferenced destination such as Credits.");
        Assert.That(productionItems, Has.All.Not.Null);
        Assert.That(productionItems, Has.All.Matches<MainMenuItemView>(item => item.HasRequiredReferences));
        Assert.That(productionItems, Has.All.Matches<MainMenuItemView>(item => !item.IsDeveloperEntry));
        Assert.That(productionItems, Has.All.Matches<MainMenuItemView>(item => HasAncestorNamed(item.transform, "MenuRoot")));
        Assert.That(productionItems.Select(item => item.transform.GetSiblingIndex()), Is.Ordered);
        CollectionAssert.AreEqual(
            productionButtons.Select(button => button.GetComponent<MainMenuItemView>()).ToArray(),
            productionItems,
            "Controller references must follow the same order as the four authored production slots.");
        Assert.That(
            FindAllInScene<TMP_Text>(scene).Any(text =>
                string.Equals(text.text?.Trim(), "CREDITS", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            "Credits must be replaced by Objectives rather than left as hidden or decorative text.");

        VerticalLayoutGroup[] automaticLayouts = FindAllInScene<VerticalLayoutGroup>(scene)
            .Where(layout => HasAncestorNamed(layout.transform, "MenuRoot"))
            .ToArray();
        Assert.That(automaticLayouts, Is.Empty, "The angled menu uses individually authored slots, not an automatic vertical layout.");
    }

    [Test]
    public void TitleScreenScene_UsesBackgroundOnlyWireframeComposition()
    {
        Scene scene = OpenTitleScene();
        Transform[] transforms = FindAllInScene<Transform>(scene);
        string[] forbiddenNames =
        {
            "ShowcaseRoot",
            "ShowcaseSubject",
            "ShowcaseLabel",
            "TitleStencil"
        };

        for (int index = 0; index < forbiddenNames.Length; index++)
        {
            Assert.That(
                transforms.Any(transform => transform.name == forbiddenNames[index]),
                Is.False,
                $"Legacy presentation object '{forbiddenNames[index]}' should not remain in the background-only main menu.");
        }

        RectTransform mainMenuScreen = FindNamedRectTransform(scene, "MainMenuScreen");
        Assert.That(
            mainMenuScreen.GetComponentsInChildren<Transform>(true)
                .Any(transform => transform.name == "InputHints"),
            Is.False,
            "Navigation tips are forbidden only on the main screen; local Objectives/Settings hints may remain.");

        ScrapMenuBackgroundController background = FindExactlyOneInScene<ScrapMenuBackgroundController>(scene);
        Graphic proceduralGraphic = ReadObjectReference<Graphic>(background, "_proceduralBackground");
        Material proceduralMaterial = ReadObjectReference<Material>(background, "_proceduralBackgroundMaterial");
        RectTransform backgroundRect = proceduralGraphic.rectTransform;
        const float tolerance = 0.01f;

        Assert.That(Vector2.Distance(backgroundRect.anchorMin, Vector2.zero), Is.LessThan(tolerance));
        Assert.That(Vector2.Distance(backgroundRect.anchorMax, Vector2.one), Is.LessThan(tolerance));
        Assert.That(backgroundRect.offsetMin.sqrMagnitude, Is.LessThan(tolerance * tolerance));
        Assert.That(backgroundRect.offsetMax.sqrMagnitude, Is.LessThan(tolerance * tolerance));
        Assert.That(proceduralMaterial.shader, Is.Not.Null);
        Assert.That(proceduralMaterial.shader.isSupported, Is.True);
        Assert.That(ShaderUtil.ShaderHasError(proceduralMaterial.shader), Is.False);
        Assert.That(proceduralMaterial.HasProperty("_MainTex"), Is.True);
    }

    [Test]
    public void TitleScreenScene_PlacesTitleLeftAndMenuRightWithinSafeArea()
    {
        Scene scene = OpenTitleScene();
        RectTransform safeArea = FindNamedRectTransform(scene, "SafeArea");
        RectTransform titleRoot = FindNamedRectTransform(scene, "TitleRoot");
        RectTransform menuRoot = FindNamedRectTransform(scene, "MenuRoot");

        Canvas.ForceUpdateCanvases();
        float safeCenterX = safeArea.rect.center.x;
        Assert.That(CenterXRelativeTo(safeArea, titleRoot), Is.LessThan(safeCenterX),
            "The title treatment should occupy the left side of the wireframe.");
        Assert.That(CenterXRelativeTo(safeArea, menuRoot), Is.GreaterThan(safeCenterX),
            "The production action stack should occupy the right side of the wireframe.");
    }

    [Test]
    public void TitleScreenScene_KeepsDeveloperDestinationsSeparateAndDisabledByDefault()
    {
        Scene scene = OpenTitleScene();
        TitleScreenController controller = FindExactlyOneInScene<TitleScreenController>(scene);
        GameObject developerRoot = ReadObjectReference<GameObject>(controller, "_developerRoot");
        Button weaponSandbox = ReadObjectReference<Button>(controller, "_weaponSandboxButton");
        Button enemiesTesting = ReadObjectReference<Button>(controller, "_enemiesTestingButton");

        Assert.That(developerRoot, Is.Not.Null);
        Assert.That(weaponSandbox, Is.Not.Null);
        Assert.That(enemiesTesting, Is.Not.Null);
        Assert.That(controller.IncludeTestingButtons, Is.False);
        Assert.That(developerRoot.activeSelf, Is.False, "Developer destinations must be opt-in for normal builds.");
        Assert.That(weaponSandbox.transform.IsChildOf(developerRoot.transform), Is.True);
        Assert.That(enemiesTesting.transform.IsChildOf(developerRoot.transform), Is.True);
        Assert.That(controller.PlayButton.transform.IsChildOf(developerRoot.transform), Is.False);
        Assert.That(controller.ObjectivesButton.transform.IsChildOf(developerRoot.transform), Is.False);
        Assert.That(controller.SettingsButton.transform.IsChildOf(developerRoot.transform), Is.False);
        Assert.That(controller.QuitButton.transform.IsChildOf(developerRoot.transform), Is.False);
        Assert.That(weaponSandbox.GetComponent<MainMenuItemView>().IsDeveloperEntry, Is.True);
        Assert.That(enemiesTesting.GetComponent<MainMenuItemView>().IsDeveloperEntry, Is.True);
    }

    [Test]
    public void TitleScreenScene_HasAuthoredLocalScreensAndNoVersionLabel()
    {
        Scene scene = OpenTitleScene();
        TitleScreenScreenStack stack = FindExactlyOneInScene<TitleScreenScreenStack>(scene);

        Assert.That(stack.HasValidBindings, Is.True, "Objectives, Settings, and Quit must be authored as local screen bindings.");
        Assert.That(FindAllInScene<ObjectivesMenuUI>(scene), Has.Length.EqualTo(1));
        Assert.That(FindAllInScene<SettingsScreenUI>(scene), Has.Length.EqualTo(1));
        AssertNamedObjectExists(scene, "MainMenuScreen");
        AssertNamedObjectExists(scene, "ObjectivesScreen");
        AssertNamedObjectExists(scene, "SettingsScreen");
        AssertNamedObjectExists(scene, "QuitConfirmation");

        Transform[] transforms = FindAllInScene<Transform>(scene);
        Assert.That(
            transforms.Where(transform =>
                transform.name.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transform.name.IndexOf("BuildLabel", StringComparison.OrdinalIgnoreCase) >= 0),
            Is.Empty,
            "The production title screen must not expose a version/build label.");
    }

    [Test]
    public void TitleScreenScene_HasOneAuthoredEventSystemAndPersistentPresentationServices()
    {
        Scene scene = OpenTitleScene();

        EventSystem eventSystem = FindExactlyOneInScene<EventSystem>(scene);
        TitleScreenController controller = FindExactlyOneInScene<TitleScreenController>(scene);
        Assert.That(eventSystem.firstSelectedGameObject, Is.SameAs(controller.PlayButton.gameObject),
            "Keyboard/gamepad navigation needs an authored recovery selection.");
        BaseInputModule inputModule = eventSystem.GetComponent<BaseInputModule>();
        Assert.That(inputModule, Is.Not.Null);
        SerializedProperty deselectOnBackgroundClick = new SerializedObject(inputModule)
            .FindProperty("m_DeselectOnBackgroundClick");
        Assert.That(deselectOnBackgroundClick, Is.Not.Null);
        Assert.That(deselectOnBackgroundClick.boolValue, Is.False,
            "Clicking decorative background must not strand controller focus.");
        UserSettingsService settings = FindExactlyOneInScene<UserSettingsService>(scene);
        UserSettingsApplier applier = FindExactlyOneInScene<UserSettingsApplier>(scene);
        ScrapSceneTransition transition = FindExactlyOneInScene<ScrapSceneTransition>(scene);
        Transform persistentRoot = FindAllInScene<Transform>(scene)
            .Single(transform => transform.name == "PersistentSystemsRoot");

        Assert.That(settings.transform == persistentRoot || settings.transform.IsChildOf(persistentRoot), Is.True);
        Assert.That(applier.transform == persistentRoot || applier.transform.IsChildOf(persistentRoot), Is.True);
        Assert.That(transition.transform == persistentRoot || transition.transform.IsChildOf(persistentRoot), Is.True);
        Assert.That(
            transition.GetComponentsInChildren<Canvas>(true).Any(canvas => canvas.sortingOrder >= 5000),
            Is.True,
            "The authored scene transition must render above the menu and local screens.");
    }

    [Test]
    public void TitleScreenController_AwakeDoesNotConstructFixedHierarchyOrEventSystem()
    {
        Scene scene = OpenTitleScene();
        TitleScreenController controller = FindExactlyOneInScene<TitleScreenController>(scene);
        int[] componentIdsBefore = FindAllInScene<Component>(scene)
            .Where(component => component != null)
            .Select(component => component.GetInstanceID())
            .OrderBy(id => id)
            .ToArray();
        int eventSystemCountBefore = FindAllInScene<EventSystem>(scene).Length;
        MethodInfo awake = typeof(TitleScreenController).GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(awake, Is.Not.Null);
        Assert.That(
            typeof(TitleScreenController).GetMethod("BuildUiIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            Is.Null,
            "Fixed title UI may not be constructed at runtime.");

        awake.Invoke(controller, null);

        int[] componentIdsAfter = FindAllInScene<Component>(scene)
            .Where(component => component != null)
            .Select(component => component.GetInstanceID())
            .OrderBy(id => id)
            .ToArray();
        CollectionAssert.AreEqual(componentIdsBefore, componentIdsAfter);
        Assert.That(FindAllInScene<EventSystem>(scene), Has.Length.EqualTo(eventSystemCountBefore));
    }

    [Test]
    public void TitleScreenController_AwakeUnlocksAndShowsCursor()
    {
        Scene scene = OpenTitleScene();
        TitleScreenController controller = FindExactlyOneInScene<TitleScreenController>(scene);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        InvokeAwake(controller);

        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(Cursor.visible, Is.True);
    }

    [Test]
    public void TitleScreenScene_PassesAuthoringValidator()
    {
        Scene scene = OpenTitleScene();

        TitleScreenAuthoringValidator.Result result = TitleScreenAuthoringValidator.ValidateScene(scene);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
    }

    [Test]
    public void WeaponSandboxScene_HasPauseMenuUiForEscapeMenu()
    {
        AssertSceneHasPauseMenu(WeaponSandboxScenePath, "Weapon sandbox");
    }

    [Test]
    public void EnemiesTestingScene_HasPauseMenuUiForEscapeMenu()
    {
        AssertSceneHasPauseMenu(EnemiesTestingScenePath, "Enemies testing");
    }

    private static Scene OpenTitleScene()
    {
        Assert.That(File.Exists(TitleScenePath), Is.True, $"Expected scene at '{TitleScenePath}'.");
        return EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
    }

    private static void AssertSceneHasPauseMenu(string scenePath, string sceneLabel)
    {
        Assert.That(File.Exists(scenePath), Is.True, $"Expected scene at '{scenePath}'.");
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        PauseMenuUI[] pauseMenus = FindAllInScene<PauseMenuUI>(scene);
        EventSystem[] eventSystems = FindAllInScene<EventSystem>(scene);

        Assert.That(pauseMenus, Has.Length.EqualTo(1), $"{sceneLabel} scene should include PauseMenuUI so Escape can open the pause menu.");
        Assert.That(pauseMenus[0].GetComponentInParent<Canvas>(true), Is.Not.Null, $"{sceneLabel} PauseMenuUI should live under a Canvas.");
        Assert.That(eventSystems, Has.Length.GreaterThanOrEqualTo(1), $"{sceneLabel} scene should include an EventSystem for pause menu buttons.");
    }

    private static void InvokeAwake(TitleScreenController controller)
    {
        MethodInfo awake = typeof(TitleScreenController).GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(controller, null);
    }

    private static string ReadButtonLabel(Button button)
    {
        Assert.That(button, Is.Not.Null);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        Assert.That(label, Is.Not.Null, $"Button '{button.name}' is missing its authored TMP label.");
        return label.text.Trim();
    }

    private static T ReadObjectReference<T>(UnityEngine.Object target, string propertyName) where T : UnityEngine.Object
    {
        SerializedObject serializedObject = new(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        Assert.That(property, Is.Not.Null, $"Serialized field '{propertyName}' was not found on {target.GetType().Name}.");
        Assert.That(property.objectReferenceValue, Is.AssignableTo<T>(), $"Serialized field '{propertyName}' is not assigned.");
        return (T)property.objectReferenceValue;
    }

    private static T FindExactlyOneInScene<T>(Scene scene) where T : Component
    {
        T[] values = FindAllInScene<T>(scene);
        Assert.That(values, Has.Length.EqualTo(1), $"Expected exactly one {typeof(T).Name} in '{scene.path}'.");
        return values[0];
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    private static void AssertNamedObjectExists(Scene scene, string objectName)
    {
        Assert.That(
            FindAllInScene<Transform>(scene).Any(transform => transform.name == objectName),
            Is.True,
            $"Expected authored object '{objectName}' in the title scene.");
    }

    private static RectTransform FindNamedRectTransform(Scene scene, string objectName)
    {
        RectTransform[] matches = FindAllInScene<RectTransform>(scene)
            .Where(transform => transform.name == objectName)
            .ToArray();
        Assert.That(matches, Has.Length.EqualTo(1),
            $"Expected exactly one authored RectTransform named '{objectName}'.");
        return matches[0];
    }

    private static float CenterXRelativeTo(RectTransform reference, RectTransform target)
    {
        Vector3 worldCenter = target.TransformPoint(target.rect.center);
        return reference.InverseTransformPoint(worldCenter).x;
    }

    private static bool HasAncestorNamed(Transform transform, string name)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == name)
                return true;
        }
        return false;
    }
}
