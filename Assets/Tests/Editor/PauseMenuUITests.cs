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
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
    }

    [Test]
    public void Awake_CreatesResumeAndReturnToTitleButtons()
    {
        GameObject root = new("PauseMenuRoot");

        CreatePauseMenu(root);

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        string[] labels = buttons.Select(GetButtonLabel).ToArray();

        CollectionAssert.AreEqual(new[] { "Continuar", "Volver al titulo" }, labels);
    }

    [Test]
    public void Awake_WiresPauseButtonsToExpectedHandlers()
    {
        GameObject root = new("PauseMenuRoot");

        CreatePauseMenu(root);

        AssertButtonInvokes(root, "Continuar", "Resume");
        AssertButtonInvokes(root, "Volver al titulo", "ReturnToTitle");
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

    private static string GetButtonLabel(Button button)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Assert.That(label, Is.Not.Null);
        return label.text;
    }
}
