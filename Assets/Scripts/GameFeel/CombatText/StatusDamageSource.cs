using UnityEngine;

/// <summary>
/// Cached semantic source for periodic status damage. It deliberately contains no
/// scene lookup and can be copied into each authoritative tick's feedback context.
/// </summary>
public readonly struct StatusDamageSource
{
    public readonly WeaponInstance Weapon;
    public readonly IWeaponFeedbackSink FeedbackSink;
    public readonly WeaponFeedbackMode Mode;
    public readonly WeaponUpgradePath UpgradePath;
    public readonly float ReferenceDamage;
    public readonly int StatusInstanceId;
    public readonly WeaponStatusKind StatusKind;
    public readonly bool IsAbilityDamage;

    public bool IsValid => FeedbackSink != null && StatusInstanceId > 0 && IsBurnFamily(StatusKind);

    public StatusDamageSource(
        WeaponInstance weapon,
        IWeaponFeedbackSink feedbackSink,
        WeaponFeedbackMode mode,
        WeaponUpgradePath upgradePath,
        float referenceDamage,
        int statusInstanceId,
        WeaponStatusKind statusKind,
        bool isAbilityDamage)
    {
        Weapon = weapon;
        FeedbackSink = feedbackSink;
        Mode = mode;
        UpgradePath = upgradePath;
        ReferenceDamage = SanitizeReferenceDamage(referenceDamage);
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        IsAbilityDamage = isAbilityDamage;
    }

    public DamageFeedbackKind DamageKind => StatusKind == WeaponStatusKind.JellifiedBurn
        ? DamageFeedbackKind.JellifiedBurn
        : DamageFeedbackKind.Burn;

    private static float SanitizeReferenceDamage(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 1f;
        return Mathf.Max(1f, value);
    }

    private static bool IsBurnFamily(WeaponStatusKind kind)
    {
        return kind == WeaponStatusKind.Burn || kind == WeaponStatusKind.JellifiedBurn;
    }
}

/// <summary>Shared allocation-free identity source for damaging status instances.</summary>
public static class StatusDamageInstanceRuntime
{
    private static int s_nextId = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBeforeRuntimeLoad() => s_nextId = 1;

    public static int Next()
    {
        int id = s_nextId;
        s_nextId = s_nextId == int.MaxValue ? 1 : s_nextId + 1;
        return Mathf.Max(1, id);
    }
}
