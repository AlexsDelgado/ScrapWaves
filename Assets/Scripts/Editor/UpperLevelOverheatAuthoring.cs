#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable editor authoring; never builds gameplay visuals at runtime.</summary>
public static class UpperLevelOverheatAuthoring
{
    public const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    public const string VfxPath = "Assets/GameFeel/Prefabs/PlayerUpperLevelOverheatVfx.prefab";

    [MenuItem("ScrapWaves/Level/Author Upper Level Overheat")]
    public static void Run()
    {
        RequireCleanScenes();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
        PlayerMovement player = Components<PlayerMovement>(scene).Single(p => p.gameObject.activeInHierarchy);
        OverheatManager overheat = Components<OverheatManager>(scene).Single();
        UpperLevelVolume[] existing = Components<UpperLevelVolume>(scene);
        if (existing.Length > 1) throw new InvalidOperationException("GameplayScene must have exactly one upper volume.");
        UpperLevelVolume volume;
        if (existing.Length == 0)
        {
            GameObject root = new("UpperLevelVolume");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.layer = LayerMask.NameToLayer("Ignore Raycast");
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            // Terrain peaks at 154.54; the first upper slab is at 155.12. All upper tiers share this region.
            // The XZ margin covers slab edges; the ceiling includes the upper launch arcs and ordinary jumps.
            Vector3 min = new(-240f, 154.9f, -240f), max = new(250f, 360f, 245f);
            box.center = (min + max) * .5f;
            box.size = max - min;
            volume = root.AddComponent<UpperLevelVolume>();
            ConfigureBaseSurfaces(scene, volume, min.y);
        }
        else volume = existing[0]; // Keep designer-adjusted bounds and base references on subsequent runs.

        PlayerPlatformState state = player.GetComponent<PlayerPlatformState>();
        if (state == null) state = player.gameObject.AddComponent<PlayerPlatformState>();
        SetReference(state, "_upperLevelVolume", volume);
        PlayerUpperLevelOverheat hazard = player.GetComponent<PlayerUpperLevelOverheat>();
        bool newHazard = hazard == null;
        if (newHazard) hazard = player.gameObject.AddComponent<PlayerUpperLevelOverheat>();
        SetReference(hazard, "_platformState", state);
        SetReference(hazard, "_upperLevelVolume", volume);
        SetReference(hazard, "_overheatManager", overheat);
        SetReference(hazard, "_health", player.GetComponent<PlayerHealth>());
        Transform effects = player.transform.Find("UpperLevelOverheatVfx");
        if (effects == null)
        {
            effects = ((GameObject)PrefabUtility.InstantiatePrefab(CreateMissingVfx(), player.transform)).transform;
            effects.name = "UpperLevelOverheatVfx";
            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            effects.localPosition = capsule != null
                ? capsule.center - Vector3.up * capsule.height * .5f : Vector3.zero;
            PrefabUtility.RecordPrefabInstancePropertyModifications(effects);
        }
        var serialized = new SerializedObject(hazard);
        ParticleSystem[] particles = effects.GetComponentsInChildren<ParticleSystem>(true);
        SerializedProperty array = serialized.FindProperty("_effects");
        array.arraySize = particles.Length;
        for (int i = 0; i < particles.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = particles[i];
        if (newHazard)
        {
            serialized.FindProperty("_graceSeconds").floatValue = 2f;
            serialized.FindProperty("_tickInterval").floatValue = 2f;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(hazard);
        Physics.SyncTransforms();
        ValidateScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save GameplayScene.");
        AssetDatabase.SaveAssets();
        Debug.Log("UPPER_OVERHEAT_AUTHORED: one editable box, positive base support, player hazard and reusable fire/smoke prefab.");
    }

    private static void ConfigureBaseSurfaces(Scene scene, UpperLevelVolume volume, float upperBottom)
    {
        var roots = new HashSet<Transform>();
        Physics.SyncTransforms();
        foreach (Terrain terrain in Components<Terrain>(scene).Where(t => t.gameObject.activeInHierarchy))
        {
            roots.Add(terrain.transform);
            Transform map = terrain.transform.parent;
            if (map == null) continue;
            foreach (Collider support in map.GetComponentsInChildren<Collider>(true))
                if (support.enabled && support.gameObject.activeInHierarchy && !support.isTrigger &&
                    EntireCollisionSubtreeBelow(support.transform, upperBottom)) roots.Add(support.transform);
        }
        foreach (MapLaunchPad vent in Components<MapLaunchPad>(scene).Where(v => v.gameObject.activeInHierarchy))
            foreach (Transform child in vent.GetComponentsInChildren<Transform>(true))
                if (child.name == "WalkableMound" && EntireCollisionSubtreeBelow(child, upperBottom)) roots.Add(child);
        var serialized = new SerializedObject(volume);
        SerializedProperty array = serialized.FindProperty("_baseSurfaceRoots");
        array.arraySize = roots.Count;
        int index = 0;
        foreach (Transform root in roots.OrderBy(t => t.GetInstanceID())) array.GetArrayElementAtIndex(index++).objectReferenceValue = root;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("UPPER_BASE_SUPPORTS: " + string.Join(", ", roots.Select(t => t.name)));
    }

    private static bool EntireCollisionSubtreeBelow(Transform root, float height)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        return colliders.Length > 0 && colliders.All(c => c.enabled && c.gameObject.activeInHierarchy && c.bounds.max.y < height);
    }

    [MenuItem("ScrapWaves/Level/Validate Upper Level Overheat")]
    public static void Validate()
    {
        RequireCleanScenes();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
        Physics.SyncTransforms();
        ValidateScene(scene);
    }

    public static void ValidateScene(Scene scene)
    {
        UpperLevelVolume volume = Components<UpperLevelVolume>(scene).Single();
        BoxCollider box = volume.GetComponent<BoxCollider>();
        if (!volume.isActiveAndEnabled || !box.enabled || !box.isTrigger) throw new InvalidOperationException("Upper volume must be an enabled trigger.");
        float highestTerrain = float.NegativeInfinity;
        foreach (Terrain terrain in Components<Terrain>(scene).Where(t => t.gameObject.activeInHierarchy))
        {
            TerrainData data = terrain.terrainData;
            int n = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, n, n);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector3 surface = terrain.transform.TransformPoint(new Vector3(x * data.size.x / (n - 1), heights[z, x] * data.size.y, z * data.size.z / (n - 1)));
                    highestTerrain = Mathf.Max(highestTerrain, surface.y);
                    if (volume.Contains(surface))
                        throw new InvalidOperationException("Upper volume includes base Terrain. Adjust its bounds before saving.");
                }
            if (!volume.IsBaseSupport(terrain.GetComponent<TerrainCollider>())) throw new InvalidOperationException("Terrain is missing from explicit base supports.");
        }
        SerializedProperty baseRoots = new SerializedObject(volume).FindProperty("_baseSurfaceRoots");
        for (int i = 0; i < baseRoots.arraySize; i++)
        {
            Transform root = baseRoots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (root == null || (root.GetComponent<Terrain>() == null && !EntireCollisionSubtreeBelow(root, box.bounds.min.y)))
                throw new InvalidOperationException("Base support root is missing or includes elevated geometry: " + root);
        }
        PlayerMovement player = Components<PlayerMovement>(scene).Single(p => p.gameObject.activeInHierarchy);
        float halfHeight = player.GetComponent<Collider>().bounds.extents.y;
        foreach (MapLaunchPad vent in Components<MapLaunchPad>(scene).Where(v => v.gameObject.activeInHierarchy))
        {
            var settings = new SerializedObject(vent);
            Transform landing = (Transform)settings.FindProperty("_landing").objectReferenceValue;
            Collider support = landing != null ? landing.GetComponent<Collider>() : null;
            if (support == null || !volume.Contains(new Vector3(support.bounds.center.x, support.bounds.max.y + .05f, support.bounds.center.z)))
                throw new InvalidOperationException("Upper volume misses the landing of " + vent.name);
            if (volume.Contains(vent.transform.position))
            {
                Vector3 start = vent.transform.position + Vector3.up * halfHeight;
                Vector3 target = new(support.bounds.center.x, support.bounds.max.y + halfHeight + .05f, support.bounds.center.z);
                if (!MapLaunchPad.TryComputeLaunchVelocity(start, target, settings.FindProperty("_apexClearance").floatValue, out Vector3 velocity, out float duration))
                    throw new InvalidOperationException("Invalid upper launch arc: " + vent.name);
                for (int sample = 0; sample <= 32; sample++)
                {
                    float t = duration * sample / 32f;
                    if (!volume.Contains(start + velocity * t + .5f * Physics.gravity * t * t - Vector3.up * halfHeight))
                        throw new InvalidOperationException("Upper volume misses the launch arc of " + vent.name);
                }
            }
        }
        PlayerUpperLevelOverheat hazard = player.GetComponent<PlayerUpperLevelOverheat>();
        if (hazard == null || player.GetComponent<PlayerPlatformState>() == null) throw new InvalidOperationException("Missing player upper-level components.");
        var fields = new SerializedObject(hazard);
        foreach (string field in new[] { "_platformState", "_overheatManager", "_health", "_upperLevelVolume" })
            if (fields.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Missing hazard reference: " + field);
        SerializedProperty effects = fields.FindProperty("_effects");
        if (effects.arraySize != 2) throw new InvalidOperationException("Player hazard requires authored fire and smoke.");
        for (int i = 0; i < effects.arraySize; i++)
            if (effects.GetArrayElementAtIndex(i).objectReferenceValue is not ParticleSystem particles || particles.main.playOnAwake)
                throw new InvalidOperationException("Hazard particles must be assigned and start stopped.");
        float lowestUpperTop = Components<Terrain>(scene).Where(t => t.transform.parent != null)
            .SelectMany(t => t.transform.parent.GetComponentsInChildren<Collider>())
            .Where(c => c.enabled && !c.isTrigger && c.bounds.max.y > highestTerrain && c.bounds.size.y < 30f &&
                volume.Contains(new Vector3(c.bounds.center.x, c.bounds.max.y, c.bounds.center.z)))
            .Select(c => c.bounds.max.y).DefaultIfEmpty(float.NaN).Min();
        Debug.Log($"UPPER_OVERHEAT_VALID: box {box.bounds.min.ToString("F3")} to {box.bounds.max.ToString("F3")}; highest Terrain={highestTerrain:F5}; lowest elevated map collider top={lowestUpperTop:F5}; Terrain clearance={box.bounds.min.y - highestTerrain:F5}. All Terrain samples outside; active vent landings and upper arcs inside.");
    }

    private static GameObject CreateMissingVfx()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPath);
        if (prefab != null) return prefab;
        Material flame = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameFeel/Materials/GF_Flamethrower_Flame.mat");
        Material smoke = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameFeel/Materials/GF_Flamethrower_Smoke.mat");
        if (flame == null || smoke == null) throw new InvalidOperationException("Missing authored flame/smoke materials.");
        GameObject root = new("PlayerUpperLevelOverheatVfx");
        try
        {
            CreateParticles(root.transform, "Fire", flame, false);
            CreateParticles(root.transform, "Smoke", smoke, true);
            return PrefabUtility.SaveAsPrefabAsset(root, VfxPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateParticles(Transform parent, string name, Material material, bool smoke)
    {
        GameObject child = new(name, typeof(ParticleSystem));
        child.transform.SetParent(parent, false);
        child.transform.localRotation = Quaternion.LookRotation(Vector3.up);
        ParticleSystem particles = child.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true; main.playOnAwake = false; main.useUnscaledTime = false;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        // These existing materials use the mesh VFX shader; spherical particles avoid opaque billboard squares.
        renderer.sharedMaterial = material; renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        ApplyCompactTuning(particles, smoke);
    }

    [MenuItem("ScrapWaves/Level/Retune Upper Overheat VFX Compact")]
    public static void RetuneCompactVfx()
    {
        RequireCleanScenes();
        GameObject root = PrefabUtility.LoadPrefabContents(VfxPath);
        try
        {
            foreach (string name in new[] { "Fire", "Smoke" })
            {
                ParticleSystem particles = root.transform.Find(name)?.GetComponent<ParticleSystem>();
                if (particles == null) throw new InvalidOperationException("Missing authored hazard effect: " + name);
                ApplyCompactTuning(particles, name == "Smoke");
            }
            if (PrefabUtility.SaveAsPrefabAsset(root, VfxPath) == null) throw new InvalidOperationException("Could not save compact hazard VFX.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("UPPER_OVERHEAT_VFX_RETUNED: compact ankle flames and lower-leg smoke; prefab only.");
    }

    public static void RetuneAndCaptureVfx()
    {
        RetuneCompactVfx();
        CaptureVfx();
    }

    private static void ApplyCompactTuning(ParticleSystem particles, bool smoke)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.transform.localPosition = Vector3.up * (smoke ? .35f : .25f);
        particles.useAutoRandomSeed = false; particles.randomSeed = smoke ? 1982u : 1981u;
        var main = particles.main;
        main.maxParticles = smoke ? 12 : 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smoke ? .8f : .5f, smoke ? 1.2f : .8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(smoke ? .4f : .45f, smoke ? .7f : .9f);
        main.startSize3D = true;
        main.startSizeX = main.startSizeY = new ParticleSystem.MinMaxCurve(smoke ? .2f : .12f, smoke ? .3f : .2f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(smoke ? .2f : .3f, smoke ? .3f : .5f);
        var emission = particles.emission; emission.rateOverTime = smoke ? 8f : 24f;
        var shape = particles.shape; shape.radius = .3f; shape.angle = 8f;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(smoke ? .65f : .7f, .15f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, smoke ? .65f : 1f, 1, smoke ? 1.2f : .25f));
    }

    /// <summary>Renders the saved player's actual authored effects without changing or saving the scene.</summary>
    public static void CaptureVfx()
    {
        RequireCleanScenes();
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("VFX capture requires Unity graphics; omit -nographics.");
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_UPPER_VFX_CAPTURE");
        if (string.IsNullOrWhiteSpace(output)) output = Path.Combine("Temp", "UpperLevelOverheat", "upper-overheat-vfx.png");
        Scene previous = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !source.IsValid() || !source.isLoaded;
        if (opened) source = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D pixels = null;
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            PlayerMovement player = Components<PlayerMovement>(source).Single(p => p.gameObject.activeInHierarchy);
            GameObject actor = Object.Instantiate(player.gameObject);
            SceneManager.MoveGameObjectToScene(actor, preview);
            actor.name = "Authored player VFX capture";
            foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Canvas canvas in actor.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            if (actor.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Animator animator = actor.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                AnimationClip idle = animator.runtimeAnimatorController != null
                    ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0) : null;
                animator.enabled = false;
                if (idle != null) idle.SampleAnimation(animator.gameObject, 0f);
            }
            Transform effect = actor.transform.Find("UpperLevelOverheatVfx");
            if (effect == null) throw new InvalidOperationException("Saved scene player has no authored overheat effect.");
            foreach (ParticleSystem particles in actor.GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true)) particles.Simulate(2f, false, true, false);
            GameObject lamp = new("Capture key light", typeof(Light));
            SceneManager.MoveGameObjectToScene(lamp, preview);
            lamp.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Light light = lamp.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            GameObject cameraObject = new("Capture camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .055f, .07f);
            camera.fieldOfView = 40f; camera.nearClipPlane = .05f; camera.farClipPlane = 40f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            Physics.SyncTransforms();
            Bounds bounds = actor.GetComponent<Collider>().bounds;
            Vector3 focus = bounds.center + Vector3.up * .1f;
            camera.transform.position = focus + new Vector3(.45f, .22f, 1f).normalized * Mathf.Max(4.5f, bounds.size.y * 2.5f);
            camera.transform.LookAt(focus);
            target = new RenderTexture(960, 640, 24, RenderTextureFormat.ARGB32); target.Create();
            camera.targetTexture = target; camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllBytes(output, pixels.EncodeToPNG());
            Debug.Log("UPPER_OVERHEAT_VFX_CAPTURE: " + output);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
            if (opened) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
    private static void RequireCleanScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author outside Play Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene edits detected; no scene or asset was changed.");
    }
}
#endif
