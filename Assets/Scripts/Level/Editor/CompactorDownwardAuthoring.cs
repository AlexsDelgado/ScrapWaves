using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Derives a descending mechanical take without altering the artist's FBX or its materials.</summary>
public static class CompactorDownwardAuthoring
{
    public const float DownwardTravel = 2.5f;
    private const float PistonAnchorHeight = 4.7f;
    private const float PistonAttachmentHeight = 2.62f;
    private const string RampMeshPath = "Assets/Art/Level/CompactorDoor/Compactor_ThresholdRamp.asset";
    private static readonly Bounds Aperture = new(new Vector3(0f, 1.67f, .36f), new Vector3(2.52f, 2.65f, .24f));

    public static void BuildOpeningClip()
    {
        AnimationClip source = CompactorEscapeSetupEditor.ImportedOpeningClip;
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CompactorEscapeSetupEditor.ModelPath));
        try
        {
            Transform lift = FindPart(instance.transform, "Door_Lift");
            Transform[] pistons = { FindPart(instance.transform, "Piston_1"), FindPart(instance.transform, "Piston_2") };
            source.SampleAnimation(instance, 0f);
            Vector3 liftClosed = lift.localPosition;
            float closedHeight = lift.position.y;
            Vector3[] pistonPositions = pistons.Select(p => p.position).ToArray();
            Vector3[] pistonScales = pistons.Select(p => p.localScale).ToArray();
            int[] pistonAxes = pistons.Select(LongitudinalAxis).ToArray();
            float[] pistonLengths = pistons.Select((p, i) => p.GetComponent<MeshFilter>().sharedMesh.bounds.size[pistonAxes[i]]).ToArray();
            source.SampleAnimation(instance, source.length);
            float originalTravel = lift.position.y - closedHeight;
            if (originalTravel <= .01f)
                throw new InvalidOperationException("The source compactor take must contain an upward translation.");

            AnimationClip authored = Object.Instantiate(source);
            authored.name = "OpenDown";
            authored.legacy = false;
            authored.wrapMode = WrapMode.ClampForever;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(authored);
            settings.loopTime = false;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(authored, settings);

            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(source.length * source.frameRate) + 1);
            var positions = new Dictionary<Transform, Vector3[]> { [lift] = new Vector3[sampleCount] };
            var scales = new Dictionary<Transform, Vector3[]>();
            foreach (Transform piston in pistons)
            {
                positions[piston] = new Vector3[sampleCount];
                scales[piston] = new Vector3[sampleCount];
            }
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float time = source.length * sample / (sampleCount - 1f);
                source.SampleAnimation(instance, time);
                float progress = Mathf.Clamp01((lift.position.y - closedHeight) / originalTravel);
                float travel = DownwardTravel * progress;
                positions[lift][sample] = liftClosed + lift.parent.InverseTransformVector(Vector3.down * travel);
                float movingEnd = PistonAttachmentHeight - travel;
                for (int i = 0; i < pistons.Length; i++)
                {
                    Vector3 center = pistonPositions[i];
                    center.y = (PistonAnchorHeight + movingEnd) * .5f;
                    positions[pistons[i]][sample] = pistons[i].parent.InverseTransformPoint(center);
                    Vector3 scale = pistonScales[i];
                    scale[pistonAxes[i]] = (PistonAnchorHeight - movingEnd) / pistonLengths[i];
                    scales[pistons[i]][sample] = scale;
                }
            }
            foreach (var entry in positions)
                SetVectorCurves(authored, instance.transform, entry.Key, "m_LocalPosition", entry.Value, source.length);
            foreach (var entry in scales)
                SetVectorCurves(authored, instance.transform, entry.Key, "m_LocalScale", entry.Value, source.length);

            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(CompactorEscapeSetupEditor.DownwardClipPath);
            if (existing == null)
                AssetDatabase.CreateAsset(authored, CompactorEscapeSetupEditor.DownwardClipPath);
            else
            {
                EditorUtility.CopySerialized(authored, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(authored);
            }
        }
        finally { Object.DestroyImmediate(instance); }
    }

    public static void ConfigurePrefab(GameObject root)
    {
        ExitDoor door = root.GetComponent<ExitDoor>();
        CompactorDoorPresentation presentation = root.GetComponent<CompactorDoorPresentation>();
        Animator animator = root.GetComponentInChildren<Animator>();
        Transform lift = FindPart(animator.transform, "Door_Lift");
        AnimationClip clip = CompactorEscapeSetupEditor.OpeningClip;
        if (clip == null) throw new InvalidOperationException("Build the downward opening take before configuring the prefab.");
        presentation.Dispose();
        clip.SampleAnimation(animator.gameObject, 0f);
        BoxCollider panel = root.transform.Find("Closed Door Blocker").GetComponent<BoxCollider>();
        panel.transform.localPosition = Vector3.zero;
        // Include the edge strips as well as the central sheet. At gameplay scale the
        // original .01-unit discrepancy becomes a visible .3-unit clipping allowance.
        panel.center = new Vector3(0f, 1.57f, .36f);
        panel.size = new Vector3(2.54f, 2.48f, .24f);
        panel.enabled = true;
        Renderer suction = root.transform.Find("Black Suction Opening").GetComponent<Renderer>();
        presentation.Configure(door, animator, clip, suction, panel);
        presentation.ConfigureMovingPanel(lift);

        SetBox(root.transform, "Entrance Header Collision", new Vector3(0f, 4.195f, .05f), new Vector3(2.52f, 2.4f, .7f));
        SetBox(root.transform, "Chamber Floor Collision", new Vector3(0f, .1725f, -.185f), new Vector3(2.7f, .345f, 1.67f));
        SetBox(root.transform, "Chamber Roof Collision", new Vector3(0f, 3.08f, -.405f), new Vector3(2.7f, .17f, 1.39f));
        SetBox(root.transform, "Left Chamber Collision", new Vector3(-1.43f, 1.6f, -.405f), new Vector3(.16f, 3.05f, 1.39f));
        SetBox(root.transform, "Right Chamber Collision", new Vector3(1.43f, 1.6f, -.405f), new Vector3(.16f, 3.05f, 1.39f));
        CreateRamp(root.transform);
        CreateUpperActuators(root.transform, animator.transform);

        ExitDoorTraversal traversal = root.GetComponent<ExitDoorTraversal>();
        if (traversal == null) traversal = root.AddComponent<ExitDoorTraversal>();
        traversal.Configure(door, panel, Aperture);
    }

    private static void SetVectorCurves(AnimationClip clip, Transform root, Transform part, string property, Vector3[] values, float duration)
    {
        string path = AnimationUtility.CalculateTransformPath(part, root);
        for (int axis = 0; axis < 3; axis++)
        {
            Keyframe[] keys = values.Select((value, i) => new Keyframe(duration * i / (values.Length - 1f), value[axis])).ToArray();
            AnimationCurve curve = new(keys);
            for (int key = 0; key < keys.Length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property + "." + "xyz"[axis]), curve);
        }
    }

    private static int LongitudinalAxis(Transform piston)
    {
        Vector3 size = piston.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        int axis = size.x > size.y ? 0 : 1;
        if (size.z > size[axis]) axis = 2;
        Vector3 direction = Vector3.zero;
        direction[axis] = 1f;
        if (Mathf.Abs(Vector3.Dot(piston.TransformDirection(direction), Vector3.up)) < .99f)
            throw new InvalidOperationException("Compactor piston must remain aligned with the vertical guides.");
        return axis;
    }

    private static void CreateUpperActuators(Transform root, Transform model)
    {
        Material steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Level/CompactorDoor/Materials/Frame_Steel.mat");
        foreach (string pistonName in new[] { "Piston_1", "Piston_2" })
        {
            Vector3 position = root.InverseTransformPoint(FindPart(model, pistonName).position);
            // The original lower housings now guide the descending rods. Upper sleeves supply
            // the fixed actuator anchor so every animated rod keeps a positive mechanical length.
            Transform sleeve = FindOrCreatePrimitive(root, pistonName + " Upper Actuator", PrimitiveType.Cylinder);
            sleeve.localPosition = new Vector3(position.x, 4.975f, position.z);
            sleeve.localRotation = Quaternion.identity;
            sleeve.localScale = new Vector3(.21f, .275f, .21f);
            sleeve.GetComponent<Renderer>().sharedMaterial = steel;
            Transform mount = FindOrCreatePrimitive(root, pistonName + " Upper Mount", PrimitiveType.Cube);
            mount.localPosition = new Vector3(Mathf.Sign(position.x) * 1.57f, 5.15f, position.z);
            mount.localRotation = Quaternion.identity;
            mount.localScale = new Vector3(.5f, .16f, .35f);
            mount.GetComponent<Renderer>().sharedMaterial = steel;
        }
    }

    private static Transform FindOrCreatePrimitive(Transform root, string name, PrimitiveType type)
    {
        Transform existing = root.Find(name);
        if (existing != null) return existing;
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }

    private static void CreateRamp(Transform root)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RampMeshPath);
        bool createAsset = mesh == null;
        if (createAsset) mesh = new Mesh { name = "Compactor Threshold Ramp" };
        mesh.Clear();
        mesh.vertices = new[]
        {
            new Vector3(-1.26f, .345f, .65f), new Vector3(1.26f, .345f, .65f),
            new Vector3(-1.26f, -.07f, 2.05f), new Vector3(1.26f, -.07f, 2.05f),
            new Vector3(-1.26f, -.12f, .65f), new Vector3(1.26f, -.12f, .65f),
            new Vector3(-1.26f, -.12f, 2.05f), new Vector3(1.26f, -.12f, 2.05f)
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (createAsset) AssetDatabase.CreateAsset(mesh, RampMeshPath);
        else EditorUtility.SetDirty(mesh);
        Transform ramp = root.Find("Threshold Approach Ramp");
        if (ramp == null)
        {
            ramp = new GameObject("Threshold Approach Ramp").transform;
            ramp.SetParent(root, false);
        }
        MeshFilter filter = ramp.GetComponent<MeshFilter>();
        if (filter == null) filter = ramp.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = ramp.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = ramp.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Level/CompactorDoor/Materials/Dark_Steel.mat");
        renderer.shadowCastingMode = ShadowCastingMode.On;
        MeshCollider rampCollider = ramp.GetComponent<MeshCollider>();
        if (rampCollider == null) rampCollider = ramp.gameObject.AddComponent<MeshCollider>();
        rampCollider.sharedMesh = mesh;
        Transform threshold = FindOrCreatePrimitive(root, "Threshold Floor Plate", PrimitiveType.Cube);
        threshold.localPosition = new Vector3(0f, .3175f, -.185f);
        threshold.localScale = new Vector3(2.7f, .055f, 1.67f);
        threshold.GetComponent<Renderer>().sharedMaterial = renderer.sharedMaterial;
    }

    private static void SetBox(Transform root, string name, Vector3 center, Vector3 size)
    {
        Transform child = root.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(root, false);
        }
        BoxCollider box = child.GetComponent<BoxCollider>();
        if (box == null) box = child.gameObject.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.isTrigger = false;
    }

    private static Transform FindPart(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).Single(p => p.name == name);
}
