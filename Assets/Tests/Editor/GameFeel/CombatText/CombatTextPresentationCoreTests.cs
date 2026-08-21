using NUnit.Framework;
using UnityEngine;

public sealed class CombatTextPresentationCoreTests
{
    private CombatTextProfile _profile;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        _profile.Sanitize();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_profile);
    }

    [Test]
    public void StyleResolutionCannotBeDowngradedByLaterRoutineHits()
    {
        CombatTextEvent first = CombatTextAggregateCoreTests.CreateEvent(10, critical: true, weakPoint: true);
        CombatTextEvent routine = CombatTextAggregateCoreTests.CreateEvent(3);
        CombatTextAggregate aggregate = new(in first, 0f);
        aggregate.TryMerge(in routine, 0.1f);

        Assert.That(CombatTextStyleResolver.ResolveStyle(in aggregate), Is.EqualTo(CombatTextStyleId.CriticalWeakPoint));
        Assert.That(CombatTextStyleResolver.ResolvePriority(in aggregate, _profile), Is.EqualTo(CombatTextPriority.CriticalWeakPoint));
    }

    [Test]
    public void BurnAndJellifiedBurnResolveToDistinctStyles()
    {
        CombatTextEvent burn = CombatTextAggregateCoreTests.CreateEvent(4, kind: DamageFeedbackKind.Burn, statusInstanceId: 1);
        CombatTextEvent jelly = CombatTextAggregateCoreTests.CreateEvent(
            4,
            kind: DamageFeedbackKind.JellifiedBurn,
            statusInstanceId: 2,
            statusKind: WeaponStatusKind.JellifiedBurn);

        Assert.That(CombatTextStyleResolver.ResolveStyle(in burn), Is.EqualTo(CombatTextStyleId.Burn));
        Assert.That(CombatTextStyleResolver.ResolveStyle(in jelly), Is.EqualTo(CombatTextStyleId.JellifiedBurn));
    }

    [Test]
    public void ImportantOnlyAcceptsSemanticPrioritiesAndRejectsRoutineDamage()
    {
        Assert.That(CombatTextVisibilityPolicy.AllowsMode(CombatTextMode.ImportantOnly, CombatTextPriority.Kill), Is.True);
        Assert.That(CombatTextVisibilityPolicy.AllowsMode(CombatTextMode.ImportantOnly, CombatTextPriority.WeakPoint), Is.True);
        Assert.That(CombatTextVisibilityPolicy.AllowsMode(CombatTextMode.ImportantOnly, CombatTextPriority.AutomaticDirect), Is.False);
        Assert.That(CombatTextVisibilityPolicy.AllowsMode(CombatTextMode.ImportantOnly, CombatTextPriority.BurnTally), Is.False);
        Assert.That(CombatTextVisibilityPolicy.AllowsMode(CombatTextMode.Off, CombatTextPriority.Kill), Is.False);
    }

    [Test]
    public void DistancePolicyUsesRoutineAndImportantCutoffs()
    {
        CombatTextVisibilityDecision reduced = CombatTextVisibilityPolicy.EvaluateDistance(
            30f,
            CombatTextPriority.AutomaticDirect,
            _profile);
        CombatTextVisibilityDecision routineFar = CombatTextVisibilityPolicy.EvaluateDistance(
            39f,
            CombatTextPriority.AutomaticDirect,
            _profile);
        CombatTextVisibilityDecision important = CombatTextVisibilityPolicy.EvaluateDistance(
            49f,
            CombatTextPriority.Kill,
            _profile);

        Assert.That(reduced.Visible, Is.True);
        Assert.That(reduced.DistanceScale, Is.EqualTo(_profile.DistantScaleMultiplier));
        Assert.That(routineFar.Visible, Is.False);
        Assert.That(important.Visible, Is.True);
    }

    [Test]
    public void FallbackPolicyUsesMortarWindowForShellExplosionAndFragmentDamage()
    {
        _profile.MortarFallbackWindow = 0.19f;
        _profile.RocketExplosionFallbackWindow = 0.11f;
        _profile.FragmentFallbackWindow = 0.31f;

        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Explosion,
                WeaponFeedbackMode.Automatic,
                WeaponType.Mortar),
            Is.EqualTo(0.19f));
        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Fragment,
                WeaponFeedbackMode.Automatic,
                WeaponType.Mortar),
            Is.EqualTo(0.19f));
        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Explosion,
                WeaponFeedbackMode.Automatic,
                WeaponType.RocketLauncher),
            Is.EqualTo(0.11f));
        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Fragment,
                WeaponFeedbackMode.Automatic,
                WeaponType.RocketLauncher),
            Is.EqualTo(0.31f));
    }

    [Test]
    public void FallbackPolicyUsesSpecificCannonWindowsForHeadHunterAndActiveScatter()
    {
        _profile.HeadHunterFallbackWindow = 0.07f;
        _profile.CannonActiveScatterFallbackWindow = 0.17f;
        _profile.CannonAutomaticFallbackWindow = 0.15f;

        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Piercing,
                WeaponFeedbackMode.Automatic,
                WeaponType.AutomaticCannon),
            Is.EqualTo(0.07f));
        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Ability,
                WeaponFeedbackMode.Active,
                WeaponType.AutomaticCannon),
            Is.EqualTo(0.17f));
        Assert.That(
            _profile.GetFallbackWindow(
                DamageFeedbackKind.Direct,
                WeaponFeedbackMode.Automatic,
                WeaponType.AutomaticCannon),
            Is.EqualTo(0.15f));
    }

    [TestCase(99_999L, false, "99999")]
    [TestCase(99_999L, true, "99999")]
    [TestCase(125_000L, true, "125K")]
    [TestCase(1_400_000L, true, "1.4M")]
    [TestCase(1_400_000L, false, "1400000")]
    public void FormattingIsExactOrCompactAtAuthoredBoundaries(long value, bool compact, string expected)
    {
        char[] buffer = new char[32];
        int length = CombatTextFormatter.Write(value, compact, buffer);
        Assert.That(new string(buffer, 0, length), Is.EqualTo(expected));
    }

    [Test]
    public void ResolvedScaleRemainsInsideGlobalSafetyBounds()
    {
        CombatTextEvent hit = CombatTextAggregateCoreTests.CreateEvent(
            1_000_000,
            critical: true,
            weakPoint: true,
            kill: true,
            referenceDamage: 1f,
            targetClass: WeaponEnemyKind.Boss);
        CombatTextAggregate aggregate = new(in hit, 0f);

        float scale = CombatTextStyleResolver.ResolveScale(in aggregate, _profile, 1.25f, 1f);
        Assert.That(scale, Is.InRange(_profile.MinimumResolvedScale, _profile.MaximumResolvedScale));
    }

    [Test]
    public void DirectorMetricsExposeQualityCapsAccessibilityAndResettableTiming()
    {
        GameObject root = new("Combat Text Metrics Test");
        var options = new GameFeelRuntimeOptions
        {
            Quality = GameFeelQualityLevel.Low,
            ReducedMotion = true,
            ReducedShake = true,
            ReducedFlash = true,
            CombatText = CombatTextMode.ImportantOnly,
            CombatTextScale = 0.75f
        };
        CombatTextDirector director = null;
        try
        {
            director = new CombatTextDirector(root.transform, null, _profile, options);
            director.SetCompactLargeNumbersOverride(false);
            director.Tick(1f, 1f / 60f);

            CombatTextMetrics metrics = director.Metrics;
            Assert.That(metrics.ActiveViewLimit, Is.EqualTo(_profile.LowActiveViews));
            Assert.That(metrics.VisibleBurnTallyLimit, Is.EqualTo(_profile.LowVisibleBurnTallies));
            Assert.That(metrics.ViewStartsPerFrameLimit, Is.EqualTo(_profile.LowStartsPerFrame));
            Assert.That(metrics.PoolCapacity, Is.EqualTo(_profile.LowPrewarmViews));
            Assert.That(metrics.AppliedQuality, Is.EqualTo(GameFeelQualityLevel.Low));
            Assert.That(metrics.AppliedReducedMotion, Is.True);
            Assert.That(metrics.AppliedReducedShake, Is.True);
            Assert.That(metrics.AppliedReducedFlash, Is.True);
            Assert.That(metrics.AppliedCombatTextMode, Is.EqualTo(CombatTextMode.ImportantOnly));
            Assert.That(metrics.AppliedCombatTextScale, Is.EqualTo(0.75f));
            Assert.That(metrics.AppliedCompactFormatting, Is.False);
            Assert.That(metrics.LastUpdateMilliseconds, Is.GreaterThanOrEqualTo(0f));
            Assert.That(metrics.LastUpdateManagedAllocationBytes, Is.GreaterThanOrEqualTo(0L));

            WeaponFeedbackContext invalidDamage = new(
                null,
                WeaponFeedbackMode.Automatic,
                0f,
                Vector3.zero,
                Vector3.forward,
                damageAmount: 3);
            director.TryEmit(in invalidDamage, 2f);
            Assert.That(metrics.DamageEventsReceived, Is.EqualTo(1));
            director.ResetMetrics();
            Assert.That(metrics.DamageEventsReceived, Is.Zero);
            Assert.That(metrics.ActiveViewLimit, Is.EqualTo(_profile.LowActiveViews));
        }
        finally
        {
            director?.StopAll();
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void DirectorStopAllPreservesInFlightProducerSequencesAndTheirIds()
    {
        GameObject root = new("Combat Text Sequence Ownership Test");
        CombatTextDirector director = null;
        try
        {
            director = new CombatTextDirector(root.transform, null, _profile, new GameFeelRuntimeOptions());
            DamageFeedbackSequenceRuntime.Configure(capacity: 4, orphanTimeout: 2f);

            int inFlightId = DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Explosion, 1);
            DamageFeedbackSequenceRuntime.CompleteSequence(inFlightId);

            director.StopAll();

            int nextId = DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Explosion, 1);
            DamageFeedbackSequenceRuntime.CompleteSequence(nextId);
            Assert.That(nextId, Is.Not.EqualTo(inFlightId),
                "Rebuilding presentation must not recycle an ID still owned by an in-flight producer.");

            DamageFeedbackSequenceRuntime.CompleteContributor(inFlightId);
            Assert.That(DamageFeedbackSequenceRuntime.IsComplete(inFlightId), Is.True);
            Assert.That(DamageFeedbackSequenceRuntime.IsComplete(nextId), Is.False,
                "Completing the old producer must not complete the newly registered action.");
        }
        finally
        {
            DamageFeedbackSequenceRuntime.Configure(_profile.SequenceCapacity, _profile.SequenceOrphanTimeout);
            director?.StopAll();
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void DirectorMetricsExposeSequenceRegistryOverflow()
    {
        GameObject root = new("Combat Text Sequence Overflow Metrics Test");
        CombatTextDirector director = null;
        try
        {
            director = new CombatTextDirector(root.transform, null, _profile, new GameFeelRuntimeOptions());
            DamageFeedbackSequenceRuntime.Configure(capacity: 1, orphanTimeout: 2f);

            Assert.That(DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Direct, 1), Is.GreaterThan(0));
            Assert.That(DamageFeedbackSequenceRuntime.BeginSequence(DamageFeedbackKind.Direct, 1), Is.Zero);
            director.Tick(1f, 0f);

            Assert.That(director.Metrics.SequenceOverflows, Is.EqualTo(1));
        }
        finally
        {
            DamageFeedbackSequenceRuntime.Configure(_profile.SequenceCapacity, _profile.SequenceOrphanTimeout);
            director?.StopAll();
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void StatusLifecycleCloseReleasesMatchingTargetAndWildcardPuddleSegments()
    {
        GameObject root = new("Combat Text Status Close Test");
        GameObject firstTarget = new("First Burn Target");
        GameObject secondTarget = new("Second Burn Target");
        CombatTextDirector director = null;
        try
        {
            var options = new GameFeelRuntimeOptions { Quality = GameFeelQualityLevel.Low };
            director = new CombatTextDirector(root.transform, null, _profile, options);
            WeaponInstance weapon = new();
            WeaponFeedbackContext first = new(
                weapon,
                WeaponFeedbackMode.Automatic,
                0f,
                firstTarget.transform.position,
                Vector3.up,
                damageAmount: 4,
                target: firstTarget.transform,
                damageKind: DamageFeedbackKind.Burn,
                statusInstanceId: 41,
                statusKind: WeaponStatusKind.Burn,
                segmentIndex: 0);
            WeaponFeedbackContext second = new(
                weapon,
                WeaponFeedbackMode.Automatic,
                0f,
                secondTarget.transform.position,
                Vector3.up,
                damageAmount: 6,
                target: secondTarget.transform,
                damageKind: DamageFeedbackKind.Burn,
                statusInstanceId: 41,
                statusKind: WeaponStatusKind.Burn,
                segmentIndex: 0);

            director.TryEmit(in first, 0f);
            director.TryEmit(in second, 0.01f);
            Assert.That(director.ActiveAggregateCount, Is.EqualTo(2));

            director.NotifyStatusSegmentClosed(
                null,
                WeaponStatusKind.Burn,
                statusInstanceId: 41,
                segmentIndex: 0,
                now: 0.02f);
            director.Tick(0.2f, 0f);

            Assert.That(director.ActiveAggregateCount, Is.Zero,
                "A puddle wildcard close must release every target tally sharing its status segment.");
        }
        finally
        {
            director?.StopAll();
            Object.DestroyImmediate(secondTarget);
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(root);
        }
    }
}
