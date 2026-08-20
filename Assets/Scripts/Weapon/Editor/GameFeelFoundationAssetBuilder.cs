#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class GameFeelFoundationAssetBuilder
{
    private const string GameFeelRoot = "Assets/GameFeel";
    private const string MeshRoot = GameFeelRoot + "/Meshes";
    private const string MaterialRoot = GameFeelRoot + "/Materials";
    private const string ProfileRoot = GameFeelRoot + "/Profiles";
    private const string CannonPrefabRoot = GameFeelRoot + "/Prefabs/Weapons/AutomaticCannon";
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset";
    private const string QualityPath = ProfileRoot + "/GameFeelQuality_PC.asset";
    private const string EnemyReactionProfilePath = GameFeelRoot + "/Resources/EnemyReactionProfile.asset";

    private sealed class BuildAssets
    {
        public Mesh Cone;
        public Mesh Ring;
        public Mesh Bullet;
        public Mesh Bolt;
        public Mesh Shard;
        public Mesh Cube;
        public Material OrangeVfx;
        public Material WhiteVfx;
        public Material BlueVfx;
        public Material SmokeVfx;
        public Material Projectile;
        public Material Tracer;
        public Material HeadHunter;
        public GameFeelQualitySettings Quality;
    }

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Cannon Production Assets")]
    public static void BuildFromMenu()
    {
        BuildAll();
        Debug.Log("Game Feel foundation and Automatic Cannon production assets rebuilt.");
    }

    public static void BuildBatch()
    {
        BuildAll();
    }

    public static void BuildEnemyReactionsBatch()
    {
        EnsureFolders();
        EnsureEnemyReactionProfile();
        AddEnemyFeedbackComponents();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void BuildAll()
    {
        EnsureFolders();
        AssetDatabase.Refresh();
        BuildAssets assets = BuildSharedAssets();
        Dictionary<AutomaticCannonVfxStyle, GameObject> prefabs = BuildCannonPrefabs(assets);
        WeaponPresentationProfile profile = BuildProfile(assets, prefabs);
        MigrateProjectilePrefab(assets);
        AddPlayerRecoilRig(assets);
        EnsureEnemyReactionProfile();
        AddEnemyFeedbackComponents();
        DuplicateSandbox();
        AssignProductionProfile(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "GameFeel");
        EnsureFolder(GameFeelRoot, "Meshes");
        EnsureFolder(GameFeelRoot, "Materials");
        EnsureFolder(GameFeelRoot, "Profiles");
        EnsureFolder(GameFeelRoot, "Prefabs");
        EnsureFolder(GameFeelRoot + "/Prefabs", "Weapons");
        EnsureFolder(GameFeelRoot + "/Prefabs/Weapons", "AutomaticCannon");
        EnsureFolder(GameFeelRoot, "Shaders");
        EnsureFolder(GameFeelRoot, "Resources");
        EnsureFolder("Assets/Scenes", "Testing");
    }

    private static EnemyReactionProfile EnsureEnemyReactionProfile()
    {
        EnemyReactionProfile profile = AssetDatabase.LoadAssetAtPath<EnemyReactionProfile>(EnemyReactionProfilePath);
        if (profile != null)
            return profile;
        profile = ScriptableObject.CreateInstance<EnemyReactionProfile>();
        AssetDatabase.CreateAsset(profile, EnemyReactionProfilePath);
        return profile;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static BuildAssets BuildSharedAssets()
    {
        BuildAssets assets = new()
        {
            Cone = SaveMesh(MeshRoot + "/GF_CannonCone.asset", CreateCone("GF_CannonCone", 10, 0.5f, 1f)),
            Ring = SaveMesh(MeshRoot + "/GF_PressureRing.asset", CreateRing("GF_PressureRing", 16, 4, 0.5f, 0.08f)),
            Bullet = SaveMesh(MeshRoot + "/GF_CannonRound.asset", CreateCylinder("GF_CannonRound", 10, 0.32f, 1.2f, true)),
            Bolt = SaveMesh(MeshRoot + "/GF_HeadHunterBolt.asset", CreateCylinder("GF_HeadHunterBolt", 6, 0.18f, 1.8f, true)),
            Shard = SaveMesh(MeshRoot + "/GF_ScrapShard.asset", CreateShard("GF_ScrapShard")),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx")
        };

        Shader vfxShader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        Shader projectileShader = Shader.Find("ScrapWaves/GameFeel/Scrap Projectile");
        if (vfxShader == null || projectileShader == null)
            throw new InvalidOperationException("Game Feel shaders were not imported before the asset build.");

        assets.OrangeVfx = CreateMaterial(
            MaterialRoot + "/GF_Cannon_Orange.mat",
            vfxShader,
            new Color(1f, 0.28f, 0.025f),
            new Color(1f, 0.55f, 0.08f),
            2.6f);
        assets.WhiteVfx = CreateMaterial(
            MaterialRoot + "/GF_Cannon_Core.mat",
            vfxShader,
            new Color(1f, 0.78f, 0.25f),
            new Color(1f, 0.96f, 0.72f),
            3.8f);
        assets.BlueVfx = CreateMaterial(
            MaterialRoot + "/GF_HeadHunter_Blue.mat",
            vfxShader,
            new Color(0.08f, 0.38f, 0.85f),
            new Color(0.42f, 0.86f, 1f),
            3.5f);
        assets.SmokeVfx = CreateMaterial(
            MaterialRoot + "/GF_Cannon_Smoke.mat",
            vfxShader,
            new Color(0.22f, 0.13f, 0.08f, 0.24f),
            new Color(0.5f, 0.19f, 0.04f, 0.2f),
            0.35f);
        assets.Projectile = CreateMaterial(
            MaterialRoot + "/GF_CannonRound.mat",
            projectileShader,
            new Color(0.18f, 0.07f, 0.02f),
            new Color(1f, 0.32f, 0.025f),
            1.8f);
        assets.Tracer = CreateMaterial(
            MaterialRoot + "/GF_CannonTracer.mat",
            projectileShader,
            new Color(0.32f, 0.11f, 0.015f),
            new Color(1f, 0.78f, 0.18f),
            4.2f);
        assets.HeadHunter = CreateMaterial(
            MaterialRoot + "/GF_HeadHunterBolt.mat",
            projectileShader,
            new Color(0.025f, 0.1f, 0.26f),
            new Color(0.28f, 0.78f, 1f),
            5.2f);

        assets.Quality = AssetDatabase.LoadAssetAtPath<GameFeelQualitySettings>(QualityPath);
        if (assets.Quality == null)
        {
            assets.Quality = ScriptableObject.CreateInstance<GameFeelQualitySettings>();
            AssetDatabase.CreateAsset(assets.Quality, QualityPath);
        }

        return assets;
    }

    private static Dictionary<AutomaticCannonVfxStyle, GameObject> BuildCannonPrefabs(BuildAssets assets)
    {
        Dictionary<AutomaticCannonVfxStyle, GameObject> prefabs = new();
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.AutomaticShot, "Cannon_AutomaticShot", 0.09f, 0.72f, 4, 1, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.ManualShot, "Cannon_ManualShot", 0.14f, 1f, 7, 2, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.BaseActive, "Cannon_BaseActive", 0.48f, 1.7f, 18, 5, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.Impact, "Cannon_Impact", 0.32f, 0.85f, 7, 2, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.CriticalImpact, "Cannon_CriticalImpact", 0.46f, 1.35f, 14, 4, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.WeakPointImpact, "Cannon_WeakPointImpact", 0.5f, 1.5f, 16, 3, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.ContinuousLoop, "Cannon_ContinuousLoop", 0.2f, 0.9f, 22, 4, true);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.ContinuousStop, "Cannon_ContinuousStop", 0.34f, 0.95f, 8, 5, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.HeadHunterShot, "Cannon_HeadHunterShot", 0.2f, 1.1f, 7, 1, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.HeadHunterCharge, "Cannon_HeadHunterCharge", 1.2f, 1.25f, 14, 2, true);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.HeadHunterRelease, "Cannon_HeadHunterRelease", 0.58f, 2f, 22, 3, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.KillImpact, "Cannon_KillImpact", 0.65f, 1.8f, 24, 7, false);
        BuildCuePrefab(prefabs, assets, AutomaticCannonVfxStyle.HeatPulse, "Cannon_HeatPulse", 0.42f, 1.2f, 10, 4, false);
        return prefabs;
    }

    private static void BuildCuePrefab(
        Dictionary<AutomaticCannonVfxStyle, GameObject> result,
        BuildAssets assets,
        AutomaticCannonVfxStyle style,
        string name,
        float lifetime,
        float size,
        int sparkCount,
        int smokeCount,
        bool loop)
    {
        bool headHunter = style == AutomaticCannonVfxStyle.HeadHunterShot ||
                          style == AutomaticCannonVfxStyle.HeadHunterCharge ||
                          style == AutomaticCannonVfxStyle.HeadHunterRelease ||
                          style == AutomaticCannonVfxStyle.WeakPointImpact;
        bool impact = style == AutomaticCannonVfxStyle.Impact ||
                      style == AutomaticCannonVfxStyle.CriticalImpact ||
                      style == AutomaticCannonVfxStyle.WeakPointImpact ||
                      style == AutomaticCannonVfxStyle.KillImpact;
        bool ringStyle = impact || style == AutomaticCannonVfxStyle.BaseActive ||
                         style == AutomaticCannonVfxStyle.HeadHunterCharge ||
                         style == AutomaticCannonVfxStyle.HeatPulse;

        GameObject root = new(name);
        root.AddComponent<PooledWeaponVfx>();
        AutomaticCannonCueVfx animator = root.AddComponent<AutomaticCannonCueVfx>();

        Material primary = headHunter ? assets.BlueVfx : assets.OrangeVfx;
        GameObject core = CreateMeshLayer("Shader Core", root.transform, ringStyle ? assets.Ring : assets.Cone, primary);
        core.transform.localPosition = ringStyle ? Vector3.zero : Vector3.forward * 0.18f;
        core.transform.localScale = ringStyle ? Vector3.one : new Vector3(0.7f, 0.7f, 1.4f);

        GameObject accent = CreateMeshLayer("Emissive Accent", root.transform, ringStyle ? assets.Ring : assets.Bullet, headHunter ? assets.WhiteVfx : assets.WhiteVfx);
        accent.transform.localScale = ringStyle ? new Vector3(0.68f, 0.68f, 0.68f) : new Vector3(0.25f, 0.25f, 0.75f);
        accent.transform.localPosition = ringStyle ? Vector3.forward * 0.02f : Vector3.forward * 0.28f;

        ParticleSystem sparks = CreateParticles(
            "Uneven Scrap Sparks",
            root.transform,
            assets.Shard,
            headHunter ? assets.BlueVfx : assets.WhiteVfx,
            sparkCount,
            loop,
            impact ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone,
            impact ? 0.16f : 0.05f,
            impact ? 5.2f : 7.4f,
            0.18f);
        ParticleSystem smoke = CreateParticles(
            "Blocky Pressure Smoke",
            root.transform,
            assets.Cube,
            assets.SmokeVfx,
            smokeCount,
            loop && style == AutomaticCannonVfxStyle.ContinuousLoop,
            ParticleSystemShapeType.Sphere,
            0.14f,
            0.8f,
            0.48f);

        GameObject lightObject = new("Short Light Pulse");
        lightObject.transform.SetParent(root.transform, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = headHunter ? new Color(0.3f, 0.78f, 1f) : new Color(1f, 0.42f, 0.04f);
        light.intensity = impact ? 2.2f : 1.4f;
        light.range = impact ? 3.6f : 2.2f;
        light.shadows = LightShadows.None;

        SerializedObject serialized = new(animator);
        serialized.FindProperty("_style").enumValueIndex = (int)style;
        serialized.FindProperty("_primaryColor").colorValue = headHunter ? new Color(0.2f, 0.68f, 1f) : new Color(1f, 0.28f, 0.025f);
        serialized.FindProperty("_coreColor").colorValue = headHunter ? Color.white : new Color(1f, 0.95f, 0.62f);
        serialized.FindProperty("_lifetime").floatValue = lifetime;
        serialized.FindProperty("_size").floatValue = size;
        serialized.FindProperty("_baseEmission").floatValue = headHunter ? 3.6f : impact ? 2.8f : 2.3f;
        serialized.FindProperty("_rotationDegreesPerSecond").floatValue = ringStyle ? (headHunter ? 420f : 220f) : 0f;
        SetObjectArray(serialized.FindProperty("_meshLayers"), new UnityEngine.Object[]
        {
            core.GetComponent<Renderer>(), accent.GetComponent<Renderer>()
        });
        SetObjectArray(serialized.FindProperty("_particleLayers"), new UnityEngine.Object[] { sparks, smoke });
        SetObjectArray(serialized.FindProperty("_animatedRoots"), new UnityEngine.Object[] { core.transform, accent.transform });
        serialized.FindProperty("_lightPulse").objectReferenceValue = light;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        string path = CannonPrefabRoot + "/" + name + ".prefab";
        root.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        result[style] = prefab;
    }

    private static WeaponPresentationProfile BuildProfile(
        BuildAssets assets,
        Dictionary<AutomaticCannonVfxStyle, GameObject> prefabs)
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        AudioClip shoot = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/shoot.wav");

        List<WeaponPresentationCueData> cues = new()
        {
            Cue(WeaponPresentationCue.AutomaticCannonAutoBurst, null, Array.Empty<AudioClip>(), 0.8f, 0.98f, 1.02f, 0.12f, 0.02f, 4, false, new Vector3(0f, 0f, -0.045f), new Vector3(-0.14f, 0f, 0f), 0.25f),
            Cue(WeaponPresentationCue.AutomaticCannonAutoShot, prefabs[AutomaticCannonVfxStyle.AutomaticShot], new[] { shoot }, 0.4f, 0.96f, 1.04f, 0.1f, 0.018f, 20, false, new Vector3(0f, 0f, -0.018f), new Vector3(-0.055f, 0f, 0f), 0.12f),
            Cue(WeaponPresentationCue.AutomaticCannonManualVolley, null, Array.Empty<AudioClip>(), 1f, 0.94f, 1f, 0.18f, 0.04f, 4, false, new Vector3(0f, 0f, -0.06f), new Vector3(-0.18f, 0f, 0f), 0.35f),
            Cue(WeaponPresentationCue.AutomaticCannonManualShot, prefabs[AutomaticCannonVfxStyle.ManualShot], new[] { shoot }, 0.5f, 0.94f, 1.02f, 0.15f, 0.025f, 24, false, new Vector3(0f, 0f, -0.028f), new Vector3(-0.085f, 0f, 0f), 0.18f),
            Cue(WeaponPresentationCue.AutomaticCannonBaseActive, prefabs[AutomaticCannonVfxStyle.BaseActive], new[] { shoot }, 1f, 0.92f, 1f, 0.52f, 0.12f, 4, false, new Vector3(0f, 0f, -0.14f), new Vector3(-0.5f, 0f, 0f), 1.2f),
            Cue(WeaponPresentationCue.AutomaticCannonContinuousShot, prefabs[AutomaticCannonVfxStyle.AutomaticShot], new[] { shoot }, 0.18f, 1f, 1.08f, 0.08f, 0.045f, 20, false, new Vector3(0f, 0f, -0.01f), new Vector3(-0.035f, 0f, 0f), 0.08f),
            Cue(WeaponPresentationCue.AutomaticCannonContinuousActive, prefabs[AutomaticCannonVfxStyle.BaseActive], new[] { shoot }, 1f, 0.98f, 1.04f, 0.34f, 0.12f, 3, false, new Vector3(0f, 0f, -0.08f), new Vector3(-0.26f, 0f, 0f), 0.65f),
            Cue(WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic, prefabs[AutomaticCannonVfxStyle.HeadHunterShot], new[] { shoot }, 0.8f, 0.98f, 1.02f, 0.2f, 0.07f, 8, false, new Vector3(0f, 0f, -0.06f), new Vector3(-0.2f, 0f, 0f), 0.5f),
            Cue(WeaponPresentationCue.AutomaticCannonHeadHunterManual, prefabs[AutomaticCannonVfxStyle.HeadHunterShot], new[] { shoot }, 1f, 0.96f, 1f, 0.24f, 0.08f, 8, false, new Vector3(0f, 0f, -0.1f), new Vector3(-0.34f, 0f, 0f), 0.75f),
            Cue(WeaponPresentationCue.AutomaticCannonHeadHunterCharge, prefabs[AutomaticCannonVfxStyle.HeadHunterCharge], Array.Empty<AudioClip>(), 0.75f, 0.98f, 1.02f, 1.1f, 0.1f, 1, true, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.AutomaticCannonHeadHunterActive, prefabs[AutomaticCannonVfxStyle.HeadHunterRelease], new[] { shoot }, 1f, 0.94f, 0.98f, 0.62f, 0.2f, 3, false, new Vector3(0f, 0f, -0.22f), new Vector3(-0.8f, 0f, 0f), 1.8f),
            Cue(WeaponPresentationCue.AutomaticCannonImpact, prefabs[AutomaticCannonVfxStyle.Impact], Array.Empty<AudioClip>(), 0.45f, 0.96f, 1.06f, 0.34f, 0.02f, 28, false, Vector3.zero, Vector3.zero, 0f, 0f, 0),
            Cue(WeaponPresentationCue.AutomaticCannonCriticalImpact, prefabs[AutomaticCannonVfxStyle.CriticalImpact], Array.Empty<AudioClip>(), 0.75f, 1f, 1.08f, 0.48f, 0.04f, 14, false, new Vector3(0f, 0f, -0.018f), new Vector3(-0.06f, 0f, 0f), 0.2f, 0.014f, 1),
            Cue(WeaponPresentationCue.AutomaticCannonWeakPointImpact, prefabs[AutomaticCannonVfxStyle.WeakPointImpact], Array.Empty<AudioClip>(), 1f, 1f, 1.04f, 0.52f, 0.055f, 10, false, new Vector3(0f, 0f, -0.035f), new Vector3(-0.12f, 0f, 0f), 0.35f, 0.024f, 2),
            Cue(WeaponPresentationCue.AutomaticCannonContinuousLoop, prefabs[AutomaticCannonVfxStyle.ContinuousLoop], Array.Empty<AudioClip>(), 0.7f, 1f, 1.08f, 0f, 0.1f, 1, true, new Vector3(0f, 0f, -0.02f), new Vector3(-0.06f, 0f, 0f), 0.15f),
            Cue(WeaponPresentationCue.AutomaticCannonContinuousStop, prefabs[AutomaticCannonVfxStyle.ContinuousStop], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.36f, 0.08f, 4, false, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.AutomaticCannonKillImpact, prefabs[AutomaticCannonVfxStyle.KillImpact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.68f, 0.09f, 10, false, new Vector3(0f, 0f, -0.06f), new Vector3(-0.16f, 0f, 0f), 0.55f, 0.04f, 3),
            Cue(WeaponPresentationCue.AutomaticCannonHeatPulse, prefabs[AutomaticCannonVfxStyle.HeatPulse], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.45f, 0.15f, 3, false, Vector3.zero, Vector3.zero, 0.12f),
            Cue(WeaponPresentationCue.AutomaticCannonAmmoEmpty, null, Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.12f, 0.2f, 2, false, Vector3.zero, Vector3.zero, 0f)
        };

        List<WeaponFeedbackBinding> bindings = new()
        {
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonHeadHunterManual, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonHeadHunterActive, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonContinuousShot, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonContinuousShot, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonContinuousActive, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonAutoShot, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonManualShot, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.AutomaticCannonBaseActive, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.AutomaticCannonContinuousLoop, WeaponFeedbackModeFilter.Any, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.SustainedFireStopped, WeaponPresentationCue.AutomaticCannonContinuousStop, WeaponFeedbackModeFilter.Any, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ChargeStarted, WeaponPresentationCue.AutomaticCannonHeadHunterCharge, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.AutomaticCannonWeakPointImpact, weakPoint: FeedbackFilter.Required),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.AutomaticCannonCriticalImpact, critical: FeedbackFilter.Required),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.AutomaticCannonImpact),
            Binding(WeaponFeedbackEvent.DamageConfirmed, WeaponPresentationCue.AutomaticCannonKillImpact, kill: FeedbackFilter.Required),
            Binding(WeaponFeedbackEvent.AmmoEmpty, WeaponPresentationCue.AutomaticCannonAmmoEmpty),
            Binding(WeaponFeedbackEvent.HeatThresholdCrossed, WeaponPresentationCue.AutomaticCannonHeatPulse)
        };

        List<ProjectileArchetypePresentation> archetypes = new()
        {
            ProjectileArchetype(ProjectilePresentationArchetypeId.CannonRound, assets.Bullet, assets.Projectile, assets.OrangeVfx, new Vector3(0.24f, 0.24f, 0.72f), 0.028f, 0.026f, 0.004f, 0.32f),
            ProjectileArchetype(ProjectilePresentationArchetypeId.CannonTracer, assets.Bullet, assets.Tracer, assets.WhiteVfx, new Vector3(0.25f, 0.25f, 0.92f), 0.075f, 0.045f, 0f, 0.65f),
            ProjectileArchetype(ProjectilePresentationArchetypeId.HeadHunterBolt, assets.Bolt, assets.HeadHunter, assets.BlueVfx, new Vector3(0.18f, 0.18f, 1.7f), 0.22f, 0.16f, 0.015f, 1.25f)
        };

        SetPrivate(profile, "_weaponType", WeaponType.AutomaticCannon);
        SetPrivate(profile, "_defaultQuality", GameFeelQualityLevel.High);
        SetPrivate(profile, "_cues", cues);
        SetPrivate(profile, "_feedbackBindings", bindings);
        SetPrivate(profile, "_projectileArchetypes", archetypes);
        SetPrivate(profile, "_qualitySettings", assets.Quality);
        profile.RebuildCache();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static WeaponPresentationCueData Cue(
        WeaponPresentationCue cue,
        GameObject prefab,
        AudioClip[] clips,
        float volume,
        float pitchMin,
        float pitchMax,
        float duration,
        float replay,
        int maximum,
        bool loop,
        Vector3 cameraPosition,
        Vector3 cameraRotation,
        float fov,
        float hitStop = 0f,
        int hitStopPriority = 0)
    {
        WeaponPresentationCueData data = new()
        {
            Cue = cue,
            VfxPrefab = prefab,
            LayerAudioClips = false,
            MechanicalLayerVolume = 0.7f,
            ApplyHeatStrainToMechanicalLayer = false,
            Volume = volume,
            PitchMin = pitchMin,
            PitchMax = pitchMax,
            Duration = duration,
            MinReplayInterval = replay,
            PrewarmCount = Mathf.Min(maximum, loop ? 1 : Mathf.Clamp(maximum / 2, 1, 12)),
            MaxSimultaneous = maximum,
            Loop = loop,
            CameraPositionImpulse = cameraPosition,
            CameraRotationImpulse = cameraRotation,
            CameraFovKick = fov,
            CameraMinReplayInterval = replay,
            HitStopDuration = hitStop,
            HitStopPriority = hitStopPriority,
            EssentialGameplayCue = cue != WeaponPresentationCue.AutomaticCannonHeatPulse,
            SecondaryEffect = cue == WeaponPresentationCue.AutomaticCannonHeatPulse,
            MinimumQuality = GameFeelQualityLevel.Low,
            SpatialBlend = 0.75f,
            MinimumDistance = 1f,
            MaximumDistance = 35f,
            AudioPriority = hitStopPriority > 1 ? 70 : 120
        };
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                data.AudioClips.Add(clips[i]);
        }
        data.Sanitize();
        return data;
    }

    private static WeaponFeedbackBinding Binding(
        WeaponFeedbackEvent feedbackEvent,
        WeaponPresentationCue cue,
        WeaponFeedbackModeFilter mode = WeaponFeedbackModeFilter.Any,
        WeaponUpgradePathFilter path = WeaponUpgradePathFilter.Any,
        FeedbackFilter critical = FeedbackFilter.Any,
        FeedbackFilter weakPoint = FeedbackFilter.Any,
        FeedbackFilter kill = FeedbackFilter.Any)
    {
        return new WeaponFeedbackBinding
        {
            Event = feedbackEvent,
            Cue = cue,
            Mode = mode,
            UpgradePath = path,
            Critical = critical,
            WeakPoint = weakPoint,
            Kill = kill
        };
    }

    private static ProjectileArchetypePresentation ProjectileArchetype(
        ProjectilePresentationArchetypeId id,
        Mesh mesh,
        Material material,
        Material trail,
        Vector3 scale,
        float trailLifetime,
        float startWidth,
        float endWidth,
        float lightIntensity)
    {
        return new ProjectileArchetypePresentation
        {
            Archetype = id,
            Mesh = mesh,
            Material = material,
            TrailMaterial = trail,
            LocalScale = scale,
            TrailLifetime = trailLifetime,
            TrailStartWidth = startWidth,
            TrailEndWidth = endWidth,
            LightIntensity = lightIntensity,
            LightRange = 2.4f,
            BaseEmission = 1.2f
        };
    }

    private static void MigrateProjectilePrefab(BuildAssets assets)
    {
        const string path = "Assets/Prefabs/Projectile.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            MeshFilter originalFilter = root.GetComponent<MeshFilter>();
            MeshRenderer originalRenderer = root.GetComponent<MeshRenderer>();
            Mesh defaultMesh = originalFilter != null ? originalFilter.sharedMesh : assets.Bullet;
            Material defaultMaterial = originalRenderer != null ? originalRenderer.sharedMaterial : assets.Projectile;

            Transform existingVisual = root.transform.Find("Visual");
            if (existingVisual != null)
                UnityEngine.Object.DestroyImmediate(existingVisual.gameObject);
            if (originalFilter != null)
                UnityEngine.Object.DestroyImmediate(originalFilter);
            if (originalRenderer != null)
                UnityEngine.Object.DestroyImmediate(originalRenderer);

            GameObject visual = CreateMeshLayer("Visual", root.transform, defaultMesh, defaultMaterial);
            visual.transform.localScale = Vector3.one;
            TrailRenderer trail = visual.AddComponent<TrailRenderer>();
            trail.enabled = false;
            trail.time = 0.08f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0f;
            trail.sharedMaterial = assets.OrangeVfx;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;

            GameObject lightObject = new("Projectile Light");
            lightObject.transform.SetParent(visual.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.enabled = false;

            ProjectileVisualController controller = root.GetComponent<ProjectileVisualController>();
            if (controller == null)
                controller = root.AddComponent<ProjectileVisualController>();
            SerializedObject serialized = new(controller);
            serialized.FindProperty("_visualRoot").objectReferenceValue = visual.transform;
            serialized.FindProperty("_meshFilter").objectReferenceValue = visual.GetComponent<MeshFilter>();
            serialized.FindProperty("_meshRenderer").objectReferenceValue = visual.GetComponent<MeshRenderer>();
            serialized.FindProperty("_trail").objectReferenceValue = trail;
            serialized.FindProperty("_light").objectReferenceValue = light;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AddPlayerRecoilRig(BuildAssets assets)
    {
        const string path = "Assets/Prefabs/player.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform firePoint = FindTransform(root.transform, "firepoint");
            if (firePoint == null)
                firePoint = FindTransform(root.transform, "fire");
            if (firePoint == null)
                return;

            Transform existing = firePoint.Find("Automatic Cannon Presentation Rig");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject rig = new("Automatic Cannon Presentation Rig");
            rig.transform.SetParent(firePoint, false);
            rig.transform.localPosition = Vector3.zero;

            WeaponRecoilFeedback recoil = root.GetComponent<WeaponRecoilFeedback>();
            if (recoil == null)
                recoil = root.AddComponent<WeaponRecoilFeedback>();
            SerializedObject recoilSerialized = new(recoil);
            recoilSerialized.FindProperty("_recoilRoot").objectReferenceValue = rig.transform;
            SetObjectArray(recoilSerialized.FindProperty("_heatRenderers"), Array.Empty<Renderer>());
            recoilSerialized.ApplyModifiedPropertiesWithoutUndo();

            WeaponPresentationController presentation = root.GetComponent<WeaponPresentationController>();
            if (presentation != null)
            {
                SerializedObject presentationSerialized = new(presentation);
                presentationSerialized.FindProperty("_recoilFeedback").objectReferenceValue = recoil;
                presentationSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AddEnemyFeedbackComponents()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool isEnemy = root.GetComponentInChildren<EnemyHealth>(true) != null ||
                           root.GetComponentInChildren<WeaponDummyEnemy>(true) != null;
            if (isEnemy)
            {
                if (root.GetComponent<EnemyHitFeedback>() == null)
                    root.AddComponent<EnemyHitFeedback>();
                if (root.GetComponent<EnemyDeathFeedback>() == null)
                    root.AddComponent<EnemyDeathFeedback>();
                if (root.GetComponent<EnemyStatusFeedback>() == null)
                    root.AddComponent<EnemyStatusFeedback>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void DuplicateSandbox()
    {
        const string source = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";
        const string target = "Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(target) == null && !AssetDatabase.CopyAsset(source, target))
            Debug.LogWarning("Could not duplicate the weapon sandbox for Game Feel testing.");
    }

    private static void AssignProductionProfile(WeaponPresentationProfile profile)
    {
        string[] weaponAssetPaths =
        {
            "Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset",
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset"
        };
        for (int i = 0; i < weaponAssetPaths.Length; i++)
        {
            WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(weaponAssetPaths[i]);
            if (data == null)
                continue;
            data.PresentationProfile = profile;
            EditorUtility.SetDirty(data);
        }
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
        bool loop,
        ParticleSystemShapeType shapeType,
        float radius,
        float speed,
        float lifetime)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        ParticleSystem particles = layer.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = loop;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, lifetime);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime * 1.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.11f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.simulationSpace = loop ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = loop ? Mathf.Max(1f, count * 2f) : 0f;
        if (!loop)
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = shapeType;
        shape.radius = radius;
        if (shapeType == ParticleSystemShapeType.Cone)
            shape.angle = 12f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.25f, 0.04f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        ParticleSystemRenderer renderer = layer.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static Material CreateMaterial(
        string path,
        Shader shader,
        Color baseColor,
        Color emissionColor,
        float emissionIntensity)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_EmissionColor", emissionColor);
        material.SetFloat("_EmissionIntensity", emissionIntensity);
        EditorUtility.SetDirty(material);
        return material;
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

    private static Mesh CreateCone(string name, int sides, float radius, float length)
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();
        vertices.Add(Vector3.zero);
        vertices.Add(Vector3.forward * length);
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = 2 + i;
            int b = 2 + (i + 1) % sides;
            triangles.Add(1); triangles.Add(a); triangles.Add(b);
            triangles.Add(0); triangles.Add(b); triangles.Add(a);
        }
        return CreateMesh(name, vertices, triangles);
    }

    private static Mesh CreateCylinder(string name, int sides, float radius, float length, bool tapered)
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();
        float backRadius = tapered ? radius * 0.72f : radius;
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            Vector2 radial = new(Mathf.Cos(angle), Mathf.Sin(angle));
            vertices.Add(new Vector3(radial.x * backRadius, radial.y * backRadius, -length * 0.5f));
            vertices.Add(new Vector3(radial.x * radius, radial.y * radius, length * 0.5f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2;
            int b = ((i + 1) % sides) * 2;
            triangles.Add(a); triangles.Add(a + 1); triangles.Add(b + 1);
            triangles.Add(a); triangles.Add(b + 1); triangles.Add(b);
        }
        return CreateMesh(name, vertices, triangles);
    }

    private static Mesh CreateRing(string name, int majorSegments, int minorSegments, float radius, float thickness)
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();
        for (int i = 0; i < majorSegments; i++)
        {
            float major = i / (float)majorSegments * Mathf.PI * 2f;
            Vector3 outward = new(Mathf.Cos(major), Mathf.Sin(major), 0f);
            for (int j = 0; j < minorSegments; j++)
            {
                float minor = j / (float)minorSegments * Mathf.PI * 2f;
                vertices.Add(outward * (radius + Mathf.Cos(minor) * thickness) + Vector3.forward * (Mathf.Sin(minor) * thickness));
            }
        }
        for (int i = 0; i < majorSegments; i++)
        {
            for (int j = 0; j < minorSegments; j++)
            {
                int a = i * minorSegments + j;
                int b = ((i + 1) % majorSegments) * minorSegments + j;
                int c = ((i + 1) % majorSegments) * minorSegments + (j + 1) % minorSegments;
                int d = i * minorSegments + (j + 1) % minorSegments;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }
        return CreateMesh(name, vertices, triangles);
    }

    private static Mesh CreateShard(string name)
    {
        List<Vector3> vertices = new()
        {
            new(-0.08f, -0.05f, -0.18f),
            new(0.09f, -0.04f, -0.12f),
            new(0f, 0.08f, -0.06f),
            new(0.02f, 0f, 0.24f)
        };
        List<int> triangles = new() { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 };
        return CreateMesh(name, vertices, triangles);
    }

    private static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
    {
        Mesh mesh = new() { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        List<Vector2> uvs = new(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 vertex = vertices[i];
            uvs.Add(new Vector2(
                vertex.x + vertex.z * 0.37f,
                vertex.y + vertex.z * 0.73f));
        }
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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

    private static Transform FindTransform(Transform root, string normalizedNeedle)
    {
        string normalizedName = root.name.Replace(" ", string.Empty).ToLowerInvariant();
        if (normalizedName.Contains(normalizedNeedle))
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform match = FindTransform(root.GetChild(i), normalizedNeedle);
            if (match != null)
                return match;
        }
        return null;
    }
}
#endif
