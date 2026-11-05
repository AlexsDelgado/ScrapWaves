using System;
using System.IO;
using NUnit.Framework;

public sealed class DemoLeaderboardPlayerProfileTests
{
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ScrapWavesPlayerProfileTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, DemoLeaderboardPlayerProfile.FileName);
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_directory))
            return;
        string target = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "ScrapWavesPlayerProfileTests-");
        Assert.That(target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(target, true);
    }

    [Test]
    public void FirstLaunch_DoesNotGuessANameOrCreateAFile()
    {
        var profile = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(profile.Name, Is.Empty);
        Assert.That(profile.LastError, Is.Empty);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public void SavedName_SurvivesNewInstanceAndKeepsExistingAliasIdentity()
    {
        var profile = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(profile.TrySave("  Ana   R  "), Is.True, profile.LastError);
        var restarted = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(restarted.Name, Is.EqualTo("Ana R"));
        Assert.That(DemoLeaderboardOnlineClient.ProfileForName(restarted.Name),
            Is.EqualTo(DemoLeaderboardOnlineClient.ProfileForName("ANA R")));
        Assert.That(restarted.TrySave("Ben"), Is.True, restarted.LastError);
        Assert.That(new DemoLeaderboardPlayerProfile(_path).Name, Is.EqualTo("Ben"));
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("{\"Version\":1,\"Name\":\"   \"}")]
    public void InvalidPreference_PromptsAgainAndBacksUpBeforeReplacement(string original)
    {
        File.WriteAllText(_path, original);
        var profile = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(profile.Name, Is.Empty);
        Assert.That(profile.LastError, Is.Not.Empty);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        Assert.That(profile.TrySave("Ada"), Is.True, profile.LastError);
        string[] backups = Directory.GetFiles(_directory, "*.corrupt-*.bak");
        Assert.That(backups, Has.Length.EqualTo(1));
        Assert.That(File.ReadAllText(backups[0]), Is.EqualTo(original));
        Assert.That(new DemoLeaderboardPlayerProfile(_path).Name, Is.EqualTo("Ada"));
    }

    [Test]
    public void InvalidEntry_PreservesThePreviouslySavedName()
    {
        var profile = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(profile.TrySave("Ada"), Is.True);
        string original = File.ReadAllText(_path);
        Assert.That(profile.TrySave("\n"), Is.False);
        Assert.That(profile.Name, Is.EqualTo("Ada"));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        Assert.That(new DemoLeaderboardPlayerProfile(_path).Name, Is.EqualTo("Ada"));
    }

    [Test]
    public void FailedWrite_DoesNotPublishNameAndCanRetry()
    {
        string blockedParent = Path.Combine(_directory, "blocked");
        File.WriteAllText(blockedParent, "Not a directory");
        var profile = new DemoLeaderboardPlayerProfile(Path.Combine(blockedParent, "player.json"));
        Assert.That(profile.TrySave("Ada"), Is.False);
        Assert.That(profile.Name, Is.Empty);
        Assert.That(profile.LastError, Is.Not.Empty);
        File.Delete(blockedParent);
        Assert.That(profile.TrySave("Ada"), Is.True, profile.LastError);
        Assert.That(profile.Name, Is.EqualTo("Ada"));
    }

    [Test]
    public void ChangeName_PreservesOldScoresAndBoundPendingUploads()
    {
        var profile = new DemoLeaderboardPlayerProfile(_path);
        Assert.That(profile.TrySave("Ada"), Is.True);
        var entry = new DemoLeaderboardEntry { Name = profile.Name, RunId = "old-run", Kills = 11, SurvivalSeconds = 22 };
        string scorePath = Path.Combine(_directory, DemoLeaderboardStore.FileName);
        string queuePath = Path.Combine(_directory, "pending.json");
        var store = new DemoLeaderboardStore(scorePath);
        var queue = new DemoLeaderboardOutbox(queuePath);
        Assert.That(store.Submit(entry), Is.True);
        string originalProfile = DemoLeaderboardOnlineClient.ProfileForName(entry.Name);
        Assert.That(queue.Add(entry, originalProfile), Is.True);
        queue.Pending[0].PlayerId = "original-player";
        queue.Pending[0].KillsSent = true;
        Assert.That(queue.Save(), Is.True);
        string scores = File.ReadAllText(scorePath);
        string uploads = File.ReadAllText(queuePath);

        Assert.That(profile.TrySave("Ben"), Is.True, profile.LastError);

        Assert.That(File.ReadAllText(scorePath), Is.EqualTo(scores));
        Assert.That(File.ReadAllText(queuePath), Is.EqualTo(uploads));
        DemoLeaderboardPendingRun oldRun = new DemoLeaderboardOutbox(queuePath).Pending[0];
        Assert.That(oldRun.Entry.Name, Is.EqualTo("Ada"));
        Assert.That(oldRun.Profile, Is.EqualTo(originalProfile));
        Assert.That(oldRun.PlayerId, Is.EqualTo("original-player"));
        Assert.That(oldRun.KillsSent, Is.True);
        Assert.That(oldRun.SurvivalSent, Is.False);
        Assert.That(DemoLeaderboardOnlineClient.ProfileForName(profile.Name), Is.Not.EqualTo(originalProfile));
        Assert.That(profile.TrySave("ADA"), Is.True);
        Assert.That(DemoLeaderboardOnlineClient.ProfileForName(profile.Name), Is.EqualTo(originalProfile));
    }

    [Test]
    public void DefaultPath_UsesTheSameIsolatedDirectoryAsLeaderboardSaves()
    {
        const string variable = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
        string previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, _directory);
            Assert.That(DemoLeaderboardPlayerProfile.DefaultPath, Is.EqualTo(_path));
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }
}
