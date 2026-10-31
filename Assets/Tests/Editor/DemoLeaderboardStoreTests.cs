using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class DemoLeaderboardStoreTests
{
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ScrapWavesLeaderboardTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, DemoLeaderboardStore.FileName);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            string target = Path.GetFullPath(_directory);
            string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "ScrapWavesLeaderboardTests-");
            Assert.That(target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.Delete(target, recursive: true);
        }
    }

    [Test]
    public void MissingFileLoadsEmptyWithoutWriting()
    {
        var store = new DemoLeaderboardStore(_path);
        DemoLeaderboardData data = store.Load();
        Assert.That(data.MostKills, Is.Empty);
        Assert.That(data.LongestSurvival, Is.Empty);
        Assert.That(data.NextOrder, Is.EqualTo(1));
        Assert.That(File.Exists(_path), Is.False);
        Assert.That(store.LastError, Is.Empty);
    }

    [TestCase("  Ada   Lovelace  ", "Ada Lovelace")]
    [TestCase("Ana\u00a0\u2002Maria", "Ana Maria")]
    [TestCase("Jose\u0301", "Jos\u00e9")]
    [TestCase("1234567890123456", "1234567890123456")]
    public void NamesNormalizeWhitespaceAndUnicode(string input, string expected)
    {
        Assert.That(DemoLeaderboardNames.TryNormalize(input, out string display), Is.True);
        Assert.That(display, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("12345678901234567")]
    [TestCase("Ana\nMaria")]
    [TestCase("Ana\rMaria")]
    [TestCase("Ana\tMaria")]
    [TestCase("Ana\u2028Maria")]
    [TestCase("Ana\u2029Maria")]
    [TestCase("Ana\0Maria")]
    [TestCase("\u200b")]
    public void NamesRejectEmptyLongAndControlInput(string input)
    {
        Assert.That(DemoLeaderboardNames.TryNormalize(input, out string display), Is.False);
        Assert.That(display, Is.Empty);
    }

    [Test]
    public void EachBoardKeepsTenIndependentBestPlayersAcrossRestart()
    {
        var store = new DemoLeaderboardStore(_path);
        for (int index = 0; index < 12; index++)
            Assert.That(store.Submit(Entry("Player " + index, index, 120 - index)), Is.True, store.LastError);

        DemoLeaderboardData restored = new DemoLeaderboardStore(_path).Load();
        Assert.That(restored.MostKills.Select(entry => entry.Kills),
            Is.EqualTo(new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 }));
        Assert.That(restored.LongestSurvival.Select(entry => entry.SurvivalSeconds),
            Is.EqualTo(new[] { 120, 119, 118, 117, 116, 115, 114, 113, 112, 111 }));
        Assert.That(restored.NextOrder, Is.EqualTo(13));
    }

    [Test]
    public void SameNormalizedNameHasIndependentBestAttemptsForKillsAndTime()
    {
        var store = new DemoLeaderboardStore(_path);
        DemoLeaderboardEntry killRun = Entry("  Ada   Lee  ", 100, 60);
        DemoLeaderboardEntry timeRun = Entry("ADA LEE", 50, 120);
        Assert.That(store.Submit(killRun), Is.True);
        Assert.That(store.Submit(timeRun), Is.True);
        Assert.That(store.Submit(Entry("ada lee", 40, 50)), Is.True,
            "A valid non-improving attempt is accepted without replacing either best score.");

        DemoLeaderboardData data = store.Load();
        Assert.That(data.MostKills, Has.Count.EqualTo(1));
        Assert.That(data.LongestSurvival, Has.Count.EqualTo(1));
        Assert.That(data.MostKills[0].RunId, Is.EqualTo(killRun.RunId));
        Assert.That(data.MostKills[0].Name, Is.EqualTo("Ada Lee"));
        Assert.That(data.LongestSurvival[0].RunId, Is.EqualTo(timeRun.RunId));
        Assert.That(data.NextOrder, Is.EqualTo(4));
    }

    [Test]
    public void KillsBreakTiesByTimeThenEarlierCompletionAndSurvivalUsesInversePriority()
    {
        var store = new DemoLeaderboardStore(_path);
        Assert.That(store.Submit(Entry("First", 10, 20)), Is.True);
        Assert.That(store.Submit(Entry("Second", 10, 20)), Is.True);
        Assert.That(store.Submit(Entry("More time", 10, 30)), Is.True);
        Assert.That(store.Submit(Entry("More kills", 11, 20)), Is.True);

        DemoLeaderboardData data = new DemoLeaderboardStore(_path).Load();
        Assert.That(data.MostKills.Select(entry => entry.Name),
            Is.EqualTo(new[] { "More kills", "More time", "First", "Second" }));
        Assert.That(data.LongestSurvival.Select(entry => entry.Name),
            Is.EqualTo(new[] { "More time", "More kills", "First", "Second" }));
    }

    [Test]
    public void ExactTieAtCutoffKeepsTheEarlierResult()
    {
        var store = new DemoLeaderboardStore(_path);
        for (int index = 0; index < 10; index++)
            Assert.That(store.Submit(Entry("Player " + index, 100, 100)), Is.True);
        Assert.That(store.Submit(Entry("Late arrival", 100, 100)), Is.True);

        DemoLeaderboardData data = store.Load();
        Assert.That(data.MostKills.Select(entry => entry.Name), Does.Not.Contain("Late arrival"));
        Assert.That(data.LongestSurvival.Select(entry => entry.Name), Does.Not.Contain("Late arrival"));
        Assert.That(data.MostKills.Last().Name, Is.EqualTo("Player 9"));
    }

    [Test]
    public void DuplicateRetainedRunCannotReplaceItsResultOrConsumeAnotherOrder()
    {
        var store = new DemoLeaderboardStore(_path);
        DemoLeaderboardEntry result = Entry("Player", 10, 60);
        Assert.That(store.Submit(result), Is.True);
        string original = File.ReadAllText(_path);

        var restarted = new DemoLeaderboardStore(_path);
        result.Kills = 999;
        Assert.That(restarted.Submit(result), Is.False);
        Assert.That(restarted.LastError, Does.Contain("already"));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        Assert.That(restarted.Load().NextOrder, Is.EqualTo(2));
    }

    [Test]
    public void SubmittedEntriesAndReturnedSnapshotsCannotMutateTheStore()
    {
        var store = new DemoLeaderboardStore(_path);
        DemoLeaderboardEntry result = Entry("Player", 10, 60);
        result.Victory = true;
        result.CompletedOrder = 999;
        Assert.That(store.Submit(result), Is.True);
        result.Name = "Changed";
        result.Kills = 200;

        DemoLeaderboardData firstSnapshot = store.Load();
        firstSnapshot.MostKills[0].Name = "Snapshot change";
        firstSnapshot.LongestSurvival.Clear();
        DemoLeaderboardData nextSnapshot = store.Load();
        Assert.That(nextSnapshot.MostKills[0].Name, Is.EqualTo("Player"));
        Assert.That(nextSnapshot.MostKills[0].Kills, Is.EqualTo(10));
        Assert.That(nextSnapshot.MostKills[0].CompletedOrder, Is.EqualTo(1));
        Assert.That(nextSnapshot.MostKills[0].Victory, Is.True);
        Assert.That(nextSnapshot.LongestSurvival, Has.Count.EqualTo(1));
        Assert.That(nextSnapshot.MostKills[0], Is.Not.SameAs(nextSnapshot.LongestSurvival[0]));
    }

    [Test]
    public void InvalidSubmissionsDoNotCreateAFile()
    {
        var store = new DemoLeaderboardStore(_path);
        Assert.That(store.Submit(null), Is.False);
        Assert.That(store.Submit(Entry(" ", 1, 2)), Is.False);
        Assert.That(store.Submit(Entry("Player", -1, 2)), Is.False);
        Assert.That(store.Submit(Entry("Player", 1, -2)), Is.False);
        DemoLeaderboardEntry invalidId = Entry("Player", 1, 2);
        invalidId.RunId = string.Empty;
        Assert.That(store.Submit(invalidId), Is.False);
        Assert.That(File.Exists(_path), Is.False);
        Assert.That(store.Load().NextOrder, Is.EqualTo(1));
    }

    [Test]
    public void CorruptFileIsPreservedOnLoadAndBackedUpBeforeFirstReplacement()
    {
        const string corrupt = "{ not valid leaderboard json";
        File.WriteAllText(_path, corrupt);
        var store = new DemoLeaderboardStore(_path);
        Assert.That(store.Load().MostKills, Is.Empty);
        Assert.That(store.LastError, Is.Not.Empty);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(corrupt));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));

        Assert.That(store.Submit(Entry("Player", 10, 60)), Is.True, store.LastError);
        string[] backups = Directory.GetFiles(_directory, "*.corrupt-*.bak");
        Assert.That(backups, Has.Length.EqualTo(1));
        Assert.That(File.ReadAllText(backups[0]), Is.EqualTo(corrupt));
        Assert.That(new DemoLeaderboardStore(_path).Load().MostKills[0].Kills, Is.EqualTo(10));
        Assert.That(store.LastError, Is.Empty);
    }

    [Test]
    public void NewerDataVersionCannotBeOverwrittenOrCleared()
    {
        const string newer = "{\"Version\":2,\"FutureData\":\"preserve\"}";
        File.WriteAllText(_path, newer);
        var store = new DemoLeaderboardStore(_path);
        Assert.That(store.Load().MostKills, Is.Empty);
        Assert.That(store.Submit(Entry("Player", 10, 60)), Is.False);
        Assert.That(store.Clear(), Is.False);
        Assert.That(store.LastError, Does.Contain("newer version"));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(newer));
    }

    [Test]
    public void LoadedDataDropsInvalidRowsDeduplicatesNamesAndRepairsNextOrder()
    {
        var data = new DemoLeaderboardData { NextOrder = 0 };
        DemoLeaderboardEntry better = Entry("Player", 20, 60);
        better.CompletedOrder = 8;
        DemoLeaderboardEntry worse = Entry("PLAYER", 10, 80);
        worse.CompletedOrder = 2;
        DemoLeaderboardEntry invalid = Entry("Bad", -1, 10);
        invalid.CompletedOrder = 3;
        data.MostKills.AddRange(new[] { worse, null, invalid, better });
        data.LongestSurvival = null;
        File.WriteAllText(_path, JsonUtility.ToJson(data));

        var store = new DemoLeaderboardStore(_path);
        DemoLeaderboardData loaded = store.Load();
        Assert.That(loaded.MostKills, Has.Count.EqualTo(1));
        Assert.That(loaded.MostKills[0].RunId, Is.EqualTo(better.RunId));
        Assert.That(loaded.LongestSurvival, Is.Empty);
        Assert.That(loaded.NextOrder, Is.EqualTo(9));
        Assert.That(store.Submit(Entry("New player", 21, 90)), Is.True);
        Assert.That(store.Load().MostKills[0].CompletedOrder, Is.EqualTo(9));
    }

    [Test]
    public void FailedWriteDoesNotPublishTheResultAndCanBeRetried()
    {
        string blockedParent = Path.Combine(_directory, "blocked-parent");
        File.WriteAllText(blockedParent, "This is a file, not a directory.");
        var store = new DemoLeaderboardStore(Path.Combine(blockedParent, "leaderboard.json"));
        DemoLeaderboardEntry result = Entry("Player", 10, 60);
        Assert.That(store.Submit(result), Is.False);
        Assert.That(store.LastError, Is.Not.Empty);
        Assert.That(store.Load().MostKills, Is.Empty);
        Assert.That(store.Load().NextOrder, Is.EqualTo(1));

        File.Delete(blockedParent);
        Assert.That(store.Submit(result), Is.True, store.LastError);
        Assert.That(store.Load().MostKills[0].CompletedOrder, Is.EqualTo(1));
        Assert.That(store.LastError, Is.Empty);
    }

    [Test]
    public void ClearOnlyResetsTheLeaderboardFile()
    {
        string metaPath = Path.Combine(_directory, "scrapwaves_save.json");
        const string meta = "{\"Scrap\":137}";
        File.WriteAllText(metaPath, meta);
        var store = new DemoLeaderboardStore(_path);
        Assert.That(store.Submit(Entry("Player", 10, 60)), Is.True);
        Assert.That(store.Clear(), Is.True);

        DemoLeaderboardData restored = new DemoLeaderboardStore(_path).Load();
        Assert.That(restored.MostKills, Is.Empty);
        Assert.That(restored.LongestSurvival, Is.Empty);
        Assert.That(restored.NextOrder, Is.EqualTo(1));
        Assert.That(File.ReadAllText(metaPath), Is.EqualTo(meta));
    }

    [Test]
    public void DefaultPathHonorsTheExistingEditorTestIsolationOverride()
    {
        const string key = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
        string previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, _directory);
            Assert.That(DemoLeaderboardStore.DefaultPath, Is.EqualTo(_path));
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    private static DemoLeaderboardEntry Entry(string name, int kills, int seconds) => new()
    {
        RunId = Guid.NewGuid().ToString("N"),
        Name = name,
        Kills = kills,
        SurvivalSeconds = seconds
    };
}
