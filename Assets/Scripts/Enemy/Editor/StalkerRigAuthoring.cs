#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Targeted migration from the validated eight rigid sections to an editable Generic rig.</summary>
public static class StalkerRigAuthoring
{
    public const string PrefabPath = "Assets/Prefabs/Stalker.prefab";
    public const string ModelPath = "Assets/Arte/Gusano.fbx";
    public const string AssetFolder = "Assets/Art/Enemies/Stalker/Rigged";
    public const string SourceFolder = "Assets/Art/Enemies/Stalker/ProceduralMeshes";
    public const string VisualName = "RigVisual";
    private const float Tolerance = 0.00002f;
    private static readonly string[] Names = { "Head", "Body01", "Body02", "Body03", "Body04", "Body05", "Body06", "Tail" };
    public static readonly string[] ClipNames = { "Emergence", "Ascent", "ApexDive", "Dive", "ImpactReburrow", "Death" };
    public static readonly float[] ClipDurations = { 0.30f, 0.80f, 0.60f, 0.80f, 0.35f, 1.10f };

    [MenuItem("Tools/ScrapWaves/Enemies/Build Stalker Generic Rig")]
    public static void Build()
    {
        RequireSafeEditor();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (root.transform.Find(VisualName) != null)
            {
                ValidateRoot(root);
                Debug.Log("PASS Stalker Generic rig already authored; no assets changed.");
                return;
            }
            // The source section assets remain the independent, read-only geometry input after
            // the old procedural scripts are retired. No dependency on their authoring utility.
            Require(root.GetComponents<CharacterController>().Length == 1 && root.GetComponents<CapsuleCollider>().Length == 0 &&
                    root.transform.Find("Head")?.GetComponent<EnemyDamageHitZone>() != null, "Expected the existing Stalker gameplay/collider layout.");
            Transform oldVisual = root.transform.Find("ArticulatedVisual");
            Require(oldVisual != null && oldVisual.childCount == 8, "Expected the validated eight-section visual.");
            var source = new Mesh[8];
            var pivots = new Vector3[8];
            var proxies = new Transform[8];
            Material[] materials = null;
            MeshRenderer sourceRenderer = null;
            for (int i = 0; i < 8; i++)
            {
                Transform part = oldVisual.GetChild(i);
                Require(part.name == Names[i] && NeutralRotationScale(part), "Unexpected source piece pose/order.");
                source[i] = AssetDatabase.LoadAssetAtPath<Mesh>(SourceFolder + "/" + Names[i] + ".asset");
                Require(source[i] != null && part.GetComponent<MeshFilter>().sharedMesh == source[i], "Wrong source mesh.");
                MeshRenderer renderer = part.GetComponent<MeshRenderer>();
                if (materials == null) { materials = renderer.sharedMaterials; sourceRenderer = renderer; }
                else Require(materials.SequenceEqual(renderer.sharedMaterials), "Source material assignments differ.");
                pivots[i] = root.transform.InverseTransformPoint(part.position);
                if (i > 0) proxies[i] = root.transform.Find("SegmentColliders/" + Names[i]);
            }
            Require(materials.Length == 4 && proxies.Skip(1).All(p => p != null), "Expected four materials and seven collision proxies.");
            var preserved = root.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform) && !IsLegacyProcedural(c) && !(c is EnemyDeathFeedback) && !(c is EnemyHitFeedback))
                .Concat(root.transform.Find("Head").GetComponents<Component>())
                .Concat(root.transform.Find("SegmentColliders").GetComponentsInChildren<Component>(true))
                .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            Vector3 rootPosition = root.transform.localPosition, rootScale = root.transform.localScale;
            Quaternion rootRotation = root.transform.localRotation;

            Transform visual = Child(root.transform, VisualName);
            visual.SetSiblingIndex(oldVisual.GetSiblingIndex());
            Transform rig = Child(visual, "Rig");
            Transform skeleton = Child(rig, "Skeleton");
            var bones = new Transform[8];
            for (int i = 0; i < 8; i++)
            {
                bones[i] = Child(i == 0 ? skeleton : bones[i - 1], Names[i]);
                bones[i].localPosition = i == 0 ? pivots[i] : pivots[i] - pivots[i - 1];
            }
            Transform meshObject = Child(rig, "Mesh");
            meshObject.gameObject.layer = sourceRenderer.gameObject.layer;
            SkinnedMeshRenderer skin = meshObject.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMaterials = materials;
            skin.bones = bones;
            skin.rootBone = skeleton;
            skin.quality = SkinQuality.Bone1;
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = sourceRenderer.shadowCastingMode;
            skin.receiveShadows = sourceRenderer.receiveShadows;
            skin.lightProbeUsage = sourceRenderer.lightProbeUsage;
            skin.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            skin.renderingLayerMask = sourceRenderer.renderingLayerMask;
            skin.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
            Mesh merged = Merge(source, pivots, bones, meshObject);
            CheckMergedRest(merged, source, pivots, bones, meshObject);
            float reach = merged.bounds.size.y + 4f;
            skin.localBounds = new Bounds(merged.bounds.center, Vector3.one * reach * 2f);

            EnsureFolder(AssetFolder);
            skin.sharedMesh = SaveAsset(merged, AssetFolder + "/StalkerSkin.asset");
            Avatar avatar = AvatarBuilder.BuildGenericAvatar(rig.gameObject, "Skeleton");
            avatar.name = "StalkerAvatar";
            Require(avatar.isValid && !avatar.isHuman, "Generic Avatar creation failed.");
            avatar = SaveAsset(avatar, AssetFolder + "/StalkerAvatar.asset");
            AnimationClip[] clips = AuthorClips(bones, rig);
            AnimatorController controller = AuthorController(clips);
            Animator animator = rig.gameObject.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.fireEvents = false;

            StalkerRigAnimation adapter = root.AddComponent<StalkerRigAnimation>();
            var serialized = new SerializedObject(adapter);
            SetReference(serialized, "_animator", animator);
            SetReference(serialized, "_visualRoot", visual);
            SetReference(serialized, "_bodyRenderer", skin);
            SetArray(serialized, "_bones", bones);
            SetArray(serialized, "_collisionSegments", proxies);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var feedback = new SerializedObject(root.GetComponent<EnemyHitFeedback>());
            SetReference(feedback, "_visualRoot", visual);
            SerializedProperty renderers = feedback.FindProperty("_renderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = skin;
            feedback.ApplyModifiedPropertiesWithoutUndo();

            Component procedural = root.GetComponents<Component>().SingleOrDefault(IsLegacyProcedural);
            Require(procedural != null, "Expected the old procedural component to replace.");
            Object.DestroyImmediate(procedural);
            EnemyDeathFeedback oldDeath = root.GetComponent<EnemyDeathFeedback>();
            if (oldDeath != null) Object.DestroyImmediate(oldDeath);
            Object.DestroyImmediate(oldVisual.gameObject);
            ValidateRoot(root);
            foreach (var item in preserved)
                Require(EditorJsonUtility.ToJson(item.Key) == item.Value, "Migration changed preserved component " + item.Key.GetType().Name);
            Require(Near(root.transform.localPosition, rootPosition) && Near(root.transform.localScale, rootScale) &&
                    Quaternion.Angle(root.transform.localRotation, rootRotation) <= Tolerance, "Root placement changed.");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            Require(saved, "Could not save Stalker rig prefab.");
            Debug.Log("PASS Stalker Generic rig: eight rigid-weight bones, one skin, six editable clips, seven preserved boxes; original meshes/FBX untouched.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static string Validate()
    {
        RequireSafeEditor();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { ValidateRoot(root); return "PASS Stalker Generic rig: source geometry/materials preserved; eight rigid bones, six clips, seven independent boxes, unchanged head/CC contract."; }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/ScrapWaves/Enemies/Validate Stalker Generic Rig")]
    public static void ValidateBatch() => Debug.Log(Validate());

    private static Mesh Merge(Mesh[] sources, Vector3[] pivots, Transform[] bones, Transform renderer)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var colors = new List<Color>();
        var weights = new List<BoneWeight>();
        var uvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
        int[] dimensions = new int[8];
        var triangles = Enumerable.Range(0, 4).Select(_ => new List<int>()).ToArray();
        bool hasNormals = sources[0].HasVertexAttribute(VertexAttribute.Normal);
        bool hasTangents = sources[0].HasVertexAttribute(VertexAttribute.Tangent);
        bool hasColors = sources[0].HasVertexAttribute(VertexAttribute.Color);
        for (int channel = 0; channel < 8; channel++)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
            dimensions[channel] = sources[0].HasVertexAttribute(attribute) ? sources[0].GetVertexAttributeDimension(attribute) : 0;
        }
        for (int part = 0; part < 8; part++)
        {
            Mesh source = sources[part];
            Require(source.isReadable && source.subMeshCount == 4 && source.blendShapeCount == 0, "Unexpected rigid source mesh.");
            Require(source.HasVertexAttribute(VertexAttribute.Normal) == hasNormals && source.HasVertexAttribute(VertexAttribute.Tangent) == hasTangents &&
                    source.HasVertexAttribute(VertexAttribute.Color) == hasColors, "Source vertex channels differ.");
            int offset = vertices.Count;
            vertices.AddRange(source.vertices.Select(v => v + pivots[part]));
            if (hasNormals) normals.AddRange(source.normals);
            if (hasTangents) tangents.AddRange(source.tangents);
            if (hasColors) colors.AddRange(source.colors);
            for (int i = 0; i < source.vertexCount; i++) weights.Add(new BoneWeight { boneIndex0 = part, weight0 = 1f });
            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                int dimension = source.HasVertexAttribute(attribute) ? source.GetVertexAttributeDimension(attribute) : 0;
                Require(dimension == dimensions[channel], "Source UV channels differ.");
                if (dimension == 0) continue;
                var values = new List<Vector4>(); source.GetUVs(channel, values); uvs[channel].AddRange(values);
            }
            for (int submesh = 0; submesh < 4; submesh++)
            {
                Require(source.GetTopology(submesh) == MeshTopology.Triangles, "Expected triangle submeshes.");
                triangles[submesh].AddRange(source.GetTriangles(submesh).Select(i => i + offset));
            }
        }
        var mesh = new Mesh { name = "StalkerSkin", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        if (hasNormals) mesh.SetNormals(normals);
        if (hasTangents) mesh.SetTangents(tangents);
        if (hasColors) mesh.SetColors(colors);
        for (int channel = 0; channel < 8; channel++)
        {
            if (dimensions[channel] == 0) continue;
            if (dimensions[channel] == 2) mesh.SetUVs(channel, uvs[channel].Select(v => (Vector2)v).ToList());
            else if (dimensions[channel] == 3) mesh.SetUVs(channel, uvs[channel].Select(v => (Vector3)v).ToList());
            else mesh.SetUVs(channel, uvs[channel]);
        }
        mesh.subMeshCount = 4;
        for (int submesh = 0; submesh < 4; submesh++) mesh.SetTriangles(triangles[submesh], submesh, false);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * renderer.localToWorldMatrix).ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static AnimationClip[] AuthorClips(Transform[] bones, Transform rig)
    {
        // Explicit pose tables, in head-to-tail order. These become ordinary editable .anim keys;
        // no trajectory sampling or procedural path solver remains in the visual animation.
        Pose[][] takes =
        {
            new[] { P(0, 0,-5,-4,-2,2,4,5,4), P(.10f, 0,-4,-6,-4,-1,2,4,5), P(.30f, 0,0,0,0,0,0,0,0) },
            new[] { P(0, 0,0,0,0,0,0,0,0), P(.20f, 0,1,2,3,2,1,0,-1), P(.40f, 0,0,-1,-2,-1,1,2,3),
                    P(.60f, 0,-1,-2,-1,1,2,1,0), P(.80f, 0,0,0,0,0,0,0,0) },
            new[] { P(0, 0,0,0,0,0,0,0,0), P(.16f, 0,6,5,2,-2,-3,-2,0), P(.36f, 0,3,5,6,3,-2,-3,-2), P(.60f, 0,0,1,2,3,3,2,1) },
            new[] { P(0, 0,0,1,2,3,3,2,1), P(.25f, 0,0,0,1,1,1,1,0), P(.50f, 0,0,-1,-1,0,1,1,1), P(.80f, 0,0,1,2,3,3,2,1) },
            new[] { P(0, 0,0,1,2,3,3,2,1), P(.12f, 0,-5,-4,-3,-1,1,2,2), P(.25f, 0,-2,-3,-2,-1,0,0,0), P(.35f, 0,0,0,0,0,0,0,0) },
            new[] { P(0, 0,0,0,0,0,0,0,0), P(.18f, -5,3,3,2,1,0,0,0), P(.60f, -12,7,7,6,5,4,3,2), P(1.10f, -20,8,8,8,7,6,5,4) }
        };
        var clips = new AnimationClip[6];
        string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int take = 0; take < takes.Length; take++)
        {
            var clip = new AnimationClip { name = ClipNames[take], frameRate = 60f, legacy = false };
            for (int bone = 0; bone < bones.Length; bone++)
            {
                var tracks = Enumerable.Range(0, properties.Length).Select(_ => new List<Keyframe>()).ToArray();
                for (int frame = 0; frame < takes[take].Length; frame++)
                {
                    Pose pose = takes[take][frame];
                    Vector3 position = bones[bone].localPosition;
                    // Small joint compression travels with the authored ascent pose, while panels remain rigid.
                    if (take == 1 && bone > 0) position.y *= 1f - Mathf.Abs(pose.Angles[bone]) * 0.0015f;
                    if (take == 0 && bone > 0) position.y *= 1f - Mathf.Abs(pose.Angles[bone]) * 0.001f;
                    if (take == 4 && bone > 0) position.y *= 1f - Mathf.Abs(pose.Angles[bone]) * 0.002f;
                    if (take == 5 && bone == 0) position.y -= new[] { 0f, .4f, 1.8f, 3f }[frame];
                    Quaternion rotation = Quaternion.Euler(pose.Angles[bone], 0f, 0f);
                    float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                    for (int component = 0; component < properties.Length; component++)
                        tracks[component].Add(new Keyframe(pose.Time, values[component], 0f, 0f));
                }
                string path = AnimationUtility.CalculateTransformPath(bones[bone], rig);
                for (int component = 0; component < properties.Length; component++)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), properties[component]), new AnimationCurve(tracks[component].ToArray()));
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = take == 1 || take == 3;
            settings.loopBlend = false;
            settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            clips[take] = SaveAsset(clip, AssetFolder + "/" + ClipNames[take] + ".anim");
        }
        return clips;
    }

    private static AnimatorController AuthorController(AnimationClip[] clips)
    {
        string path = AssetFolder + "/Stalker.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        Require(controller != null && controller.layers.Length == 1, "Unexpected existing Stalker controller.");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState state in machine.states) machine.RemoveState(state.state);
        AnimatorState dormant = machine.AddState("Dormant");
        dormant.writeDefaultValues = true;
        machine.defaultState = dormant;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimatorState state = machine.AddState(ClipNames[i], new Vector3(260f, 60f + i * 70f, 0f));
            state.motion = clips[i]; state.speed = 1f; state.writeDefaultValues = true;
        }
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(machine);
        AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    private static void ValidateRoot(GameObject root)
    {
        Require(!root.GetComponents<Component>().Any(IsLegacyProcedural) && root.GetComponent<EnemyDeathFeedback>() == null &&
                root.transform.Find("ArticulatedVisual") == null, "Old procedural/death visual components remain.");
        Require(root.GetComponents<CharacterController>().Length == 1 && root.GetComponents<CapsuleCollider>().Length == 0, "Root collider contract changed.");
        Transform head = root.transform.Find("Head");
        Require(head != null && head.GetComponent<SphereCollider>() != null && head.GetComponent<EnemyDamageHitZone>() != null, "Missing original Head hit zone.");
        Transform visual = root.transform.Find(VisualName), rig = root.transform.Find(VisualName + "/Rig");
        Require(visual != null && rig != null && Neutral(visual) && Neutral(rig), "Expected neutral RigVisual/Rig hierarchy.");
        Require(visual.GetComponentsInChildren<Collider>(true).Length == 0 && visual.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
            "Rig visual contains gameplay components.");
        Animator animator = rig.GetComponent<Animator>();
        Require(animator != null && animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman && !animator.applyRootMotion &&
                animator.cullingMode == AnimatorCullingMode.AlwaysAnimate, "Expected a valid Generic Animator with root motion disabled.");
        SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Require(skins.Length == 1 && visual.GetComponentsInChildren<MeshRenderer>(true).Length == 0, "Expected exactly one skinned renderer.");
        SkinnedMeshRenderer skin = skins[0];
        Require(skin.bones.Length == 8 && skin.sharedMaterials.Length == 4 && skin.sharedMesh != null, "Invalid skin bone/material layout.");
        GameObject originalModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        MeshRenderer originalRenderer = originalModel != null ? originalModel.GetComponentsInChildren<MeshRenderer>(true).SingleOrDefault() : null;
        Require(originalRenderer != null && skin.sharedMaterials.SequenceEqual(originalRenderer.sharedMaterials), "Original FBX material references changed.");
        Transform[] bones = skin.bones;
        var pivots = new Vector3[8];
        var sources = new Mesh[8];
        for (int i = 0; i < 8; i++)
        {
            Require(bones[i] != null && bones[i].name == Names[i] && NeutralRotationScale(bones[i]) &&
                    bones[i].parent == (i == 0 ? rig.Find("Skeleton") : bones[i - 1]), "Invalid skeleton rest hierarchy.");
            pivots[i] = rig.InverseTransformPoint(bones[i].position);
            sources[i] = AssetDatabase.LoadAssetAtPath<Mesh>(SourceFolder + "/" + Names[i] + ".asset");
            Require(sources[i] != null, "Missing preserved source section.");
        }
        CheckMergedRest(skin.sharedMesh, sources, pivots, bones, skin.transform);
        StalkerRigAnimation[] adapters = root.GetComponents<StalkerRigAnimation>();
        Require(adapters.Length == 1, "Expected one StalkerRigAnimation adapter.");
        var adapter = new SerializedObject(adapters[0]);
        Require(adapter.FindProperty("_animator").objectReferenceValue == animator && adapter.FindProperty("_visualRoot").objectReferenceValue == visual &&
                adapter.FindProperty("_bodyRenderer").objectReferenceValue == skin, "Wrong rig adapter references.");
        SerializedProperty boneReferences = adapter.FindProperty("_bones"), collisionReferences = adapter.FindProperty("_collisionSegments");
        Require(boneReferences.arraySize == 8 && collisionReferences.arraySize == 8 && collisionReferences.GetArrayElementAtIndex(0).objectReferenceValue == null,
            "Wrong bone/collision arrays.");
        Transform collisionRoot = root.transform.Find("SegmentColliders");
        Require(collisionRoot != null && Neutral(collisionRoot) && collisionRoot.childCount == 7 && root.GetComponentsInChildren<Collider>(true).Length == 9,
            "Expected seven independent collision boxes plus Head and CharacterController.");
        for (int i = 0; i < 8; i++)
        {
            Require(boneReferences.GetArrayElementAtIndex(i).objectReferenceValue == bones[i], "Wrong ordered bone reference.");
            if (i == 0) continue;
            Transform proxy = collisionRoot.GetChild(i - 1);
            BoxCollider box = proxy.GetComponent<BoxCollider>();
            Require(proxy.name == Names[i] && NeutralRotationScale(proxy) && Near(proxy.localPosition, pivots[i]) && box != null &&
                    Near(box.center, sources[i].bounds.center) && Near(box.size, sources[i].bounds.size) && proxy.GetComponents<Component>().Length == 2 &&
                    collisionReferences.GetArrayElementAtIndex(i).objectReferenceValue == proxy && box.GetComponentInParent<EnemyHealth>() == root.GetComponent<EnemyHealth>(),
                "Collision proxy rest geometry/reference changed.");
        }
        var feedback = new SerializedObject(root.GetComponent<EnemyHitFeedback>());
        Require(feedback.FindProperty("_visualRoot").objectReferenceValue == visual && feedback.FindProperty("_renderers").arraySize == 1 &&
                feedback.FindProperty("_renderers").GetArrayElementAtIndex(0).objectReferenceValue == skin, "Unsafe hit-feedback visual target.");
        var controller = animator.runtimeAnimatorController as AnimatorController;
        Require(controller != null && controller.layers.Length == 1 && controller.parameters.Length == 0, "Unexpected controller layout.");
        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
        Require(controller.animationClips.Select(c => c.name).Distinct().OrderBy(n => n).SequenceEqual(ClipNames.OrderBy(n => n)), "Expected exactly six clips and no spit animation.");
        for (int take = 0; take < ClipNames.Length; take++)
        {
            AnimatorState state = states.Select(s => s.state).SingleOrDefault(s => s.name == ClipNames[take]);
            AnimationClip clip = state != null ? state.motion as AnimationClip : null;
            Require(clip != null && !clip.legacy && Mathf.Abs(clip.length - ClipDurations[take]) < .001f && state.transitions.Length == 0, "Clip/state duration or transition mismatch.");
            Require(AnimationUtility.GetAnimationClipSettings(clip).loopTime == (take == 1 || take == 3), "Unexpected loop setting.");
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Require(bindings.Length > 0 && bindings.All(b => b.type == typeof(Transform) && b.path.StartsWith("Skeleton/Head", StringComparison.Ordinal) &&
                    (b.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal) || b.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal))),
                "Clip must animate only bone position/rotation; no root motion, scale or gameplay curves.");
            if (take != 5)
                foreach (EditorCurveBinding binding in bindings.Where(b => b.path == "Skeleton/Head"))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Require(curve.keys.All(k => Mathf.Abs(k.value - curve.keys[0].value) <= Tolerance), "Live clip changes the head bone.");
                }
        }
    }

    private static void CheckMergedRest(Mesh merged, Mesh[] sources, Vector3[] pivots, Transform[] bones, Transform renderer)
    {
        Require(merged.vertexCount == sources.Sum(m => m.vertexCount) && merged.subMeshCount == 4 && merged.bindposes.Length == 8,
            "Merged mesh coverage/bind poses differ.");
        Vector3[] actual = merged.vertices, normals = merged.normals;
        Vector4[] tangents = merged.tangents;
        Color[] colors = merged.colors;
        BoneWeight[] weights = merged.boneWeights;
        Matrix4x4[] bind = merged.bindposes;
        int offset = 0;
        var expectedTriangles = Enumerable.Range(0, 4).Select(_ => new List<int>()).ToArray();
        var expectedUvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
        for (int part = 0; part < 8; part++)
        {
            Mesh source = sources[part];
            Vector3[] vertices = source.vertices, sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Color[] sourceColors = source.colors;
            Matrix4x4 skinMatrix = renderer.worldToLocalMatrix * bones[part].localToWorldMatrix * bind[part];
            for (int i = 0; i < vertices.Length; i++)
            {
                int destination = offset + i;
                Vector3 expected = vertices[i] + pivots[part];
                Require(Near(actual[destination], expected) && Near(skinMatrix.MultiplyPoint3x4(actual[destination]), expected), "Skinned rest silhouette changed.");
                BoneWeight weight = weights[destination];
                Require(weight.boneIndex0 == part && Mathf.Abs(weight.weight0 - 1f) <= Tolerance && weight.weight1 == 0f && weight.weight2 == 0f && weight.weight3 == 0f,
                    "Mechanical section must have one rigid weight.");
                if (sourceNormals.Length > 0) Require(Near(normals[destination], sourceNormals[i]), "Normal changed.");
                if (sourceTangents.Length > 0) Require((tangents[destination] - sourceTangents[i]).sqrMagnitude <= Tolerance * Tolerance, "Tangent changed.");
                if (sourceColors.Length > 0) Require(((Vector4)colors[destination] - (Vector4)sourceColors[i]).sqrMagnitude <= Tolerance * Tolerance, "Vertex color changed.");
            }
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>(); source.GetUVs(channel, values); expectedUvs[channel].AddRange(values);
            }
            for (int submesh = 0; submesh < 4; submesh++) expectedTriangles[submesh].AddRange(source.GetTriangles(submesh).Select(i => i + offset));
            offset += source.vertexCount;
        }
        for (int channel = 0; channel < 8; channel++)
        {
            var values = new List<Vector4>(); merged.GetUVs(channel, values);
            Require(values.Count == expectedUvs[channel].Count, "UV channel coverage changed.");
            for (int i = 0; i < values.Count; i++) Require((values[i] - expectedUvs[channel][i]).sqrMagnitude <= Tolerance * Tolerance, "UV value changed.");
        }
        for (int submesh = 0; submesh < 4; submesh++) Require(merged.GetTriangles(submesh).SequenceEqual(expectedTriangles[submesh]), "Triangle/material assignment changed.");
    }

    private static T SaveAsset<T>(T created, string path) where T : Object
    {
        Require(path.StartsWith(AssetFolder + "/", StringComparison.Ordinal), "Unexpected asset target.");
        Object existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing == null) { AssetDatabase.CreateAsset(created, path); return created; }
        Require(existing is T && existing.name == created.name, "Unexpected existing asset at " + path);
        EditorUtility.CopySerialized(created, existing);
        Object.DestroyImmediate(created);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return (T)existing;
    }
    private static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name); go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false); return go.transform;
    }
    private static void SetReference(SerializedObject target, string name, Object value)
    { SerializedProperty property = target.FindProperty(name); Require(property != null, "Missing field " + name); property.objectReferenceValue = value; }
    private static void SetArray(SerializedObject target, string name, Transform[] values)
    {
        SerializedProperty property = target.FindProperty(name); Require(property != null, "Missing field " + name); property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static bool NeutralRotationScale(Transform t) => Quaternion.Angle(t.localRotation, Quaternion.identity) < Tolerance && Near(t.localScale, Vector3.one);
    private static bool IsLegacyProcedural(Component component) => component != null && component.GetType().Name == "StalkerProceduralBody";
    private static bool Neutral(Transform t) => NeutralRotationScale(t) && Near(t.localPosition, Vector3.zero);
    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= Tolerance * Tolerance;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RequireSafeEditor() => Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Run rig authoring in a settled editor outside Play Mode.");
    private static void EnsureFolder(string path)
    {
        Require(path == AssetFolder, "Unexpected authoring folder.");
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        Require(AssetDatabase.IsValidFolder(parent), "Expected existing Stalker art folder.");
        Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, "Rigged")), "Could not create rig asset folder.");
    }
    private static Pose P(float time, params float[] angles) { Require(angles.Length == 8, "Pose requires eight bone angles."); return new Pose { Time = time, Angles = angles }; }
    private sealed class Pose { public float Time; public float[] Angles; }
}
#endif
