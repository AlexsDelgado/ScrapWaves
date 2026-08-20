#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MortarAssetBuilder
{
    private const string GameFeelRoot = "Assets/GameFeel";
    private const string MeshRoot = GameFeelRoot + "/Meshes";
    private const string MaterialRoot = GameFeelRoot + "/Materials";
    private const string PrefabRoot = GameFeelRoot + "/Prefabs/Weapons/Mortar";
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset";
    private const string QualityPath = GameFeelRoot + "/Profiles/GameFeelQuality_PC.asset";

    private sealed class BuildAssets
    {
        public Mesh Shell;
        public Mesh Annulus;
        public Mesh Shard;
        public Mesh Sphere;
        public Mesh Cube;
        public Mesh Cylinder;
        public Material Metal;
        public Material Charge;
        public Material Indicator;
        public Material Dirt;
        public Material Smoke;
        public Material Scorch;
        public GameFeelQualitySettings Quality;
    }

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Mortar Production Assets")]
    public static void BuildFromMenu()
    {
        BuildBatch();
        Debug.Log("Mortar production presentation rebuilt.");
    }

    public static void BuildBatch()
    {
        EnsureFolders();
        AssetDatabase.Refresh();
        BuildAssets assets = BuildSharedAssets();
        GameObject shell = BuildShellPrefab(assets);
        GameObject landingIndicator = BuildLandingIndicatorPrefab(assets);
        Dictionary<MortarCueStyle, GameObject> cuePrefabs = BuildCuePrefabs(assets);
        WeaponPresentationProfile profile = BuildProfile(assets, shell, landingIndicator, cuePrefabs);
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
        EnsureFolder(GameFeelRoot + "/Prefabs/Weapons", "Mortar");
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
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (vfxShader == null || litShader == null)
            throw new InvalidOperationException("Mortar presentation requires the Scrap VFX and URP Lit shaders.");

        return new BuildAssets
        {
            Shell = SaveMesh(MeshRoot + "/GF_Mortar_ScrapShell.asset", CreateShellMesh()),
            Annulus = SaveMesh(MeshRoot + "/GF_Mortar_Annulus.asset", CreateAnnulusMesh(48, 0.39f, 0.5f)),
            Shard = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_ScrapShard.asset"),
            Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
            Cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"),
            Metal = CreateLitMaterial(
                MaterialRoot + "/GF_Mortar_Metal.mat",
                litShader,
                new Color(0.12f, 0.14f, 0.15f, 1f),
                new Color(0.12f, 0.05f, 0.015f),
                0.84f,
                0.26f),
            Charge = CreateVfxMaterial(
                MaterialRoot + "/GF_Mortar_Charge.mat",
                vfxShader,
                new Color(1f, 0.36f, 0.035f, 0.88f),
                new Color(1f, 0.68f, 0.12f),
                3.2f,
                8f,
                2.8f),
            Indicator = CreateVfxMaterial(
                MaterialRoot + "/GF_Mortar_Indicator.mat",
                vfxShader,
                new Color(1f, 0.28f, 0.025f, 0.5f),
                new Color(1f, 0.56f, 0.08f),
                2.4f,
                7f,
                3.5f),
            Dirt = CreateLitMaterial(
                MaterialRoot + "/GF_Mortar_Dirt.mat",
                litShader,
                new Color(0.22f, 0.12f, 0.055f, 1f),
                new Color(0.06f, 0.022f, 0.006f),
                0.08f,
                0.1f),
            Smoke = CreateVfxMaterial(
                MaterialRoot + "/GF_Mortar_Smoke.mat",
                vfxShader,
                new Color(0.16f, 0.13f, 0.11f, 0.52f),
                new Color(0.25f, 0.13f, 0.055f),
                0.45f,
                3.5f,
                0.8f),
            Scorch = CreateVfxMaterial(
                MaterialRoot + "/GF_Mortar_Scorch.mat",
                vfxShader,
                new Color(0.045f, 0.025f, 0.018f, 0.68f),
                new Color(0.12f, 0.045f, 0.012f),
                0.18f,
                9f,
                0.25f),
            Quality = AssetDatabase.LoadAssetAtPath<GameFeelQualitySettings>(QualityPath)
        };
    }

    private static GameObject BuildShellPrefab(BuildAssets assets)
    {
        GameObject root = new("GF_Mortar_AuthoredShell");
        root.AddComponent<MortarShellImpact>();
        MortarShellVfx vfx = root.AddComponent<MortarShellVfx>();

        GameObject shellRoot = new("Rotating Scrap Shell");
        shellRoot.transform.SetParent(root.transform, false);
        Renderer body = CreateMeshLayer("Cast Iron Body", shellRoot.transform, assets.Shell, assets.Metal).GetComponent<Renderer>();
        GameObject chargeBand = CreateMeshLayer("Unstable Charge Band", shellRoot.transform, assets.Cylinder, assets.Charge);
        chargeBand.transform.localPosition = new Vector3(0f, 0f, -0.16f);
        chargeBand.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        chargeBand.transform.localScale = new Vector3(0.175f, 0.025f, 0.175f);
        GameObject fuse = CreateMeshLayer("Hot Fuse Cap", shellRoot.transform, assets.Sphere, assets.Charge);
        fuse.transform.localPosition = new Vector3(0f, 0f, -0.46f);
        fuse.transform.localScale = Vector3.one * 0.105f;

        GameObject flightRoot = new("Short Flight Treatment");
        flightRoot.transform.SetParent(root.transform, false);
        TrailRenderer trail = flightRoot.AddComponent<TrailRenderer>();
        trail.sharedMaterial = assets.Charge;
        trail.time = 0.2f;
        trail.widthMultiplier = 0.11f;
        trail.minVertexDistance = 0.035f;
        trail.numCornerVertices = 2;
        trail.numCapVertices = 2;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.55f, 0.42f), new Keyframe(1f, 0f));
        ParticleSystem flightSmoke = CreateParticles(
            "Intermittent Flight Smoke",
            flightRoot.transform,
            assets.Sphere,
            assets.Smoke,
            24,
            ParticleSystemShapeType.Sphere,
            0.045f,
            0.28f,
            0.46f,
            0f,
            new Color(0.22f, 0.18f, 0.15f, 0.48f),
            new Color(0.08f, 0.07f, 0.065f, 0f),
            burst: false,
            rate: 14f);

        GameObject landingRoot = new("World Anchored Landing Prediction");
        landingRoot.transform.SetParent(root.transform, false);
        GameObject blastRing = CreateMeshLayer("Authoritative Blast Radius", landingRoot.transform, assets.Annulus, assets.Indicator);
        GameObject countdownRing = CreateMeshLayer("Time To Impact Contraction", landingRoot.transform, assets.Annulus, assets.Charge);
        countdownRing.transform.localPosition = Vector3.up * 0.008f;
        countdownRing.transform.localScale = Vector3.one * 0.72f;
        GameObject core = CreateMeshLayer("Landing Core", landingRoot.transform, assets.Cylinder, assets.Charge);
        core.transform.localPosition = Vector3.up * 0.012f;
        core.transform.localScale = new Vector3(0.13f, 0.012f, 0.13f);
        GameObject crossX = CreateMeshLayer("Landing Cross X", landingRoot.transform, assets.Cube, assets.Indicator);
        crossX.transform.localPosition = Vector3.up * 0.014f;
        crossX.transform.localScale = new Vector3(0.48f, 0.012f, 0.035f);
        GameObject crossZ = CreateMeshLayer("Landing Cross Z", landingRoot.transform, assets.Cube, assets.Indicator);
        crossZ.transform.localPosition = Vector3.up * 0.014f;
        crossZ.transform.localScale = new Vector3(0.035f, 0.012f, 0.48f);

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_flightRoot").objectReferenceValue = flightRoot;
        serialized.FindProperty("_shellRoot").objectReferenceValue = shellRoot.transform;
        SetObjectArray(serialized.FindProperty("_shellRenderers"), new UnityEngine.Object[]
        {
            chargeBand.GetComponent<Renderer>(),
            fuse.GetComponent<Renderer>()
        });
        serialized.FindProperty("_trail").objectReferenceValue = trail;
        serialized.FindProperty("_flightSmoke").objectReferenceValue = flightSmoke;
        serialized.FindProperty("_landingRoot").objectReferenceValue = landingRoot;
        serialized.FindProperty("_blastRadiusRing").objectReferenceValue = blastRing.transform;
        serialized.FindProperty("_countdownRing").objectReferenceValue = countdownRing.transform;
        serialized.FindProperty("_landingCore").objectReferenceValue = core.transform;
        SetObjectArray(serialized.FindProperty("_indicatorRenderers"), new UnityEngine.Object[]
        {
            blastRing.GetComponent<Renderer>(),
            countdownRing.GetComponent<Renderer>(),
            core.GetComponent<Renderer>(),
            crossX.GetComponent<Renderer>(),
            crossZ.GetComponent<Renderer>()
        });
        serialized.FindProperty("_surfaceOffset").floatValue = 0.035f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        landingRoot.SetActive(false);
        flightRoot.SetActive(false);
        shellRoot.SetActive(false);
        _ = body;
        return SavePrefab(root, "GF_Mortar_AuthoredShell");
    }

    private static Dictionary<MortarCueStyle, GameObject> BuildCuePrefabs(BuildAssets assets)
    {
        Dictionary<MortarCueStyle, GameObject> result = new();
        foreach (MortarCueStyle style in Enum.GetValues(typeof(MortarCueStyle)))
            result[style] = BuildCuePrefab(assets, style);
        return result;
    }

    private static GameObject BuildLandingIndicatorPrefab(BuildAssets assets)
    {
        GameObject root = new("GF_Mortar_ManualLandingIndicator");
        MortarLandingIndicatorVfx vfx = root.AddComponent<MortarLandingIndicatorVfx>();
        GameObject blast = CreateMeshLayer("Authored Explosion Radius", root.transform, assets.Annulus, assets.Indicator);
        GameObject time = CreateMeshLayer("Time To Impact Pulse", root.transform, assets.Annulus, assets.Charge);
        time.transform.localPosition = Vector3.up * 0.012f;
        time.transform.localScale = Vector3.one * 0.72f;
        GameObject core = CreateMeshLayer("Predicted Landing Core", root.transform, assets.Cylinder, assets.Charge);
        core.transform.localPosition = Vector3.up * 0.016f;
        core.transform.localScale = new Vector3(0.13f, 0.012f, 0.13f);
        GameObject crossX = CreateMeshLayer("Prediction Cross X", root.transform, assets.Cube, assets.Indicator);
        crossX.transform.localPosition = Vector3.up * 0.018f;
        crossX.transform.localScale = new Vector3(0.52f, 0.012f, 0.035f);
        GameObject crossZ = CreateMeshLayer("Prediction Cross Z", root.transform, assets.Cube, assets.Indicator);
        crossZ.transform.localPosition = Vector3.up * 0.018f;
        crossZ.transform.localScale = new Vector3(0.035f, 0.012f, 0.52f);

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_blastRadiusRing").objectReferenceValue = blast.transform;
        serialized.FindProperty("_timeToImpactRing").objectReferenceValue = time.transform;
        serialized.FindProperty("_landingCore").objectReferenceValue = core.transform;
        SetObjectArray(serialized.FindProperty("_renderers"), new UnityEngine.Object[]
        {
            blast.GetComponent<Renderer>(),
            time.GetComponent<Renderer>(),
            core.GetComponent<Renderer>(),
            crossX.GetComponent<Renderer>(),
            crossZ.GetComponent<Renderer>()
        });
        serialized.FindProperty("_minimumPulsePeriod").floatValue = 0.12f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, "GF_Mortar_ManualLandingIndicator");
    }

    private static GameObject BuildCuePrefab(BuildAssets assets, MortarCueStyle style)
    {
        bool grapeshot = style == MortarCueStyle.GrapeshotAirburst || style == MortarCueStyle.GrapeshotImpact;
        bool charged = style == MortarCueStyle.MultiChargedImpact || style == MortarCueStyle.MultiChargedRepeat;
        bool barrage = style == MortarCueStyle.BarrageWarning;
        bool launch = style == MortarCueStyle.Launch;
        Color primary = grapeshot
            ? new Color(1f, 0.84f, 0.08f, 0.95f)
            : charged
                ? new Color(0.7f, 0.25f, 1f, 0.95f)
                : new Color(1f, 0.38f, 0.045f, 0.95f);
        Color coreColor = charged ? new Color(0.95f, 0.62f, 1f, 1f) : new Color(1f, 0.94f, 0.68f, 1f);

        GameObject root = new("GF_Mortar_" + style);
        root.AddComponent<PooledWeaponVfx>();
        MortarCueVfx vfx = root.AddComponent<MortarCueVfx>();
        GameObject animated = new("Animated Mortar Response");
        animated.transform.SetParent(root.transform, false);
        List<Renderer> renderers = new();
        List<ParticleSystem> particles = new();

        if (launch)
        {
            GameObject flash = CreateMeshLayer("Tube Flash Core", animated.transform, assets.Sphere, assets.Charge);
            flash.transform.localScale = new Vector3(0.14f, 0.14f, 0.28f);
            renderers.Add(flash.GetComponent<Renderer>());
            particles.Add(CreateParticles("Launch Smoke", animated.transform, assets.Sphere, assets.Smoke, 18, ParticleSystemShapeType.Cone, 0.12f, 2.1f, 0.58f, 18f, new Color(0.34f, 0.25f, 0.19f, 0.72f), new Color(0.09f, 0.08f, 0.075f, 0f)));
            particles.Add(CreateParticles("Uneven Ignition Sparks", animated.transform, assets.Shard, assets.Charge, 12, ParticleSystemShapeType.Cone, 0.08f, 4.5f, 0.34f, 25f, primary, coreColor));
        }
        else if (barrage)
        {
            ParticleSystem rain = CreateParticles("Zone Projectile Rain Ambience", animated.transform, assets.Shard, assets.Charge, 72, ParticleSystemShapeType.Circle, 0.9f, 0f, 0.72f, 0f, primary, coreColor, burst: false, rate: 14f);
            rain.transform.localPosition = Vector3.up * 1.2f;
            ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-3.4f, -6.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            particles.Add(rain);
        }
        else
        {
            bool airburst = style == MortarCueStyle.GrapeshotAirburst;
            GameObject flash = CreateMeshLayer(airburst ? "Airburst Flash Core" : "Impact Flash Core", animated.transform, assets.Sphere, assets.Charge);
            flash.transform.localScale = Vector3.one * (airburst ? 0.18f : 0.13f);
            renderers.Add(flash.GetComponent<Renderer>());
            GameObject pressure = CreateMeshLayer(airburst ? "Branching Airburst Ring" : "Ground Pressure Ring", animated.transform, assets.Annulus, assets.Indicator);
            pressure.transform.localPosition = Vector3.up * 0.025f;
            renderers.Add(pressure.GetComponent<Renderer>());

            if (!airburst)
            {
                GameObject scorch = CreateMeshLayer("Persistent Scorch Impression", animated.transform, assets.Annulus, assets.Scorch);
                scorch.transform.localPosition = Vector3.up * 0.008f;
                scorch.transform.localScale = Vector3.one * 0.72f;
                renderers.Add(scorch.GetComponent<Renderer>());
                particles.Add(CreateParticles("Dirt And Scrap Plume", animated.transform, assets.Shard, assets.Dirt, grapeshot ? 7 : 18, ParticleSystemShapeType.Cone, 0.12f, grapeshot ? 2.8f : 5.2f, grapeshot ? 0.35f : 0.65f, grapeshot ? 25f : 18f, new Color(0.34f, 0.16f, 0.055f, 1f), new Color(0.12f, 0.055f, 0.025f, 0f)));
                particles.Add(CreateParticles("Rising Smoke Column", animated.transform, assets.Sphere, assets.Smoke, grapeshot ? 4 : 12, ParticleSystemShapeType.Cone, 0.08f, grapeshot ? 0.8f : 1.7f, grapeshot ? 0.45f : 1.15f, 12f, new Color(0.28f, 0.22f, 0.18f, 0.66f), new Color(0.08f, 0.075f, 0.07f, 0f)));
            }

            particles.Add(CreateParticles(
                airburst ? "Branching Submunition Streaks" : "Radial Hot Scrap",
                animated.transform,
                assets.Shard,
                assets.Charge,
                airburst ? 22 : grapeshot ? 6 : 14,
                ParticleSystemShapeType.Sphere,
                airburst ? 0.16f : 0.1f,
                airburst ? 7.2f : 4.8f,
                airburst ? 0.48f : 0.42f,
                0f,
                primary,
                coreColor));
        }

        float lifetime = barrage ? 5.8f : launch ? 0.62f : style == MortarCueStyle.Impact ? 1.25f : 0.82f;
        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_style").enumValueIndex = (int)style;
        serialized.FindProperty("_primaryColor").colorValue = primary;
        serialized.FindProperty("_coreColor").colorValue = coreColor;
        SetObjectArray(serialized.FindProperty("_meshLayers"), renderers.ToArray());
        SetObjectArray(serialized.FindProperty("_particleLayers"), particles.ToArray());
        SetObjectArray(serialized.FindProperty("_animatedRoots"), new UnityEngine.Object[] { animated.transform });
        serialized.FindProperty("_lifetime").floatValue = lifetime;
        serialized.FindProperty("_baseEmission").floatValue = charged ? 4.4f : grapeshot ? 3.8f : 3.5f;
        serialized.FindProperty("_rotationDegreesPerSecond").floatValue = barrage ? 22f : airburstRotation(style);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, "GF_Mortar_" + style);
    }

    private static float airburstRotation(MortarCueStyle style) =>
        style == MortarCueStyle.GrapeshotAirburst ? 145f : 0f;

    private static WeaponPresentationProfile BuildProfile(
        BuildAssets assets,
        GameObject shell,
        GameObject landingIndicator,
        Dictionary<MortarCueStyle, GameObject> prefabs)
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        List<WeaponPresentationCueData> cues = new()
        {
            Cue(WeaponPresentationCue.MortarAutomaticLaunch, prefabs[MortarCueStyle.Launch], 0.62f, 0f, 0.055f, 0.08f, 0f, 12, 0.025f, 0),
            Cue(WeaponPresentationCue.MortarManualLaunch, prefabs[MortarCueStyle.Launch], 0.62f, 0f, 0.075f, 0.12f, 0.2f, 12, 0.025f, 0),
            Cue(WeaponPresentationCue.MortarActiveBarrage, prefabs[MortarCueStyle.BarrageWarning], 5.8f, 0f, 0.09f, 0.14f, 0.35f, 2, 0.15f, 0),
            Cue(WeaponPresentationCue.MortarImpact, prefabs[MortarCueStyle.Impact], 1.25f, 0.018f, 0.15f, 0.2f, 0.32f, 28, 0.045f, 1),
            Cue(WeaponPresentationCue.MortarGrapeshotAirburst, prefabs[MortarCueStyle.GrapeshotAirburst], 0.82f, 0.006f, 0.05f, 0.07f, 0.12f, 18, 0.025f, 1),
            Cue(WeaponPresentationCue.MortarGrapeshotImpact, prefabs[MortarCueStyle.GrapeshotImpact], 0.72f, 0f, 0f, 0f, 0f, 52, 0f, 0),
            Cue(WeaponPresentationCue.MortarMultiChargedImpact, prefabs[MortarCueStyle.MultiChargedImpact], 0.92f, 0.014f, 0.12f, 0.16f, 0.22f, 24, 0.035f, 1),
            Cue(WeaponPresentationCue.MortarMultiChargedRepeat, prefabs[MortarCueStyle.MultiChargedRepeat], 0.92f, 0.018f, 0.16f, 0.22f, 0.3f, 24, 0.035f, 2)
        };
        List<WeaponFeedbackBinding> bindings = new()
        {
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.MortarAutomaticLaunch, WeaponFeedbackModeFilter.Automatic),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.MortarManualLaunch, WeaponFeedbackModeFilter.Manual),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.MortarActiveBarrage, WeaponFeedbackModeFilter.Active)
        };
        MortarPresentationSettings settings = new()
        {
            ShellPrefab = shell,
            LandingIndicatorPrefab = landingIndicator,
            ShellPrewarmCount = 24,
            ShellPoolCapacity = 128,
            MaximumDetailedRainShells = 14,
            DamageFeedbackSubVolleyShellCount = 5
        };
        SetPrivate(profile, "_weaponType", WeaponType.Mortar);
        SetPrivate(profile, "_defaultQuality", GameFeelQualityLevel.High);
        SetPrivate(profile, "_cues", cues);
        SetPrivate(profile, "_feedbackBindings", bindings);
        SetPrivate(profile, "_projectileArchetypes", new List<ProjectileArchetypePresentation>());
        SetPrivate(profile, "_mortar", settings);
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
        float replay,
        int hitStopPriority,
        bool secondary = false)
    {
        WeaponPresentationCueData data = new()
        {
            Cue = cue,
            VfxPrefab = prefab,
            Volume = 0f,
            Duration = duration,
            MinReplayInterval = replay,
            PrewarmCount = Mathf.Min(secondary ? 12 : 6, maximum),
            MaxSimultaneous = maximum,
            CameraPositionImpulse = new Vector3(0f, 0f, -cameraPosition),
            CameraRotationImpulse = new Vector3(-cameraRotation, 0f, 0f),
            CameraFovKick = fovKick,
            CameraMinReplayInterval = 0.045f,
            HitStopDuration = hitStop,
            HitStopPriority = hitStopPriority,
            EssentialGameplayCue = !secondary,
            SecondaryEffect = secondary,
            MinimumQuality = GameFeelQualityLevel.Low,
            SpatialBlend = 0.95f,
            MinimumDistance = 1f,
            MaximumDistance = 48f,
            AudioPriority = 116
        };
        data.Sanitize();
        return data;
    }

    private static WeaponFeedbackBinding Binding(
        WeaponFeedbackEvent feedbackEvent,
        WeaponPresentationCue cue,
        WeaponFeedbackModeFilter mode) =>
        new() { Event = feedbackEvent, Cue = cue, Mode = mode };

    private static void AssignProfile(WeaponPresentationProfile profile)
    {
        string[] paths =
        {
            "Assets/ScriptableObjects/WeaponSO/Mortar.asset",
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset"
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
        Color endColor,
        bool burst = true,
        float rate = 0f)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        ParticleSystem particles = layer.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = !burst;
        main.playOnAwake = false;
        main.duration = burst ? Mathf.Max(0.1f, lifetime) : 5.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.17f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.stopAction = ParticleSystemStopAction.None;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = burst ? 0f : Mathf.Max(0f, rate);
        if (burst)
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
            new[] { new GradientAlphaKey(startColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.06f));
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

    private static Mesh CreateShellMesh()
    {
        const int segments = 14;
        float[] z = { -0.52f, -0.42f, -0.29f, 0.18f, 0.36f, 0.52f };
        float[] radius = { 0.07f, 0.15f, 0.17f, 0.17f, 0.12f, 0.015f };
        List<Vector3> vertices = new();
        List<Vector2> uvs = new();
        for (int ring = 0; ring < z.Length; ring++)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius[ring], Mathf.Sin(angle) * radius[ring], z[ring]));
                uvs.Add(new Vector2(i / (float)segments, ring / (float)(z.Length - 1)));
            }
        }
        List<int> triangles = new();
        for (int ring = 0; ring < z.Length - 1; ring++)
        {
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = ring * segments + i;
                int b = ring * segments + next;
                int c = (ring + 1) * segments + i;
                int d = (ring + 1) * segments + next;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }
        Mesh mesh = new() { name = "GF Mortar Riveted Shell" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateAnnulusMesh(int segments, float innerRadius, float outerRadius)
    {
        List<Vector3> vertices = new();
        List<Vector2> uvs = new();
        List<int> triangles = new();
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(direction * innerRadius);
            vertices.Add(direction * outerRadius);
            uvs.Add(new Vector2(0f, i / (float)segments));
            uvs.Add(new Vector2(1f, i / (float)segments));
        }
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int inner = i * 2;
            int outer = inner + 1;
            int nextInner = next * 2;
            int nextOuter = nextInner + 1;
            triangles.Add(inner); triangles.Add(outer); triangles.Add(nextOuter);
            triangles.Add(inner); triangles.Add(nextOuter); triangles.Add(nextInner);
        }
        Mesh mesh = new() { name = "GF Mortar Authored Annulus" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
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

    private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetPrivate<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, fieldName);
        field.SetValue(target, value);
    }
}
#endif
