#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Actual Play Mode evidence in an unsaved scene. Run after Apply Emergence Authoring.
/// Batch invocation: -batchmode -executeMethod EnemyEmergenceCapture.Run -logFile ...
/// Do not pass -quit (this asynchronous method quits after capture) or -nographics.
/// </summary>
[InitializeOnLoad]
public static class EnemyEmergenceCapture
{
    private const string SessionKey = "ScrapWaves.EmergenceCapture";
    private const int Width = 1600, Height = 1000, FrameRate = 30;
    private static readonly string Output = Path.GetFullPath(".utmp/emergence-validation");
    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/Chaser.prefab", "Assets/Prefabs/EnemyPro.prefab",
        "Assets/Prefabs/Slime (variant).prefab", "Assets/Prefabs/Drone.prefab",
        "Assets/Prefabs/Drone (variant).prefab", "Assets/Prefabs/Drone_Elite.prefab"
    };
    private static readonly string[] Labels = { "CHASER", "SLIME", "HELLFIRE", "DRONE", "BOMBER", "DRONE ELITE" };
    private static readonly Vector3[] Positions =
    {
        new Vector3(-13f, 0f, -8f), new Vector3(0f, 0f, -8f), new Vector3(13f, 0f, -8f),
        new Vector3(-13f, 0f, 10f), new Vector3(0f, 0f, 10f), new Vector3(13f, 0f, 10f)
    };

    [Serializable] private sealed class SavedScene
    {
        public string path;
        public bool isLoaded, isActive;
    }
    [Serializable] private sealed class SavedScenes { public SavedScene[] scenes; }
    [Serializable] private sealed class LeaseEvidence
    {
        public string prefab;
        public int lease, instanceId;
        public float minimum, maximum, sampledDuration, completionTime;
        public Vector3 rootAtStart, rootAtCompletion;
        public bool invulnerable, dotImmune, collisionSuppressed, rootHeld, exactRestPose, collisionRestored, samePooledInstance;
    }
    [Serializable] private sealed class CaptureReport
    {
        public string unityVersion;
        public string mode = "Actual Unity Play Mode; production prefabs, real pool/placement and Update; no player target";
        public int width = Width, height = Height, frameRate = FrameRate, frames;
        public float simulatedSeconds;
        public bool passed;
        public List<LeaseEvidence> leases = new List<LeaseEvidence>();
        public List<string> failures = new List<string>();
        public List<float> frameTimes = new List<float>();
        public List<string> stills = new List<string>();
    }
    private sealed class Actor
    {
        public GameObject prefab, instance;
        public EnemyEmergence emergence;
        public Text label;
        public Vector3 restPosition, restScale;
        public Quaternion restRotation;
        public bool[] colliderDefaults;
        public int firstInstanceId;
        public LeaseEvidence evidence;
    }

    private static readonly List<Actor> Actors = new List<Actor>();
    private static CaptureReport report;
    private static Camera camera;
    private static RenderTexture target;
    private static Texture2D readback;
    private static Text heading;
    private static bool started, finishing, released, reused;
    private static int previousFrame = -1;
    private static float startTime, cycleDuration, reuseTime;
    private static double wallDeadline;
    private static int nextStill;
    private struct CaptureAfterLateUpdate { }
    private static PlayerLoopSystem originalPlayerLoop;
    private static bool playerLoopInstalled;

    static EnemyEmergenceCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += ResumeAfterReload;
    }

    [MenuItem("Tools/ScrapWaves/Enemies/Capture Emergence Validation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start emergence capture outside Play Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save or discard the current scene changes before capture.");

        Directory.CreateDirectory(Path.Combine(Output, "frames"));
        // Only remove prior generated frames from this exact output folder.
        foreach (string path in Directory.GetFiles(Path.Combine(Output, "frames"), "frame-*.png")) File.Delete(path);
        SessionState.SetString(SessionKey + ".scenes", JsonUtility.ToJson(new SavedScenes
        {
            scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene
                { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray()
        }));
        SessionState.SetFloat(SessionKey + ".timeScale", Time.timeScale);
        SessionState.SetInt(SessionKey + ".captureFramerate", Time.captureFramerate);
        SessionState.SetBool(SessionKey + ".active", true);
        SessionState.SetInt(SessionKey + ".exitCode", 1);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(SessionKey + ".active", false)) return;
        if (EditorApplication.isPlaying) StartRuntime();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(SessionKey + ".active", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) StartRuntime();
        if (change == PlayModeStateChange.ExitingPlayMode) RestorePlayerLoop();
        if (change == PlayModeStateChange.EnteredEditMode) RestoreEditor();
    }

    private static void StartRuntime()
    {
        if (started) return;
        started = true;
        report = new CaptureReport { unityVersion = Application.unityVersion };
        wallDeadline = EditorApplication.timeSinceStartup + 300d;
        try
        {
            Actors.Clear();
            finishing = released = reused = false;
            nextStill = 0;
            previousFrame = -1;
            cycleDuration = 0f;
            Time.timeScale = 1f;
            Time.captureFramerate = FrameRate;
            UnityEngine.Random.InitState(186);
            BuildStage();
            EnemyPoolRegistry.EnsureExists();
            for (int i = 0; i < PrefabPaths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPaths[i]);
                Require(prefab != null, "Missing capture prefab: " + PrefabPaths[i]);
                EnemyEmergence authored = prefab.GetComponent<EnemyEmergence>();
                Require(authored != null && authored.VisualPivot != null, "Run emergence authoring first: " + PrefabPaths[i]);
                Actor actor = new Actor
                {
                    prefab = prefab,
                    restPosition = authored.VisualPivot.localPosition,
                    restRotation = authored.VisualPivot.localRotation,
                    restScale = authored.VisualPivot.localScale,
                    colliderDefaults = prefab.GetComponentsInChildren<Collider>(true).Select(c => c.enabled).ToArray(),
                    label = AddLabel(Labels[i], 21, new Vector2(i % 3 == 0 ? 0.23f : i % 3 == 1 ? 0.5f : 0.77f, i < 3 ? 0.19f : 0.60f))
                };
                Actors.Add(actor);
                EnemyPoolRegistry.Instance.RegisterPrefab(new EnemyPoolRegistry.Entry { Prefab = prefab, InitialSize = 1, MaxSize = 1 });
                cycleDuration = Mathf.Max(cycleDuration, authored.MaximumDuration + 1f);
            }
            cycleDuration = Mathf.Max(2.3f, cycleDuration);
            SpawnLease(1);
            startTime = Time.time;
            InstallFrameObserver();
            EditorApplication.update += Watchdog;
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void BuildStage()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.70f, 0.78f);
        RenderSettings.fog = false;
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Capture ground - selected surface y=0";
        floor.transform.position = new Vector3(0f, -1.5f, 1f);
        floor.transform.localScale = new Vector3(50f, 3f, 38f);
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.12f, 0.19f, 0.23f));
        floor.GetComponent<Renderer>().sharedMaterial = material;
        Light key = new GameObject("Capture key light").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.7f;
        key.color = new Color(1f, 0.94f, 0.83f);
        key.transform.rotation = Quaternion.Euler(46f, -25f, 0f);
        key.shadows = LightShadows.Soft;
        camera = new GameObject("Emergence evidence camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 27f, -43f);
        camera.transform.LookAt(new Vector3(0f, 2f, 1f));
        camera.orthographic = true;
        camera.orthographicSize = 17f;
        camera.aspect = (float)Width / Height;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 150f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.06f, 0.09f);
        camera.allowHDR = false;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        camera.targetTexture = target;
        readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        GameObject canvasObject = new GameObject("Capture labels", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.5f;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        heading = AddLabel("SCRAPWAVES / ENEMY EMERGENCE", 29, new Vector2(0.5f, 0.94f));
        AddLabel("Actual Unity Play Mode  /  authored prefab ranges  /  no player target", 18, new Vector2(0.5f, 0.895f));
        AddLabel("Grounded families", 20, new Vector2(0.5f, 0.11f));
        Physics.SyncTransforms();
    }

    private static Text AddLabel(string text, int size, Vector2 anchor)
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(canvas.transform, false);
        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.88f, 0.95f, 1f);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = new Vector2(size > 24 ? 1400f : 560f, 82f);
        return label;
    }

    private static void SpawnLease(int lease)
    {
        for (int i = 0; i < Actors.Count; i++)
        {
            Actor actor = Actors[i];
            bool placed = OrbitalSpawnPlacement.TrySpawnGrounded(actor.prefab, Positions[i], 1, 1, 1,
                20f, 80f, 0f, 0.02f, 4, 0.2f, 0.2f, Positions[i] + Vector3.back * 20f, out GameObject instance, out _);
            Require(placed && instance != null, "Placement failed: " + PrefabPaths[i]);
            actor.instance = instance;
            actor.emergence = instance.GetComponent<EnemyEmergence>();
            EnemyHealth health = instance.GetComponent<EnemyHealth>();
            int hp = health.CurrentHealth;
            bool direct = health.ApplyDamage(2);
            bool dot = health.ApplyDotDamage(2);
            actor.evidence = new LeaseEvidence
            {
                prefab = PrefabPaths[i], lease = lease, instanceId = instance.GetInstanceID(),
                minimum = actor.emergence.MinimumDuration, maximum = actor.emergence.MaximumDuration,
                sampledDuration = actor.emergence.SampledDuration, rootAtStart = instance.transform.position,
                rootHeld = true, invulnerable = !direct && health.CurrentHealth == hp,
                dotImmune = !dot && health.CurrentHealth == hp,
                collisionSuppressed = instance.GetComponentsInChildren<Collider>(true).All(c => !c.enabled),
                samePooledInstance = lease == 1 || instance.GetInstanceID() == actor.firstInstanceId
            };
            if (lease == 1) actor.firstInstanceId = instance.GetInstanceID();
            report.leases.Add(actor.evidence);
            Require(actor.evidence.sampledDuration >= actor.evidence.minimum && actor.evidence.sampledDuration <= actor.evidence.maximum, "Sample outside authored range: " + Labels[i]);
            Require(actor.evidence.invulnerable && actor.evidence.dotImmune && actor.evidence.collisionSuppressed, "Emergence safety gate failed: " + Labels[i]);
            Require(actor.evidence.samePooledInstance, "Pool did not return the same instance: " + Labels[i]);
        }
    }

    private static void InstallFrameObserver()
    {
        // EditorApplication.update has no ordering contract relative to gameplay Update/LateUpdate.
        // Observe the real runtime state only after the complete PostLateUpdate phase instead.
        originalPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem loop = originalPlayerLoop;
        loop.subSystemList = (PlayerLoopSystem[])originalPlayerLoop.subSystemList.Clone();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
            PlayerLoopSystem phase = loop.subSystemList[i];
            var systems = new List<PlayerLoopSystem>(phase.subSystemList ?? Array.Empty<PlayerLoopSystem>());
            systems.Add(new PlayerLoopSystem { type = typeof(CaptureAfterLateUpdate), updateDelegate = Tick });
            phase.subSystemList = systems.ToArray();
            loop.subSystemList[i] = phase;
            PlayerLoop.SetPlayerLoop(loop);
            playerLoopInstalled = true;
            return;
        }
        throw new InvalidOperationException("Unity PlayerLoop did not contain PostLateUpdate.");
    }

    private static void RestorePlayerLoop()
    {
        if (!playerLoopInstalled) return;
        PlayerLoop.SetPlayerLoop(originalPlayerLoop);
        playerLoopInstalled = false;
        EditorApplication.update -= Watchdog;
    }

    private static void Watchdog()
    {
        if (!finishing && EditorApplication.isPlaying && EditorApplication.timeSinceStartup > wallDeadline)
            Fail(new TimeoutException("Capture did not finish within five minutes."));
    }

    private static void Tick()
    {
        if (finishing || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup > wallDeadline) throw new TimeoutException("Capture did not finish within five minutes.");
            if (Time.frameCount == previousFrame) return;
            previousFrame = Time.frameCount;
            float elapsed = Time.time - startTime;
            if (!released && elapsed >= cycleDuration)
            {
                ValidateCompletedLease();
                EnemyPoolRegistry.Instance.ReleaseAllActive();
                foreach (Actor actor in Actors)
                    Require(Vector3.Distance(actor.emergence.VisualPivot.localPosition, actor.restPosition) < 0.001f, "Pool return retained lowered visual pose.");
                released = true;
                reuseTime = elapsed + 0.4f;
            }
            if (released && !reused && elapsed >= reuseTime) { SpawnLease(2); reused = true; }
            int lease = reused ? 2 : 1;
            float leaseElapsed = reused ? elapsed - reuseTime : elapsed;
            heading.text = released && !reused ? "RETURNED TO POOL / SAME INSTANCES NEXT" : $"EMERGENCE / LEASE {lease} / {leaseElapsed:0.00}s";
            for (int i = 0; i < Actors.Count; i++)
            {
                Actor actor = Actors[i];
                if (!actor.instance.activeSelf) { actor.label.text = Labels[i] + "\nIN POOL"; continue; }
                string state = actor.emergence.IsEmerging ? $"RISING {actor.emergence.NormalizedProgress:P0}" : "READY";
                actor.label.text = $"{Labels[i]}  /  {actor.evidence.sampledDuration:0.00}s\n{state}";
                if (actor.emergence.IsEmerging)
                {
                    actor.evidence.rootHeld &= Vector3.Distance(actor.instance.transform.position, actor.evidence.rootAtStart) < 0.02f;
                    Require(actor.instance.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "Collider restored before completion: " + Labels[i]);
                }
                else if (actor.evidence.completionTime == 0f)
                {
                    actor.evidence.completionTime = leaseElapsed;
                    actor.evidence.rootAtCompletion = actor.instance.transform.position;
                    Transform pivot = actor.emergence.VisualPivot;
                    actor.evidence.exactRestPose = Vector3.Distance(pivot.localPosition, actor.restPosition) < 0.001f
                        && Quaternion.Angle(pivot.localRotation, actor.restRotation) < 0.01f
                        && Vector3.Distance(pivot.localScale, actor.restScale) < 0.001f;
                    Collider[] colliders = actor.instance.GetComponentsInChildren<Collider>(true);
                    actor.evidence.collisionRestored = colliders.Length == actor.colliderDefaults.Length
                        && colliders.Select(c => c.enabled).SequenceEqual(actor.colliderDefaults);
                    if (i >= 3) Require(Mathf.Abs(actor.evidence.rootAtCompletion.y - actor.evidence.rootAtStart.y) < 0.02f, "Flyer endpoint jumped: " + Labels[i]);
                }
            }
            CaptureFrame(elapsed);
            if (reused && elapsed >= reuseTime + cycleDuration)
            {
                ValidateCompletedLease();
                Finish();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void ValidateCompletedLease()
    {
        foreach (Actor actor in Actors)
        {
            Require(!actor.emergence.BlocksGameplayNow, "Emergence never completed: " + actor.prefab.name);
            Require(actor.evidence.rootHeld, "Gameplay root moved during emergence: " + actor.prefab.name);
            Require(actor.evidence.exactRestPose, "Visual authoring pose was not restored: " + actor.prefab.name);
            Require(actor.evidence.collisionRestored, "Collider authoring flags were not restored: " + actor.prefab.name);
        }
    }

    private static void CaptureFrame(float elapsed)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline != null)
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
        else camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        readback.Apply(false);
        RenderTexture.active = previous;
        byte[] png = readback.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(Output, "frames", $"frame-{report.frames:0000}.png"), png);
        report.frameTimes.Add(elapsed);
        report.frames++;
        report.simulatedSeconds = elapsed;
        float[] stillTimes = { 0.1f, 0.45f, 0.8f, cycleDuration - 0.2f, reuseTime + 0.45f, reuseTime + cycleDuration - 0.2f };
        if (nextStill < stillTimes.Length && elapsed >= stillTimes[nextStill] && (nextStill < 4 || reused))
        {
            string filename = $"still-{nextStill + 1:00}-{elapsed:0.00}s.png";
            File.WriteAllBytes(Path.Combine(Output, filename), png);
            report.stills.Add(filename);
            nextStill++;
        }
    }

    private static void BuildContactSheet()
    {
        const int cellWidth = 800, cellHeight = 500;
        Texture2D sheet = new Texture2D(cellWidth * 2, cellHeight * 3, TextureFormat.RGB24, false);
        for (int i = 0; i < report.stills.Count && i < 6; i++)
        {
            Texture2D still = new Texture2D(2, 2);
            still.LoadImage(File.ReadAllBytes(Path.Combine(Output, report.stills[i])));
            Color[] pixels = new Color[cellWidth * cellHeight];
            for (int y = 0; y < cellHeight; y++)
                for (int x = 0; x < cellWidth; x++) pixels[y * cellWidth + x] = still.GetPixelBilinear((x + 0.5f) / cellWidth, (y + 0.5f) / cellHeight);
            sheet.SetPixels((i % 2) * cellWidth, (2 - i / 2) * cellHeight, cellWidth, cellHeight, pixels);
            Object.DestroyImmediate(still);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(Output, "contact-sheet.png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Fail(Exception exception)
    {
        if (report == null) report = new CaptureReport { unityVersion = Application.unityVersion };
        report.failures.Add(exception.ToString());
        Debug.LogError("Emergence capture failed: " + exception);
        Finish();
    }

    private static void Finish()
    {
        if (finishing) return;
        finishing = true;
        RestorePlayerLoop();
        report.passed = report.failures.Count == 0 && report.leases.Count == 12;
        Directory.CreateDirectory(Output);
        try { if (report.stills.Count > 0) BuildContactSheet(); }
        catch (Exception ex) { report.failures.Add("Contact sheet: " + ex.Message); report.passed = false; }
        File.WriteAllText(Path.Combine(Output, "capture-report.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(Output, "capture-report.txt"),
            $"{(report.passed ? "PASS" : "FAIL")}: actual Unity Play Mode, {report.frames} frames at {FrameRate} fps; {report.simulatedSeconds:0.00}s simulation.\n"
            + "Six production prefabs; two leases each through EnemyPoolRegistry and OrbitalSpawnPlacement.\n"
            + "No player target: flyers use authored ground-plus-hover-height fallback. No project scene saved.\n"
            + string.Join("\n", report.leases.Select(e => $"{e.prefab} lease {e.lease}: {e.sampledDuration:0.000}s in [{e.minimum:0.00}, {e.maximum:0.00}], instance {e.instanceId}, root held={e.rootHeld}, pose restored={e.exactRestPose}, colliders restored={e.collisionRestored}"))
            + "\n" + string.Join("\n", report.failures));
        SessionState.SetInt(SessionKey + ".exitCode", report.passed ? 0 : 1);
        Debug.Log("Emergence capture " + (report.passed ? "PASS" : "FAIL") + ": " + Output);
        if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        if (readback != null) Object.DestroyImmediate(readback);
        EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }

    private static void RestoreEditor()
    {
        int exitCode = SessionState.GetInt(SessionKey + ".exitCode", 1);
        SessionState.SetBool(SessionKey + ".active", false);
        Time.timeScale = SessionState.GetFloat(SessionKey + ".timeScale", 1f);
        Time.captureFramerate = SessionState.GetInt(SessionKey + ".captureFramerate", 0);
        string json = SessionState.GetString(SessionKey + ".scenes", "");
        if (!string.IsNullOrEmpty(json))
        {
            SavedScenes saved = JsonUtility.FromJson<SavedScenes>(json);
            if (saved.scenes != null && saved.scenes.Length > 0 && saved.scenes.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(saved.scenes.Select(s => new SceneSetup
                    { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray());
        }
        started = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }
}
#endif
