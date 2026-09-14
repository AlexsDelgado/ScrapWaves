using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public class SupportUiAuthoringTests
{
    [SetUp]
    public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TestCase(typeof(MaterialInventoryHUD))]
    [TestCase(typeof(LevelUpStatFeedback))]
    [TestCase(typeof(SurvivorHud))]
    [TestCase(typeof(UIManager))]
    [TestCase(typeof(LevelExitHud))]
    public void AuthoringTwicePreservesTheExistingView(Type presenterType)
    {
        var owner = new GameObject("Controller");
        var ui = new GameObject("UI");
        try
        {
            Component presenter = owner.AddComponent(presenterType);
            presenterType.GetMethod("AuthorUi").Invoke(presenter, new object[] { ui.transform });
            int count = ui.GetComponentsInChildren<Transform>(true).Length;
            TMP_Text firstLabel = ui.GetComponentInChildren<TMP_Text>(true);
            Assert.That(firstLabel, Is.Not.Null);
            firstLabel.fontSize = 31f;
            var editedGroup = new GameObject("HandOrganizedGroup");
            editedGroup.transform.SetParent(ui.transform, false);
            Canvas authoredCanvas = ui.GetComponentInChildren<Canvas>(true);
            authoredCanvas.transform.SetParent(editedGroup.transform, false);
            count++;

            presenterType.GetMethod("AuthorUi").Invoke(presenter, new object[] { ui.transform });

            Assert.That(ui.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
            Assert.That(firstLabel.fontSize, Is.EqualTo(31f), "Authoring must preserve hand edits.");
            Assert.That(authoredCanvas.transform.parent, Is.SameAs(editedGroup.transform),
                "Authoring must preserve grouping beneath the scene UI root.");
            Assert.That(owner.GetComponentsInChildren<Canvas>(true), Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(ui);
        }
    }

    [Test]
    public void MaterialRefreshUpdatesSavedRowsWithoutRebuildingOrRestyling()
    {
        var ui = new GameObject("UI");
        var owner = new GameObject("Inventory");
        try
        {
            var inventory = owner.AddComponent<MaterialInventory>();
            MaterialInventoryDisplayView view = MaterialInventoryDisplayView.Create(ui.transform,
                MaterialDisplayLayout.Vertical, showEmpty: false);
            Transform row = view.transform.Find(MaterialCatalog.GetDisplayName(MaterialType.Gears));
            TMP_Text amount = row.Find("Amount").GetComponent<TMP_Text>();
            amount.fontSize = 35f;
            int originalCount = ui.GetComponentsInChildren<Transform>(true).Length;
            view.Refresh(inventory);
            Assert.That(row.gameObject.activeSelf, Is.False);

            inventory.Add(MaterialType.Gears, 42);
            view.Refresh(inventory);

            Assert.That(row.gameObject.activeSelf, Is.True);
            Assert.That(amount.text, Is.EqualTo("42"));
            Assert.That(amount.fontSize, Is.EqualTo(35f));
            Assert.That(ui.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(originalCount));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(ui);
        }
    }

    [Test]
    public void FeedbackAnimationReusesAuthoredMessagesAndRestoresTheirEditedPose()
    {
        var owner = new GameObject("FeedbackController");
        var ui = new GameObject("UI");
        try
        {
            var feedback = owner.AddComponent<LevelUpStatFeedback>();
            feedback.AuthorUi(ui.transform);
            var slots = (TextMeshProUGUI[])Field("_messageSlots").GetValue(feedback);
            Assert.That(slots.Length, Is.GreaterThanOrEqualTo(20));
            TextMeshProUGUI message = slots[0];
            Vector2 editedPosition = new(24f, -16f);
            Color editedColor = new(0.2f, 0.4f, 0.8f, 0.7f);
            message.rectTransform.anchoredPosition = editedPosition;
            message.color = editedColor;
            Call(feedback, "CacheMessageRestState");
            int count = ui.GetComponentsInChildren<Transform>(true).Length;

            IEnumerator animation = (IEnumerator)Call(feedback, "AnimateMessage", "++Damage", 0);
            Assert.That(animation.MoveNext(), Is.True);
            Assert.That(message.gameObject.activeSelf, Is.True);
            Assert.That(message.text, Is.EqualTo("++Damage"));
            Assert.That(ui.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
            Call(feedback, "ResetMessage", 0);

            Assert.That(message.gameObject.activeSelf, Is.False);
            Assert.That(message.rectTransform.anchoredPosition, Is.EqualTo(editedPosition));
            Assert.That(message.color, Is.EqualTo(editedColor));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(ui);
        }
    }

    private static FieldInfo Field(string name) => typeof(LevelUpStatFeedback).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static object Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, arguments);
}
