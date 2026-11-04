#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Reproducible production-scene exit renders and capsule clearance measurements, without saving the scene.</summary>
public static class ExitDoorValidationCapture
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Width = 1280;
    private const int Height = 800;

    [Serializable] private sealed class Report
    {
        public string UnityVersion;
        public string Scene = ScenePath;
        public string Scope = "Production scene, production descending clip/presentation/collision; actual player capsule dimensions. No scene or asset is saved.";
        public string Limitation = "Controlled Edit Mode samples and Unity physics queries, not a recording of player input or a completed gameplay run. The cyan capsule is a measurement reference. Save, leaderboard and victory are not invoked.";
        public Vector3 DoorPosition;
        public Vector3 DoorScale;
        public Vector3 DoorEulerAngles;
        public float PlayerHeight;
        public float PlayerRadius;
        public float ChargeSeconds;
        public List<Pose> Poses = new();
        public List<GroundProbe> ApproachGround = new();
        public List<PhysicsTravel> PhysicsTravelChecks = new();
    }

    [Serializable] private sealed class Pose
    {
        public float Progress;
        public string State;
        public Bounds MovingPanel;
        public float SillHeight;
        public float LintelHeight;
        public Passage Walk;
        public Passage AbovePanel;
        public string Image;
    }

    [Serializable] private sealed class Passage
    {
        public Vector3 From;
        public Vector3 To;
        public bool Clear;
        public bool StartOverlaps;
        public string Obstacle;
        public float HitDistance;
    }

    [Serializable] private sealed class GroundProbe
    {
        public Vector3 LocalApproach;
        public bool Hit;
        public string Collider;
        public Vector3 Position;
        public Vector3 Normal;
    }

    [Serializable] private sealed class PhysicsTravel
    {
        public float Progress;
        public string Route;
        public string Method = "Isolated Unity Physics3D simulation of the production solid door colliders and actual-size capsule; 50 steps of .02s, initial speed20m/s, gravity off. No gameplay or save callbacks.";
        public string CollisionMode;
        public Vector3 Start;
        public Vector3 End;
        public bool ExpectedToClear;
        public bool FullyCleared;
        public bool Executed;
        public string Limitation;
        public bool Passed;
    }

    [MenuItem("Tools/ScrapWaves/Capture Descending Exit Validation")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit validation capture runs outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Rendering requires graphics; do not pass -nographics.");

        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_EXIT_PREVIEW_OUTPUT")
            ?? Path.GetFullPath(".utmp/exit-door-20261103");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        var cleanup = new List<Object>();
        RenderTexture target = null;
        try
        {
            var roots = scene.GetRootGameObjects();
            ExitDoor door = roots.SelectMany(r => r.GetComponentsInChildren<ExitDoor>(true)).Single();
            CompactorDoorPresentation presentation = door.GetComponent<CompactorDoorPresentation>();
            Animator animator = Get<Animator>(presentation, "_animator");
            AnimationClip clip = Get<AnimationClip>(presentation, "_openingClip");
            Renderer suction = Get<Renderer>(presentation, "_suctionRenderer");
            BoxCollider panel = Get<Collider>(presentation, "_closedDoorCollider") as BoxCollider;
            PlayerMovement player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerMovement>(true)).Single();
            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            Vector3 playerScale = capsule.transform.lossyScale;
            float height = capsule.height * Mathf.Abs(playerScale.y);
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(playerScale.x), Mathf.Abs(playerScale.z));
            float duration = Get<float>(door, "_chargeDurationSeconds");
            var report = new Report
            {
                UnityVersion = Application.unityVersion,
                DoorPosition = door.transform.position,
                DoorScale = door.transform.lossyScale,
                DoorEulerAngles = door.transform.eulerAngles,
                PlayerHeight = height,
                PlayerRadius = radius,
                ChargeSeconds = duration
            };
            presentation.Configure(door, animator, clip, suction, panel);
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            if (!physics.IsValid()) throw new InvalidOperationException("Capture scene has no valid physics scene.");
            foreach (float x in new[] { -.8f, 0f, .8f })
                foreach (float z in new[] { 3f, 2.5f, 2.1f, 2.05f, 1.95f, 1.85f, 1.4f, .8f, .5f, 0f })
                    report.ApproachGround.Add(ProbeGround(physics, door.transform, x, z));

            Camera camera = NewObject("Exit review camera", scene, cleanup).AddComponent<Camera>();
            camera.enabled = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.045f, .055f, .07f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1800f;
            camera.fieldOfView = 48f;
            var extra = camera.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = false;
            extra.renderShadows = true;
            target = new RenderTexture(Width, Height, 24) { name = "Exit review target" };
            target.Create();
            camera.targetTexture = target;
            Light light = NewObject("Exit review fill", scene, cleanup).AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.transform.rotation = Quaternion.LookRotation(-door.transform.forward + Vector3.down * .65f);

            GameObject ui = NewObject("Exit review captions", scene, cleanup);
            Canvas canvas = ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = .5f;
            canvas.sortingOrder = 100;
            Text header = Label(ui.transform, true);
            Text footer = Label(ui.transform, false);

            GameObject reference = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cleanup.Add(reference);
            SceneManager.MoveGameObjectToScene(reference, scene);
            reference.name = "Actual player capsule dimensions - measurement reference";
            Object.DestroyImmediate(reference.GetComponent<Collider>());
            reference.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            var referenceMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            cleanup.Add(referenceMaterial);
            referenceMaterial.SetColor("_BaseColor", new Color(.05f, .95f, 1f));
            reference.GetComponent<Renderer>().sharedMaterial = referenceMaterial;
            reference.SetActive(false);

            float sill = door.transform.TransformPoint(new Vector3(0f, .345f, 0f)).y;
            float lintel = door.transform.TransformPoint(new Vector3(0f, 2.995f, 0f)).y;
            foreach (float progress in new[] { 0f, .5f, .85f, 1f })
            {
                Sample(door, presentation, progress);
                Physics.SyncTransforms();
                Pose pose = new()
                {
                    Progress = progress,
                    State = door.State.ToString(),
                    MovingPanel = panel.bounds,
                    SillHeight = sill,
                    LintelHeight = lintel,
                    Walk = CheckPassage(physics, door.transform, sill + height / 2f + .06f, height, radius),
                    AbovePanel = CheckPassage(physics, door.transform,
                        Mathf.Max(sill, panel.bounds.max.y) + height / 2f + .06f, height, radius),
                    Image = $"door-{Mathf.RoundToInt(progress * 100):000}.png"
                };
                report.Poses.Add(pose);
                FrameWide(camera, door.transform);
                header.text = $"DESCENDING EXIT  /  {progress:P0} OPEN";
                footer.text = $"Production GameplayScene  |  {duration:0}s opening  |  solid panel follows the door\nControlled scene sample; timer completion alone does not win.";
                Render(camera, target, output, pose.Image);

                if (Mathf.Approximately(progress, .85f))
                {
                    reference.SetActive(true);
                    reference.transform.position = Vector3.Lerp(pose.AbovePanel.From, pose.AbovePanel.To, .45f);
                    Vector3 focus = reference.transform.position;
                    camera.transform.position = focus + door.transform.forward * 20f + door.transform.right * 11f + Vector3.up * 7f;
                    camera.transform.LookAt(focus + Vector3.down * .7f);
                    header.text = "EARLY PASSAGE  /  85% OPEN";
                    footer.text = $"Cyan reference = actual player capsule ({height:0.00} m high, {radius:0.00} m radius)\nUnity capsule sweep: {(pose.AbovePanel.Clear ? "CLEAR" : "BLOCKED by " + pose.AbovePanel.Obstacle)}. This is a geometry check, not recorded player input.";
                    Render(camera, target, output, "early-clearance.png");
                    reference.SetActive(false);
                }
            }

            // Show the exact production Used pose, including its moving collision, at an early completion.
            Sample(door, presentation, .85f, true);
            Physics.SyncTransforms();
            header.text = "EARLY ESCAPE  /  DOOR HELD AT 85%";
            footer.text = "Production Used state retains the crossing pose and panel collision.\nControlled presentation sample; victory/save/leaderboard calls are not executed by this capture.";
            FrameWide(camera, door.transform);
            Render(camera, target, output, "early-used-hold.png");

            // Persist the requested preview and scene-query evidence before optional simulation.
            File.WriteAllText(Path.Combine(output, "validation.json"), JsonUtility.ToJson(report, true));
            foreach (Pose pose in report.Poses.Where(p => p.Progress == 0f || Mathf.Approximately(p.Progress, .85f) || p.Progress == 1f))
            {
                Sample(door, presentation, pose.Progress);
                Physics.SyncTransforms();
                report.PhysicsTravelChecks.Add(SimulateTravel(door, physics, pose.Walk, pose.Progress, "sill-height",
                    height, radius, player.GetComponent<Rigidbody>().collisionDetectionMode, pose.Progress == 1f));
                if (Mathf.Approximately(pose.Progress, .85f))
                    report.PhysicsTravelChecks.Add(SimulateTravel(door, physics, pose.AbovePanel, pose.Progress, "above-panel",
                        height, radius, player.GetComponent<Rigidbody>().collisionDetectionMode, true));
            }

            if (Environment.GetEnvironmentVariable("SCRAPWAVES_EXIT_PREVIEW_VIDEO") == "1")
            {
                string frames = Path.Combine(output, "frames");
                Directory.CreateDirectory(frames);
                for (int i = 0; i <= 120; i++)
                {
                    float progress = Mathf.Min(i / 100f, 1f);
                    Sample(door, presentation, progress);
                    FrameWide(camera, door.transform);
                    header.text = $"DESCENDING EXIT  /  {progress:P0} OPEN";
                    footer.text = $"Production scene, animation and collision  |  {duration:0}s sequence condensed\nControlled Edit Mode preview; escaping requires the full player capsule to cross.";
                    Render(camera, target, frames, $"frame-{i:0000}.png");
                }
            }
            File.WriteAllText(Path.Combine(output, "validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("EXIT_DOOR_CAPTURE_COMPLETE " + output + "\n" + JsonUtility.ToJson(report, true));
        }
        finally
        {
            foreach (var presentation in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompactorDoorPresentation>(true)))
                presentation.Dispose();
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static GroundProbe ProbeGround(PhysicsScene physics, Transform door, float x, float z)
    {
        Vector3 local = new(x, 0f, z);
        // Start below the lintel so interior probes measure the traversable floor, not the roof.
        Vector3 origin = door.TransformPoint(local + Vector3.up * 2.5f);
        bool hit = physics.Raycast(origin, Vector3.down, out RaycastHit result, 600f, ~0, QueryTriggerInteraction.Ignore);
        return new GroundProbe { LocalApproach = local, Hit = hit,
            Collider = hit ? Hierarchy(result.collider.transform) : null,
            Position = hit ? result.point : Vector3.zero,
            Normal = hit ? result.normal : Vector3.zero };
    }

    private static Passage CheckPassage(PhysicsScene physics, Transform door, float centerY, float height, float radius)
    {
        Vector3 from = door.TransformPoint(new Vector3(0f, 0f, .85f));
        Vector3 to = door.TransformPoint(new Vector3(0f, 0f, 0f));
        from.y = to.y = centerY;
        Vector3 halfShaft = Vector3.up * Mathf.Max(0f, height / 2f - radius);
        Collider[] overlaps = new Collider[32];
        int overlapCount = physics.OverlapCapsule(from - halfShaft, from + halfShaft, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        Vector3 delta = to - from;
        bool hit = physics.CapsuleCast(from - halfShaft, from + halfShaft, radius, delta.normalized,
            out RaycastHit result, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        return new Passage { From = from, To = to, Clear = !hit && overlapCount == 0,
            StartOverlaps = overlapCount > 0,
            Obstacle = overlapCount > 0 ? Hierarchy(overlaps[0].transform) : hit ? Hierarchy(result.collider.transform) : null,
            HitDistance = hit ? result.distance : delta.magnitude };
    }

    private static PhysicsTravel SimulateTravel(ExitDoor door, PhysicsScene sourcePhysics, Passage passage, float progress, string route,
        float height, float radius, CollisionDetectionMode collisionMode, bool expectedToClear)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        PhysicsMaterial material = null;
        PhysicsTravel evidence = new() { Progress = progress, Route = route,
            CollisionMode = collisionMode.ToString(), Start = passage.From, ExpectedToClear = expectedToClear };
        try
        {
            PhysicsScene physics = scene.GetPhysicsScene();
            if (!physics.IsValid() || physics == sourcePhysics || physics == Physics.defaultPhysicsScene)
            {
                evidence.Limitation = "Editor preview scenes did not supply an isolated physics world. Simulation was skipped to avoid advancing unrelated bodies; actual-scene capsule casts remain available. Dynamic-body verification requires Play Mode.";
                return evidence;
            }
            foreach (Collider source in door.GetComponentsInChildren<Collider>())
            {
                if (!source.enabled || source.isTrigger) continue;
                var go = new GameObject(source.name + " - validation collision");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                go.transform.localScale = source.transform.lossyScale;
                Collider copy;
                if (source is BoxCollider box)
                {
                    var clonedBox = go.AddComponent<BoxCollider>();
                    clonedBox.center = box.center;
                    clonedBox.size = box.size;
                    copy = clonedBox;
                }
                else if (source is MeshCollider mesh)
                {
                    var clonedMesh = go.AddComponent<MeshCollider>();
                    clonedMesh.sharedMesh = mesh.sharedMesh;
                    clonedMesh.convex = mesh.convex;
                    copy = clonedMesh;
                }
                else throw new InvalidOperationException("Unsupported authored door collider: " + source.GetType().Name);
                copy.sharedMaterial = source.sharedMaterial;
            }
            var player = new GameObject("Actual-size player capsule physics probe");
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.position = passage.From;
            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = height;
            capsule.radius = radius;
            material = new PhysicsMaterial("Player movement frictionless validation")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            capsule.sharedMaterial = material;
            Rigidbody body = player.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = collisionMode;
            Physics.SyncTransforms();
            body.linearVelocity = (passage.To - passage.From).normalized * 20f;
            for (int i = 0; i < 50; i++) physics.Simulate(.02f);
            Vector3 localCenter = door.transform.InverseTransformPoint(body.position);
            float trailingSide = localCenter.z + radius / Mathf.Abs(door.transform.lossyScale.z);
            bool cleared = trailingSide <= door.GetComponent<ExitDoorTraversal>().Aperture.min.z;
            evidence.End = body.position;
            evidence.Executed = true;
            evidence.FullyCleared = cleared;
            evidence.Passed = cleared == expectedToClear;
            return evidence;
        }
        catch (Exception exception)
        {
            evidence.Limitation = exception.GetType().Name + ": " + exception.Message;
            return evidence;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (material != null) Object.DestroyImmediate(material);
        }
    }

    private static void Sample(ExitDoor door, CompactorDoorPresentation presentation, float progress, bool used = false)
    {
        Set(door, "_chargeRemaining", Get<float>(door, "_chargeDurationSeconds") * (1f - progress));
        Set(door, "_escapeProgress", progress);
        Set(door, "_state", used ? ExitDoorState.Used : progress >= 1f ? ExitDoorState.Ready : ExitDoorState.Charging);
        typeof(CompactorDoorPresentation).GetMethod("Synchronize", Fields).Invoke(presentation, null);
    }

    private static void FrameWide(Camera camera, Transform door)
    {
        Vector3 focus = door.TransformPoint(new Vector3(0f, 2.35f, 0f));
        camera.transform.position = focus + door.forward * 230f + door.right * 90f + Vector3.up * 35f;
        camera.transform.LookAt(focus);
    }

    private static void Render(Camera camera, RenderTexture target, string output, string file)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(output, file), image.EncodeToPNG());
        RenderTexture.active = previous;
        Object.DestroyImmediate(image);
    }

    private static Text Label(Transform parent, bool top)
    {
        var go = new GameObject(top ? "Title" : "Evidence", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rect.pivot = new Vector2(.5f, top ? 1f : 0f);
        rect.offsetMin = new Vector2(24f, top ? -90f : 14f);
        rect.offsetMax = new Vector2(-24f, top ? -18f : 85f);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = top ? 31 : 20;
        text.fontStyle = top ? FontStyle.Bold : FontStyle.Normal;
        text.color = top ? Color.white : new Color(.65f, .95f, 1f);
        text.alignment = top ? TextAnchor.UpperLeft : TextAnchor.LowerLeft;
        text.raycastTarget = false;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static GameObject NewObject(string name, Scene scene, List<Object> cleanup)
    {
        var go = new GameObject(name);
        cleanup.Add(go);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static string Hierarchy(Transform value) => value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name;
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
}
#endif
