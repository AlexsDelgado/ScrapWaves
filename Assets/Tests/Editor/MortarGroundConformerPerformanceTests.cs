using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Geometry equivalence and conservative fast-path eligibility, without timing assertions.</summary>
public sealed class MortarGroundConformerPerformanceTests
{
    private const string MarkerPath = "Assets/GameFeel/Prefabs/Weapons/Mortar/GF_Mortar_ManualLandingIndicator.prefab";
    private static readonly Vector3 Anchor = new(3000f, 75f, 3000f);
    private const float Offset = .015f;
    private readonly List<Object> _objects = new();
    private readonly List<MortarGroundConformer> _conformers = new();
    private Scene _scene;

    private sealed class Pair
    {
        public MortarLandingIndicatorVfx FastMarker, GenericMarker;
        public MortarGroundConformer Fast, Generic;
    }

    [SetUp]
    public void SetUp() => _scene = SceneManager.GetActiveScene();

    [TearDown]
    public void TearDown()
    {
        foreach (MortarGroundConformer conformer in _conformers) conformer.Dispose();
        _conformers.Clear();
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
    }

    [TestCase(0f)]
    [TestCase(24f)]
    [TestCase(-24f)]
    public void SingleBox_MatchesGenericVerticesAndIndicesWithFewerQueries(float degrees)
    {
        Quaternion rotation = Quaternion.Euler(degrees, 17f, 0f);
        Vector3 normal = rotation * Vector3.up;
        Box("Single ground", -normal * .25f, new Vector3(14f, .5f, 14f), rotation);
        Pair pair = Markers();
        Conform(pair, Anchor, normal);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        Assert.That(pair.Fast.BroadphaseCount, Is.EqualTo(1));
        Assert.That(pair.Fast.TriangleShortcutCount, Is.GreaterThan(0));
        Assert.That(pair.Fast.BoxRaycastCount, Is.EqualTo(pair.Fast.RaycastCount));
        Assert.That(pair.Fast.RaycastCount, Is.LessThan(pair.Generic.RaycastCount * .4),
            "A convex planar face should require vertex probes only.");
    }

    [Test]
    public void MovingBoxAndAnimatedLayers_RefreshCurrentGeometryEveryTime()
    {
        GameObject ground = Box("Moving ground", Vector3.down * .25f, new Vector3(14f, .5f, 14f));
        Pair pair = Markers();
        foreach (float phase in new[] { 0f, .35f, .8f })
        {
            ground.transform.position = Anchor + new Vector3(.15f * phase, -.25f + .4f * phase, 0f);
            ground.transform.rotation = Quaternion.Euler(phase * 12f, 0f, 0f);
            // Hold the selected contact fixed: unchanged aiming must not cache stale terrain.
            Conform(pair, Anchor, Vector3.up, 2.5f + phase * .4f, phase);
            AssertEquivalent(pair);
        }
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(3));
        Assert.That(pair.Fast.BroadphaseCount, Is.EqualTo(3));
    }

    [Test]
    public void FiniteBoxLedge_MatchesGenericCullingInsteadOfBridgingUnsupportedSpace()
    {
        Box("Finite ledge", new Vector3(-1f, -.25f, 0f), new Vector3(3f, .5f, 7f));
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        Assert.That(pair.Fast.TriangleShortcutCount, Is.GreaterThan(0));
        MeshFilter ring = pair.FastMarker.transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>();
        Assert.That(ring.sharedMesh.triangles.Length, Is.InRange(3, 1151), "The ledge must cull part of the annulus.");
        Vector3[] vertices = ring.sharedMesh.vertices;
        foreach (int index in ring.sharedMesh.triangles)
            Assert.That(ring.transform.TransformPoint(vertices[index]).x, Is.LessThanOrEqualTo(Anchor.x + .501f));
    }

    [Test]
    public void AdjacentBoxFaces_KeepGenericEdgeAndInteriorChecksAcrossTheRidge()
    {
        Box("Two upward box faces", Vector3.down * (3f * Mathf.Sqrt(2f)),
            new Vector3(6f, 6f, 12f), Quaternion.Euler(0f, 0f, 45f));
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        Assert.That(pair.Fast.TriangleShortcutCount, Is.InRange(1, 503),
            "Triangles spanning distinct box faces must retain their midpoint/interior checks.");
    }

    [Test]
    public void NewlyCreatedOverlappingTerrain_DisablesFastPathImmediately()
    {
        Box("Base floor", Vector3.down * .25f, new Vector3(14f, .5f, 14f));
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        Box("New nearby terrain", new Vector3(1f, .25f, 0f), new Vector3(2f, .5f, 7f));
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1), "A new collider must invalidate eligibility this refresh.");
    }

    [Test]
    public void MovingTerrainEnteringAndLeavingProjectionVolume_ChangesEligibilityImmediately()
    {
        Box("Base floor", Vector3.down * .25f, new Vector3(14f, .5f, 14f));
        GameObject moving = Box("Moving obstruction", new Vector3(20f, .25f, 0f), Vector3.one);
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        moving.transform.position = Anchor + new Vector3(1f, .25f, 0f);
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
        moving.transform.position = Anchor + new Vector3(20f, .25f, 0f);
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(2));
    }

    [Test]
    public void StackedFloorAndPanel_KeepNearestTangentSurfaceSemantics()
    {
        Box("Sandbox floor", new Vector3(0f, -.07f, 0f), new Vector3(14f, .12f, 14f));
        Box("Sandbox panel", new Vector3(0f, .01f, 0f), new Vector3(10f, .08f, 10f));
        Pair pair = Markers();
        foreach (float contactHeight in new[] { -.01f, .05f })
        {
            Conform(pair, Anchor + Vector3.up * contactHeight, Vector3.up);
            AssertEquivalent(pair);
            MeshFilter ring = pair.FastMarker.transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>();
            Assert.That(ring.sharedMesh.vertices.Max(v => ring.transform.TransformPoint(v).y),
                Is.EqualTo(Anchor.y + contactHeight + Offset).Within(.0005f));
        }
        Assert.That(pair.Fast.BoxFastPathCount, Is.Zero);
        Assert.That(pair.Fast.BoxRaycastCount, Is.Zero);
    }

    [TestCase(1, true)]
    [TestCase(30, true)]
    [TestCase(31, false)]
    [TestCase(35, false)]
    public void CrowdedOwnerGeometry_IsFilteredOnlyWhenCandidateSetIsComplete(int ownerColliderCount, bool eligible)
    {
        Box("Ground", Vector3.down * .25f, new Vector3(14f, .5f, 14f));
        GameObject owner = Keep(new GameObject("Ignored owner"));
        owner.AddComponent<Rigidbody>().isKinematic = true;
        for (int i = 0; i < ownerColliderCount; i++)
        {
            GameObject body = Box("Owned collider " + i, new Vector3(0f, .3f + i * .01f, 0f), new Vector3(.5f, .1f, .5f));
            body.transform.SetParent(owner.transform, true);
        }
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up, ignoredRoot: owner.transform);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(eligible ? 1 : 0),
            "A saturated broadphase cannot prove all non-owner colliders were included.");
    }

    [Test]
    public void TriggerAndDamageableGeometry_DoNotPreventSafeSingleTerrainBox()
    {
        Box("Ground", Vector3.down * .25f, new Vector3(14f, .5f, 14f));
        Box("Ignored trigger", Vector3.up * .4f, new Vector3(8f, .2f, 8f)).GetComponent<Collider>().isTrigger = true;
        GameObject enemy = Box("Damageable", Vector3.up * .6f, new Vector3(8f, .2f, 8f));
        enemy.AddComponent<ReticleTestDamageable>();
        Pair pair = Markers();
        Conform(pair, Anchor, Vector3.up);
        AssertEquivalent(pair);
        Assert.That(pair.Fast.BoxFastPathCount, Is.EqualTo(1));
    }

    private Pair Markers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MarkerPath);
        Assert.That(prefab, Is.Not.Null);
        var pair = new Pair
        {
            FastMarker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>(),
            GenericMarker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>()
        };
        pair.FastMarker.enabled = pair.GenericMarker.enabled = false;
        pair.Fast = new MortarGroundConformer(pair.FastMarker.GetComponentsInChildren<Renderer>(true),
            pair.FastMarker.transform.Find("Authored Explosion Radius"));
        ConstructorInfo constructor = typeof(MortarGroundConformer).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(Renderer[]), typeof(Transform), typeof(bool) }, null);
        Assert.That(constructor, Is.Not.Null);
        pair.Generic = (MortarGroundConformer)constructor.Invoke(new object[]
        {
            pair.GenericMarker.GetComponentsInChildren<Renderer>(true),
            pair.GenericMarker.transform.Find("Authored Explosion Radius"), false
        });
        _conformers.Add(pair.Fast);
        _conformers.Add(pair.Generic);
        return pair;
    }

    private static void Conform(Pair pair, Vector3 contact, Vector3 normal, float radius = 2.5f,
        float phase = .35f, Transform ignoredRoot = null)
    {
        foreach (MortarLandingIndicatorVfx marker in new[] { pair.FastMarker, pair.GenericMarker })
        {
            marker.Configure(contact + normal * Offset, normal, radius, .55f, WeaponUpgradePath.None);
            typeof(MortarLandingIndicatorVfx).GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(marker, new object[] { phase });
        }
        Physics.SyncTransforms();
        pair.Fast.Conform(contact, normal, ignoredRoot, Offset, 3f, .06f);
        pair.Generic.Conform(contact, normal, ignoredRoot, Offset, 3f, .06f);
    }

    private static void AssertEquivalent(Pair pair)
    {
        var expected = pair.GenericMarker.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
        foreach (MeshFilter actual in pair.FastMarker.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshFilter baseline = expected[actual.name];
            Assert.That(actual.sharedMesh.triangles, Is.EqualTo(baseline.sharedMesh.triangles), actual.name + " indices");
            Vector3[] actualVertices = actual.sharedMesh.vertices;
            Vector3[] baselineVertices = baseline.sharedMesh.vertices;
            Assert.That(actualVertices.Length, Is.EqualTo(baselineVertices.Length));
            for (int i = 0; i < actualVertices.Length; i++)
                Assert.That(Vector3.Distance(actual.transform.TransformPoint(actualVertices[i]),
                    baseline.transform.TransformPoint(baselineVertices[i])), Is.LessThan(.0005f), actual.name + " vertex " + i);
        }
    }

    private GameObject Box(string name, Vector3 center, Vector3 size, Quaternion rotation = default)
    {
        GameObject result = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        result.name = name;
        result.transform.position = Anchor + center;
        result.transform.localScale = size;
        result.transform.rotation = rotation == default ? Quaternion.identity : rotation;
        return result;
    }

    private T Keep<T>(T value) where T : Object
    {
        _objects.Add(value);
        if (value is GameObject gameObject) SceneManager.MoveGameObjectToScene(gameObject, _scene);
        return value;
    }
}
