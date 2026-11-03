using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class HudVfxTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void Fills_KeepGameplayValues_Accessibility_AndMaterialOwnership()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var previous = PresentationAccessibilityRuntime.Current;
        var root = new GameObject("Isolated HUD test");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        LiquidHealthBarVfx health = null;
        EnergyMeterVfx energy = null;
        try
        {
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
            var hp = MakeImage(root, "HP");
            var xp = MakeImage(root, "XP");
            var original = hp.material;
            health = hp.gameObject.AddComponent<LiquidHealthBarVfx>();
            energy = xp.gameObject.AddComponent<EnergyMeterVfx>();
            Call(health, "OnEnable"); Call(energy, "OnEnable");
            Assert.That(hp.material, Is.Not.SameAs(xp.material));
            Assert.That(hp.type, Is.EqualTo(Image.Type.Simple));
            health.SetFill(.7f, true);
            health.SetFill(.2f);
            Assert.That(hp.fillAmount, Is.EqualTo(.2f));
            Assert.That(hp.material.GetFloat("_Fill"), Is.EqualTo(.7f));
            Assert.That(hp.material.GetFloat("_Impact"), Is.GreaterThan(0f));
            health.SetFill(0f);
            Assert.That(hp.material.GetFloat("_Fill"), Is.Zero);
            health.SetFill(float.NaN);
            Assert.That(hp.fillAmount, Is.Zero);

            energy.SetExperience(.9f, 1);
            energy.SetExperience(.12f, 2);
            Assert.That(xp.fillAmount, Is.EqualTo(.12f));
            Assert.That(xp.material.GetFloat("_Fill"), Is.EqualTo(1f));
            var animation = typeof(EnergyMeterVfx).GetField("_animation", Private).GetValue(energy);
            for (int i = 0; i < 6; i++)
                animation.GetType().GetMethod("Advance").Invoke(animation, new object[] { .1f });
            Call(energy, "ApplyProperties");
            Assert.That(xp.material.GetFloat("_Fill"), Is.EqualTo(.12f).Within(.001f));

            foreach (var preferences in new[] {
                previous.WithReducedMotion(true), previous.WithReducedFlash(true) })
            {
                PresentationAccessibilityRuntime.Apply(preferences);
                health.SetFill(.22f);
                energy.SetHeat(.5f, false);
                energy.SetHeat(1f, true);
                Assert.That(hp.material.GetFloat("_Fill"), Is.EqualTo(.22f));
                Assert.That(hp.material.GetFloat("_Impact"), Is.Zero);
                Assert.That(hp.material.GetFloat("_LowHealth"), Is.Zero);
                Assert.That(xp.material.GetFloat("_HeatBurst"), Is.Zero);
                energy.SetExperience(.3f, 3);
                Assert.That(xp.material.GetFloat("_Fill"), Is.EqualTo(.3f));
                Assert.That(xp.material.GetFloat("_LevelSweep"), Is.EqualTo(-1f));
            }
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
            energy.SetHeat(.4f, false, true);
            energy.SetHeat(.2f, true, true);
            Assert.That(xp.fillAmount, Is.EqualTo(1f));
            energy.SetHeat(0f, false, true);
            Assert.That(xp.material.GetFloat("_Fill"), Is.Zero);

            Call(health, "OnDisable");
            Assert.That(hp.material, Is.SameAs(original));
            Assert.That(hp.type, Is.EqualTo(Image.Type.Filled));
            health.SetFill(.33f);
            Call(health, "OnEnable");
            Assert.That(hp.material.GetFloat("_Fill"), Is.EqualTo(.33f));
            Call(energy, "OnDisable");
            energy.SetExperience(.42f, 3);
            Call(energy, "OnEnable");
            Assert.That(xp.material.GetFloat("_Fill"), Is.EqualTo(.42f));
        }
        finally
        {
            if (health != null) Call(health, "OnDisable");
            if (energy != null) Call(energy, "OnDisable");
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            PresentationAccessibilityRuntime.Apply(previous);
        }
    }

    [Test]
    public void Shaders_RenderPartialAndFullFills_WithAtlasUvs()
    {
        var target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var mesh = new Mesh();
        var previous = RenderTexture.active;
        int previousZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
        // Offset vertices and cropped atlas UVs reproduce the old blank-bar failure.
        mesh.vertices = new[] { new Vector3(.1f,.1f,0), new Vector3(.9f,.1f,0), new Vector3(.9f,.9f,0), new Vector3(.1f,.9f,0) };
        mesh.uv = new[] { new Vector2(.25f,.25f), new Vector2(.75f,.25f), new Vector2(.75f,.75f), new Vector2(.25f,.75f) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        try
        {
            Shader.SetGlobalInt("unity_GUIZTestMode", 8);
            for (int mode = 0; mode < 3; mode++)
            {
                var shader = Resources.Load<Shader>(mode == 0 ? "UI/LiquidHealthBar" : "UI/EnergyMeter");
                Assert.That(shader, Is.Not.Null);
                Assert.That(shader.isSupported, Is.True);
                var material = new Material(shader);
                try
                {
                    material.SetTexture("_MainTex", Texture2D.whiteTexture);
                    material.SetVector("_SpriteUVRect", new Vector4(.25f, .25f, .5f, .5f));
                    material.SetVector(mode == 0 ? "_LiquidSize" : "_MeterSize", new Vector4(mode == 1 ? 100 : 400, 100, 0, 0));
                    if (mode > 0) material.SetFloat("_ExperienceMode", mode == 2 ? 1 : 0);
                    foreach (float fill in new[] { 0f, .5f, 1f })
                    {
                        material.SetFloat("_Fill", fill);
                        RenderTexture.active = target;
                        GL.Clear(true, true, Color.clear);
                        GL.PushMatrix();
                        try
                        {
                            GL.LoadOrtho();
                            Assert.That(material.SetPass(0), Is.True);
                            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                        }
                        finally { GL.PopMatrix(); }
                        pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                        pixels.Apply();
                        float filled = Brightness(pixels.GetPixel(mode == 1 ? 64 : 36, mode == 1 ? 36 : mode == 2 ? 38 : 64));
                        float empty = Brightness(pixels.GetPixel(mode == 1 ? 64 : 92, mode == 1 ? 92 : mode == 2 ? 38 : 64));
                        if (fill == 0f) Assert.That(filled, Is.LessThan(mode == 2 ? .2f : .06f), $"Mode {mode} at zero");
                        else Assert.That(filled, Is.GreaterThan(.08f), $"Mode {mode} visible at {fill}");
                        if (fill == .5f) Assert.That(empty, Is.LessThan(filled * .5f), $"Mode {mode} fill direction");
                        if (fill == 1f) Assert.That(empty, Is.GreaterThan(.08f), $"Mode {mode} full");
                    }
                    Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
                }
                finally { Object.DestroyImmediate(material); }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            Shader.SetGlobalInt("unity_GUIZTestMode", previousZTest);
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(mesh);
        }
    }

    private static Image MakeImage(GameObject root, string name)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(root.transform, false);
        HudUiFactory.EnsureHorizontalFill(image);
        return image;
    }
    private static float Brightness(Color c) => Mathf.Max(c.r, c.g, c.b);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
}
