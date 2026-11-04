using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class DemoLeaderboardPendingRun
{
    public DemoLeaderboardEntry Entry;
    public string Profile;
    public string PlayerId;
    public bool KillsSent;
    public bool SurvivalSent;
}

/// <summary>
/// Durable uploads, separate from the local top ten. One instance owns one project/environment/board path.
/// Callers persist the player binding before uploading and each board acknowledgement after success.
/// </summary>
public sealed class DemoLeaderboardOutbox
{
    public const int CurrentVersion = 1;
    public const int Capacity = 1000;

    [Serializable]
    private sealed class FileData
    {
        // No field defaults: missing version or run list must not deserialize as a valid empty queue.
        public int Version;
        public List<DemoLeaderboardPendingRun> Runs;
    }

    private readonly string _path;
    private readonly List<DemoLeaderboardPendingRun> _pending = new();
    private readonly IReadOnlyList<DemoLeaderboardPendingRun> _readOnlyPending;
    public IReadOnlyList<DemoLeaderboardPendingRun> Pending => _readOnlyPending;
    public string LastError { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; } = true;

    public DemoLeaderboardOutbox(string path)
    {
        _readOnlyPending = _pending.AsReadOnly();
        try
        {
            _path = Path.GetFullPath(path);
            if (Directory.Exists(_path))
                throw new IOException("The upload queue location is a directory.");
            if (!File.Exists(_path))
                return;

            JObject json = JObject.Parse(File.ReadAllText(_path));
            // JsonUtility may materialize an omitted List as empty. Require the field explicitly
            // so a truncated queue cannot be mistaken for an empty one and overwritten.
            if (json["Version"]?.Type != JTokenType.Integer || json["Runs"]?.Type != JTokenType.Array)
                throw new InvalidDataException("The upload queue is missing its version or run list.");
            FileData data = json.ToObject<FileData>();
            if (data == null || data.Version != CurrentVersion || data.Runs == null)
                throw new InvalidDataException("The upload queue has an unsupported or missing schema.");
            if (!Validate(data.Runs, out string error))
                throw new InvalidDataException(error);
            _pending.AddRange(data.Runs);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LastError = "Pending uploads are unavailable; the queue was preserved: " + exception.Message;
        }
    }

    /// <summary>Snapshots and saves before returning true. A matching duplicate is already safely queued.</summary>
    public bool Add(DemoLeaderboardEntry entry, string profile)
    {
        if (!IsAvailable)
            return false;
        if (!ValidEntry(entry) || !ValidProfile(profile))
        {
            LastError = "The pending result has an invalid name, run ID, profile, or score.";
            return false;
        }

        DemoLeaderboardNames.TryNormalize(entry.Name, out string name);
        foreach (DemoLeaderboardPendingRun existing in _pending)
        {
            if (!string.Equals(existing.Entry.RunId, entry.RunId, StringComparison.Ordinal))
                continue;
            bool same = string.Equals(existing.Profile, profile, StringComparison.Ordinal) &&
                        string.Equals(existing.Entry.Name, name, StringComparison.Ordinal) &&
                        existing.Entry.Kills == entry.Kills &&
                        existing.Entry.SurvivalSeconds == entry.SurvivalSeconds &&
                        existing.Entry.Victory == entry.Victory;
            LastError = same ? string.Empty : "The run ID is already queued with a different result or player.";
            return same;
        }

        if (_pending.Count >= Capacity)
        {
            LastError = "Pending uploads are full. This result could not be queued for global upload.";
            return false;
        }

        var pending = new DemoLeaderboardPendingRun
        {
            Profile = profile,
            Entry = new DemoLeaderboardEntry
            {
                RunId = entry.RunId,
                Name = name,
                Kills = entry.Kills,
                SurvivalSeconds = entry.SurvivalSeconds,
                CompletedOrder = entry.CompletedOrder,
                Victory = entry.Victory
            }
        };
        _pending.Add(pending);
        if (Save())
            return true;
        _pending.Remove(pending);
        return false;
    }

    /// <summary>Persists the current identity binding/acknowledgements. On failure, stop upload processing.</summary>
    public bool Save()
    {
        if (!IsAvailable)
            return false;
        if (!Validate(_pending, out string error))
        {
            LastError = "Pending uploads could not be saved: " + error;
            return false;
        }

        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string json = JsonUtility.ToJson(new FileData { Version = CurrentVersion, Runs = _pending }, true);
            // Flush through to disk before replacing the last complete queue.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, null);
            else
                File.Move(temporaryPath, _path);
            LastError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            LastError = "Pending uploads could not be saved: " + exception.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception) { /* Cleanup must not interrupt results or replace the original failure. */ }
        }
    }

    /// <summary>Removes only fully acknowledged results, restoring the queue if the write fails.</summary>
    public bool Remove(DemoLeaderboardPendingRun run)
    {
        if (!IsAvailable)
            return false;
        int index = run == null ? -1 : _pending.IndexOf(run);
        if (index < 0 || !run.KillsSent || !run.SurvivalSent)
        {
            LastError = "A result must be queued and acknowledged by both boards before removal.";
            return false;
        }
        _pending.RemoveAt(index);
        if (Save())
            return true;
        _pending.Insert(index, run);
        return false;
    }

    private static bool Validate(List<DemoLeaderboardPendingRun> runs, out string error)
    {
        error = string.Empty;
        if (runs.Count > Capacity)
        {
            error = "The upload queue exceeds its supported capacity.";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DemoLeaderboardPendingRun run in runs)
        {
            if (run == null || !ValidEntry(run.Entry) || !ValidProfile(run.Profile) ||
                (!string.IsNullOrEmpty(run.PlayerId) && !ValidIdentifier(run.PlayerId, 128)) ||
                ((run.KillsSent || run.SurvivalSent) && string.IsNullOrEmpty(run.PlayerId)) ||
                !ids.Add(run.Entry.RunId))
            {
                error = "The upload queue contains an invalid or duplicate result.";
                return false;
            }
        }
        return true;
    }

    private static bool ValidEntry(DemoLeaderboardEntry entry) =>
        entry != null && ValidIdentifier(entry.RunId, 128) &&
        DemoLeaderboardNames.TryNormalize(entry.Name, out _) && entry.Kills >= 0 && entry.SurvivalSeconds >= 0;

    private static bool ValidIdentifier(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            return false;
        foreach (char character in value)
            if (char.IsControl(character))
                return false;
        return true;
    }

    private static bool ValidProfile(string profile)
    {
        if (string.IsNullOrEmpty(profile) || profile.Length > 30)
            return false;
        foreach (char character in profile)
            if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                  (character >= '0' && character <= '9') || character == '_' || character == '-'))
                return false;
        return true;
    }
}
