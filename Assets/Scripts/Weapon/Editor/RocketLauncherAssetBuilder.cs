#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RocketLauncherAssetBuilder
{
    private const string GameFeelRoot = "Assets/GameFeel";
    private const string MeshRoot = GameFeelRoot + "/Meshes";
    private const string MaterialRoot = GameFeelRoot + "/Materials";
    private const string PrefabRoot = GameFeelRoot + "/Prefabs/Weapons/RocketLauncher";
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset";
    private const string QualityPath = GameFeelRoot + "/Profiles/GameFeelQuality_PC.asset";

    private sealed class BuildAssets
    {
        public Mesh Rocket;
        public Mesh Ring;
        public Mesh Cone;
        public Mesh Shard;
        public Mesh Sphere;
        public Material Fire;
        public Material Core;
        public Material Smoke;
        public Material Kinetic;
        public Material Fragment;
        public Material RocketBody;
        public Material FragmentBody;
        public GameFeelQualitySettings Quality;
    }

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Rocket Launcher Production Assets")]
    public static void BuildFromMenu()
    {
        BuildBatch();
        Debug.Log("Rocket Launcher production presentation rebuilt.");
    }

    public static void BuildBatch()
    {
        EnsureFolders();
        AssetDatabase.Refresh();
        BuildAssets assets = BuildSharedAssets();
        Dictionary<RocketLauncherVfxStyle, GameObject> prefabs = BuildPrefabs(assets);
        WeaponPresentationProfile profile = BuildProfile(assets, prefabs);
        AddFlightSmokeToProjectilePrefab(assets);
        AssignProfile(profile);
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
        EnsureFolder(GameFeelRoot + "/Prefabs/Weapons", "RocketLauncher");
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
        Shader projectileShader = Shader.Find("ScrapWaves/GameFeel/Scrap Projectile");
        if (vfxShader == null || projectileShader == null)
            throw new InvalidOperationException("Game Feel shaders must be imported before building Rocket assets.");

        BuildAssets assets = new()
        {
            Rocket = SaveMesh(MeshRoot + "/GF_Rocket.asset", CreateRocketMesh()),
            Ring = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_PressureRing.asset"),
            Cone = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_CannonCone.asset"),
            Shard = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/GF_ScrapShard.asset"),
            Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
            Fire = CreateMaterial(MaterialRoot + "/GF_Rocket_Fire.mat", vfxShader, new Color(1f, 0.18f, 0.015f), new Color(1f, 0.68f, 0.08f), 4.2f),
            Core = CreateMaterial(MaterialRoot + "/GF_Rocket_Core.mat", vfxShader, new Color(1f, 0.72f, 0.16f), new Color(1f, 0.98f, 0.72f), 5.5f),
            Smoke = CreateMaterial(MaterialRoot + "/GF_Rocket_Smoke.mat", vfxShader, new Color(0.16f, 0.12f, 0.09f, 0.34f), new Color(0.34f, 0.2f, 0.08f, 0.22f), 0.3f),
            Kinetic = CreateMaterial(MaterialRoot + "/GF_Rocket_Kinetic.mat", vfxShader, new Color(0.24f, 0.56f, 0.76f), new Color(0.7f, 0.96f, 1f), 4.8f),
            Fragment = CreateMaterial(MaterialRoot + "/GF_Rocket_Fragment.mat", vfxShader, new Color(0.64f, 0.035f, 0.015f), new Color(1f, 0.42f, 0.035f), 4.4f),
            RocketBody = CreateMaterial(MaterialRoot + "/GF_Rocket_Body.mat", projectileShader, new Color(0.11f, 0.095f, 0.08f), new Color(1f, 0.28f, 0.025f), 2.4f),
            FragmentBody = CreateMaterial(MaterialRoot + "/GF_FragmentRocket_Body.mat", projectileShader, new Color(0.22f, 0.035f, 0.018f), new Color(1f, 0.58f, 0.06f), 3.2f),
            Quality = AssetDatabase.LoadAssetAtPath<GameFeelQualitySettings>(QualityPath)
        };

        if (assets.Ring == null || assets.Cone == null || assets.Shard == null)
            throw new InvalidOperationException("Build the shared Cannon foundation before the Rocket Launcher slice.");
        return assets;
    }

    private static Dictionary<RocketLauncherVfxStyle, GameObject> BuildPrefabs(BuildAssets assets)
    {
        Dictionary<RocketLauncherVfxStyle, GameObject> result = new();
        BuildPrefab(result, assets, RocketLauncherVfxStyle.Launch, "Rocket_Launch", 0.34f, 1.1f, 14, 7, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.Impact, "Rocket_Impact", 0.78f, 1.25f, 24, 13, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.KineticImpact, "Rocket_KineticImpact", 0.95f, 1.45f, 28, 15, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.FragmentImpact, "Rocket_FragmentImpact", 0.72f, 1.15f, 34, 14, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.ClusterLaunch, "Rocket_ClusterLaunch", 1.05f, 1.75f, 52, 22, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.TargetingLoop, "Rocket_TargetingLoop", 0.7f, 0.75f, 4, 2, true);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.KineticStatus, "Rocket_KineticStatus", 0.8f, 0.9f, 8, 4, false);
        BuildPrefab(result, assets, RocketLauncherVfxStyle.KillImpact, "Rocket_KillImpact", 1.15f, 1.8f, 44, 24, false);
        return result;
    }

    private static void BuildPrefab(
        Dictionary<RocketLauncherVfxStyle, GameObject> result,
        BuildAssets assets,
        RocketLauncherVfxStyle style,
        string name,
        float lifetime,
        float size,
        int debrisCount,
        int smokeCount,
        bool loop)
    {
        bool launch = style == RocketLauncherVfxStyle.Launch;
        bool kinetic = style == RocketLauncherVfxStyle.KineticImpact || style == RocketLauncherVfxStyle.KineticStatus;
        bool fragment = style == RocketLauncherVfxStyle.FragmentImpact || style == RocketLauncherVfxStyle.ClusterLaunch;
        bool directionalFragment = style == RocketLauncherVfxStyle.FragmentImpact;
        bool targeting = style == RocketLauncherVfxStyle.TargetingLoop;
        Material primary = kinetic ? assets.Kinetic : fragment ? assets.Fragment : assets.Fire;

        GameObject root = new(name);
        root.AddComponent<PooledWeaponVfx>();
        RocketLauncherCueVfx animator = root.AddComponent<RocketLauncherCueVfx>();

        List<Renderer> meshLayers = new();
        List<Transform> animatedRoots = new();
        Transform forwardMiniExplosionRoot = null;
        List<Transform> forwardMiniExplosions = new();
        if (launch)
        {
            GameObject backblast = CreateMeshLayer("Backblast Cone", root.transform, assets.Cone, assets.Fire);
            backblast.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            backblast.transform.localPosition = Vector3.back * 0.15f;
            backblast.transform.localScale = new Vector3(0.75f, 0.75f, 1.5f);
            GameObject muzzleCore = CreateMeshLayer("Launch Core", root.transform, assets.Cone, assets.Core);
            muzzleCore.transform.localScale = new Vector3(0.38f, 0.38f, 0.75f);
            meshLayers.Add(backblast.GetComponent<Renderer>());
            meshLayers.Add(muzzleCore.GetComponent<Renderer>());
            animatedRoots.Add(backblast.transform);
            animatedRoots.Add(muzzleCore.transform);
        }
        else
        {
            GameObject core = CreateMeshLayer("Explosion Core", root.transform, assets.Sphere, kinetic ? assets.Kinetic : assets.Core);
            meshLayers.Add(core.GetComponent<Renderer>());
            animatedRoots.Add(core.transform);
            if (directionalFragment)
            {
                core.transform.localScale = Vector3.one * 0.7f;
                GameObject radiusRing = CreateMeshLayer("Explosion Radius", root.transform, assets.Ring, primary);
                radiusRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                radiusRing.transform.localScale = Vector3.one * 2f;
                GameObject pressureRing = CreateMeshLayer("Pressure Front", root.transform, assets.Ring, assets.Core);
                pressureRing.transform.localScale = Vector3.one * 1.25f;
                meshLayers.Add(radiusRing.GetComponent<Renderer>());
                meshLayers.Add(pressureRing.GetComponent<Renderer>());
                animatedRoots.Add(radiusRing.transform);
                animatedRoots.Add(pressureRing.transform);

                GameObject miniRootObject = new("Forward Mini Explosions");
                miniRootObject.transform.SetParent(root.transform, false);
                forwardMiniExplosionRoot = miniRootObject.transform;
                Vector3[] normalizedPositions =
                {
                    new(0f, 0f, 0.18f),
                    new(-0.08f, 0f, 0.38f),
                    new(0.08f, 0f, 0.38f),
                    new(0f, 0f, 0.57f),
                    new(-0.2f, 0f, 0.72f),
                    new(0.2f, 0f, 0.72f),
                    new(0f, 0f, 0.92f)
                };
                for (int i = 0; i < normalizedPositions.Length; i++)
                {
                    GameObject miniExplosion = new($"Forward Mini Explosion {i + 1}");
                    miniExplosion.transform.SetParent(forwardMiniExplosionRoot, false);
                    miniExplosion.transform.localPosition = normalizedPositions[i];
                    float depthScale = Mathf.Lerp(0.82f, 1.12f, normalizedPositions[i].z);
                    miniExplosion.transform.localScale = Vector3.one * depthScale;

                    GameObject miniCore = CreateMeshLayer("Mini Core", miniExplosion.transform, assets.Sphere, assets.Core);
                    miniCore.transform.localScale = Vector3.one * 0.07f;
                    GameObject miniRing = CreateMeshLayer("Mini Ring", miniExplosion.transform, assets.Ring, primary);
                    miniRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    miniRing.transform.localScale = Vector3.one * 0.1f;
                    meshLayers.Add(miniCore.GetComponent<Renderer>());
                    meshLayers.Add(miniRing.GetComponent<Renderer>());
                    forwardMiniExplosions.Add(miniExplosion.transform);
                }
            }
            else
            {
                core.transform.localScale = targeting ? Vector3.one * 0.18f : Vector3.one * 0.55f;
                GameObject pressureRing = CreateMeshLayer("Pressure Front", root.transform, assets.Ring, primary);
                pressureRing.transform.localScale = targeting ? Vector3.one * 0.65f : Vector3.one;
                GameObject crossedRing = CreateMeshLayer("Cross Pressure Front", root.transform, assets.Ring, primary);
                crossedRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                crossedRing.transform.localScale = targeting ? Vector3.one * 0.45f : Vector3.one * 0.72f;
                meshLayers.Add(pressureRing.GetComponent<Renderer>());
                meshLayers.Add(crossedRing.GetComponent<Renderer>());
                animatedRoots.Add(pressureRing.transform);
                animatedRoots.Add(crossedRing.transform);
            }
        }

        ParticleSystem debris = CreateParticles(
            "Sparks and Mesh Debris",
            root.transform,
            assets.Shard,
            primary,
            debrisCount,
            loop,
            launch || directionalFragment ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere,
            launch ? 0.16f : directionalFragment ? 0.12f : 0.42f,
            launch ? 5.5f : fragment ? 9f : 6.5f,
            launch ? 0.34f : 0.72f,
            directionalFragment ? 22.5f : 16f);
        if (launch)
            debris.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        ParticleSystem smoke = CreateParticles(
            "Blocky Smoke",
            root.transform,
            assets.Shard,
            assets.Smoke,
            smokeCount,
            loop,
            launch ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere,
            launch ? 0.2f : 0.55f,
            launch ? 2.2f : 3.2f,
            launch ? 0.65f : 1.15f);
        if (launch)
            smoke.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject lightObject = new("Rocket Light Pulse");
        lightObject.transform.SetParent(root.transform, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.range = launch ? 5f : 9f;
        light.intensity = launch ? 4f : 7f;
        light.color = kinetic ? new Color(0.6f, 0.9f, 1f) : fragment ? new Color(1f, 0.22f, 0.02f) : new Color(1f, 0.62f, 0.12f);

        SerializedObject serialized = new(animator);
        serialized.FindProperty("_style").enumValueIndex = (int)style;
        serialized.FindProperty("_primaryColor").colorValue = light.color;
        serialized.FindProperty("_coreColor").colorValue = kinetic ? Color.white : new Color(1f, 0.96f, 0.64f);
        serialized.FindProperty("_lifetime").floatValue = lifetime;
        serialized.FindProperty("_size").floatValue = directionalFragment ? 1f : size;
        serialized.FindProperty("_baseEmission").floatValue = kinetic ? 4.6f : fragment ? 4.2f : 3.6f;
        serialized.FindProperty("_rotationDegreesPerSecond").floatValue = targeting ? 270f : kinetic ? 190f : 125f;
        serialized.FindProperty("_scaleFromExplosionRadius").boolValue = !launch && !targeting && style != RocketLauncherVfxStyle.KineticStatus;
        serialized.FindProperty("_explosionRadiusMultiplier").floatValue = directionalFragment ? 1f : 0.55f;
        serialized.FindProperty("_forwardMiniExplosionRoot").objectReferenceValue = forwardMiniExplosionRoot;
        SetObjectArray(serialized.FindProperty("_forwardMiniExplosions"), forwardMiniExplosions.ToArray());
        serialized.FindProperty("_forwardConeRangeMultiplier").floatValue = directionalFragment ? 4f : 0f;
        if (kinetic && style == RocketLauncherVfxStyle.KineticImpact)
        {
            serialized.FindProperty("_scaleOverLife").animationCurveValue = new AnimationCurve(
                new Keyframe(0f, 1.25f),
                new Keyframe(0.16f, 0.18f),
                new Keyframe(0.28f, 0.32f),
                new Keyframe(1f, 1.3f));
        }
        SetObjectArray(serialized.FindProperty("_meshLayers"), meshLayers.ToArray());
        SetObjectArray(serialized.FindProperty("_particleLayers"), new UnityEngine.Object[] { debris, smoke });
        SetObjectArray(serialized.FindProperty("_animatedRoots"), animatedRoots.ToArray());
        serialized.FindProperty("_lightPulse").objectReferenceValue = light;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        result[style] = prefab;
    }

    private static WeaponPresentationProfile BuildProfile(
        BuildAssets assets,
        Dictionary<RocketLauncherVfxStyle, GameObject> prefabs)
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        List<WeaponPresentationCueData> cues = new()
        {
            Cue(WeaponPresentationCue.RocketAutomaticLaunch, prefabs[RocketLauncherVfxStyle.Launch], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.34f, 0.08f, 8, false, new Vector3(0f, 0f, -0.12f), new Vector3(-0.42f, 0f, 0f), 0.8f),
            Cue(WeaponPresentationCue.RocketManualLaunch, prefabs[RocketLauncherVfxStyle.Launch], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.38f, 0.08f, 6, false, new Vector3(0f, 0f, -0.16f), new Vector3(-0.56f, 0f, 0f), 1.05f),
            Cue(WeaponPresentationCue.RocketActiveLaunch, prefabs[RocketLauncherVfxStyle.Launch], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.48f, 0.12f, 5, false, new Vector3(0f, 0f, -0.22f), new Vector3(-0.78f, 0f, 0f), 1.45f),
            Cue(WeaponPresentationCue.RocketClusterLaunch, prefabs[RocketLauncherVfxStyle.Launch], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.58f, 0.16f, 3, false, new Vector3(0f, 0f, -0.28f), new Vector3(-0.96f, 0f, 0f), 1.8f),
            Cue(WeaponPresentationCue.RocketClusterDetonation, prefabs[RocketLauncherVfxStyle.ClusterLaunch], Array.Empty<AudioClip>(), 0f, 1f, 1f, 1.08f, 0.14f, 4, false, new Vector3(0f, 0f, -0.26f), new Vector3(-0.84f, 0f, 0f), 1.7f, 0.025f, 3),
            Cue(WeaponPresentationCue.RocketFragmentChildImpact, prefabs[RocketLauncherVfxStyle.FragmentImpact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.76f, 0f, 20, false, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.RocketTargetingLoop, prefabs[RocketLauncherVfxStyle.TargetingLoop], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.7f, 0.05f, 1, true, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.RocketLockAcquired, null, Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.16f, 0.04f, 6, false, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.RocketTargetingCancelled, null, Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.18f, 0.08f, 2, false, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.RocketImpact, prefabs[RocketLauncherVfxStyle.Impact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.82f, 0.06f, 14, false, new Vector3(0f, 0f, -0.18f), new Vector3(-0.56f, 0f, 0f), 1.2f, 0.02f, 2),
            Cue(WeaponPresentationCue.RocketKineticImpact, prefabs[RocketLauncherVfxStyle.KineticImpact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.98f, 0.09f, 10, false, new Vector3(0f, 0f, -0.28f), new Vector3(-0.9f, 0f, 0f), 1.8f, 0.035f, 3),
            Cue(WeaponPresentationCue.RocketFragmentImpact, prefabs[RocketLauncherVfxStyle.FragmentImpact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.76f, 0.04f, 20, false, new Vector3(0f, 0f, -0.12f), new Vector3(-0.38f, 0f, 0f), 0.85f, 0.012f, 1),
            Cue(WeaponPresentationCue.RocketKineticStatus, prefabs[RocketLauncherVfxStyle.KineticStatus], Array.Empty<AudioClip>(), 0f, 1f, 1f, 0.72f, 0.12f, 12, false, Vector3.zero, Vector3.zero, 0f),
            Cue(WeaponPresentationCue.RocketKillImpact, prefabs[RocketLauncherVfxStyle.KillImpact], Array.Empty<AudioClip>(), 0f, 1f, 1f, 1.18f, 0.12f, 8, false, new Vector3(0f, 0f, -0.3f), new Vector3(-1f, 0f, 0f), 2f, 0.045f, 4)
        };

        List<WeaponFeedbackBinding> bindings = new()
        {
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RocketClusterLaunch, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RocketActiveLaunch, WeaponFeedbackModeFilter.Active),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RocketManualLaunch, WeaponFeedbackModeFilter.Manual),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.RocketAutomaticLaunch, WeaponFeedbackModeFilter.Automatic),
            Binding(WeaponFeedbackEvent.ChargeStarted, WeaponPresentationCue.RocketTargetingLoop, WeaponFeedbackModeFilter.Active),
            Binding(WeaponFeedbackEvent.ChargeCancelled, WeaponPresentationCue.RocketTargetingCancelled, WeaponFeedbackModeFilter.Active),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.RocketKineticImpact, path: WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.RocketFragmentImpact, path: WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ProjectileImpact, WeaponPresentationCue.RocketImpact, path: WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.StatusApplied, WeaponPresentationCue.RocketKineticStatus, path: WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.DamageConfirmed, WeaponPresentationCue.RocketKillImpact, kill: FeedbackFilter.Required)
        };

        List<ProjectileArchetypePresentation> archetypes = new()
        {
            ProjectileArchetype(ProjectilePresentationArchetypeId.Rocket, assets.Rocket, assets.RocketBody, assets.Fire, assets.Smoke, new Vector3(0.52f, 0.52f, 1.35f), 0.24f, 0.2f, 0.025f, 1.2f, 24f, 0.15f, 0.52f),
            ProjectileArchetype(ProjectilePresentationArchetypeId.FragmentRocket, assets.Rocket, assets.FragmentBody, assets.Fragment, null, new Vector3(0.25f, 0.25f, 0.7f), 0.11f, 0.085f, 0f, 0f, 0f, 0f, 0f),
            ProjectileArchetype(ProjectilePresentationArchetypeId.ClusterRocket, assets.Rocket, assets.FragmentBody, assets.Fragment, assets.Smoke, new Vector3(0.48f, 0.48f, 1.2f), 0.2f, 0.16f, 0.015f, 0.9f, 18f, 0.18f, 0.62f)
        };

        SetPrivate(profile, "_weaponType", WeaponType.RocketLauncher);
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
            ApplyEventIntensityToPitch = false,
            Volume = volume,
            PitchMin = pitchMin,
            PitchMax = pitchMax,
            Duration = duration,
            MinReplayInterval = replay,
            PrewarmCount = Mathf.Min(maximum, loop ? 1 : Mathf.Clamp(maximum / 2, 1, 10)),
            MaxSimultaneous = maximum,
            Loop = loop,
            CameraPositionImpulse = cameraPosition,
            CameraRotationImpulse = cameraRotation,
            CameraFovKick = fov,
            CameraMinReplayInterval = replay,
            HitStopDuration = hitStop,
            HitStopPriority = hitStopPriority,
            EssentialGameplayCue = cue != WeaponPresentationCue.RocketKineticStatus,
            SecondaryEffect = cue == WeaponPresentationCue.RocketKineticStatus,
            MinimumQuality = GameFeelQualityLevel.Low,
            SpatialBlend = 0.9f,
            MinimumDistance = 1.2f,
            MaximumDistance = 55f,
            AudioPriority = hitStopPriority > 1 ? 55 : 105
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
        FeedbackFilter kill = FeedbackFilter.Any)
    {
        return new WeaponFeedbackBinding
        {
            Event = feedbackEvent,
            Cue = cue,
            Mode = mode,
            UpgradePath = path,
            Kill = kill
        };
    }

    private static ProjectileArchetypePresentation ProjectileArchetype(
        ProjectilePresentationArchetypeId id,
        Mesh mesh,
        Material material,
        Material trail,
        Material smoke,
        Vector3 scale,
        float trailLifetime,
        float startWidth,
        float endWidth,
        float lightIntensity,
        float smokeRate,
        float smokeSize,
        float smokeLifetime)
    {
        return new ProjectileArchetypePresentation
        {
            Archetype = id,
            Mesh = mesh,
            Material = material,
            TrailMaterial = trail,
            FlightSmokeMaterial = smoke,
            LocalScale = scale,
            TrailLifetime = trailLifetime,
            TrailStartWidth = startWidth,
            TrailEndWidth = endWidth,
            LightIntensity = lightIntensity,
            LightRange = 4.5f,
            BaseEmission = 1.65f,
            FlightSmokeRate = smokeRate,
            FlightSmokeSize = smokeSize,
            FlightSmokeLifetime = smokeLifetime
        };
    }

    private static void AddFlightSmokeToProjectilePrefab(BuildAssets assets)
    {
        const string path = "Assets/Prefabs/Projectile.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform visual = root.transform.Find("Visual");
            if (visual == null)
                return;

            Transform existing = visual.Find("Rocket Flight Smoke");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject smokeObject = new("Rocket Flight Smoke");
            smokeObject.transform.SetParent(visual, false);
            smokeObject.transform.localPosition = Vector3.back * 0.45f;
            ParticleSystem smoke = smokeObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = smoke.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 0.52f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            main.stopAction = ParticleSystemStopAction.None;
            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule color = smoke.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.48f, 0.4f, 0.32f), 0f), new GradientColorKey(new Color(0.13f, 0.11f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystemRenderer renderer = smokeObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = assets.Shard;
            renderer.sharedMaterial = assets.Smoke;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ProjectileVisualController controller = root.GetComponent<ProjectileVisualController>();
            if (controller != null)
            {
                SerializedObject serialized = new(controller);
                serialized.FindProperty("_flightSmoke").objectReferenceValue = smoke;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignProfile(WeaponPresentationProfile profile)
    {
        string[] paths =
        {
            "Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset",
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_RocketLauncher.asset"
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
        float lifetime,
        float coneAngle = 16f)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        ParticleSystem particles = layer.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = loop;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, lifetime);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime * 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.simulationSpace = loop ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = loop ? Mathf.Max(1f, count * 1.8f) : 0f;
        if (!loop)
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
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.22f, 0.025f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
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

    private static Material CreateMaterial(string path, Shader shader, Color baseColor, Color emissionColor, float emissionIntensity)
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

    private static Mesh CreateRocketMesh()
    {
        const int sides = 8;
        List<Vector3> vertices = new();
        List<int> triangles = new();
        float[] radii = { 0.18f, 0.24f, 0.24f, 0f };
        float[] z = { -0.62f, -0.48f, 0.28f, 0.72f };
        for (int section = 0; section < radii.Length; section++)
        {
            for (int i = 0; i < sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radii[section], Mathf.Sin(angle) * radii[section], z[section]));
            }
        }
        for (int section = 0; section < radii.Length - 1; section++)
        {
            for (int i = 0; i < sides; i++)
            {
                int a = section * sides + i;
                int b = section * sides + (i + 1) % sides;
                int c = (section + 1) * sides + (i + 1) % sides;
                int d = (section + 1) * sides + i;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
            }
        }

        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 radial = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 tangent = new(-radial.y, radial.x, 0f);
            int start = vertices.Count;
            vertices.Add(radial * 0.16f + tangent * 0.025f + Vector3.forward * -0.48f);
            vertices.Add(radial * 0.4f + tangent * 0.018f + Vector3.forward * -0.56f);
            vertices.Add(radial * 0.16f + tangent * 0.025f + Vector3.forward * -0.12f);
            vertices.Add(radial * 0.16f - tangent * 0.025f + Vector3.forward * -0.48f);
            vertices.Add(radial * 0.4f - tangent * 0.018f + Vector3.forward * -0.56f);
            vertices.Add(radial * 0.16f - tangent * 0.025f + Vector3.forward * -0.12f);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start + 3); triangles.Add(start + 5); triangles.Add(start + 4);
        }

        Mesh mesh = new() { name = "GF_Rocket" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
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
}
#endif
