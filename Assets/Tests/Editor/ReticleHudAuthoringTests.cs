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

    private sealed class FixedMortarStatus : IMortarReticleStatus
    {
        public RaycastHit Hit;
        public bool TryGetManualTerrainImpact(Vector3 fallbackDirection, out RaycastHit hit)
        {
            hit = Hit;
            return hit.collider != null;
        }
        public float ManualRange => 18f;
        public float ManualExplosionRadius => 1.8f;
        public float ManualFootprintRadius => ManualExplosionRadius;
        public float ShellCollisionRadius => .18f;
        public float ManualTravelTime => .55f;
        public float ArcHeight => 5f;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MortarMarker_OnlyShowsGroundAndRecoversAfterInvalidContacts(bool authored)
    {
        Vector3 anchor = new(4000f, 100f, 4000f);
        var (hud, runtime) = CreateMortarFixture(authored, anchor);
        var status = new FixedMortarStatus();
        Collider floor = Surface("Ground", anchor);
        Collider platform = Surface("Raised platform", anchor + Vector3.up * 4f);
        platform.gameObject.layer = LayerMask.NameToLayer("Terrain");
        Physics.SyncTransforms();
        status.Hit = Contact(floor, Vector3.up);
        UpdateAndAssertVisible(true, "floor");

        string[] rejected = { "damageable child", "enemy layer", "wall", "ceiling", "dynamic prop", "non-ground layer", "steep face" };
        for (int i = 0; i < rejected.Length; i++)
        {
            string kind = rejected[i];
            Collider candidate = Surface(kind, anchor + Vector3.right * (20f + i * 10f));
            Vector3 normal = Vector3.up;
            switch (kind)
            {
                case "damageable child":
                    GameObject actor = Track(new GameObject("Damageable parent"));
                    actor.AddComponent<ReticleTestDamageable>();
                    candidate.transform.SetParent(actor.transform, true);
                    break;
                case "enemy layer": candidate.gameObject.layer = LayerMask.NameToLayer("Enemy"); break;
                case "wall": normal = Vector3.right; break;
                case "ceiling": normal = Vector3.down; break;
                case "dynamic prop": candidate.gameObject.AddComponent<Rigidbody>().useGravity = false; break;
                case "non-ground layer": candidate.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); break;
                case "steep face":
                    candidate.transform.rotation = Quaternion.Euler(0f, 0f, 60f);
                    normal = candidate.transform.up;
                    break;
            }
            Physics.SyncTransforms();
            status.Hit = Contact(candidate, normal);
            UpdateAndAssertVisible(false, kind);
            status.Hit = Contact(platform, Vector3.up);
            UpdateAndAssertVisible(true, "elevated platform after " + kind);
        }

        void UpdateAndAssertVisible(bool expected, string context)
        {
            Invoke(hud, "UpdateMortarMarker", runtime, status);
            MortarLandingIndicatorVfx marker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
            if (authored)
            {
                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.gameObject.activeSelf, Is.EqualTo(expected), context);
                if (expected)
                {
                    Assert.That(marker.IsGroundConforming, Is.True, context);
                    Assert.That(Vector3.Distance(marker.ContactPoint, status.Hit.point), Is.LessThan(.001f), context);
                }
            }
            else Assert.That(marker, Is.Null);
            Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf,
                Is.EqualTo(expected && !authored), context + " legacy view");
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MortarMarker_ProjectsEnemyAndPropsOntoSupportingFloorWithoutLosingFootprint(bool authored)
    {
        Vector3 anchor = new(4100f, 100f, 4100f);
        var (hud, runtime) = CreateMortarFixture(authored, anchor);
        var status = new FixedMortarStatus();
        Collider floor = Surface("Supporting floor", anchor);
        Collider platform = Surface("Elevated supporting platform", anchor + Vector3.up * 2f);
        platform.gameObject.layer = LayerMask.NameToLayer("Terrain");
        platform.gameObject.SetActive(false);
        foreach (string kind in new[] { "enemy", "dynamic prop", "crafting station" })
        {
            Collider model = Surface(kind, anchor);
            model.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
            switch (kind)
            {
                case "enemy":
                    model.gameObject.layer = LayerMask.NameToLayer("Enemy");
                    model.gameObject.AddComponent<ReticleTestDamageable>();
                    break;
                case "dynamic prop": model.gameObject.AddComponent<Rigidbody>().useGravity = false; break;
                default: model.gameObject.AddComponent<CraftingStation>(); break;
            }
            foreach (Collider support in new[] { floor, platform })
            {
                platform.gameObject.SetActive(support == platform);
                model.gameObject.SetActive(false);
                Physics.SyncTransforms();
                model.transform.position = new Vector3(anchor.x, support.bounds.max.y + .85f, anchor.z);
                Physics.SyncTransforms();
                status.Hit = Contact(support, Vector3.up);
                Invoke(hud, "UpdateMortarMarker", runtime, status);
                int unobstructedVertexCount = MarkerVertices(hud, authored).Length;
                Assert.That(unobstructedVertexCount, Is.GreaterThan(12));

                model.gameObject.SetActive(true);
                Physics.SyncTransforms();
                status.Hit = Contact(model, Vector3.up);
                Assert.That(status.Hit.point.y, Is.GreaterThan(support.bounds.max.y + 1f));
                Invoke(hud, "UpdateMortarMarker", runtime, status);
                MortarLandingIndicatorVfx marker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
                string context = kind + " above " + support.name;
                float offset = Read<float>(hud, "_mortarSurfaceOffset");
                if (authored)
                {
                    Assert.That(marker, Is.Not.Null);
                    Assert.That(marker.gameObject.activeSelf, Is.True, context);
                    Assert.That(marker.IsGroundConforming, Is.True, context);
                    Assert.That(marker.ContactPoint.y, Is.EqualTo(support.bounds.max.y).Within(.003f), context);
                    offset = marker.SurfaceOffset;
                }
                Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf, Is.EqualTo(!authored), context);
                Vector3[] vertices = MarkerVertices(hud, authored);
                Assert.That(vertices.Length, Is.EqualTo(unobstructedVertexCount),
                    context + ": the model must not remove any floor-supported marker triangles or ring segments.");
                Assert.That(vertices.Min(vertex => vertex.y), Is.EqualTo(support.bounds.max.y + offset).Within(.003f), context);
                Assert.That(vertices.Max(vertex => vertex.y), Is.EqualTo(support.bounds.max.y + offset).Within(.003f), context);
                Assert.That(vertices.Max(vertex => Vector3.ProjectOnPlane(vertex - anchor, Vector3.up).magnitude),
                    Is.EqualTo(status.ManualFootprintRadius).Within(.025f), context + ": retain the full footprint around the object.");
            }
            floor.gameObject.SetActive(false);
            platform.gameObject.SetActive(false);
            Physics.SyncTransforms();
            Invoke(hud, "UpdateMortarMarker", runtime, status);
            MortarLandingIndicatorVfx hiddenMarker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
            if (authored) Assert.That(hiddenMarker.gameObject.activeSelf, Is.False, kind + " without ground");
            Assert.That(Read<GameObject>(hud, "_mortarMarkerRoot").activeSelf, Is.False, kind + " without ground");
            model.gameObject.SetActive(false);
            floor.gameObject.SetActive(true);
        }
    }

    private (ReticleHud Hud, WeaponInstance Runtime) CreateMortarFixture(bool authored, Vector3 anchor)
    {
        var (hud, _) = CreateAuthored();
        WeaponManager manager = hud.gameObject.AddComponent<WeaponManager>();
        typeof(ReticleHud).GetField("_weaponManager", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, manager);
        typeof(WeaponManager).GetProperty(nameof(WeaponManager.CurrentAimSolution))
            .SetValue(manager, new AimSolution(anchor + Vector3.up * 2f, anchor + Vector3.forward * 18f, Time.frameCount));
        // A profile without a mortar prefab exercises the existing legacy view.
        string weaponPath = authored ? "Mortar" : "AutomaticCannon";
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/" + weaponPath + ".asset");
        Assert.That(data, Is.Not.Null);
        return (hud, new WeaponInstance { Data = data });
    }

    private static Vector3[] MarkerVertices(ReticleHud hud, bool authored)
    {
        if (authored)
            return Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker").GetComponentsInChildren<MeshFilter>()
                .Where(filter => filter.GetComponent<Renderer>().enabled)
                .SelectMany(filter =>
                {
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    return filter.sharedMesh.triangles.Select(index => filter.transform.TransformPoint(vertices[index]));
                }).ToArray();
        return new[] { Read<LineRenderer>(hud, "_mortarLandingRing"), Read<LineRenderer>(hud, "_mortarBlastRing") }
            .SelectMany(line => Enumerable.Range(0, line.positionCount).Select(line.GetPosition)).ToArray();
    }

    private Collider Surface(string name, Vector3 position)
    {
        GameObject surface = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        surface.name = name;
        surface.transform.position = position;
        surface.transform.localScale = new Vector3(6f, .5f, 6f);
        return surface.GetComponent<Collider>();
    }

    private static RaycastHit Contact(Collider surface, Vector3 normal)
    {
        Assert.That(surface.Raycast(new Ray(surface.bounds.center + normal * 10f, -normal),
            out RaycastHit hit, 20f), Is.True, surface.name);
        return hit;
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
