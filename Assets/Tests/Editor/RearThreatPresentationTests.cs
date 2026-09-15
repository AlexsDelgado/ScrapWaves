using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class RearThreatPresentationTests
{
    private const float InnerRadius = 0.8f;
    private const float IdleOuterRadius = 1.1f;
    private const float MaximumOuterRadius = 1.65f;
    private const float MaximumSpikeLength = 0.65f;
    private const float MaximumExtent = MaximumOuterRadius + MaximumSpikeLength;
    private readonly List<Object> _cleanup = new();
    private Scene _previewScene;

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
    }

    [TestCase(IdleOuterRadius)]
    [TestCase(MaximumOuterRadius)]
    public void ClusteredFillIsOneContinuousRearStripWithAStableInnerClearance(float outerRadius)
    {
        var (fill, outline) = BuildGeometry(outerRadius,
            new[] { -65f, -12f, 0f, 12f, 64f }, new[] { 0.4f, 0.55f, 0.65f, 0.55f, 0.4f });
        AssertFiniteBoundedMesh(fill, RearThreatMesh.MaximumSamples * 2);
        AssertFiniteBoundedMesh(outline, RearThreatMesh.MaximumSamples * 4 + 8);
        AssertOneOpenArcStrip(fill);
        Assert.That(fill.vertices.Count(vertex => Mathf.Abs(vertex.magnitude - InnerRadius) < 0.00001f),
            Is.EqualTo(fill.vertexCount / 2), "Every radial sample must keep its inner boundary at the same radius.");
        Assert.That(fill.vertices.Max(vertex => vertex.magnitude),
            Is.EqualTo(outerRadius + MaximumSpikeLength).Within(0.0001f));
    }

    [Test]
    public void CoincidentAndEndCapSpikesDoNotCreateCracksOrDegenerateTriangles()
    {
        var (fill, outline) = BuildGeometry(MaximumOuterRadius,
            new[] { -95f, 95f, 0f, 0f, 95f }, new[] { 0.65f, 0.65f, 0.65f, 0.4f, 0.1f });
        AssertFiniteBoundedMesh(fill, RearThreatMesh.MaximumSamples * 2);
        AssertFiniteBoundedMesh(outline, RearThreatMesh.MaximumSamples * 4 + 8);
        AssertOneOpenArcStrip(fill);
    }

    [Test]
    public void IdleShapeRemainsAnOpenRearArcWithoutASpikeOrForwardBridge()
    {
        var (fill, outline) = BuildGeometry(IdleOuterRadius, new float[5], new float[5]);
        AssertFiniteBoundedMesh(fill, RearThreatMesh.MaximumSamples * 2);
        AssertFiniteBoundedMesh(outline, RearThreatMesh.MaximumSamples * 4 + 8);
        AssertOneOpenArcStrip(fill);
        Assert.That(fill.vertices.Max(vertex => vertex.magnitude), Is.EqualTo(IdleOuterRadius).Within(0.0001f));
        Assert.That(fill.vertices.Min(vertex => vertex.magnitude), Is.EqualTo(InnerRadius).Within(0.0001f));
        Assert.That(fill.vertices.All(vertex => vertex.z <= IdleOuterRadius * Mathf.Sin(5f * Mathf.Deg2Rad) + 0.0001f),
            Is.True, "The opening must face forward, allowing only the five-degree exit margin beyond the rear hemisphere.");
    }

    [Test]
    public void PreviewChangesReuseTheAuthoredHierarchyMaterialsAndOwnedMeshes()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        RearThreatValidation.SetPreview(presenter, 0f, Array.Empty<float>(), Array.Empty<float>());
        Mesh fill = presenter.Fill.sharedMesh;
        Mesh outline = presenter.Outline.sharedMesh;
        int[] hierarchy = presenter.GetComponentsInChildren<Transform>(true).Select(value => value.GetInstanceID()).ToArray();
        Material fillMaterial = presenter.Fill.GetComponent<Renderer>().sharedMaterial;
        Material outlineMaterial = presenter.Outline.GetComponent<Renderer>().sharedMaterial;
        Assert.That(fill.vertices.Max(vertex => vertex.magnitude), Is.EqualTo(0.86f).Within(0.0001f));

        RearThreatValidation.SetPreview(presenter, 1f, Array.Empty<float>(), Array.Empty<float>());
        Assert.That(presenter.Fill.sharedMesh, Is.SameAs(fill));
        Assert.That(presenter.Outline.sharedMesh, Is.SameAs(outline));
        Assert.That(fill.vertices.Max(vertex => vertex.magnitude), Is.EqualTo(0.8815f).Within(0.0001f),
            "Without a threat bearing, only the small global share of growth should be visible.");
        Assert.That(fill.vertices.Min(vertex => vertex.magnitude), Is.EqualTo(InnerRadius).Within(0.0001f));
        RearThreatValidation.SetPreview(presenter, 0.6f, new[] { -20f, 20f }, new[] { 0.8f, 0.8f });
        Assert.That(presenter.GetComponentsInChildren<Transform>(true).Select(value => value.GetInstanceID()).ToArray(), Is.EqualTo(hierarchy));
        Assert.That(presenter.Fill.GetComponent<Renderer>().sharedMaterial, Is.SameAs(fillMaterial));
        Assert.That(presenter.Outline.GetComponent<Renderer>().sharedMaterial, Is.SameAs(outlineMaterial));
        AssertFiniteBoundedMesh(fill, RearThreatMesh.MaximumSamples * 2);
        AssertOneOpenArcStrip(fill);
    }

    [Test]
    public void UrgencyUsesTheAuthoredPaletteWithoutChangingSharedMaterials()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        Renderer renderer = presenter.Fill.GetComponent<Renderer>();
        Color sharedColor = renderer.sharedMaterial.GetColor("_BaseColor");
        var serialized = new SerializedObject(presenter);
        Color idle = serialized.FindProperty("_idleColor").colorValue;
        Color alert = serialized.FindProperty("_alertColor").colorValue;
        var properties = new MaterialPropertyBlock();
        RearThreatValidation.SetPreview(presenter, 0f, Array.Empty<float>(), Array.Empty<float>());
        renderer.GetPropertyBlock(properties);
        AssertColorWithin(properties.GetColor("_BaseColor"), idle);
        Assert.That(idle.a, Is.EqualTo(0.08f).Within(0.0001f));
        RearThreatValidation.SetPreview(presenter, 1f, Array.Empty<float>(), Array.Empty<float>());
        renderer.GetPropertyBlock(properties);
        AssertColorWithin(properties.GetColor("_BaseColor"), alert);
        Assert.That(alert.a, Is.EqualTo(0.95f).Within(0.0001f));
        Assert.That(presenter.Outline.GetComponent<Renderer>().enabled, Is.False, "The authored style has no outline.");
        AssertColorWithin(renderer.sharedMaterial.GetColor("_BaseColor"), sharedColor);
    }

    [Test]
    public void PreviewNeverWritesIntoThePersistentPrefabMeshes()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RearThreatValidation.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Author the indicator prefab before running presentation validation.");
        RearThreatPresenter assetPresenter = prefab.GetComponent<RearThreatPresenter>();
        Mesh fillAsset = assetPresenter.Fill.sharedMesh;
        Mesh outlineAsset = assetPresenter.Outline.sharedMesh;
        Vector3[] originalFill = fillAsset.vertices;
        Vector3[] originalOutline = outlineAsset.vertices;
        Assert.That(AssetDatabase.Contains(fillAsset), Is.True);
        Assert.That(AssetDatabase.Contains(outlineAsset), Is.True);

        RearThreatPresenter presenter = CreateAuthoredPreview();
        RearThreatValidation.SetPreview(presenter, 1f, new[] { -65f, -12f, 0f, 12f, 64f },
            new[] { 1f, 1f, 1f, 1f, 1f });

        Assert.That(presenter.Fill.sharedMesh, Is.Not.SameAs(fillAsset));
        Assert.That(presenter.Outline.sharedMesh, Is.Not.SameAs(outlineAsset));
        Assert.That(fillAsset.vertices, Is.EqualTo(originalFill));
        Assert.That(outlineAsset.vertices, Is.EqualTo(originalOutline));
    }

    [Test]
    public void FlexStaysNearThreatsAndKeepsClearancePeakBearingAndGrowthBounds()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        RearThreatValidation.SetPreview(presenter, 1f, new[] { 0f }, new[] { 1f });
        Mesh fill = presenter.Fill.sharedMesh;
        Vector3[] vertices = fill.vertices;
        AssertOneOpenArcStrip(fill);
        AssertFiniteBoundedMesh(fill, RearThreatMesh.MaximumSamples * 2);
        for (int i = 0; i < vertices.Length; i += 2)
            Assert.That(vertices[i].magnitude, Is.EqualTo(InnerRadius).Within(0.0001f));
        Vector3 peak = vertices.OrderByDescending(vertex => vertex.magnitude).First();
        Assert.That(Vector3.Angle(peak, Vector3.back), Is.LessThan(0.01f));
        Assert.That(peak.magnitude, Is.EqualTo(1.596f).Within(0.0001f));
        float quietSide = OuterRadiusAt(fill, 65f);
        float shoulder = OuterRadiusAt(fill, 12f);
        Assert.That(quietSide, Is.EqualTo(0.8815f).Within(0.0001f));
        Assert.That(shoulder, Is.GreaterThan(quietSide + 0.005f),
            "The arc must bend outside the narrow tip, in the direction of the threat.");
        Assert.That(vertices.Max(vertex => vertex.magnitude), Is.LessThanOrEqualTo(1.5961f));
    }

    [Test]
    public void EmergingSpikeIsRounderThanAFullUrgencySpike()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        RearThreatValidation.SetPreview(presenter, 0f, new[] { 0f }, new[] { 0f });
        Mesh fill = presenter.Fill.sharedMesh;
        float softRatio = (OuterRadiusAt(fill, 8f) - OuterRadiusAt(fill, 65f))
            / (OuterRadiusAt(fill, 0f) - OuterRadiusAt(fill, 65f));
        RearThreatValidation.SetPreview(presenter, 1f, new[] { 0f }, new[] { 1f });
        float sharpRatio = (OuterRadiusAt(fill, 8f) - OuterRadiusAt(fill, 65f))
            / (OuterRadiusAt(fill, 0f) - OuterRadiusAt(fill, 65f));
        Assert.That(softRatio, Is.GreaterThan(sharpRatio + 0.2f),
            "A newly emerging spike should be a broad mound before it sharpens at close range.");
    }

    [Test]
    public void SpikeBeyondArcDoesNotPullTheVisibleEdge()
    {
        var (fill, outline) = BuildGeometry(0.946f, new float[5], new float[5]);
        new RearThreatMesh().Update(fill, outline, InnerRadius, 0.946f, 95f, 10f, 0.01f, 1.596f,
            new[] { 96f, -96f, 0f, 0f, 0f }, new[] { 0.65f, 0.65f, 0f, 0f, 0f }, 0.75f, 0.86f);
        for (int i = 1; i < fill.vertexCount; i += 2)
            Assert.That(fill.vertices[i].magnitude, Is.EqualTo(0.8815f).Within(0.0001f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void SpringRisesQuicklyRelaxesGentlyAndComesToRest(int framesPerSecond)
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        Action<RearThreatSnapshot, float> advance = VisualStepper(presenter);
        RearThreatSnapshot threat = Snapshot(1, 0f, 1f);
        RearThreatSnapshot empty = Snapshot(0, 0f, 0f);
        float dt = 1f / framesPerSecond;
        float previous = 0f;
        for (int i = 0; i < framesPerSecond; i++)
        {
            advance(threat, dt);
            float current = Lengths(presenter)[0];
            Assert.That(current, Is.InRange(previous, 0.650001f), "Arrival must not bounce or overshoot its configured limit.");
            previous = current;
        }
        Assert.That(previous, Is.EqualTo(0.65f).Within(0.001f));
        for (int i = 0; i < framesPerSecond / 10; i++) advance(empty, dt);
        Assert.That(Lengths(presenter)[0], Is.GreaterThan(0.4f), "Retreat must relax rather than abruptly disappear.");
        for (int i = 0; i < framesPerSecond * 2; i++) advance(empty, dt);
        Assert.That(Lengths(presenter), Is.All.Zero);
        Assert.That(Field<float>(presenter, "_urgency"), Is.Zero);
        Assert.That(Field<float[]>(presenter, "_lengthVelocities"), Is.All.Zero);
        advance(empty, dt);
        Assert.That(Lengths(presenter), Is.All.Zero, "Idle must have no perpetual breathing or oscillation.");
    }

    [Test]
    public void PausingFreezesSpringMomentumAndReplacementEmergesAtItsOwnBearing()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        Action<RearThreatSnapshot, float> advance = VisualStepper(presenter);
        RearThreatSnapshot threat = Snapshot(1, -65f, 1f);
        for (int i = 0; i < 10; i++) advance(threat, 1f / 60f);
        float[] lengths = (float[])Lengths(presenter).Clone();
        float[] velocities = (float[])Field<float[]>(presenter, "_lengthVelocities").Clone();
        RearThreatSnapshot replacement = Snapshot(2, 65f, 1f);
        advance(replacement, 0f);
        Assert.That(Lengths(presenter), Is.EqualTo(lengths));
        Assert.That(Field<float[]>(presenter, "_lengthVelocities"), Is.EqualTo(velocities));
        // Occupy all slots so the next unrelated group must replace a visible one.
        var spikes = new RearThreatSpike[5];
        for (int i = 0; i < spikes.Length; i++) spikes[i] = new RearThreatSpike(i + 1, RearThreatMesh.Direction(-65f + i * 25f), 2f, 1f, 1);
        RearThreatSnapshot crowded = Snapshot(spikes, 1f);
        for (int i = 0; i < 60; i++) advance(crowded, 1f / 60f);
        replacement = Snapshot(9, 70f, 1f);
        advance(replacement, 1f / 60f);
        int slot = Array.IndexOf(Field<int[]>(presenter, "_trackIds"), 9);
        Assert.That(slot, Is.GreaterThanOrEqualTo(0));
        Assert.That(Field<float[]>(presenter, "_angles")[slot], Is.EqualTo(70f).Within(0.01f));
        Assert.That(Lengths(presenter)[slot], Is.InRange(0f, 0.1f), "Replacement must emerge small at the new bearing, not drag a fully grown spike across the arc.");
    }

    private static Action<RearThreatSnapshot, float> VisualStepper(RearThreatPresenter presenter) =>
        (Action<RearThreatSnapshot, float>)Delegate.CreateDelegate(typeof(Action<RearThreatSnapshot, float>), presenter,
            typeof(RearThreatPresenter).GetMethod("AdvanceVisuals", BindingFlags.Instance | BindingFlags.NonPublic));

    [Test]
    public void PausedBasisChangesPreserveWorldBearingsAndReconfigurationClearsMomentum()
    {
        RearThreatPresenter presenter = CreateAuthoredPreview();
        Action<RearThreatSnapshot, float> advance = VisualStepper(presenter);
        RearThreatSnapshot threat = Snapshot(1, -35f, 1f);
        for (int i = 0; i < 10; i++) advance(threat, 1f / 60f);
        Vector3 bearing = Field<Vector3[]>(presenter, "_directions")[0];
        float length = Lengths(presenter)[0];
        float velocity = Field<float[]>(presenter, "_lengthVelocities")[0];
        Vector3 forward = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward;
        var rotated = (RearThreatSnapshot)Activator.CreateInstance(typeof(RearThreatSnapshot), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { new[] { threat.GetSpike(0) }, 1, true, forward, 1f }, null);
        advance(rotated, 0f);
        Vector3 renderedBearing = Quaternion.LookRotation(forward) * RearThreatMesh.Direction(Field<float[]>(presenter, "_angles")[0]);
        Assert.That(Vector3.Angle(renderedBearing, bearing), Is.LessThan(0.01f));
        Assert.That(Lengths(presenter)[0], Is.EqualTo(length));
        Assert.That(Field<float[]>(presenter, "_lengthVelocities")[0], Is.EqualTo(velocity));
        presenter.Configure(presenter.Sensor, presenter.Fill, presenter.Outline);
        Assert.That(Lengths(presenter), Is.All.Zero);
        Assert.That(Field<float[]>(presenter, "_lengthVelocities"), Is.All.Zero);
        Assert.That(Field<float>(presenter, "_urgencyVelocity"), Is.Zero);
        Assert.That(Field<int[]>(presenter, "_trackIds"), Is.All.Zero);
    }

    private static RearThreatSnapshot Snapshot(int track, float angle, float urgency) => Snapshot(
        track == 0 ? Array.Empty<RearThreatSpike>() : new[] { new RearThreatSpike(track, RearThreatMesh.Direction(angle), 2f, urgency, 1) }, urgency);

    private static RearThreatSnapshot Snapshot(RearThreatSpike[] spikes, float urgency) =>
        (RearThreatSnapshot)Activator.CreateInstance(typeof(RearThreatSnapshot), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { spikes, spikes.Length, true, Vector3.forward, urgency }, null);

    private static T Field<T>(RearThreatPresenter presenter, string name) =>
        (T)typeof(RearThreatPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);

    private static float[] Lengths(RearThreatPresenter presenter) => Field<float[]>(presenter, "_lengths");

    private static float OuterRadiusAt(Mesh mesh, float angle)
    {
        Vector3[] vertices = mesh.vertices;
        int nearest = 1;
        float difference = float.PositiveInfinity;
        for (int i = 1; i < vertices.Length; i += 2)
        {
            float value = Vector3.Angle(vertices[i], RearThreatMesh.Direction(angle));
            if (value < difference) { difference = value; nearest = i; }
        }
        return vertices[nearest].magnitude;
    }

    private RearThreatPresenter CreateAuthoredPreview()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RearThreatValidation.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Author the indicator prefab before running presentation validation.");
        _previewScene = EditorSceneManager.NewPreviewScene();
        GameObject instance = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(instance, _previewScene);
        var player = new GameObject("Rear threat validation player");
        SceneManager.MoveGameObjectToScene(player, _previewScene);
        RearThreatPresenter presenter = instance.GetComponent<RearThreatPresenter>();
        Assert.That(presenter, Is.Not.Null);
        presenter.BindPlayer(player.transform);
        return presenter;
    }

    private (Mesh Fill, Mesh Outline) BuildGeometry(float outerRadius, float[] angles, float[] lengths)
    {
        var fill = new Mesh();
        var outline = new Mesh();
        _cleanup.Add(fill);
        _cleanup.Add(outline);
        new RearThreatMesh().Update(fill, outline, InnerRadius, outerRadius, 95f, 10f, 0.03f,
            MaximumExtent, angles, lengths);
        return (fill, outline);
    }

    private static void AssertFiniteBoundedMesh(Mesh mesh, int maximumVertexCount)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Assert.That(vertices.Length, Is.InRange(4, maximumVertexCount));
        Assert.That(triangles.Length, Is.GreaterThan(0));
        Assert.That(triangles.Length % 3, Is.Zero);
        foreach (Vector3 vertex in vertices)
        {
            Assert.That(IsFinite(vertex.x) && IsFinite(vertex.y) && IsFinite(vertex.z), Is.True);
            Assert.That(vertex.y, Is.Zero.Within(0.000001f), "The indicator must remain horizontal.");
            Assert.That(vertex.magnitude, Is.InRange(InnerRadius - 0.00001f, MaximumExtent + 0.0001f));
            Assert.That(mesh.bounds.Contains(vertex), Is.True, "Bounds must include every spike tip.");
        }
        float winding = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            for (int j = 0; j < 3; j++) Assert.That(triangles[i + j], Is.InRange(0, vertices.Length - 1));
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            float signedArea = Vector3.Cross(b - a, c - a).y;
            Assert.That(Mathf.Abs(signedArea), Is.GreaterThan(0.0000000001f), "No duplicate samples or zero-area triangles.");
            if (winding == 0f) winding = Mathf.Sign(signedArea);
            Assert.That(Mathf.Sign(signedArea), Is.EqualTo(winding), "Triangles must keep consistent winding across spikes.");
            Assert.That(ContainsOrigin(a, b, c), Is.False, "No triangle may bridge across the player's clearance.");
        }
    }

    private static void AssertOneOpenArcStrip(Mesh fill)
    {
        int[] triangles = fill.triangles;
        var edges = new Dictionary<(int, int), int>();
        var used = new HashSet<int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            for (int j = 0; j < 3; j++)
            {
                int a = triangles[i + j];
                int b = triangles[i + (j + 1) % 3];
                used.Add(a);
                var edge = (Math.Min(a, b), Math.Max(a, b));
                edges.TryGetValue(edge, out int count);
                edges[edge] = count + 1;
            }
        }
        Assert.That(used.Count, Is.EqualTo(fill.vertexCount), "No unused buffer vertices should be submitted.");
        Assert.That(edges.Values.All(count => count == 1 || count == 2), Is.True, "No non-manifold or overlapping fill edges.");
        Assert.That(used.Count - edges.Count + triangles.Length / 3, Is.EqualTo(1), "The crescent must be one open radial strip.");
        var boundary = new Dictionary<int, List<int>>();
        foreach (var edge in edges.Where(pair => pair.Value == 1).Select(pair => pair.Key))
        {
            AddNeighbour(boundary, edge.Item1, edge.Item2);
            AddNeighbour(boundary, edge.Item2, edge.Item1);
        }
        Assert.That(boundary.Count, Is.GreaterThan(0));
        Assert.That(boundary.Values.All(neighbours => neighbours.Count == 2), Is.True, "Inner/outer edges and end caps must meet without cracks.");
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(boundary.Keys.First());
        while (pending.Count > 0)
        {
            int vertex = pending.Pop();
            if (!visited.Add(vertex)) continue;
            foreach (int neighbour in boundary[vertex]) pending.Push(neighbour);
        }
        Assert.That(visited.Count, Is.EqualTo(boundary.Count), "One closed contour must include both end caps; no disconnected arrows or full ring.");
    }

    private static void AddNeighbour(Dictionary<int, List<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out List<int> neighbours)) adjacency[from] = neighbours = new List<int>();
        neighbours.Add(to);
    }

    private static bool ContainsOrigin(Vector3 a, Vector3 b, Vector3 c)
    {
        float ab = a.x * b.z - a.z * b.x;
        float bc = b.x * c.z - b.z * c.x;
        float ca = c.x * a.z - c.z * a.x;
        return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void AssertColorWithin(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.000001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.000001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.000001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.000001f));
    }
}
