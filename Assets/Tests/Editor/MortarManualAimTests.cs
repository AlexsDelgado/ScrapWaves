using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Native physics and authored-prefab captures in an unsaved, disposable scene.</summary>
public sealed class MortarManualAimTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset";
    private const string WeaponPath = "Assets/ScriptableObjects/WeaponSO/Mortar.asset";
    private static readonly Vector3 Anchor = new(1000f, 100f, 1000f);
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private readonly List<Object> _objects = new();
    private Scene _scene;
    private Scene _previous;
    private MortarTuning _tuning;
    private Material _groundMaterial;
    private Material _obstacleMaterial;
    private Vector3 Origin => Anchor + Vector3.up * 1.8f;

    private sealed class RecordingSink : IWeaponPresentationSink
    {
        public readonly List<WeaponPresentationContext> Cues = new();
        public void Emit(in WeaponPresentationContext context) => Cues.Add(context);
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    [SetUp]
    public void SetUp()
    {
        _previous = SceneManager.GetActiveScene();
        // Use the runner's scene; every fixture object is owned and placed far from gameplay.
        // Do not create, save or close scenes around an unsaved user/runner scene.
        _scene = _previous;
        _tuning = new MortarTuning();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
    }

    [TestCase(-90f, 5f)]
    [TestCase(-20f, 5f)]
    [TestCase(0f, 18f)]
    [TestCase(7.5f, 22f)]
    [TestCase(90f, 22f)]
    public void PitchMapping_PassesThroughDesignerPointsAndClamps(float pitch, float expected)
    {
        Assert.That(MortarManualAim.EvaluateDistance(pitch, _tuning), Is.EqualTo(expected).Within(.001f));
    }

    [Test]
    public void PitchMapping_IsContinuousAndMonotoneAcrossTheLevelJoin()
    {
        float previous = MortarManualAim.EvaluateDistance(-90f, _tuning);
        for (float pitch = -89.75f; pitch <= 90f; pitch += .25f)
        {
            float value = MortarManualAim.EvaluateDistance(pitch, _tuning);
            Assert.That(value, Is.InRange(5f, 22f));
            Assert.That(value, Is.GreaterThanOrEqualTo(previous - .0001f));
            Assert.That(value - previous, Is.LessThan(.5f), "Small camera motion must not create a range jump.");
            previous = value;
        }
        float left = (18f - MortarManualAim.EvaluateDistance(-.01f, _tuning)) / .01f;
        float right = (MortarManualAim.EvaluateDistance(.01f, _tuning) - 18f) / .01f;
        Assert.That(Mathf.Abs(left - right), Is.LessThan(.03f), "The control-point join must have a smooth derivative.");
    }

    [TestCase(-20f, 5f)]
    [TestCase(0f, 18f)]
    [TestCase(7.5f, 22f)]
    public void FlatGround_ResolvesSelectedHorizontalDistance(float pitch, float expected)
    {
        Flat();
        MortarManualAimSolution solution = Resolve(pitch);
        Assert.That(solution.Range, Is.EqualTo(expected).Within(.001f));
        Assert.That(solution.Origin, Is.EqualTo(Origin));
        Assert.That(solution.SurfacePoint.z - Origin.z, Is.EqualTo(expected).Within(.02f));
        Assert.That(solution.SurfacePoint.y - Anchor.y, Is.EqualTo(0f).Within(.02f));
        Assert.That(Vector3.Dot(solution.SurfaceNormal, Vector3.up), Is.GreaterThan(.999f));
        Assert.That(solution.ArcHeight, Is.GreaterThanOrEqualTo(_tuning.MortarArcHeight));
    }

    [TestCase(10f)]
    [TestCase(-10f)]
    public void SlopedGround_UsesSurfaceAtSelectedRange(float slopeDegrees)
    {
        Slope(slopeDegrees);
        MortarManualAimSolution solution = Resolve(0f);
        Assert.That(solution.SurfacePoint.z - Origin.z, Is.EqualTo(18f).Within(.025f));
        Assert.That(solution.SurfacePoint.y - Anchor.y,
            Is.EqualTo(Mathf.Tan(slopeDegrees * Mathf.Deg2Rad) * 18f).Within(.03f));
        Assert.That(Vector3.Angle(solution.SurfaceNormal, Vector3.up), Is.EqualTo(Mathf.Abs(slopeDegrees)).Within(.1f));
        AssertCollisionMatchesPrediction(solution, .55f, .04f);
    }

    [Test]
    public void Ledge_FindsLowerFloorAtSelectedRange()
    {
        Ledge();
        MortarManualAimSolution solution = Resolve(0f);
        Assert.That(solution.SurfacePoint.z - Origin.z, Is.EqualTo(18f).Within(.02f));
        Assert.That(solution.SurfacePoint.y - Anchor.y, Is.EqualTo(-5f).Within(.02f));
        AssertCollisionMatchesPrediction(solution, .55f, .04f);
    }

    [Test]
    public void Void_HasNoLandingTarget()
    {
        Assert.That(MortarManualAim.TryResolve(Origin, Vector3.forward, Vector3.forward,
            _tuning, .18f, null, _hits, out _), Is.False);
    }

    [Test]
    public void SkyAim_ClampsRangeAndUsesHorizontalFallbackAtZenith()
    {
        Flat();
        Assert.That(MortarManualAim.TryResolve(Origin, Vector3.up, Vector3.right,
            _tuning, .18f, null, _hits, out var solution), Is.True);
        Assert.That(solution.Range, Is.EqualTo(22f).Within(.001f));
        Assert.That(solution.SurfacePoint.x - Origin.x, Is.EqualTo(22f).Within(.02f));
        Assert.That(solution.SurfacePoint.z - Origin.z, Is.EqualTo(0f).Within(.02f));
    }

    [Test]
    public void Movement_ResolvesFromTheCurrentMuzzleOrigin()
    {
        Flat();
        MortarManualAimSolution before = Resolve(0f);
        Vector3 movement = new(3f, .5f, 4f);
        Assert.That(MortarManualAim.TryResolve(Origin + movement, Direction(0f), Vector3.forward,
            _tuning, .18f, null, _hits, out var after), Is.True);
        Assert.That(after.Origin, Is.EqualTo(Origin + movement));
        Assert.That(after.SurfacePoint.x - before.SurfacePoint.x, Is.EqualTo(movement.x).Within(.02f));
        Assert.That(after.SurfacePoint.z - before.SurfacePoint.z, Is.EqualTo(movement.z).Within(.02f));
        Assert.That(after.SurfacePoint.y, Is.EqualTo(before.SurfacePoint.y).Within(.02f));
    }

    [TestCase("ceiling")]
    [TestCase("near-wall")]
    public void InterveningMapObstacle_PreviewMatchesActualShellCollision(string obstacle)
    {
        Flat();
        GameObject blocker = Obstacle(obstacle);
        MortarManualAimSolution solution = Resolve(0f);
        Assert.That(Predict(solution, .55f, out RaycastHit collision), Is.True);
        Assert.That(collision.collider, Is.SameAs(blocker.GetComponent<Collider>()));
        Assert.That(collision.point.z, Is.LessThan(solution.SurfacePoint.z - 1f));
        AssertCollisionMatchesPrediction(solution, .55f, .04f);
        AssertCollisionMatchesPrediction(solution, .55f / 1.5f, .04f);
    }

    [Test]
    public void HeatChangesFlightTimeWithoutChangingDestinationOrCollision()
    {
        Flat();
        MortarManualAimSolution solution = Resolve(0f);
        float cold = _tuning.MortarManualTravelTime;
        float hot = cold / (1f + _tuning.MortarHeatManualSpeedBonus);
        Assert.That(hot, Is.LessThan(cold));
        Assert.That(Predict(solution, cold, out var coldHit), Is.True);
        Assert.That(Predict(solution, hot, out var hotHit), Is.True);
        Assert.That(Vector3.Distance(coldHit.point, hotHit.point), Is.LessThan(.01f));
        AssertCollisionMatchesPrediction(solution, cold, .04f);
        AssertCollisionMatchesPrediction(solution, hot, .04f);
    }

    [Test]
    public void MultiChargedShell_KeepsRepeatsAtThePredictedCollision()
    {
        Flat();
        MortarManualAimSolution solution = Resolve(0f);
        var sink = new RecordingSink();
        MortarShellImpact shell = Shell(solution, .55f, new MortarUpgradePayload(false, 0, 0f, 0f, 3, 1f), sink);
        Vector3 expected = shell.PredictedPresentationCollisionPoint;
        StepToImpact(shell, .55f, sink);
        Assert.That(sink.Cues.Count, Is.EqualTo(1));
        Assert.That(Vector3.Distance(sink.Cues[0].Position, expected), Is.LessThan(.04f));
        for (int i = 0; i < 2; i++)
        {
            Set(shell, "_repeatExplosionTimer", 0f);
            Invoke(shell, "Tick", 0f);
        }
        Assert.That(sink.Cues.Count, Is.EqualTo(3));
        foreach (var cue in sink.Cues)
            Assert.That(Vector3.Distance(cue.Position, expected), Is.LessThan(.04f));
        Assert.That(sink.Cues[2].Cue, Is.EqualTo(WeaponPresentationCue.MortarMultiChargedRepeat));
    }

    [Test]
    public void GrapeshotShell_KeepsSelectedTargetAndAirburstsBeforeLanding()
    {
        Flat();
        MortarManualAimSolution solution = Resolve(0f);
        var sink = new RecordingSink();
        MortarShellImpact shell = Shell(solution, .55f, new MortarUpgradePayload(true, 5, 70f, .5f, 1, 0f), sink);
        Assert.That(Get<Vector3>(shell, "_target"), Is.EqualTo(solution.Target));
        float burst = Get<float>(shell, "_grapeshotAirburstNormalizedTime");
        Assert.That(burst, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(MortarTrajectory.Evaluate(solution.Origin, solution.Target, solution.ArcHeight, burst).y,
            Is.GreaterThan(solution.SurfacePoint.y));
        Assert.That(shell.HasPredictedPresentationCollision, Is.False,
            "Grapeshot retains its airburst presentation instead of promising one parent-shell impact.");
    }

    [TestCase("01-down-20deg-5m", -20f, "flat")]
    [TestCase("02-level-0deg-18m", 0f, "flat")]
    [TestCase("03-up-7_5deg-22m", 7.5f, "flat")]
    [TestCase("04-uphill-18m", 0f, "uphill")]
    [TestCase("05-downhill-18m", 0f, "downhill")]
    [TestCase("06-ledge-lower-floor", 0f, "ledge")]
    [TestCase("07-ceiling-collision", 0f, "ceiling")]
    [TestCase("08-near-wall-collision", 0f, "near-wall")]
    public void CaptureNativeAuthoredPreview(string name, float pitch, string terrain)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Native render captures require a graphics device; run the batch editor without -nographics.");
        switch (terrain)
        {
            case "uphill": Slope(10f); break;
            case "downhill": Slope(-10f); break;
            case "ledge": Ledge(); break;
            default: Flat(); break;
        }
        if (terrain == "ceiling" || terrain == "near-wall") Obstacle(terrain);
        MortarManualAimSolution solution = Resolve(pitch);
        Assert.That(Predict(solution, .55f, out var hit), Is.True);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile, Is.Not.Null);
        var marker = Keep(Object.Instantiate(profile.Mortar.LandingIndicatorPrefab)).GetComponent<MortarLandingIndicatorVfx>();
        if (hit.normal.y >= .2f)
            marker.ConfigureGroundContact(hit.point, hit.normal, _tuning.MortarManualExplosionRadius, .55f, WeaponUpgradePath.None);
        else
            marker.Configure(hit.point + hit.normal * .035f, hit.normal, _tuning.MortarManualExplosionRadius, .55f, WeaponUpgradePath.None);
        var sink = new RecordingSink();
        MortarShellImpact shell = Shell(solution, .55f, MortarUpgradePayload.None, sink);
        // Advance the same Tick called by Update in controlled steps, before the first collision.
        float flightProgress = terrain == "near-wall" ? .018f : terrain == "ceiling" ? .045f : .5f;
        for (int i = 1; i <= 32; i++) Step(shell, .55f, flightProgress * i / 32f);
        Assert.That(shell, Is.Not.Null);
        Assert.That(sink.Cues, Is.Empty);
        // A small physical plinth identifies the actual launch origin; no UI or debug trajectory is built.
        Cube("Launch origin plinth", new Vector3(0f, .2f, 0f), new Vector3(.65f, .4f, .65f));
        var lightObject = Keep(new GameObject("Disposable capture light", typeof(Light)));
        var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        var cameraObject = Keep(new GameObject("Native mortar QA camera", typeof(Camera)));
        var camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false; camera.scene = _scene; camera.cameraType = CameraType.Preview;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .07f, .09f);
        camera.nearClipPlane = .05f; camera.farClipPlane = 120f; camera.fieldOfView = 50f;
        camera.transform.position = Anchor + new Vector3(24f, 21f, -10f);
        camera.transform.LookAt(Anchor + new Vector3(0f, 1.5f, 12f));
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        string output = Path.GetFullPath(".utmp/mortar-manual-aim");
        Directory.CreateDirectory(output);
        Render(camera, Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".csv"),
            "pitch_degrees,selected_range,surface_x,surface_y,surface_z,predicted_x,predicted_y,predicted_z,shell_x,shell_y,shell_z,travel_seconds\n" +
            FormattableString.Invariant($"{pitch},{solution.Range},{solution.SurfacePoint.x-Anchor.x},{solution.SurfacePoint.y-Anchor.y},{solution.SurfacePoint.z-Anchor.z},{hit.point.x-Anchor.x},{hit.point.y-Anchor.y},{hit.point.z-Anchor.z},{shell.transform.position.x-Anchor.x},{shell.transform.position.y-Anchor.y},{shell.transform.position.z-Anchor.z},0.55\n"));
        File.WriteAllText(Path.Combine(output, "capture-notes.txt"),
            "Native Unity Camera.Render PNG captures (1280x720) of the production authored shell and landing indicator in an unsaved disposable physics fixture. " +
            "The real MortarManualAim solver, MortarTrajectory prediction and MortarShellImpact.Tick (the body called by Update) advance the shot. " +
            "Camera placement is a QA overview, not the gameplay camera or a human playthrough. " +
            "The launch plinth is at horizontal zero; exact selected range and collision positions are recorded in each CSV. " +
            "Collision tests separately advance each shot to its actual impact at cold and hot speeds. No assets, scenes, user settings or saves are written by this fixture.\n");
    }

    private MortarManualAimSolution Resolve(float pitch)
    {
        Physics.SyncTransforms();
        Assert.That(MortarManualAim.TryResolve(Origin, Direction(pitch), Vector3.forward,
            _tuning, .18f, null, _hits, out var solution), Is.True);
        return solution;
    }

    private static Vector3 Direction(float pitch) => Quaternion.AngleAxis(-pitch, Vector3.right) * Vector3.forward;

    private bool Predict(MortarManualAimSolution solution, float travel, out RaycastHit hit) =>
        MortarTrajectory.TryPredictManualCollision(solution.Origin, solution.Target, solution.ArcHeight,
            travel, .18f, null, _hits, MortarUpgradePayload.None, out hit, solution.SurfaceNormal);

    private void AssertCollisionMatchesPrediction(MortarManualAimSolution solution, float travel, float tolerance)
    {
        var sink = new RecordingSink();
        MortarShellImpact shell = Shell(solution, travel, MortarUpgradePayload.None, sink);
        Assert.That(shell.HasPredictedPresentationCollision, Is.True);
        Vector3 expected = shell.PredictedPresentationCollisionPoint;
        StepToImpact(shell, travel, sink);
        Assert.That(sink.Cues.Count, Is.EqualTo(1), "The production shell must report one actual impact.");
        Assert.That(Vector3.Distance(sink.Cues[0].Position, expected), Is.LessThan(tolerance),
            "The authored landing indicator must match the collision consumed by the real shell.");
    }

    private void StepToImpact(MortarShellImpact shell, float travel, RecordingSink sink)
    {
        for (int i = 1; i <= 640 && shell != null && sink.Cues.Count == 0; i++) Step(shell, travel, i / 320f);
        Assert.That(sink.Cues.Count, Is.GreaterThan(0), "Shell did not collide within two normalized flight times.");
    }

    private static void Step(MortarShellImpact shell, float travel, float normalizedTime)
    {
        float elapsed = Get<float>(shell, "_elapsed");
        Invoke(shell, "Tick", Mathf.Max(0f, normalizedTime * travel - elapsed));
    }

    private MortarShellImpact Shell(MortarManualAimSolution solution, float travel, MortarUpgradePayload payload, RecordingSink sink)
    {
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath);
        Assert.That(profile, Is.Not.Null); Assert.That(data, Is.Not.Null);
        var shell = Keep(Object.Instantiate(profile.Mortar.ShellPrefab)).GetComponent<MortarShellImpact>();
        Set(shell, "_payload", payload);
        Set(shell, "_manualTargeting", true);
        Set(shell, "_manualLandingNormal", solution.SurfaceNormal);
        Set(shell, "_presentationSink", sink);
        Set(shell, "_hasFeedbackTemplate", true);
        var runtime = new WeaponInstance { Data = data, Level = payload.UseGrapeshot || payload.RepeatExplosionCount > 1 ? 6 : 1 };
        Set(shell, "_feedbackTemplate", new WeaponFeedbackContext(runtime, WeaponFeedbackMode.Manual, 0f,
            solution.Origin, solution.Target - solution.Origin));
        Invoke(shell, "Configure", solution.Origin, solution.Target, travel, solution.ArcHeight,
            1, _tuning.MortarManualExplosionRadius, 0f, 0f, .18f, null);
        return shell;
    }

    private void Flat() => Cube("Flat terrain", new Vector3(0f, -.25f, 10f), new Vector3(70f, .5f, 70f));

    private void Slope(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        GameObject slope = Cube("Sloped terrain", new Vector3(0f, Mathf.Tan(radians) * 14f - .25f / Mathf.Cos(radians), 14f), new Vector3(16f, .5f, 42f));
        slope.transform.rotation = Quaternion.Euler(-degrees, 0f, 0f);
        Physics.SyncTransforms();
    }

    private void Ledge()
    {
        Cube("Upper ledge", new Vector3(0f, -.5f, 1f), new Vector3(16f, 1f, 14f));
        Cube("Lower landing", new Vector3(0f, -5.25f, 23f), new Vector3(16f, .5f, 30f));
    }

    private GameObject Obstacle(string kind) => kind == "ceiling"
        ? Cube("Overhead obstacle", new Vector3(0f, 5f, 5f), new Vector3(7f, .4f, 6f), true)
        : Cube("Near wall", new Vector3(0f, 3f, 1.3f), new Vector3(6f, 6f, .3f), true);

    private GameObject Cube(string name, Vector3 localCenter, Vector3 scale, bool obstacle = false)
    {
        var result = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        result.name = name; result.transform.position = Anchor + localCenter; result.transform.localScale = scale;
        if (_groundMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            _groundMaterial = Keep(new Material(shader)); _groundMaterial.color = new Color(.21f, .25f, .27f);
            _obstacleMaterial = Keep(new Material(shader)); _obstacleMaterial.color = new Color(.46f, .30f, .18f);
        }
        result.GetComponent<Renderer>().sharedMaterial = obstacle ? _obstacleMaterial : _groundMaterial;
        Physics.SyncTransforms();
        return result;
    }

    private T Keep<T>(T obj) where T : Object
    {
        _objects.Add(obj);
        if (obj is GameObject go) SceneManager.MoveGameObjectToScene(go, _scene);
        return obj;
    }

    private static void Render(Camera camera, string path)
    {
        var target = new RenderTexture(1280, 720, 24); target.Create();
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.aspect = 1280f / 720f; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(5000), "Unity returned an implausibly empty capture.");
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
        }
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
