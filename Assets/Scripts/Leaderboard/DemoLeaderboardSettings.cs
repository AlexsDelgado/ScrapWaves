using System;
using UnityEngine;

/// <summary>The single scene-authored switch for the demonstration leaderboard.</summary>
[DisallowMultipleComponent]
public sealed class DemoLeaderboardSettings : MonoBehaviour
{
    [SerializeField, Tooltip("Turn off to hide the boards, skip name entry and keep testing runs out of the leaderboard.")]
    private bool _enableDemoLeaderboard = true;

    private bool _lastNotifiedValue;
    public bool Enabled => _enableDemoLeaderboard;
    public event Action<bool> Changed;

    private void Awake() => _lastNotifiedValue = Enabled;

    private void Update()
    {
        if (_lastNotifiedValue != Enabled)
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
        Changed?.Invoke(Enabled);
    }
}
