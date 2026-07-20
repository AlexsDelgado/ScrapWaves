using UnityEngine;

public static class WeaponDamageApplier
{
    public static bool TryApplyDamage(IDamageable damageable, int damage)
    {
        if (damageable == null || damage <= 0)
            return false;

        int modifiedDamage = Mathf.Max(1, WeaponDamageAmplifierStatus.ModifyDamage(damageable, damage));
        bool applied = damageable.ApplyDamage(modifiedDamage);
        if (applied)
            PlayerCombatHooks.TryLifesteal(modifiedDamage);

        return applied;
    }
}
