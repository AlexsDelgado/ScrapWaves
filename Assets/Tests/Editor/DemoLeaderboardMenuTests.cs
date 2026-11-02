using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DemoLeaderboardMenuTests
{
    private GameObject _root;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Leaderboard menu fixture", typeof(RectTransform));
        _root.SetActive(false);
    }

    [TearDown]
    public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);

    [Test]
    public void NameEntry_RejectsWhitespaceAndSubmitsNormalizedNameOnlyOnce()
    {
        PlayerNameEntryView view = CreateNameEntry(out TMP_InputField input, out Button start);
        int submissions = 0;
        string submittedName = null;
        view.Confirmed += value => { submissions++; submittedName = value; };
        view.Prepare();
        view.ActivateInput();

        input.text = "   ";
        start.onClick.Invoke();
        Assert.That(start.interactable, Is.False);
        Assert.That(submissions, Is.Zero);

        input.text = "  Ana   R  ";
        start.onClick.Invoke();
        start.onClick.Invoke();
        input.onSubmit.Invoke(input.text);
        Assert.That(submissions, Is.EqualTo(1));
        Assert.That(submittedName, Is.EqualTo("Ana R"));
        Assert.That(input.interactable, Is.False);
        Assert.That(start.interactable, Is.False);
    }

    [Test]
    public void NameEntry_FailedLaunchCanRetryAndNewPromptClearsPreviousName()
    {
        PlayerNameEntryView view = CreateNameEntry(out TMP_InputField input, out Button start);
        int submissions = 0;
        view.Confirmed += _ => submissions++;
        view.Prepare();
        view.ActivateInput();
        input.text = "Ada";
        start.onClick.Invoke();

        view.ShowLaunchError();
        Assert.That(input.text, Is.EqualTo("Ada"));
        Assert.That(start.interactable, Is.True);
        start.onClick.Invoke();
        Assert.That(submissions, Is.EqualTo(2));

        view.ResetEntry();
        view.Prepare();
        view.ActivateInput();
        Assert.That(input.text, Is.Empty);
        Assert.That(start.interactable, Is.False);
        input.text = "Ben";
        start.onClick.Invoke();
        Assert.That(submissions, Is.EqualTo(3));
    }

    [Test]
    public void Boards_PopulateAuthoredRowsWithoutCreatingObjectsAndRenderNamesAsPlainText()
    {
        DemoLeaderboardView view = _root.AddComponent<DemoLeaderboardView>();
        Set(view, "_root", _root);
        TMP_Text[,] kills = AddRows(view, "_mostKillsRows");
        TMP_Text[,] time = AddRows(view, "_longestSurvivalRows");
        var data = new DemoLeaderboardData();
        data.MostKills.Add(new DemoLeaderboardEntry { Name = "<b>Ada</b>", Kills = 123, SurvivalSeconds = 90 });
        data.LongestSurvival.Add(new DemoLeaderboardEntry { Name = "Ben", Kills = 14, SurvivalSeconds = 725 });
        int countBefore = _root.GetComponentsInChildren<Transform>(true).Length;

        view.ShowData(data);

        Assert.That(view.HasRequiredReferences, Is.True);
        Assert.That(_root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(countBefore));
        Assert.That(kills[0, 1].text, Is.EqualTo("<b>Ada</b>"));
        Assert.That(kills[0, 1].richText, Is.False);
        Assert.That(kills[0, 2].text, Is.EqualTo("123"));
        Assert.That(time[0, 2].text, Is.EqualTo("12:05"));
        Assert.That(kills[9, 0].text, Is.EqualTo("10"));
        Assert.That(kills[9, 1].text, Is.EqualTo("—"));
        Assert.That(kills[9, 2].text, Is.EqualTo("—"));
        view.ShowData(new DemoLeaderboardData());
        Assert.That(kills[0, 1].text, Is.EqualTo("—"));
        Assert.That(time[0, 2].text, Is.EqualTo("—"));
    }

    private PlayerNameEntryView CreateNameEntry(out TMP_InputField input, out Button start)
    {
        PlayerNameEntryView view = _root.AddComponent<PlayerNameEntryView>();
        input = Child("Input").AddComponent<TMP_InputField>();
        input.textComponent = Label("Input text", input.transform);
        input.textViewport = input.transform as RectTransform;
        start = Child("Start").AddComponent<Button>();
        Set(view, "_input", input);
        Set(view, "_validation", Label("Validation", _root.transform));
        Set(view, "_startButton", start);
        Set(view, "_cancelButton", Child("Cancel").AddComponent<Button>());
        return view;
    }

    private TMP_Text[,] AddRows(DemoLeaderboardView view, string field)
    {
        Type rowType = typeof(DemoLeaderboardView).GetNestedType("Row", BindingFlags.NonPublic);
        Array rows = Array.CreateInstance(rowType, 10);
        var labels = new TMP_Text[10, 3];
        string[] fields = { "_rank", "_name", "_value" };
        for (int row = 0; row < 10; row++)
        {
            object binding = Activator.CreateInstance(rowType, true);
            for (int column = 0; column < fields.Length; column++)
            {
                labels[row, column] = Label($"{field} {row} {column}", _root.transform);
                Set(binding, fields[column], labels[row, column]);
            }
            rows.SetValue(binding, row);
        }
        Set(view, field, rows);
        return labels;
    }

    private GameObject Child(string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(_root.transform, false);
        return child;
    }

    private static TMP_Text Label(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.AddComponent<TextMeshProUGUI>();
    }

    private static void Set(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, field);
        info.SetValue(target, value);
    }
}
