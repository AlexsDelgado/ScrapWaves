using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

public class AchievementToastAuthoringTests
{
    [SetUp]
    public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [Test]
    public void GameplayToastReusesAuthoredViewAndPreservesEditedStyle()
    {
        var ui = new GameObject("UI");
        var serviceObject = new GameObject("ToastService");
        AchievementDefinition achievement = ScriptableObject.CreateInstance<AchievementDefinition>();
        achievement.name = "First victory";
        try
        {
            AchievementUnlockToastView view = AchievementUnlockToastView.AuthorUi(ui.transform);
            var service = serviceObject.AddComponent<AchievementUnlockToast>();
            var title = (TextMeshProUGUI)ViewField("_title").GetValue(view);
            title.fontSize = 28f;
            title.color = Color.cyan;
            int originalCount = ui.GetComponentsInChildren<Transform>(true).Length;

            Assert.That(Prepare(service, achievement), Is.True);
            Assert.That(title.text, Is.EqualTo("First victory"));
            Assert.That(title.fontSize, Is.EqualTo(28f));
            Assert.That(title.color, Is.EqualTo(Color.cyan));
            Assert.That(serviceObject.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(ui.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(originalCount));
            Call(service, "FinishPresentation");
            Assert.That(Prepare(service, achievement), Is.True);
            Assert.That(ui.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(originalCount));
            Assert.That(AchievementUnlockToastView.AuthorUi(ui.transform), Is.SameAs(view));
        }
        finally
        {
            Object.DestroyImmediate(serviceObject);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(achievement);
        }
    }

    [Test]
    public void DisabledAuthoredToastWaitsAndRebindsWhenEnabled()
    {
        var ui = new GameObject("UI");
        var serviceObject = new GameObject("ToastService");
        AchievementDefinition achievement = ScriptableObject.CreateInstance<AchievementDefinition>();
        try
        {
            AchievementUnlockToastView view = AchievementUnlockToastView.AuthorUi(ui.transform);
            var service = serviceObject.AddComponent<AchievementUnlockToast>();
            RectTransform panel = (RectTransform)ViewField("_panel").GetValue(view);
            Canvas canvas = (Canvas)ViewField("_canvas").GetValue(view);
            Assert.That(Prepare(service, achievement), Is.True);

            view.enabled = false;
            Assert.That(Prepare(service, achievement), Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False);

            view.enabled = true;
            Assert.That(Prepare(service, achievement), Is.True);
            Assert.That(panel.gameObject.activeSelf, Is.True);

            canvas.enabled = false;
            Assert.That(Prepare(service, achievement), Is.False);
            Assert.That(canvas.enabled, Is.False, "The service must preserve authored Canvas visibility.");
            canvas.enabled = true;
            Assert.That(Prepare(service, achievement), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(serviceObject);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(achievement);
        }
    }

    [Test]
    public void GameplayWithoutAuthoredToastWaitsWithoutCreatingACanvas()
    {
        var serviceObject = new GameObject("ToastService");
        AchievementDefinition achievement = ScriptableObject.CreateInstance<AchievementDefinition>();
        try
        {
            var service = serviceObject.AddComponent<AchievementUnlockToast>();
            Assert.That(Prepare(service, achievement), Is.False);
            Assert.That(serviceObject.GetComponentsInChildren<Canvas>(true), Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(serviceObject);
            Object.DestroyImmediate(achievement);
        }
    }

    private static bool Prepare(AchievementUnlockToast service, AchievementDefinition achievement) =>
        (bool)Call(service, "TryPreparePresentation", achievement);

    private static object Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

    private static FieldInfo ViewField(string name) => typeof(AchievementUnlockToastView)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
}
