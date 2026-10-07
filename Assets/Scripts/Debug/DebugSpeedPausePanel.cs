using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Submenú de pausa para calibrar ritmo. Muestra el valor base y el propuesto
/// con el factor, para copiarlo después al stat o al prefab. No graba nada.
/// </summary>
[DisallowMultipleComponent]
public class DebugSpeedPausePanel : MonoBehaviour
{
    private static readonly Color Plate = new(0.122f, 0.145f, 0.133f, 1f);
    private static readonly Color DeepSteel = new(0.067f, 0.078f, 0.075f, 0.97f);
    private static readonly Color Bone = new(0.949f, 0.961f, 0.922f, 1f);
    private static readonly Color MutedSteel = new(0.678f, 0.741f, 0.69f, 1f);
    private static readonly Color ScrapGreen = new(0.659f, 0.78f, 0.561f, 1f);
    private static readonly Color WarningRust = new(0.851f, 0.416f, 0.196f, 1f);

    private DebugSpeedTool _tool;
    private Slider _playerSlider;
    private Slider _projectileSlider;
    private Slider _enemySlider;
    private Toggle _applyToggle;
    private TextMeshProUGUI _playerValue;
    private TextMeshProUGUI _projectileValue;
    private TextMeshProUGUI _enemyValue;
    private TextMeshProUGUI _playerReadout;
    private TextMeshProUGUI _projectileReadout;
    private TextMeshProUGUI _enemyReadout;
    private bool _refreshing;

    public Button BackButton { get; private set; }

    public void Build(Action onBack)
    {
        RectTransform root = gameObject.GetComponent<RectTransform>();
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();
        root.sizeDelta = new Vector2(1500f, 480f);

        Image border = gameObject.GetComponent<Image>();
        if (border == null)
            border = gameObject.AddComponent<Image>();
        border.sprite = HudUiFactory.WhiteSprite;
        border.color = ScrapGreen;
        border.raycastTarget = true;

        Image plate = CreateImage("Plate", transform, DeepSteel);
        Stretch(plate.rectTransform, 4f);

        TextMeshProUGUI title = CreateText("Title", plate.transform, "VELOCIDADES", 26f, FontStyles.Bold, Bone, TextAlignmentOptions.MidlineLeft);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(240f, 40f), new Vector2(0f, 1f));

        TextMeshProUGUI hint = CreateText(
            "Hint",
            plate.transform,
            "Cada fila es un tipo. El número de la derecha es base × factor, para copiar si cierra.",
            15f,
            FontStyles.Normal,
            MutedSteel,
            TextAlignmentOptions.MidlineLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(250f, -12f), new Vector2(620f, 36f), new Vector2(0f, 1f));

        _applyToggle = CreateToggle(plate.transform, "Probar al reanudar");
        SetRect(_applyToggle.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-188f, -12f), new Vector2(280f, 36f), new Vector2(1f, 1f));

        BackButton = CreateButton(plate.transform, "BackButton", "VOLVER");
        SetRect(BackButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(156f, 44f), new Vector2(1f, 1f));
        if (onBack != null)
            BackButton.onClick.AddListener(() => onBack());

        _playerSlider = CreateColumn(plate.transform, "JUGADOR", -490f, out _playerValue, out _playerReadout);
        _projectileSlider = CreateColumn(plate.transform, "PROYECTILES", 0f, out _projectileValue, out _projectileReadout);
        _enemySlider = CreateColumn(plate.transform, "ENEMIGOS", 490f, out _enemyValue, out _enemyReadout);
    }

    public void Bind(DebugSpeedTool tool)
    {
        _tool = tool;
        if (_tool == null || _playerSlider == null)
            return;

        _refreshing = true;
        _playerSlider.SetValueWithoutNotify(_tool.Scale);
        _projectileSlider.SetValueWithoutNotify(_tool.ProjectileSpeedScale);
        _enemySlider.SetValueWithoutNotify(_tool.EnemySpeedScale);
        _applyToggle.SetIsOnWithoutNotify(_tool.IsApplying);
        _refreshing = false;
        Refresh();
    }

    public void Refresh()
    {
        if (_tool == null)
            return;

        SetFactorLabel(_playerValue, _tool.Scale);
        SetFactorLabel(_projectileValue, _tool.ProjectileSpeedScale);
        SetFactorLabel(_enemyValue, _tool.EnemySpeedScale);
        _tool.FillColumns(out string player, out string projectiles, out string enemies);
        if (_playerReadout != null)
            _playerReadout.text = player;
        if (_projectileReadout != null)
            _projectileReadout.text = projectiles;
        if (_enemyReadout != null)
            _enemyReadout.text = enemies;
    }

    private Slider CreateColumn(Transform parent, string title, float centerX, out TextMeshProUGUI valueLabel, out TextMeshProUGUI readout)
    {
        var column = new GameObject(title, typeof(RectTransform));
        column.transform.SetParent(parent, false);
        RectTransform columnRt = column.GetComponent<RectTransform>();
        SetRect(columnRt, new Vector2(0.5f, 1f), new Vector2(centerX, -64f), new Vector2(460f, 388f), new Vector2(0.5f, 1f));

        Image background = column.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = Plate;
        background.raycastTarget = true;

        TextMeshProUGUI header = CreateText("Header", column.transform, title, 16f, FontStyles.Bold, MutedSteel, TextAlignmentOptions.MidlineLeft);
        SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(220f, 26f), new Vector2(0f, 1f));

        valueLabel = CreateText("Value", column.transform, "x1.00", 16f, FontStyles.Bold, Bone, TextAlignmentOptions.MidlineRight);
        SetRect(valueLabel.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(200f, 26f), new Vector2(1f, 1f));

        Slider slider = HudUiFactory.CreateSlider(column.transform, "Slider", new Vector2(428f, 26f), 0.5f, 2.5f, 1f);
        RectTransform sliderRt = slider.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0.5f, 1f);
        sliderRt.anchorMax = new Vector2(0.5f, 1f);
        sliderRt.pivot = new Vector2(0.5f, 1f);
        sliderRt.anchoredPosition = new Vector2(0f, -40f);
        slider.onValueChanged.AddListener(OnSliderChanged);

        var readoutGo = new GameObject("Readout", typeof(RectTransform));
        readoutGo.transform.SetParent(column.transform, false);
        SetRect(readoutGo.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(14f, -78f), new Vector2(432f, 296f), new Vector2(0f, 1f));
        readout = readoutGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(readout);
        readout.fontSize = 16f;
        readout.fontStyle = FontStyles.Bold;
        readout.alignment = TextAlignmentOptions.TopLeft;
        readout.color = Bone;
        readout.lineSpacing = 4f;
        readout.raycastTarget = false;
        readout.textWrappingMode = TextWrappingModes.NoWrap;
        readout.overflowMode = TextOverflowModes.Overflow;
        return slider;
    }

    private void OnSliderChanged(float _)
    {
        if (_refreshing || _tool == null)
            return;

        _tool.Scale = _playerSlider.value;
        _tool.ProjectileSpeedScale = _projectileSlider.value;
        _tool.EnemySpeedScale = _enemySlider.value;
        Refresh();
    }

    private Toggle CreateToggle(Transform parent, string caption)
    {
        var go = new GameObject("ApplyToggle", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image box = CreateImage("Box", go.transform, Plate);
        RectTransform boxRt = box.rectTransform;
        boxRt.anchorMin = new Vector2(0f, 0.5f);
        boxRt.anchorMax = new Vector2(0f, 0.5f);
        boxRt.pivot = new Vector2(0f, 0.5f);
        boxRt.anchoredPosition = new Vector2(8f, 0f);
        boxRt.sizeDelta = new Vector2(28f, 28f);

        Image check = CreateImage("Check", box.transform, ScrapGreen);
        Stretch(check.rectTransform, 6f);

        Toggle toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = false;
        toggle.onValueChanged.AddListener(OnApplyChanged);

        TextMeshProUGUI label = CreateText("Label", go.transform, caption, 16f, FontStyles.Bold, Bone, TextAlignmentOptions.MidlineLeft);
        SetRect(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(220f, 32f), new Vector2(0f, 0.5f));
        return toggle;
    }

    private void OnApplyChanged(bool value)
    {
        if (_refreshing || _tool == null)
            return;

        _tool.SetApplying(value);
        Refresh();
    }

    private Button CreateButton(Transform parent, string name, string caption)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image plate = go.AddComponent<Image>();
        plate.sprite = HudUiFactory.WhiteSprite;
        plate.color = Plate;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = plate;
        ColorBlock colors = button.colors;
        colors.normalColor = Plate;
        colors.highlightedColor = WarningRust;
        colors.selectedColor = WarningRust;
        colors.pressedColor = ScrapGreen;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI label = CreateText("Label", go.transform, caption, 22f, FontStyles.Bold, Bone, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 8f);
        label.raycastTarget = false;
        return button;
    }

    private static void SetFactorLabel(TextMeshProUGUI label, float scale)
    {
        if (label == null)
            return;

        float delta = (scale - 1f) * 100f;
        string sign = delta >= 0f ? "+" : string.Empty;
        label.text = $"x{scale:0.00}   {sign}{delta:0}%";
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = align;
        label.raycastTarget = false;
        return label;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = HudUiFactory.WhiteSprite;
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }
}
