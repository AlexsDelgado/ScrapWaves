#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Renders the actual saved station/prompt prefab states; never saves a scene or invokes a transaction.</summary>
public static class CraftingStationPreview
{
    [MenuItem("ScrapWaves/Crafting/Capture Station States")]
    public static void Capture()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Station preview requires graphics; omit -nographics.");
        string output = Path.GetFullPath(".utmp/crafting-stations/preview");
        Directory.CreateDirectory(output);
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previousTarget = RenderTexture.active;
        Material floorMaterial = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CraftingStationAuthoring.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Author the station prefab first.");
            GameObject active = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            GameObject broken = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Prepare(active, true);
            Prepare(broken, false);
            Bounds originalBounds = BoundsOf(active);
            float width = originalBounds.size.x;
            active.transform.position = new Vector3(-width * .75f, -originalBounds.min.y, 0f);
            broken.transform.position = new Vector3(width * .75f, -originalBounds.min.y, 0f);

            Camera camera = new GameObject("Station preview camera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.045f, .055f, .06f);
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 500f;
            Light key = new GameObject("Preview key", typeof(Light)).GetComponent<Light>();
            SceneManager.MoveGameObjectToScene(key.gameObject, scene);
            key.type = LightType.Directional;
            key.color = new Color(1f, .89f, .76f);
            key.intensity = 2.2f;
            key.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            Light fill = new GameObject("Preview fill", typeof(Light)).GetComponent<Light>();
            SceneManager.MoveGameObjectToScene(fill.gameObject, scene);
            fill.type = LightType.Directional;
            fill.color = new Color(.62f, .76f, 1f);
            fill.intensity = .7f;
            fill.transform.rotation = Quaternion.Euler(32f, 155f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0f, -.035f, 0f);
            floor.transform.localScale = new Vector3(width * 12f, .05f, width * 12f);
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(.075f, .085f, .09f));
            floorMaterial.SetFloat("_Smoothness", .1f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Bounds both = BoundsOf(active);
            both.Encapsulate(BoundsOf(broken));
            Frame(camera, both, 1600f / 900f);
            FacePrompt(active, camera);
            FacePrompt(broken, camera);
            Render(camera, output, "station-states.png", 1600, 900);
            broken.SetActive(false);
            Frame(camera, BoundsOf(active), 1.2f);
            FacePrompt(active, camera);
            Render(camera, output, "active-station.png", 1200, 1000);
            active.SetActive(false);
            broken.SetActive(true);
            Frame(camera, BoundsOf(broken), 1.2f);
            FacePrompt(broken, camera);
            Render(camera, output, "broken-station.png", 1200, 1000);

            GameObject prompt = broken.GetComponentInChildren<InteractPrompt>(true).gameObject;
            foreach (Renderer renderer in broken.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(prompt.transform)) renderer.enabled = false;
            Bounds promptBounds = BoundsOf(prompt);
            camera.orthographicSize = Mathf.Max(promptBounds.extents.x, promptBounds.extents.y) * 1.3f;
            camera.transform.position = promptBounds.center + Vector3.back * 10f;
            camera.transform.LookAt(promptBounds.center);
            FacePrompt(broken, camera);
            Render(camera, output, "broken-prompt.png", 800, 800);
            File.WriteAllText(Path.Combine(output, "evidence.txt"),
                "Unity renders of the saved CraftingStation prefab; active left, broken/inactive right. " +
                "Authored models/materials/prompt are sampled in an isolated preview scene. " +
                "No gameplay scene is saved, no transaction or persistent save is invoked. " +
                "This visual fixture is not a recording of player movement.\n" +
                "Active bounds: " + originalBounds + "\n");
            Debug.Log("CRAFTING_STATION_PREVIEW: " + output);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            EditorSceneManager.ClosePreviewScene(scene);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
        }
    }

    private static void Prepare(GameObject root, bool available)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        var station = new SerializedObject(root.GetComponent<CraftingStation>());
        ((GameObject)station.FindProperty("_activeVisual").objectReferenceValue).SetActive(available);
        ((GameObject)station.FindProperty("_brokenVisual").objectReferenceValue).SetActive(!available);
        var prompt = new SerializedObject(root.GetComponentInChildren<InteractPrompt>(true));
        var visual = (Transform)prompt.FindProperty("_visual").objectReferenceValue;
        visual.gameObject.SetActive(true);
        ((GameObject)prompt.FindProperty("_availableVisual").objectReferenceValue).SetActive(available);
        ((GameObject)prompt.FindProperty("_unavailableVisual").objectReferenceValue).SetActive(!available);
    }

    private static void FacePrompt(GameObject root, Camera camera)
    {
        Transform prompt = root.GetComponentInChildren<InteractPrompt>(true).transform;
        prompt.rotation = Quaternion.LookRotation(prompt.position - camera.transform.position, Vector3.up);
    }

    private static Bounds BoundsOf(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void Frame(Camera camera, Bounds bounds, float aspect)
    {
        camera.orthographicSize = Mathf.Max(bounds.size.y * .7f, bounds.size.x / aspect * .68f);
        camera.transform.position = bounds.center + new Vector3(.35f, .55f, -1f).normalized * bounds.size.magnitude * 2.5f;
        camera.transform.LookAt(bounds.center);
    }

    private static void Render(Camera camera, string output, string name, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
#endif
