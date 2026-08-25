using System;
using UnityEngine;

/// <summary>
/// Exact identity for a combat-text aggregate. Explicit explosion sequences treat
/// their primary explosion and fragment descendants as one compatible damage family.
/// The original damage kind remains available for presentation semantics.
/// </summary>
public readonly struct CombatTextAggregationKey : IEquatable<CombatTextAggregationKey>
{
    public readonly int TargetInstanceId;
    public readonly int WeaponInstanceId;
    public readonly DamageFeedbackKind DamageKind;
    public readonly int ActionSequenceId;
    public readonly int StatusInstanceId;
    public readonly WeaponStatusKind StatusKind;
    public readonly int SegmentIndex;

    public CombatTextAggregationKey(
        int targetInstanceId,
        int weaponInstanceId,
        DamageFeedbackKind damageKind,
        int actionSequenceId,
        int statusInstanceId,
        WeaponStatusKind statusKind,
        int segmentIndex)
    {
        TargetInstanceId = targetInstanceId;
        WeaponInstanceId = weaponInstanceId;
        DamageKind = damageKind;
        ActionSequenceId = Mathf.Max(0, actionSequenceId);
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        SegmentIndex = Mathf.Max(0, segmentIndex);
    }

    public static CombatTextAggregationKey FromEvent(in CombatTextEvent combatTextEvent)
    {
        return new CombatTextAggregationKey(
            combatTextEvent.TargetInstanceId,
            combatTextEvent.WeaponInstanceId,
            combatTextEvent.DamageKind,
            combatTextEvent.ActionSequenceId,
            combatTextEvent.StatusInstanceId,
            combatTextEvent.StatusKind,
            combatTextEvent.SegmentIndex);
    }

    public bool Equals(CombatTextAggregationKey other)
    {
        return TargetInstanceId == other.TargetInstanceId &&
               WeaponInstanceId == other.WeaponInstanceId &&
               GetAggregationKind() == other.GetAggregationKind() &&
               ActionSequenceId == other.ActionSequenceId &&
               StatusInstanceId == other.StatusInstanceId &&
               StatusKind == other.StatusKind &&
               SegmentIndex == other.SegmentIndex;
    }

    public override bool Equals(object obj) => obj is CombatTextAggregationKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = TargetInstanceId;
            hash = (hash * 397) ^ WeaponInstanceId;
            hash = (hash * 397) ^ (int)GetAggregationKind();
            hash = (hash * 397) ^ ActionSequenceId;
            hash = (hash * 397) ^ StatusInstanceId;
            hash = (hash * 397) ^ (int)StatusKind;
            hash = (hash * 397) ^ SegmentIndex;
            return hash;
        }
    }

    public static bool operator ==(CombatTextAggregationKey left, CombatTextAggregationKey right) => left.Equals(right);
    public static bool operator !=(CombatTextAggregationKey left, CombatTextAggregationKey right) => !left.Equals(right);

    private DamageFeedbackKind GetAggregationKind()
    {
        if (ActionSequenceId != 0 &&
            (DamageKind == DamageFeedbackKind.Explosion || DamageKind == DamageFeedbackKind.Fragment))
        {
            return DamageFeedbackKind.Explosion;
        }

        return DamageKind;
    }
}

/// <summary>
/// Reusable value record for an exact running damage total. It owns no view and
/// allocates no per-event state.
/// </summary>
public struct CombatTextAggregate
{
    public CombatTextAggregationKey Key { get; private set; }
    public long TotalAppliedDamage { get; private set; }
    public int StrongestSingleAppliedDamage { get; private set; }
    public float StrongestSingleRatio { get; private set; }
    public int HitCount { get; private set; }
    public float ReferenceDamage { get; private set; }
    public Vector3 WorldPosition { get; private set; }
    public Transform Target { get; private set; }
    public WeaponType WeaponType { get; private set; }
    public WeaponFeedbackMode Mode { get; private set; }
    public WeaponEnemyKind TargetClass { get; private set; }
    public bool IsCritical { get; private set; }
    public bool IsWeakPoint { get; private set; }
    public bool IsKill { get; private set; }
    public bool IsAbilityDamage { get; private set; }
    public float EventIntensity { get; private set; }
    public float FirstEventTime { get; private set; }
    public float LastEventTime { get; private set; }
    public bool IsClosed { get; private set; }
    public float ClosedTime { get; private set; }

    public bool IsBurnFamily => Key.DamageKind.IsBurnFamily();
    public float TotalRatio => TotalAppliedDamage / Mathf.Max(1f, ReferenceDamage);

    public CombatTextAggregate(in CombatTextEvent combatTextEvent, float now)
    {
        Key = CombatTextAggregationKey.FromEvent(in combatTextEvent);
        TotalAppliedDamage = combatTextEvent.AppliedDamage;
        StrongestSingleAppliedDamage = combatTextEvent.AppliedDamage;
        ReferenceDamage = Mathf.Max(1f, combatTextEvent.ReferenceDamage);
        StrongestSingleRatio = combatTextEvent.AppliedDamage / ReferenceDamage;
        HitCount = combatTextEvent.AppliedDamage > 0 ? 1 : 0;
        WorldPosition = combatTextEvent.WorldPosition;
        Target = combatTextEvent.Target;
        WeaponType = combatTextEvent.WeaponType;
        Mode = combatTextEvent.Mode;
        TargetClass = combatTextEvent.TargetClass;
        IsCritical = combatTextEvent.IsCritical;
        IsWeakPoint = combatTextEvent.IsWeakPoint;
        IsKill = combatTextEvent.IsKill;
        IsAbilityDamage = combatTextEvent.IsAbilityDamage;
        EventIntensity = combatTextEvent.EventIntensity;
        FirstEventTime = now;
        LastEventTime = now;
        IsClosed = false;
        ClosedTime = 0f;
    }

    public bool CanMerge(
        in CombatTextEvent combatTextEvent,
        float now,
        float fallbackWindow,
        float maximumSegmentLifetime)
    {
        if (IsClosed || combatTextEvent.AppliedDamage <= 0 ||
            Key != CombatTextAggregationKey.FromEvent(in combatTextEvent))
        {
            return false;
        }

        if (now - FirstEventTime > Mathf.Max(0.01f, maximumSegmentLifetime))
            return false;

        return Key.ActionSequenceId != 0 || now - LastEventTime <= Mathf.Max(0.01f, fallbackWindow);
    }

    public bool TryMerge(in CombatTextEvent combatTextEvent, float now)
    {
        if (IsClosed || combatTextEvent.AppliedDamage <= 0 ||
            Key != CombatTextAggregationKey.FromEvent(in combatTextEvent))
        {
            return false;
        }

        TotalAppliedDamage = SaturatingAdd(TotalAppliedDamage, combatTextEvent.AppliedDamage);
        HitCount = HitCount == int.MaxValue ? int.MaxValue : HitCount + 1;

        float reference = Mathf.Max(1f, combatTextEvent.ReferenceDamage);
        float ratio = combatTextEvent.AppliedDamage / reference;
        if (combatTextEvent.AppliedDamage > StrongestSingleAppliedDamage)
            StrongestSingleAppliedDamage = combatTextEvent.AppliedDamage;
        if (ratio > StrongestSingleRatio)
            StrongestSingleRatio = ratio;

        IsCritical |= combatTextEvent.IsCritical;
        IsWeakPoint |= combatTextEvent.IsWeakPoint;
        IsKill |= combatTextEvent.IsKill;
        IsAbilityDamage |= combatTextEvent.IsAbilityDamage;
        EventIntensity = Mathf.Max(EventIntensity, combatTextEvent.EventIntensity);
        WorldPosition = combatTextEvent.WorldPosition;
        if (combatTextEvent.Target != null)
            Target = combatTextEvent.Target;
        LastEventTime = now;
        return true;
    }

    public void MarkClosed(float now)
    {
        if (IsClosed)
            return;
        IsClosed = true;
        ClosedTime = now;
    }

    private static long SaturatingAdd(long current, int value)
    {
        return current > long.MaxValue - value ? long.MaxValue : current + value;
    }
}
