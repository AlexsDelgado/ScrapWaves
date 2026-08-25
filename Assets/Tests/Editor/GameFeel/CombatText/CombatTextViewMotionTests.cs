using System.Reflection;
using NUnit.Framework;
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
    private GameObject _canvasRoot;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        _canvasRoot = new GameObject("CombatTextViewMotionTests", typeof(RectTransform));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_canvasRoot);
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
        view.Tick(peakTime);
        Assert.That(view.transform.localScale.x, Is.EqualTo(2.6f).Within(0.001f));

        view.Tick(motion.SettleTime - peakTime);
        Assert.That(view.transform.localScale.x, Is.EqualTo(2f).Within(0.001f));
        Assert.That(view.IsFading, Is.False);

        view.Tick(0.31f);
        Assert.That(view.IsFading, Is.True);
        Assert.That(view.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f));
        Assert.That(view.transform.localScale.x, Is.LessThan(2f));

        Assert.That(view.Tick(0.50f), Is.True);
        Assert.That(view.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(0.001f));
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
        view.Tick(peakTime);
        Assert.That(view.transform.localScale.x, Is.EqualTo(motion.PopOvershoot).Within(0.001f));

        view.Tick(motion.SettleTime - peakTime);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));

        Assert.That(view.Tick(1f), Is.False);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(view.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.001f));

        view.BeginRelease();
        view.Tick(0f);
        Assert.That(view.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));

        Assert.That(view.Tick(motion.Lifetime), Is.True);
        Assert.That(view.transform.localScale.x, Is.EqualTo(motion.EndScaleMultiplier).Within(0.001f));
        Assert.That(view.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(0.001f));
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

        Vector2 normalVelocity = (Vector2)VelocityField.GetValue(normal);
        Vector2 reducedVelocity = (Vector2)VelocityField.GetValue(reduced);
        Vector2 normalOffset = (Vector2)SpawnOffsetField.GetValue(normal);
        Vector2 reducedOffset = (Vector2)SpawnOffsetField.GetValue(reduced);

        Assert.That(reducedVelocity.x, Is.EqualTo(normalVelocity.x * 0.25f).Within(0.001f));
        Assert.That(reducedOffset.x, Is.EqualTo(normalOffset.x * 0.25f).Within(0.001f));

        reduced.Tick(_profile.ReducedMotion.SettleTime);
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
        reduced.Tick(0f);

        Assert.That(reduced.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
    }

    private CombatTextView CreateView(int index = 0)
    {
        Assert.That(CreateProgrammaticMethod, Is.Not.Null);
        return (CombatTextView)CreateProgrammaticMethod.Invoke(
            null,
            new object[] { _canvasRoot.transform as RectTransform, _profile, index });
    }

    private static void Play(
        CombatTextView view,
        CombatTextMotionSettings motion,
        bool isBurnTally,
        float resolvedScale = 1f,
        int deterministicSeed = 41)
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
            Vector2.zero,
            resolvedScale,
            deterministicSeed,
            motion);
        view.Play(in presentation);
    }
}
