#if UNITY_EDITOR

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Construye la jerarquía del HUD de gameplay durante el authoring en editor.
/// </summary>
public static class GameplayHudHierarchyBuilder
{
    public const string BottomStripName = "BottomStrip";
    public const string ColumnLeftName = "ColumnLeft";
    public const string ColumnCenterName = "ColumnCenter";
    public const string ColumnRightName = "ColumnRight";

    public static Canvas Build(Transform root, Transform playerBarsContent = null)
    {
        if (root == null)
            return null;

        Canvas canvas = root.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            var canvasGo = new GameObject("GameplayHudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            HudUiWire.StretchFull(canvasGo.GetComponent<RectTransform>());
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                canvasGo.layer = uiLayer;

            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        Transform canvasRoot = canvas.transform;
        Transform bottomStrip = canvasRoot.Find(BottomStripName);
        if (bottomStrip != null)
        {
            EnsureBottomStripContents(bottomStrip, playerBarsContent);
            return canvas;
        }

        EnsureLayer(canvasRoot, "PlayerCombatFeedback", typeof(PlayerCombatFeedback));
        EnsureLayer(canvasRoot, "BossHealthBarHud", typeof(BossHealthBarHud));
        EnsureLayer(canvasRoot, "OverheatObjectiveHud", typeof(OverheatObjectiveHud));
        EnsureLayer(canvasRoot, "OffscreenObjectiveIndicators", typeof(OffscreenObjectiveIndicators));
        BuildBottomStrip(canvasRoot, playerBarsContent);
        EnsureLayer(canvasRoot, "PauseMenuUI", typeof(PauseMenuUI));
        EnsureLayer(canvasRoot, "RunEndScreenUI", typeof(RunEndScreenUI));
        BuildRunEndHierarchy(canvasRoot.Find("RunEndScreenUI"));
        return canvas;
    }

    private static void BuildBottomStrip(Transform canvasRoot, Transform playerBarsContent)
    {
        var stripGo = new GameObject(BottomStripName, typeof(RectTransform));
        stripGo.transform.SetParent(canvasRoot, false);
        var stripRt = stripGo.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(1f, 0f);
        stripRt.pivot = new Vector2(0.5f, 0f);
        stripRt.anchoredPosition = Vector2.zero;
        stripRt.sizeDelta = new Vector2(0f, 140f);

        var stripLayout = stripGo.AddComponent<HorizontalLayoutGroup>();
        stripLayout.spacing = 12f;
        stripLayout.padding = new RectOffset(12, 12, 8, 8);
        stripLayout.childAlignment = TextAnchor.MiddleCenter;
        stripLayout.childControlWidth = true;
        stripLayout.childControlHeight = true;
        stripLayout.childForceExpandWidth = true;
        stripLayout.childForceExpandHeight = true;

        Transform left = CreateColumn(stripGo.transform, ColumnLeftName);
        Transform center = CreateColumn(stripGo.transform, ColumnCenterName);
        Transform right = CreateColumn(stripGo.transform, ColumnRightName);

        if (playerBarsContent != null)
        {
            playerBarsContent.SetParent(left, false);
            if (playerBarsContent is RectTransform barsRt)
            {
                barsRt.anchorMin = new Vector2(0f, 0.5f);
                barsRt.anchorMax = new Vector2(1f, 0.5f);
                barsRt.pivot = new Vector2(0.5f, 0.5f);
                barsRt.anchoredPosition = Vector2.zero;
                barsRt.localScale = Vector3.one * 0.65f;
            }
        }

        if (left.GetComponent<PlayerBarsHud>() == null)
            left.gameObject.AddComponent<PlayerBarsHud>();

        if (playerBarsContent == null)
            BuildPlaceholderPlayerBars(left);

        if (center.GetComponent<PassiveLoadoutHud>() == null)
            center.gameObject.AddComponent<PassiveLoadoutHud>();

        if (right.GetComponent<WeaponClusterHud>() == null)
            right.gameObject.AddComponent<WeaponClusterHud>();

        HudBottomStripLayouts.BuildPassivesColumn(center);
        HudBottomStripLayouts.BuildWeaponColumn(right);
    }

    public static void EnsureBottomStripContents(Transform bottomStrip, Transform playerBarsContent = null)
    {
        if (bottomStrip == null)
            return;

        Transform left = bottomStrip.Find(ColumnLeftName);
        Transform center = bottomStrip.Find(ColumnCenterName);
        Transform right = bottomStrip.Find(ColumnRightName);
        if (left == null || center == null || right == null)
            return;

        if (playerBarsContent != null && left.Find("PlayerBarsRoot") == null)
        {
            playerBarsContent.SetParent(left, false);
            if (playerBarsContent is RectTransform barsRt)
            {
                barsRt.anchorMin = new Vector2(0f, 0.5f);
                barsRt.anchorMax = new Vector2(1f, 0.5f);
                barsRt.pivot = new Vector2(0.5f, 0.5f);
                barsRt.anchoredPosition = Vector2.zero;
                barsRt.localScale = Vector3.one * 0.65f;
            }
        }

        if (left.GetComponent<PlayerBarsHud>() == null)
            left.gameObject.AddComponent<PlayerBarsHud>();

        if (center.GetComponent<PassiveLoadoutHud>() == null)
            center.gameObject.AddComponent<PassiveLoadoutHud>();

        if (right.GetComponent<WeaponClusterHud>() == null)
            right.gameObject.AddComponent<WeaponClusterHud>();

        if (center.Find("Passives") == null)
            HudBottomStripLayouts.BuildPassivesColumn(center);

        if (right.Find("WeaponSlots") == null)
            HudBottomStripLayouts.BuildWeaponColumn(right);
    }

    private static Transform CreateColumn(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 120f);
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 120f;
        return go.transform;
    }

    private static void BuildPlaceholderPlayerBars(Transform columnLeft)
    {
        var rootGo = new GameObject("PlayerBarsRoot", typeof(RectTransform));
        rootGo.transform.SetParent(columnLeft, false);
        var rootRt = rootGo.GetComponent<RectTransform>();
        HudUiWire.StretchFull(rootRt);

        CreateFilledBar(rootGo.transform, "HpFill", new Vector2(0f, 28f), new Vector2(200f, 14f), new Color(0.18f, 0.82f, 0.28f, 0.95f));
        CreateFilledBar(rootGo.transform, "XpFill", new Vector2(0f, 8f), new Vector2(200f, 12f), new Color(0.25f, 0.55f, 1f, 0.95f));
        CreateFilledBar(rootGo.transform, "OverheatFill", new Vector2(0f, 48f), new Vector2(40f, 40f), new Color(1f, 0.42f, 0.08f, 0.95f), true);
    }

    private static void CreateFilledBar(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color, bool radial = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.sprite = HudUiFactory.WhiteSprite;
        img.type = Image.Type.Filled;
        img.fillMethod = radial ? Image.FillMethod.Radial360 : Image.FillMethod.Horizontal;
        img.fillOrigin = radial ? (int)Image.Origin360.Bottom : (int)Image.OriginHorizontal.Left;
        img.color = color;
        img.raycastTarget = false;
    }

    private static void EnsureLayer(Transform parent, string name, System.Type componentType)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HudUiWire.StretchFull(go.GetComponent<RectTransform>());
            existing = go.transform;
        }

        if (existing.GetComponent(componentType) == null)
            existing.gameObject.AddComponent(componentType);
    }

    // Same industrial palette as RunMenuPrefabBuilder / LevelUpMenu.
    private static readonly Color DeepSteel = new(0.067f, 0.078f, 0.075f, 1f);
    private static readonly Color SelectedPlate = new(0.22f, 0.25f, 0.22f, 1f);
    private static readonly Color Bone = new(0.949f, 0.961f, 0.922f, 1f);
    private static readonly Color MutedSteel = new(0.678f, 0.741f, 0.69f, 1f);
    private static readonly Color Rust = new(0.851f, 0.416f, 0.196f, 1f);
    private static readonly Color RustLight = new(0.95f, 0.60f, 0.36f, 1f);
    private static readonly Color RunEndBackdrop = new(0.018f, 0.026f, 0.022f, 0.55f);

    public static bool HasStyledRunEndFrame(Transform runEndRoot)
    {
        if (runEndRoot == null)
            return false;
        Transform panel = runEndRoot.Find("RunEndRoot/Panel") ?? runEndRoot.Find("Panel");
        return panel != null && panel.Find("Background/FineBorder") != null;
    }

    public static void BuildRunEndHierarchy(Transform runEndRoot)
    {
        if (runEndRoot == null)
            return;

        // Preserve only a fully styled industrial frame; rebuild placeholder / incomplete roots.
        Transform existingRoot = runEndRoot.Find("RunEndRoot");
        if (existingRoot != null)
        {
            if (HasStyledRunEndFrame(runEndRoot))
                return;
            Object.DestroyImmediate(existingRoot.gameObject);
        }

        var rootGo = new GameObject("RunEndRoot", typeof(RectTransform));
        rootGo.transform.SetParent(runEndRoot, false);
        HudUiWire.StretchFull(rootGo.GetComponent<RectTransform>());

        Image backdrop = CreateSolidImage(rootGo.transform, "Overlay", RunEndBackdrop, raycast: true);
        HudUiWire.StretchFull(backdrop.rectTransform);

        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.transform.SetParent(rootGo.transform, false);
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(720f, 480f);
        Image panelFrame = panelGo.AddComponent<Image>();
        panelFrame.sprite = HudUiFactory.WhiteSprite;
        panelFrame.color = MutedSteel;
        panelFrame.raycastTarget = true;

        Image background = CreateSolidImage(panelGo.transform, "Background", DeepSteel);
        StretchInsets(background.rectTransform, 3f, 3f, 3f, 3f);
        var backgroundLayout = background.gameObject.AddComponent<LayoutElement>();
        backgroundLayout.ignoreLayout = true;

        Image fineBorder = CreateSolidImage(background.transform, "FineBorder",
            new Color(MutedSteel.r, MutedSteel.g, MutedSteel.b, 0.55f));
        StretchInsets(fineBorder.rectTransform, 3f, 3f, 3f, 3f);
        Image fill = CreateSolidImage(fineBorder.transform, "Fill", DeepSteel);
        StretchInsets(fill.rectTransform, 1f, 1f, 1f, 1f);

        var layout = panelGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 40, 40);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateRunEndLabel(panelGo.transform, "Title", 42f, FontStyles.Bold, 64f, Bone);
        CreateRunEndLabel(panelGo.transform, "Stats", 24f, FontStyles.Normal, 140f, MutedSteel);
        CreateRunEndMenuButton(panelGo.transform, "RetryButton", "Retry", primary: true);
        CreateRunEndMenuButton(panelGo.transform, "MainMenuButton", "Main Menu", primary: false);

        rootGo.SetActive(false);
    }

    private static void CreateRunEndLabel(
        Transform parent,
        string name,
        float fontSize,
        FontStyles style,
        float height,
        Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
    }

    private static Button CreateRunEndMenuButton(Transform parent, string name, string label, bool primary)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 56f;
        le.minHeight = 56f;
        le.preferredWidth = 320f;

        Image border = go.AddComponent<Image>();
        border.sprite = HudUiFactory.WhiteSprite;
        border.color = MutedSteel;
        border.raycastTarget = true;

        Image fill = CreateSolidImage(go.transform, "Fill", primary ? Rust : SelectedPlate, raycast: true);
        StretchInsets(fill.rectTransform, 2f, 2f, 2f, 2f);

        Image topEdge = CreateSolidImage(fill.transform, "TopEdge", primary ? RustLight : MutedSteel);
        topEdge.rectTransform.anchorMin = new Vector2(0f, 1f);
        topEdge.rectTransform.anchorMax = new Vector2(1f, 1f);
        topEdge.rectTransform.pivot = new Vector2(0.5f, 1f);
        topEdge.rectTransform.anchoredPosition = Vector2.zero;
        topEdge.rectTransform.sizeDelta = new Vector2(0f, 2f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = fill;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.selectedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        StretchInsets(labelGo.GetComponent<RectTransform>(), 18f, 18f, 6f, 6f);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.text = label;
        tmp.fontSize = 23f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Bone;
        tmp.raycastTarget = false;
        return button;
    }

    private static Image CreateSolidImage(Transform parent, string name, Color color, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = HudUiFactory.WhiteSprite;
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static void StretchInsets(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}

#endif
