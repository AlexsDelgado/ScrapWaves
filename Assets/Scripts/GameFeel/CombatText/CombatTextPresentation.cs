using UnityEngine;

public readonly struct CombatTextPresentation
{
    public readonly long TotalAppliedDamage;
    public readonly CombatTextStyleId Style;
    public readonly CombatTextPriority Priority;
    public readonly DamageFeedbackKind DamageKind;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly bool IsKill;
    public readonly bool IsBurnTally;
    public readonly bool CompactLargeNumbers;
    public readonly bool ReducedFlash;
    public readonly bool AllowLocalShake;
    public readonly Vector2 ScreenPosition;
    public readonly float ResolvedScale;
    public readonly int DeterministicSeed;
    public readonly CombatTextMotionSettings Motion;

    public CombatTextPresentation(
        long totalAppliedDamage,
        CombatTextStyleId style,
        CombatTextPriority priority,
        DamageFeedbackKind damageKind,
        bool isCritical,
        bool isWeakPoint,
        bool isKill,
        bool isBurnTally,
        bool compactLargeNumbers,
        bool reducedFlash,
        bool allowLocalShake,
        Vector2 screenPosition,
        float resolvedScale,
        int deterministicSeed,
        CombatTextMotionSettings motion)
    {
        TotalAppliedDamage = totalAppliedDamage;
        Style = style;
        Priority = priority;
        DamageKind = damageKind;
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        IsKill = isKill;
        IsBurnTally = isBurnTally;
        CompactLargeNumbers = compactLargeNumbers;
        ReducedFlash = reducedFlash;
        AllowLocalShake = allowLocalShake;
        ScreenPosition = screenPosition;
        ResolvedScale = resolvedScale;
        DeterministicSeed = deterministicSeed;
        Motion = motion;
    }
}

public readonly struct CombatTextMergePresentation
{
    public readonly long TotalAppliedDamage;
    public readonly CombatTextStyleId Style;
    public readonly CombatTextPriority Priority;
    public readonly DamageFeedbackKind DamageKind;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly bool IsKill;
    public readonly bool CompactLargeNumbers;
    public readonly bool ReducedFlash;
    public readonly float ResolvedScale;
    public readonly float RePunchScale;
    public readonly float RePunchDuration;
    public readonly float UpwardNudge;

    public CombatTextMergePresentation(
        long totalAppliedDamage,
        CombatTextStyleId style,
        CombatTextPriority priority,
        DamageFeedbackKind damageKind,
        bool isCritical,
        bool isWeakPoint,
        bool isKill,
        bool compactLargeNumbers,
        bool reducedFlash,
        float resolvedScale,
        float rePunchScale,
        float rePunchDuration,
        float upwardNudge)
    {
        TotalAppliedDamage = totalAppliedDamage;
        Style = style;
        Priority = priority;
        DamageKind = damageKind;
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        IsKill = isKill;
        CompactLargeNumbers = compactLargeNumbers;
        ReducedFlash = reducedFlash;
        ResolvedScale = resolvedScale;
        RePunchScale = rePunchScale;
        RePunchDuration = rePunchDuration;
        UpwardNudge = upwardNudge;
    }
}
