using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ReticleHudAuthoringTests
{
    private readonly List<GameObject> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void AuthorUi_CreatesEditableViewsOutsidePlayerAndReusesThem()
    {
        var (hud, ui) = CreateAuthored();
        Assert.That(hud.HasAuthoredUi, Is.True);
        GameObject canvas = Read<GameObject>(hud, "_canvasRoot");
        Assert.That(canvas.transform.parent, Is.EqualTo(ui.transform));
        Assert.That(hud.GetComponentsInChildren<Canvas>(true), Is.Empty);
        Assert.That(canvas.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        foreach (string field in new[] { "_wideBracketRoot", "_circleDotRoot", "_mortarVRoot", "_rocketFrame" })
        {
            RectTransform shape = Read<RectTransform>(hud, field);
            Assert.That(shape.IsChildOf(canvas.transform), Is.True);
            Assert.That(shape.anchorMin, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(shape.anchorMax, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(shape.anchoredPosition, Is.EqualTo(Vector2.zero));
        }
        int[] hierarchy = Hierarchy(ui);
        Image circle = ui.GetComponentsInChildren<Image>(true).Single(image => image.name == "Circle");
        circle.color = Color.cyan;
        hud.AuthorUi(ui.transform);
        Assert.That(Hierarchy(ui), Is.EqualTo(hierarchy));
        Assert.That(circle.color, Is.EqualTo(Color.cyan), "Reauthoring must preserve hand-edited image colors.");
    }

    [Test]
    public void AuthoredViews_ReferenceDurableSpriteAndMaterials()
    {
        var (hud, ui) = CreateAuthored();
        Image circle = ui.GetComponentsInChildren<Image>(true).Single(image => image.name == "Circle");
        Assert.That(circle.sprite, Is.Not.Null);
        Assert.That(AssetDatabase.Contains(circle.sprite), Is.True);
        Assert.That(AssetDatabase.Contains(circle.sprite.texture), Is.True);
        Assert.That(AssetDatabase.Contains(Read<LineRenderer>(hud, "_mortarLandingRing").sharedMaterial), Is.True);
        Assert.That(AssetDatabase.Contains(Read<LineRenderer>(hud, "_mortarBlastRing").sharedMaterial), Is.True);
        Transform dot = Read<Transform>(hud, "_mortarCenterDot");
        Assert.That(AssetDatabase.Contains(dot.GetComponent<Renderer>().sharedMaterial), Is.True);
        Assert.That(dot.GetComponent<Collider>(), Is.Null, "The prediction marker must not affect aim or terrain collisions.");
    }

    [Test]
    public void SharedWhiteSprite_IsPersistentAndRuntimeLoadsItForFilledImages()
    {
        Sprite authored = HudUiFactory.EnsureWhiteSpriteAsset();
        Assert.That(AssetDatabase.Contains(authored), Is.True);
        Assert.That(AssetDatabase.Contains(authored.texture), Is.True);
        typeof(HudUiFactory).GetField("s_whiteSprite", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        Assert.That(Resources.Load<Sprite>(HudUiFactory.WhiteSpriteResourcePath), Is.EqualTo(authored));
        Assert.That(HudUiFactory.WhiteSprite, Is.EqualTo(authored));
        GameObject imageObject = Track(new GameObject("Filled image", typeof(RectTransform), typeof(Image)));
        Image fill = imageObject.GetComponent<Image>();
        HudUiFactory.EnsureHorizontalFill(fill);
        Assert.That(fill.sprite, Is.EqualTo(authored));
        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
    }

    [Test]
    public void AwakeAndModeChanges_OnlyBindAuthoredHierarchy()
    {
        var (hud, ui) = CreateAuthored();
        int[] hierarchy = Hierarchy(ui);
        Invoke(hud, "Awake");
        foreach (ReticleMode mode in new[] { ReticleMode.CircleDot, ReticleMode.WideBrackets, ReticleMode.Mortar, ReticleMode.RocketLock, ReticleMode.Hidden })
            Invoke(hud, "ApplyMode", mode);
        hud.SetVisible(false);
        hud.SetVisible(true);
        Assert.That(Hierarchy(ui), Is.EqualTo(hierarchy));
        Assert.That(hud.transform.childCount, Is.Zero);
    }

    [Test]
    public void Awake_WithoutAuthoredViewsDoesNotConstructUi()
    {
        GameObject owner = Track(new GameObject("Unconfigured reticle owner"));
        owner.SetActive(false);
        ReticleHud hud = owner.AddComponent<ReticleHud>();
        Invoke(hud, "Awake");
        hud.SetVisible(true);
        Assert.That(hud.HasAuthoredUi, Is.False);
        Assert.That(owner.transform.childCount, Is.Zero);
        Assert.That(Read<GameObject>(hud, "_canvasRoot"), Is.Null);
        Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot"), Is.Null);
        Assert.That(Read<bool>(hud, "_isVisible"), Is.False);
    }

    [Test]
    public void TintBinding_RestoresAuthoredColorsWithoutTintingShadows()
    {
        var (hud, ui) = CreateAuthored();
        Image circle = ui.GetComponentsInChildren<Image>(true).Single(image => image.name == "Circle");
        Image shadow = ui.GetComponentsInChildren<Image>(true).Single(image => image.name == "CircleShadow");
        Color authoredColor = new(0.2f, 0.8f, 0.6f, 0.9f);
        Color shadowColor = shadow.color;
        circle.color = authoredColor;
        Invoke(hud, "Awake");
        Invoke(hud, "ApplyWeakPointFlashColor");
        Assert.That(circle.color, Is.EqualTo(Color.red));
        Assert.That(shadow.color, Is.EqualTo(shadowColor));
        Invoke(hud, "RestoreReticleTintColors");
        Assert.That(circle.color, Is.EqualTo(authoredColor));
    }

    [Test]
    public void MortarProfileSwitch_UsesAuthoredPredictionMarkerWithoutSpawning()
    {
        var (hud, ui) = CreateAuthored();
        WeaponData mortar = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");
        Assert.That(mortar?.PresentationProfile?.Mortar?.LandingIndicatorPrefab, Is.Not.Null);
        int[] hierarchy = Hierarchy(ui);
        Invoke(hud, "EnsureAuthoredMortarMarker", new WeaponInstance { Data = mortar });
        MortarLandingIndicatorVfx marker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
        Assert.That(marker, Is.Not.Null);
        Assert.That(marker.transform.IsChildOf(ui.transform), Is.True);
        Invoke(hud, "SetMortarMarkerVisible", true);
        Assert.That(marker.gameObject.activeSelf, Is.True);
        Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf, Is.False);
        Invoke(hud, "EnsureAuthoredMortarMarker", new WeaponInstance { Data = cannon });
        Invoke(hud, "SetMortarMarkerVisible", true);
        Assert.That(marker.gameObject.activeSelf, Is.False);
        Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf, Is.True);
        Assert.That(Hierarchy(ui), Is.EqualTo(hierarchy));
    }

    [Test]
    public void ControllerDisposal_HidesButDoesNotDestroySceneOwnedViews()
    {
        var (hud, ui) = CreateAuthored();
        int[] hierarchy = Hierarchy(ui);
        Invoke(hud, "Awake");
        Invoke(hud, "SetMortarMarkerVisible", true);
        Invoke(hud, "OnDestroy");
        Assert.That(Hierarchy(ui), Is.EqualTo(hierarchy));
        Assert.That(Read<GameObject>(hud, "_canvasRoot").activeSelf, Is.False);
        Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf, Is.False);
    }

    private (ReticleHud Hud, GameObject Ui) CreateAuthored()
    {
        GameObject ui = Track(new GameObject("Authored UI test root"));
        GameObject owner = Track(new GameObject("Reticle test owner"));
        owner.SetActive(false);
        ReticleHud hud = owner.AddComponent<ReticleHud>();
        hud.AuthorUi(ui.transform);
        return (hud, ui);
    }

    private GameObject Track(GameObject value) { _cleanup.Add(value); return value; }
    private static int[] Hierarchy(GameObject root) => root.GetComponentsInChildren<Transform>(true)
        .Select(transform => transform.GetInstanceID()).OrderBy(id => id).ToArray();
    private static T Read<T>(object target, string field) => (T)target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Invoke(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
