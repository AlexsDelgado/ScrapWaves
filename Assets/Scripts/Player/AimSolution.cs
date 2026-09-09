using UnityEngine;

/// <summary>The intended muzzle-to-target line, shared by combat and presentation.</summary>
public readonly struct AimSolution
{
    public Vector3 Origin { get; }
    public Vector3 TargetPoint { get; }
    public Vector3 Direction { get; }
    public bool IsValid { get; }
    public int FrameNumber { get; }

    public AimSolution(Vector3 origin, Vector3 targetPoint, int frameNumber)
    {
        Origin = origin;
        TargetPoint = targetPoint;
        Vector3 delta = targetPoint - origin;
        IsValid = delta.sqrMagnitude > 0.0001f;
        Direction = IsValid ? delta.normalized : Vector3.zero;
        FrameNumber = frameNumber;
    }
}

public static class WeaponAimPolicy
{
    public static bool PreferDamageableAimPoint(WeaponInstance weapon)
    {
        if (weapon?.Data == null) return false;
        if (weapon.Data.WeaponType == WeaponType.RocketLauncher) return true;
        return weapon.Data.WeaponType == WeaponType.AutomaticCannon
            && !(weapon.HasAdvancedPath && weapon.SelectedPath == WeaponUpgradePath.PathB);
    }
}
