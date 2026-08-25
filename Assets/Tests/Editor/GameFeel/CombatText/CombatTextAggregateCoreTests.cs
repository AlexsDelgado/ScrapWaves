using NUnit.Framework;
using UnityEngine;

public sealed class CombatTextAggregateCoreTests
{
    [Test]
    public void SameExactKeyMergesAppliedDamageAndPreservesStrongestSemantics()
    {
        CombatTextEvent first = CreateEvent(12, targetId: 10, weaponId: 20, sequenceId: 30);
        CombatTextEvent second = CreateEvent(
            7,
            targetId: 10,
            weaponId: 20,
            sequenceId: 30,
            critical: true,
            weakPoint: true,
            kill: true,
            referenceDamage: 5f);
        CombatTextAggregate aggregate = new(in first, 1f);

        Assert.That(aggregate.CanMerge(in second, 1.2f, 0.01f, 2f), Is.True);
        Assert.That(aggregate.TryMerge(in second, 1.2f), Is.True);
        Assert.That(aggregate.TotalAppliedDamage, Is.EqualTo(19));
        Assert.That(aggregate.HitCount, Is.EqualTo(2));
        Assert.That(aggregate.StrongestSingleAppliedDamage, Is.EqualTo(12));
        Assert.That(aggregate.StrongestSingleRatio, Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(aggregate.IsCritical, Is.True);
        Assert.That(aggregate.IsWeakPoint, Is.True);
        Assert.That(aggregate.IsKill, Is.True);
    }

    [Test]
    public void EveryAggregationIdentityFieldParticipatesInEquality()
    {
        CombatTextEvent baseline = CreateEvent(5, 10, 20, 30);
        CombatTextAggregationKey key = CombatTextAggregationKey.FromEvent(in baseline);
        CombatTextEvent differentTarget = CreateEvent(5, 11, 20, 30);
        CombatTextEvent differentWeapon = CreateEvent(5, 10, 21, 30);
        CombatTextEvent differentSequence = CreateEvent(5, 10, 20, 31);
        CombatTextEvent differentKind = CreateEvent(5, 10, 20, 30, kind: DamageFeedbackKind.Fragment);
        CombatTextEvent differentStatus = CreateEvent(5, 10, 20, 30, statusInstanceId: 9);
        CombatTextEvent differentStatusKind = CreateEvent(
            5,
            10,
            20,
            30,
            statusKind: WeaponStatusKind.JellifiedBurn);
        CombatTextEvent differentSegment = CreateEvent(5, 10, 20, 30, segmentIndex: 1);

        Assert.That(CombatTextAggregationKey.FromEvent(in differentTarget), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentWeapon), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentSequence), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentKind), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentStatus), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentStatusKind), Is.Not.EqualTo(key));
        Assert.That(CombatTextAggregationKey.FromEvent(in differentSegment), Is.Not.EqualTo(key));
    }

    [Test]
    public void SequencedExplosionAndFragmentMergeWhileRetainingPresentationKind()
    {
        CombatTextEvent explosion = CreateEvent(
            12,
            targetId: 10,
            weaponId: 20,
            sequenceId: 30,
            kind: DamageFeedbackKind.Explosion);
        CombatTextEvent fragment = CreateEvent(
            7,
            targetId: 10,
            weaponId: 20,
            sequenceId: 30,
            kind: DamageFeedbackKind.Fragment);
        CombatTextAggregationKey explosionKey = CombatTextAggregationKey.FromEvent(in explosion);
        CombatTextAggregationKey fragmentKey = CombatTextAggregationKey.FromEvent(in fragment);
        CombatTextAggregate aggregate = new(in explosion, 1f);

        Assert.That(fragment.DamageKind, Is.EqualTo(DamageFeedbackKind.Fragment));
        Assert.That(explosionKey.DamageKind, Is.EqualTo(DamageFeedbackKind.Explosion));
        Assert.That(fragmentKey.DamageKind, Is.EqualTo(DamageFeedbackKind.Fragment));
        Assert.That(fragmentKey, Is.EqualTo(explosionKey));
        Assert.That(fragmentKey.GetHashCode(), Is.EqualTo(explosionKey.GetHashCode()));
        Assert.That(aggregate.CanMerge(in fragment, 1.1f, 0.01f, 2f), Is.True);
        Assert.That(aggregate.TryMerge(in fragment, 1.1f), Is.True);
        Assert.That(aggregate.TotalAppliedDamage, Is.EqualTo(19));
        Assert.That(aggregate.Key.DamageKind, Is.EqualTo(DamageFeedbackKind.Explosion));
    }

    [Test]
    public void UnsequencedExplosionAndFragmentRemainSeparateFallbackAggregates()
    {
        CombatTextEvent explosion = CreateEvent(12, sequenceId: 0, kind: DamageFeedbackKind.Explosion);
        CombatTextEvent fragment = CreateEvent(7, sequenceId: 0, kind: DamageFeedbackKind.Fragment);
        CombatTextAggregate aggregate = new(in explosion, 1f);

        Assert.That(CombatTextAggregationKey.FromEvent(in fragment),
            Is.Not.EqualTo(CombatTextAggregationKey.FromEvent(in explosion)));
        Assert.That(aggregate.CanMerge(in fragment, 1.1f, 1f, 2f), Is.False);
    }

    [Test]
    public void UnsequencedEventsRespectFallbackAndHardSegmentWindows()
    {
        CombatTextEvent combatTextEvent = CreateEvent(9, 1, 2, sequenceId: 0);
        CombatTextAggregate aggregate = new(in combatTextEvent, 10f);

        Assert.That(aggregate.CanMerge(in combatTextEvent, 10.19f, 0.20f, 1f), Is.True);
        Assert.That(aggregate.CanMerge(in combatTextEvent, 10.21f, 0.20f, 1f), Is.False);
        Assert.That(aggregate.CanMerge(in combatTextEvent, 11.01f, 2f, 1f), Is.False);
    }

    [Test]
    public void ZeroDamageCannotEnterOrChangeAnAggregate()
    {
        CombatTextEvent first = CreateEvent(4, 1, 2, 3);
        CombatTextEvent zero = CreateEvent(0, 1, 2, 3);
        CombatTextAggregate aggregate = new(in first, 0f);

        Assert.That(zero.IsValid, Is.False);
        Assert.That(aggregate.TryMerge(in zero, 0.1f), Is.False);
        Assert.That(aggregate.TotalAppliedDamage, Is.EqualTo(4));
        Assert.That(aggregate.HitCount, Is.EqualTo(1));
    }

    internal static CombatTextEvent CreateEvent(
        int damage,
        int targetId = 1,
        int weaponId = 2,
        int sequenceId = 3,
        bool critical = false,
        bool weakPoint = false,
        bool kill = false,
        float referenceDamage = 10f,
        DamageFeedbackKind kind = DamageFeedbackKind.Direct,
        int statusInstanceId = 0,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        int segmentIndex = 0,
        bool ability = false,
        WeaponFeedbackMode mode = WeaponFeedbackMode.Automatic,
        WeaponEnemyKind targetClass = WeaponEnemyKind.Normal)
    {
        return new CombatTextEvent(
            damage,
            referenceDamage,
            Vector3.one,
            null,
            targetId,
            weaponId,
            sequenceId,
            kind,
            statusInstanceId,
            statusKind,
            segmentIndex,
            WeaponType.AutomaticCannon,
            mode,
            targetClass,
            critical,
            weakPoint,
            kill,
            ability,
            1f);
    }
}
