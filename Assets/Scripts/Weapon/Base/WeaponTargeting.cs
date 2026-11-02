using UnityEngine;

public interface IWeaponTargeting
{
    bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target);
}

public sealed class ConfiguredEnemyTargeting : IWeaponTargeting
{
    /// <summary>ΔY máximo para auto-aim offhand (alineado con PlayerAutoAttack).</summary>
    public const float DefaultMaxAimVerticalDelta = 2.5f;

    // Shared by targeting and its observation-only geometry guide.
    public static void GetAcquisitionConstraint(WeaponInstance weapon, out float fullAngle, out bool bodyForward)
    {
        bodyForward = weapon?.Data != null && weapon.Data.AutomaticAimConstraint == WeaponAutomaticAimConstraint.BodyForward180;
        bool fullCircle = weapon?.Data != null && (weapon.Data.AutomaticAimConstraint == WeaponAutomaticAimConstraint.Full360
            || weapon.Data.AutoTargetingMode == WeaponTargetingMode.IgnoreCameraClosest);
        fullAngle = bodyForward ? 180f : fullCircle ? 360f : 90f;
    }

    // Resolves off-hand automatic targets from the weapon asset's targeting mode.
    public bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target)
    {
        target = null;
        if (weapon?.Data == null || owner == null)
            return false;

        if (aimDirection.sqrMagnitude <= 0.0001f)
            aimDirection = owner.forward;

        GetAcquisitionConstraint(weapon, out float fullAngle, out bool bodyForward);
        bool random = weapon.Data.AutoTargetingMode == WeaponTargetingMode.RandomInRange;
        float maxDy = DefaultMaxAimVerticalDelta;

        if (bodyForward)
        {
            return random
                ? EnemyRegistry.TryGetRandomOnPlaneInCone(owner.position, owner.forward, range, 180f, out target)
                : EnemyRegistry.TryGetClosestOnPlaneInCone(owner.position, owner.forward, range, 180f, out target);
        }

        if (fullAngle >= 360f)
        {
            return random
                ? EnemyRegistry.TryGetRandomOnPlaneWithinVerticalDelta(owner.position, range, maxDy, out target)
                : EnemyRegistry.TryGetClosestOnPlaneWithinVerticalDelta(owner.position, range, maxDy, out target);
        }

        return random
            ? EnemyRegistry.TryGetRandomOnPlaneInConeWithinVerticalDelta(owner.position, aimDirection, range, 90f, maxDy, out target)
            : EnemyRegistry.TryGetClosestOnPlaneInConeWithinVerticalDelta(owner.position, aimDirection, range, 90f, maxDy, out target);
    }
}
