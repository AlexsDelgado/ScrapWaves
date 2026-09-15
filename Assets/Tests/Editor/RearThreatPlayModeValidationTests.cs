using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Runs the saved presenter/sensor in Play Mode without any combat or spawning systems.</summary>
public sealed class RearThreatPlayModeValidationTests
{
    private const string SetupKey = "RearThreatPlayModeValidation.SceneSetup";
    private List<Object> _cleanup = new();
    private List<GameObject> _enemies = new();

    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator RuntimeTransitionsAndSteadyStateAllocations()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previousSetup.Select(scene => new SavedScene
            {
                Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive
            }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // Allocate captured locals only after the domain reload. Iterator closure objects
        // created before EnterPlayMode are not restored by the test runner's serializer.
        yield return RunRuntimeScenario();
    }

    private IEnumerator RunRuntimeScenario()
    {
        _cleanup ??= new List<Object>();
        _enemies ??= new List<GameObject>();
        Assert.That(Application.isPlaying, Is.True, "Runtime scenario must start after EnterPlayMode completes.");
        Time.timeScale = 1f;
        Scene scene = SceneManager.GetActiveScene();
        Directory.CreateDirectory(RearThreatValidation.OutputDirectory);
        Material playerMaterial = Track(RearThreatValidation.CreateOpaqueMaterial(new Color(0.22f, 0.27f, 0.3f, 1f)));
        Material enemyMaterial = Track(RearThreatValidation.CreateOpaqueMaterial(new Color(0.4f, 0.18f, 0.1f, 1f)));
        GameObject player = Track(RearThreatValidation.CreatePrimitive(scene, PrimitiveType.Capsule,
            "Rear threat validation player", new Vector3(0f, 1f, 0f), new Vector3(0.65f, 1f, 0.65f), playerMaterial));
        Assert.That(player, Is.Not.Null, "The capsule factory must return a live player root.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RearThreatValidation.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Author the rear threat indicator prefab first.");
        GameObject indicator = Track(Object.Instantiate(prefab));
        Assert.That(indicator, Is.Not.Null, "Instantiating the saved indicator prefab must return its root.");
        RearThreatPresenter presenter = indicator.GetComponent<RearThreatPresenter>();
        Assert.That(presenter, Is.Not.Null, "The saved indicator root must retain its RearThreatPresenter in Play Mode.");
        Assert.That(presenter.Sensor, Is.Not.Null, "The saved presenter must retain its serialized sensor reference.");
        Assert.That(presenter.Fill, Is.Not.Null, "The saved presenter must retain its serialized fill reference.");
        Assert.That(presenter.Outline, Is.Not.Null, "The saved presenter must retain its serialized outline reference.");
        presenter.BindPlayer(player.transform);
        RearThreatSensor sensor = presenter.Sensor;
        Assert.That(sensor.Player, Is.SameAs(player.transform), "BindPlayer must configure the runtime sensor.");
        MethodInfo updateMethod = typeof(RearThreatPresenter).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(updateMethod, Is.Not.Null, "Performance validation requires the presenter's existing update method.");
        Action present = (Action)Delegate.CreateDelegate(typeof(Action), presenter,
            updateMethod);
        Camera camera = RearThreatValidation.CreateValidationCamera(scene);
        Track(camera.gameObject);
        float[] clusterAngles = { -72f, -36f, 0f, 36f, 72f };
        for (int i = 0; i < 100; i++)
        {
            GameObject enemy = Track(new GameObject("Non-combat validation enemy " + i));
            enemy.SetActive(false);
            enemy.transform.position = player.transform.position
                + RearThreatMesh.Direction(clusterAngles[i / 20] + (i % 20) * 0.2f) * (2f + (i % 20) * 0.3f);
            enemy.AddComponent<EnemyHealth>();
            enemy.AddComponent<EnemyRegistryMember>();
            if (i % 20 == 0)
            {
                GameObject body = RearThreatValidation.CreatePrimitive(scene, PrimitiveType.Cube,
                    "Reference enemy body", enemy.transform.position, new Vector3(0.45f, 0.6f, 0.45f), enemyMaterial);
                body.transform.SetParent(enemy.transform, true);
            }
            _enemies.Add(enemy);
        }
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(sensor.Snapshot.IsValidPlayer, Is.True);
        Assert.That(sensor.Snapshot.Count, Is.Zero);
        Capture(camera, "Idle");

        GameObject first = _enemies[40];
        first.transform.position = player.transform.position + Vector3.back * 8f;
        first.SetActive(true);
        yield return WaitForSeconds(0.4f);
        Assert.That(sensor.Snapshot.Count, Is.EqualTo(1));
        Assert.That(sensor.Snapshot.OverallUrgency, Is.EqualTo(0.25f).Within(0.001f));
        Capture(camera, "Approaching");
        first.transform.position = player.transform.position + Vector3.back * 2f;
        yield return WaitForSeconds(0.7f);
        Assert.That(sensor.Snapshot.OverallUrgency, Is.EqualTo(1f).Within(0.001f));
        Capture(camera, "CloseSingle");

        yield return ValidateAndCaptureMotion(camera, presenter, sensor, player.transform, first);

        foreach (GameObject enemy in _enemies) enemy.SetActive(true);
        // Warm both Unity's mesh wrappers and the sensor's caches before measuring managed allocations.
        for (int i = 0; i < 100; i++) yield return null;
        // Batch frames may advance far faster than real time; let all five smoothed spikes settle before capturing.
        yield return WaitForSeconds(0.7f);
        Assert.That(sensor.Snapshot.Count, Is.EqualTo(5));
        Assert.That(sensor.Snapshot.OverallUrgency, Is.EqualTo(1f).Within(0.001f));
        Assert.That(sensor.Snapshot.GetSpike(0).EnemyCount, Is.EqualTo(20));
        for (int i = 0; i < sensor.Snapshot.Count; i++)
            Assert.That(sensor.Snapshot.GetSpike(i).Urgency, Is.EqualTo(1f).Within(0.001f));
        float[] displayedLengths = (float[])typeof(RearThreatPresenter)
            .GetField("_lengths", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
        Assert.That(displayedLengths.Length, Is.EqualTo(5));
        for (int i = 0; i < displayedLengths.Length; i++)
            Assert.That(displayedLengths[i], Is.EqualTo(0.65f).Within(0.01f), "Every selected threat must be visible at its settled length before capture.");
        Capture(camera, "FiveClusters100Enemies");

        GameObject wall = Track(RearThreatValidation.CreatePrimitive(scene, PrimitiveType.Cube,
            "Opaque occlusion validation wall", new Vector3(0f, 1f, -1.6f), new Vector3(7f, 4f, 0.25f), playerMaterial));
        yield return null;
        Assert.That(sensor.Snapshot.Count, Is.EqualTo(5), "Terrain must not remove threats from the sensor.");
        Capture(camera, "ThroughWall");
        wall.SetActive(false);

        float sampleClock = Time.time;
        Action scan = () => { sampleClock += 0.2f; sensor.RefreshForPresentation(sampleClock); };
        for (int i = 0; i < 100; i++) scan();
        (long scanBytes, double scanMs) = Measure(scan, 1000);
        sensor.RefreshForPresentation(Time.time);
        for (int i = 0; i < 100; i++) present();
        (long presentationBytes, double presentationMs) = Measure(present, 1000);

        var geometry = new RearThreatMesh();
        Mesh fill = Track(new Mesh());
        Mesh outline = Track(new Mesh());
        float[] lengths = { 0.65f, 0.65f, 0.65f, 0.65f, 0.65f };
        Action rebuild = () => geometry.Update(fill, outline, 0.8f, 1.65f, 95f, 10f, 0.03f, 2.3f, clusterAngles, lengths);
        for (int i = 0; i < 100; i++) rebuild();
        (long geometryBytes, double geometryMs) = Measure(rebuild, 1000);
        File.WriteAllText(Path.Combine(RearThreatValidation.OutputDirectory, "RuntimePerformance.txt"),
            "Warm synchronous main-thread measurements, 100 registered live enemies, 1000 calls per measurement.\n"
            + "Sensor uses an advancing explicit clock, so every call performs a full scan. Presenter is measured separately within one frame.\n"
            + $"Sensor: {scanBytes} managed bytes, {scanMs:F3} ms total, {scanMs / 1000d:F6} ms/call.\n"
            + $"Presenter: {presentationBytes} managed bytes, {presentationMs:F3} ms total, {presentationMs / 1000d:F6} ms/call.\n"
            + $"Mesh rebuild: {geometryBytes} managed bytes, {geometryMs:F3} ms total, {geometryMs / 1000d:F6} ms/call.\n"
            + "Counters exclude asynchronous Editor work and GPU time; screenshots exercise actual rendered Play Mode frames.\n");
        Assert.That(scanBytes, Is.Zero, "Steady-state sensor scans must not allocate managed memory.");
        Assert.That(presentationBytes, Is.Zero, "Steady-state presentation must not allocate managed memory.");
        Assert.That(geometryBytes, Is.Zero, "Updating the reusable fill/outline buffers must not allocate managed memory.");

        Time.timeScale = 0f;
        yield return null;
        present();
        RearThreatSnapshot pausedSnapshot = sensor.Snapshot;
        Vector3[] pausedFill = presenter.Fill.sharedMesh.vertices;
        Vector3[] pausedOutline = presenter.Outline.sharedMesh.vertices;
        // Changing transform positions must not advance the sample clock during pause.
        for (int i = 0; i < 20; i++)
            _enemies[i].transform.position = player.transform.position + Vector3.forward * (20f + i);
        for (int i = 0; i < 10; i++) yield return null;
        present();
        Assert.That(sensor.Snapshot.Count, Is.EqualTo(pausedSnapshot.Count));
        Assert.That(sensor.Snapshot.OverallUrgency, Is.EqualTo(pausedSnapshot.OverallUrgency));
        for (int i = 0; i < pausedSnapshot.Count; i++)
        {
            Assert.That(sensor.Snapshot.GetSpike(i).TrackId, Is.EqualTo(pausedSnapshot.GetSpike(i).TrackId));
            Assert.That(sensor.Snapshot.GetSpike(i).WorldDirection, Is.EqualTo(pausedSnapshot.GetSpike(i).WorldDirection));
            Assert.That(sensor.Snapshot.GetSpike(i).Distance, Is.EqualTo(pausedSnapshot.GetSpike(i).Distance));
        }
        Assert.That(presenter.Fill.sharedMesh.vertices, Is.EqualTo(pausedFill), "Pause must stop visual interpolation.");
        Assert.That(presenter.Outline.sharedMesh.vertices, Is.EqualTo(pausedOutline));
        Time.timeScale = 1f;
        yield return WaitForSeconds(0.4f);
        Assert.That(sensor.Snapshot.Count, Is.EqualTo(4), "The relocated group must disappear after sampling resumes.");

        foreach (GameObject enemy in _enemies) enemy.SetActive(false);
        yield return WaitForSeconds(0.7f);
        Assert.That(sensor.Snapshot.Count, Is.Zero);
        Capture(camera, "DespawnedBackToIdle");
        player.transform.position += Vector3.up;
        player.transform.rotation = Quaternion.Euler(30f, 50f, 10f);
        yield return null;
        present();
        Assert.That(Vector3.Distance(presenter.transform.position, player.transform.position + Vector3.up * 0.25f), Is.LessThan(0.0001f),
            "The default hip offset must follow jumps above the player root without inheriting pitch or roll.");
        Assert.That(Vector3.Dot(presenter.transform.up, Vector3.up), Is.EqualTo(1f).Within(0.0001f),
            "Pitch and roll must not tilt the horizontal warning arc.");
        Assert.That(presenter.transform.forward.y, Is.EqualTo(0f).Within(0.0001f));
        player.SetActive(false);
        yield return null;
        present();
        Assert.That(presenter.Fill.GetComponent<Renderer>().enabled, Is.False);
        Assert.That(presenter.Outline.GetComponent<Renderer>().enabled, Is.False);
        player.SetActive(true);
        yield return null;
        present();
        Assert.That(presenter.Fill.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(presenter.Outline.GetComponent<Renderer>().enabled, Is.False, "Re-enabling the player must preserve the outline-free style.");
        Assert.That(sensor.Snapshot.IsValidPlayer, Is.True);
    }

    private IEnumerator ValidateAndCaptureMotion(Camera camera, RearThreatPresenter presenter,
        RearThreatSensor sensor, Transform player, GameObject movingEnemy)
    {
        const float fullLength = 0.65f;
        const int despawnFrame = 60;
        int[] captureFrames = { 0, 3, 6, 9, 12, 18, 24, 30, 36, 42, 48, 54, 60, 66, 78, 90, 108, 132 };
        string directory = Path.Combine(RearThreatValidation.OutputDirectory, "MotionSequence");
        Directory.CreateDirectory(directory);
        var manifest = new StringBuilder("frame,scaled_seconds,stage,target_bearing,displayed_bearing,spike_length,peak_radius,sensor_count,track_id,file\n");
        float previousCaptureDelta = Time.captureDeltaTime;
        float previousTimeScale = Time.timeScale;
        Vector3 previousEnemyPosition = movingEnemy.transform.position;
        bool previousEnemyActive = movingEnemy.activeSelf;
        float[] lengths = GetPresenterField<float[]>(presenter, "_lengths");
        float[] angles = GetPresenterField<float[]>(presenter, "_angles");
        Vector3[] directions = GetPresenterField<Vector3[]>(presenter, "_directions");
        int[] tracks = GetPresenterField<int[]>(presenter, "_trackIds");
        var vertices = new List<Vector3>(2048);
        int captures = 0;
        int nextCapture = 0;
        int movingTrack = 0;
        int growthStartFrame = -1;
        int growthHalfFrame = -1;
        int decayHalfFrame = -1;
        int intermediateGrowthFrames = 0;
        int intermediateBearingFrames = 0;
        int framesWithBearingMotion = 0;
        float maximumLengthStep = 0f;
        float maximumBearingStep = 0f;
        float maximumPeakStep = 0f;
        float priorLength = 0f;
        float priorBearing = -55f;
        float priorPeak = 0.86f;
        try
        {
            // PNG encoding must not change simulated frame duration or conceal intermediate motion.
            // All sampled meshes below come from normal runtime LateUpdate calls, never a synthetic spring step.
            Time.captureDeltaTime = 1f / 60f;
            Time.timeScale = 1f;
            movingEnemy.SetActive(false);
            yield return WaitForSeconds(1.6f);
            AssertCalmIdle(presenter, sensor, vertices);
            float startTime = Time.time;

            for (int frame = 0; frame <= 132; frame++)
            {
                float targetBearing = Mathf.Lerp(-55f, 55f, Mathf.Clamp01((frame - 12f) / 36f));
                movingEnemy.transform.position = player.position + RearThreatMesh.Direction(targetBearing) * 2f;
                if (frame == 1) movingEnemy.SetActive(true);
                if (frame == despawnFrame) movingEnemy.SetActive(false);

                // Resuming after a normal frame observes its completed LateUpdate; no WaitForEndOfFrame
                // is used because the Editor batch runner does not support that yield instruction.
                yield return null;
                RearThreatSnapshot snapshot = sensor.Snapshot;
                if (snapshot.Count > 0)
                {
                    Assert.That(snapshot.Count, Is.EqualTo(1));
                    if (movingTrack == 0) movingTrack = snapshot.GetSpike(0).TrackId;
                    Assert.That(snapshot.GetSpike(0).TrackId, Is.EqualTo(movingTrack),
                        "A continuously moving living enemy must retain its visual track.");
                }
                int slot = FindTrackSlot(tracks, movingTrack);
                float length = slot >= 0 ? lengths[slot] : 0f;
                float bearing = slot >= 0 ? angles[slot] : priorBearing;
                float peakRadius = ReadPeak(presenter.Fill.sharedMesh, vertices, out float peakBearing);
                maximumLengthStep = Mathf.Max(maximumLengthStep, Mathf.Abs(length - priorLength));
                maximumPeakStep = Mathf.Max(maximumPeakStep, Mathf.Abs(peakRadius - priorPeak));
                Assert.That(length, Is.InRange(0f, fullLength + 0.002f), "Damped growth and relaxation must remain bounded.");

                if (frame < despawnFrame)
                {
                    if (length > 0.002f && growthStartFrame < 0) growthStartFrame = frame;
                    if (length >= fullLength * 0.5f && growthHalfFrame < 0) growthHalfFrame = frame;
                    if (length > 0.02f && length < fullLength - 0.02f) intermediateGrowthFrames++;
                }
                else if (length <= fullLength * 0.5f && decayHalfFrame < 0) decayHalfFrame = frame;

                if (frame >= 13 && frame <= 59 && slot >= 0)
                {
                    float step = Mathf.Abs(Mathf.DeltaAngle(priorBearing, bearing));
                    maximumBearingStep = Mathf.Max(maximumBearingStep, step);
                    if (step > 0.05f) framesWithBearingMotion++;
                    if (bearing > -45f && bearing < 45f) intermediateBearingFrames++;
                    Assert.That(bearing, Is.InRange(-55.5f, 55.5f), "Following the sweep must not overshoot its endpoint.");
                    if (length > 0.2f)
                        Assert.That(Mathf.Abs(Mathf.DeltaAngle(peakBearing, bearing)), Is.LessThan(3f),
                            "The rendered outer edge must point toward the displayed threat bearing.");
                }
                if (frame == 18)
                    Assert.That(length, Is.GreaterThan(fullLength * 0.5f), "A close threat must grow visibly within 0.3 seconds.");
                if (frame == 59)
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(bearing, 55f)), Is.LessThan(6f),
                        "The spike must catch up to the enemy after the sweep stops.");

                string stage = frame == 0 ? "Idle" : frame <= 12 ? "Growing"
                    : frame < 49 ? "Following" : frame < despawnFrame ? "Holding" : "Relaxing";
                if (nextCapture < captureFrames.Length && frame == captureFrames[nextCapture])
                {
                    RecordMotionFrame(camera, directory, manifest, captures++, frame, Time.time - startTime,
                        stage, targetBearing, bearing, length, peakRadius, snapshot, movingTrack);
                    nextCapture++;
                }

                if (frame == 30)
                {
                    // Pause while the spring still has work to do, not only after it has settled.
                    Time.timeScale = 0f;
                    yield return null;
                    yield return null;
                    float[] pausedLengths = (float[])lengths.Clone();
                    float[] pausedAngles = (float[])angles.Clone();
                    Vector3[] pausedDirections = (Vector3[])directions.Clone();
                    int[] pausedTracks = (int[])tracks.Clone();
                    Vector3[] pausedVertices = presenter.Fill.sharedMesh.vertices;
                    RearThreatSnapshot pausedSnapshot = sensor.Snapshot;
                    Vector3 pausedEnemyPosition = movingEnemy.transform.position;
                    movingEnemy.transform.position = player.position + RearThreatMesh.Direction(80f) * 9f;
                    for (int pausedFrame = 0; pausedFrame < 6; pausedFrame++) yield return null;
                    Assert.That(lengths, Is.EqualTo(pausedLengths), "Pause must freeze a relaxing/growing spring mid-motion.");
                    Assert.That(angles, Is.EqualTo(pausedAngles));
                    Assert.That(directions, Is.EqualTo(pausedDirections));
                    Assert.That(tracks, Is.EqualTo(pausedTracks));
                    Assert.That(presenter.Fill.sharedMesh.vertices, Is.EqualTo(pausedVertices));
                    Assert.That(sensor.Snapshot.GetSpike(0).WorldDirection, Is.EqualTo(pausedSnapshot.GetSpike(0).WorldDirection));
                    int pausedSlot = FindTrackSlot(tracks, movingTrack);
                    RecordMotionFrame(camera, directory, manifest, captures++, frame, Time.time - startTime,
                        "Paused", 80f, angles[pausedSlot], lengths[pausedSlot],
                        ReadPeak(presenter.Fill.sharedMesh, vertices, out _), sensor.Snapshot, movingTrack);
                    movingEnemy.transform.position = pausedEnemyPosition;
                    Time.timeScale = 1f;
                }

                priorLength = length;
                priorBearing = bearing;
                priorPeak = peakRadius;
            }

            Assert.That(intermediateGrowthFrames, Is.GreaterThanOrEqualTo(3), "Growth must render intermediate lengths.");
            Assert.That(framesWithBearingMotion, Is.GreaterThan(15), "Bearing must flow between the 10 Hz sensor updates.");
            Assert.That(intermediateBearingFrames, Is.GreaterThan(8), "The moving threat must sweep through intermediate bearings.");
            Assert.That(maximumLengthStep, Is.LessThan(0.25f), "A single 60 Hz frame must not pop most of the spike into view.");
            Assert.That(maximumBearingStep, Is.LessThan(12f), "A sampled target change must not snap the displayed bearing.");
            Assert.That(maximumPeakStep, Is.LessThan(0.3f), "The actual mesh must deform continuously between frames.");
            Assert.That(growthHalfFrame, Is.GreaterThanOrEqualTo(growthStartFrame));
            Assert.That(decayHalfFrame - despawnFrame, Is.GreaterThan(growthHalfFrame - growthStartFrame),
                "A lost threat must relax more gently than a newly close threat grows.");

            // Leave enough real simulated time for the gentler spring to settle completely.
            yield return WaitForSeconds(0.4f);
            AssertCalmIdle(presenter, sensor, vertices);
            Vector3[] settledVertices = presenter.Fill.sharedMesh.vertices;
            for (int i = 0; i < 12; i++) yield return null;
            Assert.That(presenter.Fill.sharedMesh.vertices, Is.EqualTo(settledVertices),
                "Calm idle must remain stable, without residual spring jitter.");
            RecordMotionFrame(camera, directory, manifest, captures++, 156, Time.time - startTime,
                "SettledIdle", 55f, 0f, 0f, ReadPeak(presenter.Fill.sharedMesh, vertices, out _), sensor.Snapshot, 0);
            Assert.That(captures, Is.EqualTo(20));
            File.WriteAllText(Path.Combine(directory, "Frames.csv"), manifest.ToString());
            File.WriteAllText(Path.Combine(directory, "MotionValidation.txt"),
                string.Format(CultureInfo.InvariantCulture,
                    "Actual Play Mode frames, fixed simulated 60 Hz, 20 PNGs; captures and assertions are outside allocation measurements.\n"
                    + "Continuous growth frames: {0}; moving-bearing frames: {1}; intermediate-bearing frames: {2}.\n"
                    + "Maximum per-frame length change: {3:F5} m; bearing change: {4:F3} deg; rendered peak-radius change: {5:F5} m.\n"
                    + "Growth half-length: {6} frames after first visible growth; relaxation half-length: {7} frames after despawn.\n"
                    + "Mid-motion pause froze geometry and track state; despawn returned to stable 0.86 m outer / 0.8 m inner idle.\n",
                    intermediateGrowthFrames, framesWithBearingMotion, intermediateBearingFrames,
                    maximumLengthStep, maximumBearingStep, maximumPeakStep,
                    growthHalfFrame - growthStartFrame, decayHalfFrame - despawnFrame));
        }
        finally
        {
            Time.captureDeltaTime = previousCaptureDelta;
            Time.timeScale = previousTimeScale;
            if (movingEnemy != null)
            {
                movingEnemy.transform.position = previousEnemyPosition;
                movingEnemy.SetActive(previousEnemyActive);
            }
            // Preserve a partial evidence manifest if an assertion interrupts the sequence.
            File.WriteAllText(Path.Combine(directory, "Frames.csv"), manifest.ToString());
        }
        yield return WaitForSeconds(0.7f);
    }

    private static void AssertCalmIdle(RearThreatPresenter presenter, RearThreatSensor sensor, List<Vector3> vertices)
    {
        Assert.That(sensor.Snapshot.Count, Is.Zero);
        Assert.That(sensor.Snapshot.OverallUrgency, Is.Zero);
        foreach (float length in GetPresenterField<float[]>(presenter, "_lengths"))
            Assert.That(length, Is.Zero, "Settled idle must remove every spike.");
        Assert.That(ReadPeak(presenter.Fill.sharedMesh, vertices, out _), Is.EqualTo(0.86f).Within(0.001f));
        float minimumRadius = float.MaxValue;
        for (int i = 0; i < vertices.Count; i++) minimumRadius = Mathf.Min(minimumRadius, vertices[i].magnitude);
        Assert.That(minimumRadius, Is.EqualTo(0.8f).Within(0.001f));
    }

    private static float ReadPeak(Mesh mesh, List<Vector3> vertices, out float bearing)
    {
        mesh.GetVertices(vertices);
        float maximumRadiusSqr = -1f;
        Vector3 peak = Vector3.back;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 vertex = vertices[i];
            vertex.y = 0f;
            if (vertex.sqrMagnitude <= maximumRadiusSqr) continue;
            maximumRadiusSqr = vertex.sqrMagnitude;
            peak = vertex;
        }
        bearing = Vector3.SignedAngle(Vector3.back, peak, Vector3.up);
        return Mathf.Sqrt(Mathf.Max(0f, maximumRadiusSqr));
    }

    private static int FindTrackSlot(int[] tracks, int track)
    {
        if (track == 0) return -1;
        for (int i = 0; i < tracks.Length; i++) if (tracks[i] == track) return i;
        return -1;
    }

    private static T GetPresenterField<T>(RearThreatPresenter presenter, string name)
    {
        return (T)typeof(RearThreatPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
    }

    private static void RecordMotionFrame(Camera camera, string directory, StringBuilder manifest, int index,
        int frame, float elapsed, string stage, float targetBearing, float displayedBearing, float length,
        float peakRadius, RearThreatSnapshot snapshot, int track)
    {
        string file = index.ToString("00", CultureInfo.InvariantCulture) + "_" + stage + ".png";
        RearThreatValidation.WriteCameraPng(camera, Path.Combine(directory, file));
        manifest.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F4},{2},{3:F3},{4:F3},{5:F5},{6:F5},{7},{8},{9}\n",
            frame, elapsed, stage, targetBearing, displayedBearing, length, peakRadius, snapshot.Count, track, file);
    }

    [UnityTearDown]
    public IEnumerator CleanupAndExitPlayMode()
    {
        Time.timeScale = 1f;
        _enemies ??= new List<GameObject>();
        _cleanup ??= new List<Object>();
        foreach (GameObject enemy in _enemies)
            if (enemy != null) EnemyRegistry.Unregister(enemy.transform);
        _enemies.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.Destroy(_cleanup[i]);
        _cleanup.Clear();
        if (Application.isPlaying)
        {
            yield return null;
            yield return new ExitPlayMode();
        }
        string json = SessionState.GetString(SetupKey, string.Empty);
        SessionState.EraseString(SetupKey);
        if (string.IsNullOrEmpty(json)) yield break;
        SavedSetup saved = JsonUtility.FromJson<SavedSetup>(json);
        SceneSetup[] setup = saved.Scenes.Where(scene => !string.IsNullOrEmpty(scene.Path))
            .Select(scene => new SceneSetup { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray();
        if (setup.Length > 0 && setup.Any(scene => scene.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
    }

    private static IEnumerator WaitForSeconds(float duration)
    {
        float until = Time.time + duration;
        while (Time.time < until) yield return null;
    }

    private static (long Bytes, double Milliseconds) Measure(Action action, int iterations)
    {
        Stopwatch.GetTimestamp();
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long ticks = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) action();
        ticks = Stopwatch.GetTimestamp() - ticks;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (bytes, ticks * 1000d / Stopwatch.Frequency);
    }

    private static void Capture(Camera camera, string state) => RearThreatValidation.WriteCameraPng(camera,
        Path.Combine(RearThreatValidation.OutputDirectory, "PlayMode_" + state + ".png"));

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
}
