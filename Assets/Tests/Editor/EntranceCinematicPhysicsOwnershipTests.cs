using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Focused lifecycle tests for the cinematic's Rigidbody hold. The native menu/low-frame-rate
/// fixture covers scheduler ordering; these tests isolate ownership and interruption cleanup.
/// No production scene, prefab, save, or player preference is modified.
/// </summary>
public sealed class EntranceCinematicPhysicsOwnershipTests
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _created = new();
    private readonly List<EntranceCinematic> _intros = new();
    private SceneSetup[] _previousSetup;
    private bool _openedScene;
    private float _previousTimeScale;
    private Vector3 _previousGravity;
    private SimulationMode _previousSimulation;
    private int _previousPauseLocks;

    [SetUp]
    public void SetUp()
    {
        Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
        // Dedicated batch runs own Unity's temporary unsaved startup scene.
        for (int index = 0; !Application.isBatchMode && index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            Assert.That(scene.isDirty, Is.False, "Save or discard dirty scenes before this fixture: " + scene.name);
            Assert.That(string.IsNullOrEmpty(scene.path) && scene.rootCount > 0, Is.False,
                "Save an untitled scene with content before this fixture.");
        }
        _previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(_previousSetup.Length > 1 && _previousSetup.Any(scene => string.IsNullOrEmpty(scene.path)), Is.False,
            "Save untitled additive scenes so the original setup can be restored.");
        _previousTimeScale = Time.timeScale;
        _previousGravity = Physics.gravity;
        _previousSimulation = Physics.simulationMode;
        _previousPauseLocks = GameplayPause.LockCount;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _openedScene = true;
        GameplayPause.Reset();
        Time.timeScale = 1f;
        Physics.gravity = new Vector3(0f, -18f, 0f);
        Physics.simulationMode = SimulationMode.Script;
        EntranceCinematic.PrepareNewRun();
    }

    [TearDown]
    public void TearDown()
    {
        if (!_openedScene) return;
        try
        {
            foreach (EntranceCinematic intro in _intros)
                if (intro != null) Call(intro, "OnDisable");
            for (int index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
        }
        finally
        {
            _created.Clear();
            _intros.Clear();
            Physics.simulationMode = _previousSimulation;
            Physics.gravity = _previousGravity;
            GameplayPause.Reset();
            for (int index = 0; index < _previousPauseLocks; index++) GameplayPause.Push();
            Time.timeScale = _previousTimeScale;
            EntranceCinematic.PrepareNewRun();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (_previousSetup != null && _previousSetup.Length > 0
                && _previousSetup.All(scene => !string.IsNullOrEmpty(scene.path)))
                EditorSceneManager.RestoreSceneManagerSetup(_previousSetup);
            _openedScene = false;
        }
    }

    [Test]
    public void AwakeHold_PreventsPhysicsDescentBeforeAnyPresentationCapture()
    {
        Rig rig = CreateRig(false, RigidbodyInterpolation.None);
        Rigidbody unheld = Track(new GameObject("Unheld gravity control")).AddComponent<Rigidbody>();
        unheld.position = new Vector3(4f, 5f, 0f);
        float heldStart = rig.Body.position.y;
        float controlStart = unheld.position.y;

        Call(rig.Intro, "Awake");
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(rig.Body.isKinematic, Is.True, "The body must be frozen before the startup coroutine captures the rig.");
        Physics.SyncTransforms();
        // Physics.Simulate deliberately runs despite timeScale == 0. The falling control
        // proves that this is a real native physics check, not a paused-world no-op.
        for (int tick = 0; tick < 17; tick++) Physics.Simulate(.02f);

        Assert.That(unheld.position.y, Is.LessThan(controlStart - 1f));
        Assert.That(rig.Body.position.y, Is.EqualTo(heldStart).Within(.00001f));
        Assert.That(rig.Player.transform.position.y, Is.EqualTo(heldStart).Within(.00001f));
        Assert.That(rig.Intro.IsFinished, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisableBeforeCapture_RestoresOriginalBodyWithoutTouchingLaterInterpolation(bool originallyKinematic)
    {
        Rig rig = CreateRig(originallyKinematic, RigidbodyInterpolation.None);
        GameplayPause.Push(); // A different UI owner is already holding the world.
        Time.timeScale = 0f;
        Call(rig.Intro, "Awake");
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None),
            "The early hold must not snapshot or override interpolation before motor initialization.");
        rig.Body.interpolation = RigidbodyInterpolation.Interpolate;

        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.EqualTo(originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1), "Only the cinematic's pause ownership is released.");
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(EntranceCinematic.StartupHeld, Is.False);

        rig.Body.isKinematic = !originallyKinematic;
        rig.Body.interpolation = RigidbodyInterpolation.Extrapolate;
        rig.Intro.RestorePresentation();
        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.EqualTo(!originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Extrapolate));
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1));
    }

    [Test]
    public void CaptureAfterPlayerMovementAwake_RestoresTheMotorsInterpolationSetting()
    {
        Rig rig = CreateRig(false, RigidbodyInterpolation.None);
        Call(rig.Intro, "Awake");
        PlayerMovement movement = rig.Player.AddComponent<PlayerMovement>();
        Call(movement, "Awake");
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
        Assert.That(rig.Body.isKinematic, Is.True, "Motor initialization must not release the startup body hold.");

        rig.Intro.Sample(4f);
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        rig.Intro.RestorePresentation();

        Assert.That(rig.Body.isKinematic, Is.False, "Capture must retain the pre-Awake dynamic state, not recapture frozen=true.");
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate),
            "Restore the initialized motor setting, not the prefab's earlier None value.");
        Assert.That(movement.enabled, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CapturedInterruption_RestoresOriginalPhysicsAndDoesNotOverwriteANewOwner(bool originallyKinematic)
    {
        Rig rig = CreateRig(originallyKinematic, RigidbodyInterpolation.Interpolate);
        Vector3 originalVisual = rig.Visual.localPosition;
        Call(rig.Intro, "Awake");
        rig.Intro.Sample(4f);
        Assert.That(rig.Body.isKinematic, Is.True);
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));

        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.EqualTo(originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
        Assert.That(rig.Visual.localPosition, Is.EqualTo(originalVisual));
        Assert.That(GameplayPause.LockCount, Is.Zero);

        rig.Body.isKinematic = !originallyKinematic;
        rig.Body.interpolation = RigidbodyInterpolation.Extrapolate;
        rig.Intro.RestorePresentation();
        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.EqualTo(!originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Extrapolate));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CompletedPresentation_ReleasesBodyBeforeTheTwoPhysicsTickInterpolationDelay(bool originallyKinematic)
    {
        Rig rig = CreateRig(originallyKinematic, RigidbodyInterpolation.Interpolate);
        Call(rig.Intro, "Awake");
        Call(rig.Intro, "FinishPresentation");
        Assert.That(rig.Body.isKinematic, Is.EqualTo(originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));

        IEnumerator delayed = (IEnumerator)Call(rig.Intro, "RestoreInterpolationAfterPhysics");
        Assert.That(delayed.MoveNext(), Is.True);
        Assert.That(delayed.Current, Is.TypeOf<WaitForFixedUpdate>());
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        Assert.That(delayed.MoveNext(), Is.True);
        Assert.That(delayed.Current, Is.TypeOf<WaitForFixedUpdate>());
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        Assert.That(delayed.MoveNext(), Is.False);
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
        Assert.That(rig.Body.isKinematic, Is.EqualTo(originallyKinematic));
    }

    [Test]
    public void DisableDuringPendingDelay_RestoresInterpolationAndMakesStaleCleanupHarmless()
    {
        Rig rig = CreateRig(false, RigidbodyInterpolation.Interpolate);
        Call(rig.Intro, "Awake");
        Call(rig.Intro, "FinishPresentation");
        IEnumerator delayed = (IEnumerator)Call(rig.Intro, "RestoreInterpolationAfterPhysics");
        Assert.That(delayed.MoveNext(), Is.True);

        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.False);
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
        rig.Body.isKinematic = true;
        rig.Body.interpolation = RigidbodyInterpolation.Extrapolate;

        Assert.That(delayed.MoveNext(), Is.True);
        Assert.That(delayed.MoveNext(), Is.False);
        rig.Intro.RestorePresentation();
        Call(rig.Intro, "OnDisable");
        Assert.That(rig.Body.isKinematic, Is.True, "A later physics owner is not overwritten by a stale restore.");
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Extrapolate));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void PreviewOrLatePlayerBinding_CapturesAndRestoresOriginalPhysics(bool awakeBeforeBinding, bool originallyKinematic)
    {
        Rig rig = CreateRig(originallyKinematic, RigidbodyInterpolation.Extrapolate, bindPlayer: false);
        if (awakeBeforeBinding) Call(rig.Intro, "Awake");
        Set(rig.Intro, "_player", rig.Player.transform);
        rig.Intro.Sample(4f);
        Assert.That(rig.Body.isKinematic, Is.True);
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));

        rig.Intro.RestorePresentation();
        Assert.That(rig.Body.isKinematic, Is.EqualTo(originallyKinematic));
        Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Extrapolate));
    }

    [Test]
    public void RepeatedRetryInstances_RestoreDynamicBodyAndReleaseEachHoldOnlyOnce()
    {
        for (int launch = 0; launch < 2; launch++)
        {
            Rig rig = CreateRig(false, RigidbodyInterpolation.Interpolate);
            Call(rig.Intro, "Awake");
            EntranceCinematic.PrepareRetry();
            IEnumerator retry = rig.Intro.PlayEntrance();
            try
            {
                Assert.That(retry.MoveNext(), Is.True);
                Assert.That(rig.Intro.IsFinished, Is.True, "Retry must finalize immediately on each fresh instance.");
                Assert.That(rig.Body.isKinematic, Is.False);
                rig.Intro.ReleaseForSelection();
                IEnumerator delayed = (IEnumerator)Call(rig.Intro, "RestoreInterpolationAfterPhysics");
                Assert.That(delayed.MoveNext(), Is.True);
                Assert.That(delayed.MoveNext(), Is.True);
                Assert.That(delayed.MoveNext(), Is.False);
                Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));

                rig.Body.isKinematic = true;
                rig.Body.interpolation = RigidbodyInterpolation.Extrapolate;
                Call(rig.Intro, "OnDisable");
                Call(rig.Intro, "OnDisable");
                Assert.That(rig.Body.isKinematic, Is.True);
                Assert.That(rig.Body.interpolation, Is.EqualTo(RigidbodyInterpolation.Extrapolate));
                Assert.That(GameplayPause.LockCount, Is.Zero);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(EntranceCinematic.StartupHeld, Is.False);
            }
            finally { (retry as IDisposable)?.Dispose(); }
            Object.DestroyImmediate(rig.Intro.gameObject);
            Object.DestroyImmediate(rig.Player);
        }
    }

    private Rig CreateRig(bool originallyKinematic, RigidbodyInterpolation interpolation, bool bindPlayer = true)
    {
        GameObject player = Track(new GameObject("Cinematic physics fixture"));
        player.layer = 2; // Exclude the fixture's own capsule from the cinematic floor ray.
        player.transform.position = new Vector3(0f, 5f, 0f);
        player.AddComponent<CapsuleCollider>();
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.isKinematic = originallyKinematic;
        body.interpolation = interpolation;
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(player.transform, false);
        visual.localPosition = new Vector3(.1f, -.3f, .2f);
        Camera camera = Track(new GameObject("Cinematic fixture camera")).AddComponent<Camera>();
        camera.transform.position = new Vector3(3f, 7f, -5f);
        AnimationClip clip = Track(new AnimationClip { name = "Fixture entrance", legacy = true });
        clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0f, 13f, -.75f));
        EntranceCinematic intro = Track(new GameObject("Cinematic fixture director")).AddComponent<EntranceCinematic>();
        _intros.Add(intro);
        if (bindPlayer) Set(intro, "_player", player.transform);
        Set(intro, "_visual", visual);
        Set(intro, "_camera", camera);
        Set(intro, "_entrance", clip);
        Physics.SyncTransforms();
        return new Rig { Player = player, Body = body, Visual = visual, Intro = intro };
    }

    private T Track<T>(T value) where T : Object { _created.Add(value); return value; }
    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, HiddenInstance);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }
    private static object Call(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, HiddenInstance);
        Assert.That(method, Is.Not.Null, name);
        return method.Invoke(target, null);
    }
    private sealed class Rig
    {
        public GameObject Player;
        public Rigidbody Body;
        public Transform Visual;
        public EntranceCinematic Intro;
    }
}
