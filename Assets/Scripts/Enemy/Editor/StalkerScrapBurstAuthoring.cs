#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Authors only Stalker's reusable decorative scrap meshes and resource prefab.</summary>
public static class StalkerScrapBurstAuthoring
{
    public const string PrefabPath = "Assets/Resources/VFX/StalkerScrapBurst.prefab";
    public const string MeshFolder = "Assets/Art/Enemies/Stalker/ScrapBurst/Meshes";
    public const string GearPath = "Assets/Art/Drops/Gear.fbx";
    private const string MaterialFolder = "Assets/Art/Environment/Geysers/Materials/";
    private static readonly string[] Names = { "Cog", "Bolt", "Nail", "Nut" };
    private static readonly string[] MaterialNames =
    {
        "Geyser_DullScrap", "Geyser_BareSteel", "Geyser_RustScrap", "Geyser_HeatDarkenedMetal"
    };

    [MenuItem("Tools/ScrapWaves/Enemies/Build Stalker Scrap Burst")]
    public static void Build()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Author outside Play Mode.");
        var materials = MaterialNames.Select(n => AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + n + ".mat")).ToArray();
        Require(materials.All(m => m != null), "The existing Geyser metal materials must be present; no fallback material is authored.");
        Mesh originalGear = AssetDatabase.LoadAllAssetsAtPath(GearPath).OfType<Mesh>().SingleOrDefault();
        Require(originalGear != null, "Expected one existing Gear mesh.");
        Mesh[] meshes = { CopyGear(originalGear), MakeBolt(), MakeNail(), MakeNut() };
        GameObject root = null;
        try
        {
            EnsureFolder(MeshFolder);
            EnsureFolder("Assets/Resources/VFX");
            for (int i = 0; i < meshes.Length; i++)
            {
                Normalize(meshes[i], Names[i]);
                ValidateMesh(meshes[i]);
                meshes[i] = SaveMesh(meshes[i], MeshFolder + "/" + Names[i] + ".asset");
            }
            root = new GameObject("StalkerScrapBurst");
            var systems = new ParticleSystem[Names.Length];
            for (int i = 0; i < systems.Length; i++)
                systems[i] = MakeSystem(root.transform, Names[i], meshes[i], materials[i], (uint)(20401 + i));
            root.AddComponent<StalkerScrapBurstVfx>().Configure(systems);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            Require(saved, "Could not save the scrap burst prefab.");
            Validate();
            Debug.Log("STALKER_SCRAP_BURST_AUTHORED: " + string.Join(", ", meshes.Select(m =>
                m.name + "=" + m.vertexCount + " vertices/" + m.triangles.Length / 3 + " triangles")) +
                "; four world-space systems, 24 particles each; original Gear FBX and shared materials unchanged.");
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            foreach (Mesh mesh in meshes)
                if (mesh != null && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
        }
    }

    [MenuItem("Tools/ScrapWaves/Enemies/Validate Stalker Scrap Burst")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab != null && prefab.GetComponent<StalkerScrapBurstVfx>() != null, "Missing burst manager/prefab.");
        Require(prefab.GetComponentsInChildren<Collider>(true).Length == 0 &&
                prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Scrap must remain decorative particles.");
        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Require(systems.Length == 4, "Expected four scrap kinds.");
        for (int i = 0; i < Names.Length; i++)
        {
            ParticleSystem particles = systems.Single(p => p.name == Names[i]);
            var main = particles.main;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            Require(!main.playOnAwake && !main.loop && Mathf.Approximately(main.duration, 3f) &&
                    main.maxParticles == StalkerScrapBurstVfx.MaxParticlesPerKind &&
                    main.simulationSpace == ParticleSystemSimulationSpace.World &&
                    Mathf.Approximately(main.startSpeed.constant, 0f) && Mathf.Approximately(main.gravityModifier.constant, 1f),
                    "Unexpected particle configuration: " + Names[i]);
            Require(!particles.emission.enabled && !particles.shape.enabled && !particles.collision.enabled &&
                    !particles.trails.enabled && particles.rotationOverLifetime.enabled &&
                    particles.rotationOverLifetime.separateAxes && particles.sizeOverLifetime.enabled,
                    "Unexpected particle modules: " + Names[i]);
            Require(renderer.renderMode == ParticleSystemRenderMode.Mesh &&
                    renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows &&
                    renderer.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + MaterialNames[i] + ".mat"),
                    "Unexpected renderer/material: " + Names[i]);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/" + Names[i] + ".asset");
            Require(renderer.mesh == mesh, "Unexpected mesh reference.");
            ValidateMesh(mesh);
        }
        Debug.Log("STALKER_SCRAP_BURST_VALID: source gear reused; normalized readable meshes; four bounded decorative systems.");
    }

    private static ParticleSystem MakeSystem(Transform parent, string name, Mesh mesh, Material material, uint seed)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        var particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = seed;
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 3f;
        main.maxParticles = StalkerScrapBurstVfx.MaxParticlesPerKind;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startLifetime = 3f;
        main.startColor = Color.white;
        main.startRotation3D = true;
        main.gravityModifier = 1f;
        main.stopAction = ParticleSystemStopAction.None;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var collision = particles.collision; collision.enabled = false;
        var trails = particles.trails; trails.enabled = false;
        var rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
        rotation.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(.65f, 1f, 0f, -1f / .35f),
            new Keyframe(1f, 0f, -1f / .35f, 0f)));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return particles;
    }

    private static Mesh CopyGear(Mesh source)
    {
        // Editor-only snapshot supports an unreadable imported mesh without changing its importer.
        var mesh = new Mesh { name = "Cog", indexFormat = source.indexFormat };
        using (Mesh.MeshDataArray snapshot = MeshUtility.AcquireReadOnlyMeshData(source))
        {
            Mesh.MeshData data = snapshot[0];
            using (var values = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            {
                data.GetVertices(values); mesh.vertices = values.ToArray();
                if (data.HasVertexAttribute(VertexAttribute.Normal)) { data.GetNormals(values); mesh.normals = values.ToArray(); }
            }
            if (data.HasVertexAttribute(VertexAttribute.Tangent))
                using (var values = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                { data.GetTangents(values); mesh.tangents = values.ToArray(); }
            if (data.HasVertexAttribute(VertexAttribute.Color))
                using (var values = new NativeArray<Color>(data.vertexCount, Allocator.Temp))
                { data.GetColors(values); mesh.colors = values.ToArray(); }
            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!data.HasVertexAttribute(attribute)) continue;
                int dimensions = data.GetVertexAttributeDimension(attribute);
                using (var values = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                {
                    data.GetUVs(channel, values);
                    if (dimensions == 2) mesh.SetUVs(channel, values.ToArray().Select(v => new Vector2(v.x, v.y)).ToList());
                    else if (dimensions == 3) mesh.SetUVs(channel, values.ToArray().Select(v => new Vector3(v.x, v.y, v.z)).ToList());
                    else mesh.SetUVs(channel, values.ToArray().ToList());
                }
            }
            var triangles = new List<int>();
            for (int submesh = 0; submesh < data.subMeshCount; submesh++)
            {
                SubMeshDescriptor descriptor = data.GetSubMesh(submesh);
                Require(descriptor.topology == MeshTopology.Triangles, "Gear contains unsupported non-triangle topology.");
                using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                { data.GetIndices(indices, submesh); triangles.AddRange(indices.ToArray()); }
            }
            mesh.SetTriangles(triangles, 0, false);
        }
        if (!mesh.HasVertexAttribute(VertexAttribute.Normal)) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh MakeBolt()
    {
        var build = new SolidMesh();
        build.Frustum(-.43f, .20f, .072f, .072f, 10, true, true);
        build.Frustum(-.48f, -.43f, .035f, .072f, 10, true, false);
        build.Frustum(.20f, .36f, .23f, .23f, 6, true, true);
        for (int i = 0; i < 4; i++)
        {
            float y = -.32f + i * .115f;
            build.Frustum(y - .02f, y, .072f, .105f, 10, false, false);
            build.Frustum(y, y + .02f, .105f, .072f, 10, false, false);
        }
        return build.Finish("Bolt");
    }

    private static Mesh MakeNail()
    {
        var build = new SolidMesh();
        build.Frustum(-.34f, .35f, .033f, .033f, 8, false, false);
        build.Frustum(-.50f, -.34f, 0f, .033f, 8, false, false);
        build.Frustum(.35f, .40f, .155f, .155f, 10, true, true);
        return build.Finish("Nail");
    }

    private static Mesh MakeNut()
    {
        var build = new SolidMesh();
        const int sides = 12;
        const float outside = .45f, inside = .22f, bottom = -.13f, top = .13f;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
            // Alternating corners and edge midpoints describe a true hexagon; the bore has 12 sides.
            float ra = outside * (i % 2 == 0 ? 1f : Mathf.Cos(Mathf.PI / 6f));
            float rb = outside * ((i + 1) % 2 == 0 ? 1f : Mathf.Cos(Mathf.PI / 6f));
            Vector3 oa0 = Ring(a, ra, bottom), ob0 = Ring(b, rb, bottom);
            Vector3 oa1 = Ring(a, ra, top), ob1 = Ring(b, rb, top);
            Vector3 ia0 = Ring(a, inside, bottom), ib0 = Ring(b, inside, bottom);
            Vector3 ia1 = Ring(a, inside, top), ib1 = Ring(b, inside, top);
            build.Quad(oa0, oa1, ob1, ob0);
            build.Quad(ia0, ib0, ib1, ia1);
            build.Quad(ia1, ib1, ob1, oa1);
            build.Quad(oa0, ob0, ib0, ia0);
        }
        return build.Finish("Nut");
    }

    private static Vector3 Ring(float angle, float radius, float y) =>
        new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

    private static void Normalize(Mesh mesh, string name)
    {
        mesh.RecalculateBounds();
        Vector3 center = mesh.bounds.center;
        Vector3 size = mesh.bounds.size;
        float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        Require(largest > .000001f, "Empty mesh: " + name);
        mesh.vertices = mesh.vertices.Select(v => (v - center) / largest).ToArray();
        mesh.name = name;
        mesh.RecalculateBounds();
    }

    private static void ValidateMesh(Mesh mesh)
    {
        Require(mesh != null && mesh.isReadable && mesh.vertexCount > 0 && mesh.vertexCount < 2500, "Invalid/over-budget scrap mesh.");
        Vector3 size = mesh.bounds.size;
        Require(mesh.bounds.center.sqrMagnitude < .000001f && Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= 1.00001f,
            "Scrap mesh must be centered and fit within one unit.");
        Require(mesh.subMeshCount == 1 && mesh.triangles.Length > 0 && mesh.triangles.Length % 3 == 0,
            "Scrap mesh must contain one triangle submesh.");
        Require(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), "Nonfinite mesh vertex.");
    }

    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Require(slash > 0 && path.StartsWith("Assets/", StringComparison.Ordinal), "Invalid asset folder.");
        EnsureFolder(path.Substring(0, slash));
        Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1))),
            "Could not create " + path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SolidMesh
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<int> _triangles = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Require(cross.sqrMagnitude > 0.000000000001f, "Degenerate generated scrap triangle.");
            int first = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            Vector3 normal = cross.normalized;
            _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
            _uv.Add(Vector2.zero); _uv.Add(Vector2.up); _uv.Add(Vector2.one);
            _triangles.Add(first); _triangles.Add(first + 1); _triangles.Add(first + 2);
        }

        public void Frustum(float y0, float y1, float r0, float r1, int sides, bool capBottom, bool capTop)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 a0 = Ring(a, r0, y0), b0 = Ring(b, r0, y0);
                Vector3 a1 = Ring(a, r1, y1), b1 = Ring(b, r1, y1);
                if (r0 <= 0f) Triangle(a0, a1, b1);
                else if (r1 <= 0f) Triangle(a0, a1, b0);
                else Quad(a0, a1, b1, b0);
                if (capBottom && r0 > 0f) Triangle(new Vector3(0, y0, 0), a0, b0);
                if (capTop && r1 > 0f) Triangle(new Vector3(0, y1, 0), b1, a1);
            }
        }

        public Mesh Finish(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_vertices); mesh.SetNormals(_normals); mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
