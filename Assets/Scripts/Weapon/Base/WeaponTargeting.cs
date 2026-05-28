using UnityEngine;

public interface IWeaponTargeting
{
    bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target);
}

public sealed class ConfiguredEnemyTargeting : IWeaponTargeting
{
    // Resolves off-hand automatic targets from the weapon asset's targeting mode.
    public bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target)
    {
        target = null;
        if (weapon?.Data == null || owner == null)
            return false;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            aimDirection = owner.forward;

        return weapon.Data.AutoTargetingMode switch
        {
            WeaponTargetingMode.RandomInRange => EnemyRegistry.TryGetRandomOnPlaneInCone(owner.position, aimDirection, range, 90f, out target),
            WeaponTargetingMode.IgnoreCameraClosest => EnemyRegistry.TryGetClosestOnPlane(owner.position, range, out target),
            _ => EnemyRegistry.TryGetClosestOnPlaneInCone(owner.position, aimDirection, range, 90f, out target)
        };
    }
}
