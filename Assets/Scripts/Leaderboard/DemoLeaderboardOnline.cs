using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DemoLeaderboardOnlineSnapshot
{
    public DemoLeaderboardData Data;
    public string Status;
    public bool IsGlobal;
    public int PendingCount;
}

public interface IDemoLeaderboardRemote
{
    Task<string> SelectPlayerAsync(string profile, string expectedPlayerId);
    Task SubmitAsync(string boardId, DemoLeaderboardEntry entry, bool survival);
    Task<System.Collections.Generic.List<DemoLeaderboardEntry>> GetScoresAsync(string boardId, bool survival);
}

public sealed class DemoLeaderboardIdentityException : InvalidOperationException
{
    public DemoLeaderboardIdentityException(string message) : base(message) { }
}

/// <summary>Owns uploads beyond scene lifetime. A profile represents one alias on this installation.</summary>
public sealed class DemoLeaderboardOnlineClient
{
    private readonly IDemoLeaderboardRemote _remote;
    private readonly DemoLeaderboardOutbox _outbox;
    private readonly string _killsId;
    private readonly string _survivalId;
    // Authentication is a UGS singleton: even reconfigured clients must finish outstanding requests
    // before another client may switch profile.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Func<bool> _enabled;
    private string _queueError;

    public DemoLeaderboardOnlineClient(IDemoLeaderboardRemote remote, DemoLeaderboardOutbox outbox,
        string killsId, string survivalId, Func<bool> enabled)
    {
        _remote = remote;
        _outbox = outbox;
        _killsId = killsId;
        _survivalId = survivalId;
        _enabled = enabled;
    }

    public bool Enqueue(DemoLeaderboardEntry entry)
    {
        if (!_enabled()) return false;
        if (!DemoLeaderboardScoreCodec.CanEncode(entry))
        {
            _queueError = "Latest run exceeds the online score limit; not uploaded.";
            return false;
        }
        bool saved = _outbox.Add(entry, ProfileForName(entry.Name));
        _queueError = saved ? null : "Latest run could not be queued for upload.";
        return saved;
    }

    public static string ProfileForName(string name)
    {
        if (!DemoLeaderboardNames.TryNormalize(name, out string normalized))
            throw new ArgumentException("Invalid leaderboard name.", nameof(name));
        return "sw-" + Hash(normalized.ToUpperInvariant()).Substring(0, 24);
    }

    internal static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    public async Task FlushAsync()
    {
        await Gate.WaitAsync();
        try { await FlushLockedAsync(); }
        finally { Gate.Release(); }
    }

    private async Task FlushLockedAsync()
    {
        if (!_outbox.IsAvailable) throw new IOException(_outbox.LastError);
        // Snapshot the list: a newly completed run may be enqueued while a request is in flight.
        var pending = new System.Collections.Generic.List<DemoLeaderboardPendingRun>(_outbox.Pending);
        bool identityBlocked = false;
        foreach (DemoLeaderboardPendingRun run in pending)
        {
            if (!_enabled()) return;
            try { await FlushRunAsync(run); }
            catch (DemoLeaderboardIdentityException) { identityBlocked = true; }
        }
        if (identityBlocked)
            throw new DemoLeaderboardIdentityException("Some pending players need their original credentials; other players were processed.");
    }

    private async Task FlushRunAsync(DemoLeaderboardPendingRun run)
    {
        if (run.KillsSent && run.SurvivalSent)
        {
            if (!_outbox.Remove(run)) throw new IOException(_outbox.LastError);
            return;
        }
        string playerId = await _remote.SelectPlayerAsync(run.Profile, run.PlayerId);
        if (!_enabled()) return;
        if (string.IsNullOrWhiteSpace(playerId) || (!string.IsNullOrEmpty(run.PlayerId) && run.PlayerId != playerId))
            throw new DemoLeaderboardIdentityException("Pending score belongs to another player. Its upload was retained.");
        run.PlayerId = playerId;
        SaveQueue(); // Persist identity before sending either score.
        if (!run.KillsSent)
        {
            await _remote.SubmitAsync(_killsId, run.Entry, false);
            if (!_enabled()) return; // Outcome may be accepted; Best Score makes replay safe.
            run.KillsSent = true;
            SaveQueue();
        }
        if (!_enabled()) return;
        if (!run.SurvivalSent)
        {
            await _remote.SubmitAsync(_survivalId, run.Entry, true);
            if (!_enabled()) return;
            run.SurvivalSent = true;
            SaveQueue();
        }
        if (!_outbox.Remove(run)) throw new IOException(_outbox.LastError);
    }

    private void SaveQueue()
    {
        if (!_outbox.Save()) throw new IOException(_outbox.LastError);
    }

    public async Task<DemoLeaderboardOnlineSnapshot> RefreshAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!_enabled()) return Unavailable("Disabled.");
            string uploadError = null;
            try { await FlushLockedAsync(); }
            catch (Exception) { uploadError = "Uploads pending / unavailable."; }
            if (!_enabled()) return Unavailable("Disabled.");
            await _remote.SelectPlayerAsync("sw-reader", null);
            if (!_enabled()) return Unavailable("Disabled.");
            var kills = await _remote.GetScoresAsync(_killsId, false);
            if (!_enabled()) return Unavailable("Disabled.");
            var survival = await _remote.GetScoresAsync(_survivalId, true);
            if (!_enabled()) return Unavailable("Disabled.");
            int pending = _outbox.Pending.Count;
            return new DemoLeaderboardOnlineSnapshot
            {
                Data = new DemoLeaderboardData { MostKills = kills, LongestSurvival = survival },
                IsGlobal = true,
                PendingCount = pending,
                Status = "GLOBAL | Updated " + DateTime.Now.ToString("HH:mm") +
                    (_queueError != null ? " | " + _queueError : pending > 0 ? " | " + pending + " run(s) waiting to upload" :
                        uploadError != null ? " | " + uploadError : "")
            };
        }
        catch (Exception)
        {
            return Unavailable("Unavailable. " +
                (_queueError ?? (_outbox.Pending.Count > 0
                    ? _outbox.Pending.Count + " run(s) saved for retry."
                    : "Check connection or setup. No local scores shown.")));
        }
        finally { Gate.Release(); }
    }

    private DemoLeaderboardOnlineSnapshot Unavailable(string reason) => new()
    {
        Data = new DemoLeaderboardData(), IsGlobal = false,
        PendingCount = _outbox.Pending.Count, Status = "GLOBAL | " + reason
    };
}

/// <summary>Scene-independent facade. Unconfigured and automated Editor runs never authenticate.</summary>
public static class DemoLeaderboardOnline
{
    private static DemoLeaderboardOnlineClient _client;
    private static string _scope;
    private static string _unavailable = "GLOBAL | Online service is not configured. Scores stay on this PC.";
    private static int _generation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ClearConfiguration();

    public static void ClearConfiguration()
    {
        _generation++;
        _client = null;
        _scope = null;
    }

    public static void Configure(DemoLeaderboardSettings settings)
    {
        if (settings == null || !settings.Enabled || !settings.UseOnlineLeaderboard)
        {
            ClearConfiguration();
            return;
        }
        string problem = null;
        if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            problem = "Unity Cloud project is not linked. Scores stay on this PC.";
        else if (string.IsNullOrWhiteSpace(settings.EnvironmentName) ||
            string.IsNullOrWhiteSpace(settings.KillsLeaderboardId) ||
            string.IsNullOrWhiteSpace(settings.SurvivalLeaderboardId) ||
            settings.KillsLeaderboardId == settings.SurvivalLeaderboardId)
            problem = "Set the environment and two distinct board IDs. Scores stay on this PC.";
#if UNITY_EDITOR
        else if (!settings.AllowOnlineInEditor || Application.isBatchMode ||
                 !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY")))
            problem = "Editor online access is disabled. Scores stay on this PC.";
#endif
        if (problem != null)
        {
            ClearConfiguration();
            _unavailable = string.IsNullOrWhiteSpace(Application.cloudProjectId)
                ? "GLOBAL | Not configured. Local play only."
                : "GLOBAL | Online access unavailable. Local play only.";
            return;
        }
        string scope = Application.cloudProjectId + "\n" + settings.EnvironmentName + "\n" +
            settings.KillsLeaderboardId + "\n" + settings.SurvivalLeaderboardId;
        if (_scope == scope && _client != null) return;
        ClearConfiguration();
        _scope = scope;
        int generation = _generation;
        string queuePath = Path.Combine(Path.GetDirectoryName(DemoLeaderboardStore.DefaultPath),
            "scrapwaves_online_pending_" + DemoLeaderboardOnlineClient.Hash(scope) + ".json");
        Func<bool> enabled = () => generation == _generation && Application.isPlaying;
        _client = new DemoLeaderboardOnlineClient(new UgsDemoLeaderboardRemote(settings.EnvironmentName, enabled),
            new DemoLeaderboardOutbox(queuePath), settings.KillsLeaderboardId, settings.SurvivalLeaderboardId,
            enabled);
    }

    public static Task<DemoLeaderboardOnlineSnapshot> RefreshAsync() => _client != null
        ? _client.RefreshAsync()
        : Task.FromResult(new DemoLeaderboardOnlineSnapshot
        {
            Data = new DemoLeaderboardData(), IsGlobal = false, Status = _unavailable
        });

    public static void RecordResult(DemoLeaderboardEntry entry)
    {
        if (_client == null) return;
        try
        {
            if (_client.Enqueue(entry)) _ = FlushSafelyAsync(_client);
            else Debug.LogWarning("Global leaderboard: this run could not enter the upload queue; local save was attempted separately.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Global leaderboard: could not queue the completed run: " + exception.Message);
        }
    }

    private static async Task FlushSafelyAsync(DemoLeaderboardOnlineClient client)
    {
        try { await client.FlushAsync(); }
        catch (Exception exception)
        {
            Debug.LogWarning("Global leaderboard upload pending; it will retry from the menu: " + exception.Message);
        }
    }
}
