using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Focused EditMode integration with real ground support and Overheat lifecycle events.</summary>
public sealed class OverheatWarningHudTests
{
    private readonly List<GameObject> _created = new();
    private EnemyEmergenceTestLifecycle _lifecycle;
    private PlayerPlatformState _platform;
    private PlayerHealth _health;
    private OverheatManager _overheat;
    private OverheatWarningHud _warning;
    private GameObject _view;
    private CanvasGroup _visibility;
    private TextMeshProUGUI _text;
    private TextMeshProUGUI _repeatText;
    private RectTransform _motion;
    private float _timeScale;
    private int _pauseLocks;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _lifecycle = new EnemyEmergenceTestLifecycle();
        _timeScale = Time.timeScale;
        _pauseLocks = GameplayPause.LockCount;
        Time.timeScale = 1f;
        GameplayPause.Reset();

        GameObject floor = NewObject("Warning test Base");
        floor.transform.position = new Vector3(0f, -.5f, 0f);
        floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
        floor.SetActive(true);
        GameObject region = NewObject("Warning test upper region");
        region.transform.position = Vector3.up * 10f;
        BoxCollider box = region.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(100f, 10f, 100f);
        UpperLevelVolume volume = region.AddComponent<UpperLevelVolume>();
        Set(volume, "_baseSurfaceRoots", new[] { floor.transform });
        _lifecycle.SetActive(region, true);

        GameObject actor = NewObject("Warning test player");
        CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = .5f;
        actor.AddComponent<Rigidbody>().useGravity = false;
        PlayerMovement movement = actor.AddComponent<PlayerMovement>();
        Set(movement, "_useFrictionlessMovementMaterial", false);
        _health = actor.AddComponent<PlayerHealth>();
        _platform = actor.AddComponent<PlayerPlatformState>();
        Set(_platform, "_upperLevelVolume", volume);
        _lifecycle.SetActive(actor, true);
        MovePlayer(11f);

        GameObject manager = NewObject("Warning test Overheat");
        _overheat = manager.AddComponent<OverheatManager>();
        Set(_overheat, "_playerPlatformState", _platform);
        _lifecycle.SetActive(manager, true);

        _view = NewObject("Authored warning fixture", typeof(RectTransform), typeof(CanvasGroup), typeof(RectMask2D));
        ((RectTransform)_view.transform).sizeDelta = new Vector2(1920f, 112f);
        _visibility = _view.GetComponent<CanvasGroup>();
        _visibility.alpha = 0f;
        _visibility.blocksRaycasts = false;
        _visibility.interactable = false;
        GameObject label = new("Authored moving text", typeof(RectTransform));
        label.transform.SetParent(_view.transform, false);
        _motion = (RectTransform)label.transform;
        _motion.sizeDelta = new Vector2(1500f, 100f);
        _text = label.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_text);
        _text.raycastTarget = false;
        GameObject repeat = new("Authored repeating text", typeof(RectTransform));
        repeat.transform.SetParent(_view.transform, false);
        _repeatText = repeat.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_repeatText);
        _repeatText.raycastTarget = false;
        _warning = _view.AddComponent<OverheatWarningHud>();
        Set(_warning, "_overheatManager", _overheat);
        Set(_warning, "_playerPlatformState", _platform);
        Set(_warning, "_root", _view);
        Set(_warning, "_text", _text);
        Set(_warning, "_repeatText", _repeatText);
        Set(_warning, "_visibility", _visibility);
        Set(_warning, "_motion", _motion);
        _lifecycle.SetActive(_view, true);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) _lifecycle.Destroy(_created[i]);
        _created.Clear();
        Time.timeScale = _timeScale;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void Upstairs_RemainsVisibleAcrossManyPassesAndReenable_WithoutCountingDownGroundDelay()
    {
        StartOverheat();
        Tick(0f);
        float startX = _motion.anchoredPosition.x;
        Tick(.3f);
        Assert.That(_motion.anchoredPosition.x, Is.Not.EqualTo(startX), "The actual saved text transform must move.");
        for (int i = 0; i < 24; i++)
        {
            TickPreparation(2.5f);
            Tick(2.5f);
            Assert.That(_visibility.alpha, Is.EqualTo(1f), "Waiting upstairs must have no pass or time budget.");
            Assert.That(HasVisibleLabel(_text) || HasVisibleLabel(_repeatText), Is.True,
                "The authored repeat copy must cover each marquee wrap.");
        }
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(5f));
        Assert.That(_warning.ElapsedDisplayTime, Is.GreaterThan(60f));
        Assert.That(_view.transform.childCount, Is.EqualTo(2), "The presenter must not generate replacement labels.");
        Vector2 position = _motion.anchoredPosition;
        _lifecycle.SetActive(_view, false);
        Assert.That(_visibility.alpha, Is.Zero);
        _lifecycle.SetActive(_view, true);
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(_motion.anchoredPosition, Is.EqualTo(position));
    }

    [TestCase(5f)]
    [TestCase(8.5f)]
    public void GroundWarning_LastUntilAuthoritativeManagerDelayCompletes(float duration)
    {
        Set(_overheat, "_preparationDuration", duration);
        MovePlayer(1.02f);
        StartOverheat();
        Tick(30f);
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(duration));
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET READY"));
        TickPreparation(duration - .01f);
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f), "HUD animation time must not independently expire the warning.");
        TickPreparation(.02f);
        Assert.That(_overheat.AreEncountersReady, Is.True);
        Assert.That(_visibility.alpha, Is.Zero, "The manager transition must hide the warning immediately.");
        Tick(30f);
        _lifecycle.SetActive(_view, false);
        _lifecycle.SetActive(_view, true);
        Tick(0f);
        Assert.That(_visibility.alpha, Is.Zero);
    }

    [Test]
    public void Pauses_HideAndFreezeMotionAndGroundPreparation_ThenResumeSameRemainder()
    {
        MovePlayer(1.02f);
        StartOverheat();
        TickPreparation(1f);
        Tick(.35f);
        float elapsed = _warning.ElapsedDisplayTime;
        Vector2 position = _motion.anchoredPosition;
        GameplayPause.Push();
        TickPreparation(10f);
        Tick(10f);
        Assert.That(_visibility.alpha, Is.Zero);
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(4f));
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed));
        Assert.That(_motion.anchoredPosition, Is.EqualTo(position));
        GameplayPause.Pop();
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(_motion.anchoredPosition, Is.EqualTo(position));
        Time.timeScale = 0f;
        TickPreparation(10f);
        Tick(10f);
        Assert.That(_visibility.alpha, Is.Zero);
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(4f));
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed));
        Time.timeScale = 1f;
        TickPreparation(.2f);
        Tick(.2f);
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(3.8f).Within(.0001f));
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed + .2f).Within(.0001f));
        Assert.That(_motion.anchoredPosition, Is.Not.EqualTo(position));
    }

    [Test]
    public void LandingAndAscent_SwitchVisibleMessageWithoutGap_AndResumeExistingManagerDelay()
    {
        StartOverheat();
        Tick(.7f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET TO GROUND LEVEL"));
        MovePlayer(1.02f);
        Assert.That(_platform.IsBaseSupported, Is.True);
        Tick(0f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET READY"));
        Assert.That(_repeatText.text, Is.EqualTo(_text.text));
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(HasVisibleLabel(_text), Is.True, "The changed message must be immediately in view.");
        TickPreparation(2f);
        Tick(2f);
        MovePlayer(11f);
        Tick(0f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET TO GROUND LEVEL"));
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(HasVisibleLabel(_text), Is.True);
        TickPreparation(60f);
        Tick(60f);
        Assert.That(_overheat.PreparationTimeRemaining, Is.EqualTo(3f));
        MovePlayer(1.02f);
        Tick(0f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET READY"));
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        TickPreparation(2.99f);
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        TickPreparation(.02f);
        Assert.That(_visibility.alpha, Is.Zero);
    }

    [TestCase("event-end")]
    [TestCase("run-reset")]
    [TestCase("death")]
    [TestCase("manager-disabled")]
    public void Cancellation_HidesImmediatelyAndCannotReopenStaleWarning(string reason)
    {
        StartOverheat();
        Tick(.4f);
        switch (reason)
        {
            case "event-end": _overheat.NotifyOverheatObjectiveCleared(); break;
            case "run-reset": _health.FullHeal(resetPhase: true); break;
            case "manager-disabled": _lifecycle.SetActive(_overheat.gameObject, false); break;
            case "death":
                ChallengeProgressTracker previous = ChallengeProgressTracker.Instance;
                var tracker = NewObject("Warning death tracker").AddComponent<ChallengeProgressTracker>();
                var instance = typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance));
                instance.SetValue(null, tracker);
                try { _health.TakeDamage(1000); }
                finally { instance.SetValue(null, previous); }
                break;
        }
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_visibility.alpha, Is.Zero);
        Tick(30f);
        _lifecycle.SetActive(_view, false);
        _lifecycle.SetActive(_view, true);
        Tick(0f);
        Assert.That(_visibility.alpha, Is.Zero, "Re-enabling the authored view must not resurrect a cancelled event.");
        _health.FullHeal(resetPhase: true);
        if (!_overheat.gameObject.activeSelf) _lifecycle.SetActive(_overheat.gameObject, true);
        StartOverheat();
        Tick(0f);
        Assert.That(_warning.ElapsedDisplayTime, Is.Zero);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
    }

    [Test]
    public void EditorControls_ChangeSpeedFontAndBar_WithoutAutosizingOrGeneratingUi()
    {
        Set(_warning, "_scrollSpeed", 90f);
        Set(_warning, "_fontSize", 128f);
        Set(_warning, "_barHeight", 200f);
        Set(_warning, "_messageGap", 96f);
        StartOverheat();
        Tick(0f);
        Vector2 before = _motion.anchoredPosition;
        Tick(.5f);
        Assert.That(_motion.anchoredPosition.x, Is.EqualTo(before.x - 45f).Within(.001f));
        Assert.That(((RectTransform)_view.transform).rect.height, Is.EqualTo(200f).Within(.001f));
        foreach (TextMeshProUGUI label in new[] { _text, _repeatText })
        {
            Assert.That(label.fontSize, Is.EqualTo(128f));
            Assert.That(label.enableAutoSizing, Is.False);
            Assert.That(label.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(label.raycastTarget, Is.False);
        }
        Assert.That(_view.GetComponent<RectMask2D>(), Is.Not.Null);
        Assert.That(_view.transform.childCount, Is.EqualTo(2));
        Assert.That(_visibility.blocksRaycasts, Is.False);
        Assert.That(_visibility.interactable, Is.False);
    }

    private void StartOverheat() => Invoke(_overheat, "OnMaxHeatReached");
    private void Tick(float elapsed) => Invoke(_warning, "Tick", elapsed);
    private void TickPreparation(float elapsed) => Invoke(_overheat, "TickPreparation", elapsed);
    private bool HasVisibleLabel(TextMeshProUGUI label)
    {
        var corners = new Vector3[4];
        label.rectTransform.GetWorldCorners(corners);
        var viewport = (RectTransform)_view.transform;
        float left = viewport.InverseTransformPoint(corners[0]).x;
        float right = viewport.InverseTransformPoint(corners[3]).x;
        return left < viewport.rect.xMax && right > viewport.rect.xMin;
    }
    private void MovePlayer(float y)
    {
        _platform.transform.position = new Vector3(0f, y, 0f);
        _platform.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    private GameObject NewObject(string name, params Type[] components)
    {
        GameObject go = new(name, components);
        go.SetActive(false);
        _created.Add(go);
        return go;
    }
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static object Invoke(object target, string name, params object[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(target.GetType().Name, name)).Invoke(target, args);
}
