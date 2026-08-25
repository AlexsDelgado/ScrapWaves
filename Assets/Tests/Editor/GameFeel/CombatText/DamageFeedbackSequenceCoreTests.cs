using NUnit.Framework;

public sealed class DamageFeedbackSequenceCoreTests
{
    [Test]
    public void ClosedSequenceCompletesOnlyAfterEveryContributor()
    {
        DamageFeedbackSequenceRegistry registry = new(capacity: 4, orphanTimeout: 2f);
        int id = registry.BeginSequence(DamageFeedbackKind.Fragment, 2, now: 1f);

        registry.CompleteSequence(id, 1.1f);
        Assert.That(registry.IsComplete(id), Is.False);
        registry.CompleteContributor(id, 1.2f);
        Assert.That(registry.IsComplete(id), Is.False);
        registry.CompleteContributor(id, 1.3f);

        Assert.That(registry.IsComplete(id), Is.True);
        Assert.That(registry.TryGetState(id, out _, out int remaining, out bool closed, out bool complete), Is.True);
        Assert.That(remaining, Is.Zero);
        Assert.That(closed, Is.True);
        Assert.That(complete, Is.True);
    }

    [Test]
    public void AddedContributorExtendsFixedRecordWithoutAllocatingAnotherSlot()
    {
        DamageFeedbackSequenceRegistry registry = new(capacity: 2, orphanTimeout: 2f);
        int id = registry.BeginSequence(DamageFeedbackKind.ManualMultiHit, 1, 0f);
        registry.AddContributor(id, 0.1f);
        registry.CompleteSequence(id, 0.2f);
        registry.CompleteContributor(id, 0.3f);

        Assert.That(registry.IsComplete(id), Is.False);
        registry.CompleteContributor(id, 0.4f);
        Assert.That(registry.IsComplete(id), Is.True);
        Assert.That(registry.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void ClosedSequenceRejectsLateContributorRegistration()
    {
        DamageFeedbackSequenceRegistry registry = new(capacity: 2, orphanTimeout: 2f);
        int id = registry.BeginSequence(DamageFeedbackKind.Fragment, 1, 0f);

        registry.CompleteSequence(id, 0.1f);
        registry.AddContributor(id, 0.2f);

        Assert.That(registry.TryGetState(
            id,
            out _,
            out int remaining,
            out bool registrationClosed,
            out bool complete), Is.True);
        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(registrationClosed, Is.True);
        Assert.That(complete, Is.False);

        registry.CompleteContributor(id, 0.3f);
        Assert.That(registry.IsComplete(id), Is.True);
    }

    [Test]
    public void CapacityExhaustionReturnsReservedZeroAndRecordsOverflow()
    {
        DamageFeedbackSequenceRegistry registry = new(capacity: 1, orphanTimeout: 2f);

        Assert.That(registry.BeginSequence(DamageFeedbackKind.Direct, 1, 0f), Is.GreaterThan(0));
        Assert.That(registry.BeginSequence(DamageFeedbackKind.Direct, 1, 0f), Is.Zero);
        Assert.That(registry.OverflowCount, Is.EqualTo(1));
        Assert.That(registry.Capacity, Is.EqualTo(1));
    }

    [Test]
    public void OrphanTimeoutCompletesThenRecyclesARecord()
    {
        DamageFeedbackSequenceRegistry registry = new(
            capacity: 1,
            orphanTimeout: 0.5f,
            completedRetention: 0.25f);
        int first = registry.BeginSequence(DamageFeedbackKind.Explosion, 3, 0f);

        registry.Tick(0.51f);
        Assert.That(registry.IsComplete(first), Is.True);
        Assert.That(registry.TimeoutCount, Is.EqualTo(1));
        registry.Tick(0.77f);
        Assert.That(registry.ActiveCount, Is.Zero);
        Assert.That(registry.BeginSequence(DamageFeedbackKind.Explosion, 1, 0.8f), Is.GreaterThan(0));
    }

    [Test]
    public void ProducerHeartbeatKeepsClosedLongFlightSequenceAuthoritative()
    {
        DamageFeedbackSequenceRegistry registry = new(capacity: 1, orphanTimeout: 0.5f);
        int id = registry.BeginSequence(DamageFeedbackKind.Explosion, 1, 0f);
        registry.CompleteSequence(id, 0.1f);

        registry.TouchSequence(id, 0.45f);
        registry.Tick(0.8f);

        Assert.That(registry.IsComplete(id), Is.False,
            "An active long-flight producer must not be closed by the orphan fallback.");
        registry.CompleteContributor(id, 0.85f);
        Assert.That(registry.IsComplete(id), Is.True);
        Assert.That(registry.TimeoutCount, Is.Zero);
    }
}
