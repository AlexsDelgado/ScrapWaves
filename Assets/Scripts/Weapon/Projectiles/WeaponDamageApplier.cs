using UnityEngine;

public static class WeaponDamageApplier
{
    /// <summary>WeaponId activo opcional para crédito de kill cuando el call site no lo pasa explícito.</summary>
    public static string PendingSourceWeaponId { get; set; }

    public static DamageApplicationResult ApplyDamage(
        IDamageable damageable,
        int requestedDamage,
        DamageChannel channel = DamageChannel.Direct,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        bool canTriggerLifesteal = true,
        string sourceWeaponId = null)
    {
        if (damageable == null || requestedDamage <= 0)
            return default;

        string weaponId = !string.IsNullOrEmpty(sourceWeaponId) ? sourceWeaponId : PendingSourceWeaponId;

        int modifiedDamage = Mathf.Max(
            1,
            WeaponDamageAmplifierStatus.ModifyDamage(damageable, requestedDamage));
        DamageRequest request = new(
            requestedDamage,
            modifiedDamage,
            channel,
            statusKind,
            canTriggerLifesteal,
            weaponId);
        DamageApplicationResult result = damageable.ApplyDamage(in request);

        if (!string.IsNullOrEmpty(weaponId))
            NotifyEnemyWeaponCredit(damageable, weaponId);

        if (result.Applied && result.AppliedDamage > 0)
        {
            ChallengeProgressTracker.NotifyDamageInstance(result.AppliedDamage);
            if (request.CanTriggerLifesteal)
                PlayerCombatHooks.TryLifesteal(result.AppliedDamage);
        }

        return result;
    }

    public static bool TryApplyDamage(IDamageable damageable, int damage)
    {
        return ApplyDamage(damageable, damage, DamageChannel.Direct).Applied;
    }

    public static void SetPendingWeapon(WeaponInstance weapon)
    {
        PendingSourceWeaponId = weapon?.Data != null ? weapon.Data.WeaponId : null;
    }

    public static void SetPendingWeapon(WeaponData data)
    {
        PendingSourceWeaponId = data != null ? data.WeaponId : null;
    }

    public static void ClearPendingWeapon()
    {
        PendingSourceWeaponId = null;
    }

    private static void NotifyEnemyWeaponCredit(IDamageable damageable, string weaponId)
    {
        if (damageable is EnemyHealth health)
        {
            health.NotifyDamagedByWeapon(weaponId);
            return;
        }

        if (damageable is Component component)
            component.GetComponentInParent<EnemyHealth>()?.NotifyDamagedByWeapon(weaponId);
    }
}
