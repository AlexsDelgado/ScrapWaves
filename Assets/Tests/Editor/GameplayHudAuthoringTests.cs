using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class GameplayHudAuthoringTests
{
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameplayPause.Reset();
        Time.timeScale = 1f;
    }

    [TestCase(typeof(PauseMenuUI))]
    [TestCase(typeof(BossHealthBarHud))]
    [TestCase(typeof(OverheatObjectiveHud))]
    [TestCase(typeof(OffscreenObjectiveIndicators))]
    [TestCase(typeof(PlayerCombatFeedback))]
    [TestCase(typeof(RunEndScreenUI))]
    public void AuthoringAndAwake_PreserveExistingObjectsAndAuthoredPresentation(Type componentType)
    {
        GameObject root = new("Authored HUD", typeof(RectTransform), typeof(Canvas));
        try
        {
            Component component = root.AddComponent(componentType);
            MethodInfo author = componentType.GetMethod("AuthorUi", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(author, Is.Not.Null);
            author.Invoke(component, null);
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            int[] initialIds = descendants.Select(value => value.GetInstanceID()).ToArray();
            Image image = root.GetComponentsInChildren<Image>(true).FirstOrDefault();
            Color editedColor = new(0.15f, 0.25f, 0.8f, 0.42f);
            if (image != null) image.color = editedColor;
            TMP_Text label = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (label != null) label.fontSize = 31f;

            author.Invoke(component, null);
            componentType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);

            CollectionAssert.AreEqual(initialIds, root.GetComponentsInChildren<Transform>(true).Select(value => value.GetInstanceID()));
            if (image != null) Assert.That(image.color, Is.EqualTo(editedColor));
            if (label != null) Assert.That(label.fontSize, Is.EqualTo(31f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            GameplayPause.Reset();
            Time.timeScale = 1f;
        }
    }

    [Test]
    public void PauseMenu_AwakeUsesSerializedReferencesAfterAuthoredChildrenAreRenamed()
    {
        GameObject root = new("Authored Pause", typeof(RectTransform), typeof(Canvas));
        try
        {
            PauseMenuUI pause = root.AddComponent<PauseMenuUI>();
            pause.AuthorUi();
            Transform title = root.transform.Find("PauseRoot/PauseTitlePlate/Title");
            TMP_Text label = title.GetComponent<TMP_Text>();
            label.text = "BREAK TIME";
            title.name = "MyPauseHeading";
            Button resume = root.transform.Find("PauseRoot/MainActionPanel/ResumeButton").GetComponent<Button>();
            resume.name = "ContinuePlaying";
            int childCount = root.GetComponentsInChildren<Transform>(true).Length;
            MethodInfo awake = typeof(PauseMenuUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake.Invoke(pause, null);
            awake.Invoke(pause, null);
            typeof(PauseMenuUI).GetMethod("ShowPause", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pause, null);
            Assert.That(GameplayPause.IsUiPaused, Is.True);
            resume.onClick.Invoke();
            Assert.That(GameplayPause.IsUiPaused, Is.False);
            Assert.That(label.text, Is.EqualTo("BREAK TIME"));
            Assert.That(root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(childCount));
        }
        finally
        {
            Object.DestroyImmediate(root);
            GameplayPause.Reset();
            Time.timeScale = 1f;
        }
    }
}
