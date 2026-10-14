using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Production marker geometry and native renders in the runner's existing scene.</summary>
public sealed class MortarGroundMarkerTests
{
    private const string PrefabPath = "Assets/GameFeel/Prefabs/Weapons/Mortar/GF_Mortar_ManualLandingIndicator.prefab";
    private const string CaptureEnvironment = "SCRAPWAVES_MORTAR_GROUND_CAPTURE_DIRECTORY";
    private const float Radius = 1.8f;
    private const float CaptureRadius = 2.5f;
    private static readonly Vector3 Anchor = new(1000f, 100f, 1000f);
    private readonly List<Object> _objects = new();
    private readonly List<Collider> _ground = new();
    private Scene _scene;
    private Material _groundMaterial;

    [SetUp]
    public void SetUp() => _scene = SceneManager.GetActiveScene();

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        _ground.Clear();
        _groundMaterial = null;
    }

    [TestCase(0f)]
    [TestCase(24f)]
    [TestCase(-24f)]
    public void AuthoredLayers_FollowFlatAndSlopedGroundAtSmallOffset(float degrees)
    {
        Vector3 normal = Plane(degrees);
        MortarLandingIndicatorVfx marker = Marker(Anchor, normal);
        Assert.That(marker.IsGroundConforming, Is.True);
        Assert.That(marker.ContactPoint, Is.EqualTo(Anchor), "The marker must retain the actual selected contact.");
        Assert.That(marker.SurfaceOffset, Is.InRange(.001f, .02f));
        AssertSupportedTriangles(marker, normal, marker.SurfaceOffset + .006f);
        AssertTangentRadius(marker, Anchor, normal);
    }

    [Test]
    public void UnevenMesh_AllVisibleLayersFollowTheLocalSurface()
    {
        Uneven();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        AssertSupportedTriangles(marker, Vector3.up, .09f);
        Vector3[] vertices = RenderedVertices(marker);
        Assert.That(vertices.Max(p => p.y) - vertices.Min(p => p.y), Is.GreaterThan(.16f),
            "A tilted plane is insufficient on the authored uneven ground fixture.");
        Assert.That(marker.ContactPoint, Is.EqualTo(Anchor));
        AssertTangentRadius(marker, Anchor, Vector3.up);
    }

    [TestCase(1.8f)]
    [TestCase(2.5f)]
    [TestCase(2.9f)]
    public void UnevenMesh_DenseTriangleInteriorSamplesRemainAboveGround(float blastRadius)
    {
        Uneven();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up, radius: blastRadius);
        AssertTangentRadius(marker, Anchor, Vector3.up, blastRadius);
        const int divisions = 6;
        int sampleCount = 0;
        float minimumClearance = float.PositiveInfinity;
        Vector3 lowestSample = default;
        foreach (float phase in new[] { 0f, .35f, .8f })
        {
            ApplyPhase(marker, phase);
            MeshFilter ring = marker.transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>();
            Mesh authoredRing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                .transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] sourceFootprint = authoredRing.vertices.Select(ring.transform.TransformPoint).ToArray();
            Vector3[] renderedFootprint = ring.sharedMesh.vertices.Select(ring.transform.TransformPoint).ToArray();
            Assert.That(TangentArea(renderedFootprint, ring.sharedMesh.triangles, Vector3.up),
                Is.EqualTo(TangentArea(sourceFootprint, authoredRing.triangles, Vector3.up)).Within(.005f),
                "The ring must cover its complete authored footprint; hiding buried triangles cannot satisfy clearance.");
            foreach (Vector3[] triangle in Triangles(marker))
            for (int b = 0; b <= divisions; b++)
            for (int c = 0; c <= divisions - b; c++)
            {
                // A dense barycentric grid checks interiors independently of the
                // production conformer's vertex, midpoint and centroid probes.
                Vector3 sample = triangle[0] + (triangle[1] - triangle[0]) * (b / (float)divisions)
                    + (triangle[2] - triangle[0]) * (c / (float)divisions);
                Assert.That(SurfaceBelow(sample, Vector3.up, out RaycastHit support), Is.True);
                float clearance = Vector3.Dot(sample - support.point, support.normal);
                if (clearance < minimumClearance)
                {
                    minimumClearance = clearance;
                    lowestSample = sample;
                }
                sampleCount++;
            }
        }
        Assert.That(sampleCount, Is.GreaterThan(1000));
        Assert.That(minimumClearance, Is.GreaterThanOrEqualTo(.001f),
            "Every rendered triangle interior must clear uneven ground by at least 1mm; lowest sample is "
            + (lowestSample - Anchor) + " with clearance " + minimumClearance);
    }

    [Test]
    public void Step_CullsBridgesAndKeepsSupportedPortionsAtBothHeights()
    {
        Step();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        AssertSupportedTriangles(marker, Vector3.up, .025f);
        Vector3[] vertices = RenderedVertices(marker);
        Assert.That(vertices.Any(p => p.y < Anchor.y + .05f), Is.True);
        Assert.That(vertices.Any(p => p.y > Anchor.y + .75f), Is.True);
        foreach (Vector3[] triangle in Triangles(marker))
            Assert.That(triangle.Max(p => p.y) - triangle.Min(p => p.y), Is.LessThan(.05f),
                "A triangle must not connect the upper and lower ledge with a hovering diagonal.");
        Assert.That(marker.ContactPoint, Is.EqualTo(Anchor));
    }

    [Test]
    public void FiniteRampEdge_FollowsTheRampAndUnderlyingFloorWithoutBridges()
    {
        Vector3 normal = RampEdge();
        MortarLandingIndicatorVfx marker = Marker(Anchor, normal);
        AssertSupportedTriangles(marker, normal, .025f);
        var supports = new HashSet<string>();
        foreach (Vector3 vertex in RenderedVertices(marker))
        {
            Assert.That(SurfaceBelow(vertex, normal, out RaycastHit hit), Is.True);
            supports.Add(hit.collider.name);
        }
        Assert.That(supports, Does.Contain("Finite ramp"));
        Assert.That(supports, Does.Contain("Ramp base floor"));
        foreach (Vector3[] triangle in Triangles(marker))
        {
            var triangleSupports = new HashSet<Collider>();
            foreach (Vector3 vertex in triangle)
            {
                Assert.That(SurfaceBelow(vertex, normal, out RaycastHit hit), Is.True);
                triangleSupports.Add(hit.collider);
            }
            Assert.That(triangleSupports.Count, Is.EqualTo(1),
                "A triangle crossing the finite ramp edge must not bridge to its lower base floor.");
        }
        Assert.That(marker.ContactPoint, Is.EqualTo(Anchor));
        AssertTangentRadius(marker, Anchor, normal);
    }

    [Test]
    public void UnsupportedGap_RemovesTrianglesRatherThanBridgingTheVoid()
    {
        GroundCube("Left bank", new Vector3(-3f, -.25f, 0f), new Vector3(5f, .5f, 8f));
        GroundCube("Right bank", new Vector3(3f, -.25f, 0f), new Vector3(5f, .5f, 8f));
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        AssertSupportedTriangles(marker, Vector3.up, .025f);
        foreach (Vector3[] triangle in Triangles(marker))
        {
            bool left = triangle.All(p => p.x <= Anchor.x - .499f);
            bool right = triangle.All(p => p.x >= Anchor.x + .499f);
            Assert.That(left || right, Is.True, "No rendered triangle may span the unsupported central gap.");
        }
    }

    [Test]
    public void Projection_IgnoresOwnerDamageableAndTriggerGeometry()
    {
        Plane(0f);
        GameObject owner = Keep(new GameObject("Marker ignored owner"));
        GameObject ownedCollider = Cube("Owner collider", new Vector3(0f, .4f, 0f), new Vector3(6f, .2f, 6f));
        ownedCollider.transform.SetParent(owner.transform, true);
        GameObject enemy = Cube("Damageable obstruction", new Vector3(0f, .5f, 0f), new Vector3(6f, .2f, 6f));
        enemy.AddComponent<ReticleTestDamageable>();
        GameObject trigger = Cube("Trigger obstruction", new Vector3(0f, .6f, 0f), new Vector3(6f, .2f, 6f));
        trigger.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        // Put the reference plane closer to every excluded surface than to ground:
        // selecting the closest hit alone would fail this filter-specific fixture.
        MortarLandingIndicatorVfx marker = Marker(Anchor + Vector3.up * .65f, Vector3.up, owner.transform);
        AssertSupportedTriangles(marker, Vector3.up, .025f);
        Assert.That(RenderedVertices(marker).Max(p => p.y), Is.LessThan(Anchor.y + .025f),
            "Projection must remain on the ground below owner, target and trigger colliders.");
    }

    [Test]
    public void ReconfigureAndPulse_AlwaysProjectTheOriginalAuthoredGeometry()
    {
        Uneven();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        ApplyPhase(marker, .35f);
        var expected = GeometrySnapshot(marker);
        for (int i = 0; i < 20; i++)
        {
            marker.ConfigureGroundContact(Anchor + Vector3.right * .4f, Vector3.up,
                1.1f, .3f, WeaponUpgradePath.PathA);
            ApplyPhase(marker, i / 20f);
            marker.ConfigureGroundContact(Anchor, Vector3.up, Radius, .55f, WeaponUpgradePath.None);
            ApplyPhase(marker, .35f);
        }
        marker.gameObject.SetActive(false);
        marker.gameObject.SetActive(true);
        marker.ConfigureGroundContact(Anchor, Vector3.up, Radius, .55f, WeaponUpgradePath.None);
        ApplyPhase(marker, .35f);
        var actual = GeometrySnapshot(marker);
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
        foreach (string name in expected.Keys)
        {
            Assert.That(actual[name].Length, Is.EqualTo(expected[name].Length), name);
            for (int i = 0; i < actual[name].Length; i++)
                Assert.That(Vector3.Distance(actual[name][i], expected[name][i]), Is.LessThan(.001f), name);
        }
        AssertSupportedTriangles(marker, Vector3.up, .09f);
        AssertTangentRadius(marker, Anchor, Vector3.up);
    }

    [Test]
    public void TransformedParentAndAuthoredLayerOffsets_PreserveTangentCoordinates()
    {
        Vector3 normal = Plane(24f);
        GameObject parent = Keep(new GameObject("Marker parent with transform"));
        parent.transform.SetPositionAndRotation(Anchor + new Vector3(.4f, .3f, -.2f), Quaternion.Euler(0f, 32f, 0f));
        parent.transform.localScale = Vector3.one * 1.3f;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        MortarLandingIndicatorVfx marker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>();
        marker.transform.SetParent(parent.transform, false);
        marker.Configure(Anchor, normal, Radius, .55f, WeaponUpgradePath.None);
        ApplyPhase(marker, .35f);
        var planar = GeometrySnapshot(marker);
        var authoredIndices = marker.GetComponentsInChildren<MeshFilter>(true)
            .ToDictionary(filter => filter.name, filter => filter.sharedMesh.triangles);
        marker.ConfigureGroundContact(Anchor, normal, Radius, .55f, WeaponUpgradePath.None);
        ApplyPhase(marker, .35f);
        var projected = GeometrySnapshot(marker);
        foreach (string name in planar.Keys)
        {
            Assert.That(projected[name].Length, Is.GreaterThanOrEqualTo(planar[name].Length));
            for (int i = 0; i < planar[name].Length; i++)
                Assert.That(Vector3.ProjectOnPlane(projected[name][i] - planar[name][i], normal).magnitude,
                    Is.LessThan(.001f), name + " must move only along the contact normal.");
            for (int i = planar[name].Length; i < projected[name].Length; i++)
                Assert.That(InsideAuthoredTangentTriangles(projected[name][i], planar[name], authoredIndices[name], normal),
                    Is.True, name + " subdivision must remain within the original authored triangle footprint.");
        }
        MeshFilter ring = marker.transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>();
        Assert.That(TangentArea(projected[ring.name], ring.sharedMesh.triangles, normal),
            Is.EqualTo(TangentArea(planar[ring.name], authoredIndices[ring.name], normal)).Within(.005f),
            "Subdivision must retain the complete annulus footprint on continuous ground.");
        AssertSupportedTriangles(marker, normal, marker.SurfaceOffset + .006f);
        Assert.That(marker.ContactPoint, Is.EqualTo(Anchor));
    }

    [Test]
    public void InspectorValidation_PreservesAuthoredScalesAcrossRepeatedGroundConfiguration()
    {
        Plane(0f);
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        ApplyPhase(marker, .35f);
        Transform[] layers = marker.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.transform).ToArray();
        Vector3[] scales = layers.Select(layer => layer.localScale).ToArray();
        for (int pass = 0; pass < 4; pass++)
        {
            typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(marker, null);
            marker.ConfigureGroundContact(Anchor, Vector3.up, Radius, .55f, WeaponUpgradePath.None);
            ApplyPhase(marker, .35f);
            AssertTangentRadius(marker, Anchor, Vector3.up);
            for (int i = 0; i < layers.Length; i++)
                Assert.That(Vector3.Distance(layers[i].localScale, scales[i]), Is.LessThan(.0001f),
                    layers[i].name + " must retain its authored base scale after inspector validation.");
        }
    }

    [Test]
    public void HotReloadMigration_DefaultNewCacheFieldsPreserveExistingAuthoredBaselines()
    {
        Plane(0f);
        // Keep the entire erroneous 12.5m ring on real supporting geometry so
        // its scale is visible rather than hidden by the ground-edge culler.
        _ground[0].transform.localScale = new Vector3(40f, .5f, 40f);
        Physics.SyncTransforms();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up, radius: CaptureRadius);
        ApplyPhase(marker, .35f);
        Transform[] layers = marker.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.transform).ToArray();
        Vector3[] expectedScales = layers.Select(layer => layer.localScale).ToArray();
        // Simulate upgrading an already configured live component: its existing
        // authored base scales/radius survive reload, while newly added cache
        // bookkeeping fields begin at their defaults.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MortarLandingIndicatorVfx).GetField("_baseScalesCached", flags).SetValue(marker, false);
        foreach (string field in new[] { "_cachedBlastLayer", "_cachedTimeLayer", "_cachedCoreLayer" })
            typeof(MortarLandingIndicatorVfx).GetField(field, flags).SetValue(marker, null);
        typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", flags).Invoke(marker, null);
        marker.ConfigureGroundContact(Anchor, Vector3.up, CaptureRadius, .55f, WeaponUpgradePath.None);
        ApplyPhase(marker, .35f);
        string output = Environment.GetEnvironmentVariable(CaptureEnvironment);
        if (!string.IsNullOrWhiteSpace(output) && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            var light = Keep(new GameObject("Hot reload capture light", typeof(Light))).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var camera = Keep(new GameObject("Native hot reload marker camera", typeof(Camera))).GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = _scene;
            camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .07f, .09f);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 70f;
            camera.fieldOfView = 50f;
            camera.transform.position = Anchor + new Vector3(12f, 24f, -22f);
            camera.transform.LookAt(Anchor);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            Directory.CreateDirectory(output);
            Render(camera, Path.Combine(output, "08-hot-reload-marker.png"));
            float renderedRadius = RenderedVertices(marker).Max(point => Vector3.ProjectOnPlane(point - Anchor, Vector3.up).magnitude);
            File.WriteAllText(Path.Combine(output, "08-hot-reload-marker-notes.txt"),
                "Native Camera.Render of the production marker after a simulated component-version hot reload. " +
                "Existing _blastBaseScale/_timeBaseScale/_coreBaseScale, _blastRadius and configured transforms are preserved. " +
                "Only the newly introduced _baseScalesCached and cached layer-reference fields are reset to their defaults, then OnValidate/reconfigure runs. " +
                "The ground is an owned 40m cube top; camera/light are owned test objects. No overlays, compositing, gameplay scene or saves modified.\n" +
                FormattableString.Invariant($"requested_radius_m={CaptureRadius}; rendered_tangent_radius_m={renderedRadius}; contact={Anchor}\n"));
        }
        AssertTangentRadius(marker, Anchor, Vector3.up, CaptureRadius);
        for (int i = 0; i < layers.Length; i++)
            Assert.That(Vector3.Distance(layers[i].localScale, expectedScales[i]), Is.LessThan(.0001f),
                layers[i].name + " must not treat its resolved radius/pulse scale as its authored base after migration.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HotReloadMigration_UnconfiguredPrewarmedMarkerKeepsPulseAndCoreBases(bool validateBeforePrewarm)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var marker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>();
        marker.Prewarm();
        ApplyPhase(marker, .35f);
        Assert.That(marker.BlastRadius, Is.Zero, "This regression must exercise a marker that has never been configured.");
        Transform[] layers = marker.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.transform).ToArray();
        Vector3[] expectedScales = layers.Select(layer => layer.localScale).ToArray();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.That((bool)typeof(MortarLandingIndicatorVfx).GetField("_cached", flags).GetValue(marker), Is.True);
        typeof(MortarLandingIndicatorVfx).GetField("_baseScalesCached", flags).SetValue(marker, false);
        foreach (string field in new[] { "_cachedBlastLayer", "_cachedTimeLayer", "_cachedCoreLayer" })
            typeof(MortarLandingIndicatorVfx).GetField(field, flags).SetValue(marker, null);
        if (validateBeforePrewarm)
            typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", flags).Invoke(marker, null);
        marker.Prewarm();
        ApplyPhase(marker, .35f);
        for (int i = 0; i < layers.Length; i++)
            Assert.That(Vector3.Distance(layers[i].localScale, expectedScales[i]), Is.LessThan(.0001f),
                layers[i].name + " must retain its base despite radius remaining zero during migration.");

        // A reload without immediate validation must still retain its bases
        // when a later validation precedes the first real configuration.
        typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", flags).Invoke(marker, null);
        marker.Configure(Anchor, Vector3.up, CaptureRadius, .55f, WeaponUpgradePath.None);
        ApplyPhase(marker, .35f);
        AssertTangentRadius(marker, Anchor, Vector3.up, CaptureRadius);
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].name != "Authored Explosion Radius")
                Assert.That(Vector3.Distance(layers[i].localScale, expectedScales[i]), Is.LessThan(.0001f),
                    layers[i].name + " must remain correct on the first real configuration.");
    }

    [Test]
    public void InspectorValidation_AfterMigrationCapturesNewlyReplacedLayerScale()
    {
        var marker = Keep(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)))
            .GetComponent<MortarLandingIndicatorVfx>();
        marker.Prewarm();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MortarLandingIndicatorVfx).GetField("_baseScalesCached", flags).SetValue(marker, false);
        foreach (string field in new[] { "_cachedBlastLayer", "_cachedTimeLayer", "_cachedCoreLayer" })
            typeof(MortarLandingIndicatorVfx).GetField(field, flags).SetValue(marker, null);
        typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", flags).Invoke(marker, null);
        marker.Prewarm();

        Transform replacement = Keep(new GameObject("Replacement authored pulse")).transform;
        replacement.SetParent(marker.transform, false);
        Vector3 authoredScale = new(.9f, .8f, .7f);
        replacement.localScale = authoredScale;
        typeof(MortarLandingIndicatorVfx).GetField("_timeToImpactRing", flags).SetValue(marker, replacement);
        typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", flags).Invoke(marker, null);
        marker.Prewarm();
        ApplyPhase(marker, .35f);
        Vector3 expected = authoredScale * Mathf.Lerp(1.18f, .42f, .35f);
        Assert.That(Vector3.Distance(replacement.localScale, expected), Is.LessThan(.0001f),
            "An actual replacement must capture its own authored scale instead of inheriting the migrated layer's base.");
    }

    [Test]
    public void GroundProjection_UsesInstanceMeshesAndLeavesSharedAssetsUnmodified()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        MeshFilter[] sources = prefab.GetComponentsInChildren<MeshFilter>(true);
        var vertices = sources.Select(f => f.sharedMesh.vertices).ToArray();
        var triangles = sources.Select(f => f.sharedMesh.triangles).ToArray();
        byte[] prefabBytes = File.ReadAllBytes(PrefabPath);
        Uneven();
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        MeshFilter[] instances = marker.GetComponentsInChildren<MeshFilter>(true);
        Assert.That(instances.Length, Is.EqualTo(sources.Length), "Conformance must reuse the authored mesh layers.");
        for (int i = 0; i < sources.Length; i++)
        {
            Assert.That(instances[i].sharedMesh, Is.Not.SameAs(sources[i].sharedMesh));
            Assert.That(sources[i].sharedMesh.vertices, Is.EqualTo(vertices[i]));
            Assert.That(sources[i].sharedMesh.triangles, Is.EqualTo(triangles[i]));
        }
        Assert.That(File.ReadAllBytes(PrefabPath), Is.EqualTo(prefabBytes));
        marker.Configure(Anchor, Vector3.up, Radius, .55f, WeaponUpgradePath.None);
        Assert.That(marker.IsGroundConforming, Is.False);
        for (int i = 0; i < sources.Length; i++)
            Assert.That(instances[i].sharedMesh, Is.SameAs(sources[i].sharedMesh), "Planar mode must restore authored meshes.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WallAndCeilingContacts_RetainThePlanarAuthoredMarker(bool ceiling)
    {
        Plane(0f);
        MortarLandingIndicatorVfx marker = Marker(Anchor, Vector3.up);
        Vector3 normal = ceiling ? Vector3.down : Vector3.right;
        Vector3 contact = Anchor + Vector3.up * 2f;
        marker.ConfigureGroundContact(contact, normal, Radius, .55f, WeaponUpgradePath.PathB);
        ApplyPhase(marker, .35f);
        MortarLandingIndicatorVfx planar = Keep(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)))
            .GetComponent<MortarLandingIndicatorVfx>();
        planar.Configure(contact + normal * marker.SurfaceOffset, normal, Radius, .55f, WeaponUpgradePath.PathB);
        ApplyPhase(planar, .35f);
        Assert.That(marker.IsGroundConforming, Is.False);
        var actual = GeometrySnapshot(marker);
        var expected = GeometrySnapshot(planar);
        foreach (string name in expected.Keys)
        {
            Assert.That(actual[name].Length, Is.EqualTo(expected[name].Length));
            for (int i = 0; i < actual[name].Length; i++)
                Assert.That(Vector3.Distance(actual[name][i], expected[name][i]), Is.LessThan(.001f), name);
        }
    }

    [TestCase("01-flat", "flat")]
    [TestCase("02-slope", "slope")]
    [TestCase("03-uneven", "uneven")]
    [TestCase("04-step", "step")]
    [TestCase("05-ramp-edge", "ramp-edge")]
    public void CaptureNativeProductionMarker(string name, string terrain)
    {
        string output = Environment.GetEnvironmentVariable(CaptureEnvironment);
        if (string.IsNullOrWhiteSpace(output))
            Assert.Ignore("Set " + CaptureEnvironment + " to .utmp/mortar-ground-marker/captures to enable native captures.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Native Camera.Render captures require a graphics device.");
        Vector3 normal = Vector3.up;
        switch (terrain)
        {
            case "slope": normal = Plane(24f); break;
            case "uneven": Uneven(); break;
            case "step": Step(); break;
            case "ramp-edge": normal = RampEdge(); break;
            default: Plane(0f); break;
        }
        MortarLandingIndicatorVfx marker = Marker(Anchor, normal, radius: CaptureRadius);
        ApplyPhase(marker, .35f);
        AssertTangentRadius(marker, Anchor, normal, CaptureRadius);
        AssertSupportedTriangles(marker, normal, terrain == "uneven" ? .09f : .025f);
        for (int i = 0; i < 5; i++) marker.RefreshGroundGeometry();
        const int timingIterations = 60;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < timingIterations; i++) marker.RefreshGroundGeometry();
        stopwatch.Stop();
        double millisecondsPerRefresh = stopwatch.Elapsed.TotalMilliseconds / timingIterations;
        var light = Keep(new GameObject("Ground marker capture light", typeof(Light))).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        var camera = Keep(new GameObject("Native ground marker camera", typeof(Camera))).GetComponent<Camera>();
        camera.enabled = false;
        camera.scene = _scene;
        camera.cameraType = CameraType.Preview;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .07f, .09f);
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 40f;
        camera.fieldOfView = 40f;
        camera.transform.position = Anchor + new Vector3(4.8f, 4.2f, -6f);
        camera.transform.LookAt(Anchor + Vector3.up * .15f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        Render(camera, Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".csv"),
            "terrain,contact_x,contact_y,contact_z,radius,surface_offset,rendered_triangle_count,refresh_iterations,refresh_total_ms,ms_per_refresh\n" +
            FormattableString.Invariant($"{terrain},0,0,0,{marker.BlastRadius},{marker.SurfaceOffset},{Triangles(marker).Count()},{timingIterations},{stopwatch.Elapsed.TotalMilliseconds:F4},{millisecondsPerRefresh:F4}\n"));
        File.WriteAllText(Path.Combine(output, "capture-notes.txt"),
            "Native Unity Camera.Render captures of the production GF_Mortar_ManualLandingIndicator prefab. " +
            "The production ConfigureGroundContact and animation ApplyFrame methods project the authored mesh layers. " +
            "Terrain and overview camera are owned test fixtures at (1000,100,1000) in the runner's existing scene. " +
            "No scene is created or saved, and no gameplay assets or user saves are written. " +
            "No image compositing or UI overlays are used. CSV files record radius, visual offset and rendered triangle count. " +
            "Timing is a native Editor sample of 60 synchronous RefreshGroundGeometry calls after five warmups on each owned terrain; " +
            "it excludes rendering and is informational, with no hardware-dependent pass/fail threshold. Actual gameplay profiling remains separate.\n");
    }

    private MortarLandingIndicatorVfx Marker(Vector3 contact, Vector3 normal, Transform ignoredRoot = null, float radius = Radius)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        var marker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>();
        Assert.That(marker, Is.Not.Null);
        Physics.SyncTransforms();
        marker.ConfigureGroundContact(contact, normal, radius, .55f, WeaponUpgradePath.None, ignoredRoot);
        return marker;
    }

    private void AssertSupportedTriangles(MortarLandingIndicatorVfx marker, Vector3 projectionNormal, float maximumGap)
    {
        int triangleCount = 0;
        foreach (Vector3[] triangle in Triangles(marker))
        {
            triangleCount++;
            Vector3[] samples = { triangle[0], triangle[1], triangle[2],
                (triangle[0] + triangle[1]) * .5f, (triangle[1] + triangle[2]) * .5f,
                (triangle[2] + triangle[0]) * .5f, (triangle[0] + triangle[1] + triangle[2]) / 3f };
            foreach (Vector3 sample in samples)
            {
                Assert.That(SurfaceBelow(sample, projectionNormal, out RaycastHit support), Is.True,
                    "Rendered geometry extends over unsupported terrain at " + (sample - Anchor));
                float gap = Vector3.Dot(sample - support.point, support.normal);
                Assert.That(gap, Is.InRange(-.003f, maximumGap),
                    "Rendered geometry must stay close above its actual supporting surface at " + (sample - Anchor));
            }
        }
        Assert.That(triangleCount, Is.GreaterThan(8), "The ground marker must remain visible.");
    }

    private bool SurfaceBelow(Vector3 point, Vector3 normal, out RaycastHit closest)
    {
        closest = default;
        float distance = float.PositiveInfinity;
        Ray ray = new(point + normal * 4f, -normal);
        foreach (Collider collider in _ground)
        {
            if (!collider.Raycast(ray, out RaycastHit hit, 8f) || hit.distance >= distance) continue;
            closest = hit;
            distance = hit.distance;
        }
        return !float.IsPositiveInfinity(distance);
    }

    private static void AssertTangentRadius(MortarLandingIndicatorVfx marker, Vector3 contact, Vector3 normal, float expectedRadius = Radius)
    {
        MeshFilter ring = marker.transform.Find("Authored Explosion Radius").GetComponent<MeshFilter>();
        float radius = ring.sharedMesh.vertices.Max(vertex =>
            Vector3.ProjectOnPlane(ring.transform.TransformPoint(vertex) - contact, normal).magnitude);
        Assert.That(radius, Is.EqualTo(expectedRadius).Within(.015f), "Ground conformance must preserve the authored tangent-plane blast radius.");
        Assert.That(marker.BlastRadius, Is.EqualTo(expectedRadius));
    }

    private static bool InsideAuthoredTangentTriangles(Vector3 point, Vector3[] vertices, int[] indices, Vector3 normal)
    {
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            float orientation = Vector3.Dot(Vector3.Cross(b - a, c - a), normal);
            if (Mathf.Abs(orientation) < .0000001f) continue;
            float sign = Mathf.Sign(orientation);
            if (InsideEdge(a, b) && InsideEdge(b, c) && InsideEdge(c, a)) return true;
            bool InsideEdge(Vector3 start, Vector3 end)
            {
                Vector3 edge = Vector3.ProjectOnPlane(end - start, normal);
                return Vector3.Dot(Vector3.Cross(edge, point - start), normal) * sign >= -.001f * edge.magnitude;
            }
        }
        return false;
    }

    private static float TangentArea(Vector3[] vertices, int[] indices, Vector3 normal)
    {
        float area = 0f;
        for (int i = 0; i < indices.Length; i += 3)
            area += Mathf.Abs(Vector3.Dot(Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]],
                vertices[indices[i + 2]] - vertices[indices[i]]), normal)) * .5f;
        return area;
    }

    private static IEnumerable<Vector3[]> Triangles(MortarLandingIndicatorVfx marker)
    {
        foreach (MeshFilter filter in marker.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] indices = filter.sharedMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
                yield return new[] { filter.transform.TransformPoint(vertices[indices[i]]),
                    filter.transform.TransformPoint(vertices[indices[i + 1]]), filter.transform.TransformPoint(vertices[indices[i + 2]]) };
        }
    }

    private static Vector3[] RenderedVertices(MortarLandingIndicatorVfx marker) => Triangles(marker).SelectMany(t => t).ToArray();

    private static Dictionary<string, Vector3[]> GeometrySnapshot(MortarLandingIndicatorVfx marker) =>
        marker.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name,
            f => f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray());

    private Vector3 Plane(float degrees)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, degrees);
        Vector3 normal = rotation * Vector3.up;
        GameObject ground = GroundCube("Planar ground", -normal * .25f, new Vector3(12f, .5f, 12f));
        ground.transform.rotation = rotation;
        Physics.SyncTransforms();
        return normal;
    }

    private void Step()
    {
        GroundCube("Lower step", new Vector3(-2.8f, -.25f, 0f), new Vector3(6.4f, .5f, 10f));
        GroundCube("Upper step", new Vector3(3.4f, .3f, 0f), new Vector3(6f, 1f, 10f));
    }

    private Vector3 RampEdge()
    {
        GroundCube("Ramp base floor", new Vector3(0f, -1.45f, 0f), new Vector3(14f, .5f, 14f));
        Quaternion rotation = Quaternion.Euler(-24f, 0f, 0f);
        Vector3 normal = rotation * Vector3.up;
        GameObject ramp = GroundCube("Finite ramp", new Vector3(-.8f, 0f, 0f) - normal * .2f,
            new Vector3(2.8f, .4f, 8f));
        ramp.transform.rotation = rotation;
        var rampMaterial = Keep(new Material(GroundMaterial()) { color = new Color(.38f, .24f, .49f) });
        ramp.GetComponent<Renderer>().sharedMaterial = rampMaterial;
        Physics.SyncTransforms();
        return normal;
    }

    private void Uneven()
    {
        const int side = 49;
        const float spacing = .2f;
        var vertices = new Vector3[side * side];
        var indices = new List<int>((side - 1) * (side - 1) * 6);
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            float px = (x - (side - 1) * .5f) * spacing;
            float pz = (z - (side - 1) * .5f) * spacing;
            vertices[z * side + x] = new Vector3(px, .23f * Mathf.Sin(px * 1.5f) * Mathf.Cos(pz * 1.3f), pz);
            if (x == side - 1 || z == side - 1) continue;
            int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
            indices.AddRange(new[] { a, c, b, b, c, d });
        }
        Mesh mesh = Keep(new Mesh { name = "Owned uneven terrain mesh" });
        mesh.vertices = vertices;
        mesh.triangles = indices.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject ground = Keep(new GameObject("Uneven ground", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)));
        ground.transform.position = Anchor;
        ground.GetComponent<MeshFilter>().sharedMesh = mesh;
        ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();
        var collider = ground.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        _ground.Add(collider);
        Physics.SyncTransforms();
    }

    private GameObject GroundCube(string name, Vector3 center, Vector3 scale)
    {
        GameObject result = Cube(name, center, scale);
        _ground.Add(result.GetComponent<Collider>());
        return result;
    }

    private GameObject Cube(string name, Vector3 center, Vector3 scale)
    {
        GameObject result = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        result.name = name;
        result.transform.position = Anchor + center;
        result.transform.localScale = scale;
        result.GetComponent<Renderer>().sharedMaterial = GroundMaterial();
        Physics.SyncTransforms();
        return result;
    }

    private Material GroundMaterial()
    {
        if (_groundMaterial != null) return _groundMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Assert.That(shader, Is.Not.Null);
        _groundMaterial = Keep(new Material(shader) { color = new Color(.20f, .25f, .28f) });
        return _groundMaterial;
    }

    private T Keep<T>(T value) where T : Object
    {
        _objects.Add(value);
        if (value is GameObject go) SceneManager.MoveGameObjectToScene(go, _scene);
        return value;
    }

    private static void ApplyPhase(MortarLandingIndicatorVfx marker, float phase)
    {
        typeof(MortarLandingIndicatorVfx).GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(marker, new object[] { phase });
        marker.RefreshGroundGeometry();
    }

    private static void Render(Camera camera, string path)
    {
        var target = new RenderTexture(1280, 720, 24);
        target.Create();
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.aspect = 1280f / 720f;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(5000));
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }
    }
}
