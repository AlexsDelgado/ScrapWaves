#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Disposable, staged inspection of production rigs/controllers; never saves scene or prefab changes.</summary>
public static class LarguiruchoPreviewCapture
{
    public const string Output = ".utmp/larguirucho-preview";
    private const int FrameRate = 24;
    private const int FrameCount = FrameRate * 15;
    private const int PreviewLayer = 30;
    private static readonly string[] Prefabs =
    {
        "Assets/Prefabs/Chaser.prefab",
        "Assets/Prefabs/Chaser (variant).prefab",
        "Assets/Prefabs/Chaser_Elite.prefab"
    };
    private static readonly int WalkSpeed = Animator.StringToHash("WalkSpeed");
    private static readonly int ChargePhase = Animator.StringToHash("ChargePhase");
    private static readonly int DashPhase = Animator.StringToHash("DashPhase");
    private static readonly int RecoverPhase = Animator.StringToHash("RecoverPhase");
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void Pilot() => Capture(true);
    public static void Render() => Capture(false);
    public static void RenderAndProfile()
    {
        Capture(false);
        Profile();
    }

    private sealed class Actor : IDisposable
    {
        public GameObject Root;
        public Animator Animator;
        public SkinnedMeshRenderer[] Skins;
        public Mesh[] Baked;
        public string State = "Idle";
        public float Phase;
        public int Variant;

        public void Show(string state, float phase, float speed, float delta)
        {
            Phase = Mathf.Clamp01(phase);
            if (state == "Charge") Animator.SetFloat(ChargePhase, Phase);
            else if (state == "Dash") Animator.SetFloat(DashPhase, Phase);
            else if (state == "Recover") Animator.SetFloat(RecoverPhase, Phase);
            Animator.SetFloat(WalkSpeed, speed);
            if (state != State)
            {
                Animator.CrossFadeInFixedTime(Animator.StringToHash(state), 0.06f, 0, 0f);
                State = state;
            }
            Animator.Update(delta);
            if (Baked == null) return;
            for (int i = 0; i < Skins.Length; i++)
            {
                // These baked meshes remain under the source renderer's transform.
                Skins[i].BakeMesh(Baked[i], true);
                Baked[i].RecalculateBounds();
            }
        }

        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            if (Baked != null)
                foreach (Mesh mesh in Baked)
                    if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    private static Actor CreateActor(Scene scene, int variant, bool bake)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[variant]);
        if (prefab == null) throw new InvalidOperationException("Missing " + Prefabs[variant]);
        var actor = new Actor { Root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene), Variant = variant };
        try
        {
            // No gameplay callbacks are manually invoked: this is intentionally a presentation-only stage.
            foreach (MonoBehaviour script in actor.Root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (Collider collider in actor.Root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in actor.Root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (Transform child in actor.Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            Transform emergence = actor.Root.transform.Find("EmergenceVisual");
            if (emergence == null) throw new InvalidOperationException("Missing emergence wrapper in " + Prefabs[variant]);
            emergence.localPosition = Vector3.zero;
            actor.Animator = emergence.GetComponentInChildren<Animator>(true);
            if (actor.Animator == null || actor.Animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Missing production Animator in " + Prefabs[variant]);
            actor.Animator.enabled = true;
            actor.Animator.applyRootMotion = false;
            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            actor.Animator.Rebind();
            foreach (string state in new[] { "Idle", "Walk", "Charge", "Dash", "Recover" })
                if (!actor.Animator.HasState(0, Animator.StringToHash(state))
                    && !actor.Animator.HasState(0, Animator.StringToHash(actor.Animator.GetLayerName(0) + "." + state)))
                    throw new InvalidOperationException("Run Larguirucho authoring first; missing state " + state);
            actor.Animator.Play(Animator.StringToHash("Idle"), 0, 0f);
            actor.Animator.Update(0f);
            actor.Skins = emergence.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => !s.name.StartsWith("[", StringComparison.Ordinal)).ToArray();
            if (actor.Skins.Length < 2) throw new InvalidOperationException("Expected body and tread skins in " + Prefabs[variant]);
            if (bake)
            {
                actor.Baked = new Mesh[actor.Skins.Length];
                for (int i = 0; i < actor.Skins.Length; i++)
                {
                    SkinnedMeshRenderer skin = actor.Skins[i];
                    actor.Baked[i] = new Mesh { name = "Disposable Larguirucho preview mesh" };
                    var display = new GameObject("Disposable baked " + skin.name) { layer = PreviewLayer };
                    SceneManager.MoveGameObjectToScene(display, scene);
                    display.transform.SetParent(skin.transform, false);
                    display.AddComponent<MeshFilter>().sharedMesh = actor.Baked[i];
                    var renderer = display.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = skin.sharedMaterials;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    skin.enabled = false;
                    skin.BakeMesh(actor.Baked[i], true);
                }
            }
            return actor;
        }
        catch { actor.Dispose(); throw; }
    }

    private static void Stage(Actor actor, float time, float delta)
    {
        if (time < 2f) { actor.Show("Walk", 0f, 1f, delta); return; }
        if (time < 6f) { AttackCycle(actor, time - 2f, false, delta); return; }
        if (time < 11f) { AttackCycle(actor, time - 6f, true, delta); return; }
        if (time < 13.5f) { actor.Show("Walk", 0f, 1f / (2f * 2.125f), delta); return; }
        actor.Show("Idle", 0f, 0f, delta);
    }

    private static void AttackCycle(Actor actor, float elapsed, bool earlyContact, float delta)
    {
        float charge = actor.Variant == 0 ? 2f : 1.5f;
        float fullDash = actor.Variant == 0 ? 12f / 30f : 14f / 38f;
        float dash = earlyContact ? 0.1f : fullDash;
        float recovery = actor.Variant == 0 ? 0.6f : 1.5f;
        if (elapsed < charge) actor.Show("Charge", elapsed / (1f / 3f), 0f, delta);
        else if (elapsed < charge + dash) actor.Show("Dash", (elapsed - charge) / fullDash, 0f, delta);
        else if (elapsed < charge + dash + recovery) actor.Show("Recover", (elapsed - charge - dash) / recovery, 0f, delta);
        else actor.Show("Walk", 0f, 1f, delta);
    }

    private static void Capture(bool pilot)
    {
        RequireEditMode();
        Directory.CreateDirectory(Output);
        if (!pilot) Directory.CreateDirectory(Output + "/frames");
        // Preview scenes work even when the editor's open scene is untitled and unsaved.
        // Never switch the active scene or change its RenderSettings.
        Scene stage = EditorSceneManager.NewPreviewScene();
        var actors = new List<Actor>();
        var materials = new List<Material>();
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            Light key = AddLight(stage, "Preview warm key", new Vector3(35f, -28f, 0f), new Color(1f, 0.91f, 0.78f), 2.1f);
            key.shadows = LightShadows.Soft;
            AddLight(stage, "Preview cool rim", new Vector3(25f, 160f, 0f), new Color(0.50f, 0.70f, 1f), 1.5f);
            AddLight(stage, "Preview fill", new Vector3(70f, 30f, 0f), new Color(0.73f, 0.84f, 1f), 0.7f);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            Material floor = new(lit) { name = "Disposable stage floor", color = new Color(0.13f, 0.16f, 0.19f) };
            floor.SetFloat("_Smoothness", 0.25f);
            materials.Add(floor);
            CreateBox(stage, "Preview floor", new Vector3(0, -0.20f, 0), new Vector3(36, 0.2f, 30), floor);
            Material lane = new(lit) { name = "Disposable lane stripe", color = new Color(0.30f, 0.36f, 0.40f) };
            materials.Add(lane);
            for (int i = -2; i <= 2; i++) CreateBox(stage, "Preview floor seam", new Vector3(i * 5.5f, -0.094f, 0), new Vector3(0.025f, 0.006f, 25), lane);
            for (int i = 0; i < 3; i++)
            {
                Actor actor = CreateActor(stage, i, true);
                actor.Root.transform.position = new Vector3((1 - i) * 5.6f, 0, 0);
                actor.Root.transform.rotation = Quaternion.Euler(0f, -12f, 0f);
                // Align the actual visible mesh to the stage only; the production prefab transform is untouched.
                Bounds bounds = VisualBounds(actor);
                actor.Root.transform.position += Vector3.up * (-0.09f - bounds.min.y);
                actors.Add(actor);
            }
            var cameraObject = new GameObject("Disposable Larguirucho preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, stage);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = stage;
            camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.065f, 0.09f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(13f, 9.8f, 23f);
            camera.transform.LookAt(new Vector3(0f, 2.5f, 0f));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            target.Create();
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var metrics = new StringBuilder("frame,time,variant,state,normalized_phase,skin_count,bone_references\n");
            int[] pilots = { 24, 77, 104 };
            string[] pilotNames = { "pilot-walk.png", "pilot-charge.png", "pilot-thrust.png" };
            int end = pilot ? pilots.Last() + 1 : FrameCount;
            for (int frame = 0; frame < end; frame++)
            {
                float time = frame / (float)FrameRate;
                foreach (Actor actor in actors)
                {
                    Stage(actor, time, 1f / FrameRate);
                    metrics.AppendLine(string.Join(",", frame, time.ToString("F4", Invariant), actor.Variant, actor.State,
                        actor.Phase.ToString("F4", Invariant), actor.Skins.Length, actor.Skins.Sum(s => s.bones.Length)));
                }
                int pilotIndex = Array.IndexOf(pilots, frame);
                if (!pilot || pilotIndex >= 0)
                {
                    SaveFrame(camera, target, image, pilot ? Output + "/" + pilotNames[pilotIndex] : Output + $"/frames/frame-{frame:D4}.png");
                }
                if (frame % 48 == 0) Debug.Log($"LARGUIRUCHO_PREVIEW {frame}/{end}");
            }
            File.WriteAllText(Output + (pilot ? "/pilot-metrics.csv" : "/capture-metrics.csv"), metrics.ToString());
            File.WriteAllText(Output + "/limits.txt",
                "Staged Unity presentation inspection, using all three production Chaser prefabs, their migrated rigs, original materials and actual Animator controllers.\n" +
                "The actors remain in place so the tread cycle, articulation and in-place Root can be inspected. This is not full gameplay, a collision test, or a movement/damage simulation. All gameplay scripts and colliders are disabled only on these disposable instances.\n" +
                "15 seconds at 24 fps. Left: base Chaser. Center: variant/Shocker. Right: Elite. No scene, prefab, importer or project asset is saved.\n" +
                "0-2s walk; 2-6s full staged attack cycle with each prefab's current windup/dash/recovery duration; 6-11s early-contact cycle (0.10s after dash start); 11-13.5s slow overheat locomotion; 13.5-15s stationary pose.\n" +
                "The capture manually supplies presentation states and phases to the Animator; independent runtime tests cover the behavior-to-animation bridge, collision timing and lifecycle.\n");
            Debug.Log("LARGUIRUCHO_PREVIEW_COMPLETE " + Path.GetFullPath(Output));
        }
        finally
        {
            foreach (Actor actor in actors) actor.Dispose();
            foreach (Material material in materials) Object.DestroyImmediate(material);
            if (image != null) Object.DestroyImmediate(image);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(stage);
        }
    }

    private static Bounds VisualBounds(Actor actor)
    {
        Bounds bounds = default;
        bool first = true;
        for (int i = 0; i < actor.Skins.Length; i++)
            foreach (Vector3 vertex in actor.Baked[i].vertices)
            {
                Vector3 world = actor.Skins[i].transform.TransformPoint(vertex);
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                else bounds.Encapsulate(world);
            }
        return bounds;
    }

    private static Light AddLight(Scene scene, string name, Vector3 euler, Color color, float intensity)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        SceneManager.MoveGameObjectToScene(light.gameObject, scene);
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(euler);
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << PreviewLayer;
        return light;
    }

    private static void CreateBox(Scene scene, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(box, scene);
        box.name = name;
        box.layer = PreviewLayer;
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(box.GetComponent<Collider>());
    }

    private static void SaveFrame(Camera camera, RenderTexture target, Texture2D image, string path)
    {
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; }
    }

    public static void Profile()
    {
        RequireEditMode();
        Directory.CreateDirectory(Output);
        var report = new StringBuilder("population,animated_bone_references,animator_batch_mean_ms,animator_batch_p95_ms,ten_death_bakes_ms\n");
        Scene stage = EditorSceneManager.NewPreviewScene();
        var actors = new List<Actor>();
        try
        {
            foreach (int population in new[] { 100, 300 })
            {
                while (actors.Count < population)
                {
                    Actor actor = CreateActor(stage, actors.Count % 3, false);
                    actor.Root.transform.position = new Vector3(actors.Count % 20 * 6f, 0, actors.Count / 20 * 6f);
                    actors.Add(actor);
                }
                for (int frame = 0; frame < 20; frame++)
                    for (int i = 0; i < population; i++) Stage(actors[i], (frame / 60f + i * 0.037f) % 15f, 1f / 60f);
                double[] samples = new double[90];
                var watch = new Stopwatch();
                for (int frame = 0; frame < samples.Length; frame++)
                {
                    watch.Restart();
                    for (int i = 0; i < population; i++) Stage(actors[i], (2f + frame / 60f + i * 0.037f) % 15f, 1f / 60f);
                    watch.Stop();
                    samples[frame] = watch.Elapsed.TotalMilliseconds;
                }
                var bake = new Mesh();
                double bakeMs;
                try
                {
                    watch.Restart();
                    for (int i = 0; i < 10; i++)
                        foreach (SkinnedMeshRenderer skin in actors[i].Skins) skin.BakeMesh(bake, false);
                    watch.Stop();
                    bakeMs = watch.Elapsed.TotalMilliseconds;
                }
                finally { Object.DestroyImmediate(bake); }
                Array.Sort(samples);
                report.AppendLine(string.Join(",", population, actors.Take(population).Sum(a => a.Skins.Sum(s => s.bones.Length)),
                    samples.Average().ToString("F3", Invariant), samples[(int)(samples.Length * 0.95f)].ToString("F3", Invariant), bakeMs.ToString("F3", Invariant)));
            }
            File.WriteAllText(Output + "/controlled-profile.csv", report.ToString());
            File.WriteAllText(Output + "/profile-limits.txt",
                $"Unity {Application.unityVersion}; CPU {SystemInfo.processorType}; GPU {SystemInfo.graphicsDeviceName}.\n" +
                "Controlled editor CPU sampling of 100/300 active production rigs, 20 warmup and 90 measured batches. Timed work includes setting presentation phases and Animator.Update at 1/60s; rendering, physics, gameplay, AI and full frame scheduling are excluded. AlwaysAnimate is forced for the disposable instances so visibility cannot hide work. This is not a frame-rate benchmark.\n" +
                "Ten-death-bake timing measures one BakeMesh per body/tread skin across 10 actors, excluding dissolve allocation, material setup and rendering. It is a local cost sample, not an upper bound on burst deaths.\n");
            Debug.Log("LARGUIRUCHO_PROFILE_COMPLETE\n" + report);
        }
        finally
        {
            foreach (Actor actor in actors) actor.Dispose();
            EditorSceneManager.ClosePreviewScene(stage);
        }
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run disposable Larguirucho capture outside Play Mode.");
    }
}
#endif
