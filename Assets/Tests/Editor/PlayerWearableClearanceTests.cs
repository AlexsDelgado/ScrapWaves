using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Checks final-pose surface following and records body contact at the approved wearable placement.</summary>
public sealed class PlayerWearableClearanceTests
{
    private const string SetupKey = "PlayerWearableClearanceTests.SceneSetup";
    private const string Output = ".utmp/player-animation/clipping-runtime";
    private const float FrameTime = 1f / 30f;
    private readonly List<Object> _cleanup = new();
    private bool _previousBackfaces;
    private bool _physicsSettingSaved;

    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }
    private sealed class Scenario
    {
        public string Name;
        public bool Armed, Crouching, Sliding;
        public Vector3 Velocity;
        public float Yaw, Pitch;
    }
    private sealed class EdgeMesh
    {
        public Transform Transform;
        public Vector3[] Vertices;
        public Vector3[] WorldVertices;
        public int[] Edges;
    }
    private sealed class Artifact
    {
        public WeaponType Type;
        public EdgeMesh[] Meshes;
        public int EdgeCount => Meshes.Sum(mesh => mesh.Edges.Length / 2);
    }
    private sealed class RuntimePosePump : MonoBehaviour
    {
        public PlayerAnimationDriver Driver;
        public Rigidbody Body;
        public Transform Hand;
        public Vector3 Velocity, Direction = Vector3.forward;

        // EditMode tests resume through EditorApplication.update, whose position
        // relative to the player loop is not guaranteed. Supply the target during
        // actual Update so the driver's LateUpdate cannot consume a fallback first.
        private void Update() => Evaluate();

        public void Evaluate()
        {
            Body.linearVelocity = Velocity;
            Driver.EvaluatePoseForWeapons(FrameTime, Hand.position + Direction * 30f);
        }
    }

    [UnityTest]
    public IEnumerator ProductionWearables_FollowFinalAnimatedSurfaceAcrossRuntimeMovementAndAim()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled Editor scene before running the wearable clearance diagnostic.");
        Assert.That(Enumerable.Range(0, SceneManager.sceneCount).All(index => !SceneManager.GetSceneAt(index).isDirty),
            Is.True, "Save scene changes before running the diagnostic; unsaved work is never discarded.");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene
            { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return RecordClearance();
    }

    private IEnumerator RecordClearance()
    {
        Assert.That(Application.isPlaying, Is.True);
        Time.timeScale = 1f;
        Time.captureDeltaTime = FrameTime;
        _previousBackfaces = Physics.queriesHitBackfaces;
        _physicsSettingSaved = true;
        Physics.queriesHitBackfaces = true;
        Directory.CreateDirectory(Output);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject actor = Track(Object.Instantiate(prefab));
        actor.name = "Disposable production wearable clearance diagnostic";
        PlayerAnimationDriver driver = actor.GetComponent<PlayerAnimationDriver>();
        PlayerMovement movement = actor.GetComponent<PlayerMovement>();
        Rigidbody body = actor.GetComponent<Rigidbody>();
        Assert.That(driver, Is.Not.Null);
        Assert.That(movement, Is.Not.Null);
        Assert.That(body, Is.Not.Null);
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = behaviour == driver;
        foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        SetField(movement, "_isGrounded", true);
        SetField(driver, "_aimProvider", null);
        SetField(driver, "_weapons", null);
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        body.interpolation = RigidbodyInterpolation.None;
        body.linearDamping = 0f;

        Transform visual = driver.RigAnimator.transform;
        Transform hand = visual.GetComponentsInChildren<Transform>(true).Single(item => item.name == "hand.R");
        SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
        Assert.That(skins, Is.Not.Empty);
        Mesh[] baked = skins.Select(_ => Track(new Mesh { name = "Runtime clearance body surface" })).ToArray();
        MeshCollider[] surfaces = skins.Select(_ => Track(new GameObject("Disposable body raycast surface"))
            .AddComponent<MeshCollider>()).ToArray();
        for (int index = 0; index < skins.Length; index++)
        {
            // The collider is outside the dynamic Rigidbody hierarchy: a concave body
            // surface is used only for direct ray queries and never for player physics.
            surfaces[index].gameObject.layer = 2;
            var snapshot = new GameObject("Runtime evaluated body snapshot");
            snapshot.transform.SetParent(skins[index].transform, false);
            snapshot.AddComponent<MeshFilter>().sharedMesh = baked[index];
            snapshot.AddComponent<MeshRenderer>().sharedMaterials = skins[index].sharedMaterials;
            skins[index].enabled = false;
        }

        WearableWeaponMountCatalog catalog = Resources.Load<WearableWeaponMountCatalog>(WearableWeaponMountCatalog.ResourceName);
        Assert.That(catalog, Is.Not.Null);
        PlayerWeaponMountController sockets = actor.GetComponent<PlayerWeaponMountController>();
        PlayerWearableSurfaceFollower follower = actor.GetComponent<PlayerWearableSurfaceFollower>();
        Assert.That(follower, Is.Not.Null);
        Assert.That(follower.Attachments.Count, Is.EqualTo(5));
        Assert.That(follower.Attachments.Select(attachment => attachment.Type),
            Is.EquivalentTo(catalog.Definitions.Select(definition => definition.Type)));
        foreach (PlayerWearableSurfaceFollower.SurfaceAttachment attachment in follower.Attachments)
            Assert.That(attachment.Socket, Is.SameAs(sockets.GetAnimatedSocket(attachment.Type)), attachment.Type.ToString());
        var artifacts = new List<Artifact>();
        foreach (WearableWeaponMountDefinition definition in catalog.Definitions)
        {
            Transform socket = sockets.GetAnimatedSocket(definition.Type);
            Assert.That(socket, Is.Not.Null, definition.Type + " needs its production animated socket.");
            GameObject instance = Object.Instantiate(definition.Prefab, socket, false);
            instance.name = definition.Type + " clearance diagnostic";
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
            data.WeaponType = definition.Type;
            instance.GetComponent<AutomaticWeaponMount>().Bind(new WeaponInstance { Data = data, State = WeaponState.Automatic });
            EdgeMesh[] meshes = instance.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null).Select(CreateEdges).ToArray();
            Assert.That(meshes, Is.Not.Empty, definition.Type.ToString());
            artifacts.Add(new Artifact { Type = definition.Type, Meshes = meshes });
        }
        Assert.That(artifacts.Count, Is.EqualTo(5));

        Camera camera = CreateCamera(actor.scene);
        CreateLight("Runtime clearance front light", new Vector3(35f, -30f, 0f), 1.8f);
        CreateLight("Runtime clearance rear light", new Vector3(25f, 150f, 0f), 1.3f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f, .6f, .6f);
        RenderSettings.fog = false;
        WeaponData manualData = Track(ScriptableObject.CreateInstance<WeaponData>());
        manualData.WeaponType = WeaponType.AutomaticCannon;
        WeaponInstance manual = new() { Data = manualData, State = WeaponState.Manual, CurrentAmmo = 100f };
        RuntimePosePump pump = actor.AddComponent<RuntimePosePump>();
        pump.Driver = driver;
        pump.Body = body;
        pump.Hand = hand;
        var report = new StringBuilder("scenario,frame,artifact,surface_crossing_edges,tested_edges,requested_aim_yaw,requested_aim_pitch,actual_aim_yaw,actual_aim_pitch\n");
        var worst = new StringBuilder("scenario,frame,total_surface_crossing_edges\n");
        var firstLocalPositions = new Dictionary<WeaponType, Vector3>();
        var firstLocalRotations = new Dictionary<WeaponType, Quaternion>();
        var surfaceDrivenSockets = new HashSet<WeaponType>();
        bool positiveControlChecked = false;

        foreach (Scenario scenario in Scenarios())
        {
            SetField(movement, "_isCrouching", scenario.Crouching);
            SetField(movement, "_isSliding", scenario.Sliding);
            driver.SetManualWeaponOverride(scenario.Armed ? manual : null);
            driver.ResetPresentation();
            pump.Direction = Quaternion.Euler(-scenario.Pitch, scenario.Yaw, 0f) * Vector3.forward;
            pump.Velocity = scenario.Velocity;
            for (int frame = 0; frame < 16; frame++)
            {
                body.linearVelocity = scenario.Velocity;
                yield return null;
                pump.Evaluate();
            }
            int initialEvaluations = driver.GraphEvaluationCount;
            int worstCrossings = -1;
            int worstFrame = -1;
            // Twenty-four samples cover a complete 22-frame directional run cycle.
            for (int frame = 0; frame < 24; frame++)
            {
                body.linearVelocity = scenario.Velocity;
                yield return null;
                pump.Evaluate();
                Assert.That(driver.LastEvaluatedFrame, Is.EqualTo(Time.frameCount), scenario.Name + ": stale pose.");
                AssertFinalSurfacePose(follower, scenario.Name, firstLocalPositions, firstLocalRotations, surfaceDrivenSockets);
                float actualPitch = (float)typeof(PlayerAnimationDriver).GetField("_aimPitch",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(driver);
                if (scenario.Armed)
                {
                    Assert.That(driver.CurrentAimYaw, Is.EqualTo(scenario.Yaw).Within(2f),
                        scenario.Name + ": runtime graph must consume the requested yaw, not fallback forward aim.");
                    Assert.That(actualPitch, Is.EqualTo(scenario.Pitch).Within(2f),
                        scenario.Name + ": runtime graph must consume the requested pitch.");
                }
                BakeSurfaces(skins, baked, surfaces);
                if (!positiveControlChecked)
                {
                    AssertBodyRaycastPositiveControl(skins[0], baked[0], surfaces[0]);
                    positiveControlChecked = true;
                }
                int total = 0;
                foreach (Artifact artifact in artifacts)
                {
                    int crossings = CountCrossings(artifact, surfaces);
                    total += crossings;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5:F1},{6:F1},{7:F2},{8:F2}",
                        scenario.Name, frame, artifact.Type, crossings, artifact.EdgeCount, scenario.Yaw, scenario.Pitch,
                        driver.CurrentAimYaw, actualPitch));
                }
                if (total > worstCrossings)
                {
                    worstCrossings = total;
                    worstFrame = frame;
                    Vector3 focus = actor.transform.position + Vector3.up * .15f;
                    Capture(camera, focus, new Vector3(2.8f, .65f, 5f), scenario.Name + "-worst-front.png");
                    Capture(camera, focus, new Vector3(-2.8f, .65f, -5f), scenario.Name + "-worst-back.png");
                }
            }
            Assert.That(driver.GraphEvaluationCount - initialEvaluations, Is.EqualTo(24),
                scenario.Name + ": evaluate the production graph once per sampled frame.");
            worst.AppendLine($"{scenario.Name},{worstFrame},{worstCrossings}");
            File.WriteAllText(Path.Combine(Output, "surface-crossings.csv"), report.ToString());
            File.WriteAllText(Path.Combine(Output, "worst-poses.csv"), worst.ToString());
        }
        File.WriteAllText(Path.Combine(Output, "capture-info.txt"),
            "Production prefab, real PlayerAnimationDriver graph, procedural aim and armed running follow-through.\n" +
            "All five production wearable meshes use their animated sockets. Disabled input supplies grounded state and Rigidbody velocity.\n" +
            "A test-only Update callback supplies aim before the production driver's LateUpdate; actual yaw/pitch are asserted and recorded.\n" +
            "Each scenario settles for 16 frames, then samples 24 frames at 30 fps. Front/back images show the highest total crossing count.\n" +
            "Counts are unique wearable triangle edges that cross the actual CPU-baked player skin, using two-sided MeshCollider raycasts.\n" +
            "A ray through the first baked body triangle confirms that the collision query can detect the sampled body surface.\n" +
            "This is a surface-intersection diagnostic, not a penetration depth or volume measurement; fully contained parts can have no crossings.\n" +
            "The restored catalog layout includes mounting contact with the player mesh, so crossing counts are inspection data, not a zero-contact requirement.\n" +
            "Every sample asserts valid skin anchors and that the driver already applied their final pose before weapon consumption.\n" +
            "At least one socket must move relative to its parent bone across the sequence, demonstrating surface following beyond single-bone parenting.\n" +
            "Sockets with measured motion relative to their bone: " + string.Join(", ", surfaceDrivenSockets) + ".\n");
        Assert.That(positiveControlChecked, Is.True, "The body-surface raycast needs a positive control.");
        Assert.That(surfaceDrivenSockets, Is.Not.Empty,
            "The skinned surface must drive a socket relative to its carrier bone; fixed bone offsets alone do not follow surface deformation.");
    }

    private static void AssertFinalSurfacePose(PlayerWearableSurfaceFollower follower, string scenario,
        Dictionary<WeaponType, Vector3> firstLocalPositions, Dictionary<WeaponType, Quaternion> firstLocalRotations,
        HashSet<WeaponType> surfaceDrivenSockets)
    {
        // Snapshot immediately after the driver's one pose evaluation. Evaluating
        // again must make no change: weapons already need these exact final poses.
        Vector3[] positions = follower.Attachments.Select(attachment => attachment.Socket.position).ToArray();
        Quaternion[] rotations = follower.Attachments.Select(attachment => attachment.Socket.rotation).ToArray();
        foreach (PlayerWearableSurfaceFollower.SurfaceAttachment attachment in follower.Attachments)
        {
            string label = scenario + ": " + attachment.Type;
            Assert.That(attachment.A.TryEvaluate(out Vector3 a), Is.True, label + " anchor A");
            Assert.That(attachment.B.TryEvaluate(out Vector3 b), Is.True, label + " anchor B");
            Assert.That(attachment.C.TryEvaluate(out Vector3 c), Is.True, label + " anchor C");
            Assert.That(PlayerWearableSurfaceFollower.TryGetFrame(follower.transform.InverseTransformPoint(a),
                follower.transform.InverseTransformPoint(b), follower.transform.InverseTransformPoint(c), out _, out _),
                Is.True, label + ": the evaluated skin patch must define a usable rigid frame.");
            if (!firstLocalPositions.ContainsKey(attachment.Type))
            {
                firstLocalPositions.Add(attachment.Type, attachment.Socket.localPosition);
                firstLocalRotations.Add(attachment.Type, attachment.Socket.localRotation);
            }
            else if (Vector3.Distance(firstLocalPositions[attachment.Type], attachment.Socket.localPosition) > .0001f
                || Quaternion.Angle(firstLocalRotations[attachment.Type], attachment.Socket.localRotation) > .05f)
                surfaceDrivenSockets.Add(attachment.Type);
        }
        follower.EvaluateAttachments();
        for (int index = 0; index < follower.Attachments.Count; index++)
        {
            PlayerWearableSurfaceFollower.SurfaceAttachment attachment = follower.Attachments[index];
            string label = scenario + ": " + attachment.Type;
            Assert.That(Vector3.Distance(attachment.Socket.position, positions[index]), Is.LessThan(.00002f),
                label + ": the driver must update the surface socket before any weapon reads it.");
            Assert.That(Quaternion.Angle(attachment.Socket.rotation, rotations[index]), Is.LessThan(.05f),
                label + ": the driver's socket rotation must include final procedural aim and recoil.");
        }
    }

    [Test]
    public void ProductionWearables_RestoreAuthoredCatalogPositionsAndRotationsInBindPose()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject actor = Track(Object.Instantiate(prefab));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        actor.transform.SetPositionAndRotation(new Vector3(1.5f, .3f, -2f), Quaternion.Euler(0f, 37f, 0f));
        PlayerAnimationDriver driver = actor.GetComponent<PlayerAnimationDriver>();
        PlayerWearableSurfaceFollower follower = actor.GetComponent<PlayerWearableSurfaceFollower>();
        PlayerWeaponMountController sockets = actor.GetComponent<PlayerWeaponMountController>();
        Assert.That(driver, Is.Not.Null);
        Assert.That(follower, Is.Not.Null);
        Assert.That(sockets, Is.Not.Null);
        GameObject visual = driver.RigAnimator.gameObject;
        driver.RigAnimator.runtimeAnimatorController = null;
        AnimationClip slide = PlaceholderPlayerAnimationBuilder.Clips().Single(clip => clip.name == "Slide");
        slide.SampleAnimation(visual, slide.length * .4f);
        follower.EvaluateAttachments();
        MethodInfo restore = typeof(PlaceholderPlayerAnimationBuilder).GetMethod("RestoreBindPose",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(restore, Is.Not.Null);
        restore.Invoke(null, new object[] { visual });
        follower.EvaluateAttachments();
        WearableWeaponMountCatalog catalog = Resources.Load<WearableWeaponMountCatalog>(WearableWeaponMountCatalog.ResourceName);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(follower.Attachments.Count, Is.EqualTo(5));
        foreach (WearableWeaponMountDefinition definition in catalog.Definitions)
        {
            Transform socket = sockets.GetAnimatedSocket(definition.Type);
            Assert.That(socket, Is.Not.Null, definition.Type.ToString());
            Assert.That(Vector3.Distance(actor.transform.InverseTransformPoint(socket.position), definition.LocalPosition),
                Is.LessThan(.00002f), definition.Type + ": preserve the authored position after animation resets.");
            Quaternion rootRotation = Quaternion.Inverse(actor.transform.rotation) * socket.rotation;
            Assert.That(Quaternion.Angle(rootRotation, Quaternion.Euler(definition.LocalEulerAngles)), Is.LessThan(.05f),
                definition.Type + ": preserve the authored rotation after animation resets.");
        }
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        foreach (bool armed in new[] { false, true })
        {
            string prefix = armed ? "Armed" : "Unarmed";
            yield return new Scenario { Name = prefix + "-Idle", Armed = armed };
            yield return new Scenario { Name = prefix + "-Forward", Armed = armed, Velocity = Vector3.forward * 5f };
            yield return new Scenario { Name = prefix + "-Backward", Armed = armed, Velocity = Vector3.back * 4.4f };
            yield return new Scenario { Name = prefix + "-Left", Armed = armed, Velocity = Vector3.left * 4.4f };
            yield return new Scenario { Name = prefix + "-Right", Armed = armed, Velocity = Vector3.right * 4.4f };
            yield return new Scenario { Name = prefix + "-Crouch", Armed = armed, Crouching = true };
            yield return new Scenario { Name = prefix + "-Slide", Armed = armed, Crouching = true, Sliding = true, Velocity = Vector3.forward * 7f };
        }
        foreach (float yaw in new[] { -95f, 0f, 95f })
            foreach (float pitch in new[] { -60f, 0f, 70f })
                if (yaw != 0f || pitch != 0f)
                    yield return new Scenario { Name = $"Aim-Y{yaw}-P{pitch}", Armed = true, Yaw = yaw, Pitch = pitch };
    }

    private static EdgeMesh CreateEdges(MeshFilter filter)
    {
        Mesh mesh = filter.sharedMesh;
        // Editor access deliberately supports imported meshes with Read/Write off;
        // enabling that flag on production assets would retain unnecessary runtime copies.
        using var dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh);
        Mesh.MeshData data = dataArray[0];
        using var nativeVertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
        data.GetVertices(nativeVertices);
        Vector3[] vertices = nativeVertices.ToArray();
        var triangleList = new List<int>();
        for (int submesh = 0; submesh < data.subMeshCount; submesh++)
        {
            using var indices = new NativeArray<int>(data.GetSubMesh(submesh).indexCount, Allocator.Temp);
            data.GetIndices(indices, submesh);
            triangleList.AddRange(indices.ToArray());
        }
        int[] triangles = triangleList.ToArray();
        var unique = new HashSet<ulong>();
        var edges = new List<int>();
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            for (int side = 0; side < 3; side++)
            {
                int a = triangles[triangle + side], b = triangles[triangle + (side + 1) % 3];
                if (a > b) (a, b) = (b, a);
                if (unique.Add(((ulong)(uint)a << 32) | (uint)b)) { edges.Add(a); edges.Add(b); }
            }
        return new EdgeMesh { Transform = filter.transform, Vertices = vertices,
            WorldVertices = new Vector3[vertices.Length], Edges = edges.ToArray() };
    }

    private static void BakeSurfaces(SkinnedMeshRenderer[] skins, Mesh[] baked, MeshCollider[] surfaces)
    {
        for (int index = 0; index < skins.Length; index++)
        {
            skins[index].BakeMesh(baked[index]);
            surfaces[index].transform.SetPositionAndRotation(skins[index].transform.position, skins[index].transform.rotation);
            surfaces[index].transform.localScale = skins[index].transform.lossyScale;
            surfaces[index].sharedMesh = null;
            surfaces[index].sharedMesh = baked[index];
        }
        Physics.SyncTransforms();
    }

    private static int CountCrossings(Artifact artifact, MeshCollider[] surfaces)
    {
        const float endpointTolerance = .0001f;
        int crossings = 0;
        foreach (EdgeMesh mesh in artifact.Meshes)
        {
            Matrix4x4 matrix = mesh.Transform.localToWorldMatrix;
            for (int vertex = 0; vertex < mesh.Vertices.Length; vertex++)
                mesh.WorldVertices[vertex] = matrix.MultiplyPoint3x4(mesh.Vertices[vertex]);
            for (int edge = 0; edge < mesh.Edges.Length; edge += 2)
            {
                Vector3 start = mesh.WorldVertices[mesh.Edges[edge]];
                Vector3 delta = mesh.WorldVertices[mesh.Edges[edge + 1]] - start;
                float length = delta.magnitude;
                if (length <= 2f * endpointTolerance) continue;
                Vector3 direction = delta / length;
                var ray = new Ray(start + direction * endpointTolerance, direction);
                foreach (MeshCollider surface in surfaces)
                    if (surface.Raycast(ray, out _, length - 2f * endpointTolerance)) { crossings++; break; }
            }
        }
        return crossings;
    }

    private static void AssertBodyRaycastPositiveControl(SkinnedMeshRenderer skin, Mesh baked, MeshCollider surface)
    {
        Vector3[] vertices = baked.vertices;
        int[] triangles = baked.triangles;
        Assert.That(triangles.Length, Is.GreaterThanOrEqualTo(3), "The sampled body needs an actual triangle surface.");
        Vector3 a = skin.transform.TransformPoint(vertices[triangles[0]]);
        Vector3 b = skin.transform.TransformPoint(vertices[triangles[1]]);
        Vector3 c = skin.transform.TransformPoint(vertices[triangles[2]]);
        Vector3 normal = Vector3.Cross(b - a, c - a);
        Assert.That(normal.sqrMagnitude, Is.GreaterThan(.0000000001f), "The first body triangle must be nondegenerate.");
        normal.Normalize();
        Vector3 center = (a + b + c) / 3f;
        var ray = new Ray(center + normal * .01f, -normal);
        Assert.That(surface.Raycast(ray, out _, .02f), Is.True,
            "The positive-control ray must cross the first baked body triangle; zero wearable counts must not come from inactive queries.");
    }

    private Camera CreateCamera(Scene scene)
    {
        Camera camera = Track(new GameObject("Runtime clearance camera")).AddComponent<Camera>();
        camera.enabled = false;
        camera.scene = scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.10f, .12f, .15f);
        camera.orthographic = true;
        camera.orthographicSize = 1.15f;
        camera.nearClipPlane = .01f;
        return camera;
    }

    private void CreateLight(string name, Vector3 euler, float intensity)
    {
        Light light = Track(new GameObject(name)).AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.transform.rotation = Quaternion.Euler(euler);
    }

    private static void Capture(Camera camera, Vector3 focus, Vector3 offset, string filename)
    {
        camera.transform.position = focus + offset;
        camera.transform.LookAt(focus);
        RenderTexture target = RenderTexture.GetTemporary(600, 600, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new(600, 600, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 600, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
        }
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private T Track<T>(T item) where T : Object { _cleanup.Add(item); return item; }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        if (_physicsSettingSaved) Physics.queriesHitBackfaces = _previousBackfaces;
        _physicsSettingSaved = false;
        for (int index = _cleanup.Count - 1; index >= 0; index--)
            if (_cleanup[index] != null) Object.DestroyImmediate(_cleanup[index]);
        _cleanup.Clear();
        if (Application.isPlaying)
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
        string saved = SessionState.GetString(SetupKey, "");
        if (string.IsNullOrEmpty(saved)) yield break;
        SessionState.EraseString(SetupKey);
        SavedSetup setup = JsonUtility.FromJson<SavedSetup>(saved);
        if (setup?.Scenes?.Length > 0 && setup.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(scene => new SceneSetup
            { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
    }
}
