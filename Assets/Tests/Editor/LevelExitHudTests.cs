using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class LevelExitHudTests
{
    private readonly List<GameObject> _objects = new();
    private readonly Dictionary<FieldInfo, object> _previousStatics = new();
    private GameObject _owner;
    private GameObject _panel;
    private LevelExitHud _hud;
    private LevelExitObjective _objective;
    private ExitDoor _door;
    private TextMeshProUGUI _text;
    private float _previousTimeScale;

    [SetUp]
    public void SetUp()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        PreserveStatic(typeof(GameManager), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(LevelExitObjective), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(TutorialDirector), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(GameplayPause), "_locks", 0);

        _owner = CreateObject("Objective HUD", typeof(RectTransform), typeof(Canvas));
        _objective = _owner.AddComponent<LevelExitObjective>();
        _door = _owner.AddComponent<ExitDoor>();
        Set(_door, "_exitObjective", _objective);
        Set(_door, "_chargeDurationSeconds", 20f);
        _hud = _owner.AddComponent<LevelExitHud>();
        Set(_hud, "_exitObjective", _objective);
        Set(_hud, "_exitDoor", _door);
        // An inactive, unconfigured pressure object prevents runtime discovery of
        // unrelated scene systems without starting overheat or changing spawn state.
        LevelExitPressure pressure = CreateObject("Inactive exit pressure").AddComponent<LevelExitPressure>();
        Set(_hud, "_exitPressure", pressure);
        _hud.AuthorUi(_owner.transform);
        // Authoring may create its own child canvas when this owner is inactive.
        // Use the presenter's saved bindings, as runtime does, instead of assuming a hierarchy depth.
        _panel = Get<GameObject>(_hud, "_root");
        _text = Get<TextMeshProUGUI>(_hud, "_statusText");
        Assert.That(_panel, Is.Not.Null);
        Assert.That(_text, Is.Not.Null);

        _owner.SetActive(true);
        Set(_door, "_craftingUi", null);
        Set(_door, "_levelUpChoiceUi", null);
        _door.ResetCharge();
        // Explicit lifecycle calls make the event path deterministic in Edit Mode.
        Call(_door, "OnDisable");
        Call(_door, "OnEnable");
        Call(_hud, "OnDisable");
        Call(_hud, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        if (_hud != null) Call(_hud, "OnDisable");
        if (_door != null) Call(_door, "OnDisable");
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        foreach (KeyValuePair<FieldInfo, object> entry in _previousStatics)
            entry.Key.SetValue(null, entry.Value);
        _previousStatics.Clear();
        Time.timeScale = _previousTimeScale;
    }

    [Test]
    public void BatteryDiscoveryThenEscapeStatesKeepTheirExistingPriority()
    {
        Assert.That(_panel.activeSelf, Is.True);
        Assert.That(_text.text, Is.EqualTo("Find a way to escape"));
        _objective.RegisterKey();
        Assert.That(_text.text, Is.EqualTo("Batteries: 1/2"));

        _objective.RegisterKey();
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.AwaitingActivation));
        Assert.That(_text.text, Is.EqualTo("¡Salida desbloqueada! Acercate a la puerta y pulsa [E]"));
        Assert.That(_door.TryStartCharging(), Is.True);
        Call(_door, "TickCharge", 10f);
        Assert.That(_text.text, Is.EqualTo("Abriendo: 50% - salta y atravesala"));
        Call(_door, "TickCharge", 10f);
        Assert.That(_text.text, Is.EqualTo("Salida abierta - atravesala para escapar"));

        // This test only renders the completed state; it never submits a run or saves a profile.
        Set(_door, "_state", ExitDoorState.Used);
        Call(_hud, "Refresh");
        Assert.That(_panel.activeSelf, Is.False);
    }

    [Test]
    public void TutorialHidesDiscoveryAndExitMessagesUntilItFinishes()
    {
        TutorialDirector tutorial = CreateObject("Inactive tutorial fixture").AddComponent<TutorialDirector>();
        Set(tutorial, "_active", true);
        SetStatic(typeof(TutorialDirector), "<Instance>k__BackingField", tutorial);
        Call(_hud, "Refresh");
        Assert.That(_panel.activeSelf, Is.False);
        Set(_door, "_state", ExitDoorState.Ready);
        Call(_hud, "Refresh");
        Assert.That(_panel.activeSelf, Is.False, "The tutorial takes priority even over an open exit.");

        Set(tutorial, "_active", false);
        Call(_hud, "Refresh");
        Assert.That(_panel.activeSelf, Is.True);
        Assert.That(_text.text, Is.EqualTo("Salida abierta - atravesala para escapar"));
    }

    [Test]
    public void InspectorCopyAndColorEditsSurviveRefreshWithoutReplacingTheAuthoredView()
    {
        Image background = _panel.GetComponent<Image>();
        Assert.That(background, Is.Not.Null);
        Assert.That(background.color.a, Is.InRange(.01f, .99f));
        Assert.That(background.raycastTarget, Is.False);
        Color panelColor = new(.12f, .2f, .3f, .65f);
        Color textColor = new(.6f, .9f, 1f, 1f);
        background.color = panelColor;
        _text.fontSizeMin = 19f;
        Set(_hud, "_discoveryText", "Look for an exit");
        Set(_hud, "_batteryProgressText", "Cells {collected} of {required}");
        Set(_hud, "_objectiveColor", textColor);
        int childCount = _owner.GetComponentsInChildren<Transform>(true).Length;

        _hud.AuthorUi(_owner.transform);
        Call(_hud, "Refresh");
        Assert.That(_text.text, Is.EqualTo("Look for an exit"));
        _objective.RegisterKey();
        Assert.That(_text.text, Is.EqualTo("Cells 1 of 2"));
        Assert.That(_text.color, Is.EqualTo(textColor));
        Assert.That(background.color, Is.EqualTo(panelColor));
        Assert.That(_text.fontSizeMin, Is.EqualTo(19f));
        Assert.That(_owner.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(childCount));
    }

    private GameObject CreateObject(string name, params Type[] components)
    {
        var value = new GameObject(name, components);
        value.SetActive(false);
        _objects.Add(value);
        return value;
    }

    private void PreserveStatic(Type type, string name, object replacement)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        _previousStatics.Add(field, field.GetValue(null));
        field.SetValue(null, replacement);
    }

    private static void SetStatic(Type type, string name, object value) => type
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static T Get<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void Call(object target, string name, params object[] arguments) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
}
