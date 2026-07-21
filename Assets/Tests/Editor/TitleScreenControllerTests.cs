using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScreenControllerTests
{
    private const string TitleScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string WeaponSandboxScenePath = "Assets/Scenes/WeaponTestingSandbox.unity";
    private const string EnemiesTestingScenePath = "Assets/Scenes/enemiesTesting.unity";

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
    }

    [Test]
    public void Awake_CreatesExactlyThreeMenuButtonsWithExpectedLabels()
    {
        GameObject root = new("TitleScreenRoot");

        CreateController(root);

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        string[] labels = buttons.Select(GetButtonLabel).ToArray();

        Assert.That(buttons, Has.Length.EqualTo(3));
        CollectionAssert.AreEqual(
            new[] { "Play", "Weapon Sandbox", "Enemies Testing" },
            labels);
    }

    [Test]
    public void Awake_CreatesEventSystemWhenMissing()
    {
        Assert.That(UnityEngine.Object.FindAnyObjectByType<EventSystem>(), Is.Null);

        GameObject root = new("TitleScreenRoot");
        CreateController(root);

        EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.That(eventSystems, Has.Length.EqualTo(1));
    }

    [Test]
    public void Awake_UnlocksAndShowsCursorForMenu()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        GameObject root = new("TitleScreenRoot");
        CreateController(root);

        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(Cursor.visible, Is.True);
    }

    [Test]
    public void Awake_WiresButtonsToSharedSceneNavigationMethods()
    {
        GameObject root = new("TitleScreenRoot");
        CreateController(root);

        AssertButtonInvokes(root, "Play", nameof(SceneNavigation.LoadPlay));
        AssertButtonInvokes(root, "Weapon Sandbox", nameof(SceneNavigation.LoadWeaponSandbox));
        AssertButtonInvokes(root, "Enemies Testing", nameof(SceneNavigation.LoadEnemiesTesting));
    }

    [Test]
    public void EnabledBuildSettingsOrder_MatchesTitleScreenBootFlow()
    {
        string[] enabledScenePaths = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Assets/Scenes/TitleScreen.unity",
                "Assets/Scenes/GameplayScene.unity",
                "Assets/Scenes/WeaponTestingSandbox.unity",
                "Assets/Scenes/enemiesTesting.unity"
            },
            enabledScenePaths);
    }

    [Test]
    public void TitleScreenScene_HasEditableCanvasWithControllerAndButtons()
    {
        Assert.That(File.Exists(TitleScenePath), Is.True, $"Expected scene at '{TitleScenePath}'.");

        Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();
        Type controllerType = GetTitleScreenControllerType();

        Assert.That(controllerType, Is.Not.Null, "TitleScreenController type was not found.");
        Assert.That(roots, Has.Length.EqualTo(1));
        Assert.That(roots[0].GetComponent(controllerType), Is.Not.Null);

        Canvas canvas = roots[0].GetComponentInChildren<Canvas>(true);
        Camera titleCamera = roots[0].GetComponentInChildren<Camera>(true);
        EventSystem eventSystem = roots[0].GetComponentInChildren<EventSystem>(true);
        Button[] buttons = roots[0].GetComponentsInChildren<Button>(true);
        string[] labels = buttons.Select(GetButtonLabel).ToArray();

        Assert.That(canvas, Is.Not.Null, "TitleScreen scene should contain an editable Canvas.");
        Assert.That(titleCamera, Is.Not.Null, "TitleScreen scene should contain an editable Main Camera.");
        Assert.That(titleCamera.CompareTag("MainCamera"), Is.True, "TitleScreen camera should be tagged MainCamera.");
        Assert.That(eventSystem, Is.Not.Null, "TitleScreen scene should contain an editable EventSystem.");
        Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
        CollectionAssert.AreEqual(
            new[] { "Play", "Weapon Sandbox", "Enemies Testing", "Quit" },
            labels);

        Component controller = roots[0].GetComponent(controllerType);
        MethodInfo awake = controllerType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(controller, null);

        Button quitButton = buttons.Single(current => GetButtonLabel(current) == "Quit");
        CollectionAssert.Contains(
            GetRuntimeListenerMethodNames(quitButton.onClick).ToArray(),
            nameof(SceneNavigation.QuitApplication));
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

    private static void AssertSceneHasPauseMenu(string scenePath, string sceneLabel)
    {
        Assert.That(File.Exists(scenePath), Is.True, $"Expected scene at '{scenePath}'.");

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        PauseMenuUI[] pauseMenus = UnityEngine.Object.FindObjectsByType<PauseMenuUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);

        Assert.That(pauseMenus, Has.Length.EqualTo(1), $"{sceneLabel} scene should include PauseMenuUI so Escape can open the pause menu.");
        Assert.That(pauseMenus[0].GetComponentInParent<Canvas>(true), Is.Not.Null, $"{sceneLabel} PauseMenuUI should live under a Canvas.");
        Assert.That(eventSystem, Is.Not.Null, $"{sceneLabel} scene should include an EventSystem for pause menu buttons.");
    }

    private static void AssertButtonInvokes(GameObject root, string expectedLabel, string expectedMethodName)
    {
        Button button = root
            .GetComponentsInChildren<Button>(true)
            .Single(current => GetButtonLabel(current) == expectedLabel);

        string[] methodNames = GetRuntimeListenerMethodNames(button.onClick).ToArray();

        CollectionAssert.AreEqual(new[] { expectedMethodName }, methodNames);
    }

    private static IEnumerable<string> GetRuntimeListenerMethodNames(UnityEvent unityEvent)
    {
        FieldInfo callsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(callsField, Is.Not.Null, "Could not inspect UnityEvent runtime listeners.");

        object calls = callsField.GetValue(unityEvent);
        Assert.That(calls, Is.Not.Null, "UnityEvent call list was null.");

        FieldInfo runtimeCallsField = calls.GetType().GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(runtimeCallsField, Is.Not.Null, "Could not inspect UnityEvent runtime calls.");

        if (runtimeCallsField.GetValue(calls) is not IEnumerable runtimeCalls)
            yield break;

        foreach (object runtimeCall in runtimeCalls)
        {
            Delegate callback = GetDelegate(runtimeCall);
            if (callback != null)
                yield return callback.Method.Name;
        }
    }

    private static Delegate GetDelegate(object runtimeCall)
    {
        for (Type current = runtimeCall.GetType(); current != null; current = current.BaseType)
        {
            FieldInfo delegateField = current.GetField("Delegate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (delegateField == null)
                continue;

            return delegateField.GetValue(runtimeCall) as Delegate;
        }

        return null;
    }

    private static Component CreateController(GameObject root)
    {
        Type controllerType = GetTitleScreenControllerType();
        Assert.That(controllerType, Is.Not.Null, "TitleScreenController type was not found.");

        Component controller = root.AddComponent(controllerType);
        MethodInfo awake = controllerType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.That(awake, Is.Not.Null, "TitleScreenController.Awake was not found.");
        awake.Invoke(controller, null);
        return controller;
    }

    private static string GetButtonLabel(Button button)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Assert.That(label, Is.Not.Null, $"Button '{button.name}' is missing a TMP label.");
        return label.text;
    }

    private static Type GetTitleScreenControllerType()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(GetTypesSafely)
            .FirstOrDefault(type => type != null && type.Name == "TitleScreenController");
    }

    private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null);
        }
    }
}
