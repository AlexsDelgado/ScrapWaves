using UnityEngine;

/// <summary>Holds a flight plane through small jumps, then settles after a meaningful height change.</summary>
internal struct DroneHoverMotion
{
    private const float SettleDistance = 0.1f;
    private bool _initialized;
    private bool _followingPlayer;
    private float _targetY;

    public void SetHeight(float height)
    {
        _initialized = true;
        _followingPlayer = false;
        _targetY = height;
    }

    public float Step(float currentY, float playerTargetY, float minimumY,
        float deadZone, float speed, float deltaTime)
    {
        if (deltaTime <= 0f)
            return currentY;
        if (!_initialized)
            SetHeight(currentY);

        // Use the reachable height so a platform cannot keep the pursuit latch active.
        float reachableTargetY = Mathf.Max(playerTargetY, minimumY);
        if (!_followingPlayer && Mathf.Abs(reachableTargetY - currentY) > Mathf.Max(0f, deadZone))
            _followingPlayer = true;
        if (_followingPlayer)
            _targetY = reachableTargetY;

        // Terrain clearance remains active even inside the player's dead zone.
        float safeTargetY = Mathf.Max(_targetY, minimumY);
        float smoothedY = Mathf.Lerp(currentY, safeTargetY, 1f - Mathf.Exp(-6f * deltaTime));
        float nextY = Mathf.MoveTowards(currentY, smoothedY, Mathf.Max(0f, speed) * deltaTime);
        if (_followingPlayer && Mathf.Abs(safeTargetY - nextY) <= SettleDistance)
            _followingPlayer = false;
        return nextY;
    }
}