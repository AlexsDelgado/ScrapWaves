using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class CombatTextSpatialDirectorTests
{
    private readonly List<GameObject> _extraTargets = new();
    private CombatTextProfile _profile;
    private GameObject _runtimeRoot;
    private GameObject _cameraObject;
    private GameObject _target;
    private Camera _camera;
    private CombatTextDirector _director;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        _profile.ViewPrefab = null;
        _profile.LowActiveViews = 1;
        _profile.LowPrewarmViews = 1;
        _profile.LowVisibleBurnTallies = 1;
        _profile.LaneSpacing = 0f;
        _profile.CameraSurfaceBias = 0f;
        ZeroJitter(_profile.NormalMotion);
        ZeroJitter(_profile.BurnTallyMotion);
        _profile.Sanitize();

        _runtimeRoot = new GameObject("Moving Player Presentation Root");
        _cameraObject = new GameObject("Combat Text Camera", typeof(Camera));
        _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        _camera = _cameraObject.GetComponent<Camera>();
        _target = new GameObject("Damage Target");
        _director = new CombatTextDirector(
            _runtimeRoot.transform,
            _camera,
            _profile,
            new GameFeelRuntimeOptions { Quality = GameFeelQualityLevel.Low });
    }

    [TearDown]
    public void TearDown()
    {
        _director?.Dispose();
        foreach (GameObject target in _extraTargets) Object.DestroyImmediate(target);
        _extraTargets.Clear();
        Object.DestroyImmediate(_target);
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_runtimeRoot);
        Object.DestroyImmediate(_profile);
    }

    [Test]
    public void DirectNumberUsesWorldImpactAndIsIndependentFromPlayerRoot()
    {
        Vector3 impact = new(1.5f, 1.25f, 2f);
        WeaponFeedbackContext context = CreateContext(impact, DamageFeedbackKind.Direct);

        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        CombatTextView view = FindActiveView();
        Assert.That(_director.WorldRoot.parent, Is.Null);
        Assert.That(_director.WorldRoot.GetComponent<CombatTextWorldRenderDriver>(), Is.Not.Null);
        Assert.That(view.transform.position, Is.EqualTo(impact));

        _runtimeRoot.transform.SetPositionAndRotation(
            new Vector3(30f, 4f, -8f),
            Quaternion.Euler(0f, 120f, 0f));
        _director.Tick(0f, 0f);

        Assert.That(view.transform.position, Is.EqualTo(impact));
        Assert.That(_director.WorldRoot.GetComponentsInChildren<Canvas>(true), Is.Empty);
    }

    [Test]
    public void BurnTallyFollowsTargetUntilStatusCloses()
    {
        _target.transform.position = new Vector3(0f, 1f, 1f);
        WeaponFeedbackContext context = CreateContext(
            _target.transform.position,
            DamageFeedbackKind.Burn,
            statusInstanceId: 27);

        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        CombatTextView view = FindActiveView();
        Assert.That(
            view.transform.position,
            Is.EqualTo(_target.transform.position + Vector3.up * _profile.WorldAnchorHeight));

        _target.transform.position = new Vector3(2f, 3f, 4f);
        _director.Tick(0.1f, 0.1f);
        Vector3 followedPosition = _target.transform.position + Vector3.up * _profile.WorldAnchorHeight;
        Assert.That(view.transform.position, Is.EqualTo(followedPosition));

        _director.NotifyStatusSegmentClosed(
            _target.transform,
            WeaponStatusKind.Burn,
            statusInstanceId: 27,
            segmentIndex: 0,
            now: 0.11f);
        Assert.That(view.IsReleased, Is.True);

        _target.transform.position += Vector3.one * 5f;
        _director.Tick(0.21f, 0.1f);

        Assert.That(view.transform.position, Is.EqualTo(followedPosition));

        float lifetime = _profile.BurnTallyMotion.Lifetime;
        _director.Tick(0.21f + lifetime, lifetime);
        Assert.That(view.IsActive, Is.False);
        Assert.That(_director.ActiveViewCount, Is.Zero);
    }

    [Test]
    public void BurnTallyAnchorsAboveScaledTargetBounds()
    {
        _target.transform.SetPositionAndRotation(
            new Vector3(0f, 0f, 1f),
            Quaternion.identity);
        _target.transform.localScale = new Vector3(1f, 3f, 1f);
        CapsuleCollider targetCollider = _target.AddComponent<CapsuleCollider>();
        targetCollider.height = 2f;
        targetCollider.radius = 0.5f;

        WeaponFeedbackContext context = CreateContext(
            _target.transform.position,
            DamageFeedbackKind.Burn,
            statusInstanceId: 41);

        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        CombatTextView view = FindActiveView();
        Bounds targetBounds = targetCollider.bounds;

        Assert.That(
            view.transform.position.y,
            Is.GreaterThanOrEqualTo(targetBounds.max.y + _profile.WorldAnchorClearance - 0.0001f));
        Assert.That(view.transform.position.x, Is.EqualTo(targetBounds.center.x).Within(0.0001f));
        Assert.That(view.transform.position.z, Is.EqualTo(targetBounds.center.z).Within(0.0001f));
    }

    [Test]
    public void DestroyedBurnTargetReleasesFromItsLastVisiblePosition()
    {
        _target.transform.position = new Vector3(0f, 1f, 1f);
        WeaponFeedbackContext context = CreateContext(
            _target.transform.position,
            DamageFeedbackKind.Burn,
            statusInstanceId: 52);

        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        CombatTextView view = FindActiveView();
        _target.transform.position = new Vector3(2f, 3f, 4f);
        _director.Tick(0.1f, 0.1f);
        Vector3 lastVisiblePosition = view.transform.position;

        Object.DestroyImmediate(_target);
        _director.Tick(0.2f, 0.1f);

        Assert.That(view.IsReleased, Is.True);
        Assert.That(view.transform.position, Is.EqualTo(lastVisiblePosition));
    }

    [Test]
    public void DisposeDestroysDetachedWorldRoot()
    {
        GameObject worldRoot = _director.WorldRoot.gameObject;

        _director.Dispose();

        Assert.That(worldRoot == null, Is.True);
        _director = null;
    }

    [TestCase(0.4f)]
    [TestCase(1.2f)]
    [TestCase(2.4f)]
    [TestCase(5f)]
    public void InteriorDirectNumberClearsScaledBoundsEvenWithDownwardLaneAndCameraBias(float size)
    {
        _profile.LaneSpacing = 20f;
        _profile.CameraSurfaceBias = 0.05f;
        _target.transform.localScale = Vector3.one * size;
        CapsuleCollider collider = _target.AddComponent<CapsuleCollider>();
        collider.height = 1.2f;
        Physics.SyncTransforms();
        WeaponFeedbackContext context = CreateContext(_target.transform.position, DamageFeedbackKind.Explosion);
        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        Assert.That(FindActiveView().transform.position.y,
            Is.GreaterThanOrEqualTo(collider.bounds.max.y + _profile.WorldAnchorClearance - 0.0001f));
        _target.transform.position += Vector3.right * 3f;
        Vector3 snapshot = FindActiveView().transform.position;
        _director.Tick(0.05f, 0.05f);
        Assert.That(FindActiveView().transform.position, Is.EqualTo(snapshot));
    }

    [Test]
    public void ChildRendererOutsideRootColliderIsIncludedWithoutMovingExternalImpacts()
    {
        _target.AddComponent<BoxCollider>();
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _extraTargets.Add(body);
        body.transform.SetParent(_target.transform, false);
        body.transform.localScale = new Vector3(2f, 4f, 2f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        WeaponFeedbackContext context = CreateContext(new Vector3(0f, 1f, 0f), DamageFeedbackKind.Direct);
        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        Assert.That(FindActiveView().transform.position.y, Is.GreaterThan(body.GetComponent<Renderer>().bounds.max.y));
        _director.StopAll();
        Vector3 outside = new(3f, 1f, 0f);
        context = CreateContext(outside, DamageFeedbackKind.Direct);
        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        Assert.That(FindActiveView().transform.position, Is.EqualTo(outside));
    }

    [Test]
    public void BurstOverFrameBudgetAutomaticallyRecoversEveryDistinctTarget()
    {
        RebuildDirector(16, 16, 3);
        for (int i = 0; i < 8; i++)
        {
            GameObject target = ExtraTarget(i);
            WeaponFeedbackContext context = CreateContext(target.transform.position, DamageFeedbackKind.Direct, target: target.transform);
            _director.TryEmit(in context, 0f);
        }
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(2));
        _director.Tick(0f, 0f); // Same frame remains capped.
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(2));
        for (int i = 1; i <= 3; i++) _director.Tick(i * 0.016f, 0.016f);
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(8));
        Assert.That(_director.Metrics.SumAppliedDamageReceived, Is.EqualTo(96));
        Assert.That(_director.ActiveViewCount, Is.EqualTo(8));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PendingTotalsSurviveActiveCapOrPoolExhaustionAndSequenceClosure(bool poolExhaustion)
    {
        RebuildDirector(1, 1, 7);
        if (poolExhaustion) _profile.LowActiveViews = 2; // Raise limit after fixed pool creation.
        WeaponFeedbackContext first = CreateContext(Vector3.zero, DamageFeedbackKind.Direct);
        Assert.That(_director.TryEmit(in first, 0f), Is.True);
        GameObject target = ExtraTarget(1);
        WeaponFeedbackContext pending = CreateContext(target.transform.position, DamageFeedbackKind.Direct,
            target: target.transform, damageAmount: 7, actionSequenceId: 456);
        Assert.That(_director.TryEmit(in pending, 0f), Is.False);
        Assert.That(_director.Metrics.LastSuppressionReason, Is.EqualTo(poolExhaustion
            ? CombatTextSuppressionReason.PoolExhausted : CombatTextSuppressionReason.Density));
        pending = CreateContext(target.transform.position, DamageFeedbackKind.Direct,
            target: target.transform, damageAmount: 9, actionSequenceId: 456);
        Assert.That(_director.TryEmit(in pending, 0.05f), Is.False);
        _director.NotifySequenceCompleted(456, 0.06f);
        _director.Tick(0.9f, 0.9f);
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(2));
        Assert.That(FindActiveView().GetComponentInChildren<TextMeshPro>().text, Is.EqualTo("16"));
        Assert.That(_director.Metrics.SumAppliedDamageReceived, Is.EqualTo(28));
    }

    [Test]
    public void PendingBacklogExpiresAndNeverReplaysCompletedViews()
    {
        RebuildDirector(1, 1, 7);
        _profile.NormalMotion.Lifetime = 2f;
        WeaponFeedbackContext first = CreateContext(Vector3.zero, DamageFeedbackKind.Direct);
        _director.TryEmit(in first, 0f);
        GameObject target = ExtraTarget(1);
        WeaponFeedbackContext pending = CreateContext(target.transform.position, DamageFeedbackKind.Direct, target: target.transform);
        _director.TryEmit(in pending, 0f);
        _director.Tick(1.3f, 1.3f);
        _director.Tick(2.1f, 0.8f);
        _director.Tick(2.2f, 0.1f);
        Assert.That(_director.ActiveAggregateCount, Is.Zero);
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(1));
        Assert.That(_director.Metrics.SumAppliedDamageReceived, Is.EqualTo(24));
    }

    [Test]
    public void LateDirectMergeKeepsExactTotalReadableWithoutSpawningAgain()
    {
        WeaponFeedbackContext context = CreateContext(Vector3.zero, DamageFeedbackKind.Direct, actionSequenceId: 789);
        _director.TryEmit(in context, 0f);
        _director.Tick(0.75f, 0.75f);
        Assert.That(FindActiveView().GetComponentInChildren<TextMeshPro>().alpha, Is.LessThan(0.2f));
        _director.TryEmit(in context, 0.75f);
        Assert.That(FindActiveView().IsFading, Is.False);
        _director.Tick(0.9f, 0.15f);
        TextMeshPro text = FindActiveView().GetComponentInChildren<TextMeshPro>();
        Assert.That(text.text, Is.EqualTo("24"));
        Assert.That(text.alpha, Is.EqualTo(1f).Within(0.001f));
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(1));
        Assert.That(_director.Metrics.MergedEvents, Is.EqualTo(1));
        _director.Tick(1.3f, 0.4f);
        Assert.That(_director.ActiveViewCount, Is.Zero);
    }

    [Test]
    public void HitZoneUsesParentEnemyGeometryAndInactiveRendererStillClearsKillingHit()
    {
        _target.AddComponent<EnemyHealth>();
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _extraTargets.Add(body);
        body.transform.SetParent(_target.transform, false);
        body.transform.localScale = new Vector3(2f, 4f, 2f);
        GameObject zone = ExtraTarget(0);
        zone.transform.SetParent(_target.transform, false);
        zone.AddComponent<EnemyDamageHitZone>();
        Bounds bounds = body.GetComponent<Renderer>().bounds;
        _target.SetActive(false); // Killing damage can despawn before emitting feedback.
        WeaponFeedbackContext context = CreateContext(Vector3.zero, DamageFeedbackKind.Direct, target: zone.transform);
        Assert.That(_director.TryEmit(in context, 0f), Is.True);
        Assert.That(FindActiveView().transform.position.y, Is.GreaterThan(bounds.max.y));
    }

    [Test]
    public void BurnDensityRecoversPendingClosedSegmentWithExactTotal()
    {
        RebuildDirector(4, 4, 7);
        WeaponFeedbackContext first = CreateContext(Vector3.zero, DamageFeedbackKind.Burn, statusInstanceId: 11);
        Assert.That(_director.TryEmit(in first, 0f), Is.True);
        GameObject target = ExtraTarget(1);
        WeaponFeedbackContext pending = CreateContext(target.transform.position, DamageFeedbackKind.Burn,
            statusInstanceId: 22, target: target.transform);
        Assert.That(_director.TryEmit(in pending, 0f), Is.False);
        Assert.That(_director.Metrics.LastSuppressionReason, Is.EqualTo(CombatTextSuppressionReason.BurnDensity));
        _director.NotifyStatusSegmentClosed(_target.transform, WeaponStatusKind.Burn, 11, 0, 0.01f);
        _director.NotifyStatusSegmentClosed(target.transform, WeaponStatusKind.Burn, 22, 0, 0.01f);
        _director.Tick(0.5f, 0.5f);
        Assert.That(_director.Metrics.ViewsSpawned, Is.EqualTo(2));
        Assert.That(FindActiveView().IsReleased, Is.True);
        Assert.That(FindActiveView().GetComponentInChildren<TextMeshPro>().text, Is.EqualTo("12"));
        _director.Tick(1f, 0.5f);
        Assert.That(_director.ActiveViewCount, Is.Zero);
    }

    [Test]
    public void WarmedPendingRetryTickDoesNotAllocateManagedMemory()
    {
        RebuildDirector(1, 1, 7);
        WeaponFeedbackContext first = CreateContext(Vector3.zero, DamageFeedbackKind.Direct);
        _director.TryEmit(in first, 0f);
        GameObject target = ExtraTarget(1);
        WeaponFeedbackContext pending = CreateContext(target.transform.position, DamageFeedbackKind.Direct, target: target.transform);
        _director.TryEmit(in pending, 0f);
        _director.Tick(0.01f, 0.01f);
        _director.Tick(0.02f, 0.01f);
        Assert.That(_director.Metrics.LastUpdateManagedAllocationBytes, Is.Zero);
    }
    private GameObject ExtraTarget(int index)
    {
        GameObject target = new("Pending target " + index);
        target.transform.position = new Vector3(index * 0.2f, 0f, 0f);
        _extraTargets.Add(target);
        return target;
    }

    private void RebuildDirector(int active, int pool, int starts)
    {
        _director.Dispose();
        _profile.LowActiveViews = active;
        _profile.LowPrewarmViews = pool;
        _profile.LowStartsPerFrame = starts;
        _profile.Sanitize();
        _director = new CombatTextDirector(_runtimeRoot.transform, _camera, _profile,
            new GameFeelRuntimeOptions { Quality = GameFeelQualityLevel.Low });
    }
    private WeaponFeedbackContext CreateContext(
        Vector3 impactPosition,
        DamageFeedbackKind damageKind,
        int statusInstanceId = 0,
        Transform target = null,
        int damageAmount = 12,
        int actionSequenceId = 0)
    {
        return new WeaponFeedbackContext(
            weapon: null,
            mode: WeaponFeedbackMode.Automatic,
            normalizedHeat: 0f,
            origin: _camera.transform.position,
            direction: Vector3.forward,
            impactPosition: impactPosition,
            impactNormal: Vector3.back,
            damageAmount: damageAmount,
            target: target != null ? target : _target.transform,
            referenceDamage: 10f,
            actionSequenceId: actionSequenceId,
            damageKind: damageKind,
            statusInstanceId: statusInstanceId,
            statusKind: WeaponStatusKind.Burn,
            segmentIndex: 0);
    }

    private CombatTextView FindActiveView()
    {
        CombatTextView[] views = _director.WorldRoot.GetComponentsInChildren<CombatTextView>(true);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i].IsActive)
                return views[i];
        }
        Assert.Fail("Expected one active spatial combat-text view.");
        return null;
    }

    private static void ZeroJitter(CombatTextMotionSettings motion)
    {
        motion.HorizontalSpeed = 0f;
        motion.UpwardSpeed = 0f;
        motion.DownwardAcceleration = 0f;
        motion.InitialJitterX = 0f;
        motion.InitialJitterY = 0f;
        motion.LocalShakeAmplitude = 0f;
    }
}
