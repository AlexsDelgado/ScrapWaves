using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Renders the actual HUD art, fill shaders and shield mesh in an isolated scene.</summary>
public static class ShieldHudPreviewCapture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static RenderTexture _target;
    private static Vector2 _size;

    [MenuItem("ScrapWaves/UI/Save HUD preview")]
    public static void Export()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GameplayHud V2.prefab");
        var hud = prefab.GetComponentInChildren<PlayerBarsHud>(true);
        var sourceRoot = (RectTransform)hud.transform;
        Vector2 size = sourceRoot.rect.size;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Isolated HUD preview", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        var target = RenderTexture.GetTemporary(Mathf.CeilToInt(size.x * 2), Mathf.CeilToInt(size.y * 2), 0, RenderTextureFormat.ARGB32);
        _target = target; _size = size;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        int previousZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
        try
        {
            Shader.SetGlobalInt("unity_GUIZTestMode", 8);
            foreach (var state in new[] { new Vector2Int(0, 0), new Vector2Int(1, 1),
                new Vector2Int(3, 3), new Vector2Int(6, 6), new Vector2Int(6, 4), new Vector2Int(6, 0) })
            {
                int capacity = state.x, shields = state.y;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(.025f, .035f, .03f, 1f));
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, size.x, 0, size.y);
                    foreach (Transform child in sourceRoot)
                    {
                        Image source = child.GetComponent<Image>();
                        if (source == null) continue;
                        Rect bounds = RelativeBounds(source.rectTransform, sourceRoot);
                        var go = new GameObject(source.name, typeof(RectTransform));
                        go.transform.SetParent(root.transform, false);
                        var image = go.AddComponent<Image>();
                        image.rectTransform.pivot = Vector2.zero;
                        image.rectTransform.sizeDelta = bounds.size;
                        image.sprite = source.sprite;
                        image.color = source.color;
                        image.type = Image.Type.Simple;
                        MonoBehaviour effect = null;
                        try
                        {
                            if (source.name == "HpFill")
                            {
                                var liquid = go.AddComponent<LiquidHealthBarVfx>();
                                effect = liquid; Call(effect, "OnEnable");
                                liquid.SetFill(.65f, true);
                            }
                            else if (source.name == "XpFill" || source.name == "OverheatFill")
                            {
                                var energy = go.AddComponent<EnergyMeterVfx>();
                                effect = energy; Call(effect, "OnEnable");
                                if (source.name == "XpFill") energy.SetExperience(.4f, 1, true);
                                else energy.SetHeat(.6f, false, true);
                            }
                            DrawGraphic(image, bounds.position);
                        }
                        finally
                        {
                            if (effect != null) Call(effect, "OnDisable");
                            Object.DestroyImmediate(go);
                        }
                    }
                    var shield = hud.GetComponentInChildren<ShieldChargesGraphic>(true);
                    Rect shieldBounds = RelativeBounds(shield.rectTransform, sourceRoot);
                    var shieldGo = new GameObject("Shield overlay", typeof(RectTransform));
                    shieldGo.transform.SetParent(root.transform, false);
                    var overlay = shieldGo.AddComponent<ShieldChargesGraphic>();
                    EditorUtility.CopySerialized(shield, overlay);
                    overlay.rectTransform.pivot = Vector2.zero;
                    overlay.rectTransform.sizeDelta = shieldBounds.size;
                    overlay.SetCharges(shields, capacity);
                    try { DrawGraphic(overlay, shieldBounds.position); }
                    finally { Object.DestroyImmediate(shieldGo); }
                }
                finally { GL.PopMatrix(); }
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                byte[] png = pixels.EncodeToPNG();
                File.WriteAllBytes(Path.GetFullPath($"Library/HudIntegrated-capacity-{capacity}-charges-{shields}.png"), png);
                if (capacity == 6) File.WriteAllBytes(Path.GetFullPath($"Library/HudIntegrated-{shields}.png"), png);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            Shader.SetGlobalInt("unity_GUIZTestMode", previousZTest);
            RenderTexture.ReleaseTemporary(target);
            _target = null;
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Rect RelativeBounds(RectTransform rect, Transform root)
    {
        // Asset transforms have no evaluated world corners. Resolve the authored anchors directly.
        Rect parent = ((RectTransform)root).rect;
        Vector2 size = Vector2.Scale(parent.size, rect.anchorMax - rect.anchorMin) + rect.sizeDelta;
        Vector2 anchor = rect.anchorMin + Vector2.Scale(rect.anchorMax - rect.anchorMin, rect.pivot);
        Vector2 pivot = parent.min + Vector2.Scale(parent.size, anchor) + rect.anchoredPosition;
        return new Rect(pivot - Vector2.Scale(size, rect.pivot), size);
    }

    private static void DrawGraphic(Graphic graphic, Vector2 position)
    {
        var mesh = new Mesh();
        var material = new Material(graphic.materialForRendering);
        try
        {
            using (var vertices = new VertexHelper())
            {
                graphic.GetType().GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null)
                    .Invoke(graphic, new object[] { vertices });
                vertices.FillMesh(mesh);
            }
            if (mesh.vertexCount == 0 && graphic is ShieldChargesGraphic shield && shield.Capacity == 0)
                return;
            if (mesh.vertexCount == 0 || graphic.rectTransform.rect.width <= 0f)
                throw new System.InvalidOperationException("HUD preview geometry is empty: " + graphic.name);
            material.mainTexture = graphic.mainTexture;
            RenderTexture.active = _target;
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, _size.x, 0, _size.y);
                material.SetPass(0);
                Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(position));
            }
            finally { GL.PopMatrix(); }
        }
        finally { Object.DestroyImmediate(material); Object.DestroyImmediate(mesh); }
    }

    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
}
