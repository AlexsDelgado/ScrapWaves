using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class PauseMenuUITests
{
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Time.timeScale = 1f;
        PlayerPrefs.DeleteKey(UserSettingsService.PlayerPrefsKey);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        PlayerPrefs.DeleteKey(UserSettingsService.PlayerPrefsKey);
        PlayerPrefs.Save();
    }

    [Test]
    public void Awake_CreatesResumeAndReturnToTitleButtons()
    {
        GameObject root = new("PauseMenuRoot");

        CreatePauseMenu(root);

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        string[] labels = buttons.Select(GetButtonLabel).ToArray();

        CollectionAssert.AreEqual(new[] { "Resume", "Main Menu" }, labels);
    }

    [Test]
    public void Awake_WiresPauseButtonsToExpectedHandlers()
    {
        GameObject root = new("PauseMenuRoot");

        CreatePauseMenu(root);

        AssertButtonInvokes(root, "Resume", "Resume");
        AssertButtonInvokes(root, "Main Menu", "ReturnToTitle");
    }

    [Test]
    public void Awake_CreatesTopInteractiveCanvasAboveDebugUi()
    {
        GameObject root = new("PauseMenuRoot");
        root.AddComponent<Canvas>();

        CreatePauseMenu(root);

        Transform pauseRoot = root.transform.Find("PauseRoot");
        Assert.That(pauseRoot, Is.Not.Null);

        Canvas canvas = pauseRoot.GetComponent<Canvas>();
        Assert.That(canvas, Is.Not.Null, "PauseRoot should have its own Canvas so sandbox debug UI cannot render above it.");
        Assert.That(canvas.overrideSorting, Is.True);
        Assert.That(canvas.sortingOrder, Is.GreaterThan(30000));
        Assert.That(pauseRoot.GetComponent<GraphicRaycaster>(), Is.Not.Null);
    }

    [Test]
    public void ShowPause_UnlocksAndShowsCursorWithoutCamera()
    {
        GameObject root = new("PauseMenuRoot");
        Component pauseMenu = CreatePauseMenu(root);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        InvokePrivate(pauseMenu, "ShowPause");

        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(Cursor.visible, Is.True);
    }

    [Test]
    public void ShowPause_ForcesSandboxDebugUiBackToUnlockedMouseMode()
    {
        WeaponSandboxDebugUI sandboxDebugUi = new GameObject("SandboxDebugUi").AddComponent<WeaponSandboxDebugUI>();
        SetPrivateField(sandboxDebugUi, "_uiMouseMode", false);
        SetPrivateField(sandboxDebugUi, "_autoCursorMode", false);
        SetPrivateField(sandboxDebugUi, "_temporaryCameraAim", true);

        Component pauseMenu = CreatePauseMenu(new GameObject("PauseMenuRoot"));

        InvokePrivate(pauseMenu, "ShowPause");

        Assert.That(GetPrivateField<bool>(sandboxDebugUi, "_uiMouseMode"), Is.True);
        Assert.That(GetPrivateField<bool>(sandboxDebugUi, "_autoCursorMode"), Is.True);
        Assert.That(GetPrivateField<bool>(sandboxDebugUi, "_temporaryCameraAim"), Is.False);
    }

    [Test]
    public void CanPause_RejectsVisibleLevelUpUiCreatedAfterPauseMenuAwake()
    {
        Component pauseMenu = CreatePauseMenu(new GameObject("PauseMenuRoot"));
        LevelUpChoiceUI choiceUi = new GameObject("LateLevelUpChoiceUi").AddComponent<LevelUpChoiceUI>();
        SetPrivateField(choiceUi, "_isVisible", true);

        bool canPause = InvokePrivate<bool>(pauseMenu, "CanPause");

        Assert.That(canPause, Is.False);
        Assert.That(GetPrivateField<LevelUpChoiceUI>(pauseMenu, "_levelUpChoiceUi"), Is.SameAs(choiceUi));
    }

    [Test]
    public void CanPause_RejectsVisibleCraftingUi()
    {
        CraftingUI craftingUi = new GameObject("CraftingUi").AddComponent<CraftingUI>();
        SetPrivateField(craftingUi, "_isVisible", true);
        Component pauseMenu = CreatePauseMenu(new GameObject("PauseMenuRoot"));
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        bool canPause = InvokePrivate<bool>(pauseMenu, "CanPause");

        Assert.That(canPause, Is.False);
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(Cursor.visible, Is.True);
    }

    [Test]
    public void SettingsControlsReadWriteAndSynchronizeThroughSharedServiceWithoutLiveTargets()
    {
        UserSettingsService settings = CreateSettingsService();
        settings.HorizontalSensitivity = 0.26f;
        settings.VerticalSensitivity = 0.32f;
        settings.InvertY = true;
        settings.SfxVolume = 0.4f;
        settings.MusicVolume = 0.3f;

        Component first = CreatePauseMenu(new GameObject("FirstPauseMenu"));
        Component second = CreatePauseMenu(new GameObject("SecondPauseMenu"));

        Slider firstHorizontal = GetPrivateField<Slider>(first, "_hSensSlider");
        Slider secondHorizontal = GetPrivateField<Slider>(second, "_hSensSlider");
        Toggle secondInvertY = GetPrivateField<Toggle>(second, "_invertYToggle");
        Slider secondSfx = GetPrivateField<Slider>(second, "_sfxSlider");
        Slider secondMusic = GetPrivateField<Slider>(second, "_musicSlider");

        Assert.That(firstHorizontal.value, Is.EqualTo(0.26f));
        Assert.That(GetPrivateField<Slider>(first, "_vSensSlider").value, Is.EqualTo(0.32f));
        Assert.That(GetPrivateField<Toggle>(first, "_invertYToggle").isOn, Is.True);
        Assert.That(GetPrivateField<Slider>(first, "_sfxSlider").value, Is.EqualTo(0.4f));
        Assert.That(GetPrivateField<Slider>(first, "_musicSlider").value, Is.EqualTo(0.3f));

        firstHorizontal.value = 0.19f;
        Assert.That(settings.HorizontalSensitivity, Is.EqualTo(0.19f));
        Assert.That(secondHorizontal.value, Is.EqualTo(0.19f));

        settings.InvertY = false;
        settings.SfxVolume = 0.7f;
        settings.MusicVolume = 0.8f;
        Assert.That(secondInvertY.isOn, Is.False);
        Assert.That(secondSfx.value, Is.EqualTo(0.7f));
        Assert.That(secondMusic.value, Is.EqualTo(0.8f));
    }

    [Test]
    public void MissingSettingsServiceLeavesPauseSettingsDisabled()
    {
        Component pauseMenu = CreatePauseMenu(new GameObject("PauseMenuWithoutSettings"));

        Assert.That(GetPrivateField<Slider>(pauseMenu, "_hSensSlider").interactable, Is.False);
        Assert.That(GetPrivateField<Slider>(pauseMenu, "_vSensSlider").interactable, Is.False);
        Assert.That(GetPrivateField<Toggle>(pauseMenu, "_invertYToggle").interactable, Is.False);
        Assert.That(GetPrivateField<Slider>(pauseMenu, "_sfxSlider").interactable, Is.False);
        Assert.That(GetPrivateField<Slider>(pauseMenu, "_musicSlider").interactable, Is.False);
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
        Assert.That(callsField, Is.Not.Null);

        object calls = callsField.GetValue(unityEvent);
        Assert.That(calls, Is.Not.Null);

        FieldInfo runtimeCallsField = calls.GetType().GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(runtimeCallsField, Is.Not.Null);

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

    private static Component CreatePauseMenu(GameObject root)
    {
        PauseMenuUI pauseMenu = root.AddComponent<PauseMenuUI>();
        MethodInfo awake = typeof(PauseMenuUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(awake, Is.Not.Null);
        awake.Invoke(pauseMenu, null);
        return pauseMenu;
    }

    private static UserSettingsService CreateSettingsService()
    {
        UserSettingsService settings = new GameObject("UserSettingsService").AddComponent<UserSettingsService>();
        MethodInfo awake = typeof(UserSettingsService).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(awake, Is.Not.Null);
        awake.Invoke(settings, null);
        return settings;
    }

    private static void InvokePrivate(Component component, string methodName)
    {
        MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(component, null);
    }

    private static T InvokePrivate<T>(Component component, string methodName)
    {
        MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (T)method.Invoke(component, null);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(instance);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }

    private static string GetButtonLabel(Button button)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Assert.That(label, Is.Not.Null);
        return label.text;
    }
}
