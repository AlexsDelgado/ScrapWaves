using UnityEngine;

public static class WeaponDamageApplier
{
    public static bool TryApplyDamage(IDamageable damageable, int damage)
    {
        if (damageable == null || damage <= 0)
            return false;

        int modifiedDamage = WeaponDamageAmplifierStatus.ModifyDamage(damageable, damage);
        return damageable.ApplyDamage(Mathf.Max(1, modifiedDamage));
    }
}
