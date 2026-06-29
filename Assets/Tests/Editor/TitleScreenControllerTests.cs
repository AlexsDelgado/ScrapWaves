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
                "Assets/Scenes/SampleScene.unity",
                "Assets/Scenes/WeaponTestingSandbox.unity",
                "Assets/Scenes/enemiesTesting.unity"
            },
            enabledScenePaths);
    }

    [Test]
    public void TitleScreenScene_IsMinimalBootSceneWithControllerRoot()
    {
        Assert.That(File.Exists(TitleScenePath), Is.True, $"Expected scene at '{TitleScenePath}'.");

        Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();
        Type controllerType = GetTitleScreenControllerType();

        Assert.That(controllerType, Is.Not.Null, "TitleScreenController type was not found.");
        Assert.That(roots, Has.Length.EqualTo(1));
        Assert.That(roots[0].GetComponent(controllerType), Is.Not.Null);
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
