using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;

/// <summary>Called only through the coordinator's gate; never switches identity during a request.</summary>
public sealed class UgsDemoLeaderboardRemote : IDemoLeaderboardRemote
{
    [Serializable]
    private sealed class ScoreMetadata
    {
        public int Version;
        public string ScoreFormat;
        public string Alias;
        public string RunId;
        public int Kills;
        public int SurvivalSeconds;
        public bool Victory;
    }

    private readonly string _environment;
    private readonly Func<bool> _enabled;
    private static string _initializedEnvironment;

    public UgsDemoLeaderboardRemote(string environment, Func<bool> enabled)
    {
        _environment = environment;
        _enabled = enabled;
    }

    public async Task<string> SelectPlayerAsync(string profile, string expectedPlayerId)
    {
        if (!_enabled()) throw new OperationCanceledException();
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(_environment));
            _initializedEnvironment = _environment;
        }
        if (!_enabled()) throw new OperationCanceledException();
        if (_initializedEnvironment != _environment)
            throw new InvalidOperationException("Restart the game after changing the UGS environment.");

        var auth = AuthenticationService.Instance;
        if (auth.Profile != profile || (auth.IsSignedIn && !auth.IsAuthorized))
        {
            if (auth.IsSignedIn) auth.SignOut(false);
            if (auth.Profile != profile) auth.SwitchProfile(profile);
        }
        if (!auth.IsSignedIn)
        {
            if (!string.IsNullOrEmpty(expectedPlayerId) && !auth.SessionTokenExists)
                throw new DemoLeaderboardIdentityException("This pending player's credentials are missing. Upload retained.");
            await auth.SignInAnonymouslyAsync();
        }
        if (!string.IsNullOrEmpty(expectedPlayerId) && auth.PlayerId != expectedPlayerId)
            throw new DemoLeaderboardIdentityException("Authenticated player differs from pending score owner. Upload retained.");
        return auth.PlayerId;
    }

    public async Task SubmitAsync(string boardId, DemoLeaderboardEntry entry, bool survival)
    {
        await LeaderboardsService.Instance.AddPlayerScoreAsync(boardId,
            DemoLeaderboardScoreCodec.Encode(entry, survival), new AddPlayerScoreOptions
            {
                Metadata = new ScoreMetadata
                {
                    Version = 1, ScoreFormat = DemoLeaderboardScoreCodec.Format,
                    Alias = entry.Name, RunId = entry.RunId, Kills = entry.Kills,
                    SurvivalSeconds = entry.SurvivalSeconds, Victory = entry.Victory
                }
            });
    }

    public async Task<List<DemoLeaderboardEntry>> GetScoresAsync(string boardId, bool survival)
    {
        LeaderboardScoresPage page = await LeaderboardsService.Instance.GetScoresAsync(boardId,
            new GetScoresOptions { Offset = 0, Limit = 10, IncludeMetadata = true });
        var result = new List<DemoLeaderboardEntry>();
        if (page?.Results == null) return result;
        var aliases = new Dictionary<string, int>(DemoLeaderboardNames.IdentityComparer);
        var ids = new List<string>();
        foreach (LeaderboardEntry score in page.Results)
        {
            if (score == null)
                throw new InvalidOperationException("Global board contains an incompatible score. Check its configuration.");
            ScoreMetadata metadata = null;
            try
            {
                if (!string.IsNullOrEmpty(score.Metadata)) metadata = JsonConvert.DeserializeObject<ScoreMetadata>(score.Metadata);
            }
            catch (JsonException) { /* Reject malformed or incompatible metadata below. */ }
            if (metadata == null || metadata.ScoreFormat != DemoLeaderboardScoreCodec.Format)
                throw new InvalidOperationException("Global board uses another score format. Use the ScrapWaves demo boards.");
            DemoLeaderboardScoreCodec.Decode(score.Score, survival, out int kills, out int seconds);
            string alias = metadata != null && metadata.Version == 1 &&
                DemoLeaderboardNames.TryNormalize(metadata.Alias, out string normalized) ? normalized : "Player";
            aliases.TryGetValue(alias, out int count);
            aliases[alias] = count + 1;
            ids.Add(score.PlayerId ?? "");
            result.Add(new DemoLeaderboardEntry
            {
                RunId = metadata?.RunId, Name = alias,
                Kills = kills,
                SurvivalSeconds = seconds,
                Victory = metadata?.Victory ?? false
            });
        }
        // Names are labels, never global identity. Distinguish duplicate visible aliases without merging players.
        for (int i = 0; i < result.Count; i++)
            if (aliases[result[i].Name] > 1)
                result[i].Name = "#" + DemoLeaderboardOnlineClient.Hash(ids[i]).Substring(0, 4).ToUpperInvariant() + " " + result[i].Name;
        return result;
    }
}
