#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RotatingBladeAssetBuilder
{
    private const string GameFeelRoot = "Assets/GameFeel";
    private const string MeshRoot = GameFeelRoot + "/Meshes";
    private const string MaterialRoot = GameFeelRoot + "/Materials";
    private const string PrefabRoot = GameFeelRoot + "/Prefabs/Weapons/RotatingBlade";
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/RotatingBladePresentation.asset";
    private const string QualityPath = GameFeelRoot + "/Profiles/GameFeelQuality_PC.asset";

    private sealed class BuildAssets
    {
        public Mesh Blade;
        public Mesh Ring;
        public Mesh Shard;
        public Mesh Sphere;
        public Mesh Cube;
        public Material Metal;
        public Material Edge;
        public Material ManualSlash;
        public Material Trail;
        public Material MultiBlade;
        public Material Atomic;
        public Material Spark;
        public GameFeelQualitySettings Quality;
    }

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Rotating Blade Production Assets")]
    public static void BuildFromMenu()
    {
        BuildBatch();
        Debug.Log("Rotating Blade production presentation rebuilt.");
    }

    public static void BuildBatch()
    {
        EnsureFolders();
        AssetDatabase.Refresh();
        BuildAssets assets = BuildSharedAssets();
        GameObject runtimePrefab = BuildRuntimePrefab(assets);
        Dictionary<RotatingBladeCueStyle, GameObject> impacts = BuildImpactPrefabs(assets);
        WeaponPresentationProfile profile = BuildProfile(assets, runtimePrefab, impacts);
        AssignProfile(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "GameFeel");
        EnsureFolder(GameFeelRoot, "Meshes");
        EnsureFolder(GameFeelRoot, "Materials");
        EnsureFolder(GameFeelRoot, "Prefabs");
        EnsureFolder(GameFeelRoot + "/Prefabs", "Weapons");
        EnsureFolder(GameFeelRoot + "/Prefabs/Weapons", "RotatingBlade");
        EnsureFolder("Assets/ScriptableObjects", "WeaponPresentation");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static BuildAssets BuildSharedAssets()
    {
        Shader vfxShader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        Shader slashShader = Shader.Find("ScrapWaves/GameFeel/Flowing Slash");
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (vfxShader == null || slashShader == null || litShader == null)
            throw new InvalidOperationException("Rotating Blade presentation requires the Scrap VFX, Flowing Slash, and URP Lit shaders.");

        Mesh blade = SaveMesh(MeshRoot + "/GF_RotatingBlade_ScrapBlade.asset", CreateScrapBladeMesh());
        return new BuildAssets
        {
            Blade = blade,
            Ring = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_PressureRing.asset"),
            Shard = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_ScrapShard.asset"),
            Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
            Metal = CreateLitMaterial(
                MaterialRoot + "/GF_RotatingBlade_Metal.mat",
                litShader,
                new Color(0.19f, 0.22f, 0.24f, 1f),
                new Color(0.12f, 0.4f, 0.46f),
                0.78f,
                0.34f),
            Edge = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Edge.mat",
                vfxShader,
                new Color(0.34f, 0.9f, 1f, 0.34f),
                new Color(0.46f, 0.95f, 1f),
                2.8f,
                8f,
                2.4f),
            ManualSlash = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Slash.mat",
                slashShader,
                new Color(0.35f, 0.9f, 1f, 0.9f),
                new Color(0.55f, 0.96f, 1f),
                2.8f,
                5.5f,
                4f),
            Trail = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Trail.mat",
                vfxShader,
                new Color(0.24f, 0.78f, 0.9f, 0.52f),
                new Color(0.38f, 0.92f, 1f),
                2.2f,
                6f,
                3.2f),
            MultiBlade = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Multi.mat",
                vfxShader,
                new Color(1f, 0.5f, 0.28f, 0.56f),
                new Color(1f, 0.74f, 0.48f),
                3.1f,
                7f,
                3f),
            Atomic = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Atomic.mat",
                vfxShader,
                new Color(0.31f, 0.025f, 0.5f, 0.7f),
                new Color(0.78f, 0.2f, 1f),
                4.6f,
                12f,
                5.5f),
            Spark = CreateVfxMaterial(
                MaterialRoot + "/GF_RotatingBlade_Spark.mat",
                vfxShader,
                new Color(1f, 0.56f, 0.16f, 0.88f),
                new Color(1f, 0.82f, 0.38f),
                4f,
                10f,
                4f),
            Quality = AssetDatabase.LoadAssetAtPath<GameFeelQualitySettings>(QualityPath)
        };
    }

    private static GameObject BuildRuntimePrefab(BuildAssets assets)
    {
        GameObject root = new("GF_RotatingBlade_Runtime");
        RotatingBladeVfx vfx = root.AddComponent<RotatingBladeVfx>();
        GameObject prototype = new("Authored Scrap Blade Prototype");
        prototype.transform.SetParent(root.transform, false);

        GameObject metal = CreateMeshLayer("Riveted Scrap Body", prototype.transform, assets.Blade, assets.Metal);
        GameObject edge = CreateMeshLayer("Unstable Cutting Edge", prototype.transform, assets.Blade, assets.Edge);
        edge.transform.localScale = new Vector3(1.035f, 1.05f, 1.035f);
        GameObject hub = CreateMeshLayer("Welded Hub", prototype.transform, assets.Sphere, assets.Metal);
        hub.transform.localPosition = new Vector3(0f, 0.03f, -0.42f);
        hub.transform.localScale = new Vector3(0.22f, 0.18f, 0.22f);

        TrailRenderer trail = prototype.AddComponent<TrailRenderer>();
        trail.sharedMaterial = assets.Trail;
        trail.time = 0.16f;
        trail.widthMultiplier = 0.16f;
        trail.minVertexDistance = 0.04f;
        trail.numCornerVertices = 2;
        trail.numCapVertices = 2;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        trail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.6f, 0.55f),
            new Keyframe(1f, 0f));

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_bladePrototype").objectReferenceValue = prototype;
        serialized.FindProperty("_trailMaterial").objectReferenceValue = assets.Trail;
        serialized.FindProperty("_slashMaterial").objectReferenceValue = assets.ManualSlash;
        serialized.FindProperty("_thrustMaterial").objectReferenceValue = assets.MultiBlade;
        serialized.FindProperty("_atomicMaterial").objectReferenceValue = assets.Atomic;
        serialized.FindProperty("_orbitGuideAlpha").floatValue = 0.16f;
        serialized.FindProperty("_minimumBladeLength").floatValue = 0.72f;
        serialized.FindProperty("_baseTrailWidth").floatValue = 0.16f;
        serialized.FindProperty("_baseTrailTime").floatValue = 0.13f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        prototype.SetActive(false);
        return SavePrefab(root, "GF_RotatingBlade_Runtime");
    }

    private static Dictionary<RotatingBladeCueStyle, GameObject> BuildImpactPrefabs(BuildAssets assets)
    {
        Dictionary<RotatingBladeCueStyle, GameObject> result = new();
        foreach (RotatingBladeCueStyle style in Enum.GetValues(typeof(RotatingBladeCueStyle)))
            result[style] = BuildImpactPrefab(assets, style);
        return result;
    }

    private static GameObject BuildImpactPrefab(BuildAssets assets, RotatingBladeCueStyle style)
    {
        bool final = style == RotatingBladeCueStyle.MultiBladeFinalImpact;
        bool atomic = style == RotatingBladeCueStyle.AtomicSliceImpact;
        Material material = atomic ? assets.Atomic : final ? assets.MultiBlade : assets.Spark;
        Color primary = atomic
            ? new Color(0.48f, 0.04f, 0.72f, 0.95f)
            : final
                ? new Color(1f, 0.58f, 0.32f, 0.95f)
                : new Color(1f, 0.68f, 0.24f, 0.92f);
        Color core = atomic ? new Color(0.92f, 0.45f, 1f, 1f) : new Color(1f, 0.92f, 0.64f, 1f);

        GameObject root = new("GF_RotatingBlade_" + style);
        root.AddComponent<PooledWeaponVfx>();
        RotatingBladeCueVfx vfx = root.AddComponent<RotatingBladeCueVfx>();
        GameObject visual = new("Animated Impact");
        visual.transform.SetParent(root.transform, false);
        List<Renderer> renderers = new();
        List<ParticleSystem> particles = new();

        if (atomic)
        {
            GameObject slice = CreateMeshLayer("Delayed Slice Flash", visual.transform, assets.Cube, assets.Atomic);
            slice.transform.localScale = new Vector3(1.35f, 0.035f, 0.055f);
            renderers.Add(slice.GetComponent<MeshRenderer>());
            particles.Add(CreateParticles("Cutting Filaments", visual.transform, assets.Shard, assets.Atomic, 12, ParticleSystemShapeType.Cone, 0.08f, 5.8f, 0.36f, 7f, primary, core));
        }
        else if (final)
        {
            GameObject ring = CreateMeshLayer("Heavy Final Pressure", visual.transform, assets.Ring, assets.MultiBlade);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * 0.58f;
            renderers.Add(ring.GetComponent<MeshRenderer>());
            particles.Add(CreateParticles("Final Scrap Sparks", visual.transform, assets.Shard, assets.Spark, 24, ParticleSystemShapeType.Sphere, 0.15f, 5.2f, 0.48f, 0f, primary, core));
        }
        else
        {
            GameObject flash = CreateMeshLayer("Contact Flash", visual.transform, assets.Sphere, assets.Spark);
            flash.transform.localScale = Vector3.one * 0.12f;
            renderers.Add(flash.GetComponent<MeshRenderer>());
            particles.Add(CreateParticles("Blade Friction Sparks", visual.transform, assets.Shard, assets.Spark, 10, ParticleSystemShapeType.Cone, 0.06f, 4.2f, 0.32f, 18f, primary, core));
        }

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_style").enumValueIndex = (int)style;
        serialized.FindProperty("_primaryColor").colorValue = primary;
        serialized.FindProperty("_coreColor").colorValue = core;
        SetObjectArray(serialized.FindProperty("_meshLayers"), renderers.ToArray());
        SetObjectArray(serialized.FindProperty("_particleLayers"), particles.ToArray());
        SetObjectArray(serialized.FindProperty("_animatedRoots"), new UnityEngine.Object[] { visual.transform });
        serialized.FindProperty("_lifetime").floatValue = atomic ? 0.46f : final ? 0.58f : 0.34f;
        serialized.FindProperty("_baseEmission").floatValue = atomic ? 4.8f : final ? 4.1f : 3.4f;
        serialized.FindProperty("_rotationDegreesPerSecond").floatValue = final ? 210f : 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, "GF_RotatingBlade_" + style);
    }

    private static WeaponPresentationProfile BuildProfile(
        BuildAssets assets,
        GameObject runtimePrefab,
        Dictionary<RotatingBladeCueStyle, GameObject> impacts)
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        List<WeaponPresentationCueData> cues = new()
        {
            Cue(WeaponPresentationCue.RotatingBladeManualSlash, null, 0.18f, 0.02f, 0.08f, 0.12f, 0f, 10, 0f, 0),
            Cue(WeaponPresentationCue.RotatingBladeActiveThrust, null, 0.35f, 0.06f, 0.2f, 0.55f, 0.9f, 5, 0.012f, 1),
            Cue(WeaponPresentationCue.RotatingBladeMultiSlash, null, 0.18f, 0.025f, 0.09f, 0.1f, 0f, 12, 0f, 0),
            Cue(WeaponPresentationCue.RotatingBladeMultiThrust, null, 0.3f, 0.045f, 0.16f, 0.42f, 0.55f, 9, 0.01f, 1),
            Cue(WeaponPresentationCue.RotatingBladeAtomicSlash, null, 0.14f, 0.018f, 0.07f, 0.1f, 0f, 12, 0f, 0),
            Cue(WeaponPresentationCue.RotatingBladeAtomicDash, null, 0.48f, 0.08f, 0.22f, 0.7f, 1.25f, 4, 0.014f, 2),
            Cue(WeaponPresentationCue.RotatingBladeContactImpact, impacts[RotatingBladeCueStyle.ContactSparks], 0.34f, 0f, 0f, 0f, 0f, 24, 0f, 0, secondary: true),
            Cue(WeaponPresentationCue.RotatingBladeMultiFinalImpact, impacts[RotatingBladeCueStyle.MultiBladeFinalImpact], 0.58f, 0.035f, 0.1f, 0.28f, 0.2f, 10, 0.024f, 2),
            Cue(WeaponPresentationCue.RotatingBladeAtomicSliceImpact, impacts[RotatingBladeCueStyle.AtomicSliceImpact], 0.46f, 0.012f, 0.05f, 0.12f, 0f, 18, 0.01f, 1)
        };

        List<WeaponFeedbackBinding> bindings = new()
        {
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeMultiSlash, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeAtomicSlash, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeManualSlash, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeMultiThrust, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeAtomicDash, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RotatingBladeActiveThrust, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.RotatingBladeAtomicSliceImpact, path: WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.RotatingBladeContactImpact)
        };

        RotatingBladePresentationSettings settings = new()
        {
            RuntimeVfxPrefab = runtimePrefab,
            MaximumOrbitingBlades = 6,
            MaximumConcurrentSlashes = 8,
            MaximumConcurrentThrusts = 8
        };
        SetPrivate(profile, "_weaponType", WeaponType.RotatingBlade);
        SetPrivate(profile, "_defaultQuality", GameFeelQualityLevel.High);
        SetPrivate(profile, "_cues", cues);
        SetPrivate(profile, "_feedbackBindings", bindings);
        SetPrivate(profile, "_projectileArchetypes", new List<ProjectileArchetypePresentation>());
        SetPrivate(profile, "_rotatingBlade", settings);
        SetPrivate(profile, "_qualitySettings", assets.Quality);
        profile.RebuildCache();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static WeaponPresentationCueData Cue(
        WeaponPresentationCue cue,
        GameObject prefab,
        float duration,
        float hitStop,
        float cameraPosition,
        float cameraRotation,
        float fovKick,
        int maximum,
        float minReplay,
        int hitStopPriority,
        bool secondary = false)
    {
        WeaponPresentationCueData data = new()
        {
            Cue = cue,
            VfxPrefab = prefab,
            Volume = 0f,
            Duration = duration,
            MinReplayInterval = minReplay,
            PrewarmCount = Mathf.Min(6, maximum),
            MaxSimultaneous = maximum,
            CameraPositionImpulse = new Vector3(0f, 0f, -cameraPosition),
            CameraRotationImpulse = new Vector3(-cameraRotation, 0f, 0f),
            CameraFovKick = fovKick,
            CameraMinReplayInterval = 0.035f,
            HitStopDuration = hitStop,
            HitStopPriority = hitStopPriority,
            EssentialGameplayCue = !secondary,
            SecondaryEffect = secondary,
            MinimumQuality = GameFeelQualityLevel.Low,
            SpatialBlend = 0.9f,
            MinimumDistance = 1f,
            MaximumDistance = 42f,
            AudioPriority = 120
        };
        data.Sanitize();
        return data;
    }

    private static WeaponFeedbackBinding Binding(
        WeaponFeedbackEvent feedbackEvent,
        WeaponPresentationCue cue,
        WeaponFeedbackModeFilter mode = WeaponFeedbackModeFilter.Any,
        WeaponUpgradePathFilter path = WeaponUpgradePathFilter.Any)
    {
        return new WeaponFeedbackBinding { Event = feedbackEvent, Cue = cue, Mode = mode, UpgradePath = path };
    }

    private static void AssignProfile(WeaponPresentationProfile profile)
    {
        string[] paths =
        {
            "Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset",
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_RotatingBlade.asset"
        };
        for (int i = 0; i < paths.Length; i++)
        {
            WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(paths[i]);
            if (data == null)
                continue;
            data.PresentationProfile = profile;
            EditorUtility.SetDirty(data);
        }
    }

    private static GameObject SavePrefab(GameObject root, string name)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateMeshLayer(string name, Transform parent, Mesh mesh, Material material)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        MeshFilter filter = layer.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return layer;
    }

    private static ParticleSystem CreateParticles(
        string name,
        Transform parent,
        Mesh mesh,
        Material material,
        int count,
        ParticleSystemShapeType shapeType,
        float radius,
        float speed,
        float lifetime,
        float coneAngle,
        Color startColor,
        Color endColor)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        ParticleSystem particles = layer.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, lifetime);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.55f, lifetime * 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.stopAction = ParticleSystemStopAction.None;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = shapeType;
        shape.radius = radius;
        if (shapeType == ParticleSystemShapeType.Cone)
            shape.angle = coneAngle;
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.08f));
        ParticleSystemRenderer renderer = layer.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static Material CreateLitMaterial(
        string path,
        Shader shader,
        Color baseColor,
        Color emissionColor,
        float metallic,
        float smoothness)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
            material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_EmissionColor", emissionColor);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateVfxMaterial(
        string path,
        Shader shader,
        Color baseColor,
        Color emissionColor,
        float emission,
        float noiseScale,
        float noiseSpeed)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
            material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_EmissionColor", emissionColor);
        material.SetFloat("_EmissionIntensity", emission);
        material.SetFloat("_NoiseScale", noiseScale);
        material.SetFloat("_NoiseSpeed", noiseSpeed);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh CreateScrapBladeMesh()
    {
        Vector2[] outline =
        {
            new(-0.22f, -1f),
            new(0.24f, -0.88f),
            new(0.16f, -0.55f),
            new(0.36f, -0.25f),
            new(0.2f, 0.08f),
            new(0.34f, 0.42f),
            new(0.12f, 0.78f),
            new(0f, 1.12f),
            new(-0.22f, 0.72f),
            new(-0.3f, 0.25f),
            new(-0.31f, -0.35f)
        };
        const float halfThickness = 0.075f;
        List<Vector3> vertices = new(outline.Length * 2);
        for (int i = 0; i < outline.Length; i++)
        {
            vertices.Add(new Vector3(outline[i].x, halfThickness, outline[i].y));
            vertices.Add(new Vector3(outline[i].x, -halfThickness, outline[i].y));
        }
        List<int> triangles = new();
        for (int i = 1; i < outline.Length - 1; i++)
        {
            triangles.Add(0); triangles.Add(i * 2); triangles.Add((i + 1) * 2);
            triangles.Add(1); triangles.Add((i + 1) * 2 + 1); triangles.Add(i * 2 + 1);
        }
        for (int i = 0; i < outline.Length; i++)
        {
            int next = (i + 1) % outline.Length;
            int top = i * 2;
            int bottom = top + 1;
            int nextTop = next * 2;
            int nextBottom = nextTop + 1;
            triangles.Add(top); triangles.Add(nextTop); triangles.Add(nextBottom);
            triangles.Add(top); triangles.Add(nextBottom); triangles.Add(bottom);
        }
        Mesh mesh = new() { name = "GF Rotating Blade Scrap Blade" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        List<Vector2> uvs = new(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
            uvs.Add(new Vector2(vertices[i].x + 0.5f, vertices[i].z * 0.45f + 0.5f));
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh SaveMesh(string path, Mesh generated)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }
        EditorUtility.CopySerialized(generated, existing);
        UnityEngine.Object.DestroyImmediate(generated);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void SetPrivate<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, fieldName);
        field.SetValue(target, value);
    }

    private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
#endif
