using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class DemoLeaderboardOutboxTests
{
    private string _directory;
    private string _path;

    [Serializable]
    private sealed class QueueFixture
    {
        public int Version = 1;
        public List<DemoLeaderboardPendingRun> Runs = new();
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ScrapWavesOutboxTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "pending.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_directory))
            return;
        string target = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "ScrapWavesOutboxTests-");
        Assert.That(target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(target, recursive: true);
    }

    [Test]
    public void MissingQueue_IsAvailableAndDoesNotWriteUntilAResultArrives()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.IsAvailable, Is.True);
        Assert.That(queue.Pending, Is.Empty);
        Assert.That(queue.LastError, Is.Empty);
        Assert.That(File.Exists(_path), Is.False);
    }

    [Test]
    public void Add_SnapshotsNameAndMetrics_AndRestoresAfterRestart()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        DemoLeaderboardEntry source = Entry("  Ana  R  ", 7, 42);
        string runId = source.RunId;
        Assert.That(queue.Add(source, "sw_ana"), Is.True, queue.LastError);
        source.Name = "Other";
        source.Kills = 900;
        source.SurvivalSeconds = 800;

        DemoLeaderboardPendingRun restored = new DemoLeaderboardOutbox(_path).Pending[0];
        Assert.That(restored.Entry.RunId, Is.EqualTo(runId));
        Assert.That(restored.Entry.Name, Is.EqualTo("Ana R"));
        Assert.That(restored.Entry.Kills, Is.EqualTo(7));
        Assert.That(restored.Entry.SurvivalSeconds, Is.EqualTo(42));
        Assert.That(restored.Entry.Victory, Is.True);
        Assert.That(restored.Profile, Is.EqualTo("sw_ana"));
        Assert.That(restored.PlayerId, Is.Null.Or.Empty);
        Assert.That(restored.KillsSent, Is.False);
        Assert.That(restored.SurvivalSent, Is.False);
    }

    [Test]
    public void IndependentBoardAcknowledgements_AndPlayerBindingSurviveRestart()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.Add(Entry("Ana", 7, 42), "sw_ana"), Is.True);
        DemoLeaderboardPendingRun pending = queue.Pending[0];
        pending.PlayerId = "player-ana";
        Assert.That(queue.Save(), Is.True);
        pending.KillsSent = true;
        Assert.That(queue.Save(), Is.True);

        var restored = new DemoLeaderboardOutbox(_path);
        DemoLeaderboardPendingRun run = restored.Pending[0];
        Assert.That(run.PlayerId, Is.EqualTo("player-ana"));
        Assert.That(run.KillsSent, Is.True);
        Assert.That(run.SurvivalSent, Is.False);
        Assert.That(restored.Remove(run), Is.False, "One board success must not discard the other upload.");
        run.SurvivalSent = true;
        Assert.That(restored.Save(), Is.True);
        Assert.That(restored.Remove(run), Is.True);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Is.Empty);
    }

    [Test]
    public void DuplicateRun_IsIdempotent_WithoutResettingAcknowledgementsOrAcceptingChanges()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        DemoLeaderboardEntry entry = Entry("Ana", 7, 42);
        Assert.That(queue.Add(entry, "sw_ana"), Is.True);
        queue.Pending[0].PlayerId = "player-ana";
        queue.Pending[0].KillsSent = true;
        Assert.That(queue.Save(), Is.True);
        Assert.That(queue.Add(entry, "sw_ana"), Is.True);
        Assert.That(queue.Pending, Has.Count.EqualTo(1));
        Assert.That(queue.Pending[0].KillsSent, Is.True);
        Assert.That(queue.Add(entry, "sw_someone_else"), Is.False);
        entry.Kills++;
        Assert.That(queue.Add(entry, "sw_ana"), Is.False);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending[0].Entry.Kills, Is.EqualTo(7));
    }

    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("{\"Version\":1}")]
    [TestCase("{\"Version\":2,\"Runs\":[]}")]
    [TestCase("{\"Version\":1,\"Runs\":[null]}")]
    public void InvalidOrFutureFile_IsPreservedAndBlocksWrites(string original)
    {
        File.WriteAllText(_path, original);
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.IsAvailable, Is.False);
        Assert.That(queue.LastError, Is.Not.Empty);
        Assert.That(queue.Add(Entry("Ana", 7, 42), "sw_ana"), Is.False);
        Assert.That(queue.Save(), Is.False);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
    }

    [Test]
    public void InvalidPersistedResult_BlocksWholeQueueInsteadOfSilentlyDiscardingIt()
    {
        var fixture = new QueueFixture();
        fixture.Runs.Add(new DemoLeaderboardPendingRun { Entry = Entry("Ana", -1, 42), Profile = "sw_ana" });
        string original = JsonUtility.ToJson(fixture);
        File.WriteAllText(_path, original);
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.IsAvailable, Is.False);
        Assert.That(queue.Save(), Is.False);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
    }

    [Test]
    public void QueueRetainsRunsOutsideLocalTopTen_AndRejectsCapacityOverflowWithoutDroppingEarlierRuns()
    {
        var fixture = new QueueFixture();
        for (int index = 0; index < DemoLeaderboardOutbox.Capacity; index++)
            fixture.Runs.Add(new DemoLeaderboardPendingRun { Entry = Entry("Ana", index, 42), Profile = "sw_ana" });
        string original = JsonUtility.ToJson(fixture);
        File.WriteAllText(_path, original);
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.IsAvailable, Is.True);
        Assert.That(queue.Pending, Has.Count.EqualTo(DemoLeaderboardOutbox.Capacity));
        Assert.That(queue.Add(Entry("Ben", 99999, 50), "sw_ben"), Is.False);
        Assert.That(queue.LastError, Does.Contain("full"));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
    }

    [Test]
    public void FailedAdd_DoesNotPretendTheResultWasDurablyQueued_AndCanRetry()
    {
        string blockedParent = Path.Combine(_directory, "blocked");
        File.WriteAllText(blockedParent, "This is a file, not a directory.");
        var queue = new DemoLeaderboardOutbox(Path.Combine(blockedParent, "pending.json"));
        DemoLeaderboardEntry entry = Entry("Ana", 7, 42);
        Assert.That(queue.Add(entry, "sw_ana"), Is.False);
        Assert.That(queue.Pending, Is.Empty);
        Assert.That(queue.LastError, Is.Not.Empty);
        File.Delete(blockedParent);
        Assert.That(queue.Add(entry, "sw_ana"), Is.True, queue.LastError);
        Assert.That(queue.Pending, Has.Count.EqualTo(1));
    }

    [Test]
    public void InvalidAcknowledgementWithoutPlayerId_CannotReplaceLastCompleteFile()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.Add(Entry("Ana", 7, 42), "sw_ana"), Is.True);
        string original = File.ReadAllText(_path);
        queue.Pending[0].KillsSent = true;
        Assert.That(queue.Save(), Is.False);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        queue.Pending[0].PlayerId = "player-ana";
        Assert.That(queue.Save(), Is.True);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending[0].KillsSent, Is.True);
    }

    [Test]
    public void FailedRemoval_RestoresInMemoryPendingResult()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.Add(Entry("Ana", 7, 42), "sw_ana"), Is.True);
        DemoLeaderboardPendingRun run = queue.Pending[0];
        run.PlayerId = "player-ana";
        run.KillsSent = run.SurvivalSent = true;
        Assert.That(queue.Save(), Is.True);
        File.Delete(_path);
        Directory.CreateDirectory(_path);
        Assert.That(queue.Remove(run), Is.False);
        Assert.That(queue.Pending, Has.Count.EqualTo(1));
        Assert.That(queue.Pending[0], Is.SameAs(run));
        Directory.Delete(_path);
        Assert.That(queue.Remove(run), Is.True);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Is.Empty);
    }

    private static DemoLeaderboardEntry Entry(string name, int kills, int seconds) => new()
    {
        RunId = Guid.NewGuid().ToString("N"),
        Name = name,
        Kills = kills,
        SurvivalSeconds = seconds,
        Victory = true
    };
}
