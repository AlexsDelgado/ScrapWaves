using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class CraftingStationRotationTests
{
    private readonly List<Scene> _scenes = new List<Scene>();
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void Setup() => ResetRegistry();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _scenes.Count - 1; i >= 0; i--)
            if (_scenes[i].IsValid() && _scenes[i].isLoaded) EditorSceneManager.ClosePreviewScene(_scenes[i]);
        _scenes.Clear();
        ResetRegistry();
    }

    [Test]
    public void Initialization_ChoosesOneNormalStationAndKeepsAllRootsEnabled()
    {
        Scene scene = NewScene();
        var first = Station(scene);
        var second = Station(scene);
        var tutorial = Station(scene);
        tutorial.ConfigureForTutorial();
        var random = new SequenceRandom(indices: new[] { 1 });
        CraftingStationRotation.SetRandomSource(scene, random);
        CraftingStationRotation.InitializeScene(scene);

        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(second));
        Assert.That(first.IsAvailable, Is.False);
        Assert.That(second.IsAvailable, Is.True);
        Assert.That(tutorial.IsAvailable, Is.True);
        Assert.That(tutorial.ParticipatesInRotation, Is.False);
        Assert.That(first.gameObject.activeSelf && second.gameObject.activeSelf, Is.True);
        Assert.That(random.IndexBounds, Is.EqualTo(new[] { 2 }));
        Assert.That(random.RollCount, Is.Zero);
    }

    [Test]
    public void ZeroChanceSurvivesEvenZeroRoll_ThenAddsPercentagePointsExactlyOnce()
    {
        Scene scene = NewScene();
        var station = Station(scene, 0f, 5f);
        var random = new SequenceRandom(rolls: new[] { 0d, .05d, .049d });
        CraftingStationRotation.SetRandomSource(scene, random);
        CraftingStationRotation.InitializeScene(scene);

        Assert.That(station.CompleteTransaction(), Is.False);
        Assert.That(station.CurrentBreakChancePercent, Is.EqualTo(5f));
        Assert.That(station.CompleteTransaction(), Is.False, "A roll equal to the threshold survives.");
        Assert.That(station.CurrentBreakChancePercent, Is.EqualTo(10f));
        Assert.That(station.CompleteTransaction(), Is.True);
        Assert.That(station.CurrentBreakChancePercent, Is.Zero, "Every reactivation resets wear.");
        Assert.That(random.RollCount, Is.EqualTo(3));
    }

    [Test]
    public void BreakSelectsDifferentStationAndPreviouslyBrokenStationCanReturn()
    {
        Scene scene = NewScene();
        var first = Station(scene, 100f);
        var second = Station(scene, 100f);
        var third = Station(scene, 100f);
        var random = new SequenceRandom(indices: new[] { 0, 1, 0 }, rolls: new[] { .999d, .999d });
        CraftingStationRotation.SetRandomSource(scene, random);
        CraftingStationRotation.InitializeScene(scene);

        Assert.That(first.CompleteTransaction(), Is.True);
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(third));
        Assert.That(first.IsAvailable, Is.False);
        Assert.That(second.IsAvailable, Is.False);
        Assert.That(third.CompleteTransaction(), Is.True);
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(first));
        Assert.That(random.IndexBounds, Is.EqualTo(new[] { 3, 2, 2 }), "Each replacement uniformly indexes all other eligible placements.");
    }

    [Test]
    public void SingleStationReactivatesItselfAndPublishesAnotherActivation()
    {
        Scene scene = NewScene();
        var station = Station(scene, 0f, 100f);
        var random = new SequenceRandom(rolls: new[] { 0d, .999d });
        CraftingStationRotation.SetRandomSource(scene, random);
        int activations = 0;
        CraftingStationRotation.ActiveStationChanged += _ => activations++;
        CraftingStationRotation.InitializeScene(scene);

        Assert.That(station.CompleteTransaction(), Is.False);
        Assert.That(station.CompleteTransaction(), Is.True);
        Assert.That(station.IsAvailable, Is.True);
        Assert.That(station.CurrentBreakChancePercent, Is.Zero);
        Assert.That(activations, Is.EqualTo(2));
    }

    [Test]
    public void SurvivalClampsChanceAtOneHundred()
    {
        Scene scene = NewScene();
        var station = Station(scene, 95f, 10f);
        CraftingStationRotation.SetRandomSource(scene, new SequenceRandom(rolls: new[] { .99d }));
        CraftingStationRotation.InitializeScene(scene);
        Assert.That(station.CompleteTransaction(), Is.False);
        Assert.That(station.CurrentBreakChancePercent, Is.EqualTo(100f));
    }

    [Test]
    public void LateRegistrationDoesNotReplaceActiveStation_ActiveRemovalRecovers()
    {
        Scene scene = NewScene();
        var first = Station(scene);
        CraftingStationRotation.InitializeScene(scene);
        var late = Station(scene);
        CraftingStationRotation.Register(late);
        Assert.That(first.IsAvailable, Is.True);
        Assert.That(late.IsAvailable, Is.False);

        first.gameObject.SetActive(false);
        CraftingStationRotation.Unregister(first); // Also exercises the EditMode lifecycle without a player loop.
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(late));
        Object.DestroyImmediate(late.gameObject);
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.Null);

        var replacement = Station(scene);
        CraftingStationRotation.Register(replacement);
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(replacement));
    }

    [Test]
    public void TutorialAndInactiveStationsDoNotConsumeBreakRolls()
    {
        Scene scene = NewScene();
        var first = Station(scene);
        var inactive = Station(scene);
        var tutorial = Station(scene, 100f);
        tutorial.ConfigureForTutorial();
        var random = new SequenceRandom();
        CraftingStationRotation.SetRandomSource(scene, random);
        CraftingStationRotation.InitializeScene(scene);

        Assert.That(inactive.CompleteTransaction(), Is.False);
        Assert.That(tutorial.CompleteTransaction(), Is.False);
        Assert.That(random.RollCount, Is.Zero);
        Assert.That(CraftingStationRotation.GetActiveStation(scene), Is.SameAs(first));
    }

    [Test]
    public void SeparateScenesRotateIndependently_WithoutUsingUnityGlobalRandom()
    {
        Scene firstScene = NewScene();
        Scene secondScene = NewScene();
        var first = Station(firstScene, 100f);
        var next = Station(firstScene);
        var otherSceneStation = Station(secondScene);
        CraftingStationRotation.SetRandomSource(firstScene, new SequenceRandom());
        UnityEngine.Random.State previous = UnityEngine.Random.state;
        CraftingStationRotation.InitializeScene(firstScene);
        CraftingStationRotation.InitializeScene(secondScene);
        first.CompleteTransaction();

        Assert.That(CraftingStationRotation.GetActiveStation(firstScene), Is.SameAs(next));
        Assert.That(CraftingStationRotation.GetActiveStation(secondScene), Is.SameAs(otherSceneStation));
        Assert.That(UnityEngine.Random.state, Is.EqualTo(previous));
    }

    [Test]
    public void AuthoredVisualsFollowAvailabilityWithoutDisablingStationRoot()
    {
        Scene scene = NewScene();
        var first = Station(scene, 100f);
        var second = Station(scene);
        var activeVisual = new GameObject("Authored intact model");
        var brokenVisual = new GameObject("Authored broken model");
        SceneManager.MoveGameObjectToScene(activeVisual, scene);
        SceneManager.MoveGameObjectToScene(brokenVisual, scene);
        activeVisual.transform.SetParent(first.transform);
        brokenVisual.transform.SetParent(first.transform);
        Set(first, "_activeVisual", activeVisual);
        Set(first, "_brokenVisual", brokenVisual);
        CraftingStationRotation.SetRandomSource(scene, new SequenceRandom());
        CraftingStationRotation.InitializeScene(scene);
        Assert.That(activeVisual.activeSelf, Is.True);
        Assert.That(brokenVisual.activeSelf, Is.False);

        first.CompleteTransaction();
        Assert.That(activeVisual.activeSelf, Is.False);
        Assert.That(brokenVisual.activeSelf, Is.True);
        Assert.That(first.gameObject.activeSelf, Is.True);
        Assert.That(second.IsAvailable, Is.True);
    }

    [Test]
    public void InactiveDirectOpenIsRejectedWithoutInteractionEvent()
    {
        Scene scene = NewScene();
        Station(scene);
        var inactive = Station(scene);
        CraftingStationRotation.SetRandomSource(scene, new SequenceRandom());
        CraftingStationRotation.InitializeScene(scene);
        Set(inactive, "_craftingUi", inactive.gameObject.AddComponent<CraftingUI>());
        int interactions = 0;
        inactive.OnInteracted += () => interactions++;
        inactive.OpenCrafting();
        Assert.That(inactive.IsOpen, Is.False);
        Assert.That(interactions, Is.Zero);
    }

    [Test]
    public void AlreadyOpenDirectCallIsRejectedBeforeStartingAnotherUiSession()
    {
        Scene scene = NewScene();
        var station = Station(scene);
        CraftingStationRotation.InitializeScene(scene);
        Set(station, "_craftingUi", station.gameObject.AddComponent<CraftingUI>());
        Set(station, "_isOpen", true);
        int interactions = 0;
        station.OnInteracted += () => interactions++;
        station.OpenCrafting();
        Assert.That(interactions, Is.Zero);
        Assert.That(station.IsOpen, Is.True);
    }

    [Test]
    public void UnloadingSceneClearsPoolAndFreshSceneStartsAtAuthoredChance()
    {
        Scene oldScene = NewScene();
        var oldStation = Station(oldScene);
        CraftingStationRotation.InitializeScene(oldScene);
        oldStation.CompleteTransaction();
        Assert.That(oldStation.CurrentBreakChancePercent, Is.EqualTo(5f));
        EditorSceneManager.ClosePreviewScene(oldScene);
        Assert.That(CraftingStationRotation.GetActiveStation(oldScene), Is.Null);

        Scene newScene = NewScene();
        var newStation = Station(newScene);
        CraftingStationRotation.InitializeScene(newScene);
        Assert.That(newStation.IsAvailable, Is.True);
        Assert.That(newStation.CurrentBreakChancePercent, Is.Zero);
    }

    private Scene NewScene()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        _scenes.Add(scene);
        return scene;
    }

    private static CraftingStation Station(Scene scene, float initial = 0f, float increment = 5f)
    {
        var go = new GameObject("Rotation test station");
        SceneManager.MoveGameObjectToScene(go, scene);
        var station = go.AddComponent<CraftingStation>();
        Set(station, "_initialBreakChancePercent", initial);
        Set(station, "_breakChanceIncrementPercent", increment);
        CraftingStationRotation.Register(station);
        return station;
    }

    private static void Set(object owner, string field, object value) =>
        owner.GetType().GetField(field, PrivateInstance).SetValue(owner, value);

    private static void ResetRegistry() => typeof(CraftingStationRotation)
        .GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    private sealed class SequenceRandom : CraftingStationRotation.IRandomSource
    {
        private readonly Queue<int> _indices;
        private readonly Queue<double> _rolls;
        public readonly List<int> IndexBounds = new List<int>();
        public int RollCount { get; private set; }

        public SequenceRandom(int[] indices = null, double[] rolls = null)
        {
            _indices = new Queue<int>(indices ?? Array.Empty<int>());
            _rolls = new Queue<double>(rolls ?? Array.Empty<double>());
        }

        public double NextDouble()
        {
            RollCount++;
            return _rolls.Count > 0 ? _rolls.Dequeue() : 0d;
        }

        public int NextIndex(int exclusiveMax)
        {
            IndexBounds.Add(exclusiveMax);
            return _indices.Count > 0 ? _indices.Dequeue() : 0;
        }
    }
}
