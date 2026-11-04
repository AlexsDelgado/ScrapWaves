using System;
using UnityEngine;

/// <summary>The single scene-authored switch for the demonstration leaderboard.</summary>
[DisallowMultipleComponent]
public sealed class DemoLeaderboardSettings : MonoBehaviour
{
    [SerializeField, Tooltip("Turn off to hide the boards, skip name entry and keep testing runs out of the leaderboard.")]
    private bool _enableDemoLeaderboard = true;

    [Header("Shared online leaderboard")]
    [SerializeField, Tooltip("Online shows the shared UGS boards. Turn off only for an explicitly labelled local demo.")]
    private bool _useOnlineLeaderboard = true;
    [SerializeField, Tooltip("Existing UGS environment name. Use the same project, environment and board IDs on every demo PC.")]
    private string _environmentName = "production";
    [SerializeField] private string _killsLeaderboardId = "demo_most_kills";
    [SerializeField] private string _survivalLeaderboardId = "demo_time_survived";
    [SerializeField, Tooltip("Opt in only for manual integration checks. Automated Editor tests must never create UGS players or write live scores.")]
    private bool _allowOnlineInEditor;

    private bool _lastNotifiedValue;
    private (bool, string, string, string, bool) _lastConfiguration;
    private (bool, string, string, string, bool) Configuration =>
        (_useOnlineLeaderboard, _environmentName, _killsLeaderboardId, _survivalLeaderboardId, _allowOnlineInEditor);
    public bool Enabled => _enableDemoLeaderboard;
    public bool UseOnlineLeaderboard => _useOnlineLeaderboard;
    public string EnvironmentName => _environmentName;
    public string KillsLeaderboardId => _killsLeaderboardId;
    public string SurvivalLeaderboardId => _survivalLeaderboardId;
    public bool AllowOnlineInEditor => _allowOnlineInEditor;
    public event Action<bool> Changed;

    private void Awake()
    {
        _lastNotifiedValue = Enabled;
        _lastConfiguration = Configuration;
    }

    private void Update()
    {
        if (_lastNotifiedValue != Enabled || !_lastConfiguration.Equals(Configuration))
            NotifyChanged();
    }

    public void SetEnabled(bool value)
    {
        if (_enableDemoLeaderboard == value)
            return;
        _enableDemoLeaderboard = value;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        _lastNotifiedValue = Enabled;
        _lastConfiguration = Configuration;
        Changed?.Invoke(Enabled);
    }
}
