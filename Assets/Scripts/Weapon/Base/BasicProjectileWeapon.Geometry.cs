public partial class BasicProjectileWeapon
{
    // No allocations on the normal aiming path. Null deliberately retains the
    // inherited BaseRange convergence; concrete weapons opt in only when a
    // relevant manual geometry override is enabled and use their real getter.
    public virtual float? GetManualAimConvergenceDistance() => null;

    // Debug-only snapshots may allocate. An unknown weapon supplies no geometry
    // rather than presenting an unrelated legacy scalar as a gameplay boundary.
    public virtual WeaponGeometrySnapshot CaptureGeometry() => null;

    protected WeaponGeometryGuide CreateAutomaticTargetingGuide(float rangeMeters)
    {
        ConfiguredEnemyTargeting.GetAcquisitionConstraint(Runtime, out float angle, out bool bodyForward);
        return new WeaponGeometryGuide(WeaponStatMode.Automatic, WeaponGeometryPurpose.Targeting,
            angle >= 360f ? WeaponGeometryShape.Circle : WeaponGeometryShape.PlanarCone,
            WeaponGeometryAnchor.Owner, rangeMeters: rangeMeters, radiusMeters: rangeMeters,
            fullAngleDegrees: angle, direction: bodyForward ? WeaponGeometryDirection.OwnerForward : WeaponGeometryDirection.Aim);
    }
}
