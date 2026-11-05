using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Remembers the next player's alias on this installation. It never renames recorded runs,
/// changes authentication profiles, or edits the leaderboard and its upload queue.
/// </summary>
public sealed class DemoLeaderboardPlayerProfile
{
    public const string FileName = "scrapwaves_demo_player.json";
    private const int CurrentVersion = 1;

    [Serializable]
    private sealed class FileData
    {
        public int Version;
        public string Name;
    }

    private readonly string _path;
    private bool _writeBlocked;
    private bool _preserveInvalidFile;

    public string Name { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(DemoLeaderboardStore.DefaultPath), FileName);

    public DemoLeaderboardPlayerProfile(string path = null)
    {
        string json;
        try
        {
            _path = Path.GetFullPath(path ?? DefaultPath);
            if (Directory.Exists(_path))
                throw new IOException("The player name location is a directory.");
            if (!File.Exists(_path))
                return;
            json = File.ReadAllText(_path);
        }
        catch (Exception exception)
        {
            _writeBlocked = true;
            LastError = "The saved player name could not be read: " + exception.Message;
            return;
        }

        try
        {
            JObject data = JObject.Parse(json);
            if (data["Version"]?.Type != JTokenType.Integer || data["Name"]?.Type != JTokenType.String)
                throw new InvalidDataException("Missing player name or version.");
            int version = data.Value<int>("Version");
            if (version > CurrentVersion)
            {
                _writeBlocked = true;
                LastError = "The saved player name uses a newer version and was preserved.";
                return;
            }
            if (version != CurrentVersion || !DemoLeaderboardNames.TryNormalize(data.Value<string>("Name"), out string name))
                throw new InvalidDataException("Invalid saved player name.");
            Name = name;
        }
        catch (Exception exception)
        {
            _preserveInvalidFile = true;
            LastError = "The saved player name is invalid. Enter a name again: " + exception.Message;
        }
    }

    /// <summary>Publishes the normalized name only after an atomic save succeeds.</summary>
    public bool TrySave(string name)
    {
        if (_writeBlocked)
            return false;
        if (!DemoLeaderboardNames.TryNormalize(name, out string normalized))
        {
            LastError = "Enter a name (1-16 characters).";
            return false;
        }

        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string json = JsonUtility.ToJson(new FileData { Version = CurrentVersion, Name = normalized }, true);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(_path))
            {
                if (_preserveInvalidFile)
                    File.Copy(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak");
                File.Replace(temporaryPath, _path, null);
            }
            else
                File.Move(temporaryPath, _path);

            Name = normalized;
            _preserveInvalidFile = false;
            LastError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            LastError = "The player name could not be saved: " + exception.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception) { /* Keep the original save error and allow the menu to recover. */ }
        }
    }
}
