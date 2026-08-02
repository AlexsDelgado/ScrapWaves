public sealed class NullWeaponPresentationSink : IWeaponFeedbackSink
{
    public static NullWeaponPresentationSink Instance { get; } = new();

    private NullWeaponPresentationSink()
    {
    }

    public void Emit(in WeaponPresentationContext context)
    {
    }

    public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
    {
        return default;
    }

    public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
    }

    public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
    }

    public void OnChargeStarted(in WeaponFeedbackContext context) { }
    public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress) { }
    public void OnChargeCancelled(in WeaponFeedbackContext context) { }
    public void OnShotFired(in WeaponFeedbackContext context) { }
    public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
    public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
    public void OnProjectileImpact(in WeaponFeedbackContext context) { }
    public void OnDamageConfirmed(in WeaponFeedbackContext context) { }
    public void OnStatusApplied(in WeaponFeedbackContext context) { }
    public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
    public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold) { }
    public void ConfigureProjectile(
        Projectile projectile,
        ProjectilePresentationArchetypeId archetype,
        in WeaponFeedbackContext context) { }
}
