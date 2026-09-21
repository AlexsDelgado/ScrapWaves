using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class PlayerWearableMountFitTests
{
    private const string FlamePrefabPath = "Assets/Prefabs/Weapons/Wearables/FlamethrowerWearable.prefab";
    private readonly List<GameObject> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int index = _cleanup.Count - 1; index >= 0; index--)
            if (_cleanup[index] != null) Object.DestroyImmediate(_cleanup[index]);
        _cleanup.Clear();
    }

    [Test]
    public void ProductionFlamethrower_MuzzleIsCenteredAtTheVentOpening()
    {
        GameObject wearable = Instantiate(FlamePrefabPath);
        // A rotated instance catches world-axis bounds calculations accidentally
        // being used in place of the physical outlet's own outward direction.
        wearable.transform.SetPositionAndRotation(new Vector3(1.2f, -.3f, 2.1f), Quaternion.Euler(17f, 43f, -12f));
        AssertVentMuzzle(wearable);
    }

    [Test]
    public void ProductionFlamethrower_NeutralAimPointsTheVentForward()
    {
        GameObject actor = Instantiate("Assets/Prefabs/player.prefab");
        actor.transform.SetPositionAndRotation(new Vector3(1.3f, -.6f, 2f), Quaternion.Euler(0f, 71f, 0f));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        Transform visual = actor.transform.Find("PlaceholderPlayerVisual");
        Assert.That(visual, Is.Not.Null);
        Animator animator = visual.GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null);
        animator.runtimeAnimatorController = null;

        PlayerWeaponMountController mounts = actor.GetComponent<PlayerWeaponMountController>();
        PlayerWearableSurfaceFollower follower = actor.GetComponent<PlayerWearableSurfaceFollower>();
        Assert.That(mounts, Is.Not.Null);
        Assert.That(follower, Is.Not.Null);
        Transform socket = mounts.GetAnimatedSocket(WeaponType.Flamethrower);
        Assert.That(socket, Is.Not.Null);
        GameObject wearable = Instantiate(FlamePrefabPath);
        wearable.transform.SetParent(socket, false);

        AnimationClip aim = AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath)
            .OfType<AnimationClip>().Single(clip => clip.name == "Aim");
        aim.SampleAnimation(visual.gameObject, aim.length * .25f);
        follower.EvaluateAttachments();

        Transform muzzle = wearable.GetComponent<AutomaticWeaponMount>().Muzzle;
        Assert.That(Vector3.Dot(muzzle.forward, actor.transform.forward), Is.GreaterThan(.98f),
            "The automatic flame vent should face forward in the normal held pose, rather than upward across the forearm.");
        AssertVentMuzzle(wearable);
    }

    [TestCase("Aim")]
    [TestCase("Idle")]
    [TestCase("MoveForward")]
    [TestCase("Slide")]
    public void ProductionCannon_MountPlateStaysSeatedOnAnimatedShoulder(string clipName)
    {
        GameObject actor = Instantiate("Assets/Prefabs/player.prefab");
        actor.transform.SetPositionAndRotation(new Vector3(-.7f, .4f, 1.2f), Quaternion.Euler(0f, -37f, 0f));
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        Transform visual = actor.transform.Find("PlaceholderPlayerVisual");
        visual.GetComponent<Animator>().runtimeAnimatorController = null;
        SkinnedMeshRenderer skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
        PlayerWearableSurfaceFollower follower = actor.GetComponent<PlayerWearableSurfaceFollower>();
        Transform socket = actor.GetComponent<PlayerWeaponMountController>().GetAnimatedSocket(WeaponType.AutomaticCannon);
        GameObject wearable = Instantiate("Assets/Prefabs/Weapons/Wearables/AutomaticCannonWearable.prefab");
        wearable.transform.SetParent(socket, false);
        MeshFilter plate = wearable.GetComponentsInChildren<MeshFilter>(true)
            .Single(filter => filter.name == "Shoulder mount top plate");
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath)
            .OfType<AnimationClip>().Single(candidate => candidate.name == clipName);

        foreach (Vector3 scale in new[] { Vector3.one, new Vector3(1.2f, .8f, 1.1f) })
        foreach (float phase in new[] { 0f, .25f, .67f })
        {
            actor.transform.localScale = scale;
            clip.SampleAnimation(visual.gameObject, clip.length * phase);
            follower.EvaluateAttachments();
            // Derive the underside from the actual imported plate, without using
            // the follower's cached seat point or its selected skin anchors.
            Vector3 underside = PlateUndersideCenter(plate, wearable.transform);
            float distance = DistanceToSkinnedShoulder(skin, underside);
            Assert.That(distance, Is.LessThan(.001f),
                $"{clipName} at {phase:P0}: the cannon plate must contact the animated shoulder mesh (gap={distance:F6} m).");
        }
    }

    private GameObject Instantiate(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        GameObject instance = Object.Instantiate(prefab);
        _cleanup.Add(instance);
        return instance;
    }

    private static void AssertVentMuzzle(GameObject wearable)
    {
        Transform model = wearable.transform.Find("ACBracelet(Flame)");
        Assert.That(model, Is.Not.Null);
        MeshFilter vent = model.GetComponentsInChildren<MeshFilter>(true)
            .Single(filter => filter.name == "Rounded ivory vent rim");
        // The imported source opens along -X before the wearable's authored
        // model rotation. Follow that actual outlet axis through all parents.
        Vector3 outward = model.TransformDirection(Vector3.left).normalized;
        Vector3 center = vent.transform.TransformPoint(vent.sharedMesh.bounds.center);
        using var data = MeshUtility.AcquireReadOnlyMeshData(vent.sharedMesh);
        using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
        data[0].GetVertices(vertices);
        float outerDepth = float.NegativeInfinity;
        foreach (Vector3 vertex in vertices)
            outerDepth = Mathf.Max(outerDepth, Vector3.Dot(vent.transform.TransformPoint(vertex) - center, outward));
        Vector3 opening = center + outward * outerDepth;

        Transform muzzle = wearable.GetComponent<AutomaticWeaponMount>().Muzzle;
        Vector3 fromOpening = muzzle.position - opening;
        Assert.That(Vector3.ProjectOnPlane(fromOpening, outward).magnitude, Is.LessThan(.0001f),
            "The fire point must be centered in the vent, not on a housing corner.");
        Assert.That(Vector3.Dot(fromOpening, outward), Is.EqualTo(.003f).Within(.0001f),
            "The fire point sits 3 mm outside the vent lip.");
        Assert.That(Vector3.Dot(muzzle.forward, outward), Is.GreaterThan(.9999f),
            "The fire point must face out of the physical vent opening.");
    }

    private static Vector3 PlateUndersideCenter(MeshFilter plate, Transform mount)
    {
        using var data = MeshUtility.AcquireReadOnlyMeshData(plate.sharedMesh);
        using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
        data[0].GetVertices(vertices);
        Vector3 min = Vector3.one * float.PositiveInfinity;
        Vector3 max = Vector3.one * float.NegativeInfinity;
        foreach (Vector3 vertex in vertices)
        {
            Vector3 local = mount.InverseTransformPoint(plate.transform.TransformPoint(vertex));
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        return mount.TransformPoint(new Vector3((min.x + max.x) * .5f, min.y, (min.z + max.z) * .5f));
    }

    private static float DistanceToSkinnedShoulder(SkinnedMeshRenderer skin, Vector3 point)
    {
        Mesh mesh = skin.sharedMesh;
        using var data = MeshUtility.AcquireReadOnlyMeshData(mesh);
        using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
        data[0].GetVertices(vertices);
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bindposes = mesh.bindposes;
        Transform[] bones = skin.bones;
        var skinned = new Vector3[vertices.Length];
        var shoulder = new bool[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
        {
            BoneWeight weight = weights[index];
            Vector3 source = vertices[index];
            float shoulderInfluence = 0f;
            void Apply(int bone, float influence)
            {
                if (influence <= 0f) return;
                skinned[index] += bones[bone].TransformPoint(bindposes[bone].MultiplyPoint3x4(source)) * influence;
                if (bones[bone].name == "shoulder.R" || bones[bone].name == "upper_arm.R") shoulderInfluence += influence;
            }
            Apply(weight.boneIndex0, weight.weight0);
            Apply(weight.boneIndex1, weight.weight1);
            Apply(weight.boneIndex2, weight.weight2);
            Apply(weight.boneIndex3, weight.weight3);
            shoulder[index] = shoulderInfluence >= .3f;
        }

        float nearest = float.PositiveInfinity;
        for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
        {
            using var indices = new NativeArray<int>(data[0].GetSubMesh(submesh).indexCount, Allocator.Temp);
            data[0].GetIndices(indices, submesh);
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                if (!shoulder[a] || !shoulder[b] || !shoulder[c]) continue;
                Vector3 ab = skinned[b] - skinned[a], ac = skinned[c] - skinned[a];
                Vector3 normal = Vector3.Cross(ab, ac);
                if (normal.sqrMagnitude < 1e-12f) continue;
                normal.Normalize();
                float planeDistance = Vector3.Dot(point - skinned[a], normal);
                Vector3 projected = point - normal * planeDistance;
                // The plane alone would also accept points outside the triangle.
                // Require the projection to lie within the actual skin polygon.
                Vector3 fromA = projected - skinned[a];
                float d00 = Vector3.Dot(ab, ab), d01 = Vector3.Dot(ab, ac), d11 = Vector3.Dot(ac, ac);
                float d20 = Vector3.Dot(fromA, ab), d21 = Vector3.Dot(fromA, ac);
                float denominator = d00 * d11 - d01 * d01;
                float v = (d11 * d20 - d01 * d21) / denominator;
                float w = (d00 * d21 - d01 * d20) / denominator;
                const float edgeTolerance = .0001f;
                if (v >= -edgeTolerance && w >= -edgeTolerance && v + w <= 1f + edgeTolerance)
                    nearest = Mathf.Min(nearest, Mathf.Abs(planeDistance));
            }
        }
        return nearest;
    }
}
