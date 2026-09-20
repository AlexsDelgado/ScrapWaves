using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Offline geometric and visual inspection of the production animated wearables.</summary>
public static class WearableAnimationClearanceAudit
{
    public static void RefitAndRun()
    {
        PlaceholderPlayerAnimationBuilder.RefitWearableSockets();
        Run();
    }
    private const string Output = ".utmp/player-animation/clipping";
    private sealed class Part
    {
        public WeaponType Type;
        public MeshFilter Filter;
        public Vector3[] Vertices;
        public (int, int)[] Edges;
        public Renderer Renderer;
    }

    [MenuItem("Tools/ScrapWaves/Audit Animated Wearable Clearance")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.NewPreviewScene();
        Color ambient = RenderSettings.ambientLight;
        var meshes = new List<Mesh>();
        try
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPlayerAnimationBuilder.PlayerPath), scene);
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var visual = player.GetComponent<PlayerAnimationDriver>().RigAnimator.gameObject;
            visual.GetComponent<Animator>().runtimeAnimatorController = null;
            var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
            var baked = new Mesh(); meshes.Add(baked);
            var snapshot = new GameObject("Evaluated player skin"); snapshot.transform.SetParent(skin.transform, false);
            snapshot.AddComponent<MeshFilter>().sharedMesh = baked;
            snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false;
            var mounts = player.GetComponent<PlayerWeaponMountController>();
            var catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>("Assets/Resources/WearableWeaponMounts.asset");
            var parts = new List<Part>();
            foreach (var def in catalog.Definitions)
            {
                var wearable = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab, scene);
                wearable.transform.SetParent(mounts.GetAnimatedSocket(def.Type), false);
                foreach (var filter in wearable.GetComponentsInChildren<MeshFilter>())
                {
                    var triangles = filter.sharedMesh.triangles;
                    var edges = new HashSet<(int, int)>();
                    void Edge(int a, int b) => edges.Add(a < b ? (a, b) : (b, a));
                    for (int i = 0; i < triangles.Length; i += 3)
                    { Edge(triangles[i], triangles[i+1]); Edge(triangles[i+1], triangles[i+2]); Edge(triangles[i+2], triangles[i]); }
                    parts.Add(new Part { Type = def.Type, Filter = filter, Vertices = filter.sharedMesh.vertices, Edges = edges.ToArray(), Renderer = filter.GetComponent<Renderer>() });
                }
            }
            var cameraObject = new GameObject("Clearance inspection camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 1.15f; camera.nearClipPlane = .01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f,.12f,.15f);
            var lightObject = new GameObject("Clearance key light"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(40,-25,0); RenderSettings.ambientLight = new Color(.65f,.65f,.65f);
            var clips = PlaceholderPlayerAnimationBuilder.Clips();
            // Initialise the imported skin's render data before the first CPU bake.
            // Otherwise a fresh preview instance can report an empty first pose.
            clips.Single(clip => clip.name == "Idle").SampleAnimation(visual, 0f);
            var warmup = RenderTexture.GetTemporary(16, 16, 24);
            try
            {
                skin.enabled = true;
                camera.targetTexture = warmup;
                camera.Render();
            }
            finally
            {
                skin.enabled = false;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(warmup);
            }
            var aim = clips.Single(c => c.name == "Aim");
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(PlaceholderPlayerAnimationBuilder.MaskPath);
            var layered = typeof(PlaceholderPlayerAnimationBuilder).GetMethod("SampleLayeredPose", BindingFlags.Static|BindingFlags.NonPublic);
            var maximum = catalog.Definitions.ToDictionary(d => d.Type, _ => -1);
            var worst = new Dictionary<WeaponType, string>();
            var csv = new StringBuilder("pose,phase,weapon,part,crossing_edges\n");
            foreach (bool armed in new[] { false, true })
            foreach (var clip in clips)
            {
                if (armed && new[] { "Aim", "Fire", "Slash", "Flame", "Hit", "Death" }.Contains(clip.name)) continue;
                for (int frame = 0; frame < 16; frame++)
                {
                    float phase = frame / 16f;
                    if (armed) layered.Invoke(null,new object[] { visual, clip, aim, mask, (float?)phase, (float?)phase });
                    else clip.SampleAnimation(visual, clip.length * phase);
                    player.GetComponent<PlayerWearableSurfaceFollower>()?.EvaluateAttachments();
                    skin.BakeMesh(baked);
                    var surface = new BodySurface(baked.vertices.Select(skin.transform.TransformPoint).ToArray(), baked.triangles);
                    string pose = clip.name + (armed ? "-Aim" : "");
                    var totals = catalog.Definitions.ToDictionary(d => d.Type, _ => 0);
                    foreach (var part in parts)
                    {
                        if (!part.Renderer.bounds.Intersects(surface.Bounds)) continue;
                        Vector3[] points = part.Vertices.Select(part.Filter.transform.TransformPoint).ToArray();
                        int hits = part.Edges.Count(edge => surface.Crosses(points[edge.Item1], points[edge.Item2]));
                        if (hits > 0) csv.AppendLine($"{pose},{phase:F4},{part.Type},{part.Filter.name},{hits}");
                        totals[part.Type] += hits;
                    }
                    foreach (var pair in totals)
                    {
                        if (pair.Value <= maximum[pair.Key]) continue;
                        maximum[pair.Key] = pair.Value; worst[pair.Key] = $"{pose} phase={phase:F4}: {pair.Value} crossing edges";
                        Vector3 center = mounts.GetAnimatedSocket(pair.Key).position;
                        foreach (var view in new[] { "front", "back", "side" })
                        {
                            camera.orthographicSize = .55f;
                            camera.transform.position = center + (view == "front" ? new Vector3(1, .5f, 3) : view == "back" ? new Vector3(-1,.5f,-3) : new Vector3(-3,.5f,0));
                            camera.transform.LookAt(center); Capture(camera,$"{Output}/{pair.Key}-{view}.png");
                        }
                    }
                    if (frame == 4)
                    {
                        camera.orthographicSize = 1.2f;
                        foreach (var view in new[] { "front", "back" })
                        {
                            camera.transform.position = new Vector3(view == "front" ? 3 : -3,1.7f,view == "front" ? 6 : -6);
                            camera.transform.LookAt(new Vector3(0,.15f,0)); Capture(camera,$"{Output}/{pose}-{view}.png");
                        }
                    }
                }
            }
            File.WriteAllText(Output + "/crossings.csv",csv.ToString());
            File.WriteAllLines(Output + "/summary.txt",worst.Select(p => p.Key + ": " + p.Value));
            Debug.Log("WEARABLE_CLEARANCE_AUDIT_COMPLETE\n" + string.Join("\n",worst.Select(p => p.Key + ": " + p.Value)));
            // Original mounting brackets intentionally meet the body. Keep the
            // counts as visual-review evidence; do not move the approved layout
            // merely to obtain a zero-contact score.
        }
        finally
        {
            RenderSettings.ambientLight = ambient;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
        }
    }

    // Preview scenes do not have a simulated physics scene. Test actual triangle
    // surfaces directly, with a bounding hierarchy to keep dense mesh checks cheap.
    public sealed class BodySurface
    {
        private sealed class Node
        {
            public Bounds Bounds;
            public Node Left, Right;
            public int[] Triangles;
        }

        private readonly Vector3[] _points;
        private readonly int[] _triangles;
        private readonly Node _root;

        public Bounds Bounds => _root.Bounds;

        public BodySurface(Vector3[] points, int[] triangles)
        {
            _points = points;
            _triangles = triangles;
            _root = Build(Enumerable.Range(0, triangles.Length / 3).ToArray());
        }

        private Node Build(int[] ids)
        {
            var node = new Node
            {
                Bounds = new Bounds(_points[_triangles[ids[0] * 3]], Vector3.zero)
            };
            foreach (int id in ids)
                for (int corner = 0; corner < 3; corner++)
                    node.Bounds.Encapsulate(_points[_triangles[id * 3 + corner]]);

            if (ids.Length <= 12)
            {
                node.Triangles = ids;
                return node;
            }

            Vector3 size = node.Bounds.size;
            int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
            Array.Sort(ids, (a, b) => Center(a, axis).CompareTo(Center(b, axis)));
            int half = ids.Length / 2;
            node.Left = Build(ids.Take(half).ToArray());
            node.Right = Build(ids.Skip(half).ToArray());
            return node;
        }

        private float Center(int triangle, int axis) =>
            (_points[_triangles[triangle * 3]][axis] +
             _points[_triangles[triangle * 3 + 1]][axis] +
             _points[_triangles[triangle * 3 + 2]][axis]) / 3f;

        public bool Crosses(Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            return length > .00001f && Crosses(_root, new Ray(start, delta / length), length);
        }

        private bool Crosses(Node node, Ray ray, float length)
        {
            if (!node.Bounds.IntersectRay(ray, out float distance) || distance > length)
                return false;
            if (node.Triangles == null)
                return Crosses(node.Left, ray, length) || Crosses(node.Right, ray, length);

            foreach (int id in node.Triangles)
            {
                Vector3 a = _points[_triangles[id * 3]];
                Vector3 edge1 = _points[_triangles[id * 3 + 1]] - a;
                Vector3 edge2 = _points[_triangles[id * 3 + 2]] - a;
                Vector3 h = Vector3.Cross(ray.direction, edge2);
                float determinant = Vector3.Dot(edge1, h);
                if (Mathf.Abs(determinant) < .0000001f)
                    continue;

                float inverse = 1f / determinant;
                Vector3 offset = ray.origin - a;
                float u = inverse * Vector3.Dot(offset, h);
                if (u < 0f || u > 1f)
                    continue;

                Vector3 q = Vector3.Cross(offset, edge1);
                float v = inverse * Vector3.Dot(ray.direction, q);
                if (v < 0f || u + v > 1f)
                    continue;

                float distanceOnRay = inverse * Vector3.Dot(edge2, q);
                if (distanceOnRay > .00001f && distanceOnRay < length - .00001f)
                    return true;
            }
            return false;
        }
    }

    private static void Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(600, 600, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(600, 600, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 600, 600), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
        }
    }
}
