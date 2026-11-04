using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class DemoLeaderboardOnlineViewTests
{
    private GameObject _root;
    private DemoLeaderboardView _view;
    private TMP_Text _killsName;
    private TMP_Text _survivalName;
    private TMP_Text _status;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Online leaderboard view fixture", typeof(RectTransform));
        _root.SetActive(false);
        _view = _root.AddComponent<DemoLeaderboardView>();
        Set(_view, "_root", _root);
        _killsName = AddRow("_mostKillsRows");
        _survivalName = AddRow("_longestSurvivalRows");
        _status = Label("Status");
        Set(_view, "_status", _status);
    }

    [TearDown]
    public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);

    [Test]
    public void GlobalSnapshot_RendersBothBoardsAndStatusAsPlainText()
    {
        _view.ShowOnlineSnapshot(new DemoLeaderboardOnlineSnapshot
        {
            IsGlobal = true,
            Data = Scores("<b>Ada</b>"),
            Status = "GLOBAL | <b>Updated</b> | 1 run waiting to upload",
            PendingCount = 1
        });

        Assert.That(_killsName.text, Is.EqualTo("<b>Ada</b>"));
        Assert.That(_survivalName.text, Is.EqualTo("<b>Ada</b>"));
        Assert.That(_killsName.richText, Is.False);
        Assert.That(_status.text, Is.EqualTo("GLOBAL | <b>Updated</b> | 1 run waiting to upload"));
        Assert.That(_status.richText, Is.False);
    }

    [Test]
    public void UnavailableSnapshot_ClearsOldScoresAndNeverDisplaysLocalFallbackAsGlobal()
    {
        _view.ShowOnlineSnapshot(new DemoLeaderboardOnlineSnapshot { IsGlobal = true, Data = Scores("Previous global player") });
        _view.ShowOnlineSnapshot(new DemoLeaderboardOnlineSnapshot
        {
            IsGlobal = false,
            Data = Scores("Local player"),
            Status = "GLOBAL | Unavailable. 2 runs saved for retry.",
            PendingCount = 2
        });

        Assert.That(_killsName.text, Is.EqualTo("\u2014"));
        Assert.That(_survivalName.text, Is.EqualTo("\u2014"));
        Assert.That(_status.text, Is.EqualTo("GLOBAL | Unavailable. 2 runs saved for retry."));
    }

    [Test]
    public void MissingSnapshot_ClearsOldScoresAndShowsAnExplicitUnavailableState()
    {
        _view.ShowData(Scores("Previous player"));
        _view.ShowOnlineSnapshot(null);

        Assert.That(_killsName.text, Is.EqualTo("\u2014"));
        Assert.That(_survivalName.text, Is.EqualTo("\u2014"));
        Assert.That(_status.text, Does.StartWith("GLOBAL"));
        Assert.That(_status.text, Does.Contain("Unavailable"));
    }

    [Test]
    public void LocalRefresh_ExplicitlyLabelsTheBoardAsThisComputerOnly()
    {
        string previous = Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY");
        string directory = Path.Combine(Path.GetTempPath(), "scrapwaves-online-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY", directory);
            _view.RefreshFromStore();
            Assert.That(_status.text, Is.EqualTo("LOCAL \u2022 This PC only"));
            Assert.That(Directory.Exists(directory), Is.False, "An empty local refresh must not create save data.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY", previous);
        }
    }

    private static DemoLeaderboardData Scores(string name)
    {
        var data = new DemoLeaderboardData();
        data.MostKills.Add(new DemoLeaderboardEntry { Name = name, Kills = 42 });
        data.LongestSurvival.Add(new DemoLeaderboardEntry { Name = name, SurvivalSeconds = 125 });
        return data;
    }

    private TMP_Text AddRow(string field)
    {
        Type rowType = typeof(DemoLeaderboardView).GetNestedType("Row", BindingFlags.NonPublic);
        object row = Activator.CreateInstance(rowType, true);
        TMP_Text name = Label(field + " Name");
        Set(row, "_rank", Label(field + " Rank"));
        Set(row, "_name", name);
        Set(row, "_value", Label(field + " Value"));
        Array rows = Array.CreateInstance(rowType, 1);
        rows.SetValue(row, 0);
        Set(_view, field, rows);
        return name;
    }

    private TMP_Text Label(string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(_root.transform, false);
        return child.AddComponent<TextMeshProUGUI>();
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}
