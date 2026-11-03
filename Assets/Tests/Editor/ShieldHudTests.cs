using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ShieldHudTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void ShieldEvents_UpdateCapacityConsumptionRechargeAndDetach()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Isolated shield HUD test");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        PlayerBarsHud hud = null;
        try
        {
            var health = root.AddComponent<PlayerHealth>();
            Set(health, "_currentHealth", 70);
            var view = new GameObject("Shield", typeof(RectTransform)).AddComponent<ShieldChargesGraphic>();
            view.transform.SetParent(root.transform, false);
            hud = root.AddComponent<PlayerBarsHud>();
            Set(hud, "_playerHealth", health);
            Set(hud, "_shieldMeter", view);
            Call(hud, "BindEvents");
            Call(hud, "RefreshShields");
            Assert.That(view.Capacity, Is.Zero);
            health.SetShieldConfig(6, 6f);
            Assert.That(view.Capacity, Is.EqualTo(6));
            Assert.That(view.DisplayedCharges, Is.EqualTo(6));

            // The same notification emitted by shield consumption; no quest/save side effects.
            Set(health, "_shieldCharges", 4);
            ((Action)Field(health, "OnShieldChanged").GetValue(health))?.Invoke();
            Assert.That(view.DisplayedCharges, Is.EqualTo(4));
            Set(health, "_lastDamageTime", 10f);
            typeof(PlayerHealth).GetMethod("TickShieldRecharge", Private).Invoke(health, new object[] { 15f });
            Assert.That(view.DisplayedCharges, Is.EqualTo(4));
            typeof(PlayerHealth).GetMethod("TickShieldRecharge", Private).Invoke(health, new object[] { 16f });
            Assert.That(view.DisplayedCharges, Is.EqualTo(6));
            Assert.That(health.CurrentHealth, Is.EqualTo(70));
            health.SetShieldConfig(1, 8f);
            Assert.That(view.Capacity, Is.EqualTo(1));
            Assert.That(view.DisplayedCharges, Is.EqualTo(1));
            health.SetShieldConfig(0, 0f);
            Assert.That(view.Capacity, Is.Zero);
            Call(hud, "UnbindEvents");
            Assert.That(Field(health, "OnShieldChanged").GetValue(health), Is.Null);
            health.SetShieldConfig(3, 6f);
            Assert.That(view.Capacity, Is.Zero);
            Call(hud, "BindEvents");
            Call(hud, "RefreshShields");
            Assert.That(view.DisplayedCharges, Is.EqualTo(3));
            var callbacks = (Delegate)Field(health, "OnShieldChanged").GetValue(health);
            Assert.That(callbacks.GetInvocationList().Length, Is.EqualTo(1));
        }
        finally
        {
            if (hud != null) Call(hud, "UnbindEvents");
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void AuthoredShield_AboveLife_HasDistinctCompartments()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GameplayHud V2.prefab");
        var hud = prefab.GetComponentInChildren<PlayerBarsHud>(true);
        var serialized = new SerializedObject(hud);
        var authored = (ShieldChargesGraphic)serialized.FindProperty("_shieldMeter").objectReferenceValue;
        var hp = (Image)serialized.FindProperty("_hpFill").objectReferenceValue;
        Assert.That(authored, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<ShieldChargesGraphic>(true).Length, Is.EqualTo(1));
        Assert.That(authored.raycastTarget, Is.False);
        var lifeArt = hud.transform.Find("Panel").GetComponent<Image>().sprite;
        Assert.That(new SerializedObject(authored).FindProperty("_frameArtwork").objectReferenceValue, Is.EqualTo(lifeArt));
        Assert.That(authored.mainTexture, Is.SameAs(lifeArt.texture));
        Assert.That(authored.rectTransform.anchoredPosition.y - authored.rectTransform.rect.height,
            Is.GreaterThan(hp.rectTransform.anchoredPosition.y));

        var scene = EditorSceneManager.NewPreviewScene();
        var go = new GameObject("Shield render check", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(go, scene);
        var view = go.AddComponent<ShieldChargesGraphic>();
        EditorUtility.CopySerialized(authored, view);
        view.rectTransform.pivot = Vector2.zero;
        Vector2 meterSize = authored.rectTransform.rect.size;
        view.rectTransform.sizeDelta = meterSize;
        var mesh = new Mesh();
        var material = new Material(Shader.Find("UI/Default"));
        RenderTexture previous = RenderTexture.active;
        int previousZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
        try
        {
            material.mainTexture = view.mainTexture;
            Shader.SetGlobalInt("unity_GUIZTestMode", 8);
            // Count visible blue islands at full and reduced HUD scale, independently of slot math.
            foreach (Vector2Int size in new[] { new Vector2Int(222,29), new Vector2Int(148,20) })
            {
                var pixels = new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                var target = RenderTexture.GetTemporary(size.x,size.y,0,RenderTextureFormat.ARGB32);
                try
                {
                    List<Vector2Int> fullSlots = null;
                    var frameWidths = new float[7];
                    for (int capacity = 6; capacity >= 0; capacity--)
                    for (int charges = capacity; charges >= 0; charges--)
                    {
                        view.SetCharges(charges, capacity);
                        using (var vertices = new VertexHelper())
                        {
                            typeof(ShieldChargesGraphic).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null).Invoke(view, new object[] { vertices });
                            vertices.FillMesh(mesh);
                        }
                        if (capacity == 0)
                            Assert.That(mesh.vertexCount, Is.Zero, "Before acquiring a shield, the complete strip must be hidden.");
                        else
                        {
                            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                            if (charges == capacity)
                            {
                                frameWidths[capacity] = mesh.bounds.size.x;
                                if (capacity < 6)
                                    Assert.That(frameWidths[capacity], Is.LessThan(frameWidths[capacity + 1]),
                                        "Only acquired compartments should extend the visible container.");
                            }
                            Assert.That(mesh.bounds.size.x, Is.EqualTo(frameWidths[capacity]).Within(.001f),
                                "Damage and recharge must not hide or resize acquired compartments.");
                        }
                        RenderTexture.active = target;
                        GL.Clear(true,true,new Color(.035f,.05f,.06f,1f));
                        GL.PushMatrix();
                        try
                        {
                            GL.LoadPixelMatrix(0,meterSize.x,0,meterSize.y);
                            Assert.That(material.SetPass(0), Is.True);
                            if (mesh.vertexCount > 0) Graphics.DrawMeshNow(mesh,Matrix4x4.identity);
                        }
                        finally { GL.PopMatrix(); }
                        pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);
                        pixels.Apply();
                        var blueSpans = new List<Vector2Int>();
                        int blueStart = -1;
                        bool previousBlue = false;
                        // Exclude only the shield icon on the left, never any compartment.
                        for (int x = Mathf.CeilToInt(32f / meterSize.x * size.x); x < size.x; x++)
                        {
                            Color c = pixels.GetPixel(x,size.y/2);
                            bool blue = c.b > .6f && c.b > c.r + .25f;
                            if (blue && !previousBlue) blueStart = x;
                            if (!blue && previousBlue) blueSpans.Add(new Vector2Int(blueStart, x - 1));
                            previousBlue = blue;
                        }
                        if (previousBlue) blueSpans.Add(new Vector2Int(blueStart, size.x - 1));
                        Assert.That(blueSpans.Count, Is.EqualTo(charges),
                            $"Every charge must remain separately readable: {charges}/{capacity}, {size.x}px HUD.");
                        if (capacity == 6 && charges == 6) fullSlots = blueSpans;
                        for (int slot = 0; slot < blueSpans.Count; slot++)
                            Assert.That(blueSpans[slot], Is.EqualTo(fullSlots[slot]),
                                "Each blue cell must keep its six-slot width and position as capacity changes.");
                        if (capacity == 6 && size.x == 222 && (charges == 0 || charges == 4 || charges == 6))
                            File.WriteAllBytes(Path.GetFullPath($"Library/ShieldHud-six-{charges}.png"),pixels.EncodeToPNG());
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(pixels);
                }
            }
            view.SetCharges(0, 0);
            using (var vertices = new VertexHelper())
            {
                typeof(ShieldChargesGraphic).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null).Invoke(view, new object[] { vertices });
                Assert.That(vertices.currentVertCount, Is.Zero, "The frame and icon must also be hidden without shield capacity.");
                view.SetCharges(0, 3);
                typeof(ShieldChargesGraphic).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null).Invoke(view, new object[] { vertices });
                Assert.That(vertices.currentVertCount, Is.GreaterThan(0), "Acquired sockets remain visible while all charges recharge.");
                view.SetCharges(0, 0);
                typeof(ShieldChargesGraphic).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null).Invoke(view, new object[] { vertices });
                Assert.That(vertices.currentVertCount, Is.Zero, "Removing shield capacity clears the previous mesh.");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            Shader.SetGlobalInt("unity_GUIZTestMode", previousZTest);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static FieldInfo Field(object obj, string field) => obj.GetType().GetField(field, Private);
    private static void Set(object obj, string field, object value) => Field(obj, field).SetValue(obj, value);
    private static void Call(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);
}
