using UnityEngine;

public readonly struct DamageRequest
{
    public readonly int RequestedDamage;
    public readonly int ModifiedDamage;
    public readonly DamageChannel Channel;
    public readonly WeaponStatusKind StatusKind;
    public readonly bool CanTriggerLifesteal;

    public DamageRequest(
        int requestedDamage,
        int modifiedDamage,
        DamageChannel channel,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        bool canTriggerLifesteal = true)
    {
        RequestedDamage = Mathf.Max(0, requestedDamage);
        ModifiedDamage = Mathf.Max(0, modifiedDamage);
        Channel = channel;
        StatusKind = statusKind;
        CanTriggerLifesteal = canTriggerLifesteal;
    }
}
