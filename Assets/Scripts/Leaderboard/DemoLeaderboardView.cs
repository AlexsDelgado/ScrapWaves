using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>Populates the twenty rows authored in the title scene.</summary>
[DisallowMultipleComponent]
public sealed class DemoLeaderboardView : MonoBehaviour
{
    [Serializable]
    private sealed class Row
    {
        [SerializeField] private TMP_Text _rank;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _value;

        public bool IsValid => _rank != null && _name != null && _value != null;

        public void Show(int rank, string playerName, string value)
        {
            Set(_rank, rank.ToString(CultureInfo.InvariantCulture));
            Set(_name, playerName);
            Set(_value, value);
        }

        private static void Set(TMP_Text label, string value)
        {
            if (label == null)
                return;
            label.richText = false;
            label.text = value;
        }
    }

    [SerializeField] private GameObject _root;
    [SerializeField] private Row[] _mostKillsRows = new Row[10];
    [SerializeField] private Row[] _longestSurvivalRows = new Row[10];
    [SerializeField] private TMP_Text _status;

    private const double RefreshIntervalSeconds = 30d;
    private DemoLeaderboardSettings _settings;
    private int _refreshGeneration;
    private bool _polling;
    private bool _requestPending;
    private double _nextRefresh;

    public bool HasRequiredReferences => _root != null && ValidRows(_mostKillsRows) && ValidRows(_longestSurvivalRows);
    public bool HasStatusReference => _status != null;

    private void OnEnable()
    {
        if (_settings != null)
            Refresh(_settings);
    }

    private void OnDisable() => StopRefreshing();
    private void OnDestroy() => StopRefreshing();

    private void Update()
    {
        if (!_polling || _requestPending || Time.realtimeSinceStartupAsDouble < _nextRefresh)
            return;
        if (_settings == null || !_settings.Enabled || !_settings.UseOnlineLeaderboard)
        {
            StopRefreshing();
            return;
        }
        RefreshOnline(_refreshGeneration);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.SetActive(visible);
    }

    /// <summary>Starts a menu refresh without delaying navigation or gameplay.</summary>
    public void Refresh(DemoLeaderboardSettings settings)
    {
        StopRefreshing();
        _settings = settings;
        if (!Application.isPlaying || !isActiveAndEnabled || settings == null || !settings.Enabled)
            return;
        if (!settings.UseOnlineLeaderboard)
        {
            RefreshFromStore();
            return;
        }

        ShowData(null);
        ShowStatus("GLOBAL \u2022 Connecting...");
        _nextRefresh = Time.realtimeSinceStartupAsDouble;
        _polling = true;
    }

    public void RefreshFromStore()
    {
        ShowData(new DemoLeaderboardStore().Load());
        ShowStatus("LOCAL \u2022 This PC only");
    }

    public void ShowOnlineSnapshot(DemoLeaderboardOnlineSnapshot snapshot)
    {
        // Offline data must never appear to be the global ranking.
        ShowData(snapshot != null && snapshot.IsGlobal ? snapshot.Data : null);
        ShowStatus(string.IsNullOrWhiteSpace(snapshot?.Status) ? "GLOBAL \u2022 Unavailable. Retrying..." : snapshot.Status);
    }

    public void ShowStatus(string status)
    {
        if (_status == null)
            return;
        _status.richText = false;
        _status.text = status ?? string.Empty;
    }

    private async void RefreshOnline(int generation)
    {
        _requestPending = true;
        try
        {
            DemoLeaderboardOnlineSnapshot snapshot = await DemoLeaderboardOnline.RefreshAsync();
            if (CanApplyRefresh(generation))
                ShowOnlineSnapshot(snapshot);
        }
        catch (Exception)
        {
            if (CanApplyRefresh(generation))
                ShowOnlineSnapshot(null);
        }
        finally
        {
            if (this != null && generation == _refreshGeneration)
            {
                _requestPending = false;
                _nextRefresh = Time.realtimeSinceStartupAsDouble + RefreshIntervalSeconds;
            }
        }
    }

    private bool CanApplyRefresh(int generation) => this != null && generation == _refreshGeneration &&
        Application.isPlaying && isActiveAndEnabled && _settings != null && _settings.Enabled && _settings.UseOnlineLeaderboard;

    private void StopRefreshing()
    {
        _refreshGeneration++;
        _polling = false;
        _requestPending = false;
    }

    public void ShowData(DemoLeaderboardData data)
    {
        ShowRows(_mostKillsRows, data?.MostKills, false);
        ShowRows(_longestSurvivalRows, data?.LongestSurvival, true);
    }

    public static string FormatSurvivalTime(double seconds)
    {
        long totalSeconds = double.IsNaN(seconds) || double.IsInfinity(seconds)
            ? 0L
            : (long)Math.Max(0d, Math.Floor(seconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", totalSeconds / 60L, totalSeconds % 60L);
    }

    private static void ShowRows(Row[] rows, IList<DemoLeaderboardEntry> entries, bool survival)
    {
        if (rows == null)
            return;
        for (int index = 0; index < rows.Length; index++)
        {
            DemoLeaderboardEntry entry = entries != null && index < entries.Count ? entries[index] : null;
            rows[index]?.Show(index + 1, entry?.Name ?? "—", entry == null ? "—" : survival
                ? FormatSurvivalTime(entry.SurvivalSeconds)
                : entry.Kills.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool ValidRows(Row[] rows)
    {
        if (rows == null || rows.Length != 10)
            return false;
        for (int index = 0; index < rows.Length; index++)
            if (rows[index] == null || !rows[index].IsValid)
                return false;
        return true;
    }
}
