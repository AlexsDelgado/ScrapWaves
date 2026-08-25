using UnityEngine;

/// <summary>
/// Refreshes all combat-text billboards once after the gameplay camera has moved.
/// One driver serves the entire fixed pool, avoiding a LateUpdate callback per number.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class CombatTextWorldRenderDriver : MonoBehaviour
{
    private CombatTextDirector _director;

    public void Initialize(CombatTextDirector director) => _director = director;

    private void LateUpdate() => _director?.RefreshRenderPoses();
}
