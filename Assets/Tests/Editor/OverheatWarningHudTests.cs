using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        _view = NewObject("Authored warning fixture", typeof(RectTransform), typeof(CanvasGroup));
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
        _warning = _view.AddComponent<OverheatWarningHud>();
        Set(_warning, "_overheatManager", _overheat);
        Set(_warning, "_playerPlatformState", _platform);
        Set(_warning, "_root", _view);
        Set(_warning, "_text", _text);
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
    public void DefaultTwoCycles_MoveThenStayHidden_AcrossUpdatesAndReenable()
    {
        Assert.That(Get<int>(_warning, "_cycleCount"), Is.EqualTo(2));
        Assert.That(Get<OverheatWarningLifetimeMode>(_warning, "_lifetimeMode"), Is.EqualTo(OverheatWarningLifetimeMode.Cycles));
        float cycle = Get<float>(_warning, "_cycleSeconds");
        StartOverheat();
        Tick(0f);
        float startX = _motion.anchoredPosition.x;
        Tick(.3f);
        Assert.That(_motion.anchoredPosition.x, Is.Not.EqualTo(startX), "The actual saved text transform must move.");
        Tick(cycle - .3f);
        Assert.That(_warning.CompletedCycles, Is.EqualTo(1));
        Tick(cycle - .01f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Tick(.02f);
        Assert.That(_warning.HasExhaustedBudget, Is.True);
        Assert.That(_warning.CompletedCycles, Is.EqualTo(2));
        Assert.That(_visibility.alpha, Is.Zero);
        Assert.That(_view.activeSelf, Is.True, "The controller's own UI object must stay active to observe future events.");
        Tick(30f);
        _lifecycle.SetActive(_view, false);
        _lifecycle.SetActive(_view, true);
        Tick(0f);
        Assert.That(_warning.HasExhaustedBudget, Is.True);
        Assert.That(_visibility.alpha, Is.Zero, "A hidden warning must not start another budget every Update or OnEnable.");
    }

    [Test]
    public void DurationMode_UsesSecondsInsteadOfCycleCount()
    {
        Set(_warning, "_lifetimeMode", OverheatWarningLifetimeMode.Duration);
        Set(_warning, "_durationSeconds", 1.1f);
        Set(_warning, "_cycleCount", 20);
        StartOverheat();
        Tick(1.09f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Tick(.02f);
        Assert.That(_warning.HasExhaustedBudget, Is.True);
        Assert.That(_warning.CompletedCycles, Is.Zero);
        Assert.That(_visibility.alpha, Is.Zero);
    }

    [Test]
    public void Pauses_HideAndFreezeMotionAndBudget_ThenResumeRemainder()
    {
        StartOverheat();
        Tick(.35f);
        float elapsed = _warning.ElapsedDisplayTime;
        Vector2 position = _motion.anchoredPosition;
        GameplayPause.Push();
        Tick(10f);
        Assert.That(_visibility.alpha, Is.Zero);
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed));
        Assert.That(_motion.anchoredPosition, Is.EqualTo(position));
        GameplayPause.Pop();
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(_motion.anchoredPosition, Is.EqualTo(position));
        Time.timeScale = 0f;
        Tick(10f);
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed));
        Time.timeScale = 1f;
        Tick(.2f);
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(elapsed + .2f).Within(.0001f));
        Assert.That(_motion.anchoredPosition, Is.Not.EqualTo(position));
    }

    [Test]
    public void LandingAndAscent_ChangeMessageWithoutResettingLifetime()
    {
        StartOverheat();
        Tick(.7f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET TO GROUND LEVEL"));
        MovePlayer(1.02f);
        Assert.That(_platform.IsBaseSupported, Is.True);
        Tick(0f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET READY"));
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(.7f).Within(.0001f));
        MovePlayer(11f);
        Tick(.2f);
        Assert.That(_text.text, Is.EqualTo("OVERHEAT EVENT STARTING - GET TO GROUND LEVEL"));
        Assert.That(_warning.ElapsedDisplayTime, Is.EqualTo(.9f).Within(.0001f));
        float budget = Get<float>(_warning, "_cycleSeconds") * 2f;
        Tick(budget - .9f + .01f);
        Assert.That(_warning.HasExhaustedBudget, Is.True, "Moving between locations must not extend the display budget.");
        Assert.That(_visibility.alpha, Is.Zero);
    }

    [Test]
    public void EventEndAndExplicitRunReset_HideImmediately_NewEventRestarts()
    {
        StartOverheat();
        Tick(.4f);
        _overheat.NotifyOverheatObjectiveCleared();
        Assert.That(_visibility.alpha, Is.Zero);
        StartOverheat();
        Tick(0f);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        Assert.That(_warning.ElapsedDisplayTime, Is.Zero);
        Tick(.4f);
        _health.FullHeal(resetPhase: true);
        Assert.That(_overheat.IsOverheating, Is.False);
        Assert.That(_visibility.alpha, Is.Zero);
        StartOverheat();
        Tick(0f);
        Assert.That(_warning.ElapsedDisplayTime, Is.Zero);
        Assert.That(_warning.HasExhaustedBudget, Is.False);
        Assert.That(_visibility.alpha, Is.EqualTo(1f));
        MovePlayer(1.02f);
        Invoke(_overheat, "TickPreparation", 5f);
        Assert.That(_overheat.IsPreparing, Is.False);
        Assert.That(_visibility.alpha, Is.Zero, "Encounter transition ends the warning even with display budget remaining.");
    }

    private void StartOverheat() => Invoke(_overheat, "OnMaxHeatReached");
    private void Tick(float elapsed) => Invoke(_warning, "Tick", elapsed);
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
