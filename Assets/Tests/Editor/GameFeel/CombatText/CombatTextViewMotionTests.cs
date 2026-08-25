using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class CombatTextViewMotionTests
{
    private static readonly MethodInfo CreateProgrammaticMethod = typeof(CombatTextView).GetMethod(
        "CreateProgrammatic",
        BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo VelocityField = typeof(CombatTextView).GetField(
        "_velocity",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SpawnOffsetField = typeof(CombatTextView).GetField(
        "_spawnOffset",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private CombatTextProfile _profile;
    private GameObject _worldRoot;
    private GameObject _cameraObject;
    private Camera _camera;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        _worldRoot = new GameObject("CombatTextViewMotionTests");
        _cameraObject = new GameObject("CombatTextViewMotionCamera", typeof(Camera));
        _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        _camera = _cameraObject.GetComponent<Camera>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_worldRoot);
        Object.DestroyImmediate(_profile);
    }

    [Test]
    public void DirectViewUsesAuthoredScalarEnvelopeInsteadOfGenericCurveValues()
    {
        CombatTextMotionSettings motion = _profile.NormalMotion;
        motion.Lifetime = 1f;
        motion.SpawnScale = 0.4f;
        motion.PopOvershoot = 1.3f;
        motion.SettleTime = 0.2f;
        motion.FadeStartNormalized = 0.5f;
        motion.EndScaleMultiplier = 0.7f;
        motion.HorizontalSpeed = 0f;
        motion.UpwardSpeed = 0f;
        motion.DownwardAcceleration = 0f;
        _profile.Sanitize();

        CombatTextView view = CreateView();
        Play(view, motion, isBurnTally: false, resolvedScale: 2f);

        Assert.That(view.transform.localScale.x, Is.EqualTo(0.8f).Within(0.001f));

        float peakTime = motion.SettleTime * (0.10f / 0.19f);
        view.Tick(peakTime, _camera);
        Assert.That(view.transform.localScale.x, Is.EqualTo(2.6f).Within(0.001f));

        view.Tick(motion.SettleTime - peakTime, _camera);
        Assert.That(view.transform.localScale.x, Is.EqualTo(2f).Within(0.001f));
        Assert.That(view.IsFading, Is.False);

        view.Tick(0.31f, _camera);
        Assert.That(view.IsFading, Is.True);
        Assert.That(GetText(view).alpha, Is.LessThan(1f));
        Assert.That(view.transform.localScale.x, Is.LessThan(2f));

        Assert.That(view.Tick(0.50f, _camera), Is.True);
        Assert.That(GetText(view).alpha, Is.EqualTo(0f).Within(0.001f));
        Assert.That(view.transform.localScale.x, Is.EqualTo(1.4f).Within(0.001f));
    }

    [Test]
    public void BurnTallyPopsOnceSettlesWhileHeldAndShrinksOnlyOnRelease()
    {
        CombatTextMotionSettings motion = _profile.BurnTallyMotion;
        _profile.Sanitize();

        CombatTextView view = CreateView();
        Play(view, motion, isBurnTally: true);

        Assert.That(view.transform.localScale.x, Is.EqualTo(motion.SpawnScale).Within(0.001f));

        float peakTime = motion.SettleTime * (0.10f / 0.19f);
        view.Tick(peakTime, _camera);
        Assert.That(view.transform.localScale.x, Is.EqualTo(motion.PopOvershoot).Within(0.001f));

        view.Tick(motion.SettleTime - peakTime, _camera);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));

        Assert.That(view.Tick(1f, _camera), Is.False);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(GetText(view).alpha, Is.EqualTo(1f).Within(0.001f));

        view.BeginRelease();
        view.Tick(0f, _camera);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));

        Assert.That(view.Tick(motion.Lifetime, _camera), Is.True);
        Assert.That(view.transform.localScale.x, Is.EqualTo(motion.EndScaleMultiplier).Within(0.001f));
        Assert.That(GetText(view).alpha, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ReducedMotionAppliesAuthoredLateralMultiplierAndSuppressesBurnRepunch()
    {
        _profile.ReducedMotionLateralMultiplier = 0.25f;
        _profile.NormalMotion.HorizontalSpeed = 20f;
        _profile.NormalMotion.InitialJitterX = 10f;
        _profile.ReducedMotion.HorizontalSpeed = 20f;
        _profile.ReducedMotion.InitialJitterX = 10f;
        _profile.Sanitize();

        CombatTextView normal = CreateView(0);
        CombatTextView reduced = CreateView(1);
        Play(normal, _profile.NormalMotion, isBurnTally: false, deterministicSeed: 731);
        Play(reduced, _profile.ReducedMotion, isBurnTally: true, deterministicSeed: 731);

        Vector3 normalVelocity = (Vector3)VelocityField.GetValue(normal);
        Vector3 reducedVelocity = (Vector3)VelocityField.GetValue(reduced);
        Vector3 normalOffset = (Vector3)SpawnOffsetField.GetValue(normal);
        Vector3 reducedOffset = (Vector3)SpawnOffsetField.GetValue(reduced);

        Assert.That(reducedVelocity.x, Is.EqualTo(normalVelocity.x * 0.25f).Within(0.001f));
        Assert.That(reducedOffset.x, Is.EqualTo(normalOffset.x * 0.25f).Within(0.001f));

        reduced.Tick(_profile.ReducedMotion.SettleTime, _camera);
        CombatTextMergePresentation merge = new(
            20,
            CombatTextStyleId.Burn,
            CombatTextPriority.BurnTally,
            DamageFeedbackKind.Burn,
            false,
            false,
            false,
            false,
            false,
            1f,
            0.4f,
            0.1f,
            0f);
        reduced.Merge(in merge);
        reduced.Tick(0f, _camera);

        Assert.That(reduced.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void SpatialMotionUsesWorldUnitsWorldUpAndCapturedCameraRight()
    {
        CombatTextMotionSettings motion = _profile.NormalMotion;
        _profile.WorldUnitsPerMotionUnit = 0.02f;
        motion.Lifetime = 2f;
        motion.ConnectionDuration = 0f;
        motion.HorizontalSpeed = 4f;
        motion.UpwardSpeed = 5f;
        motion.DownwardAcceleration = 0f;
        motion.InitialJitterX = 0f;
        motion.InitialJitterY = 0f;
        motion.LocalShakeAmplitude = 0f;
        _profile.Sanitize();

        _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Vector3 capturedCameraRight = _camera.transform.right.normalized;
        const int seed = 123;
        Vector3 anchor = new(1f, 2f, 3f);
        CombatTextView view = CreateView();
        Play(
            view,
            motion,
            isBurnTally: false,
            deterministicSeed: seed,
            worldPosition: anchor);

        _camera.transform.rotation = Quaternion.Euler(0f, -35f, 0f);
        const float delta = 0.5f;
        view.Tick(delta, _camera);

        float lateralSpeed = HashSignedForTest(seed ^ 0x7f4a7c15) *
            motion.HorizontalSpeed * _profile.WorldUnitsPerMotionUnit;
        Vector3 expected = anchor +
            capturedCameraRight * (lateralSpeed * delta) +
            Vector3.up * (motion.UpwardSpeed * _profile.WorldUnitsPerMotionUnit * delta);
        Assert.That(Vector3.Distance(view.transform.position, expected), Is.LessThan(0.0001f));
    }

    [Test]
    public void BillboardTracksCameraWithoutChangingItsWorldPosition()
    {
        CombatTextMotionSettings motion = _profile.NormalMotion;
        motion.HorizontalSpeed = 0f;
        motion.UpwardSpeed = 0f;
        motion.DownwardAcceleration = 0f;
        motion.InitialJitterX = 0f;
        motion.InitialJitterY = 0f;
        _profile.Sanitize();

        CombatTextView view = CreateView();
        Vector3 anchor = new(2f, 3f, 5f);
        Play(view, motion, isBurnTally: false, worldPosition: anchor);
        Assert.That(view.transform.position, Is.EqualTo(anchor));

        _camera.transform.SetPositionAndRotation(
            new Vector3(-4f, 6f, -3f),
            Quaternion.Euler(18f, 31f, 0f));
        view.ApplyRenderPose(_camera);

        Quaternion expected = Quaternion.LookRotation(
            anchor - _camera.transform.position,
            _camera.transform.up);
        Assert.That(view.transform.position, Is.EqualTo(anchor));
        Assert.That(Quaternion.Angle(view.transform.rotation, expected), Is.LessThan(0.01f));
    }

    private CombatTextView CreateView(int index = 0)
    {
        Assert.That(CreateProgrammaticMethod, Is.Not.Null);
        return (CombatTextView)CreateProgrammaticMethod.Invoke(
            null,
            new object[] { _worldRoot.transform, _profile, index });
    }

    private void Play(
        CombatTextView view,
        CombatTextMotionSettings motion,
        bool isBurnTally,
        float resolvedScale = 1f,
        int deterministicSeed = 41,
        Vector3 worldPosition = default)
    {
        CombatTextPresentation presentation = new(
            10,
            isBurnTally ? CombatTextStyleId.Burn : CombatTextStyleId.Normal,
            isBurnTally ? CombatTextPriority.BurnTally : CombatTextPriority.AutomaticDirect,
            isBurnTally ? DamageFeedbackKind.Burn : DamageFeedbackKind.Direct,
            false,
            false,
            false,
            isBurnTally,
            false,
            false,
            false,
            worldPosition,
            resolvedScale,
            deterministicSeed,
            motion);
        view.Play(in presentation, _camera);
    }

    private static TextMeshPro GetText(CombatTextView view) =>
        view.GetComponentInChildren<TextMeshPro>(true);

    private static float HashSignedForTest(int value)
    {
        unchecked
        {
            uint hash = (uint)value;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return ((hash & 0x00ffffff) / 16777215f) * 2f - 1f;
        }
    }
}
