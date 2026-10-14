using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Opt-in, isolated Play Mode marker measurements. Never opens or saves an authored scene.</summary>
public sealed class MortarMarkerPerformanceTests
{
    internal const int WarmupFrames = 30;
    internal const int SampleFrames = 120;
    internal const float Radius = 2.9f;
    internal const float TravelTime = .55f;
    private const string PrefabPath = "Assets/GameFeel/Prefabs/Weapons/Mortar/GF_Mortar_ManualLandingIndicator.prefab";
    private const string OutputVariable = "SCRAPWAVES_MORTAR_PERFORMANCE_OUTPUT";
    private static readonly Vector3 Anchor = new(3000f, 300f, 3000f);

    [UnityTest]
    public IEnumerator MeasureAuthoredMarkerAtSixtyFps()
    {
        if (!Application.isBatchMode || string.IsNullOrWhiteSpace(OutputDirectory()))
            Assert.Ignore("Opt-in benchmark: use an isolated batch editor and SCRAPWAVES_MORTAR_PERFORMANCE_OUTPUT.");
        Assert.That(Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY"), Is.Not.Null.And.Not.Empty,
            "The isolated validation process must redirect its save directory before Play Mode.");
        // This test is intentionally unavailable in an interactive editor: no user scene is replaced.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // Construct closures and transient objects after the domain reload.
        yield return MeasureInPlayMode();
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator LeavePlayModeAfterFailure()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private static string OutputDirectory() => Environment.GetEnvironmentVariable(OutputVariable)
        ?? Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_OUTPUT");

    private static IEnumerator MeasureInPlayMode()
    {
        string output = Path.GetFullPath(OutputDirectory());
        string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.That(output.StartsWith(assets, StringComparison.OrdinalIgnoreCase), Is.False, "Diagnostics must stay outside Assets.");
        Directory.CreateDirectory(output);
        var report = new MortarMarkerPerformanceReport
        {
            StartedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion,
            OperatingSystem = SystemInfo.operatingSystem, Processor = SystemInfo.processorType,
            ProcessorCount = SystemInfo.processorCount, SystemMemoryMb = SystemInfo.systemMemorySize,
            GraphicsDevice = SystemInfo.graphicsDeviceName, GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            GraphicsMemoryMb = SystemInfo.graphicsMemorySize, StopwatchFrequency = Stopwatch.Frequency,
            BatchMode = Application.isBatchMode, RenderWidth = 1280, RenderHeight = 720,
            TargetFps = 60, WarmupFrames = WarmupFrames, SamplesPerScenario = SampleFrames,
            BlastRadius = Radius, TravelSeconds = TravelTime, Prefab = PrefabPath,
            QualityLevel = QualitySettings.GetQualityLevel(), OriginalVSyncCount = QualitySettings.vSyncCount,
            OriginalTargetFrameRate = Application.targetFrameRate,
            Limitations = "Editor Play Mode, one production authored marker in controlled terrain fixtures. " +
                "Configure CPU/GC measure only the direct ConfigureGroundContact call on the main thread after warm-up. " +
                "Frame intervals separately include editor, test runner, rendering, scheduling and frame pacing; they are not player-build FPS. " +
                "The 1280x720 enabled camera renders to a RenderTexture without readback. CPU counters cannot establish GPU cost. " +
                "Ground-contact acquisition, sample storage and verification are outside the timed call. Editor-only query counters are identical between paired runs. " +
                "A production-sized radius of 2.9m is measured; many simultaneous markers or larger upgrade footprints require separate scaling tests."
        };
        var owned = new List<Object>();
        var root = new GameObject("Disposable mortar marker performance fixture"); owned.Add(root);
        int oldFps = Application.targetFrameRate, oldVSync = QualitySettings.vSyncCount;
        bool oldBackground = Application.runInBackground;
        float oldTimeScale = Time.timeScale;
        RenderTexture target = null;
        MortarMarkerPerformanceSampler sampler = null;
        try
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Run without -nographics so the separate editor/render frame intervals include native rendering.");
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            Application.runInBackground = true; Time.timeScale = 1f;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit"); Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { color = new Color(.22f, .27f, .29f) }; owned.Add(material);
            var light = new GameObject("Performance fixture light", typeof(Light)); light.transform.SetParent(root.transform);
            light.GetComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var cameraObject = new GameObject("Performance fixture camera", typeof(Camera)); cameraObject.transform.SetParent(root.transform);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.transform.position = Anchor + new Vector3(10f, 10f, -12f);
            camera.transform.LookAt(Anchor); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .08f, .10f); camera.farClipPlane = 80f;
            target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
            // A collider-only owner exercises ignored-root filtering without gameplay or save singletons.
            GameObject owner = GameObject.CreatePrimitive(PrimitiveType.Capsule); owner.name = "Ignored player collider";
            owner.transform.SetParent(root.transform); owner.transform.position = Anchor + Vector3.up;
            owner.layer = 2; owner.GetComponent<Renderer>().enabled = false;
            sampler = root.AddComponent<MortarMarkerPerformanceSampler>();
            var observer = root.AddComponent<MortarMarkerPerformanceFrameObserver>(); observer.Sampler = sampler;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); Assert.That(prefab, Is.Not.Null);
            foreach (string terrain in new[] { "flat", "uneven", "ledge" })
            {
                GameObject ground = MakeTerrain(terrain, material, owned); ground.transform.SetParent(root.transform);
                Physics.SyncTransforms();
                foreach (string motion in new[] { "render-only", "stationary", "moving-aim" })
                {
                    GameObject markerObject = Object.Instantiate(prefab, root.transform);
                    var marker = markerObject.GetComponent<MortarLandingIndicatorVfx>(); Assert.That(marker, Is.Not.Null);
                    markerObject.SetActive(motion != "render-only");
                    // All buffers/delegates are allocated before warming and observing any samples.
                    var scenario = new MortarMarkerPerformanceScenario
                    {
                        Terrain = terrain, Motion = motion, Samples = new MortarMarkerPerformanceSample[SampleFrames]
                    };
                    sampler.Begin(marker, owner.transform, Anchor, scenario);
                    double deadline = Time.realtimeSinceStartupAsDouble + 90d;
                    while (!sampler.Complete && sampler.Error == null)
                    {
                        if (Time.realtimeSinceStartupAsDouble > deadline)
                        {
                            sampler.Error = "The scenario did not complete within 90 seconds.";
                            break;
                        }
                        yield return null;
                    }
                    sampler.Stop();
                    scenario.CompletedSamples = sampler.CompletedSamples;
                    scenario.Error = sampler.Error;
                    scenario.RuntimeMeshVertices = marker.GetComponentsInChildren<MeshFilter>(true)
                        .Where(filter => filter.sharedMesh != null).Sum(filter => filter.sharedMesh.vertexCount);
                    scenario.RuntimeMeshIndices = marker.GetComponentsInChildren<MeshFilter>(true)
                        .Where(filter => filter.sharedMesh != null).Sum(filter => (long)filter.sharedMesh.GetIndexCount(0));
                    scenario.Summarize(); report.Scenarios.Add(scenario);
                    WriteReport(report, output);
                    markerObject.SetActive(false); Object.Destroy(markerObject);
                    Assert.That(scenario.Error, Is.Null, terrain + "/" + motion);
                    Assert.That(scenario.CompletedSamples, Is.EqualTo(SampleFrames));
                    Assert.That(scenario.DuplicateRefreshFrames, Is.Zero,
                        "Configure and marker LateUpdate must not conform twice in the same frame.");
                    Assert.That(scenario.MissingRefreshFrames, Is.Zero,
                        "Every active marker sample must traverse the production conformer once.");
                    yield return null;
                }
                Object.Destroy(ground); yield return null;
            }
            report.CompletedUtc = DateTime.UtcNow.ToString("O");
            WriteReport(report, output);
            UnityEngine.Debug.Log("MORTAR_MARKER_PERFORMANCE_COMPLETE " + output);
        }
        finally
        {
            if (sampler != null) sampler.Stop();
            Application.targetFrameRate = oldFps; QualitySettings.vSyncCount = oldVSync;
            Application.runInBackground = oldBackground; Time.timeScale = oldTimeScale;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.Destroy(owned[i]);
            if (target != null) { target.Release(); Object.Destroy(target); }
        }
    }

    private static GameObject MakeTerrain(string name, Material material, List<Object> owned)
    {
        var root = new GameObject("Performance terrain " + name);
        void Box(Vector3 local, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(root.transform);
            go.transform.position = Anchor + local; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        if (name == "flat") Box(new Vector3(0f, -.25f, 0f), new Vector3(24f, .5f, 24f));
        else if (name == "ledge")
        {
            Box(new Vector3(-6f, -.25f, 0f), new Vector3(12f, .5f, 24f));
            Box(new Vector3(6f, .15f, 0f), new Vector3(12f, 1.3f, 24f));
        }
        else
        {
            const int divisions = 48;
            var vertices = new Vector3[(divisions + 1) * (divisions + 1)];
            var indices = new int[divisions * divisions * 6];
            for (int z = 0; z <= divisions; z++) for (int x = 0; x <= divisions; x++)
            {
                float px = -12f + x * .5f, pz = -12f + z * .5f;
                vertices[z * (divisions + 1) + x] = new Vector3(px,
                    .22f * Mathf.Sin(px * .9f) + .13f * Mathf.Sin(pz * 1.4f), pz);
            }
            int at = 0;
            for (int z = 0; z < divisions; z++) for (int x = 0; x < divisions; x++)
            {
                int a = z * (divisions + 1) + x, b = a + divisions + 1;
                indices[at++] = a; indices[at++] = b; indices[at++] = a + 1;
                indices[at++] = a + 1; indices[at++] = b; indices[at++] = b + 1;
            }
            var mesh = new Mesh { name = "Disposable uneven performance terrain", vertices = vertices, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            root.transform.position = Anchor;
            root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = material;
            root.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        return root;
    }

    private static void WriteReport(MortarMarkerPerformanceReport report, string output)
    {
        File.WriteAllText(Path.Combine(output, "mortar-marker-performance.json"), JsonUtility.ToJson(report, true));
        var csv = new StringBuilder("terrain,motion,sample,unity_frame,configure_ms,configure_gc_bytes,frame_interval_ms,conforms,raycasts,raycast_fallbacks,conforms_after_configure,raycasts_after_configure\n");
        foreach (var scenario in report.Scenarios)
        for (int i = 0; i < scenario.CompletedSamples; i++)
        {
            var sample = scenario.Samples[i];
            csv.AppendFormat(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4:F6},{5},{6:F6},{7},{8},{9},{10},{11}\n", scenario.Terrain, scenario.Motion,
                i, sample.UnityFrame, sample.ConfigureMilliseconds, sample.ConfigureAllocatedBytes,
                sample.FrameIntervalMilliseconds, sample.Conforms, sample.Raycasts, sample.RaycastFallbacks,
                sample.ConformsAfterConfigure, sample.RaycastsAfterConfigure);
        }
        File.WriteAllText(Path.Combine(output, "mortar-marker-performance-samples.csv"), csv.ToString());
    }
}

[Serializable]
public sealed class MortarMarkerPerformanceReport
{
    public string StartedUtc, CompletedUtc, UnityVersion, OperatingSystem, Processor, GraphicsDevice, GraphicsApi, Prefab, Limitations;
    public int ProcessorCount, SystemMemoryMb, GraphicsMemoryMb, RenderWidth, RenderHeight, TargetFps, WarmupFrames, SamplesPerScenario;
    public int QualityLevel, OriginalVSyncCount, OriginalTargetFrameRate;
    public long StopwatchFrequency;
    public bool BatchMode;
    public float BlastRadius, TravelSeconds;
    public List<MortarMarkerPerformanceScenario> Scenarios = new();
}

[Serializable]
public sealed class MortarMarkerPerformanceScenario
{
    public string Terrain, Motion, Error;
    public int CompletedSamples, DuplicateRefreshFrames, MissingRefreshFrames, RuntimeMeshVertices;
    public long RuntimeMeshIndices, TotalConforms, TotalRaycasts, TotalRaycastFallbacks;
    public double P95ConfigureFractionOf60FpsBudget;
    public MortarMarkerPerformanceDistribution ConfigureMilliseconds, ConfigureAllocatedBytes, FrameIntervalMilliseconds, RaycastsPerFrame;
    public MortarMarkerPerformanceSample[] Samples;

    public void Summarize()
    {
        var captured = Samples.Take(CompletedSamples).ToArray();
        ConfigureMilliseconds = MortarMarkerPerformanceDistribution.From(captured.Select(s => s.ConfigureMilliseconds));
        ConfigureAllocatedBytes = MortarMarkerPerformanceDistribution.From(captured.Select(s => (double)s.ConfigureAllocatedBytes));
        FrameIntervalMilliseconds = MortarMarkerPerformanceDistribution.From(captured.Select(s => s.FrameIntervalMilliseconds));
        RaycastsPerFrame = MortarMarkerPerformanceDistribution.From(captured.Select(s => (double)s.Raycasts));
        TotalConforms = captured.Sum(s => s.Conforms); TotalRaycasts = captured.Sum(s => s.Raycasts);
        TotalRaycastFallbacks = captured.Sum(s => s.RaycastFallbacks);
        DuplicateRefreshFrames = captured.Count(s => s.Conforms > 1 || s.ConformsAfterConfigure > 0);
        MissingRefreshFrames = Motion == "render-only" ? 0 : captured.Count(s => s.Conforms != 1);
        P95ConfigureFractionOf60FpsBudget = ConfigureMilliseconds.P95 / (1000d / 60d);
    }
}

[Serializable]
public sealed class MortarMarkerPerformanceDistribution
{
    public double Minimum, Median, P95, P99, Maximum, Mean, Total;
    public static MortarMarkerPerformanceDistribution From(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return new MortarMarkerPerformanceDistribution();
        double Percentile(double fraction) => sorted[Mathf.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new MortarMarkerPerformanceDistribution
        {
            Minimum = sorted[0], Median = Percentile(.5), P95 = Percentile(.95), P99 = Percentile(.99),
            Maximum = sorted[sorted.Length - 1], Mean = sorted.Average(), Total = sorted.Sum()
        };
    }
}

[Serializable]
public struct MortarMarkerPerformanceSample
{
    public int UnityFrame;
    public double ConfigureMilliseconds, FrameIntervalMilliseconds;
    public long ConfigureAllocatedBytes, Conforms, Raycasts, RaycastFallbacks, ConformsAfterConfigure, RaycastsAfterConfigure;
}

// Mirrors ReticleHud's execution order and calls the public production API exactly once per frame.
[DefaultExecutionOrder(100)]
public sealed class MortarMarkerPerformanceSampler : MonoBehaviour
{
    private Action<Vector3, Vector3, float, float, WeaponUpgradePath, Transform> _configure;
    private MortarLandingIndicatorVfx _marker;
    private Transform _owner;
    private Vector3 _anchor;
    private MortarMarkerPerformanceScenario _scenario;
    private int _frame, _pendingIndex = -1;
    private long _lastTimestamp, _beforeConforms, _beforeRays, _beforeFallbacks, _afterConforms, _afterRays;
    private bool _running;
    public bool Complete { get; private set; }
    public int CompletedSamples { get; private set; }
    public string Error;

    public void Begin(MortarLandingIndicatorVfx marker, Transform owner, Vector3 anchor, MortarMarkerPerformanceScenario scenario)
    {
        _marker = marker; _owner = owner; _anchor = anchor; _scenario = scenario;
        _configure = marker.ConfigureGroundContact;
        _frame = 0; _pendingIndex = -1; _lastTimestamp = 0; CompletedSamples = 0; Error = null; Complete = false;
        _running = true;
    }

    public void Stop() => _running = false;

    private void LateUpdate()
    {
        if (!_running || Complete || Error != null) return;
        _pendingIndex = -1;
        long frameTimestamp = Stopwatch.GetTimestamp();
        double interval = _lastTimestamp == 0 ? 0d : (frameTimestamp - _lastTimestamp) * 1000d / Stopwatch.Frequency;
        _lastTimestamp = frameTimestamp;
        bool moving = _scenario.Motion == "moving-aim";
        Vector3 offset = moving ? new Vector3(Mathf.Sin(_frame * .06f) * 1.5f, 0f, Mathf.Cos(_frame * .041f) * 1.2f) : new Vector3(-.2f, 0f, 0f);
        if (!Physics.Raycast(_anchor + offset + Vector3.up * 5f, Vector3.down, out var contact, 10f, 1, QueryTriggerInteraction.Ignore))
        {
            Error = "Fixture ground acquisition failed before the measured marker call.";
            return;
        }
        _beforeConforms = _marker.GroundConformCount; _beforeRays = _marker.GroundRaycastCount;
        _beforeFallbacks = _marker.GroundRaycastFallbackCount;
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long begin = Stopwatch.GetTimestamp();
        if (_scenario.Motion != "render-only")
            _configure(contact.point, contact.normal, MortarMarkerPerformanceTests.Radius,
                MortarMarkerPerformanceTests.TravelTime, WeaponUpgradePath.None, _owner);
        long finish = Stopwatch.GetTimestamp();
        long afterBytes = GC.GetAllocatedBytesForCurrentThread();
        _afterConforms = _marker.GroundConformCount; _afterRays = _marker.GroundRaycastCount;
        int index = _frame - MortarMarkerPerformanceTests.WarmupFrames;
        if (index >= 0 && index < _scenario.Samples.Length)
        {
            _pendingIndex = index;
            _scenario.Samples[index] = new MortarMarkerPerformanceSample
            {
                UnityFrame = Time.frameCount, ConfigureMilliseconds = (finish - begin) * 1000d / Stopwatch.Frequency,
                ConfigureAllocatedBytes = afterBytes - beforeBytes, FrameIntervalMilliseconds = interval
            };
        }
        _frame++;
    }

    public void ObserveAfterMarkerLateUpdate()
    {
        if (!_running || _pendingIndex < 0 || Error != null) return;
        ref var sample = ref _scenario.Samples[_pendingIndex];
        if (sample.UnityFrame != Time.frameCount) { Error = "Performance observer missed its sampler frame."; return; }
        sample.Conforms = _marker.GroundConformCount - _beforeConforms;
        sample.Raycasts = _marker.GroundRaycastCount - _beforeRays;
        sample.RaycastFallbacks = _marker.GroundRaycastFallbackCount - _beforeFallbacks;
        sample.ConformsAfterConfigure = _marker.GroundConformCount - _afterConforms;
        sample.RaycastsAfterConfigure = _marker.GroundRaycastCount - _afterRays;
        CompletedSamples = _pendingIndex + 1;
        _pendingIndex = -1;
        if (CompletedSamples == _scenario.Samples.Length) Complete = true;
    }
}

[DefaultExecutionOrder(200)]
public sealed class MortarMarkerPerformanceFrameObserver : MonoBehaviour
{
    public MortarMarkerPerformanceSampler Sampler;
    private void LateUpdate() => Sampler?.ObserveAfterMarkerLateUpdate();
}
