using System;
using System.Reflection;
using TMPro;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class HeatPresentationTests
{
    [Test]
    public void Warning_IsOneCrossing_RearmsBelow75_AndNeverReplaysDuringCooling()
    {
        var state = new HeatPresentationState();
        Assert.That(state.Read(0f, false, false), Is.False);
        Assert.That(state.Read(.799f, false, false), Is.False);
        Assert.That(state.Read(.8f, false, false), Is.True);
        foreach (float heat in new[] { .81f, .79f, .8f, .99f })
            Assert.That(state.Read(heat, false, false), Is.False);
        Assert.That(state.Read(1f, true, false), Is.False);
        foreach (float heat in new[] { .9f, .8f, .6f, .1f, 0f })
        {
            Assert.That(state.Read(heat, false, true), Is.False);
            Assert.That(state.Phase, Is.EqualTo(heat > 0f ? HeatPresentationPhase.Cooling : HeatPresentationPhase.Normal));
        }
        state.Read(0f, false, false);
        Assert.That(state.Read(.8f, false, false), Is.True);
        state.Read(.75f, false, false);
        Assert.That(state.Read(.8f, false, false), Is.True);
    }

    [Test]
    public void InitialBindingAndDirectOverheat_AreSilent_AndResetRestoresNormal()
    {
        var state = new HeatPresentationState();
        Assert.That(state.Read(.9f, false, false), Is.False);
        Assert.That(state.Phase, Is.EqualTo(HeatPresentationPhase.Warning));
        state.Reset();
        state.Read(0f, false, false);
        Assert.That(state.Read(.2f, true, false), Is.False);
        Assert.That(state.Heat, Is.EqualTo(1f));
        Assert.That(state.Phase, Is.EqualTo(HeatPresentationPhase.Overheated));
        state.Reset();
        Assert.That(state.Heat, Is.Zero);
        Assert.That(state.Phase, Is.EqualTo(HeatPresentationPhase.Normal));
        Assert.That(state.Read(float.NaN, false, false), Is.False);
        Assert.That(state.Heat, Is.Zero);
    }

    [Test]
    public void GameplayPrefab_HasBoundPresentation_AndPreservesFilledGauge()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GameplayHud V2.prefab");
        var controllers = prefab.GetComponentsInChildren<HeatPresentationController>(true);
        Assert.That(controllers.Length, Is.EqualTo(1));
        var serialized = new SerializedObject(controllers[0]);
        foreach (string field in new[] { "_screenProfile", "_edgeMaterial" })
            Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
        Assert.That(serialized.FindProperty("_statusLabel").objectReferenceValue, Is.Null);
        Assert.That(controllers[0].transform.Find("HeatStatus"), Is.Null);
        var bars = new SerializedObject(prefab.GetComponentInChildren<PlayerBarsHud>(true));
        var fill = (Image)bars.FindProperty("_overheatFill").objectReferenceValue;
        Assert.That(fill.sprite, Is.Not.Null);
        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Vertical));
        Assert.That(fill.fillOrigin, Is.Zero);
        var material = (Material)serialized.FindProperty("_edgeMaterial").objectReferenceValue;
        Assert.That(material.shader.isSupported, Is.True);
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
    }

    [Test]
    public void Events_Accessibility_AndDisable_KeepIndependentResourcesAndSubscriptions()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        var previous = PresentationAccessibilityRuntime.Current;
        HeatPresentationController view = null;
        try
        {
            root = new GameObject("Heat feedback isolated test");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            var heat = root.AddComponent<HeatManager>();
            var overheat = root.AddComponent<OverheatManager>();
            var health = root.AddComponent<PlayerHealth>();
            view = root.AddComponent<HeatPresentationController>();
            Set(view, "_heatManager", heat);
            Set(view, "_overheatManager", overheat);
            Set(view, "_playerHealth", health);
            Set(view, "_screenProfile", AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/HeatFeedbackProfile.asset"));
            Set(view, "_edgeMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/GameFeel/Materials/HeatEdges.mat"));
            var oldLabel = new GameObject("HeatStatus", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            oldLabel.transform.SetParent(root.transform, false);
            Set(view, "_statusLabel", oldLabel);
            Call(view, "OnEnable");
            Assert.That(oldLabel.gameObject.activeSelf, Is.False, "An old live HUD must not retain the removed percentage.");
            Call(view, "ResolveSources");
            Assert.That(SubscriberCount(heat, "OnHeatChanged"), Is.EqualTo(1));
            Assert.That(SubscriberCount(overheat, "OnOverheatStarted"), Is.EqualTo(1));
            Set(heat, "_currentHeat", heat.PointsFirstSegment);
            Raise(heat, "OnHeatChanged");
            Assert.That(view.DisplayedHeat, Is.EqualTo(.8f).Within(.0001f));
            Assert.That(view.Phase, Is.EqualTo(HeatPresentationPhase.Warning));

            Set(overheat, "_isOverheating", true);
            Raise(overheat, "OnOverheatStarted");
            Assert.That(view.Phase, Is.EqualTo(HeatPresentationPhase.Overheated));
            var runtimeProfile = Get<VolumeProfile>(view, "_runtimeProfile");
            var assetProfile = Get<VolumeProfile>(view, "_screenProfile");
            Assert.That(runtimeProfile, Is.Not.SameAs(assetProfile));
            Assert.That(runtimeProfile.TryGet(out Vignette vignette), Is.True);
            assetProfile.TryGet(out Vignette original);
            float originalIntensity = original.intensity.value;
            vignette.intensity.value = .1f;
            Assert.That(original.intensity.value, Is.EqualTo(originalIntensity));

            var runtimeMaterial = Get<Material>(view, "_runtimeEdgeMaterial");
            var authoredMaterial = Get<Material>(view, "_edgeMaterial");
            var runtimeRoot = Get<GameObject>(view, "_runtimeRoot");
            Assert.That(runtimeMaterial, Is.Not.SameAs(authoredMaterial));
            Assert.That(runtimeRoot.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            var surface = runtimeRoot.GetComponentInChildren<RawImage>(true);
            Assert.That(surface.raycastTarget, Is.False);
            Assert.That(runtimeRoot.GetComponentInChildren<GraphicRaycaster>(true), Is.Null);
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
            Set(view, "_edgeWeight", 1f);
            Set(view, "_screenWeight", 1f);
            Call(view, "ApplyScreenWeight");
            Assert.That(runtimeMaterial.GetFloat("_Intensity"), Is.GreaterThan(.7f));
            Assert.That(authoredMaterial.GetFloat("_Intensity"), Is.Zero, "Never change the shared material.");
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default.WithReducedMotion(true));
            float frozenTime = runtimeMaterial.GetFloat("_EffectTime");
            view.GetType().GetMethod("AdvanceFeedback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { .1f });
            Assert.That(runtimeMaterial.GetFloat("_EffectTime"), Is.EqualTo(frozenTime));
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default.WithReducedFlash(true));
            Assert.That(runtimeMaterial.GetFloat("_Intensity"), Is.LessThanOrEqualTo(.18f));
            Assert.That(view.ScreenWeight, Is.LessThanOrEqualTo(.2f));
            PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default.WithReducedFlash(true).WithReducedMotion(true));
            Assert.That(view.ReducedMotion && view.ReducedFlash, Is.True);
            Raise(health, "OnPlayerDied");
            Assert.That(view.ScreenWeight, Is.Zero);
            Assert.That(runtimeMaterial.GetFloat("_Intensity"), Is.Zero);
            Call(view, "OnDisable");
            Assert.That(SubscriberCount(heat, "OnHeatChanged"), Is.Zero);
            Assert.That(SubscriberCount(overheat, "OnOverheatStarted"), Is.Zero);
            Call(view, "OnEnable");
            Assert.That(Get<Material>(view, "_runtimeEdgeMaterial"), Is.SameAs(runtimeMaterial));
            Assert.That(SubscriberCount(heat, "OnHeatChanged"), Is.EqualTo(1));
            Assert.That(runtimeMaterial.GetFloat("_Intensity"), Is.Zero, "Re-enabling fades in without a flash.");
        }
        finally
        {
            if (view != null) { Call(view, "OnDisable"); Call(view, "OnDestroy"); }
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
            PresentationAccessibilityRuntime.Apply(previous);
        }
    }

    [Test]
    public void PreparationWarning_ReducedMotionKeepsInstructionStaticAndVisible()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        var previous = PresentationAccessibilityRuntime.Current;
        GameObject root = null;
        try
        {
            root = new GameObject("Heat warning accessibility test", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 160f);
            var first = new GameObject("Message", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var second = new GameObject("Repeat", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            first.text = second.text = "OVERHEAT EVENT STARTING - GET TO GROUND LEVEL";
            TmpUiHelper.ApplyDefaultFont(first);
            TmpUiHelper.ApplyDefaultFont(second);
            var warning = root.AddComponent<OverheatWarningHud>();
            Set(warning, "_root", root);
            Set(warning, "_text", first);
            Set(warning, "_repeatText", second);
            Set(warning, "_motion", first.rectTransform);
            Set(warning, "_scrollDistance", 500f);
            PresentationAccessibilityRuntime.Apply(previous.WithReducedMotion(true));
            warning.GetType().GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(warning, new object[] { true });
            Call(warning, "ApplyMotion");
            Assert.That(first.rectTransform.anchoredPosition.x, Is.Zero);
            Assert.That(first.rectTransform.rect.width, Is.EqualTo(1920f));
            Assert.That(first.enableAutoSizing, Is.True);
            Assert.That(second.enabled, Is.False);
            first.ForceMeshUpdate(true, true);
            Assert.That(first.isTextOverflowing, Is.False);
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
            PresentationAccessibilityRuntime.Apply(previous);
        }
    }

    [Test]
    public void Edges_LeaveCenterTransparent_AndDisappearAtZero()
    {
        var asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameFeel/Materials/HeatEdges.mat");
        var material = new Material(asset);
        RenderTexture previous = RenderTexture.active;
        try
        {
            foreach (Vector2Int size in new[] { new Vector2Int(160, 90), new Vector2Int(90, 160) })
            {
                var target = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32);
                var pixels = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                try
                {
                    material.SetFloat("_Aspect", (float)size.x / size.y);
                    foreach (float intensity in new[] { 1f, 0f })
                    {
                        material.SetFloat("_Intensity", intensity);
                        RenderTexture.active = target;
                        GL.Clear(true, true, Color.clear);
                        Graphics.Blit(Texture2D.whiteTexture, target, material);
                        pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                        pixels.Apply();
                        float cornerBrightness = 0f;
                        for (int y = 0; y < size.y; y++)
                        for (int x = 0; x < size.x; x++)
                        {
                            Color c = pixels.GetPixel(x, y);
                            float brightness = Mathf.Max(c.r, c.g, c.b);
                            bool center = x > size.x * .15f && x < size.x * .85f && y > size.y * .15f && y < size.y * .85f;
                            if (center || intensity == 0f) Assert.That(brightness, Is.LessThan(.005f));
                            else cornerBrightness = Mathf.Max(cornerBrightness, brightness);
                        }
                        if (intensity > 0f) Assert.That(cornerBrightness, Is.GreaterThan(.04f));
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(pixels);
                }
            }
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(material); }
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static int SubscriberCount(object target, string name) => Get<Delegate>(target, name)?.GetInvocationList().Length ?? 0;
    private static void Raise(object target, string name) => Get<Action>(target, name)?.Invoke();
}
