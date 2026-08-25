using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Immutable presentation-only snapshot created from one authoritative applied
/// damage result. A default value is invalid and is never displayed.
/// </summary>
public readonly struct CombatTextEvent
{
    public readonly int AppliedDamage;
    public readonly float ReferenceDamage;
    public readonly Vector3 WorldPosition;
    public readonly Transform Target;
    public readonly int TargetInstanceId;
    public readonly int WeaponInstanceId;
    public readonly int ActionSequenceId;
    public readonly DamageFeedbackKind DamageKind;
    public readonly int StatusInstanceId;
    public readonly WeaponStatusKind StatusKind;
    public readonly int SegmentIndex;
    public readonly WeaponType WeaponType;
    public readonly WeaponFeedbackMode Mode;
    public readonly WeaponEnemyKind TargetClass;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly bool IsKill;
    public readonly bool IsAbilityDamage;
    public readonly float EventIntensity;

    public bool IsValid => AppliedDamage > 0 && TargetInstanceId != 0 && WeaponInstanceId != 0;
    public bool IsBurnFamily => DamageKind.IsBurnFamily();

    public CombatTextEvent(
        int appliedDamage,
        float referenceDamage,
        Vector3 worldPosition,
        Transform target,
        int targetInstanceId,
        int weaponInstanceId,
        int actionSequenceId,
        DamageFeedbackKind damageKind,
        int statusInstanceId,
        WeaponStatusKind statusKind,
        int segmentIndex,
        WeaponType weaponType,
        WeaponFeedbackMode mode,
        WeaponEnemyKind targetClass,
        bool isCritical,
        bool isWeakPoint,
        bool isKill,
        bool isAbilityDamage,
        float eventIntensity)
    {
        AppliedDamage = Mathf.Max(0, appliedDamage);
        ReferenceDamage = SanitizeReferenceDamage(referenceDamage, AppliedDamage);
        WorldPosition = worldPosition;
        Target = target;
        TargetInstanceId = targetInstanceId;
        WeaponInstanceId = weaponInstanceId;
        ActionSequenceId = Mathf.Max(0, actionSequenceId);
        DamageKind = damageKind;
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        SegmentIndex = Mathf.Max(0, segmentIndex);
        WeaponType = weaponType;
        Mode = mode;
        TargetClass = targetClass;
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        IsKill = isKill;
        IsAbilityDamage = isAbilityDamage;
        EventIntensity = Mathf.Max(0f, eventIntensity);
    }

    /// <summary>
    /// Creates an immutable event without scene searches. Non-positive damage or a
    /// missing stable target identity returns the invalid default event.
    /// </summary>
    public static CombatTextEvent FromFeedback(in WeaponFeedbackContext context)
    {
        if (context.DamageAmount <= 0 || context.Target == null)
            return default;

        int targetInstanceId = context.Target.GetInstanceID();
        int weaponInstanceId = context.Weapon != null
            ? RuntimeHelpers.GetHashCode(context.Weapon)
            : ((int)context.WeaponType + 1) * -7919;

        if (targetInstanceId == 0 || weaponInstanceId == 0)
            return default;

        return new CombatTextEvent(
            context.DamageAmount,
            context.ReferenceDamage,
            context.ImpactPosition,
            context.Target,
            targetInstanceId,
            weaponInstanceId,
            context.ActionSequenceId,
            context.DamageKind,
            context.StatusInstanceId,
            context.StatusKind,
            context.SegmentIndex,
            context.WeaponType,
            context.Mode,
            context.TargetClass,
            context.IsCritical,
            context.IsWeakPoint,
            context.IsKill,
            context.IsAbilityDamage,
            context.EventIntensity);
    }

    public static bool TryFromFeedback(in WeaponFeedbackContext context, out CombatTextEvent combatTextEvent)
    {
        combatTextEvent = FromFeedback(in context);
        return combatTextEvent.IsValid;
    }

    private static float SanitizeReferenceDamage(float referenceDamage, int appliedDamage)
    {
        if (float.IsNaN(referenceDamage) || float.IsInfinity(referenceDamage) || referenceDamage <= 0f)
            return Mathf.Max(1, appliedDamage);
        return Mathf.Max(1f, referenceDamage);
    }
}
