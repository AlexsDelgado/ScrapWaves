using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Separates owner filtering, real launch obstruction, and invalid overlap contacts.</summary>
public sealed class MortarOwnerCollisionTests
{
    private static readonly Vector3 Anchor = new(1000f, 0f, 1000f);
    private const float Radius = .18f;
    private readonly List<Object> _objects = new();
    private readonly RaycastHit[] _hits = new RaycastHit[64];

    private sealed class RecordingSink : IWeaponPresentationSink
    {
        public readonly List<WeaponPresentationContext> Cues = new();
        public void Emit(in WeaponPresentationContext context) => Cues.Add(context);
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
    }

    [Test]
    public void NearlyHorizontalNonconvexOverlap_ReturnsLocalSurfaceInsteadOfSyntheticWorldZero()
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        var mesh = Keep(new Mesh { name = "Owned nonconvex grazing quad" });
        mesh.vertices = new[]
        {
            new Vector3(-2f, 0f, -2f), new Vector3(-2f, 0f, 2f),
            new Vector3(2f, 0f, 2f), new Vector3(2f, 0f, -2f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var terrain = Keep(new GameObject("Grazing nonconvex floor", typeof(MeshCollider)));
        terrain.transform.position = contact;
        var collider = terrain.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
        Physics.SyncTransforms();
        Vector3 start = contact + Vector3.up * .17f;
        Vector3 end = start + new Vector3(.2f, -.0002f, 0f);
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, end, Radius, null, _hits,
            false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider));
        Assert.That(Vector3.Distance(hit.point, contact), Is.LessThan(.2f),
            $"A touching sweep must return a local physical contact. start={start}; hit={hit.point}; normal={hit.normal}; distance={hit.distance}");
        Assert.That(hit.point.y, Is.EqualTo(contact.y).Within(.0002f));
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThan(.999f));
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase(24f)]
    [TestCase(90f)]
    [TestCase(180f)]
    public void GrazingNonconvexTiltedFloorWallAndCeiling_RecoverTheirOwnSurface(float rotationDegrees)
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        Quaternion rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        Collider collider = Quad(contact, rotation, 2f);
        Vector3 normal = rotation * Vector3.up;
        Vector3 start = contact + normal * .17f;
        Vector3 end = start + rotation * new Vector3(.2f, -.0002f, 0f);
        Physics.SyncTransforms();
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, end, Radius, null, _hits,
            false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider));
        Assert.That(Vector3.Distance(hit.point, contact), Is.LessThan(.2f));
        Assert.That(Mathf.Abs(Vector3.Dot(hit.point - contact, normal)), Is.LessThan(.0003f));
        Assert.That(Vector3.Dot(hit.normal, normal), Is.GreaterThan(.999f));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void GrazingCompositeMesh_RecoversTouchingFloorInsteadOfDistantWallInSameCollider()
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        var mesh = Keep(new Mesh { name = "Owned combined floor and nearby wall" });
        mesh.vertices = new[]
        {
            new Vector3(-2f, 0f, -2f), new Vector3(-2f, 0f, 2f),
            new Vector3(2f, 0f, 2f), new Vector3(2f, 0f, -2f),
            new Vector3(.5f, 0f, -2f), new Vector3(.5f, 0f, 2f),
            new Vector3(.5f, 2f, 2f), new Vector3(.5f, 2f, -2f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        mesh.RecalculateBounds();
        var terrain = Keep(new GameObject("Combined nonconvex floor and wall", typeof(MeshCollider)));
        terrain.transform.position = contact;
        MeshCollider collider = terrain.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
        Physics.SyncTransforms();
        Vector3 start = contact + Vector3.up * .17f;
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, start + Vector3.right * .2f,
            Radius, null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider));
        Assert.That(Vector3.Distance(hit.point, contact), Is.LessThan(.0003f),
            $"The .18m sphere touches the floor only; the same mesh's wall is .5m away. Recovered point={hit.point}, normal={hit.normal}");
        Assert.That(Vector3.Dot(hit.normal, Vector3.up), Is.GreaterThan(.999f));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void GrazingTerrainCollider_RecoversLocalGroundWithoutUnsupportedClosestPoint()
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        var data = Keep(new TerrainData { heightmapResolution = 33, size = new Vector3(4f, 1f, 4f) });
        data.SetHeights(0, 0, new float[33, 33]);
        var terrain = Keep(new GameObject("Owned grazing TerrainCollider", typeof(TerrainCollider)));
        terrain.transform.position = contact + new Vector3(-2f, 0f, -2f);
        TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
        collider.terrainData = data;
        Physics.SyncTransforms();
        Vector3 start = contact + Vector3.up * .17f;
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, start + new Vector3(.2f, -.0002f, 0f),
            Radius, null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider));
        Assert.That(Vector3.Distance(hit.point, contact), Is.LessThan(.2f));
        Assert.That(hit.point.y, Is.EqualTo(contact.y).Within(.0003f));
        Assert.That(hit.normal.y, Is.GreaterThan(.999f));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void GrazingTinyOffAxisPatch_WhenAllRecoveryRaysMiss_RetainsCollisionAtLocalSweepStart()
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        Collider collider = Quad(contact + new Vector3(.06f, 0f, .06f), Quaternion.identity, .02f);
        Vector3 start = contact + Vector3.up * .15f;
        Physics.SyncTransforms();
        Assert.That(MortarTrajectory.TryGetSegmentCollision(start, start + new Vector3(.2f, -.0002f, 0f),
            Radius, null, _hits, false, out RaycastHit hit), Is.True);
        Assert.That(hit.collider, Is.SameAs(collider), "The recovery fallback must not drop a confirmed obstruction.");
        Assert.That(Vector3.Distance(hit.point, start), Is.LessThan(.0003f),
            "An off-axis patch touches the sphere but misses all six center-axis probes; use the local sphere center rather than world zero.");
        Assert.That(hit.distance, Is.Zero);
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase(1f)]
    [TestCase(1.5f)]
    public void ActualGrazingShell_PreviewAndImpactAgreeAtColdAndHotSpeeds(float speedMultiplier)
    {
        Vector3 contact = Anchor + Vector3.up * 2f;
        Collider collider = Quad(contact, Quaternion.identity, 2f);
        Vector3 start = contact + Vector3.up * .17f;
        Vector3 target = start + new Vector3(12.8f, -.0128f, 0f);
        Physics.SyncTransforms();
        Assert.That(MortarTrajectory.TryPredictManualCollision(start, target, 0f, 1f / speedMultiplier,
            Radius, null, _hits, MortarUpgradePayload.None, out RaycastHit preview, Vector3.up), Is.True);
        Assert.That(preview.collider, Is.SameAs(collider));
        Assert.That(Vector3.Distance(preview.point, contact), Is.LessThan(.0003f));
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset");
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        Assert.That(profile, Is.Not.Null);
        var shell = Keep(Object.Instantiate(profile.Mortar.ShellPrefab)).GetComponent<MortarShellImpact>();
        var sink = new RecordingSink();
        Set(shell, "_payload", MortarUpgradePayload.None);
        Set(shell, "_manualTargeting", true);
        Set(shell, "_manualLandingNormal", Vector3.up);
        Set(shell, "_presentationSink", sink);
        Set(shell, "_hasFeedbackTemplate", true);
        Set(shell, "_feedbackTemplate", new WeaponFeedbackContext(new WeaponInstance { Data = data, Level = 1 },
            WeaponFeedbackMode.Manual, speedMultiplier > 1f ? 1f : 0f, start, target - start));
        float travel = 1f / speedMultiplier;
        Invoke(shell, "Configure", start, target, travel, 0f, 1, 2.5f, 0f, 0f, Radius, null);
        Assert.That(shell.HasPredictedPresentationCollision, Is.True);
        Assert.That(Vector3.Distance(shell.PredictedPresentationCollisionPoint, preview.point), Is.LessThan(.0003f));
        Invoke(shell, "Tick", travel / 64f);
        Assert.That(sink.Cues.Count, Is.EqualTo(1));
        Assert.That(Vector3.Distance(sink.Cues[0].Position, preview.point), Is.LessThan(.0003f));
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase(-20f)]
    [TestCase(0f)]
    [TestCase(7.5f)]
    [TestCase(-4.59f)]
    public void AuthoredPlayerMovingAndJumping_OwnerCollidersDoNotMovePrediction(float pitch)
    {
        Collider floor = Cube("Saved sandbox-height floor", Anchor + new Vector3(0f, -.07f, 0f),
            new Vector3(100f, .12f, 100f));
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject player = Keep(Object.Instantiate(prefab));
        player.name = "Owned actual player hierarchy";
        Transform muzzle = player.GetComponent<PlayerWeaponMountController>().MainFirePoint;
        Assert.That(muzzle, Is.Not.Null);
        Assert.That(muzzle.IsChildOf(player.transform), Is.True);
        Collider[] ownerColliders = player.GetComponentsInChildren<Collider>(true);
        Assert.That(ownerColliders.Length, Is.GreaterThanOrEqualTo(3));
        var states = new bool[ownerColliders.Length];
        for (int i = 0; i < states.Length; i++) states[i] = ownerColliders[i].enabled;
        var camera = Keep(new GameObject("Owned mortar aim camera", typeof(Camera))).GetComponent<Camera>();
        camera.enabled = false;
        typeof(ReticleAimProvider).GetField("_aimCamera", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(player.GetComponent<ReticleAimProvider>(), camera);
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        Assert.That(data, Is.Not.Null);
        var weapon = new MortarWeapon(null, null, muzzle);
        weapon.Setup(new WeaponInstance { Data = data, Level = 1, State = WeaponState.Manual }, player.transform, null, null);
        Assert.That(weapon.ShellCollisionRadius, Is.EqualTo(Radius).Within(.0001f));
        var failures = new List<string>();
        var csv = new StringBuilder("pitch,yaw,muzzle_height,movement_x,movement_z,owner_colliders,origin_x,origin_y,origin_z,target_x,target_y,target_z,hit_collider,hit_x,hit_y,hit_z,normal_x,normal_y,normal_z,hit_distance\n");
        foreach (float yaw in new[] { 0f, 90f, -164f, 180f })
        foreach (float muzzleHeight in new[] { 1.2f, 1.5f, 1.8f, 3.5f })
        foreach (Vector3 movement in new[] { Vector3.zero, new Vector3(.025f, 0f, -.025f), new Vector3(3f, 0f, -2f) })
        {
            player.transform.SetPositionAndRotation(Anchor + movement, Quaternion.Euler(0f, yaw, 0f));
            player.transform.position += Vector3.up * (muzzleHeight - .01f - muzzle.position.y);
            camera.transform.SetPositionAndRotation(muzzle.position - player.transform.forward * 3f + Vector3.up,
                Quaternion.Euler(-pitch, yaw, 0f));
            RaycastHit enabledHit = default;
            bool enabledFound = false;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < states.Length; i++) ownerColliders[i].enabled = pass == 0 && states[i];
                Physics.SyncTransforms();
                bool resolved = weapon.TryResolveManualAim(camera.transform.forward, out MortarManualAimSolution aim);
                bool found = weapon.TryGetManualTerrainImpact(camera.transform.forward, out RaycastHit hit);
                string detail = $"pitch={pitch}; yaw={yaw}; height={muzzleHeight}; movement={movement}; ownerEnabled={pass == 0}; origin={aim.Origin}; selected={aim.SurfacePoint}; collider={hit.collider?.name}; hit={hit.point}; normal={hit.normal}";
                if (!resolved || !found || hit.collider != floor || Vector3.Distance(hit.point, aim.SurfacePoint) > .03f
                    || Mathf.Abs(hit.point.y + .01f) > .0002f || Vector3.Dot(hit.normal, Vector3.up) < .999f)
                    failures.Add(detail);
                if (pass == 0) { enabledHit = hit; enabledFound = found; }
                else if (found != enabledFound || (found && (hit.collider != enabledHit.collider
                    || Vector3.Distance(hit.point, enabledHit.point) > .0002f))) failures.Add("Owner-filter mismatch: " + detail);
                csv.Append(FormattableString.Invariant($"{pitch},{yaw},{muzzleHeight},{movement.x},{movement.z},{pass == 0},{aim.Origin.x},{aim.Origin.y},{aim.Origin.z},{aim.SurfacePoint.x},{aim.SurfacePoint.y},{aim.SurfacePoint.z},{hit.collider?.name},{hit.point.x},{hit.point.y},{hit.point.z},{hit.normal.x},{hit.normal.y},{hit.normal.z},{hit.distance}\n"));
            }
        }
        WriteDiagnostic($"owner-movement-pitch-{pitch}".Replace('.', '_'), csv.ToString());
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    [TestCase(.1f)]
    [TestCase(.17f)]
    public void GenuineLowMuzzleFloorOverlap_RemainsARealNearPlayerCollision(float clearance)
    {
        Collider floor = Cube("Actual low-muzzle floor", Anchor + new Vector3(0f, -.07f, 0f),
            new Vector3(60f, .12f, 60f));
        Transform owner = CreateOverlappingOwner(Anchor + new Vector3(0f, clearance - .01f, 0f));
        Physics.SyncTransforms();
        var tuning = new MortarTuning();
        Assert.That(MortarManualAim.TryResolve(owner.position, Vector3.forward, Vector3.forward,
            tuning, Radius, owner, _hits, out MortarManualAimSolution aim), Is.True);
        Assert.That(MortarTrajectory.TryPredictManualCollision(aim.Origin, aim.Target, aim.ArcHeight,
            tuning.MortarManualTravelTime, Radius, owner, _hits, MortarUpgradePayload.None,
            out RaycastHit hit, aim.SurfaceNormal), Is.True);
        Assert.That(hit.collider, Is.SameAs(floor));
        Assert.That(Vector3.Distance(hit.point, Anchor - Vector3.up * .01f), Is.LessThan(.001f));
        Assert.That(hit.normal.y, Is.GreaterThan(.999f));
        Assert.That(Vector3.Distance(hit.point, aim.SurfacePoint), Is.GreaterThan(17f),
            "A genuine launch obstruction must not be replaced by the distant selected target.");
    }

    [Test]
    public void GenuineNearMuzzleWall_RemainsCollisionWhileOwnerIsIgnored()
    {
        Cube("Ground beyond near wall", Anchor + new Vector3(0f, -.5f, 0f), new Vector3(60f, 1f, 60f));
        Vector3 start = Anchor + Vector3.up * 1.5f;
        Transform owner = CreateOverlappingOwner(start);
        Collider wall = Cube("Actual near-muzzle wall", start + Vector3.forward * .2f, new Vector3(2f, 4f, .1f));
        Physics.SyncTransforms();
        var tuning = new MortarTuning();
        Assert.That(MortarManualAim.TryResolve(start, Vector3.forward, Vector3.forward,
            tuning, Radius, owner, _hits, out MortarManualAimSolution aim), Is.True);
        Assert.That(MortarTrajectory.TryPredictManualCollision(aim.Origin, aim.Target, aim.ArcHeight,
            tuning.MortarManualTravelTime, Radius, owner, _hits, MortarUpgradePayload.None,
            out RaycastHit hit, aim.SurfaceNormal), Is.True);
        Assert.That(hit.collider, Is.SameAs(wall));
        Assert.That(Vector3.Distance(hit.point, start + Vector3.forward * .15f), Is.LessThan(.001f));
        Assert.That(Vector3.Dot(hit.normal, Vector3.back), Is.GreaterThan(.999f));
    }

    private Collider Quad(Vector3 position, Quaternion rotation, float halfSize)
    {
        var mesh = Keep(new Mesh { name = "Owned grazing contact quad" });
        mesh.vertices = new[]
        {
            new Vector3(-halfSize, 0f, -halfSize), new Vector3(-halfSize, 0f, halfSize),
            new Vector3(halfSize, 0f, halfSize), new Vector3(halfSize, 0f, -halfSize)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var terrain = Keep(new GameObject("Owned nonconvex contact surface", typeof(MeshCollider)));
        terrain.transform.SetPositionAndRotation(position, rotation);
        MeshCollider collider = terrain.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = false;
        return collider;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

    private Transform CreateOverlappingOwner(Vector3 position)
    {
        var owner = Keep(new GameObject("Ignored owner", typeof(CapsuleCollider)));
        owner.transform.position = position;
        var child = Keep(new GameObject("Ignored child collider", typeof(SphereCollider)));
        child.transform.SetParent(owner.transform, false);
        return owner.transform;
    }

    private Collider Cube(string name, Vector3 position, Vector3 scale)
    {
        var go = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        return go.GetComponent<Collider>();
    }

    private T Keep<T>(T value) where T : Object
    {
        _objects.Add(value);
        if (value is GameObject go) SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        return value;
    }

    private static void WriteDiagnostic(string name, string contents)
    {
        string directory = Environment.GetEnvironmentVariable("SCRAPWAVES_MORTAR_GROUND_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".csv"), contents);
    }
}
