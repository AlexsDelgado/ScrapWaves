using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BurnStatusCombatTextTests
{
    private sealed class AuthoritativeTarget : MonoBehaviour, IAuthoritativeDamageable
    {
        public int Health = 100;
        public bool BlockStatus;
        public DamageChannel LastChannel;
        public WeaponStatusKind LastStatusKind;

        public bool ApplyDamage(int amount)
        {
            DamageRequest request = new(amount, amount, DamageChannel.Direct);
            return ApplyDamage(in request).Applied;
        }

        public DamageApplicationResult ApplyDamage(in DamageRequest request)
        {
            LastChannel = request.Channel;
            LastStatusKind = request.StatusKind;
            if (BlockStatus && request.Channel == DamageChannel.Status)
                return DamageApplicationResult.BlockedResult(in request, Health);

            int before = Health;
            Health = Mathf.Max(0, Health - request.ModifiedDamage);
            return DamageApplicationResult.FromHealthDelta(in request, before, Health);
        }
    }

    private readonly struct StatusClosure
    {
        public readonly Transform Target;
        public readonly WeaponStatusKind StatusKind;
        public readonly int StatusInstanceId;
        public readonly int SegmentIndex;

        public StatusClosure(
            Transform target,
            WeaponStatusKind statusKind,
            int statusInstanceId,
            int segmentIndex)
        {
            Target = target;
            StatusKind = statusKind;
            StatusInstanceId = statusInstanceId;
            SegmentIndex = segmentIndex;
        }
    }

    private sealed class RecordingFeedbackSink : IWeaponFeedbackSink, ICombatTextStatusLifecycleSink
    {
        public readonly List<WeaponFeedbackContext> DamageEvents = new();
        public readonly List<StatusClosure> StatusClosures = new();

        public void OnDamageConfirmed(in WeaponFeedbackContext context) => DamageEvents.Add(context);
        public void OnStatusSegmentClosed(
            Transform target,
            WeaponStatusKind statusKind,
            int statusInstanceId,
            int segmentIndex)
        {
            StatusClosures.Add(new StatusClosure(target, statusKind, statusInstanceId, segmentIndex));
        }
        public void Emit(in WeaponPresentationContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnShotFired(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { }
        public void OnStatusApplied(in WeaponFeedbackContext context) { }
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold) { }
        public void ConfigureProjectile(
            Projectile projectile,
            ProjectilePresentationArchetypeId archetype,
            in WeaponFeedbackContext context) { }
    }

    private readonly List<GameObject> _objects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i] != null)
                Object.DestroyImmediate(_objects[i]);
        }
        _objects.Clear();
    }

    [Test]
    public void TickUsesStatusChannelAndEmitsExactAppliedDamageIncludingOverkillClamp()
    {
        AuthoritativeTarget target = CreateTarget(7, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 5, 2f, 0.5f, WeaponStatusKind.Burn, in source);

        burn.Tick(0.5f);
        burn.Tick(0.5f);

        Assert.That(target.LastChannel, Is.EqualTo(DamageChannel.Status));
        Assert.That(target.LastStatusKind, Is.EqualTo(WeaponStatusKind.Burn));
        Assert.That(sink.DamageEvents.Count, Is.EqualTo(2));
        Assert.That(sink.DamageEvents[0].DamageAmount, Is.EqualTo(5));
        Assert.That(sink.DamageEvents[1].DamageAmount, Is.EqualTo(2));
        Assert.That(sink.DamageEvents[1].IsKill, Is.True);
        Assert.That(sink.DamageEvents[0].StatusInstanceId, Is.GreaterThan(0));
        Assert.That(sink.DamageEvents[1].StatusInstanceId, Is.EqualTo(sink.DamageEvents[0].StatusInstanceId));
    }

    [Test]
    public void BlockedStatusTickProducesNoDamageFeedback()
    {
        AuthoritativeTarget target = CreateTarget(20, out FlamethrowerBurnStatus burn);
        target.BlockStatus = true;
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 5, 2f, 0.5f, WeaponStatusKind.Burn, in source);

        burn.Tick(0.5f);

        Assert.That(target.Health, Is.EqualTo(20));
        Assert.That(sink.DamageEvents, Is.Empty);
    }

    [Test]
    public void RefreshKeepsIdentityForSameSourceAndReplacesItForSourceOrKindChange()
    {
        AuthoritativeTarget target = CreateTarget(100, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource first = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 3, 4f, 0.5f, WeaponStatusKind.Burn, in first);
        int firstId = burn.StatusInstanceId;

        burn.Refresh(target, 4, 4f, 0.5f, WeaponStatusKind.Burn, in first);
        Assert.That(burn.StatusInstanceId, Is.EqualTo(firstId));

        StatusDamageSource replacement = CreateSource(
            sink,
            WeaponStatusKind.Burn,
            new WeaponInstance());
        burn.Refresh(target, 4, 4f, 0.5f, WeaponStatusKind.Burn, in replacement);
        int replacementId = burn.StatusInstanceId;
        Assert.That(replacementId, Is.Not.EqualTo(firstId));

        StatusDamageSource jellified = CreateSource(
            sink,
            WeaponStatusKind.JellifiedBurn,
            replacement.Weapon);
        burn.Refresh(target, 4, 4f, 0.5f, WeaponStatusKind.JellifiedBurn, in jellified);
        Assert.That(burn.StatusInstanceId, Is.Not.EqualTo(replacementId));
    }

    [Test]
    public void ContinuouslyRefreshedBurnStartsANewExactSegmentAfterHardLimit()
    {
        AuthoritativeTarget target = CreateTarget(100, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.JellifiedBurn);
        burn.Refresh(target, 1, 10f, 0.5f, WeaponStatusKind.JellifiedBurn, in source);

        for (int i = 0; i < 6; i++)
            burn.Tick(0.5f);
        burn.Refresh(target, 1, 10f, 0.5f, WeaponStatusKind.JellifiedBurn, in source);
        burn.Tick(0.3f);
        burn.Tick(0.2f);

        Assert.That(sink.DamageEvents.Count, Is.EqualTo(7));
        Assert.That(sink.DamageEvents[5].SegmentIndex, Is.EqualTo(0));
        Assert.That(sink.DamageEvents[6].SegmentIndex, Is.EqualTo(1));
        Assert.That(sink.DamageEvents[6].DamageKind, Is.EqualTo(DamageFeedbackKind.JellifiedBurn));
    }

    [Test]
    public void SourceAndKindReplacementCloseThePreviousSegmentImmediately()
    {
        AuthoritativeTarget target = CreateTarget(100, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource first = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 2, 10f, 0.5f, WeaponStatusKind.Burn, in first);
        int firstId = burn.StatusInstanceId;

        StatusDamageSource replacement = CreateSource(
            sink,
            WeaponStatusKind.Burn,
            new WeaponInstance());
        burn.Refresh(target, 2, 10f, 0.5f, WeaponStatusKind.Burn, in replacement);
        int replacementId = burn.StatusInstanceId;

        Assert.That(sink.StatusClosures.Count, Is.EqualTo(1));
        Assert.That(sink.StatusClosures[0].Target, Is.SameAs(target.transform));
        Assert.That(sink.StatusClosures[0].StatusKind, Is.EqualTo(WeaponStatusKind.Burn));
        Assert.That(sink.StatusClosures[0].StatusInstanceId, Is.EqualTo(firstId));
        Assert.That(sink.StatusClosures[0].SegmentIndex, Is.Zero);

        StatusDamageSource jellified = CreateSource(
            sink,
            WeaponStatusKind.JellifiedBurn,
            replacement.Weapon);
        burn.Refresh(target, 2, 10f, 0.5f, WeaponStatusKind.JellifiedBurn, in jellified);

        Assert.That(sink.StatusClosures.Count, Is.EqualTo(2));
        Assert.That(sink.StatusClosures[1].StatusKind, Is.EqualTo(WeaponStatusKind.Burn));
        Assert.That(sink.StatusClosures[1].StatusInstanceId, Is.EqualTo(replacementId));
        Assert.That(sink.StatusClosures[1].SegmentIndex, Is.Zero);
    }

    [Test]
    public void HardLimitClosesSegmentBeforeAdvancingItsIdentity()
    {
        AuthoritativeTarget target = CreateTarget(100, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 1, 10f, 0.5f, WeaponStatusKind.Burn, in source);
        int statusId = burn.StatusInstanceId;

        burn.Tick(3.24f);
        Assert.That(sink.StatusClosures, Is.Empty);

        burn.Tick(0.01f);

        Assert.That(sink.StatusClosures.Count, Is.EqualTo(1));
        Assert.That(sink.StatusClosures[0].StatusInstanceId, Is.EqualTo(statusId));
        Assert.That(sink.StatusClosures[0].SegmentIndex, Is.Zero);
        Assert.That(burn.TallySegmentIndex, Is.EqualTo(1));
    }

    [Test]
    public void KillingTickClosesTheCurrentStatusSegmentImmediately()
    {
        AuthoritativeTarget target = CreateTarget(2, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.JellifiedBurn);
        burn.Refresh(target, 5, 10f, 0.5f, WeaponStatusKind.JellifiedBurn, in source);
        int statusId = burn.StatusInstanceId;

        burn.Tick(0.5f);

        Assert.That(sink.StatusClosures.Count, Is.EqualTo(1));
        Assert.That(sink.StatusClosures[0].StatusKind, Is.EqualTo(WeaponStatusKind.JellifiedBurn));
        Assert.That(sink.StatusClosures[0].StatusInstanceId, Is.EqualTo(statusId));
        Assert.That(sink.StatusClosures[0].SegmentIndex, Is.Zero);
    }

    [Test]
    public void DisableClosesOnceAndClearsStateBeforePooledReuse()
    {
        AuthoritativeTarget target = CreateTarget(100, out FlamethrowerBurnStatus burn);
        RecordingFeedbackSink sink = new();
        StatusDamageSource source = CreateSource(sink, WeaponStatusKind.Burn);
        burn.Refresh(target, 5, 10f, 0.5f, WeaponStatusKind.Burn, in source);
        int firstId = burn.StatusInstanceId;

        InvokeLifecycle(burn, "OnDisable");

        Assert.That(sink.StatusClosures.Count, Is.EqualTo(1));
        Assert.That(sink.StatusClosures[0].StatusInstanceId, Is.EqualTo(firstId));
        Assert.That(burn.StatusInstanceId, Is.Zero);
        Assert.That(burn.TallySegmentIndex, Is.Zero);

        burn.Refresh(target, 1, 1f, 0.5f, WeaponStatusKind.Burn, in source);
        int secondId = burn.StatusInstanceId;
        burn.Tick(0.5f);

        Assert.That(secondId, Is.GreaterThan(0).And.Not.EqualTo(firstId));
        Assert.That(target.Health, Is.EqualTo(99), "Pooled reuse must not retain the previous stronger tick.");

        InvokeLifecycle(burn, "OnDisable");
        InvokeLifecycle(burn, "OnDestroy");
        Object.DestroyImmediate(burn);
        Assert.That(sink.StatusClosures.Count, Is.EqualTo(2),
            "Disable followed by destroy must not close the same segment twice.");
    }

    private AuthoritativeTarget CreateTarget(int health, out FlamethrowerBurnStatus burn)
    {
        GameObject gameObject = new("Burn Status Test Target");
        _objects.Add(gameObject);
        AuthoritativeTarget target = gameObject.AddComponent<AuthoritativeTarget>();
        target.Health = health;
        burn = gameObject.AddComponent<FlamethrowerBurnStatus>();
        return target;
    }

    private static void InvokeLifecycle(FlamethrowerBurnStatus burn, string methodName)
    {
        MethodInfo method = typeof(FlamethrowerBurnStatus).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing lifecycle method {methodName}.");
        method.Invoke(burn, null);
    }

    private static StatusDamageSource CreateSource(
        RecordingFeedbackSink sink,
        WeaponStatusKind kind,
        WeaponInstance weapon = null)
    {
        return new StatusDamageSource(
            weapon ?? new WeaponInstance(),
            sink,
            WeaponFeedbackMode.Automatic,
            WeaponUpgradePath.None,
            referenceDamage: 5f,
            statusInstanceId: 0,
            statusKind: kind,
            isAbilityDamage: false);
    }
}
