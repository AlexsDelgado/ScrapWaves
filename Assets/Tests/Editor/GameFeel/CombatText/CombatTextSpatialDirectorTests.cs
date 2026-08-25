using NUnit.Framework;
using UnityEngine;

public sealed class CombatTextSpatialDirectorTests
{
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

    private WeaponFeedbackContext CreateContext(
        Vector3 impactPosition,
        DamageFeedbackKind damageKind,
        int statusInstanceId = 0)
    {
        return new WeaponFeedbackContext(
            weapon: null,
            mode: WeaponFeedbackMode.Automatic,
            normalizedHeat: 0f,
            origin: _camera.transform.position,
            direction: Vector3.forward,
            impactPosition: impactPosition,
            impactNormal: Vector3.back,
            damageAmount: 12,
            target: _target.transform,
            referenceDamage: 10f,
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
