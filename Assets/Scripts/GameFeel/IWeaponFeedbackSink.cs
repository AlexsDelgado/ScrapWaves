public interface IWeaponFeedbackSink : IWeaponPresentationSink
{
    void OnChargeStarted(in WeaponFeedbackContext context);
    void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress);
    void OnChargeCancelled(in WeaponFeedbackContext context);
    void OnShotFired(in WeaponFeedbackContext context);
    void OnSustainedFireStarted(in WeaponFeedbackContext context);
    void OnSustainedFireStopped(in WeaponFeedbackContext context);
    void OnProjectileImpact(in WeaponFeedbackContext context);
    void OnDamageConfirmed(in WeaponFeedbackContext context);
    void OnStatusApplied(in WeaponFeedbackContext context);
    void OnAmmoEmpty(in WeaponFeedbackContext context);
    void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold);
    void ConfigureProjectile(
        Projectile projectile,
        ProjectilePresentationArchetypeId archetype,
        in WeaponFeedbackContext context);
}

public static class WeaponFeedbackEmitter
{
    public static IWeaponFeedbackSink Resolve(IWeaponPresentationSink sink)
    {
        return sink as IWeaponFeedbackSink ?? NullWeaponPresentationSink.Instance;
    }

    public static WeaponFeedbackContext CreateContext(
        WeaponInstance weapon,
        HeatManager heat,
        WeaponFeedbackMode mode,
        UnityEngine.Vector3 origin,
        UnityEngine.Vector3 direction,
        bool isAbility = false,
        float intensity = 1f,
        UnityEngine.Transform target = null,
        UnityEngine.Transform anchor = null)
    {
        return new WeaponFeedbackContext(
            weapon,
            mode,
            heat != null ? heat.NormalizedHeat : 0f,
            origin,
            direction,
            isAbilityDamage: isAbility,
            eventIntensity: intensity,
            target: target,
            anchor: anchor);
    }
}
