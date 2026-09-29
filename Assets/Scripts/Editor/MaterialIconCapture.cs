#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Saca una captura de cada pickup de material (los prefabs de MaterialDropVisualCatalog) con fondo
/// transparente, para usarla como ícono del HUD. Los PNG crudos van a ArtSource/UI/MaterialIcons/renders/;
/// después `python ArtSource/Tools/finish_material_icons.py` les pone el contorno y los deja en
/// Assets/Art/UI/Icons/Materials/.
/// </summary>
public static class MaterialIconCapture
{
    private const string CatalogPath = "Assets/ScriptableObjects/Economy/MaterialDropVisualCatalog.asset";
    private const string RenderFolder = "ArtSource/UI/MaterialIcons/renders";
    private const int Resolution = 512;
    private const float MinCoverageRatio = 0.55f;

    /// <summary>Ángulos 3/4 candidatos, en orden de preferencia (las chapas de canto no se leen).</summary>
    private static readonly Vector3[] ViewDirections =
    {
        new(1.2f, 0.9f, -1f), new(-1.2f, 0.9f, -1f), new(1f, 1.6f, 0.4f), new(0.3f, 1.8f, -0.6f),
        new(1.2f, 0.5f, 1f), new(-1f, 0.6f, 1.2f), new(0.2f, 0.7f, -1.5f), new(1.5f, 0.7f, 0.1f)
    };

    [MenuItem("ScrapWaves/UI/Capture Material Pickup Icons")]
    public static void CaptureAll()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<MaterialDropVisualCatalog>(CatalogPath);
        if (catalog == null)
            throw new FileNotFoundException($"Falta {CatalogPath}.");
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), RenderFolder);
        Directory.CreateDirectory(outDir);

        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            GameObject prefab = catalog.GetVisualPrefab(type);
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(MaterialIconCapture)}] {type} no tiene prefab visual en el catálogo.");
                continue;
            }
            // El primer ángulo de la lista que llegue a ~la mitad de la mejor silueta: conserva el 3/4 y solo
            // cambia de vista cuando la pieza queda de canto.
            var shots = new byte[ViewDirections.Length][];
            var coverages = new int[ViewDirections.Length];
            for (int i = 0; i < ViewDirections.Length; i++)
                shots[i] = Capture(prefab, ViewDirections[i].normalized, out coverages[i]);
            int threshold = Mathf.RoundToInt(Mathf.Max(coverages) * MinCoverageRatio);
            int chosen = Array.FindIndex(coverages, c => c >= threshold);
            File.WriteAllBytes(Path.Combine(outDir, $"Material_{type}.png"), shots[chosen]);
        }
        Debug.Log($"[{nameof(MaterialIconCapture)}] Capturas en {RenderFolder}.");
    }

    private static byte[] Capture(GameObject prefab, Vector3 viewDirection, out int coverage)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            Bounds bounds = RendererBounds(instance);

            AddLight(scene, "Key", Quaternion.Euler(45f, -35f, 0f), 1.3f, new Color(1f, 0.96f, 0.9f));
            AddLight(scene, "Rim", Quaternion.Euler(20f, 150f, 0f), 0.8f, new Color(0.8f, 0.88f, 1f));

            var camGo = new GameObject("IconCam");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.transform.position = bounds.center + viewDirection * (bounds.extents.magnitude * 4f);
            cam.transform.LookAt(bounds.center);
            cam.orthographicSize = bounds.extents.magnitude * 1.02f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = bounds.extents.magnitude * 10f;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            coverage = 0;
            foreach (Color32 c in tex.GetPixels32())
                if (c.a > 32)
                    coverage++;
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return png;
        }
        finally
        {
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void AddLight(Scene scene, string name, Quaternion rotation, float intensity, Color color)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = rotation;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
    }

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.one);
        foreach (Renderer r in renderers)
            if (r is not ParticleSystemRenderer)
                bounds.Encapsulate(r.bounds);
        return bounds;
    }
}
#endif
