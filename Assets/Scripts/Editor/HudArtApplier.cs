#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rearma el BottomStrip de <c>GameplayHud V2</c> con el arte del HUD hecho en Blender
/// (ArtSource/UI, ver blender_build_hud.py y finish_hud.py). Las ventanas de cada panel salen de
/// ArtSource/UI/HudLayout.json, así los fills y slots quedan alineados al píxel con el arte.
/// Respeta los nombres que buscan PlayerBarsHud, PassiveLoadoutHud y WeaponClusterHud.
/// Cada variante de arte vive en su carpeta (ArtSource/UI/&lt;variante&gt; y Assets/Art/UI/HUD/&lt;variante&gt;),
/// así se puede alternar entre versiones sin perder ninguna.
/// </summary>
public static class HudArtApplier
{
    private const string PrefabPath = "Assets/Prefabs/UI/GameplayHud V2.prefab";
    private const string BaseArtFolder = "Assets/Art/UI/HUD";
    private const string BaseLayoutFolder = "ArtSource/UI";

    private static string ArtFolder = BaseArtFolder;
    private static string LayoutPath = BaseLayoutFolder + "/HudLayout.json";

    /// <summary>Canvas units por píxel de render (el render sale al doble de la resolución de uso).</summary>
    private const float Scale = 0.45f;
    private const float ScreenMargin = 18f;

    private static readonly Color HpColor = new(0.18f, 0.82f, 0.28f, 1f);
    private static readonly Color XpColor = new(0.25f, 0.55f, 1f, 1f);
    private static readonly Color HeatColor = new(1f, 0.42f, 0.08f, 1f);
    private static readonly Color AmmoColor = new(0.95f, 0.78f, 0.22f, 1f);
    private static readonly Color AbilityColor = new(0.3f, 0.75f, 1f, 0.85f);
    private static readonly Color Mustard = new(0.82f, 0.74f, 0.18f, 1f);

    [System.Serializable] private class RectEntry { public string name; public int[] rect; }
    [System.Serializable] private class Piece { public string name; public int[] size; public RectEntry[] rects; }
    [System.Serializable] private class Layout { public Piece[] pieces; }

    [MenuItem("ScrapWaves/UI/Apply HUD Art To GameplayHud V2")]
    public static void Apply() => Apply(null);

    [MenuItem("ScrapWaves/UI/Apply HUD Art V2A (Player Bars)")]
    public static void ApplyV2A() => Apply("V2A");

    [MenuItem("ScrapWaves/UI/Apply HUD Art V2B (Estilo armas)")]
    public static void ApplyV2B() => Apply("V2B");

    /// <summary>Aplica una variante de arte; <c>null</c> es la versión base.</summary>
    public static void Apply(string variant)
    {
        bool hasVariant = !string.IsNullOrEmpty(variant);
        ArtFolder = hasVariant ? $"{BaseArtFolder}/{variant}" : BaseArtFolder;
        LayoutPath = hasVariant ? $"{BaseLayoutFolder}/{variant}/HudLayout.json" : $"{BaseLayoutFolder}/HudLayout.json";
        ConfigureImporters();
        Layout layout = LoadLayout();

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform canvas = root.transform.Find("GameplayHudCanvas");
            Transform strip = canvas.Find(GameplayHudHierarchyBuilder.BottomStripName);
            Piece left = Get(layout, "HudLeft"), center = Get(layout, "HudCenter"), right = Get(layout, "HudRight");
            Piece badge = Get(layout, "HudBadge");

            PrepareStrip(strip, Mathf.Max(left.size[1], center.size[1], right.size[1]) * Scale + ScreenMargin);
            Transform strayDashes = canvas.Find("DashCharges");
            if (strayDashes != null)
                Object.DestroyImmediate(strayDashes.gameObject);
            BuildLeft(strip.Find(GameplayHudHierarchyBuilder.ColumnLeftName), left);
            BuildCenter(strip.Find(GameplayHudHierarchyBuilder.ColumnCenterName), center, badge);
            BuildRight(strip.Find(GameplayHudHierarchyBuilder.ColumnRightName), right);
            BuildMaterialStrip(canvas, center.size[1] * Scale + ScreenMargin);
            MoveExitObjectiveTopRight(canvas);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[{nameof(HudArtApplier)}] Arte del HUD ({(hasVariant ? variant : "base")}) aplicado en {PrefabPath}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureImporters()
    {
        AssetDatabase.Refresh();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder, MaterialIconFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti)
                continue;
            bool dirty = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single
                         || ti.mipmapEnabled || !ti.alphaIsTransparency;
            if (!dirty)
                continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
    }

    private static Layout LoadLayout()
    {
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), LayoutPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Falta {LayoutPath}. Corré blender_build_hud.py.", path);
        return JsonUtility.FromJson<Layout>(File.ReadAllText(path));
    }

    private static Piece Get(Layout layout, string name)
    {
        foreach (Piece p in layout.pieces)
            if (p.name == name)
                return p;
        throw new System.InvalidOperationException($"{LayoutPath} no tiene la pieza {name}.");
    }

    private static int[] RectOf(Piece piece, string name)
    {
        foreach (RectEntry r in piece.rects)
            if (r.name == name)
                return r.rect;
        throw new System.InvalidOperationException($"La pieza {piece.name} no tiene la ventana {name}.");
    }

    private static Sprite Art(string name)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{name}.png");
        if (sprite == null)
            throw new FileNotFoundException($"Falta el sprite {ArtFolder}/{name}.png. Corré finish_hud.py.");
        return sprite;
    }

    private static void PrepareStrip(Transform strip, float height)
    {
        if (strip.TryGetComponent(out HorizontalLayoutGroup group))
            Object.DestroyImmediate(group);
        var rt = (RectTransform)strip;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
    }

    private static RectTransform PrepareColumn(Transform column, Piece piece, Vector2 anchor, Vector2 position)
    {
        if (column.TryGetComponent(out LayoutElement element))
            Object.DestroyImmediate(element);
        for (int i = column.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(column.GetChild(i).gameObject);

        var rt = (RectTransform)column;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(piece.size[0], piece.size[1]) * Scale;

        Image panel = AddImage(NewChild(column, "Panel"), Art(piece.name));
        HudUiWire.StretchFull(panel.rectTransform);
        return rt;
    }

    private static RectTransform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rt = NewChild(parent, name);
        HudUiWire.StretchFull(rt);
        return rt;
    }

    /// <summary>Ubica un hijo sobre una ventana del layout (px desde arriba-izquierda del panel).</summary>
    private static RectTransform Place(Transform parent, string name, int[] r, float inset = 0f)
    {
        RectTransform rt = NewChild(parent, name);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(r[0] * Scale + inset, -(r[1] * Scale + inset));
        rt.sizeDelta = new Vector2(r[2] * Scale - 2f * inset, r[3] * Scale - 2f * inset);
        return rt;
    }

    private static Image AddImage(RectTransform rt, Sprite sprite, Color? color = null, bool preserveAspect = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color ?? Color.white;
        img.preserveAspect = preserveAspect;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI AddText(RectTransform rt, float size, TextAlignmentOptions align, Color color,
        FontStyles style = FontStyles.Normal, string text = "", TextOverflowModes overflow = TextOverflowModes.Truncate)
    {
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.text = text;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        // LiberationSans SDF no trae el glifo de elipsis: Ellipsis spamea warnings en Play.
        // Ojo: Truncate también corta en vertical, así que en ventanas bajas (el badge) hay que usar Overflow.
        tmp.overflowMode = overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Image AddFill(RectTransform rt, Sprite sprite, Color color, Image.FillMethod method)
    {
        Image img = AddImage(rt, sprite, color);
        img.type = Image.Type.Filled;
        img.fillMethod = method;
        img.fillOrigin = method switch
        {
            Image.FillMethod.Horizontal => (int)Image.OriginHorizontal.Left,
            Image.FillMethod.Vertical => (int)Image.OriginVertical.Bottom,
            _ => (int)Image.Origin360.Top
        };
        img.fillClockwise = true;
        img.fillAmount = 1f;
        return img;
    }

    private static void BuildLeft(Transform column, Piece piece)
    {
        PrepareColumn(column, piece, Vector2.zero, new Vector2(ScreenMargin, ScreenMargin));
        Image hp = AddFill(Place(column, "HpFill", RectOf(piece, "HpFill"), 1f), Art("HudBarFill"), HpColor, Image.FillMethod.Horizontal);
        Image xp = AddFill(Place(column, "XpFill", RectOf(piece, "XpFill"), 1f), Art("HudBarFill"), XpColor, Image.FillMethod.Horizontal);
        Image heat = AddFill(Place(column, "OverheatFill", RectOf(piece, "OverheatFill"), 1f), Art("HudCircleMask"), HeatColor, Image.FillMethod.Vertical);

        var so = new SerializedObject(column.GetComponent<PlayerBarsHud>());
        so.FindProperty("_hpFill").objectReferenceValue = hp;
        so.FindProperty("_xpFill").objectReferenceValue = xp;
        so.FindProperty("_overheatFill").objectReferenceValue = heat;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildCenter(Transform column, Piece piece, Piece badge)
    {
        PrepareColumn(column, piece, new Vector2(0.5f, 0f), new Vector2(0f, ScreenMargin));
        RectTransform passives = Stretch(column, "Passives");
        Sprite badgeSprite = Art("HudBadge");
        Vector2 badgeSize = new Vector2(badge.size[0], badge.size[1]) * Scale;
        int[] levelRect = RectOf(badge, "Level");

        Sprite[] empties =
        {
            Art("HudSlotEmpty_Head"), Art("HudSlotEmpty_Core"),
            Art("HudSlotEmpty_Arm"), Art("HudSlotEmpty_Arm"),
            Art("HudSlotEmpty_Leg"), Art("HudSlotEmpty_Leg")
        };
        for (int i = 0; i < 6; i++)
        {
            RectTransform slot = Place(passives, $"PassiveSlot_{i}", RectOf(piece, $"PassiveSlot_{i}"));
            AddImage(Stretch(slot, "Icon"), empties[i], preserveAspect: true);

            RectTransform plate = NewChild(slot, "Badge");
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 0f);
            plate.anchoredPosition = new Vector2(badgeSize.x * 0.28f, -badgeSize.y * 0.3f);
            plate.sizeDelta = badgeSize;
            // A escala 1 la chapita queda en ~30x21 y el número no se lee; se agranda entera con su texto.
            plate.localScale = Vector3.one * BadgeScale;
            AddImage(plate, badgeSprite);
            AddText(Place(plate, "Level", levelRect), 13f, TextAlignmentOptions.Center, Mustard, FontStyles.Bold,
                overflow: TextOverflowModes.Overflow);
            plate.gameObject.SetActive(false);
        }

        var so = new SerializedObject(column.GetComponent<PassiveLoadoutHud>());
        so.FindProperty("_emptyHead").objectReferenceValue = Art("HudSlotEmpty_Head");
        so.FindProperty("_emptyCore").objectReferenceValue = Art("HudSlotEmpty_Core");
        so.FindProperty("_emptyArm").objectReferenceValue = Art("HudSlotEmpty_Arm");
        so.FindProperty("_emptyLeg").objectReferenceValue = Art("HudSlotEmpty_Leg");
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildRight(Transform column, Piece piece)
    {
        PrepareColumn(column, piece, new Vector2(1f, 0f), new Vector2(-ScreenMargin, ScreenMargin));

        RectTransform slots = Stretch(column, "WeaponSlots");
        for (int i = 0; i < WeaponManager.MaxWeaponSlots; i++)
        {
            RectTransform slot = Place(slots, $"WeaponSlot_{i}", RectOf(piece, $"WeaponSlot_{i}"));
            AddImage(Stretch(slot, "Icon"), HudUiFactory.WhiteSprite, preserveAspect: true);
        }

        RectTransform panel = Stretch(column, "WeaponPanel");
        int[] nameRect = RectOf(piece, "WeaponName");
        RectTransform nameRt = Place(panel, "WeaponName", nameRect);
        nameRt.offsetMin += new Vector2(8f, 0f);
        AddText(nameRt, 15f, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold, "Sin arma");
        RectTransform levelRt = Place(panel, "WeaponLevel", nameRect);
        levelRt.offsetMax -= new Vector2(8f, 0f);
        AddText(levelRt, 13f, TextAlignmentOptions.MidlineRight, Mustard, FontStyles.Bold);

        RectTransform ammoBar = Place(panel, "AmmoBar", RectOf(piece, "AmmoFill"), 1f);
        RectTransform ammoTrack = Stretch(ammoBar, "AmmoFill");
        AddFill(Stretch(ammoTrack, "Fill"), Art("HudBarFill"), AmmoColor, Image.FillMethod.Horizontal);
        AddText(Place(panel, "AmmoLabel", RectOf(piece, "AmmoLabel")), 12f, TextAlignmentOptions.Center,
            HudUiFactory.MutedTextColor, FontStyles.Bold, "0/0");

        RectTransform ability = Place(panel, "AbilityCooldown", RectOf(piece, "AbilityCooldown"), 1f);
        AddFill(Stretch(Stretch(ability, "AbilityCooldownFill"), "Fill"), Art("HudCircleMask"), AbilityColor, Image.FillMethod.Radial360);
        AddText(Stretch(ability, "QLabel"), 24f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, "Q");

        // Overlay del cooldown de rotación sobre el siguiente arma: con fillAmount 0 se ve el ícono.
        RectTransform rotation = Place(panel, "RotationCooldown", RectOf(piece, "WeaponSlot_1"), 1f);
        Image rotFill = AddFill(Stretch(Stretch(rotation, "RotationCooldownFill"), "Fill"), Art("HudCircleMask"),
            new Color(0.08f, 0.08f, 0.1f, 0.62f), Image.FillMethod.Radial360);
        rotFill.fillAmount = 0f;
        AddText(Stretch(rotation, "RotLabel"), 11f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);

        Transform dashLayout = BuildDashRow(column);
        var so = new SerializedObject(column.GetComponent<WeaponClusterHud>());
        so.FindProperty("_dashChargesLayout").objectReferenceValue = dashLayout;
        so.FindProperty("_hideEmptyWeaponSlots").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private const float BadgeScale = 1.4f;
    private const string MaterialIconFolder = "Assets/Art/UI/Icons/Materials";
    private const string MaterialStripName = "MaterialStrip";

    /// <summary>
    /// Materiales como fila de íconos + cantidad, apoyada sobre la placa central de pasivos. Reemplaza al panel
    /// de materiales de arriba a la izquierda (MaterialInventoryHUD del player).
    /// </summary>
    private static void BuildMaterialStrip(Transform canvas, float bottom)
    {
        Transform old = canvas.Find(MaterialStripName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        RectTransform rt = NewChild(canvas, MaterialStripName);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottom + 6f);
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MaterialInventoryDisplayView view = MaterialInventoryDisplayView.Create(rt, MaterialDisplayLayout.Horizontal,
            showEmpty: true, showNames: false, iconSize: 40f, fontSize: 22f, spacing: 22f,
            iconLookup: t => AssetDatabase.LoadAssetAtPath<Sprite>($"{MaterialIconFolder}/Material_{t}.png"));
        rt.gameObject.AddComponent<MaterialInventoryHUD>().UseDisplay(view);
    }

    /// <summary>"Batteries: x/y" arriba a la derecha, para dejarle el centro de arriba al diálogo.</summary>
    private static void MoveExitObjectiveTopRight(Transform canvas)
    {
        Transform exit = HudUiWire.FindDeepChild(canvas, "LevelExitHud");
        if (exit == null)
            return;
        var rt = (RectTransform)exit;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-28f, -24f);
        rt.sizeDelta = new Vector2(360f, 44f);
        TextMeshProUGUI text = HudUiWire.FindTmp(exit, "Text");
        if (text != null)
            text.alignment = TextAlignmentOptions.MidlineRight;
    }

    private const int DashPipCount = 3;
    private const float DashPipSize = 20f;
    private const float DashPipSpacing = 6f;

    /// <summary>
    /// Cargas de dash en una fila compacta apoyada sobre el borde superior del panel derecho,
    /// alineada a la derecha. La retícula queda sola en el centro de la pantalla.
    /// </summary>
    private static Transform BuildDashRow(Transform column)
    {
        RectTransform dash = NewChild(column, "DashCharges");
        dash.anchorMin = dash.anchorMax = dash.pivot = new Vector2(1f, 1f);
        dash.anchoredPosition = new Vector2(-14f, DashPipSize + 4f);
        dash.sizeDelta = new Vector2(DashPipCount * DashPipSize + (DashPipCount - 1) * DashPipSpacing, DashPipSize);

        RectTransform layoutRt = Stretch(dash, "Layout");
        var row = layoutRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = DashPipSpacing;
        row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.reverseArrangement = true;

        Sprite pip = Art("HudDashPip");
        for (int i = 0; i < DashPipCount; i++)
        {
            RectTransform charge = NewChild(layoutRt, $"Charge_{i}");
            // Mismas anclas que impone el HorizontalLayoutGroup, así la instancia de escena no guarda overrides.
            charge.anchorMin = charge.anchorMax = new Vector2(0f, 1f);
            charge.sizeDelta = new Vector2(DashPipSize, DashPipSize);
            AddImage(charge, pip, new Color(0.3f, 0.85f, 1f, 1f));
        }

        return layoutRt;
    }
}
#endif
