using System;
using UnityEngine;

/// <summary>A named demo attempt, carried across scene loads without creating runtime objects.</summary>
public static class DemoLeaderboardSession
{
    private static string _pendingName;
    private static bool _pendingLoadValidated;
    private static string _activeName;
    private static string _activeRunId;
    private static bool _ended;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetForMenu();

    /// <summary>Called only after name confirmation; null keeps the existing unranked play flow.</summary>
    public static void PrepareMenuLaunch(string name)
    {
        ResetForMenu();
        if (DemoLeaderboardNames.TryNormalize(name, out string displayName))
            _pendingName = displayName;
    }

    public static void CancelPendingLaunch()
    {
        _pendingName = null;
        _pendingLoadValidated = false;
    }

    public static void ResetForMenu()
    {
        CancelPendingLaunch();
        _activeName = null;
        _activeRunId = null;
        _ended = false;
    }

    // SceneNavigation calls this after validating its destination, before invoking the load.
    // Merely entering a name cannot qualify a later direct gameplay or sandbox scene entry.
    internal static void PrepareSceneLoad(SceneDestination destination, string sourceSceneName)
    {
        if (destination != SceneDestination.Play)
        {
            ResetForMenu();
            return;
        }

        if (!string.Equals(sourceSceneName, SceneNavigation.TitleSceneName, StringComparison.Ordinal))
        {
            CancelPendingLaunch();
            return;
        }

        _pendingLoadValidated = !string.IsNullOrEmpty(_pendingName);
    }

    /// <summary>Retry belongs to the same player but always becomes a distinct attempt.</summary>
    public static void PrepareRetry()
    {
        _pendingName = _activeName;
        _pendingLoadValidated = !string.IsNullOrEmpty(_pendingName);
    }

    /// <summary>Consumes one validated menu launch or retry from the new gameplay GameManager.</summary>
    public static void BeginGameplay()
    {
        _activeName = _pendingLoadValidated ? _pendingName : null;
        _activeRunId = string.IsNullOrEmpty(_activeName) ? null : Guid.NewGuid().ToString("N");
        _ended = false;
        CancelPendingLaunch();
    }

    /// <summary>Captures the same counters as results exactly once; tutorial-only attempts do not rank.</summary>
    public static void SubmitResult(bool victory)
    {
        if (_ended)
            return;

        _ended = true;
        if (string.IsNullOrEmpty(_activeRunId) || TutorialDirector.IsActive)
            return;

        float elapsed = RunSessionStats.ElapsedSeconds;
        if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0f)
            return;

        var entry = new DemoLeaderboardEntry
        {
            RunId = _activeRunId,
            Name = _activeName,
            Kills = Mathf.Max(0, RunCombatStats.EnemiesEliminated),
            SurvivalSeconds = (int)Math.Min((double)elapsed, int.MaxValue),
            Victory = victory
        };
        var store = new DemoLeaderboardStore();
        if (!store.Submit(entry) && !string.IsNullOrEmpty(store.LastError))
            Debug.LogWarning($"Demo leaderboard could not save this run: {store.LastError}");
    }
}
