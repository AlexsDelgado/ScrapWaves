using UnityEngine;

/// <summary>
/// Optional extension for feedback sinks that need an explicit end boundary for
/// an exact periodic-status tally. Status producers call this before replacing
/// an instance, rolling to another segment, or ending the status.
/// </summary>
public interface ICombatTextStatusLifecycleSink
{
    void OnStatusSegmentClosed(
        Transform target,
        WeaponStatusKind statusKind,
        int statusInstanceId,
        int segmentIndex);
}
