using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class DemoLeaderboardEntry
{
    public string RunId;
    public string Name;
    public int Kills;
    public int SurvivalSeconds;
    public long CompletedOrder;
    public bool Victory;
}

[Serializable]
public sealed class DemoLeaderboardData
{
    public int Version = 1;
    public List<DemoLeaderboardEntry> MostKills = new();
    public List<DemoLeaderboardEntry> LongestSurvival = new();
    public long NextOrder = 1;
}

public static class DemoLeaderboardNames
{
    public const int MaximumLength = 16;
    public static readonly StringComparer IdentityComparer = StringComparer.OrdinalIgnoreCase;

    public static bool TryNormalize(string input, out string displayName)
    {
        displayName = string.Empty;
        if (string.IsNullOrEmpty(input)) return false;

        string normalized;
        try { normalized = input.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }

        var result = new StringBuilder(MaximumLength);
        bool pendingSpace = false;
        foreach (char character in normalized)
        {
            UnicodeCategory category = char.GetUnicodeCategory(character);
            if (char.IsControl(character) || category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator ||
                category == UnicodeCategory.ParagraphSeparator)
                return false;
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace) result.Append(' ');
            result.Append(character);
            pendingSpace = false;
            if (result.Length > MaximumLength) return false;
        }

        if (result.Length == 0) return false;
        displayName = result.ToString();
        return true;
    }
}

/// <summary>Local demo scores only. This file never reads or resets meta progression.</summary>
public sealed class DemoLeaderboardStore
{
    public const int CurrentVersion = 1;
    public const int Capacity = 10;
    public const string FileName = "scrapwaves_demo_leaderboard.json";

    private readonly string _path;
    private DemoLeaderboardData _data;
    private bool _loaded;
    private bool _writeBlocked;
    private bool _preserveCorruptFile;

    public string LastError { get; private set; } = string.Empty;

    public static string DefaultPath
    {
        get
        {
            string directory = Application.persistentDataPath;
#if UNITY_EDITOR
            string testDirectory = Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(testDirectory)) directory = testDirectory;
#endif
            return Path.Combine(directory, FileName);
        }
    }

    public DemoLeaderboardStore(string path = null)
    {
        try { _path = Path.GetFullPath(path ?? DefaultPath); }
        catch (Exception exception)
        {
            _writeBlocked = true;
            LastError = "The leaderboard location is unavailable: " + exception.Message;
        }
    }

    /// <summary>Returns a detached snapshot. Opening a store never writes a file.</summary>
    public DemoLeaderboardData Load()
    {
        EnsureLoaded();
        return Clone(_data);
    }

    /// <summary>
    /// Accepts one completed run. True also means a valid run did not improve either board.
    /// The caller owns the per-run submission guard; retained run IDs also prevent duplicates.
    /// </summary>
    public bool Submit(DemoLeaderboardEntry entry)
    {
        EnsureLoaded();
        if (_writeBlocked) return false;
        if (!IsValidEntry(entry, requireOrder: false, out string name))
        {
            LastError = "The leaderboard result has an invalid name, run ID, or score.";
            return false;
        }
        if (ContainsRun(_data.MostKills, entry.RunId) || ContainsRun(_data.LongestSurvival, entry.RunId))
        {
            LastError = "This run has already been recorded.";
            return false;
        }
        if (_data.NextOrder == long.MaxValue)
        {
            LastError = "The leaderboard completion sequence is full.";
            return false;
        }

        DemoLeaderboardData next = Clone(_data);
        DemoLeaderboardEntry result = Clone(entry);
        result.Name = name;
        result.CompletedOrder = next.NextOrder++;
        next.MostKills.Add(Clone(result));
        next.LongestSurvival.Add(Clone(result));
        next.MostKills = Rank(next.MostKills, survivalFirst: false);
        next.LongestSurvival = Rank(next.LongestSurvival, survivalFirst: true);
        return Save(next);
    }

    /// <summary>Clears only this store's leaderboard, preserving unrelated saves and preferences.</summary>
    public bool Clear()
    {
        EnsureLoaded();
        return !_writeBlocked && Save(new DemoLeaderboardData());
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _data = new DemoLeaderboardData();
        if (_writeBlocked) return;

        string json;
        try
        {
            if (Directory.Exists(_path))
                throw new IOException("The leaderboard file location is a directory.");
            if (!File.Exists(_path)) return;
            json = File.ReadAllText(_path);
        }
        catch (Exception exception)
        {
            _writeBlocked = true;
            LastError = "The leaderboard could not be read; its file was preserved: " + exception.Message;
            return;
        }

        DemoLeaderboardData loaded;
        try
        {
            loaded = JsonUtility.FromJson<DemoLeaderboardData>(json);
            if (loaded == null || loaded.Version < 1)
                throw new ArgumentException("Missing or invalid leaderboard data version.");
        }
        catch (Exception exception)
        {
            _preserveCorruptFile = true;
            LastError = "The leaderboard file is invalid. It is preserved and will be backed up before a new save: " + exception.Message;
            return;
        }

        if (loaded.Version > CurrentVersion)
        {
            _writeBlocked = true;
            LastError = "This leaderboard uses a newer version. Its file has been preserved.";
            return;
        }

        _data.MostKills = Rank(loaded.MostKills, survivalFirst: false);
        _data.LongestSurvival = Rank(loaded.LongestSurvival, survivalFirst: true);
        long highestOrder = 0;
        foreach (DemoLeaderboardEntry entry in _data.MostKills)
            highestOrder = Math.Max(highestOrder, entry.CompletedOrder);
        foreach (DemoLeaderboardEntry entry in _data.LongestSurvival)
            highestOrder = Math.Max(highestOrder, entry.CompletedOrder);
        _data.NextOrder = Math.Max(Math.Max(1, loaded.NextOrder), highestOrder + 1);
    }

    private bool Save(DemoLeaderboardData next)
    {
        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(next, true), new UTF8Encoding(false));
            if (File.Exists(_path))
            {
                if (_preserveCorruptFile)
                {
                    string backupPath = _path + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak";
                    File.Copy(_path, backupPath, overwrite: false);
                }
                File.Replace(temporaryPath, _path, null);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }

            _data = next;
            _preserveCorruptFile = false;
            LastError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            LastError = "The leaderboard could not be saved: " + exception.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception) { /* Cleanup must not prevent returning to the game. */ }
        }
    }

    private static List<DemoLeaderboardEntry> Rank(List<DemoLeaderboardEntry> source, bool survivalFirst)
    {
        var valid = new List<DemoLeaderboardEntry>();
        if (source != null)
        {
            foreach (DemoLeaderboardEntry entry in source)
            {
                if (!IsValidEntry(entry, requireOrder: true, out string name)) continue;
                DemoLeaderboardEntry copy = Clone(entry);
                copy.Name = name;
                valid.Add(copy);
            }
        }
        valid.Sort((left, right) => Compare(left, right, survivalFirst));

        var names = new HashSet<string>(DemoLeaderboardNames.IdentityComparer);
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<DemoLeaderboardEntry>(Capacity);
        foreach (DemoLeaderboardEntry entry in valid)
        {
            if (names.Contains(entry.Name) || runIds.Contains(entry.RunId)) continue;
            names.Add(entry.Name);
            runIds.Add(entry.RunId);
            ranked.Add(entry);
            if (ranked.Count == Capacity) break;
        }
        return ranked;
    }

    private static int Compare(DemoLeaderboardEntry left, DemoLeaderboardEntry right, bool survivalFirst)
    {
        int primary = survivalFirst
            ? right.SurvivalSeconds.CompareTo(left.SurvivalSeconds)
            : right.Kills.CompareTo(left.Kills);
        if (primary != 0) return primary;
        int secondary = survivalFirst
            ? right.Kills.CompareTo(left.Kills)
            : right.SurvivalSeconds.CompareTo(left.SurvivalSeconds);
        if (secondary != 0) return secondary;
        int order = left.CompletedOrder.CompareTo(right.CompletedOrder);
        if (order != 0) return order;
        int name = StringComparer.Ordinal.Compare(left.Name, right.Name);
        return name != 0 ? name : StringComparer.Ordinal.Compare(left.RunId, right.RunId);
    }

    private static bool IsValidEntry(DemoLeaderboardEntry entry, bool requireOrder, out string name)
    {
        name = string.Empty;
        if (entry == null || !DemoLeaderboardNames.TryNormalize(entry.Name, out name) ||
            string.IsNullOrWhiteSpace(entry.RunId) || entry.RunId.Length > 128 ||
            entry.Kills < 0 || entry.SurvivalSeconds < 0)
            return false;
        foreach (char character in entry.RunId)
            if (char.IsControl(character)) return false;
        return !requireOrder || (entry.CompletedOrder > 0 && entry.CompletedOrder < long.MaxValue);
    }

    private static bool ContainsRun(List<DemoLeaderboardEntry> entries, string runId)
    {
        foreach (DemoLeaderboardEntry entry in entries)
            if (string.Equals(entry.RunId, runId, StringComparison.Ordinal)) return true;
        return false;
    }

    private static DemoLeaderboardEntry Clone(DemoLeaderboardEntry entry) => new()
    {
        RunId = entry.RunId,
        Name = entry.Name,
        Kills = entry.Kills,
        SurvivalSeconds = entry.SurvivalSeconds,
        CompletedOrder = entry.CompletedOrder,
        Victory = entry.Victory
    };

    private static DemoLeaderboardData Clone(DemoLeaderboardData data)
    {
        var clone = new DemoLeaderboardData { Version = data.Version, NextOrder = data.NextOrder };
        foreach (DemoLeaderboardEntry entry in data.MostKills) clone.MostKills.Add(Clone(entry));
        foreach (DemoLeaderboardEntry entry in data.LongestSurvival) clone.LongestSurvival.Add(Clone(entry));
        return clone;
    }
}
