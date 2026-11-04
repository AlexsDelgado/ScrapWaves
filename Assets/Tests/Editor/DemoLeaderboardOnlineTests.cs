using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

public sealed class DemoLeaderboardOnlineTests
{
    private string _directory;
    private string _path;

    private sealed class Selection
    {
        public string Profile;
        public string ExpectedPlayerId;
    }

    private sealed class Submission
    {
        public string Board;
        public string PlayerId;
        public DemoLeaderboardEntry Entry;
        public bool Survival;
    }

    private sealed class FakeRemote : IDemoLeaderboardRemote
    {
        public readonly List<Selection> Selections = new();
        public readonly List<Submission> Submissions = new();
        public readonly List<string> Queries = new();
        public Func<Selection, Task<string>> Select;
        public Func<Submission, Task> Submit;
        public Func<string, bool, Task<List<DemoLeaderboardEntry>>> Read;
        private string _playerId;

        public async Task<string> SelectPlayerAsync(string profile, string expectedPlayerId)
        {
            var selection = new Selection { Profile = profile, ExpectedPlayerId = expectedPlayerId };
            Selections.Add(selection);
            _playerId = Select == null ? "player-" + profile : await Select(selection);
            return _playerId;
        }

        public async Task SubmitAsync(string boardId, DemoLeaderboardEntry entry, bool survival)
        {
            var submission = new Submission { Board = boardId, PlayerId = _playerId, Entry = entry, Survival = survival };
            Submissions.Add(submission);
            if (Submit != null) await Submit(submission);
        }

        public Task<List<DemoLeaderboardEntry>> GetScoresAsync(string boardId, bool survival)
        {
            Queries.Add(boardId);
            return Read == null ? Task.FromResult(new List<DemoLeaderboardEntry>()) : Read(boardId, survival);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ScrapWavesOnlineTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "pending.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_directory)) return;
        string target = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "ScrapWavesOnlineTests-");
        Assert.That(target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(target, recursive: true);
    }

    [Test]
    public void Profiles_NormalizeNamesAndSeparateParticipantsWithoutUsingRawInputAsAProfile()
    {
        string ana = DemoLeaderboardOnlineClient.ProfileForName("  Ana   R  ");
        Assert.That(ana, Is.EqualTo(DemoLeaderboardOnlineClient.ProfileForName("ANA R")));
        Assert.That(DemoLeaderboardOnlineClient.ProfileForName("Jose\u0301"),
            Is.EqualTo(DemoLeaderboardOnlineClient.ProfileForName("JOS\u00c9")));
        Assert.That(ana, Is.Not.EqualTo(DemoLeaderboardOnlineClient.ProfileForName("Ben")));
        Assert.That(ana.Length, Is.LessThanOrEqualTo(30));
        Assert.That(ana, Does.Match("^[A-Za-z0-9_-]+$"));
    }

    [Test]
    public async Task DifferentParticipantsOnOnePc_UploadBothBoardsUnderTheirOwnPlayerIds()
    {
        var remote = new FakeRemote();
        var queue = new DemoLeaderboardOutbox(_path);
        var client = Client(remote, queue);
        Assert.That(client.Enqueue(Entry("Ana", 12, 34)), Is.True);
        Assert.That(client.Enqueue(Entry("Ben", 56, 78)), Is.True);

        await client.FlushAsync();

        Assert.That(remote.Submissions, Has.Count.EqualTo(4));
        Assert.That(remote.Submissions.Select(call => call.Board), Is.EqualTo(new[] { "kills", "survival", "kills", "survival" }));
        Assert.That(remote.Submissions[0].PlayerId, Is.EqualTo(remote.Submissions[1].PlayerId));
        Assert.That(remote.Submissions[2].PlayerId, Is.EqualTo(remote.Submissions[3].PlayerId));
        Assert.That(remote.Submissions[0].PlayerId, Is.Not.EqualTo(remote.Submissions[2].PlayerId));
        Assert.That(remote.Submissions[0].Entry.Name, Is.EqualTo("Ana"));
        Assert.That(remote.Submissions[2].Entry.Name, Is.EqualTo("Ben"));
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Is.Empty);
    }

    [Test]
    public async Task PartialSuccess_RestartRetriesOnlyOutstandingBoard_WithTheOriginalPlayerBinding()
    {
        var firstRemote = new FakeRemote
        {
            Submit = call => call.Survival ? Task.FromException(new IOException("offline")) : Task.CompletedTask
        };
        var client = Client(firstRemote, new DemoLeaderboardOutbox(_path));
        DemoLeaderboardEntry original = Entry("Ana", 12, 34);
        Assert.That(client.Enqueue(original), Is.True);
        await ExpectFailure<IOException>(client.FlushAsync);

        var restored = new DemoLeaderboardOutbox(_path);
        Assert.That(restored.Pending, Has.Count.EqualTo(1));
        Assert.That(restored.Pending[0].KillsSent, Is.True);
        Assert.That(restored.Pending[0].SurvivalSent, Is.False);
        string originalPlayerId = restored.Pending[0].PlayerId;
        var retryRemote = new FakeRemote();
        await Client(retryRemote, restored).FlushAsync();

        Assert.That(retryRemote.Selections.Single().ExpectedPlayerId, Is.EqualTo(originalPlayerId));
        Assert.That(retryRemote.Submissions, Has.Count.EqualTo(1));
        Assert.That(retryRemote.Submissions[0].Board, Is.EqualTo("survival"));
        Assert.That(retryRemote.Submissions[0].PlayerId, Is.EqualTo(originalPlayerId));
        Assert.That(retryRemote.Submissions[0].Entry.RunId, Is.EqualTo(original.RunId));
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Is.Empty);
    }

    [Test]
    public async Task ChangedPlayerBinding_RetainsPendingRunAndDoesNotUploadForTheWrongPerson()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        DemoLeaderboardEntry entry = Entry("Ana", 12, 34);
        Assert.That(queue.Add(entry, DemoLeaderboardOnlineClient.ProfileForName(entry.Name)), Is.True);
        queue.Pending[0].PlayerId = "original-player";
        Assert.That(queue.Save(), Is.True);
        var remote = new FakeRemote { Select = _ => Task.FromResult("different-player") };

        await ExpectFailure<DemoLeaderboardIdentityException>(Client(remote, queue).FlushAsync);

        Assert.That(remote.Submissions, Is.Empty);
        Assert.That(queue.Pending, Has.Count.EqualTo(1));
        Assert.That(new DemoLeaderboardOutbox(_path).Pending[0].PlayerId, Is.EqualTo("original-player"));
    }

    [Test]
    public async Task OneUnavailablePlayerIdentity_DoesNotBlockOtherParticipantsUploads()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        var remote = new FakeRemote
        {
            Select = selection => selection.ExpectedPlayerId == "lost-player"
                ? Task.FromException<string>(new DemoLeaderboardIdentityException("Credentials missing."))
                : Task.FromResult("player-" + selection.Profile)
        };
        var client = Client(remote, queue);
        Assert.That(client.Enqueue(Entry("Earlier", 2, 3)), Is.True);
        queue.Pending[0].PlayerId = "lost-player";
        Assert.That(queue.Save(), Is.True);
        Assert.That(client.Enqueue(Entry("Next", 4, 5)), Is.True);

        await ExpectFailure<DemoLeaderboardIdentityException>(client.FlushAsync);

        Assert.That(remote.Submissions, Has.Count.EqualTo(2));
        Assert.That(remote.Submissions.All(call => call.Entry.Name == "Next"), Is.True);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending.Single().Entry.Name, Is.EqualTo("Earlier"));
    }

    [Test]
    public async Task QueryFailure_ReturnsEmptyUnavailableSnapshot_NotPartialOrLocalResults()
    {
        var remote = new FakeRemote
        {
            Read = (_, survival) => survival
                ? Task.FromException<List<DemoLeaderboardEntry>>(new IOException("offline"))
                : Task.FromResult(new List<DemoLeaderboardEntry> { Entry("Remote", 99, 999) })
        };
        DemoLeaderboardOnlineSnapshot snapshot = await Client(remote, new DemoLeaderboardOutbox(_path)).RefreshAsync();
        Assert.That(snapshot.IsGlobal, Is.False);
        Assert.That(snapshot.Data.MostKills, Is.Empty);
        Assert.That(snapshot.Data.LongestSurvival, Is.Empty);
        Assert.That(snapshot.Status, Does.Contain("Unavailable"));
        Assert.That(snapshot.Status, Does.Contain("No local scores"));
        Assert.That(remote.Queries, Is.EqualTo(new[] { "kills", "survival" }));
    }

    [Test]
    public async Task PendingUploads_DoNotPreventFreshGlobalRead_AndAreExplicitInStatus()
    {
        var remote = new FakeRemote
        {
            Submit = _ => Task.FromException(new IOException("score write unavailable")),
            Read = (_, survival) => Task.FromResult(new List<DemoLeaderboardEntry>
                { Entry("Global", survival ? 3 : 8, survival ? 80 : 30) })
        };
        var client = Client(remote, new DemoLeaderboardOutbox(_path));
        Assert.That(client.Enqueue(Entry("Pending", 200, 300)), Is.True);

        DemoLeaderboardOnlineSnapshot snapshot = await client.RefreshAsync();

        Assert.That(snapshot.IsGlobal, Is.True);
        Assert.That(snapshot.PendingCount, Is.EqualTo(1));
        Assert.That(snapshot.Status, Does.Contain("waiting to upload"));
        Assert.That(snapshot.Data.MostKills.Single().Name, Is.EqualTo("Global"));
        Assert.That(snapshot.Data.MostKills.Single().Kills, Is.EqualTo(8));
        Assert.That(snapshot.Data.LongestSurvival.Single().SurvivalSeconds, Is.EqualTo(80));
        Assert.That(remote.Selections.Last().Profile, Is.EqualTo("sw-reader"));
    }

    [Test]
    public async Task DisabledClient_DoesNotEnqueueReplayOrRead_EvenWithPendingUploads()
    {
        var queue = new DemoLeaderboardOutbox(_path);
        Assert.That(queue.Add(Entry("Earlier", 2, 3), "sw-earlier"), Is.True);
        var remote = new FakeRemote();
        var client = Client(remote, queue, () => false);

        Assert.That(client.Enqueue(Entry("New", 4, 5)), Is.False);
        await client.FlushAsync();
        DemoLeaderboardOnlineSnapshot snapshot = await client.RefreshAsync();

        Assert.That(remote.Selections, Is.Empty);
        Assert.That(remote.Submissions, Is.Empty);
        Assert.That(remote.Queries, Is.Empty);
        Assert.That(snapshot.IsGlobal, Is.False);
        Assert.That(snapshot.Data.MostKills, Is.Empty);
        Assert.That(snapshot.Status, Does.Contain("Disabled"));
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DisableDuringFirstUpload_PreventsSecondBoardAndRetainsUncertainOutcomeForSafeReplay()
    {
        bool enabled = true;
        var started = new TaskCompletionSource<bool>();
        var finish = new TaskCompletionSource<bool>();
        var remote = new FakeRemote
        {
            Submit = _ => { started.TrySetResult(true); return finish.Task; }
        };
        var queue = new DemoLeaderboardOutbox(_path);
        var client = Client(remote, queue, () => enabled);
        Assert.That(client.Enqueue(Entry("Ana", 12, 34)), Is.True);
        Task flushing = client.FlushAsync();
        try
        {
            await Task.WhenAny(started.Task, flushing);
            Assert.That(started.Task.IsCompleted, Is.True, "Flush should reach the first board request.");
            enabled = false;
        }
        finally
        {
            finish.TrySetResult(true);
            await flushing;
        }

        Assert.That(remote.Submissions, Has.Count.EqualTo(1));
        Assert.That(remote.Submissions[0].Board, Is.EqualTo("kills"));
        DemoLeaderboardPendingRun retained = new DemoLeaderboardOutbox(_path).Pending.Single();
        Assert.That(retained.KillsSent, Is.False);
        Assert.That(retained.SurvivalSent, Is.False);
        Assert.That(retained.PlayerId, Is.Not.Empty);
    }

    [Test]
    public async Task Enqueue_SnapshotsInputAndUploadsAllQualifyingRunsBeyondTheLocalTopTen()
    {
        var remote = new FakeRemote();
        var queue = new DemoLeaderboardOutbox(_path);
        var client = Client(remote, queue);
        for (int index = 0; index < 12; index++)
        {
            DemoLeaderboardEntry entry = Entry("Player " + index, index, 12 - index);
            Assert.That(client.Enqueue(entry), Is.True);
            entry.Name = "Mutated";
            entry.Kills = 999;
            entry.SurvivalSeconds = 999;
        }
        Assert.That(queue.Pending, Has.Count.EqualTo(12));
        await client.FlushAsync();

        Assert.That(remote.Submissions, Has.Count.EqualTo(24));
        Assert.That(remote.Submissions.Where(call => !call.Survival).Select(call => call.Entry.Kills),
            Is.EqualTo(Enumerable.Range(0, 12)));
        Assert.That(remote.Submissions.Select(call => call.Entry.Name), Does.Not.Contain("Mutated"));
        Assert.That(queue.Pending, Is.Empty);
    }

    [Test]
    public async Task SeparateQueueScopes_DoNotReplayOneEventsPendingResultsIntoAnotherBoards()
    {
        var firstQueue = new DemoLeaderboardOutbox(_path);
        var secondQueue = new DemoLeaderboardOutbox(Path.Combine(_directory, "other-event.json"));
        var firstRemote = new FakeRemote();
        var secondRemote = new FakeRemote();
        var first = new DemoLeaderboardOnlineClient(firstRemote, firstQueue, "event-a-kills", "event-a-time", () => true);
        var second = new DemoLeaderboardOnlineClient(secondRemote, secondQueue, "event-b-kills", "event-b-time", () => true);
        Assert.That(first.Enqueue(Entry("Ana", 12, 34)), Is.True);
        Assert.That(second.Enqueue(Entry("Ben", 56, 78)), Is.True);

        await second.FlushAsync();

        Assert.That(firstRemote.Submissions, Is.Empty);
        Assert.That(new DemoLeaderboardOutbox(_path).Pending, Has.Count.EqualTo(1));
        Assert.That(secondRemote.Submissions.Select(call => call.Board), Is.EqualTo(new[] { "event-b-kills", "event-b-time" }));
        Assert.That(secondRemote.Submissions.All(call => call.Entry.Name == "Ben"), Is.True);
    }

    [Test]
    public async Task UnconfiguredFacade_DoesNotCreateQueueOrAttemptResultUpload()
    {
        DemoLeaderboardOnline.ClearConfiguration();
        try
        {
            DemoLeaderboardOnline.RecordResult(Entry("Ana", 12, 34));
            DemoLeaderboardOnlineSnapshot snapshot = await DemoLeaderboardOnline.RefreshAsync();
            Assert.That(snapshot.IsGlobal, Is.False);
            Assert.That(snapshot.Data.MostKills, Is.Empty);
            Assert.That(snapshot.Data.LongestSurvival, Is.Empty);
            Assert.That(snapshot.PendingCount, Is.Zero);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }
        finally { DemoLeaderboardOnline.ClearConfiguration(); }
    }

    [TestCase(0, 0)]
    [TestCase(DemoLeaderboardScoreCodec.MetricLimit, 0)]
    [TestCase(0, DemoLeaderboardScoreCodec.MetricLimit)]
    [TestCase(DemoLeaderboardScoreCodec.MetricLimit, DemoLeaderboardScoreCodec.MetricLimit)]
    public void ScoreCodec_RoundTripsBothOrdersAtExactIntegerBoundaries(int kills, int seconds)
    {
        DemoLeaderboardEntry entry = Entry("Ana", kills, seconds);
        foreach (bool survival in new[] { false, true })
        {
            double encoded = DemoLeaderboardScoreCodec.Encode(entry, survival);
            Assert.That(encoded, Is.LessThanOrEqualTo(4503599627370495d));
            DemoLeaderboardScoreCodec.Decode(encoded, survival, out int decodedKills, out int decodedSeconds);
            Assert.That(decodedKills, Is.EqualTo(kills));
            Assert.That(decodedSeconds, Is.EqualTo(seconds));
        }
    }

    [Test]
    public void ScoreCodec_PrimaryAlwaysOutranksSecondary_AndSecondaryBreaksPrimaryTies()
    {
        Assert.That(DemoLeaderboardScoreCodec.Encode(Entry("Ana", 1, 0), false),
            Is.GreaterThan(DemoLeaderboardScoreCodec.Encode(Entry("Ben", 0, DemoLeaderboardScoreCodec.MetricLimit), false)));
        Assert.That(DemoLeaderboardScoreCodec.Encode(Entry("Ana", 0, 1), true),
            Is.GreaterThan(DemoLeaderboardScoreCodec.Encode(Entry("Ben", DemoLeaderboardScoreCodec.MetricLimit, 0), true)));
        Assert.That(DemoLeaderboardScoreCodec.Encode(Entry("Ana", 10, 4), false),
            Is.GreaterThan(DemoLeaderboardScoreCodec.Encode(Entry("Ben", 10, 3), false)));
        Assert.That(DemoLeaderboardScoreCodec.Encode(Entry("Ana", 4, 10), true),
            Is.GreaterThan(DemoLeaderboardScoreCodec.Encode(Entry("Ben", 3, 10), true)));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1d)]
    [TestCase(0.5d)]
    [TestCase(4503599627370496d)]
    public void ScoreCodec_RejectsInvalidOrInexactScores(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DemoLeaderboardScoreCodec.Decode(score, false, out _, out _));
    }

    [Test]
    public async Task OverLimitRun_IsNotQueuedOrClamped_AndFailureIsVisible()
    {
        var remote = new FakeRemote();
        var queue = new DemoLeaderboardOutbox(_path);
        var client = Client(remote, queue);
        DemoLeaderboardEntry invalid = Entry("Ana", DemoLeaderboardScoreCodec.MetricLimit + 1, 42);
        Assert.That(client.Enqueue(invalid), Is.False);
        Assert.That(queue.Pending, Is.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoLeaderboardScoreCodec.Encode(invalid, false));
        DemoLeaderboardOnlineSnapshot snapshot = await client.RefreshAsync();
        Assert.That(snapshot.Status, Does.Contain("exceeds the online score limit"));
        Assert.That(remote.Submissions, Is.Empty);
    }

    private static DemoLeaderboardOnlineClient Client(FakeRemote remote, DemoLeaderboardOutbox queue, Func<bool> enabled = null) =>
        new(remote, queue, "kills", "survival", enabled ?? (() => true));

    private static async Task ExpectFailure<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        Assert.Fail("Expected " + typeof(T).Name + ".");
    }

    private static DemoLeaderboardEntry Entry(string name, int kills, int seconds) => new()
    {
        RunId = Guid.NewGuid().ToString("N"), Name = name,
        Kills = kills, SurvivalSeconds = seconds, Victory = true
    };
}
