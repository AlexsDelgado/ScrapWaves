using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ExitDoorTraversalTests
{
    private readonly List<GameObject> _objects = new();
    private readonly Dictionary<FieldInfo, object> _previousStatics = new();
    private ExitDoor _door;
    private ExitDoorTraversal _traversal;
    private LevelExitObjective _objective;
    private BoxCollider _panel;
    private PlayerMovement _player;
    private PlayerHealth _health;
    private GameManager _gameManager;
    private float _previousTimeScale;

    [SetUp]
    public void SetUp()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        PreserveStatic(typeof(GameManager), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(PlayerMovement), "s_Instance", null);
        PreserveStatic(typeof(LevelExitObjective), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(SaveManager), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(AudioManager), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(RunEndScreenUI), "<Instance>k__BackingField", null);
        PreserveStatic(typeof(DemoLeaderboardSession), "_activeRunId", null);
        PreserveStatic(typeof(DemoLeaderboardSession), "_ended", false);
        PreserveStatic(typeof(GameplayPause), "_locks", 0);

        // Keep this manager inactive: no Awake/OnEnable should find scene objects,
        // reset run statistics, or consume a pending leaderboard launch.
        GameObject managerRoot = CreateObject("Traversal game state", false);
        _gameManager = managerRoot.AddComponent<GameManager>();
        SetStatic(typeof(GameManager), "<Instance>k__BackingField", _gameManager);

        _player = CreatePlayer("Traversal player");
        _health = _player.GetComponent<PlayerHealth>();
        SetStatic(typeof(PlayerMovement), "s_Instance", _player);

        GameObject root = CreateObject("Traversal door", false);
        _objective = root.AddComponent<LevelExitObjective>();
        _door = root.AddComponent<ExitDoor>();
        Set(_door, "_exitObjective", _objective);
        Set(_door, "_chargeDurationSeconds", 20f);
        Set(_door, "_craftingUi", null);
        Set(_door, "_levelUpChoiceUi", null);
        _door.ResetCharge();

        GameObject panelRoot = CreateObject("Moving panel");
        panelRoot.transform.SetParent(root.transform, false);
        _panel = panelRoot.AddComponent<BoxCollider>();
        _panel.center = new Vector3(0f, 3f, 0f);
        _panel.size = new Vector3(4f, 6f, .5f);
        _traversal = root.AddComponent<ExitDoorTraversal>();
        root.SetActive(true);
        _traversal.Configure(_door, _panel,
            new Bounds(new Vector3(0f, 3f, 0f), new Vector3(4f, 6f, .5f)));
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        foreach (KeyValuePair<FieldInfo, object> entry in _previousStatics)
            entry.Key.SetValue(null, entry.Value);
        _previousStatics.Clear();
        Time.timeScale = _previousTimeScale;
    }

    [Test]
    public void BatteriesAndActivationAreRequired_EvenWhenThePhysicalGapIsClear()
    {
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False);
        Assert.That(_door.TryStartCharging(), Is.False);

        CollectBatteries();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False);
        Assert.That(_gameManager.IsPlaying, Is.True);

        Assert.That(_door.TryStartCharging(), Is.True);
        Assert.That(Sample(0f, 3.5f, -3f), Is.False,
            "Activation must not count an earlier crossing or a player already outside.");
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -2f), Is.True,
            "There is no minimum charge percentage once activation and physical clearance permit escape.");
        Assert.That(_door.ChargeNormalized, Is.Zero);
    }

    [Test]
    public void ChargingRequiresTheWholeCapsuleBeyondTheRearFace_AndPreservesEscapeProgress()
    {
        Activate();
        TickCharge(5f);
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, 0f), Is.False);
        Assert.That(Sample(0f, 3.5f, -.4f), Is.False,
            "The capsule still overlaps the back face even though its center has crossed.");
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -.7f), Is.True);
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Used));
        Assert.That(_door.ChargeNormalized, Is.EqualTo(.25f).Within(.0001f));
        Assert.That(_gameManager.State, Is.EqualTo(GameManager.GameState.Victory));
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False, "The same traversal cannot finish twice.");
    }

    [Test]
    public void PanelStillBlocksLowerPlayers_WhileAHighJumpCanEscapeBeforeReady()
    {
        Activate();
        LowerPanel(); // Panel top is y=2; the lower capsule intersects it.
        Assert.That(Sample(0f, 1.5f, 2f), Is.False);
        Assert.That(Sample(0f, 1.5f, -2f), Is.False);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False,
            "Rising after crossing through the solid panel does not repair that crossing.");
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -2f), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RoundedLeadingOrTrailingCapCanClearThePanelDuringAJump(bool falling)
    {
        Activate();
        LowerPanel(); // Panel top is y=2, capsule radius=.25 and straight half-segment=.75.
        Vector3[] path = falling
            ? new[] { new Vector3(0f, 3.4f, .7f), new Vector3(0f, 3.4f, 0f),
                new Vector3(0f, 2.91f, -.45f), new Vector3(0f, 2.7f, -.7f) }
            : new[] { new Vector3(0f, 2.7f, .7f), new Vector3(0f, 2.91f, .45f),
                new Vector3(0f, 3.4f, 0f), new Vector3(0f, 3.4f, -.7f) };

        // At |z|=.45 the whole-body bottom is 1.91, but the rounded section
        // touching the slab bottoms at 2.01. The swept path stays above 2.008.
        for (int i = 0; i < path.Length - 1; i++)
            Assert.That(Sample(path[i].x, path[i].y, path[i].z), Is.False);
        ExpectVictory();
        Vector3 last = path[path.Length - 1];
        Assert.That(Sample(last.x, last.y, last.z), Is.True,
            "Only the capsule section actually touching the doorway must clear its panel.");
    }

    [Test]
    public void RoundedCapCollisionBetweenSafeContactEndpointsInvalidatesTheCrossing()
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 2.55f, .7f), Is.False);
        Assert.That(Sample(0f, 3f, .25f), Is.False);
        // Entry contact at z=.5 and the final z=.25 both just clear y=2;
        // between them the rounded section dips below y=1.897 and hits the panel.
        Assert.That(Sample(0f, 3.5f, -.7f), Is.False,
            "Checking just the contact interval endpoints misses the intervening rounded-cap collision.");
        Assert.That(_gameManager.IsPlaying, Is.True);
    }

    [TestCase(1.9f, 3.5f)]
    [TestCase(0f, 5.5f)]
    [TestCase(0f, .5f)]
    public void CapsuleClippingApertureSidesHeaderOrFloorCannotEscape(float x, float y)
    {
        Activate();
        _panel.transform.localPosition = Vector3.down * 8f;
        Assert.That(Sample(x, y, 2f), Is.False);
        Assert.That(Sample(x, y, -2f), Is.False);
        Assert.That(_gameManager.IsPlaying, Is.True);
    }

    [Test]
    public void ReverseCrossingAndStartingOutsideNeverWin()
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, -2f), Is.False);
        Assert.That(Sample(0f, 3.5f, -3f), Is.False);
        Assert.That(Sample(0f, 3.5f, 0f), Is.False);
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(_gameManager.IsPlaying, Is.True);
    }

    [TestCase(2f, 2f, 2f)]
    [TestCase(30f, 30f, 15f)]
    public void FastCrossingBetweenSamplesIsDetectedInRotatedScaledDoorSpace(float scaleX, float scaleY, float scaleZ)
    {
        _door.transform.SetPositionAndRotation(new Vector3(20f, -3f, 14f), Quaternion.Euler(0f, 90f, 0f));
        _door.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 12f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -12f), Is.True);
    }

    [Test]
    public void FastDiagonalArrivalBehindTheDoorCannotCutThroughTheSideFrame()
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(4f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False,
            "A centered destination cannot make the intervening path through the side frame valid.");
        Assert.That(_gameManager.IsPlaying, Is.True);
    }

    [TestCase("ui")]
    [TestCase("time")]
    [TestCase("dead")]
    [TestCase("gameover")]
    public void PausedDeadOrEndedRunsCannotCompleteTraversal(string condition)
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        if (condition == "ui") GameplayPause.Push();
        if (condition == "time") Time.timeScale = 0f;
        if (condition == "dead") Set(_health, "_currentHealth", 0);
        if (condition == "gameover") Set(_gameManager, "_state", GameManager.GameState.GameOver);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False);
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Charging));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MovingAcrossTheDoorWhilePausedCannotWinOnResume(bool pauseTime)
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        if (pauseTime) Time.timeScale = 0f;
        else GameplayPause.Push();
        Move(_player, new Vector3(0f, 3.5f, -2f));
        ObservePausedPose();
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Charging));
        if (pauseTime) Time.timeScale = 1f;
        else GameplayPause.Pop();
        Assert.That(Sample(0f, 3.5f, -2f), Is.False,
            "A pose change during pause must invalidate the old arena-side sample.");
        Assert.That(_gameManager.IsPlaying, Is.True);
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -2f), Is.True,
            "Returning to the arena after resume permits a fresh legitimate crossing.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StationaryPausePreservesALegitimateUnfinishedCrossing(bool pauseTime)
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, 0f), Is.False);
        if (pauseTime) Time.timeScale = 0f;
        else GameplayPause.Push();
        ObservePausedPose();
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Charging));
        if (pauseTime) Time.timeScale = 1f;
        else GameplayPause.Pop();
        Assert.That(Sample(0f, 3.5f, 0f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -.7f), Is.True);
    }

    [Test]
    public void ResetAndReactivationDiscardAnUnfinishedCrossing()
    {
        Activate();
        LowerPanel();
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        Assert.That(Sample(0f, 3.5f, 0f), Is.False);
        _door.ResetCharge();
        Assert.That(_door.TryStartCharging(), Is.True);
        Assert.That(Sample(0f, 3.5f, -2f), Is.False);
        Assert.That(Sample(0f, 3.5f, 2f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 3.5f, -2f), Is.True);
    }

    [Test]
    public void OnlyTheCanonicalPlayerCanCompleteTraversal()
    {
        Activate();
        LowerPanel();
        PlayerMovement other = CreatePlayer("Other actor");
        SetStatic(typeof(PlayerMovement), "s_Instance", _player);
        Move(other, new Vector3(0f, 3.5f, 2f));
        Assert.That(_traversal.SamplePlayer(other), Is.False);
        Move(other, new Vector3(0f, 3.5f, -2f));
        Assert.That(_traversal.SamplePlayer(other), Is.False);
        Assert.That(_gameManager.IsPlaying, Is.True);
    }

    [Test]
    public void ReachingReadyDoesNotWinUntilThePlayerCrosses()
    {
        Activate();
        TickCharge(20f);
        _panel.transform.localPosition = Vector3.down * 8f;
        Assert.That(_door.State, Is.EqualTo(ExitDoorState.Ready));
        Assert.That(_gameManager.IsPlaying, Is.True);
        Assert.That(Sample(0f, 1.5f, 2f), Is.False);
        ExpectVictory();
        Assert.That(Sample(0f, 1.5f, -2f), Is.True);
        Assert.That(_door.ChargeNormalized, Is.EqualTo(1f));
    }

    private GameObject CreateObject(string name, bool active = true)
    {
        var value = new GameObject(name);
        value.SetActive(active);
        _objects.Add(value);
        return value;
    }

    private PlayerMovement CreatePlayer(string name)
    {
        GameObject root = CreateObject(name, false);
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.radius = .25f;
        capsule.height = 2f;
        var body = root.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        var player = root.AddComponent<PlayerMovement>();
        Set(player, "_rb", body);
        Set(player, "_ownCollider", capsule);
        var health = root.AddComponent<PlayerHealth>();
        Set(health, "_currentHealth", 100);
        root.SetActive(true);
        return player;
    }

    private void CollectBatteries()
    {
        while (!_objective.AllKeysCollected) _objective.RegisterKey();
    }

    private void Activate()
    {
        CollectBatteries();
        Assert.That(_door.TryStartCharging(), Is.True);
    }

    private void LowerPanel() => _panel.transform.localPosition = Vector3.down * 4f;

    private bool Sample(float x, float y, float z)
    {
        Move(_player, new Vector3(x, y, z));
        return _traversal.SamplePlayer(_player);
    }

    private void Move(PlayerMovement player, Vector3 localPosition)
    {
        Vector3 worldPosition = _door.transform.TransformPoint(localPosition);
        player.transform.position = worldPosition;
        player.GetComponent<Rigidbody>().position = worldPosition;
        Physics.SyncTransforms();
    }

    private void TickCharge(float seconds) => typeof(ExitDoor)
        .GetMethod("TickCharge", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(_door, new object[] { seconds });

    // FixedUpdate stops at timeScale=0. Exercise the render-frame pause observer,
    // rather than supplying a physics sample that the paused engine would never send.
    private void ObservePausedPose() => typeof(ExitDoorTraversal)
        .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(_traversal, null);

    private static void ExpectVictory() => LogAssert.Expect(LogType.Error,
        "[GameManager] An authored RunEndScreenUI is required in the gameplay HUD.");

    private void PreserveStatic(Type type, string name, object replacement)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(field, Is.Not.Null, type.Name + "." + name);
        _previousStatics.Add(field, field.GetValue(null));
        field.SetValue(null, replacement);
    }

    private static void SetStatic(Type type, string name, object value) => type
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
}
