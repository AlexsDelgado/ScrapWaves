using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Construye la jerarquía del HUD de gameplay para prefab o fallback runtime.
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

    public static void BuildRunEndHierarchy(Transform runEndRoot)
    {
        if (runEndRoot == null)
            return;

        // Si ya existe RunEndRoot con el Panel completo no hay nada que hacer.
        // Si existe pero sin Panel (jerarquía incompleta del prefab), lo eliminamos y lo reconstruimos.
        Transform existingRoot = runEndRoot.Find("RunEndRoot");
        if (existingRoot != null)
        {
            if (existingRoot.Find("Panel") != null)
                return;
            Object.DestroyImmediate(existingRoot.gameObject);
        }

        var rootGo = new GameObject("RunEndRoot", typeof(RectTransform));
        rootGo.transform.SetParent(runEndRoot, false);
        HudUiWire.StretchFull(rootGo.GetComponent<RectTransform>());

        var overlay = HudUiFactory.CreatePanel(rootGo.transform, "Overlay", Vector2.zero);
        var overlayRt = overlay.GetComponent<RectTransform>();
        HudUiWire.StretchFull(overlayRt);
        overlay.color = new Color(0f, 0f, 0f, 0.72f);
        overlay.raycastTarget = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.transform.SetParent(rootGo.transform, false);
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(560f, 420f);
        HudUiFactory.CreatePanel(panelGo.transform, "Background", new Vector2(560f, 420f));

        var layout = panelGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateRunEndLabel(panelGo.transform, "Title", 52f, FontStyles.Bold, 80f);
        CreateRunEndLabel(panelGo.transform, "Stats", 24f, FontStyles.Normal, 160f);
        var btn = HudUiFactory.CreateButton(panelGo.transform, "RetryButton", new Vector2(240f, 52f));
        var btnLabel = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnLabel != null)
            btnLabel.text = "Reintentar";

        rootGo.SetActive(false);
    }

    private static void CreateRunEndLabel(Transform parent, string name, float fontSize, FontStyles style, float height)
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
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }
}
