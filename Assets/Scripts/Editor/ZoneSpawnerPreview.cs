#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Captures saved spawner assets in an isolated editor scene; never saves gameplay scenes.</summary>
public static class ZoneSpawnerPreview
{
    private const int TileSize = 480;
    private const int FrameCount = 32;
    private const float FramesPerSecond = 4f;
    private const string GameplayScene = "Assets/Scenes/GameplayScene.unity";
    private static readonly string[] Types = { "Drone", "Slime", "Chaser" };

    [MenuItem("ScrapWaves/Spawners/Capture Fabrication Platforms")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the authored spawners outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Spawner capture requires graphics; omit -nographics.");

        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_SPAWNER_PREVIEW_OUTPUT")
            ?? Path.GetFullPath(".utmp/spawner-presentation-20261103/preview");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Material floorMaterial = null;
        var evidence = new StringBuilder();
        try
        {
            var presentations = new List<ZoneSpawnerPresentation>();
            var instances = new List<GameObject>();
            Bounds commonBounds = new(Vector3.up * 5f, new Vector3(13f, 12f, 13f));
            foreach (string type in Types)
            {
                string path = "Assets/Prefabs/ZoneSpawner_" + type + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Missing spawner prefab: " + path);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                ZoneSpawnerPresentation presentation = instance.GetComponentInChildren<ZoneSpawnerPresentation>(true);
                if (presentation == null) throw new InvalidOperationException(path + " has no authored presentation.");
                presentation.ApplyState(true, true);
                presentation.SetAnimationTime(0f);
                Bounds bounds = BoundsOf(instance);
                commonBounds.Encapsulate(bounds);
                presentation.SetAnimationTime(3f);
                commonBounds.Encapsulate(BoundsOf(instance));
                presentation.SetAnimationTime(0f);
                ValidateReferences(presentation, path);
                evidence.AppendLine(path + ": bounds=" + bounds + "; renderers=" + instance.GetComponentsInChildren<Renderer>(true).Length);
                presentations.Add(presentation);
                instances.Add(instance);
                instance.SetActive(false);
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview floor";
            SceneManager.MoveGameObjectToScene(floor, preview);
            floor.transform.position = new Vector3(0f, -.65f, 0f);
            floor.transform.localScale = new Vector3(100f, .3f, 100f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            Shader floorShader = Shader.Find("Universal Render Pipeline/Lit");
            if (floorShader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            floorMaterial = new Material(floorShader) { name = "Temporary preview floor" };
            floorMaterial.SetColor("_BaseColor", new Color(.055f, .064f, .063f));
            floorMaterial.SetFloat("_Smoothness", .12f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            Light key = CreateLight(preview, "Preview key", new Vector3(48f, -34f, 0f), new Color(1f, .88f, .69f), 2.5f);
            Light fill = CreateLight(preview, "Preview fill", new Vector3(30f, 145f, 0f), new Color(.64f, .78f, 1f), .75f);
            Camera camera = CreateCamera(preview);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.orthographicSize = Mathf.Max(11.5f, commonBounds.extents.magnitude * .82f);
            Vector3 center = commonBounds.center;
            camera.transform.position = center + new Vector3(18f, 16f, -23f);
            camera.transform.LookAt(center);
            target = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;

            evidence.Insert(0, "Editor renders of saved authored prefabs. Columns: Drone | Slime | Chaser.\n" +
                "32 frames, 4 fps, 8 seconds: ready [0,3), consumed [3,5), refreshed [5,8).\n" +
                "Presentation state is explicitly sampled; this is not a recorded gameplay session. No scene was saved.\n");
            CaptureRow("ready.png", 0f, true, true);
            CaptureRow("consumed.png", 3.5f, false, false);
            CaptureRow("refreshed.png", 5f, true, true);
            for (int frame = 0; frame < FrameCount; frame++)
            {
                float time = frame / FramesPerSecond;
                bool available = time < 3f || time >= 5f;
                CaptureRow("frame-" + frame.ToString("D3") + ".png", time, available, available);
            }

            foreach (GameObject instance in instances) instance.SetActive(false);
            floor.SetActive(false);
            key.gameObject.SetActive(false);
            fill.gameObject.SetActive(false);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            target = null;
            CaptureProductionContext(camera, output, evidence);
            File.WriteAllText(Path.Combine(output, "evidence.txt"), evidence.ToString());
            Debug.Log("ZONE_SPAWNER_PREVIEW: " + output + "; saved assets, ready/consumed/refreshed and 32 motion frames rendered.");

            void CaptureRow(string filename, float time, bool armed, bool available)
            {
                Texture2D row = new(TileSize * Types.Length, TileSize, TextureFormat.RGB24, false);
                Texture2D tile = new(TileSize, TileSize, TextureFormat.RGB24, false);
                try
                {
                    for (int index = 0; index < instances.Count; index++)
                    {
                        instances[index].SetActive(true);
                        presentations[index].ApplyState(armed, available);
                        presentations[index].SetAnimationTime(time);
                        camera.Render();
                        RenderTexture.active = target;
                        tile.ReadPixels(new Rect(0, 0, TileSize, TileSize), 0, 0);
                        tile.Apply();
                        row.SetPixels(index * TileSize, 0, TileSize, TileSize, tile.GetPixels());
                        instances[index].SetActive(false);
                    }
                    row.Apply();
                    File.WriteAllBytes(Path.Combine(output, filename), row.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                    Object.DestroyImmediate(row);
                    Object.DestroyImmediate(tile);
                }
            }
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void ValidateReferences(ZoneSpawnerPresentation presentation, string path)
    {
        var fields = new SerializedObject(presentation);
        string[] references = { "_spawner", "_hologramRoot", "_hologramPivot", "_lampOnMaterial", "_lampOffMaterial" };
        foreach (string field in references)
            if (fields.FindProperty(field)?.objectReferenceValue == null)
                throw new InvalidOperationException(path + " is missing " + field);
        SerializedProperty lamps = fields.FindProperty("_cornerLamps");
        if (lamps == null || lamps.arraySize != 4)
            throw new InvalidOperationException(path + " must have four authored corner lamps.");
        for (int i = 0; i < lamps.arraySize; i++)
            if (lamps.GetArrayElementAtIndex(i).objectReferenceValue == null)
                throw new InvalidOperationException(path + " is missing corner lamp " + i);
    }

    private static Camera CreateCamera(Scene scene)
    {
        Camera camera = new GameObject("Fabrication platform review camera", typeof(Camera)).GetComponent<Camera>();
        SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .045f, .048f);
        camera.orthographic = true;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 1000f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;
        return camera;
    }

    private static Light CreateLight(Scene scene, string name, Vector3 rotation, Color color, float intensity)
    {
        Light light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        SceneManager.MoveGameObjectToScene(light.gameObject, scene);
        light.type = LightType.Directional;
        light.shadows = LightShadows.None;
        light.color = color;
        light.intensity = intensity;
        light.transform.rotation = Quaternion.Euler(rotation);
        return light;
    }

    private static Bounds BoundsOf(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
        if (renderers.Length == 0) throw new InvalidOperationException(root.name + " has no visible renderers.");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void CaptureProductionContext(Camera camera, string output, StringBuilder evidence)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath(GameplayScene);
        bool opened = !source.IsValid() || !source.isLoaded;
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        GameObject sampledHologram = null;
        bool hologramWasActive = false;
        var originalLampMaterials = new Dictionary<Renderer, Material[]>();
        try
        {
            if (opened) source = EditorSceneManager.OpenScene(GameplayScene, OpenSceneMode.Additive);
            ZoneSpawner[] spawners = source.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ZoneSpawner>(true)).ToArray();
            ZoneSpawner spawner = spawners.FirstOrDefault(z => z.name.Contains("Drone")) ?? spawners.FirstOrDefault();
            if (spawner == null) throw new InvalidOperationException("GameplayScene contains no zone spawners.");
            ZoneSpawnerPresentation presentation = spawner.GetComponentInChildren<ZoneSpawnerPresentation>(true);
            if (presentation == null) throw new InvalidOperationException(spawner.name + " has no authored presentation.");
            ValidateReferences(presentation, GameplayScene + "/" + spawner.name);
            var fields = new SerializedObject(presentation);
            sampledHologram = (GameObject)fields.FindProperty("_hologramRoot").objectReferenceValue;
            hologramWasActive = sampledHologram.activeSelf;
            Material readyMaterial = (Material)fields.FindProperty("_lampOnMaterial").objectReferenceValue;
            SerializedProperty lamps = fields.FindProperty("_cornerLamps");
            // Sample the authored ready appearance directly: do not touch gameplay or the
            // presentation controller's runtime caches on an already-open scene instance.
            sampledHologram.SetActive(true);
            for (int index = 0; index < lamps.arraySize; index++)
            {
                Renderer lamp = (Renderer)lamps.GetArrayElementAtIndex(index).objectReferenceValue;
                if (!originalLampMaterials.ContainsKey(lamp)) originalLampMaterials.Add(lamp, lamp.sharedMaterials);
                lamp.sharedMaterial = readyMaterial;
            }
            Bounds bounds = BoundsOf(spawner.gameObject);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(source);
            camera.orthographicSize = radius * 1.8f;
            camera.transform.position = bounds.center + new Vector3(radius * 2.5f, radius * 2f, -radius * 3.5f);
            camera.transform.LookAt(bounds.center - Vector3.up * (radius * .2f));
            target = new RenderTexture(1200, 900, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1200, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, "production-context.png"), image.EncodeToPNG());
            evidence.AppendLine("production-context.png: saved " + GameplayScene + ", " + spawner.name +
                ", position=" + spawner.transform.position + ", scale=" + spawner.transform.localScale +
                "; dedicated review camera sampling ready presentation at the authored resting pose, not live gameplay; original visual state restored.");
        }
        finally
        {
            foreach (var lamp in originalLampMaterials)
                if (lamp.Key != null) lamp.Key.sharedMaterials = lamp.Value;
            if (sampledHologram != null) sampledHologram.SetActive(hologramWasActive);
            camera.targetTexture = null;
            RenderTexture.active = previousTarget;
            if (image != null) Object.DestroyImmediate(image);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (opened && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
}
#endif
