using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks the imported skin rather than the Blender repair's selected vertex indices.</summary>
public sealed class PlayerTorsoSkinningTests
{
    private const string ModelPath = "Assets/Art/Player/PlaceholderAnimation/PlaceholderPlayer.fbx";
    private GameObject _model;

    [TearDown]
    public void Cleanup()
    {
        if (_model != null) Object.DestroyImmediate(_model);
    }

    [TestCase("upper_arm.L")]
    [TestCase("upper_arm.R")]
    [TestCase("thigh.L")]
    [TestCase("thigh.R")]
    public void CentralTorso_RemainsIntactWhenLimbsMoveIndependently(string limbName)
    {
        SkinData skin = LoadBindSkin();
        int[] torso = SelectCentralTorso(skin.BindPositions);
        Vector3[] before = Skin(skin);
        AssertBindBaseline(skin, before);

        // Exercise one limb at a time so opposite limbs cannot cancel erroneous
        // influences. Its descendants follow naturally through the real hierarchy.
        Transform limb = skin.Bones.Single(bone => bone.name == limbName);
        limb.localRotation *= Quaternion.Euler(72f, -37f, 49f);
        Vector3[] after = Skin(skin);

        Assert.That(MaximumDisplacement(before, after, Enumerable.Range(0, before.Length)),
            Is.GreaterThan(.05f), "Positive control: the imported limb skin must actually move.");
        foreach (int vertex in torso)
            Assert.That(Vector3.Distance(before[vertex], after[vertex]), Is.LessThanOrEqualTo(.0001f),
                $"{limbName} pulls central torso vertex at {skin.BindPositions[vertex]:F4}; " +
                "the black underlayer and grey chest must follow the spine, not passing arms or thighs.");
    }

    [Test]
    public void CentralTorso_StillRespondsToSpineBending()
    {
        SkinData skin = LoadBindSkin();
        int[] torso = SelectCentralTorso(skin.BindPositions);
        Vector3[] before = Skin(skin);
        AssertBindBaseline(skin, before);

        Transform spine = skin.Bones.Single(bone => bone.name == "spine.002");
        spine.localRotation *= Quaternion.Euler(32f, 17f, -13f);
        Vector3[] after = Skin(skin);
        int[] upper = torso.Where(vertex => skin.BindPositions[vertex].y > 1.24f).ToArray();
        int[] lower = torso.Where(vertex => skin.BindPositions[vertex].y < 1.20f).ToArray();

        Assert.That(MaximumDisplacement(before, after, upper), Is.GreaterThan(.005f),
            "The fix must preserve actual upper torso bending rather than freeze the mesh in bind pose.");
        Vector3 upperMotion = AverageDisplacement(before, after, upper);
        Vector3 lowerMotion = AverageDisplacement(before, after, lower);
        Assert.That((upperMotion - lowerMotion).magnitude, Is.GreaterThan(.003f),
            "Upper and lower torso samples must respond to the bent spine along its length.");
    }

    private sealed class SkinData
    {
        public Transform Root;
        public Transform[] Bones;
        public Matrix4x4[] BindPoses;
        public Vector3[] Vertices;
        public Vector3[] BindPositions;
        public byte[] Counts;
        public BoneWeight1[] Weights;
    }

    private SkinData LoadBindSkin()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Assert.That(asset, Is.Not.Null);
        _model = Object.Instantiate(asset);
        _model.hideFlags = HideFlags.HideAndDontSave;
        foreach (Animator animator in _model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        SkinnedMeshRenderer renderer = _model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
        Mesh mesh = renderer.sharedMesh;
        Assert.That(mesh, Is.Not.Null);
        Transform[] bones = renderer.bones;
        Matrix4x4[] bindPoses = mesh.bindposes;
        Assert.That(bindPoses.Length, Is.EqualTo(bones.Length));
        Assert.That(bones.All(bone => bone != null), Is.True);

        // An FBX may open on its first animation frame. Recover the bind pose
        // from the imported inverse bind matrices, parent before child.
        Matrix4x4[] boneWorld = bindPoses.Select(bind => renderer.transform.localToWorldMatrix * bind.inverse).ToArray();
        foreach (int index in Enumerable.Range(0, bones.Length).OrderBy(index => Depth(bones[index])))
            bones[index].SetPositionAndRotation(boneWorld[index].GetColumn(3), boneWorld[index].rotation);

        using var meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
        using var vertexData = new NativeArray<Vector3>(meshData[0].vertexCount, Allocator.Temp);
        meshData[0].GetVertices(vertexData);
        using var countData = mesh.GetBonesPerVertex();
        using var weightData = mesh.GetAllBoneWeights();
        Vector3[] vertices = vertexData.ToArray();
        Matrix4x4 meshToModel = _model.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        var skin = new SkinData
        {
            Root = _model.transform,
            Bones = bones,
            BindPoses = bindPoses,
            Vertices = vertices,
            BindPositions = vertices.Select(meshToModel.MultiplyPoint3x4).ToArray(),
            Counts = countData.ToArray(),
            Weights = weightData.ToArray()
        };
        Assert.That(skin.Counts.Length, Is.EqualTo(vertices.Length));
        Assert.That(skin.Counts.Sum(count => (int)count), Is.EqualTo(skin.Weights.Length));
        return skin;
    }

    private static int Depth(Transform bone)
    {
        int depth = 0;
        while (bone.parent != null) { depth++; bone = bone.parent; }
        return depth;
    }

    private static int[] SelectCentralTorso(Vector3[] positions)
    {
        int[] selected = Enumerable.Range(0, positions.Length).Where(index =>
        {
            Vector3 point = positions[index];
            return Mathf.Abs(point.x) < .17f && point.y >= 1.02f && point.y <= 1.40f;
        }).ToArray();
        Assert.That(selected.Length, Is.GreaterThanOrEqualTo(12), "The selection must contain actual central torso geometry.");
        Assert.That(selected.Count(index => positions[index].y < 1.20f), Is.GreaterThanOrEqualTo(4),
            "Sample the lower black torso underlayer, not only the upper chest.");
        Assert.That(selected.Count(index => positions[index].y > 1.24f), Is.GreaterThanOrEqualTo(4),
            "Sample the upper torso/chest region as well as the lower underlayer.");
        return selected;
    }

    private static void AssertBindBaseline(SkinData skin, Vector3[] positions)
    {
        Assert.That(skin.BindPositions.Max(point => point.y), Is.GreaterThan(1.5f),
            "Model coordinates must use the imported player's metre scale and feet near zero.");
        Assert.That(skin.BindPositions.Min(point => point.y), Is.LessThan(.15f));
        for (int index = 0; index < positions.Length; index++)
            Assert.That(Vector3.Distance(positions[index], skin.BindPositions[index]), Is.LessThan(.0001f),
                "CPU skinning must reproduce the imported bind geometry before any test deformation.");
    }

    private static Vector3[] Skin(SkinData skin)
    {
        Matrix4x4[] matrices = Enumerable.Range(0, skin.Bones.Length)
            .Select(index => skin.Root.worldToLocalMatrix * skin.Bones[index].localToWorldMatrix * skin.BindPoses[index])
            .ToArray();
        var positions = new Vector3[skin.Vertices.Length];
        int offset = 0;
        for (int vertex = 0; vertex < positions.Length; vertex++)
        {
            float totalWeight = 0f;
            for (int influence = 0; influence < skin.Counts[vertex]; influence++)
            {
                BoneWeight1 weight = skin.Weights[offset++];
                Assert.That(weight.boneIndex, Is.InRange(0, matrices.Length - 1));
                positions[vertex] += matrices[weight.boneIndex].MultiplyPoint3x4(skin.Vertices[vertex]) * weight.weight;
                totalWeight += weight.weight;
            }
            Assert.That(totalWeight, Is.EqualTo(1f).Within(.0001f), "Each sampled vertex needs valid normalized weights.");
        }
        return positions;
    }

    private static float MaximumDisplacement(Vector3[] before, Vector3[] after, IEnumerable<int> vertices)
        => vertices.Max(index => Vector3.Distance(before[index], after[index]));

    private static Vector3 AverageDisplacement(Vector3[] before, Vector3[] after, int[] vertices)
    {
        Vector3 total = Vector3.zero;
        foreach (int vertex in vertices) total += after[vertex] - before[vertex];
        return total / vertices.Length;
    }
}
