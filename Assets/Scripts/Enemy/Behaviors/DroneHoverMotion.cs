using UnityEngine;

/// <summary>Holds a flight plane through small upward jumps and promptly settles after the player descends.</summary>
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
        float upwardDeadZone, float downwardDeadZone, float speed, float deltaTime)
    {
        if (deltaTime <= 0f)
            return currentY;
        if (!_initialized)
            SetHeight(currentY);

        // Use the reachable height so a platform cannot keep the pursuit latch active.
        float reachableTargetY = Mathf.Max(playerTargetY, minimumY);
        float heightError = reachableTargetY - currentY;
        // The downward trigger cannot sit inside the settle tolerance: that would
        // repeatedly release and reacquire pursuit before it can settle.
        bool shouldFollow = heightError > Mathf.Max(0f, upwardDeadZone)
            || heightError < -Mathf.Max(SettleDistance, downwardDeadZone);
        if (!_followingPlayer && shouldFollow)
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