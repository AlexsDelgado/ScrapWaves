using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Saved sandbox floor/panel dimensions, without loading or changing a scene.</summary>
public sealed class MortarThinPanelTests
{
    private const string MarkerPath = "Assets/GameFeel/Prefabs/Weapons/Mortar/GF_Mortar_ManualLandingIndicator.prefab";
    private const string CaptureEnvironment = "SCRAPWAVES_MORTAR_GROUND_CAPTURE_DIRECTORY";
    private const float CollisionRadius = .18f;
    private readonly List<Object> _objects = new();
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private readonly MortarTuning _tuning = new();
    private Vector3 _anchor;
    private BoxCollider _panel;
    private Scene _scene;

    private struct Sample
    {
        public float Pitch, X, Z;
        public MortarManualAimSolution Aim;
        public RaycastHit Selected, Predicted;
        public bool Resolved, HasSelected, HasPredicted;
    }

    private sealed class RecordingSink : IWeaponPresentationSink
    {
        public readonly List<WeaponPresentationContext> Cues = new();
        public void Emit(in WeaponPresentationContext context) => Cues.Add(context);
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    [SetUp]
    public void SetUp() => _scene = SceneManager.GetActiveScene();

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
    }

    [TestCase(1f)]
    [TestCase(1.5f)]
    public void ActualManualShell_ImpactsTheSelectedPanelAtColdAndHotSpeeds(float speedMultiplier)
    {
        CreateSandboxPanel(0f);
        Sample sample = ResolveSample(0f, 0f, 0f);
        Assert.That(sample.Resolved && sample.HasPredicted, Is.True, Describe(sample));
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset");
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        Assert.That(profile, Is.Not.Null);
        Assert.That(data, Is.Not.Null);
        var shell = Keep(Object.Instantiate(profile.Mortar.ShellPrefab)).GetComponent<MortarShellImpact>();
        var sink = new RecordingSink();
        Set(shell, "_payload", MortarUpgradePayload.None);
        Set(shell, "_manualTargeting", true);
        Set(shell, "_manualLandingNormal", sample.Aim.SurfaceNormal);
        Set(shell, "_presentationSink", sink);
        Set(shell, "_hasFeedbackTemplate", true);
        Set(shell, "_feedbackTemplate", new WeaponFeedbackContext(new WeaponInstance { Data = data, Level = 1 },
            WeaponFeedbackMode.Manual, speedMultiplier > 1f ? 1f : 0f,
            sample.Aim.Origin, sample.Aim.Target - sample.Aim.Origin));
        float travel = _tuning.MortarManualTravelTime / speedMultiplier;
        Invoke(shell, "Configure", sample.Aim.Origin, sample.Aim.Target, travel, sample.Aim.ArcHeight,
            1, _tuning.MortarManualExplosionRadius, 0f, 0f, CollisionRadius, null);
        for (int step = 0; step < 256 && shell != null && sink.Cues.Count == 0; step++)
            Invoke(shell, "Tick", travel / 128f);
        Assert.That(sink.Cues.Count, Is.EqualTo(1), "The actual shell must produce one impact.");
        Assert.That(Vector3.Distance(sink.Cues[0].Position, sample.Aim.SurfacePoint), Is.LessThan(.02f),
            "The live shell must hit the panel, rather than consume PhysX's invalid zero-world-point overlap result.");
        Assert.That(sink.Cues[0].Position.y, Is.EqualTo(.05f).Within(.0002f));
    }

    [Test]
    public void LegitimateContactAtWorldOrigin_IsAccepted()
    {
        _anchor = Vector3.zero;
        Collider floor = Cube("Legitimate world-origin floor", new Vector3(0f, -.5f, 0f),
            new Vector3(4f, 1f, 4f), Color.gray).GetComponent<Collider>();
        Physics.SyncTransforms();
        Assert.That(MortarTrajectory.TryGetSegmentCollision(Vector3.up, Vector3.down, CollisionRadius,
            null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(floor));
        Assert.That(hit.point.magnitude, Is.LessThan(.0002f), "Zero is a valid world contact coordinate.");
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThan(.999f));
    }

    [TestCase("floor")]
    [TestCase("wall")]
    [TestCase("ceiling")]
    public void MidflightOverlappingSegment_RecoversActualSurfacePointAndNormal(string face)
    {
        _anchor = new Vector3(1000f, 0f, 1000f);
        Vector3 normal = face == "wall" ? Vector3.right : face == "ceiling" ? Vector3.down : Vector3.up;
        Vector3 localContact = new(0f, 2f, 14f);
        Vector3 scale = face == "wall" ? new Vector3(.1f, 4f, 4f) : new Vector3(4f, .1f, 4f);
        Collider obstacle = Cube("Midflight " + face, localContact - normal * .05f, scale, Color.gray).GetComponent<Collider>();
        Physics.SyncTransforms();
        Vector3 start = _anchor + localContact + normal * .17f;
        Vector3 end = start - normal * .2f + Vector3.forward * .1f;
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, end, CollisionRadius,
            null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(obstacle));
        Assert.That(Vector3.Distance(hit.point, _anchor + localContact), Is.LessThan(.002f));
        Assert.That(Vector3.Dot(hit.normal, normal), Is.GreaterThan(.999f));
    }

    [Test]
    public void MidflightNonconvexMeshOverlap_RecoversSurfaceWithoutUnsupportedClosestPoint()
    {
        Vector3 contact = new(1000f, 2f, 1014f);
        var mesh = Keep(new Mesh { name = "Owned upward-facing nonconvex terrain quad" });
        mesh.vertices = new[]
        {
            new Vector3(-2f, 0f, -2f), new Vector3(-2f, 0f, 2f),
            new Vector3(2f, 0f, 2f), new Vector3(2f, 0f, -2f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var terrain = Keep(new GameObject("Nonconvex touching terrain", typeof(MeshCollider)));
        terrain.transform.position = contact;
        MeshCollider collider = terrain.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
        Physics.SyncTransforms();
        Vector3 start = contact + Vector3.up * .17f;
        Vector3 end = start + new Vector3(0f, -.2f, .1f);
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, end, CollisionRadius,
            null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider));
        Assert.That(hit.point.y, Is.EqualTo(contact.y).Within(.0002f));
        Assert.That(Vector3.Distance(hit.point, contact), Is.LessThan(.1f));
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThan(.999f));
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase(-20f, 5f, 0f)]
    [TestCase(0f, 18f, 0f)]
    [TestCase(7.5f, 22f, 0f)]
    [TestCase(-.05f, -1f, 0f)]
    [TestCase(.05f, -1f, 0f)]
    [TestCase(-20f, 5f, 100f)]
    [TestCase(0f, 18f, 100f)]
    [TestCase(7.5f, 22f, 100f)]
    [TestCase(-.05f, -1f, 100f)]
    [TestCase(.05f, -1f, 100f)]
    public void SelectedPanelAndFirstPredictedCollisionAgreeAcrossMovement(float pitch, float expectedRange, float elevation)
    {
        CreateSandboxPanel(elevation);
        var samples = Grid(pitch).ToArray();
        var failures = new List<string>();
        foreach (Sample sample in samples)
        {
            Assert.That(sample.Aim.SurfacePoint.x, Is.InRange(_panel.bounds.min.x, _panel.bounds.max.x));
            Assert.That(sample.Aim.SurfacePoint.z, Is.InRange(_panel.bounds.min.z, _panel.bounds.max.z),
                "The movement grid must remain inside the actual saved panel's horizontal bounds.");
            bool valid = sample.Resolved && sample.HasSelected && sample.Selected.collider == _panel
                && sample.HasPredicted && sample.Predicted.collider == _panel
                && Mathf.Abs(sample.Aim.SurfacePoint.y - (_anchor.y + .05f)) < .0002f
                && Mathf.Abs(sample.Predicted.point.y - (_anchor.y + .05f)) < .0002f;
            if (expectedRange > 0f)
                valid &= Mathf.Abs(sample.Aim.Range - expectedRange) < .001f;
            if (!valid) failures.Add(Describe(sample));
        }
        string output = Environment.GetEnvironmentVariable(CaptureEnvironment);
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            string suffix = FormattableString.Invariant($"pitch-{pitch}-elevation-{elevation}").Replace('.', '_');
            WriteSamples(Path.Combine(output, "thin-panel-" + suffix + ".csv"), samples);
        }
        Assert.That(samples.Length, Is.EqualTo(35));
        Assert.That(failures, Is.Empty,
            "The selected panel top is +0.05m, radius is 0.18m, and target center is +0.23m. " +
            "The lower floor top is -0.01m. First collision must remain on the selected panel.\n" +
            string.Join("\n", failures.Take(12)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CaptureNativePanelPredictionAndSelectedSurfaceReference(bool selectedSurfaceReference)
    {
        string output = Environment.GetEnvironmentVariable(CaptureEnvironment);
        if (string.IsNullOrWhiteSpace(output)) Assert.Ignore("Set " + CaptureEnvironment + " for diagnostic native captures.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Native rendering requires a graphics device.");
        CreateSandboxPanel(0f);
        Sample[] samples = new[] { 0f, -20f, 7.5f, -.05f, .05f }.SelectMany(Grid).ToArray();
        Sample chosen = samples.FirstOrDefault(sample => sample.Resolved && sample.HasSelected
            && sample.Selected.collider == _panel && sample.HasPredicted
            && (sample.Predicted.collider != _panel || Vector3.Distance(sample.Predicted.point, sample.Aim.SurfacePoint) > .02f
                || Vector3.Dot(sample.Predicted.normal, sample.Aim.SurfaceNormal) < .99f));
        if (!chosen.Resolved) chosen = samples[0];
        Assert.That(chosen.Resolved && chosen.HasSelected && chosen.HasPredicted, Is.True, Describe(chosen));
        Vector3 contact = selectedSurfaceReference ? chosen.Aim.SurfacePoint : chosen.Predicted.point;
        Vector3 normal = selectedSurfaceReference ? chosen.Aim.SurfaceNormal : chosen.Predicted.normal;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MarkerPath);
        Assert.That(prefab, Is.Not.Null);
        MortarLandingIndicatorVfx marker = Keep(Object.Instantiate(prefab)).GetComponent<MortarLandingIndicatorVfx>();
        marker.ConfigureGroundContact(contact, normal, 2.5f, _tuning.MortarManualTravelTime, WeaponUpgradePath.None);
        Vector3 selectedCenter = chosen.Aim.SurfacePoint;
        var light = Keep(new GameObject("Thin panel capture light", typeof(Light))).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        var camera = Keep(new GameObject("Native thin panel camera", typeof(Camera))).GetComponent<Camera>();
        camera.enabled = false;
        camera.scene = _scene;
        camera.cameraType = CameraType.Preview;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .07f, .09f);
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 50f;
        camera.fieldOfView = 40f;
        camera.transform.position = selectedCenter + new Vector3(5.5f, 5f, -7f);
        camera.transform.LookAt(selectedCenter);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        Directory.CreateDirectory(output);
        string name = selectedSurfaceReference ? "07-panel-selected-surface-reference" : "06-panel-predicted-contact";
        Render(camera, Path.Combine(output, name + ".png"));
        WriteSamples(Path.Combine(output, name + ".csv"), new[] { chosen });
        var visibleVertices = new List<Vector3>();
        foreach (MeshFilter filter in marker.GetComponentsInChildren<MeshFilter>())
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            foreach (int index in filter.sharedMesh.triangles.Distinct())
                visibleVertices.Add(filter.transform.TransformPoint(vertices[index]));
        }
        File.WriteAllText(Path.Combine(output, name + "-notes.txt"),
            "Native Camera.Render of the production marker and exact saved sandbox panel/floor dimensions translated 1000m in XZ, with original near-zero Y. " +
            (selectedSurfaceReference
                ? "Diagnostic reference: contact comes from the solver-selected surface, not the prediction. "
                : "Production path: contact comes from MortarTrajectory.TryPredictManualCollision. ") +
            "Both captures choose the same first invalid predicted contact, or the same center level-aim sample if no mismatch exists. " +
            "No UI overlays or image compositing. No scenes created, loaded or saved.\n" + Describe(chosen) + "\n" +
            FormattableString.Invariant($"marker_vertex_count={visibleVertices.Count}; marker_min_y={(visibleVertices.Count > 0 ? visibleVertices.Min(p => p.y) : float.NaN)}; marker_max_y={(visibleVertices.Count > 0 ? visibleVertices.Max(p => p.y) : float.NaN)}; panel_top={_anchor.y + .05f}; floor_top={_anchor.y - .01f}\n"));
    }

    private IEnumerable<Sample> Grid(float pitch)
    {
        foreach (float x in new[] { 0f, .025f, -.025f, 3f, -3f, 6f, -6f })
        foreach (float z in new[] { 0f, -2f, 2f, -4f, 4f })
            yield return ResolveSample(pitch, x, z);
    }

    private Sample ResolveSample(float pitch, float x, float z)
    {
        float range = MortarManualAim.EvaluateDistance(pitch, _tuning);
        Vector3 direction = Quaternion.AngleAxis(-pitch, Vector3.right) * Vector3.forward;
        var sample = new Sample { Pitch = pitch, X = x, Z = z };
        Vector3 origin = _anchor + new Vector3(x, 1.8f, 14f - range + z);
        sample.Resolved = MortarManualAim.TryResolve(origin, direction, Vector3.forward,
            _tuning, CollisionRadius, null, _hits, out sample.Aim);
        if (sample.Resolved)
        {
            sample.HasSelected = Physics.Raycast(sample.Aim.SurfacePoint + Vector3.up, Vector3.down,
                out sample.Selected, 2f, ~0, QueryTriggerInteraction.Ignore);
            sample.HasPredicted = MortarTrajectory.TryPredictManualCollision(sample.Aim.Origin, sample.Aim.Target,
                sample.Aim.ArcHeight, _tuning.MortarManualTravelTime, CollisionRadius, null, _hits,
                MortarUpgradePayload.None, out sample.Predicted, sample.Aim.SurfaceNormal);
        }
        return sample;
    }

    private void CreateSandboxPanel(float elevation)
    {
        _anchor = new Vector3(1000f, elevation, 1000f);
        Cube("Saved sandbox floor", new Vector3(0f, -.07f, 0f), new Vector3(52f, .12f, 52f), new Color(.20f, .25f, .28f));
        _panel = Cube("Saved sandbox Zone 1 panel", new Vector3(0f, .01f, 14f), new Vector3(14f, .08f, 12f),
            new Color(.38f, .24f, .49f)).GetComponent<BoxCollider>();
        Physics.SyncTransforms();
    }

    private GameObject Cube(string name, Vector3 localCenter, Vector3 scale, Color color)
    {
        var cube = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        cube.name = name;
        cube.transform.position = _anchor + localCenter;
        cube.transform.localScale = scale;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Assert.That(shader, Is.Not.Null);
        cube.GetComponent<Renderer>().sharedMaterial = Keep(new Material(shader) { color = color });
        return cube;
    }

    private string Describe(Sample sample) => FormattableString.Invariant(
        $"pitch={sample.Pitch}; movement=({sample.X},{sample.Z}); range={sample.Aim.Range}; resolved={sample.Resolved}; origin={sample.Aim.Origin - _anchor}; selected={sample.Selected.collider?.name ?? "none"} at {sample.Aim.SurfacePoint - _anchor}, normal={sample.Aim.SurfaceNormal}; target={sample.Aim.Target - _anchor}; predicted={sample.Predicted.collider?.name ?? "none"} at {sample.Predicted.point - _anchor}, normal={sample.Predicted.normal}");

    private void WriteSamples(string path, IEnumerable<Sample> samples)
    {
        var csv = new StringBuilder("pitch,movement_x,movement_z,resolved,range,origin_x,origin_y,origin_z,selected_collider,selected_x,selected_y,selected_z,selected_normal_x,selected_normal_y,selected_normal_z,target_x,target_y,target_z,predicted_collider,predicted_x,predicted_y,predicted_z,predicted_normal_x,predicted_normal_y,predicted_normal_z\n");
        foreach (Sample sample in samples)
        {
            Vector3 selected = sample.Aim.SurfacePoint - _anchor, predicted = sample.Predicted.point - _anchor;
            Vector3 origin = sample.Aim.Origin - _anchor, target = sample.Aim.Target - _anchor;
            Vector3 selectedNormal = sample.Aim.SurfaceNormal, predictedNormal = sample.Predicted.normal;
            csv.Append(FormattableString.Invariant($"{sample.Pitch},{sample.X},{sample.Z},{sample.Resolved},{sample.Aim.Range},{origin.x},{origin.y},{origin.z},{sample.Selected.collider?.name ?? "none"},{selected.x},{selected.y},{selected.z},{selectedNormal.x},{selectedNormal.y},{selectedNormal.z},{target.x},{target.y},{target.z},{sample.Predicted.collider?.name ?? "none"},{predicted.x},{predicted.y},{predicted.z},{predictedNormal.x},{predictedNormal.y},{predictedNormal.z}\n"));
        }
        File.WriteAllText(path, csv.ToString());
    }

    private T Keep<T>(T value) where T : Object
    {
        _objects.Add(value);
        if (value is GameObject go) SceneManager.MoveGameObjectToScene(go, _scene);
        return value;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

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
