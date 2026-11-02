using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class EnemyEmergenceTests
{
    private readonly List<GameObject> _created = new();
    private readonly EnemyEmergenceTestLifecycle _lifecycle = new();
    private Random.State _randomState;
    private int _pauseLocks;

    [SetUp]
    public void SetUp()
    {
        _randomState = Random.state;
        _pauseLocks = GameplayPause.LockCount;
        GameplayPause.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] != null)
            {
                EnemyRegistry.Unregister(_created[i].transform);
                _lifecycle.Destroy(_created[i]);
            }
        }
        _created.Clear();
        Random.state = _randomState;
        GameplayPause.Reset();
        for (int i = 0; i < _pauseLocks; i++) GameplayPause.Push();
    }

    [Test]
    public void Activation_BlocksBeforePlacementWithoutConsumingDuration()
    {
        Rig rig = CreateRig(2f, 2f);
        _lifecycle.SetActive(rig.Root, true);

        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);
        Assert.That(rig.Collider.enabled, Is.False);
        Assert.That(EnemyEmergence.BlocksGameplay(rig.Body), Is.True,
            "Child hurtboxes and weapon targets must resolve the root gate.");
        rig.Emergence.Simulate(20f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True,
            "Pool prewarming and the placement delay must not use up the spawn interval.");

        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.IsEmerging, Is.True);
        Assert.That(rig.Emergence.NormalizedProgress, Is.Zero);
        rig.Emergence.Simulate(1.9f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);
        rig.Emergence.Simulate(0.2f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
    }

    [Test]
    public void FixedDuration_CompletesOnceAndDoesNotRestartFromDuplicatePlacement()
    {
        Rig rig = CreateRig(2f, 2f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        rig.Emergence.Simulate(0.5f);
        Vector3 partialPose = rig.Pivot.localPosition;

        rig.Emergence.OnPoolSpawn();
        rig.Emergence.BeginEmergence(Vector3.up * 100f, Vector3.up);

        Assert.That(rig.Emergence.SampledDuration, Is.EqualTo(2f));
        Assert.That(rig.Emergence.NormalizedProgress, Is.EqualTo(0.25f).Within(0.0001f));
        AssertVector(rig.Pivot.localPosition, partialPose);
        rig.Emergence.Simulate(1.5f);
        Assert.That(rig.Emergence.IsEmerging, Is.False);
        Assert.That(rig.Emergence.NormalizedProgress, Is.EqualTo(1f));
        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
        rig.Emergence.OnPoolSpawn();
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
    }

    [Test]
    public void DurationRange_SamplesOncePerSpawnAndFreshlyAfterReuse()
    {
        Rig rig = CreateRig(0.6f, 1.4f);
        _lifecycle.SetActive(rig.Root, true);
        float expectedFirst = ExpectedSample(1234, 0.6f, 1.4f);
        Random.InitState(1234);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.SampledDuration, Is.EqualTo(expectedFirst));
        Random.State afterSample = Random.state;
        float expectedNextRandom = Random.value;
        Random.state = afterSample;

        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(Random.value, Is.EqualTo(expectedNextRandom), "Duplicate readiness must not draw another duration.");

        rig.Emergence.OnPoolDespawn();
        _lifecycle.SetActive(rig.Root, false);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.OnPoolSpawn();
        float expectedSecond = ExpectedSample(5678, 0.6f, 1.4f);
        Random.InitState(5678);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.SampledDuration, Is.EqualTo(expectedSecond));
        Assert.That(rig.Emergence.NormalizedProgress, Is.Zero);
    }

    [Test]
    public void SimultaneousSpawns_KeepIndependentIntervals()
    {
        Rig fast = CreateRig(0.5f, 0.5f);
        Rig slow = CreateRig(1.5f, 1.5f);
        _lifecycle.SetActive(fast.Root, true);
        _lifecycle.SetActive(slow.Root, true);
        fast.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        slow.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        fast.Emergence.Simulate(0.5f);
        slow.Emergence.Simulate(0.5f);
        Assert.That(fast.Emergence.BlocksGameplayNow, Is.False);
        Assert.That(slow.Emergence.BlocksGameplayNow, Is.True);
        Assert.That(slow.Emergence.NormalizedProgress, Is.EqualTo(1f / 3f).Within(0.0001f));
        slow.Emergence.Simulate(1f);
        Assert.That(slow.Emergence.BlocksGameplayNow, Is.False);
    }

    [TestCase(0f, 0f)]
    [TestCase(-2f, -1f)]
    public void NonpositiveRange_CompletesImmediatelyAfterPlacement(float min, float max)
    {
        Rig rig = CreateRig(min, max);
        _lifecycle.SetActive(rig.Root, true);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);

        Assert.That(rig.Emergence.SampledDuration, Is.Zero);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
        Assert.That(rig.Collider.enabled, Is.True);
        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
    }

    [Test]
    public void ReversedDurationRange_IsSanitizedAndCanComplete()
    {
        Rig rig = CreateRig(2f, 0.5f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.SampledDuration, Is.InRange(0.5f, 2f));
        rig.Emergence.Simulate(3f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
    }

    [Test]
    public void ZeroElapsedTime_DoesNotAdvanceThePresentation()
    {
        Rig rig = CreateRig(1f, 1f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Vector3 buried = rig.Pivot.localPosition;

        rig.Emergence.Simulate(0f);

        Assert.That(rig.Emergence.NormalizedProgress, Is.Zero);
        AssertVector(rig.Pivot.localPosition, buried);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);
    }

    [Test]
    public void UiPause_DoesNotConsumeDurationEvenWhenSimulationReceivesPositiveTime()
    {
        Rig rig = CreateRig(1f, 1f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        rig.Emergence.Simulate(0.25f);
        Vector3 pose = rig.Pivot.localPosition;
        GameplayPause.Push();
        rig.Emergence.Simulate(20f);
        Assert.That(rig.Emergence.NormalizedProgress, Is.EqualTo(0.25f));
        AssertVector(rig.Pivot.localPosition, pose);
        GameplayPause.Pop();
        rig.Emergence.Simulate(0.75f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
    }

    [Test]
    public void PlacementPreparation_RestoresSeatingColliderWithoutUnlockingGameplayOrShowingModel()
    {
        Rig rig = CreateRig(1f, 1f);
        _lifecycle.SetActive(rig.Root, true);
        Assert.That(rig.Body.forceRenderingOff, Is.True);
        rig.Emergence.PrepareForPlacement();
        Assert.That(rig.Collider.enabled, Is.True);
        Assert.That(rig.Body.forceRenderingOff, Is.True);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Collider.enabled, Is.False);
        Assert.That(rig.Body.forceRenderingOff, Is.False);
    }

    [Test]
    public void PendingCancellation_RestoresPreexistingRendererFlags()
    {
        Rig rig = CreateRig(1f, 1f);
        rig.Body.forceRenderingOff = true;
        rig.Body.enabled = false;
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.OnPoolDespawn();
        _lifecycle.SetActive(rig.Root, false);
        Assert.That(rig.Body.forceRenderingOff, Is.True);
        Assert.That(rig.Body.enabled, Is.False);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(4f)]
    public void SlopedGround_BuriesTheWholeScaledModelAndRestoresExactLocalPose(float scale)
    {
        Rig rig = CreateRig(2f, 2f, transformedPivot: true);
        rig.Root.transform.SetPositionAndRotation(new Vector3(8f, 6f, -3f), Quaternion.Euler(0f, 37f, 0f));
        rig.Root.transform.localScale = new Vector3(scale, scale * 1.5f, scale * 0.8f);
        Quaternion restRotation = rig.Pivot.localRotation;
        Vector3 restScale = rig.Pivot.localScale;
        Vector3 rootPosition = rig.Root.transform.position;
        Vector3 surface = new Vector3(8f, 5f, -3f);
        Vector3 normal = new Vector3(0.35f, 1f, -0.25f).normalized;
        rig.Emergence.Configure(rig.Pivot, 2f, 2f, 0.2f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(surface, normal);
        Bounds buriedBounds = rig.Body.bounds;
        Vector3 buriedWorldPosition = rig.Pivot.position;

        foreach (Vector3 corner in BoundsCorners(buriedBounds))
            Assert.That(Vector3.Dot(corner - surface, normal), Is.LessThanOrEqualTo(0.001f),
                "The entire visible body must start beneath the selected sloped ground plane.");
        AssertVector(rig.Root.transform.position, rootPosition);
        rig.Emergence.Simulate(1f);
        Assert.That(rig.Pivot.position.y, Is.GreaterThan(buriedWorldPosition.y));
        Assert.That(rig.Pivot.position.x, Is.EqualTo(buriedWorldPosition.x).Within(0.0001f));
        Assert.That(rig.Pivot.position.z, Is.EqualTo(buriedWorldPosition.z).Within(0.0001f));
        rig.Emergence.Simulate(1f);

        AssertVector(rig.Root.transform.position, rootPosition);
        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
        Assert.That(Quaternion.Angle(rig.Pivot.localRotation, restRotation), Is.LessThan(0.001f));
        AssertVector(rig.Pivot.localScale, restScale);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CollisionAndPhysics_RestoreAuthoredFlagsWithoutChangingKinematicOwnership(bool kinematic)
    {
        Rig rig = CreateRig(1f, 1f);
        BoxCollider disabledCollider = rig.Root.AddComponent<BoxCollider>();
        disabledCollider.enabled = false;
        Rigidbody body = rig.Root.AddComponent<Rigidbody>();
        body.isKinematic = kinematic;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        RigidbodyConstraints authoredConstraints = body.constraints;
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);

        Assert.That(rig.Collider.enabled, Is.False);
        Assert.That(disabledCollider.enabled, Is.False);
        Assert.That(body.detectCollisions, Is.False);
        Assert.That(body.isKinematic, Is.EqualTo(kinematic));
        rig.Emergence.Simulate(1f);

        Assert.That(rig.Collider.enabled, Is.True);
        Assert.That(disabledCollider.enabled, Is.False);
        Assert.That(body.detectCollisions, Is.True);
        Assert.That(body.constraints, Is.EqualTo(authoredConstraints));
        Assert.That(body.isKinematic, Is.EqualTo(kinematic));
        Assert.That(body.useGravity, Is.False);
    }

    [Test]
    public void DespawnMidRise_RestoresPoseAndDisabledFlagsBeforeReuse()
    {
        Rig rig = CreateRig(2f, 2f);
        BoxCollider disabledCollider = rig.Root.AddComponent<BoxCollider>();
        disabledCollider.enabled = false;
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        rig.Emergence.Simulate(0.75f);
        rig.Emergence.OnPoolDespawn();
        _lifecycle.SetActive(rig.Root, false);

        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
        Assert.That(rig.Collider.enabled, Is.True);
        Assert.That(disabledCollider.enabled, Is.False);
        rig.Root.transform.position = new Vector3(15f, 7f, 12f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.OnPoolSpawn();
        rig.Emergence.BeginEmergence(new Vector3(15f, 6f, 12f), Vector3.up);
        Assert.That(rig.Emergence.NormalizedProgress, Is.Zero);
        Assert.That(rig.Emergence.SampledDuration, Is.EqualTo(2f));
        rig.Emergence.Simulate(2f);
        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
        Assert.That(rig.Collider.enabled, Is.True);
        Assert.That(disabledCollider.enabled, Is.False);
    }

    [Test]
    public void CancelledSpawn_CannotCompleteOrRestartUntilANewActivation()
    {
        Rig rig = CreateRig(1f, 1f);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        rig.Emergence.Simulate(0.2f);
        rig.Emergence.CancelEmergence();
        rig.Emergence.Simulate(100f);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        Assert.That(rig.Emergence.IsEmerging, Is.False);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.True);

        _lifecycle.SetActive(rig.Root, false);
        _lifecycle.SetActive(rig.Root, true);
        rig.Emergence.BeginEmergence(Vector3.zero, Vector3.up);
        rig.Emergence.Simulate(1f);
        Assert.That(rig.Emergence.BlocksGameplayNow, Is.False);
        AssertVector(rig.Pivot.localPosition, rig.RestLocalPosition);
    }

    private Rig CreateRig(float min, float max, bool transformedPivot = false)
    {
        GameObject root = new("Emergence test enemy");
        _created.Add(root);
        root.SetActive(false);
        BoxCollider collider = root.AddComponent<BoxCollider>();
        Transform pivot = new GameObject("EmergenceVisual").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0.2f, 0.75f, -0.1f);
        if (transformedPivot)
        {
            pivot.localRotation = Quaternion.Euler(13f, 21f, 8f);
            pivot.localScale = new Vector3(0.8f, 1.2f, 1.1f);
        }
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(pivot, false);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.localScale = new Vector3(1.2f, 2f, 0.8f);
        EnemyEmergence emergence = root.AddComponent<EnemyEmergence>();
        emergence.Configure(pivot, min, max);
        return new Rig(root, pivot, body.GetComponent<Renderer>(), collider, emergence);
    }

    private static float ExpectedSample(int seed, float min, float max)
    {
        Random.InitState(seed);
        return Random.Range(min, max);
    }

    private static IEnumerable<Vector3> BoundsCorners(Bounds bounds)
    {
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
    }

    private static void AssertVector(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));

    private sealed class Rig
    {
        public readonly GameObject Root;
        public readonly Transform Pivot;
        public readonly Renderer Body;
        public readonly BoxCollider Collider;
        public readonly EnemyEmergence Emergence;
        public readonly Vector3 RestLocalPosition;

        public Rig(GameObject root, Transform pivot, Renderer body, BoxCollider collider, EnemyEmergence emergence)
        {
            Root = root;
            Pivot = pivot;
            Body = body;
            Collider = collider;
            Emergence = emergence;
            RestLocalPosition = pivot.localPosition;
        }
    }
}

/// <summary>
/// Non-ExecuteAlways scripts do not reliably receive Unity lifecycle messages in EditMode.
/// Use the actual callbacks, but never repeat callbacks for scripts Unity initialized itself.
/// Runtime capture separately verifies automatic activation and pool lifecycle ordering.
/// </summary>
internal sealed class EnemyEmergenceTestLifecycle
{
    private readonly HashSet<MonoBehaviour> _awoken = new();
    private readonly HashSet<MonoBehaviour> _enabled = new();

    public void SetActive(GameObject root, bool active)
    {
        MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
        root.SetActive(active);
        if (active)
        {
            foreach (MonoBehaviour component in components)
                if (component != null && component.gameObject.activeInHierarchy && !component.didAwake && _awoken.Add(component))
                    Invoke(component, "Awake");
        }
        foreach (MonoBehaviour component in components)
        {
            if (component == null || component.didAwake) continue;
            if (active && component.isActiveAndEnabled && _enabled.Add(component)) Invoke(component, "OnEnable");
            else if (!active && _enabled.Remove(component)) Invoke(component, "OnDisable");
        }
    }

    public void Destroy(GameObject root)
    {
        MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
        SetActive(root, false);
        foreach (MonoBehaviour component in components)
            if (component != null && !component.didAwake && _awoken.Remove(component)) Invoke(component, "OnDestroy");
        Object.DestroyImmediate(root);
    }

    private static void Invoke(MonoBehaviour component, string message)
    {
        MethodInfo method = component.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(component, null);
    }
}
