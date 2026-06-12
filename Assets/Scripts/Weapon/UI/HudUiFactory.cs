using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum HudPlaceholderKind
{
    None,
    Head,
    Torso,
    Arm,
    Leg,
    Weapon
}

public static class HudUiFactory
{
    public static readonly Color PanelColor = new(0f, 0f, 0f, 0.45f);
    public static readonly Color BorderColor = new(0.25f, 0.28f, 0.35f, 1f);
    public static readonly Color EmptySlotColor = new(0.18f, 0.2f, 0.24f, 0.65f);
    public static readonly Color MutedTextColor = new(0.78f, 0.82f, 0.88f, 1f);

    private static Sprite s_whiteSprite;

    public static Sprite WhiteSprite
    {
        get
        {
            if (s_whiteSprite != null)
                return s_whiteSprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return s_whiteSprite;
        }
    }

    public static Image CreatePanel(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.type = Image.Type.Simple;
        img.color = PanelColor;
        img.raycastTarget = false;
        return img;
    }

    public static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.text = text;
        tmp.raycastTarget = false;
        return tmp;
    }

    public static Image CreateIconSlot(Transform parent, string name, float size, Sprite sprite, HudPlaceholderKind placeholder)
    {
        var frameGo = new GameObject(name, typeof(RectTransform));
        frameGo.transform.SetParent(parent, false);
        var frameRt = frameGo.GetComponent<RectTransform>();
        frameRt.sizeDelta = new Vector2(size, size);

        var frame = frameGo.AddComponent<Image>();
        frame.sprite = WhiteSprite;
        frame.color = BorderColor;
        frame.raycastTarget = false;

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(frameGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(3f, 3f);
        iconRt.offsetMax = new Vector2(-3f, -3f);

        var icon = iconGo.AddComponent<Image>();
        icon.sprite = sprite != null ? sprite : WhiteSprite;
        icon.color = sprite != null ? Color.white : GetPlaceholderColor(placeholder);
        icon.raycastTarget = false;
        return icon;
    }

    public static Image CreateRadialFill(Transform parent, string name, float size, Color fillColor)
    {
        var rootGo = new GameObject(name, typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        var rootRt = rootGo.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(size, size);

        var trackGo = new GameObject("Track", typeof(RectTransform));
        trackGo.transform.SetParent(rootGo.transform, false);
        var trackRt = trackGo.GetComponent<RectTransform>();
        trackRt.anchorMin = Vector2.zero;
        trackRt.anchorMax = Vector2.one;
        trackRt.offsetMin = Vector2.zero;
        trackRt.offsetMax = Vector2.zero;
        var track = trackGo.AddComponent<Image>();
        track.sprite = WhiteSprite;
        track.color = EmptySlotColor;
        track.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(rootGo.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        var fill = fillGo.AddComponent<Image>();
        fill.sprite = WhiteSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 1f;
        fill.color = fillColor;
        fill.raycastTarget = false;
        return fill;
    }

    public static Slider CreateSlider(Transform parent, string name, Vector2 size, float min, float max, float value)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bg = bgGo.AddComponent<Image>();
        bg.sprite = WhiteSprite;
        bg.color = EmptySlotColor;

        var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGo.transform.SetParent(go.transform, false);
        var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(6f, 6f);
        fillAreaRt.offsetMax = new Vector2(-6f, -6f);

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fill = fillGo.AddComponent<Image>();
        fill.sprite = WhiteSprite;
        fill.color = new Color(0.35f, 0.65f, 1f, 1f);

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(go.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(14f, 0f);
        var handle = handleGo.AddComponent<Image>();
        handle.sprite = WhiteSprite;
        handle.color = Color.white;

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        return slider;
    }

    public static void EnsureHorizontalFill(Image img, Color? color = null)
    {
        if (img == null)
            return;

        if (img.sprite == null)
            img.sprite = WhiteSprite;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillClockwise = true;
        if (color.HasValue)
            img.color = color.Value;
        img.raycastTarget = false;
    }

    public static void EnsureRadial360Fill(Image img, Color? color = null)
    {
        if (img == null)
            return;

        if (img.sprite == null)
            img.sprite = WhiteSprite;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        if (color.HasValue)
            img.color = color.Value;
        img.raycastTarget = false;
    }

    public static void EnsureVerticalFill(Image img, Color? color = null)
    {
        if (img == null)
            return;

        if (img.sprite == null)
            img.sprite = WhiteSprite;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Vertical;
        img.fillOrigin = (int)Image.Origin360.Bottom;
        img.fillClockwise = true;
        if (color.HasValue)
            img.color = color.Value;
        img.raycastTarget = false;
    }

    public static void EnsureSimpleTrack(Image img, Color? color = null)
    {
        if (img == null)
            return;

        if (img.sprite == null)
            img.sprite = WhiteSprite;

        img.type = Image.Type.Simple;
        if (color.HasValue)
            img.color = color.Value;
        img.raycastTarget = false;
    }

    public static (Image track, Image fill) CreateHorizontalBar(Transform parent, string name, Vector2 size, Color fillColor)
    {
        var trackGo = new GameObject(name, typeof(RectTransform));
        trackGo.transform.SetParent(parent, false);
        var trackRt = trackGo.GetComponent<RectTransform>();
        trackRt.sizeDelta = size;

        var track = trackGo.AddComponent<Image>();
        track.sprite = WhiteSprite;
        track.color = EmptySlotColor;
        track.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        var fill = fillGo.AddComponent<Image>();
        fill.sprite = WhiteSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.color = fillColor;
        fill.raycastTarget = false;
        return (track, fill);
    }

    public static Button CreateButton(Transform parent, string label, Vector2 size)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = BorderColor;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f);
        colors.pressedColor = new Color(0.2f, 0.22f, 0.28f);
        btn.colors = colors;

        CreateLabel(go.transform, "Label", label, 18f, TextAlignmentOptions.Center);
        return btn;
    }

    public static Color GetPlaceholderColor(HudPlaceholderKind kind) => kind switch
    {
        HudPlaceholderKind.Head => new Color(0.35f, 0.55f, 0.95f, 1f),
        HudPlaceholderKind.Torso => new Color(0.35f, 0.85f, 0.45f, 1f),
        HudPlaceholderKind.Arm => new Color(0.95f, 0.55f, 0.25f, 1f),
        HudPlaceholderKind.Leg => new Color(0.65f, 0.4f, 0.95f, 1f),
        HudPlaceholderKind.Weapon => new Color(0.55f, 0.58f, 0.62f, 1f),
        _ => EmptySlotColor
    };
}
