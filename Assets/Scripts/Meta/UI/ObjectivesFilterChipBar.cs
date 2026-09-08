using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra de chips hide on/off alineada a la estética del menú Objectives
/// (tabs oscuros, acento verde, tipografía clara en mayúsculas).
/// </summary>
public sealed class ObjectivesFilterChipBar
{
    private const float BarHeight = 44f;

    // Matches title-screen Objectives: charcoal panels + sage/lime accents.
    private static readonly Color BarBackground = new(0.08f, 0.1f, 0.09f, 0.96f);
    private static readonly Color IdleChip = new(0.14f, 0.17f, 0.15f, 1f);
    private static readonly Color IdleChipBorder = new(0.32f, 0.38f, 0.33f, 1f);
    private static readonly Color ActiveChip = new(0.28f, 0.36f, 0.27f, 1f);
    private static readonly Color ActiveAccent = new(0.72f, 0.9f, 0.45f, 1f);
    private static readonly Color IdleLabel = new(0.92f, 0.95f, 0.9f, 1f);
    private static readonly Color ActiveLabel = new(0.95f, 1f, 0.88f, 1f);

    private readonly RectTransform _root;

    public RectTransform Root => _root;
    public static float Height => BarHeight;

    private ObjectivesFilterChipBar(RectTransform root)
    {
        _root = root;
    }

    public static ObjectivesFilterChipBar Ensure(Transform parent, string name)
    {
        Transform existing = parent != null ? parent.Find(name) : null;
        if (existing != null && !UsesCurrentStyle(existing))
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(existing.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            existing = null;
        }

        if (existing != null)
        {
            ObjectivesFilterChipBar bar = new(existing.GetComponent<RectTransform>());
            bar.ApplyBarChrome();
            return bar;
        }

        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, BarHeight);
        rt.anchoredPosition = Vector2.zero;

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 6, 6);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        ObjectivesFilterChipBar created = new(rt);
        created.ApplyBarChrome();
        return created;
    }

    private static bool UsesCurrentStyle(Transform bar)
    {
        if (bar.childCount == 0)
            return true;
        return bar.GetChild(0).Find("Face") != null;
    }

    public void AddOrBindChip(string chipName, string label, Func<bool> getActive, Action<bool> setActive)
    {
        string display = (label ?? string.Empty).ToUpperInvariant();
        Transform existing = _root.Find(chipName);
        Button button;
        Image face;
        Image accent;
        TextMeshProUGUI labelTmp;

        if (existing != null)
        {
            button = existing.GetComponent<Button>();
            face = existing.Find("Face")?.GetComponent<Image>() ?? existing.GetComponent<Image>();
            accent = existing.Find("Accent")?.GetComponent<Image>();
            labelTmp = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            EnsureButtonColorBlock(button);
        }
        else
        {
            var chipGo = new GameObject(chipName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            chipGo.transform.SetParent(_root, false);

            RectTransform chipRt = chipGo.GetComponent<RectTransform>();
            chipRt.sizeDelta = new Vector2(168f, 30f);

            LayoutElement le = chipGo.GetComponent<LayoutElement>();
            le.preferredWidth = 168f;
            le.minWidth = 132f;
            le.preferredHeight = 30f;

            Image border = chipGo.GetComponent<Image>();
            border.sprite = HudUiFactory.WhiteSprite;
            border.type = Image.Type.Simple;
            border.color = IdleChipBorder;
            border.raycastTarget = true;

            button = chipGo.GetComponent<Button>();
            button.targetGraphic = border;
            EnsureButtonColorBlock(button);

            var faceGo = new GameObject("Face", typeof(RectTransform), typeof(Image));
            faceGo.transform.SetParent(chipGo.transform, false);
            RectTransform faceRt = faceGo.GetComponent<RectTransform>();
            faceRt.anchorMin = Vector2.zero;
            faceRt.anchorMax = Vector2.one;
            faceRt.offsetMin = new Vector2(1f, 3f);
            faceRt.offsetMax = new Vector2(-1f, -1f);
            face = faceGo.GetComponent<Image>();
            face.sprite = HudUiFactory.WhiteSprite;
            face.raycastTarget = false;

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(chipGo.transform, false);
            RectTransform accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(1f, 0f);
            accentRt.pivot = new Vector2(0.5f, 0f);
            accentRt.sizeDelta = new Vector2(0f, 2f);
            accentRt.anchoredPosition = Vector2.zero;
            accent = accentGo.GetComponent<Image>();
            accent.sprite = HudUiFactory.WhiteSprite;
            accent.raycastTarget = false;

            labelTmp = HudUiFactory.CreateLabel(chipGo.transform, "Label", display, 14f, TextAlignmentOptions.Center);
            labelTmp.fontStyle = FontStyles.Bold;
            RectTransform labelRt = labelTmp.rectTransform;
            labelRt.offsetMin = new Vector2(6f, 4f);
            labelRt.offsetMax = new Vector2(-6f, -2f);
        }

        if (labelTmp != null)
            labelTmp.text = display;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            bool next = !getActive();
            setActive(next);
            ApplyVisual(face, accent, labelTmp, next);
        });

        ApplyVisual(face, accent, labelTmp, getActive());
    }

    /// <summary>Fija el inset superior de un hermano stretch (p. ej. ScrollRect full-bleed).</summary>
    public static void SetTopInset(RectTransform sibling, float topInset)
    {
        if (sibling == null)
            return;

        bool stretched =
            sibling.anchorMin.x <= 0.01f &&
            sibling.anchorMin.y <= 0.01f &&
            sibling.anchorMax.x >= 0.99f &&
            sibling.anchorMax.y >= 0.99f;
        if (!stretched)
            return;

        Vector2 max = sibling.offsetMax;
        sibling.offsetMax = new Vector2(max.x, -Mathf.Abs(topInset));
    }

    private void ApplyBarChrome()
    {
        if (_root == null)
            return;

        Image bg = _root.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = HudUiFactory.WhiteSprite;
            bg.color = BarBackground;
            bg.raycastTarget = false;
        }

        HorizontalLayoutGroup layout = _root.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.childAlignment = TextAnchor.MiddleRight;
    }

    private static void EnsureButtonColorBlock(Button button)
    {
        if (button == null)
            return;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.1f, 1.05f, 1f);
        colors.pressedColor = new Color(0.9f, 0.95f, 0.88f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
    }

    private static void ApplyVisual(Image face, Image accent, TextMeshProUGUI label, bool active)
    {
        if (face != null)
            face.color = active ? ActiveChip : IdleChip;
        if (accent != null)
        {
            accent.color = active ? ActiveAccent : new Color(ActiveAccent.r, ActiveAccent.g, ActiveAccent.b, 0.15f);
            accent.enabled = true;
        }

        if (label != null)
            label.color = active ? ActiveLabel : IdleLabel;
    }
}
