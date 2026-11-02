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

    public bool HasRequiredReferences => _root != null && ValidRows(_mostKillsRows) && ValidRows(_longestSurvivalRows);

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.SetActive(visible);
    }

    public void RefreshFromStore() => ShowData(new DemoLeaderboardStore().Load());

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
