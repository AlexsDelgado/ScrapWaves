using UnityEngine;

/// <summary>Sandbox controls exercise the same charge sequence as the gameplay interaction.</summary>
[DisallowMultipleComponent]
public sealed class CompactorEscapeTestController : MonoBehaviour
{
    [SerializeField] private ExitDoor _exitDoor;
    [SerializeField] private LevelExitObjective _objective;
    public ExitDoor Door => _exitDoor;
    public bool IsCharging => _exitDoor != null && _exitDoor.State == ExitDoorState.Charging;
    public string Status => _exitDoor == null ? "Compactor unavailable" :
        _exitDoor.State == ExitDoorState.Ready || _exitDoor.State == ExitDoorState.Used
            ? "100% — Ready to leave (door held open)"
            : _exitDoor.State == ExitDoorState.Charging
                ? $"Opening: {_exitDoor.ChargeNormalized:P0}"
                : "Closed — Start Escape Test to open";

    public void Configure(ExitDoor door, LevelExitObjective objective)
    {
        _exitDoor = door;
        _objective = objective;
    }

    public void StartSequence()
    {
        if (_exitDoor == null || _objective == null || IsCharging || Time.timeScale <= 0f)
            return;
        // The test grants the required keys; normal gameplay still collects them from bosses.
        while (!_objective.AllKeysCollected)
            _objective.RegisterKey();
        _exitDoor.ResetCharge();
        _exitDoor.TryStartCharging();
    }

    public void ResetSequence()
    {
        _exitDoor?.ResetCharge();
    }
}
