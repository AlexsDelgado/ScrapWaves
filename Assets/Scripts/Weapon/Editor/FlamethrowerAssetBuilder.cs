#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FlamethrowerAssetBuilder
{
    private const string GameFeelRoot = "Assets/GameFeel";
    private const string MaterialRoot = GameFeelRoot + "/Materials";
    private const string PrefabRoot = GameFeelRoot + "/Prefabs/Weapons/Flamethrower";
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset";
    private const string QualityPath = GameFeelRoot + "/Profiles/GameFeelQuality_PC.asset";

    private sealed class BuildAssets
    {
        public Mesh Sphere;
        public Mesh Ring;
        public Mesh Cone;
        public Mesh Shard;
        public Material Flame;
        public Material FlameCore;
        public Material Smoke;
        public Material Fuel;
        public Material FuelCore;
        public Material Frost;
        public Material FrostCore;
        public GameFeelQualitySettings Quality;
    }

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Flamethrower Production Assets")]
    public static void BuildFromMenu()
    {
        BuildBatch();
        Debug.Log("Flamethrower production presentation rebuilt.");
    }

    public static void BuildBatch()
    {
        EnsureFolders();
        AssetDatabase.Refresh();
        BuildAssets assets = BuildSharedAssets();
        GameObject stream = BuildStreamPrefab(assets);
        GameObject puddle = BuildPuddlePrefab(assets);
        DeleteDeferredPresentationPrefabs();
        Dictionary<FlamethrowerCueStyle, GameObject> cues = BuildCuePrefabs(assets);
        WeaponPresentationProfile profile = BuildProfile(assets, stream, puddle, cues);
        AssignProfile(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "GameFeel");
        EnsureFolder(GameFeelRoot, "Materials");
        EnsureFolder(GameFeelRoot, "Prefabs");
        EnsureFolder(GameFeelRoot + "/Prefabs", "Weapons");
        EnsureFolder(GameFeelRoot + "/Prefabs/Weapons", "Flamethrower");
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
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        if (shader == null)
            throw new InvalidOperationException("ScrapWaves/GameFeel/Scrap VFX shader is required.");

        return new BuildAssets
        {
            Sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
            Ring = AssetDatabase.LoadAssetAtPath<Mesh>(GameFeelRoot + "/Meshes/GF_PressureRing.asset"),
            Cone = AssetDatabase.LoadAssetAtPath<Mesh>(GameFeelRoot + "/Meshes/GF_CannonCone.asset"),
            Shard = AssetDatabase.LoadAssetAtPath<Mesh>(GameFeelRoot + "/Meshes/GF_ScrapShard.asset"),
            Flame = CreateMaterial(MaterialRoot + "/GF_Flamethrower_Flame.mat", shader, new Color(1f, 0.11f, 0.01f, 0.74f), new Color(1f, 0.2f, 0.015f), 2.7f, 7f, 4f),
            FlameCore = CreateMaterial(MaterialRoot + "/GF_Flamethrower_Core.mat", shader, new Color(1f, 0.78f, 0.2f, 0.95f), new Color(1f, 0.92f, 0.5f), 4.2f, 5f, 3f),
            Smoke = CreateMaterial(MaterialRoot + "/GF_Flamethrower_Smoke.mat", shader, new Color(0.16f, 0.13f, 0.11f, 0.36f), new Color(0.2f, 0.13f, 0.08f), 0.4f, 3f, 0.8f),
            Fuel = CreateMaterial(MaterialRoot + "/GF_Flamethrower_Fuel.mat", shader, new Color(0.012f, 0.18f, 0.038f, 0.88f), new Color(0.04f, 0.3f, 0.055f), 1.8f, 8f, 1.5f),
            FuelCore = CreateMaterial(MaterialRoot + "/GF_Flamethrower_FuelCore.mat", shader, new Color(0.56f, 0.78f, 0.08f, 0.9f), new Color(0.72f, 0.94f, 0.12f), 3f, 6f, 2f),
            Frost = CreateMaterial(MaterialRoot + "/GF_Flamethrower_Frost.mat", shader, new Color(0.34f, 0.57f, 0.72f, 0.72f), new Color(0.48f, 0.72f, 0.9f), 2.2f, 9f, 1.1f),
            FrostCore = CreateMaterial(MaterialRoot + "/GF_Flamethrower_FrostCore.mat", shader, new Color(0.92f, 0.98f, 1f, 0.92f), Color.white, 3.8f, 12f, 1.7f),
            Quality = AssetDatabase.LoadAssetAtPath<GameFeelQualitySettings>(QualityPath)
        };
    }

    private static GameObject BuildStreamPrefab(BuildAssets assets)
    {
        GameObject root = new("GF_Flamethrower_Stream");
        FlamethrowerStreamVfx vfx = root.AddComponent<FlamethrowerStreamVfx>();
        GameObject body = CreateEmptyMeshLayer("Procedural Flame Body", root.transform, assets.Flame);
        GameObject core = CreateEmptyMeshLayer("Procedural Bright Core", root.transform, assets.FlameCore);
        GameObject nozzle = CreateMeshLayer("Nozzle Glow", root.transform, assets.Sphere, assets.FlameCore);
        nozzle.transform.localScale = new Vector3(0.12f, 0.12f, 0.2f);
        ParticleSystem embers = CreateParticles("Fast Flame Licks", root.transform, assets.Shard, assets.FlameCore, 38, true, ParticleSystemShapeType.Cone, 0.1f, 8.5f, 0.62f, 15f, new Color(1f, 0.72f, 0.18f), new Color(1f, 0.08f, 0.01f));
        ParticleSystem smoke = CreateParticles("Trailing Heat Smoke", root.transform, assets.Sphere, assets.Smoke, 20, true, ParticleSystemShapeType.Cone, 0.13f, 3.8f, 1.05f, 19f, new Color(0.4f, 0.26f, 0.16f), new Color(0.08f, 0.07f, 0.065f));
        Light light = root.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.34f, 0.04f);
        light.range = 1.8f;
        light.intensity = 0.55f;
        light.shadows = LightShadows.None;

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_bodyFilter").objectReferenceValue = body.GetComponent<MeshFilter>();
        serialized.FindProperty("_bodyRenderer").objectReferenceValue = body.GetComponent<MeshRenderer>();
        serialized.FindProperty("_coreFilter").objectReferenceValue = core.GetComponent<MeshFilter>();
        serialized.FindProperty("_coreRenderer").objectReferenceValue = core.GetComponent<MeshRenderer>();
        serialized.FindProperty("_embers").objectReferenceValue = embers;
        serialized.FindProperty("_smoke").objectReferenceValue = smoke;
        serialized.FindProperty("_nozzleGlow").objectReferenceValue = nozzle.GetComponent<MeshRenderer>();
        serialized.FindProperty("_nozzleLight").objectReferenceValue = light;
        serialized.FindProperty("_maximumSegments").intValue = 48;
        serialized.FindProperty("_automaticWidthMultiplier").floatValue = 0.52f;
        serialized.FindProperty("_automaticHeightMultiplier").floatValue = 0.22f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(root, "GF_Flamethrower_Stream");
    }

    private static GameObject BuildPuddlePrefab(BuildAssets assets)
    {
        GameObject root = new("GF_JellifiedFuel_Puddle");
        FlamethrowerFuelPuddle puddle = root.AddComponent<FlamethrowerFuelPuddle>();
        GameObject fill = CreateEmptyMeshLayer("Viscous Fuel Fill", root.transform, assets.Fuel);
        GameObject edge = CreateEmptyMeshLayer("Irregular Fuel Edge", root.transform, assets.FuelCore);
        ParticleSystem bubbles = CreateParticles("Fuel Bubbles", root.transform, assets.Sphere, assets.FuelCore, 16, true, ParticleSystemShapeType.Circle, 0.8f, 0.22f, 0.7f, 0f, new Color(0.65f, 0.85f, 0.1f), new Color(0.08f, 0.28f, 0.04f));
        ParticleSystem smoke = CreateParticles("Dark Fuel Smoke", root.transform, assets.Sphere, assets.Smoke, 22, true, ParticleSystemShapeType.Circle, 0.72f, 0.6f, 1.4f, 0f, new Color(0.19f, 0.22f, 0.08f), new Color(0.04f, 0.05f, 0.035f));
        ConfigureGroundCircle(bubbles);
        ConfigureGroundCircle(smoke);
        ConfigurePuddleParticleFade(bubbles);
        ConfigurePuddleParticleFade(smoke);
        SerializedObject serialized = new(puddle);
        serialized.FindProperty("_fillFilter").objectReferenceValue = fill.GetComponent<MeshFilter>();
        serialized.FindProperty("_fillRenderer").objectReferenceValue = fill.GetComponent<MeshRenderer>();
        serialized.FindProperty("_edgeFilter").objectReferenceValue = edge.GetComponent<MeshFilter>();
        serialized.FindProperty("_edgeRenderer").objectReferenceValue = edge.GetComponent<MeshRenderer>();
        serialized.FindProperty("_bubbles").objectReferenceValue = bubbles;
        serialized.FindProperty("_darkSmoke").objectReferenceValue = smoke;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, "GF_JellifiedFuel_Puddle");
    }

    private static void ConfigureGroundCircle(ParticleSystem particles)
    {
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.rotation = new Vector3(-90f, 0f, 0f);
    }

    private static void ConfigurePuddleParticleFade(ParticleSystem particles)
    {
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.25f),
                new Keyframe(0.16f, 1f),
                new Keyframe(0.68f, 0.82f),
                new Keyframe(1f, 0.03f)));
    }

    private static Dictionary<FlamethrowerCueStyle, GameObject> BuildCuePrefabs(BuildAssets assets)
    {
        Dictionary<FlamethrowerCueStyle, GameObject> result = new();
        FlamethrowerCueStyle[] presentationStyles =
        {
            FlamethrowerCueStyle.FlameActiveBurst,
            FlamethrowerCueStyle.JellifiedActiveBurst,
            FlamethrowerCueStyle.NitrogenActiveBurst,
            FlamethrowerCueStyle.BurnCoating,
            FlamethrowerCueStyle.JellifiedCoating,
            FlamethrowerCueStyle.NitrogenSlow,
            FlamethrowerCueStyle.NitrogenFreeze
        };
        for (int i = 0; i < presentationStyles.Length; i++)
        {
            FlamethrowerCueStyle style = presentationStyles[i];
            result[style] = BuildCuePrefab(assets, style);
        }
        return result;
    }

    private static void DeleteDeferredPresentationPrefabs()
    {
        string[] names =
        {
            "GF_Flamethrower_FlameNozzleLoop.prefab",
            "GF_Flamethrower_JellifiedNozzleLoop.prefab",
            "GF_Flamethrower_NitrogenNozzleLoop.prefab",
            "GF_Flamethrower_SustainedStop.prefab"
        };
        for (int i = 0; i < names.Length; i++)
            AssetDatabase.DeleteAsset(PrefabRoot + "/" + names[i]);
    }

    private static GameObject BuildCuePrefab(BuildAssets assets, FlamethrowerCueStyle style)
    {
        bool nitrogen = style == FlamethrowerCueStyle.NitrogenNozzleLoop ||
                        style == FlamethrowerCueStyle.NitrogenActiveBurst ||
                        style == FlamethrowerCueStyle.NitrogenSlow ||
                        style == FlamethrowerCueStyle.NitrogenFreeze;
        bool fuel = style == FlamethrowerCueStyle.JellifiedNozzleLoop ||
                    style == FlamethrowerCueStyle.JellifiedActiveBurst ||
                    style == FlamethrowerCueStyle.JellifiedCoating;
        bool active = style == FlamethrowerCueStyle.FlameActiveBurst ||
                      style == FlamethrowerCueStyle.JellifiedActiveBurst ||
                      style == FlamethrowerCueStyle.NitrogenActiveBurst;
        Material primary = nitrogen ? assets.Frost : fuel ? assets.Fuel : assets.Flame;
        Material core = nitrogen ? assets.FrostCore : fuel ? assets.FuelCore : assets.FlameCore;
        Color primaryColor = nitrogen ? new Color(0.35f, 0.58f, 0.73f, 0.82f) : fuel ? new Color(0.03f, 0.24f, 0.045f, 0.86f) : new Color(1f, 0.15f, 0.01f, 0.86f);
        Color coreColor = nitrogen ? new Color(0.95f, 1f, 1f, 0.96f) : fuel ? new Color(0.67f, 0.9f, 0.1f, 0.95f) : new Color(1f, 0.88f, 0.34f, 1f);

        GameObject root = new("GF_Flamethrower_" + style);
        root.AddComponent<PooledWeaponVfx>();
        FlamethrowerCueVfx vfx = root.AddComponent<FlamethrowerCueVfx>();
        GameObject visual = new("Animated Visual");
        visual.transform.SetParent(root.transform, false);
        List<Renderer> renderers = new();
        List<ParticleSystem> particles = new();

        if (active)
        {
            GameObject radius = CreateMeshLayer("Damage Radius", visual.transform, assets.Ring, primary);
            radius.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            radius.transform.localScale = Vector3.one * 2f;
            GameObject pressure = CreateMeshLayer("Pressure Front", visual.transform, assets.Ring, core);
            pressure.transform.localScale = Vector3.one * 1.72f;
            GameObject center = CreateMeshLayer("Burst Core", visual.transform, assets.Sphere, core);
            center.transform.localScale = Vector3.one * 0.24f;
            renderers.Add(radius.GetComponent<MeshRenderer>());
            renderers.Add(pressure.GetComponent<MeshRenderer>());
            renderers.Add(center.GetComponent<MeshRenderer>());
            particles.Add(CreateParticles(nitrogen ? "Cold Vapor" : fuel ? "Outward Fuel Throw" : "Flame Licks", visual.transform, assets.Shard, primary, 44, false, ParticleSystemShapeType.Sphere, 0.18f, 5f, 0.7f, 0f, primaryColor, coreColor));
            particles.Add(CreateParticles(nitrogen ? "Ice Shards" : "Embers and Smoke", visual.transform, nitrogen ? assets.Shard : assets.Sphere, nitrogen ? core : assets.Smoke, 28, false, ParticleSystemShapeType.Sphere, 0.25f, 2.8f, 0.9f, 0f, coreColor, primaryColor));
        }
        else if (style == FlamethrowerCueStyle.NitrogenFreeze)
        {
            GameObject shell = CreateMeshLayer("Frost Overlay", visual.transform, assets.Sphere, primary);
            shell.transform.localScale = new Vector3(0.72f, 1.15f, 0.72f);
            GameObject crack = CreateMeshLayer("Freeze Crack Pulse", visual.transform, assets.Ring, core);
            crack.transform.localPosition = Vector3.up * 0.2f;
            crack.transform.localScale = Vector3.one * 1.5f;
            renderers.Add(shell.GetComponent<MeshRenderer>());
            renderers.Add(crack.GetComponent<MeshRenderer>());
            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * Mathf.PI * 2f;
                GameObject shard = CreateMeshLayer("Ice Shard " + (i + 1), visual.transform, assets.Shard, core);
                shard.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.52f, 0.35f + (i % 2) * 0.42f, Mathf.Sin(angle) * 0.52f);
                shard.transform.localRotation = Quaternion.Euler(i * 19f, -angle * Mathf.Rad2Deg, 18f);
                shard.transform.localScale = new Vector3(0.24f, 0.5f, 0.24f);
                renderers.Add(shard.GetComponent<MeshRenderer>());
            }
            particles.Add(CreateParticles("Dense Cold Vapor", visual.transform, assets.Sphere, assets.Frost, 36, false, ParticleSystemShapeType.Sphere, 0.55f, 0.5f, 1.2f, 0f, coreColor, primaryColor));
            particles.Add(CreateParticles("Ice Crack Shards", visual.transform, assets.Shard, assets.FrostCore, 20, false, ParticleSystemShapeType.Sphere, 0.42f, 2.1f, 0.75f, 0f, coreColor, primaryColor));
        }
        else
        {
            GameObject shell = CreateMeshLayer(
                style == FlamethrowerCueStyle.SustainedStop ? "Nozzle Afterglow" : nitrogen ? "Frost Overlay" : fuel ? "Viscous Burn Coating" : "Burn Coating",
                visual.transform,
                style == FlamethrowerCueStyle.FlameNozzleLoop || style == FlamethrowerCueStyle.JellifiedNozzleLoop || style == FlamethrowerCueStyle.NitrogenNozzleLoop ? assets.Cone : assets.Sphere,
                primary);
            shell.transform.localScale = style == FlamethrowerCueStyle.FlameNozzleLoop || style == FlamethrowerCueStyle.JellifiedNozzleLoop || style == FlamethrowerCueStyle.NitrogenNozzleLoop
                ? new Vector3(0.32f, 0.32f, 0.52f)
                : new Vector3(0.66f, 1.04f, 0.66f);
            renderers.Add(shell.GetComponent<MeshRenderer>());
            GameObject accent = CreateMeshLayer(nitrogen ? "Pressure Crack" : fuel ? "Fuel Bubbles" : "Heat Pulse", visual.transform, assets.Ring, core);
            accent.transform.localScale = Vector3.one * 1.15f;
            renderers.Add(accent.GetComponent<MeshRenderer>());
            particles.Add(CreateParticles(nitrogen ? "Cold Vapor" : fuel ? "Sticky Sparks" : "Embers", visual.transform, nitrogen ? assets.Sphere : assets.Shard, primary, 20, true, ParticleSystemShapeType.Sphere, 0.3f, 0.7f, 0.8f, 0f, coreColor, primaryColor));
        }

        Light light = null;
        if (active || style == FlamethrowerCueStyle.FlameNozzleLoop || style == FlamethrowerCueStyle.JellifiedNozzleLoop || style == FlamethrowerCueStyle.NitrogenNozzleLoop)
        {
            light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = active ? 5f : 2.2f;
            light.intensity = active ? 1.8f : 0.8f;
            light.color = coreColor;
            light.shadows = LightShadows.None;
        }

        SerializedObject serialized = new(vfx);
        serialized.FindProperty("_style").enumValueIndex = (int)style;
        serialized.FindProperty("_primaryColor").colorValue = primaryColor;
        serialized.FindProperty("_coreColor").colorValue = coreColor;
        SetObjectArray(serialized.FindProperty("_meshLayers"), renderers.ToArray());
        SetObjectArray(serialized.FindProperty("_particleLayers"), particles.ToArray());
        SetObjectArray(serialized.FindProperty("_animatedRoots"), new UnityEngine.Object[] { visual.transform });
        serialized.FindProperty("_lightPulse").objectReferenceValue = light;
        serialized.FindProperty("_lifetime").floatValue = active ? 0.72f : style == FlamethrowerCueStyle.NitrogenFreeze ? 1.15f : 0.7f;
        serialized.FindProperty("_size").floatValue = 1f;
        serialized.FindProperty("_baseEmission").floatValue = nitrogen ? 2.4f : 3f;
        serialized.FindProperty("_scaleFromExplosionRadius").boolValue = active;
        serialized.FindProperty("_explosionRadiusMultiplier").floatValue = active ? 1f : 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, "GF_Flamethrower_" + style);
    }

    private static WeaponPresentationProfile BuildProfile(
        BuildAssets assets,
        GameObject stream,
        GameObject puddle,
        Dictionary<FlamethrowerCueStyle, GameObject> prefabs)
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        AudioClip flameAudio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/010.wav");
        AudioClip pressureAudio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/shoot.wav");
        AudioClip crackAudio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/click.wav");
        List<WeaponPresentationCueData> cues = new()
        {
            Cue(WeaponPresentationCue.FlamethrowerAutomaticLoop, null, flameAudio, 0.28f, 0.72f, 0.82f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerManualLoop, null, flameAudio, 0.34f, 0.82f, 0.94f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop, null, flameAudio, 0.28f, 0.58f, 0.68f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerJellifiedManualLoop, null, flameAudio, 0.34f, 0.66f, 0.76f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop, null, flameAudio, 0.3f, 1.05f, 1.15f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerNitrogenManualLoop, null, flameAudio, 0.35f, 1.12f, 1.24f, 0.7f, true, 1),
            Cue(WeaponPresentationCue.FlamethrowerActiveBurst, prefabs[FlamethrowerCueStyle.FlameActiveBurst], pressureAudio, 0.84f, 0.78f, 0.9f, 0.72f, false, 5, active: true),
            Cue(WeaponPresentationCue.FlamethrowerJellifiedActive, prefabs[FlamethrowerCueStyle.JellifiedActiveBurst], pressureAudio, 0.82f, 0.62f, 0.74f, 0.72f, false, 5, active: true),
            Cue(WeaponPresentationCue.FlamethrowerNitrogenActive, prefabs[FlamethrowerCueStyle.NitrogenActiveBurst], pressureAudio, 0.86f, 1.05f, 1.18f, 0.72f, false, 5, active: true),
            Cue(WeaponPresentationCue.FlamethrowerBurnStatus, prefabs[FlamethrowerCueStyle.BurnCoating], null, 0f, 1f, 1f, 0.75f, false, 18, secondary: true),
            Cue(WeaponPresentationCue.FlamethrowerJellifiedStatus, prefabs[FlamethrowerCueStyle.JellifiedCoating], null, 0f, 1f, 1f, 0.85f, false, 18, secondary: true),
            Cue(WeaponPresentationCue.FlamethrowerNitrogenSlow, prefabs[FlamethrowerCueStyle.NitrogenSlow], null, 0f, 1f, 1f, 0.75f, false, 18, secondary: true),
            Cue(WeaponPresentationCue.FlamethrowerNitrogenFreeze, prefabs[FlamethrowerCueStyle.NitrogenFreeze], crackAudio, 0.68f, 0.78f, 0.9f, 1.15f, false, 18)
        };

        List<WeaponFeedbackBinding> bindings = new()
        {
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerAutomaticLoop, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerManualLoop, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerJellifiedManualLoop, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop, WeaponFeedbackModeFilter.Automatic, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.SustainedFireStarted, WeaponPresentationCue.FlamethrowerNitrogenManualLoop, WeaponFeedbackModeFilter.Manual, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.FlamethrowerJellifiedActive, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.FlamethrowerNitrogenActive, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.ShotFired, WeaponPresentationCue.FlamethrowerActiveBurst, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.Base),
            Binding(WeaponFeedbackEvent.StatusApplied, WeaponPresentationCue.FlamethrowerNitrogenFreeze, WeaponFeedbackModeFilter.Active, WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.StatusApplied, WeaponPresentationCue.FlamethrowerNitrogenSlow, path: WeaponUpgradePathFilter.PathB),
            Binding(WeaponFeedbackEvent.StatusApplied, WeaponPresentationCue.FlamethrowerJellifiedStatus, path: WeaponUpgradePathFilter.PathA),
            Binding(WeaponFeedbackEvent.StatusApplied, WeaponPresentationCue.FlamethrowerBurnStatus, path: WeaponUpgradePathFilter.Base)
        };

        FlamethrowerPresentationSettings settings = new()
        {
            StreamPrefab = stream,
            FuelPuddlePrefab = puddle,
            MaximumStreamSegments = 48,
            FuelPuddlePrewarmCount = 8,
            FuelPuddlePoolCapacity = 32
        };
        SetPrivate(profile, "_weaponType", WeaponType.Flamethrower);
        SetPrivate(profile, "_defaultQuality", GameFeelQualityLevel.High);
        SetPrivate(profile, "_cues", cues);
        SetPrivate(profile, "_feedbackBindings", bindings);
        SetPrivate(profile, "_projectileArchetypes", new List<ProjectileArchetypePresentation>());
        SetPrivate(profile, "_flamethrower", settings);
        SetPrivate(profile, "_qualitySettings", assets.Quality);
        profile.RebuildCache();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static WeaponPresentationCueData Cue(
        WeaponPresentationCue cue,
        GameObject prefab,
        AudioClip clip,
        float volume,
        float pitchMin,
        float pitchMax,
        float duration,
        bool loop,
        int maximum,
        bool active = false,
        bool secondary = false)
    {
        WeaponPresentationCueData data = new()
        {
            Cue = cue,
            VfxPrefab = prefab,
            Volume = volume,
            PitchMin = pitchMin,
            PitchMax = pitchMax,
            Duration = duration,
            MinReplayInterval = loop ? 0.05f : 0f,
            PrewarmCount = loop ? 1 : Mathf.Min(6, maximum),
            MaxSimultaneous = maximum,
            Loop = loop,
            CameraPositionImpulse = active ? new Vector3(0f, 0f, -0.12f) : Vector3.zero,
            CameraRotationImpulse = active ? new Vector3(-0.35f, 0f, 0f) : Vector3.zero,
            CameraFovKick = active ? 0.85f : 0f,
            CameraMinReplayInterval = 0.05f,
            EssentialGameplayCue = true,
            SecondaryEffect = secondary,
            MinimumQuality = GameFeelQualityLevel.Low,
            SpatialBlend = 0.9f,
            MinimumDistance = 1f,
            MaximumDistance = 48f,
            AudioPriority = active ? 60 : 110,
            ApplyHeatStrainToMechanicalLayer = loop,
            ApplyEventIntensityToPitch = loop
        };
        if (clip != null)
            data.AudioClips.Add(clip);
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
            "Assets/ScriptableObjects/WeaponSO/Flamethrower.asset",
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset"
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

    private static GameObject CreateEmptyMeshLayer(string name, Transform parent, Material material)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        layer.AddComponent<MeshFilter>();
        MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
        ConfigureRenderer(renderer, material);
        return layer;
    }

    private static GameObject CreateMeshLayer(string name, Transform parent, Mesh mesh, Material material)
    {
        GameObject layer = CreateEmptyMeshLayer(name, parent, material);
        layer.GetComponent<MeshFilter>().sharedMesh = mesh;
        return layer;
    }

    private static void ConfigureRenderer(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
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
        float coneAngle,
        Color startColor,
        Color endColor)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        ParticleSystem particles = layer.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = loop;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.1f, lifetime);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.55f, lifetime * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.simulationSpace = loop ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.stopAction = ParticleSystemStopAction.None;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = loop ? Mathf.Max(1f, count * 1.35f) : 0f;
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
            new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        ParticleSystemRenderer renderer = layer.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static Material CreateMaterial(string path, Shader shader, Color baseColor, Color emissionColor, float emission, float noiseScale, float noiseSpeed)
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
