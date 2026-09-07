using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public sealed class CompactorDoorPresentationTests
{
    private GameObject _root;
    private ExitDoor _door;
    private Animator _animator;
    private Transform _panel;
    private Renderer _suctionRenderer;
    private BoxCollider _collider;
    private AnimationClip _clip;
    private CompactorDoorPresentation _presentation;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Compactor presentation test");
        _door = _root.AddComponent<ExitDoor>();
        // The exit's game loop is not relevant to synchronous edit-mode sampling.
        _door.enabled = false;
        SetField(_door, "_exitObjective", null);
        SetField(_door, "_chargeDurationSeconds", 19f);

        GameObject model = new("Model");
        model.transform.SetParent(_root.transform, false);
        _animator = model.AddComponent<Animator>();
        _animator.cullingMode = AnimatorCullingMode.CullCompletely;
        _panel = new GameObject("Door Panel").transform;
        _panel.SetParent(model.transform, false);
        _suctionRenderer = new GameObject("Suction rectangle").AddComponent<MeshRenderer>();
        _suctionRenderer.transform.SetParent(_root.transform, false);
        _collider = _root.AddComponent<BoxCollider>();

        _clip = new AnimationClip { name = "Open", frameRate = 30f, wrapMode = WrapMode.ClampForever };
        AnimationUtility.SetEditorCurve(
            _clip,
            EditorCurveBinding.FloatCurve("Door Panel", typeof(Transform), "m_LocalPosition.y"),
            AnimationCurve.Linear(0f, 0f, 2.67f, 4f));
        AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(_clip);
        clipSettings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(_clip, clipSettings);

        _presentation = _root.AddComponent<CompactorDoorPresentation>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_presentation != null)
            _presentation.Dispose();
        if (_root != null)
            Object.DestroyImmediate(_root);
        if (_clip != null)
            Object.DestroyImmediate(_clip);
    }

    [Test]
    public void ChargeProgress_SamplesClosedMiddleAndFullOpen_IndependentOfClipDuration()
    {
        Configure();
        AssertPose(0f, true, 0f);

        SetCharge(ExitDoorState.Charging, 0f);
        RaiseEvent(_door, "OnChargeStarted");
        AssertPose(0f, true, 0.2f);

        SetCharge(ExitDoorState.Charging, 0.5f);
        RaiseProgress(0.5f);
        AssertPose(2f, true, 0.6f);
        Assert.That(_door.ChargeNormalized, Is.EqualTo(0.5f).Within(0.0001f));

        // The progress event can reach 1 before the authoritative state becomes Ready.
        SetCharge(ExitDoorState.Charging, 1f);
        RaiseProgress(1f);
        AssertPose(4f, true, 1f);

        SetCharge(ExitDoorState.Ready, 1f);
        RaiseEvent(_door, "OnDoorReady");
        AssertPose(4f, false, 1f);
    }

    [TestCase(ExitDoorState.Ready)]
    [TestCase(ExitDoorState.Used)]
    public void ReadyOrUsed_HoldsExactEndPose_WhenGraphEvaluatesForLongerThanClip(ExitDoorState state)
    {
        // Completion wins over any stale charge value when binding an already open exit.
        SetCharge(state, 0.2f);
        Configure();
        AssertPose(4f, false, 1f);

        PlayableGraph graph = GetField<PlayableGraph>(_presentation, "_graph");
        graph.Evaluate(30f);
        AssertPose(4f, false, 1f);
        Invoke(_presentation, "LateUpdate");
        AssertPose(4f, false, 1f);
    }

    [Test]
    public void LateBindingAndReset_ResampleCurrentProgressThenCloseAndRestoreCollision()
    {
        SetCharge(ExitDoorState.Charging, 0.75f);
        Configure();
        AssertPose(3f, true, 0.8f);

        SetCharge(ExitDoorState.Ready, 1f);
        RaiseEvent(_door, "OnDoorReady");
        AssertPose(4f, false, 1f);

        _door.ResetCharge();
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Locked));
        AssertPose(0f, true, 0f);

        SetCharge(ExitDoorState.AwaitingActivation, 1f);
        Invoke(_presentation, "LateUpdate");
        AssertPose(0f, true, 0f);
    }

    [Test]
    public void DisabledPresentation_UnsubscribesStopsSuctionAndRebindsToLatestState()
    {
        SetCharge(ExitDoorState.Charging, 0.5f);
        Configure();
        AssertPose(2f, true, 0.6f);

        _presentation.enabled = false;
        Invoke(_presentation, "OnDisable");
        Assert.That(GetField<PlayableGraph>(_presentation, "_graph").IsValid(), Is.False);
        Assert.That(_animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullCompletely));

        SetCharge(ExitDoorState.Ready, 1f);
        RaiseProgress(1f);
        RaiseEvent(_door, "OnDoorReady");
        Assert.That(Activity(), Is.Zero);
        Assert.That(_collider.enabled, Is.True, "The disabled component must not keep handling exit events.");

        Configure();
        Assert.That(GetField<PlayableGraph>(_presentation, "_graph").IsValid(), Is.False,
            "Configuring a disabled presentation must not create a running graph.");
        _presentation.enabled = true;
        Invoke(_presentation, "OnEnable");
        AssertPose(4f, false, 1f);
        Assert.That(GetField<PlayableGraph>(_presentation, "_graph").IsValid(), Is.True);
    }

    [Test]
    public void SuctionActivity_PreservesOtherRendererOverrides()
    {
        MaterialPropertyBlock properties = new();
        int otherId = Shader.PropertyToID("_OtherEffectSetting");
        properties.SetFloat(otherId, 0.37f);
        _suctionRenderer.SetPropertyBlock(properties);

        SetCharge(ExitDoorState.Charging, 0.5f);
        Configure();

        _suctionRenderer.GetPropertyBlock(properties);
        Assert.That(properties.GetFloat(otherId), Is.EqualTo(0.37f));
        Assert.That(Activity(), Is.EqualTo(0.6f).Within(0.001f));
    }

    private void Configure() => _presentation.Configure(_door, _animator, _clip, _suctionRenderer, _collider);

    private void SetCharge(ExitDoorState state, float normalized)
    {
        SetField(_door, "_state", state);
        SetField(_door, "_chargeRemaining", 19f * (1f - normalized));
    }

    private void AssertPose(float height, bool collisionEnabled, float activity)
    {
        Assert.That(_panel.localPosition.y, Is.EqualTo(height).Within(0.001f));
        Assert.That(_collider.enabled, Is.EqualTo(collisionEnabled));
        Assert.That(Activity(), Is.EqualTo(activity).Within(0.001f));
    }

    private float Activity()
    {
        MaterialPropertyBlock properties = new();
        _suctionRenderer.GetPropertyBlock(properties);
        return properties.GetFloat(Shader.PropertyToID("_Activity"));
    }

    private void RaiseProgress(float progress) => GetField<Action<float>>(_door, "OnChargeProgress")?.Invoke(progress);

    private static void RaiseEvent(object target, string eventName) => GetField<Action>(target, eventName)?.Invoke();

    private static void Invoke(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        method.Invoke(target, null);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(target);
    }
}
