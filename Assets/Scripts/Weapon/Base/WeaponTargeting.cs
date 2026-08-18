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

        bool fullCircle = weapon.Data.AutomaticAimConstraint == WeaponAutomaticAimConstraint.Full360
            || weapon.Data.AutoTargetingMode == WeaponTargetingMode.IgnoreCameraClosest;
        bool random = weapon.Data.AutoTargetingMode == WeaponTargetingMode.RandomInRange;

        if (fullCircle)
        {
            return random
                ? EnemyRegistry.TryGetRandomOnPlane(owner.position, range, out target)
                : EnemyRegistry.TryGetClosestOnPlane(owner.position, range, out target);
        }

        return random
            ? EnemyRegistry.TryGetRandomOnPlaneInCone(owner.position, aimDirection, range, 90f, out target)
            : EnemyRegistry.TryGetClosestOnPlaneInCone(owner.position, aimDirection, range, 90f, out target);
    }
}
