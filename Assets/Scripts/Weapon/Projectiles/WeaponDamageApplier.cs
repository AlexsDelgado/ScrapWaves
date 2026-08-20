using UnityEngine;

public static class WeaponDamageApplier
{
    public static DamageApplicationResult ApplyDamage(
        IDamageable damageable,
        int requestedDamage,
        DamageChannel channel = DamageChannel.Direct,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        bool canTriggerLifesteal = true)
    {
        if (damageable == null || requestedDamage <= 0)
            return default;

        int modifiedDamage = Mathf.Max(
            1,
            WeaponDamageAmplifierStatus.ModifyDamage(damageable, requestedDamage));
        DamageRequest request = new(
            requestedDamage,
            modifiedDamage,
            channel,
            statusKind,
            canTriggerLifesteal);
        DamageApplicationResult result = damageable.ApplyDamage(in request);

        if (result.Applied && result.AppliedDamage > 0 && request.CanTriggerLifesteal)
            PlayerCombatHooks.TryLifesteal(result.AppliedDamage);

        return result;
    }

    public static bool TryApplyDamage(IDamageable damageable, int damage)
    {
        return ApplyDamage(damageable, damage, DamageChannel.Direct).Applied;
    }
}
