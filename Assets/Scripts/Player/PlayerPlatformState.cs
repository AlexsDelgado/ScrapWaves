using UnityEngine;

/// <summary>Upper occupancy and confirmed Base support are independent, including during descent.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerHealth))]
public sealed class PlayerPlatformState : MonoBehaviour
{
    [SerializeField] private UpperLevelVolume _upperLevelVolume;
    private PlayerMovement _movement;
    private PlayerHealth _health;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _health = GetComponent<PlayerHealth>();
    }

    public bool IsConfigured => _upperLevelVolume != null && _upperLevelVolume.IsAvailable && _upperLevelVolume.IsConfigured;
    public bool IsRunActive => isActiveAndEnabled && _health != null && _health.isActiveAndEnabled &&
        _health.IsAlive && (GameManager.Instance == null || GameManager.Instance.IsPlaying);
    public bool IsSimulationActive => IsRunActive && Time.timeScale > 0f &&
        !GameplayPause.IsUiPaused && !EntranceCinematic.StartupHeld;
    public Vector3 FeetPosition => _movement != null ? _movement.FeetPosition : transform.position;
    public bool IsUpperPosition(Vector3 worldPoint) => _upperLevelVolume != null && _upperLevelVolume.Contains(worldPoint);
    public bool IsInsideUpper => IsUpperPosition(FeetPosition);
    public bool IsBaseSupported => _movement != null && _movement.isActiveAndEnabled &&
        _upperLevelVolume != null && _upperLevelVolume.IsAvailable && !IsInsideUpper &&
        _movement.TryGetGroundSupport(out Collider support) && _upperLevelVolume.IsBaseSupport(support);
    public bool CanSpawnObjectives => IsSimulationActive && IsBaseSupported;
}
