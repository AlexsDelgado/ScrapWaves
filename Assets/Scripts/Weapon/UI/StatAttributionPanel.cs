#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dev-only stat attribution view: adds tabs to the pause menu's PlayerStatsPanel.
///
/// Both dev tabs are master-detail. STATS lists every stat with its per-source subtotals and
/// opens the modifier-by-modifier breakdown on the right. LAST HIT lists the multiplicative
/// chain of the most recent resolved hit and explains where each factor came from, drilling
/// into the same stat breakdown when the factor is stat-driven.
///
/// Compiled only in the editor and development builds; release panels stay untouched.
/// </summary>
[DisallowMultipleComponent]
public sealed class StatAttributionPanel : MonoBehaviour
{
    private enum Tab { Summary, Stats, LastHit }

    /// <summary>Which factor row of the last-hit chain is selected.</summary>
    private enum Factor
    {
        Base, WeaponLevel, UpgradePath, DamageStat, AbilityStat,
        Critical, Elite, Range, DamageScale, AdditionalScale
    }

    private readonly struct Breakdown
    {
        public readonly float Base;
        public readonly float LevelUp;
        public readonly int LevelUpCount;
        public readonly float Items;
        public readonly int ItemCount;
        public readonly float Other;
        public readonly float Multiplier;
        public readonly float Final;

        public Breakdown(float baseValue, float levelUp, int levelUpCount, float items, int itemCount,
            float other, float multiplier, float final)
        {
            Base = baseValue;
            LevelUp = levelUp;
            LevelUpCount = levelUpCount;
            Items = items;
            ItemCount = itemCount;
            Other = other;
            Multiplier = multiplier;
            Final = final;
        }
    }

    private static readonly Color TabIdle = new(0.122f, 0.145f, 0.133f, 1f);
    private static readonly Color TabActive = new(0.659f, 0.78f, 0.561f, 1f);
    private static readonly Color TabIdleText = new(0.678f, 0.741f, 0.69f, 1f);
    private static readonly Color TabActiveText = new(0.06f, 0.08f, 0.07f, 1f);
    private static readonly Color SurfaceBackground = new(0.035f, 0.045f, 0.04f, 0.97f);
    private static readonly Color SurfaceBorder = new(0.659f, 0.78f, 0.561f, 1f);
    private static readonly Color BodyText = new(0.949f, 0.961f, 0.922f, 1f);
    private static readonly Color RowIdle = new(1f, 1f, 1f, 0f);
    private static readonly Color RowSelected = new(0.659f, 0.78f, 0.561f, 0.22f);
    private static readonly Color CategoryText = new(0.851f, 0.416f, 0.196f, 1f);
    private static readonly Color AlertText = new(1f, 0.62f, 0.25f, 1f);
    private static readonly Color BackButton = new(0.851f, 0.416f, 0.196f, 1f);

    private const float TabBarHeight = 30f;
    private const float TabBarTop = -90f;      // below the header (ends near -85) and its divider
    private const float RowHeight = 21f;
    private const string Mono = "<mspace=0.52em>";

    private readonly StringBuilder _builder = new(4096);
    private readonly List<Button> _tabButtons = new();
    private readonly List<StatRow> _statRows = new();
    private readonly List<FactorRow> _factorRows = new();

    private sealed class StatRow
    {
        public StatType Type;
        public Image Background;
        public TextMeshProUGUI Label;
    }

    private sealed class FactorRow
    {
        public Factor Factor;
        public Image Background;
        public TextMeshProUGUI Label;
        public bool Applicable;
    }

    private PlayerStats _stats;
    private GameObject _summaryContent;
    private GameObject _surface;
    private GameObject _statsView;
    private GameObject _lastHitView;
    private RectTransform _statListContent;
    private RectTransform _factorListContent;
    private TextMeshProUGUI _statDetailText;
    private TextMeshProUGUI _factorDetailText;
    private Tab _tab = Tab.Summary;
    private StatType _selectedStat = StatType.DamageMultiplier;
    private Factor _selectedFactor = Factor.Base;
    private bool _hasStatSelection;
    private bool _built;

    /// <summary>
    /// Mounts the tabs inside PlayerStatsPanel. <paramref name="summaryContent"/> is the text
    /// object the panel already used; it is hidden while a dev tab is open.
    /// </summary>
    public void Initialize(GameObject summaryContent)
    {
        _summaryContent = summaryContent;
        Build();
    }

    // Rebuilds the visible tab from current player state; called when the pause menu opens.
    public void Refresh()
    {
        if (!_built)
            return;

        EnsureStatsBound();
        ApplyTabVisibility();

        if (_tab == Tab.Stats)
            RenderStats();
        else if (_tab == Tab.LastHit)
            RenderLastHit();
    }

    /// <summary>
    /// Closes an open dev tab and returns to the summary. Returns true when it consumed the
    /// request, so the pause menu can treat Escape as "close the topmost popup first".
    /// </summary>
    public bool TryCloseDevTab()
    {
        if (!_built || _tab == Tab.Summary)
            return false;

        SelectTab(Tab.Summary);
        return true;
    }

    private void OnDisable()
    {
        if (_built && _tab != Tab.Summary)
            SelectTab(Tab.Summary);
    }

    private void EnsureStatsBound()
    {
        if (_stats == null)
            _stats = FindAnyObjectByType<PlayerStats>();
    }

    // ---------------------------------------------------------------- construction

    private void Build()
    {
        if (_built)
            return;

        RectTransform panel = transform as RectTransform;
        if (panel == null)
            return;

        BuildTabBar(panel);
        MakeRoomForTabBar();
        BuildSurface(panel);
        _built = true;
        SelectTab(Tab.Summary);
    }

    /// <summary>
    /// Pushes the summary content down so the tab bar never covers it. The panel is authored in
    /// the scene, so this is a runtime adjustment and only exists in dev builds.
    /// </summary>
    private void MakeRoomForTabBar()
    {
        if (_summaryContent == null)
            return;

        RectTransform content = _summaryContent.transform as RectTransform;
        if (content == null)
            return;

        float required = -TabBarTop + TabBarHeight + 10f;
        if (-content.offsetMax.y >= required)
            return;

        content.offsetMax = new Vector2(content.offsetMax.x, -required);
    }

    private void BuildTabBar(RectTransform panel)
    {
        GameObject barGo = new("DevTabBar", typeof(RectTransform));
        barGo.transform.SetParent(panel, false);
        RectTransform bar = barGo.GetComponent<RectTransform>();
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(14f, 0f);
        bar.offsetMax = new Vector2(-14f, 0f);
        bar.sizeDelta = new Vector2(bar.sizeDelta.x, TabBarHeight);
        bar.anchoredPosition = new Vector2(0f, TabBarTop);

        HorizontalLayoutGroup layout = barGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        CreateTabButton(bar, "SUMMARY", Tab.Summary);
        CreateTabButton(bar, "STATS", Tab.Stats);
        CreateTabButton(bar, "LAST HIT", Tab.LastHit);
    }

    private void CreateTabButton(RectTransform parent, string label, Tab tab)
    {
        GameObject go = new($"Tab_{tab}", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image background = go.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = TabIdle;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = background;
        Tab captured = tab;
        button.onClick.AddListener(() => SelectTab(captured));

        TextMeshProUGUI text = HudUiFactory.CreateLabel(go.transform, "Label", label, 14f, TextAlignmentOptions.Center);
        TmpUiHelper.ApplyDefaultFont(text);
        text.color = TabIdleText;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        HudUiWire.StretchFull(text.rectTransform);

        _tabButtons.Add(button);
    }

    private void BuildSurface(RectTransform panel)
    {
        // The surface hangs off the pause root rather than the panel: 380x440 is not enough to
        // read 30 stats with their breakdown. The tabs still live inside PlayerStatsPanel.
        Transform host = panel.parent != null ? panel.parent : panel;

        GameObject surfaceGo = new("StatAttributionSurface", typeof(RectTransform));
        surfaceGo.transform.SetParent(host, false);
        _surface = surfaceGo;
        RectTransform surface = surfaceGo.GetComponent<RectTransform>();
        surface.anchorMin = new Vector2(0.5f, 0.5f);
        surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.pivot = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = new Vector2(0f, -20f);
        surface.sizeDelta = new Vector2(1180f, 660f);

        Image border = surfaceGo.AddComponent<Image>();
        border.sprite = HudUiFactory.WhiteSprite;
        border.color = SurfaceBorder;
        border.raycastTarget = false;

        CreateStretched(surfaceGo.transform, "Plate", SurfaceBackground, new Vector2(3f, 3f), new Vector2(-3f, -3f));

        BuildStatsView(surfaceGo.transform);
        BuildLastHitView(surfaceGo.transform);
        BuildBackButton(surfaceGo.transform);

        surfaceGo.SetActive(false);
    }

    private void BuildBackButton(Transform surface)
    {
        GameObject go = new("BackButton", typeof(RectTransform));
        go.transform.SetParent(surface, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -12f);
        rt.sizeDelta = new Vector2(110f, 28f);

        Image background = go.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = BackButton;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => SelectTab(Tab.Summary));

        TextMeshProUGUI text = HudUiFactory.CreateLabel(go.transform, "Label", "BACK  (ESC)", 13f, TextAlignmentOptions.Center);
        TmpUiHelper.ApplyDefaultFont(text);
        text.color = TabActiveText;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        HudUiWire.StretchFull(text.rectTransform);
    }

    private void BuildStatsView(Transform surface)
    {
        GameObject viewGo = new("StatsView", typeof(RectTransform));
        viewGo.transform.SetParent(surface, false);
        _statsView = viewGo;
        HudUiWire.StretchFull(viewGo.GetComponent<RectTransform>());

        CreateTitle(viewGo.transform,
            "STAT ATTRIBUTION   ·   final = (base + additive) x multiplicative   ·   click a row for the full breakdown");

        _statListContent = CreateListColumn(viewGo.transform, "StatList");
        CreateScroll(viewGo.transform, "StatDetail",
            new Vector2(0.63f, 0f), new Vector2(1f, 1f),
            new Vector2(8f, 18f), new Vector2(-18f, -46f), out RectTransform detailContent);
        _statDetailText = CreateBodyText(detailContent);
    }

    private void BuildLastHitView(Transform surface)
    {
        GameObject viewGo = new("LastHitView", typeof(RectTransform));
        viewGo.transform.SetParent(surface, false);
        _lastHitView = viewGo;
        HudUiWire.StretchFull(viewGo.GetComponent<RectTransform>());

        CreateTitle(viewGo.transform,
            "LAST HIT   ·   damage is fully multiplicative   ·   click a factor to see where it comes from");

        _factorListContent = CreateListColumn(viewGo.transform, "FactorList");
        CreateScroll(viewGo.transform, "FactorDetail",
            new Vector2(0.63f, 0f), new Vector2(1f, 1f),
            new Vector2(8f, 18f), new Vector2(-18f, -46f), out RectTransform detailContent);
        _factorDetailText = CreateBodyText(detailContent);

        viewGo.SetActive(false);
    }

    private void CreateTitle(Transform parent, string content)
    {
        TextMeshProUGUI title = HudUiFactory.CreateLabel(parent, "Title", content, 14f, TextAlignmentOptions.Left);
        TmpUiHelper.ApplyDefaultFont(title);
        title.color = TabIdleText;
        title.raycastTarget = false;
        RectTransform rt = title.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(18f, 0f);
        rt.offsetMax = new Vector2(-140f, 0f);   // leaves room for the BACK button
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, 24f);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -14f);
    }

    private RectTransform CreateListColumn(Transform parent, string name)
    {
        CreateScroll(parent, name,
            new Vector2(0f, 0f), new Vector2(0.63f, 1f),
            new Vector2(18f, 18f), new Vector2(-8f, -46f), out RectTransform content);

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 1f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return content;
    }

    private ScrollRect CreateScroll(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, out RectTransform content)
    {
        GameObject scrollGo = new(name, typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = anchorMin;
        scrollRt.anchorMax = anchorMax;
        scrollRt.offsetMin = offsetMin;
        scrollRt.offsetMax = offsetMax;

        GameObject viewportGo = new("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewport = viewportGo.GetComponent<RectTransform>();
        HudUiWire.StretchFull(viewport);
        viewportGo.AddComponent<RectMask2D>();
        // Without a graphic the mouse wheel cannot find the ScrollRect.
        Image catcher = viewportGo.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0.01f);

        GameObject contentGo = new("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        content = contentGo.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        return scroll;
    }

    private TextMeshProUGUI CreateBodyText(RectTransform content)
    {
        TextMeshProUGUI text = content.gameObject.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(text);
        text.fontSize = 14f;
        text.color = BodyText;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return text;
    }

    private static Image CreateStretched(Transform parent, string name, Color color, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = HudUiFactory.WhiteSprite;
        image.color = color;
        image.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return image;
    }

    private Image CreateRow(RectTransform parent, string name, out TextMeshProUGUI label)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = RowHeight;

        Image background = go.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = RowIdle;

        label = HudUiFactory.CreateLabel(go.transform, "Label", string.Empty, 14f, TextAlignmentOptions.Left);
        TmpUiHelper.ApplyDefaultFont(label);
        label.color = BodyText;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(6f, 0f);
        labelRt.offsetMax = new Vector2(-6f, 0f);
        return background;
    }

    // ---------------------------------------------------------------- tabs

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        ApplyTabVisibility();
        UpdateTabButtonVisuals();

        if (tab == Tab.Stats)
            RenderStats();
        else if (tab == Tab.LastHit)
            RenderLastHit();
    }

    private void ApplyTabVisibility()
    {
        bool devTab = _tab != Tab.Summary;
        if (_summaryContent != null)
            _summaryContent.SetActive(!devTab);
        if (_surface != null)
            _surface.SetActive(devTab);
        if (_statsView != null)
            _statsView.SetActive(_tab == Tab.Stats);
        if (_lastHitView != null)
            _lastHitView.SetActive(_tab == Tab.LastHit);
    }

    private void UpdateTabButtonVisuals()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            Button button = _tabButtons[i];
            if (button == null)
                continue;

            bool active = i == (int)_tab;
            if (button.targetGraphic is Image image)
                image.color = active ? TabActive : TabIdle;

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = active ? TabActiveText : TabIdleText;
        }
    }

    // ---------------------------------------------------------------- stats tab

    private void RenderStats()
    {
        EnsureStatsBound();

        if (_stats == null)
        {
            _statDetailText.text = "No PlayerStats found in the scene.";
            return;
        }

        EnsureStatRows();

        for (int i = 0; i < _statRows.Count; i++)
            UpdateStatRow(_statRows[i]);

        RenderStatDetail();
    }

    private void EnsureStatRows()
    {
        if (_statRows.Count > 0)
            return;

        IReadOnlyList<StatDefinition> definitions = _stats.GetAllDefinitions();

        for (int category = 0; category <= (int)StatCategory.Miscellaneous; category++)
        {
            StatCategory current = (StatCategory)category;
            bool headerWritten = false;

            for (int i = 0; i < definitions.Count; i++)
            {
                StatDefinition definition = definitions[i];
                if (definition == null || definition.Category != current)
                    continue;

                if (!headerWritten)
                {
                    CreateSectionHeader(_statListContent, current.ToString().ToUpperInvariant());
                    headerWritten = true;
                }

                CreateStatRow(definition.StatType);
            }
        }

        if (!_hasStatSelection && _statRows.Count > 0)
        {
            _selectedStat = _statRows[0].Type;
            _hasStatSelection = true;
        }
    }

    private void CreateSectionHeader(RectTransform parent, string caption)
    {
        GameObject go = new($"Header_{caption}", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = RowHeight + 8f;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(text);
        text.text = caption;
        text.fontSize = 14f;
        text.fontStyle = FontStyles.Bold;
        text.color = CategoryText;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.raycastTarget = false;
    }

    private void CreateStatRow(StatType type)
    {
        Image background = CreateRow(_statListContent, $"Row_{type}", out TextMeshProUGUI label);
        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        StatType captured = type;
        button.onClick.AddListener(() =>
        {
            _selectedStat = captured;
            _hasStatSelection = true;
            RenderStats();
        });

        _statRows.Add(new StatRow { Type = type, Background = background, Label = label });
    }

    private void UpdateStatRow(StatRow row)
    {
        StatDefinition definition = _stats.GetDefinition(row.Type);
        if (definition == null)
            return;

        Breakdown breakdown = BuildBreakdown(row.Type);
        bool saturated = StatDisplayFormat.IsSaturated(row.Type, breakdown.Final);

        _builder.Clear();
        _builder.Append(Mono)
            .Append(StatDisplayNames.GetDisplayName(row.Type).PadRight(26))
            .Append(StatDisplayFormat.FormatStat(definition, breakdown.Final).PadLeft(11))
            .Append("  lvl ").Append(Column(breakdown.LevelUp, breakdown.LevelUpCount))
            .Append("  itm ").Append(Column(breakdown.Items, breakdown.ItemCount))
            .Append("  x").Append(breakdown.Multiplier.ToString("0.00", CultureInfo.InvariantCulture));

        if (saturated)
            _builder.Append("  <color=#FF9E40>[!]</color>");

        row.Label.text = _builder.ToString();
        row.Label.color = saturated ? AlertText : BodyText;
        row.Background.color = _hasStatSelection && row.Type.Equals(_selectedStat) ? RowSelected : RowIdle;
    }

    private static string Column(float value, int count)
    {
        if (count == 0)
            return "     -    ";

        string amount = (value >= 0f ? "+" : "-") + Mathf.Abs(value).ToString("0.###", CultureInfo.InvariantCulture);
        return (amount + "(" + count + ")").PadRight(11);
    }

    private Breakdown BuildBreakdown(StatType type)
    {
        float baseValue = _stats.GetBaseValue(type);
        IReadOnlyList<StatModifier> modifiers = _stats.GetModifiers(type);

        float levelUp = 0f, items = 0f, other = 0f, multiplier = 1f;
        int levelCount = 0, itemCount = 0;

        for (int i = 0; i < modifiers.Count; i++)
        {
            StatModifier modifier = modifiers[i];
            if (modifier.ModifierType == StatModifierType.Multiplicative)
            {
                multiplier *= modifier.Value;
                if (modifier.Source == StatUpgradeSource.LevelUp) levelCount++;
                else if (modifier.Source == StatUpgradeSource.PassiveItem) itemCount++;
                continue;
            }

            switch (modifier.Source)
            {
                case StatUpgradeSource.LevelUp:
                    levelUp += modifier.Value;
                    levelCount++;
                    break;
                case StatUpgradeSource.PassiveItem:
                    items += modifier.Value;
                    itemCount++;
                    break;
                default:
                    other += modifier.Value;
                    break;
            }
        }

        return new Breakdown(baseValue, levelUp, levelCount, items, itemCount, other, multiplier,
            _stats.GetStat(type));
    }

    private void RenderStatDetail()
    {
        _builder.Clear();
        AppendStatBreakdown(_hasStatSelection ? _selectedStat : (StatType?)null);
        _statDetailText.text = _builder.ToString();
    }

    /// <summary>Writes one stat's full attribution into the shared builder.</summary>
    private void AppendStatBreakdown(StatType? statType)
    {
        if (!statType.HasValue)
        {
            _builder.Append("Pick a stat from the list.");
            return;
        }

        StatType type = statType.Value;
        StatDefinition definition = _stats != null ? _stats.GetDefinition(type) : null;
        if (definition == null)
        {
            _builder.Append("Stat not configured on PlayerStats.");
            return;
        }

        Breakdown breakdown = BuildBreakdown(type);
        bool saturated = StatDisplayFormat.IsSaturated(type, breakdown.Final);

        _builder.Append("<b>").Append(StatDisplayNames.GetDisplayName(type)).Append("</b>\n").Append(Mono);
        _builder.Append("final value   ").Append(StatDisplayFormat.FormatStat(definition, breakdown.Final)).Append('\n');

        if (saturated)
        {
            float max = StatDisplayFormat.GetEffectiveMaximum(type);
            _builder.Append("<color=#FF9E40>[!] consumers clamp this to ")
                .Append(StatDisplayFormat.FormatRaw(max))
                .Append("  ->  ").Append(StatDisplayFormat.FormatStat(definition, max))
                .Append("</color>\n");
        }

        _builder.Append('\n').Append("base            ").Append(StatDisplayFormat.FormatRaw(breakdown.Base)).Append('\n');

        AppendSourceSection(type, "FROM LEVEL UPS", StatUpgradeSource.LevelUp, breakdown.LevelUp, breakdown.LevelUpCount);
        AppendSourceSection(type, "FROM PASSIVE ITEMS", StatUpgradeSource.PassiveItem, breakdown.Items, breakdown.ItemCount);
        AppendOtherSources(type);

        _builder.Append('\n')
            .Append("= (").Append(StatDisplayFormat.FormatRaw(breakdown.Base))
            .Append(" + ").Append(StatDisplayFormat.FormatRaw(breakdown.LevelUp + breakdown.Items + breakdown.Other))
            .Append(") x ").Append(StatDisplayFormat.FormatRaw(breakdown.Multiplier))
            .Append("  =  ").Append(StatDisplayFormat.FormatRaw(breakdown.Final)).Append('\n');
    }

    private void AppendSourceSection(StatType type, string title, StatUpgradeSource source, float total, int count)
    {
        _builder.Append('\n').Append("<b>").Append(title).Append("</b>   ");

        if (count == 0)
        {
            _builder.Append("no contribution\n");
            return;
        }

        _builder.Append(StatDisplayFormat.FormatSigned(total)).Append("   (").Append(count).Append(")\n");

        IReadOnlyList<StatModifier> modifiers = _stats.GetModifiers(type);
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Source == source)
                AppendModifierLine(modifiers[i]);
        }
    }

    private void AppendOtherSources(StatType type)
    {
        IReadOnlyList<StatModifier> modifiers = _stats.GetModifiers(type);
        bool headerWritten = false;

        for (int i = 0; i < modifiers.Count; i++)
        {
            StatModifier modifier = modifiers[i];
            if (modifier.Source == StatUpgradeSource.LevelUp || modifier.Source == StatUpgradeSource.PassiveItem)
                continue;

            if (!headerWritten)
            {
                _builder.Append('\n').Append("<b>OTHER SOURCES</b>\n");
                headerWritten = true;
            }

            AppendModifierLine(modifier);
        }
    }

    private void AppendModifierLine(StatModifier modifier)
    {
        bool multiplicative = modifier.ModifierType == StatModifierType.Multiplicative;
        _builder.Append("  ")
            .Append((multiplicative
                ? StatDisplayFormat.FormatMultiplier(modifier.Value)
                : StatDisplayFormat.FormatSigned(modifier.Value)).PadRight(10))
            .Append(StatDisplayFormat.DescribeModifier(modifier))
            .Append('\n');
    }

    // ---------------------------------------------------------------- last hit tab

    private void RenderLastHit()
    {
        EnsureStatsBound();
        EnsureFactorRows();

        if (!LastHitRecorder.HasRoll)
        {
            for (int i = 0; i < _factorRows.Count; i++)
            {
                _factorRows[i].Label.text = string.Empty;
                _factorRows[i].Background.color = RowIdle;
                _factorRows[i].Applicable = false;
            }

            _factorDetailText.text = "No hit recorded yet.\nShoot an enemy, then reopen this tab.";
            return;
        }

        WeaponDamageRoll roll = LastHitRecorder.LastRoll;
        WeaponDamageFactors factors = roll.Factors;

        float running = factors.BaseDamage;
        for (int i = 0; i < _factorRows.Count; i++)
            UpdateFactorRow(_factorRows[i], factors, ref running);

        RenderFactorDetail(roll, factors);
    }

    private void EnsureFactorRows()
    {
        if (_factorRows.Count > 0)
            return;

        CreateSectionHeader(_factorListContent, "MULTIPLICATIVE CHAIN");
        for (int i = 0; i <= (int)Factor.AdditionalScale; i++)
            CreateFactorRow((Factor)i);
    }

    private void CreateFactorRow(Factor factor)
    {
        Image background = CreateRow(_factorListContent, $"Factor_{factor}", out TextMeshProUGUI label);
        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        Factor captured = factor;
        button.onClick.AddListener(() =>
        {
            _selectedFactor = captured;
            RenderLastHit();
        });

        _factorRows.Add(new FactorRow { Factor = factor, Background = background, Label = label });
    }

    private void UpdateFactorRow(FactorRow row, in WeaponDamageFactors factors, ref float running)
    {
        float value = GetFactorValue(row.Factor, factors);
        bool isBase = row.Factor == Factor.Base;
        bool neutral = !isBase && Mathf.Approximately(value, 1f);
        row.Applicable = !neutral;

        if (isBase)
            running = factors.BaseDamage;
        else if (!neutral)
            running *= value;

        _builder.Clear();
        _builder.Append(Mono).Append(GetFactorLabel(row.Factor).PadRight(24));

        if (neutral)
        {
            _builder.Append("     -   ").Append("          (not applied)");
            row.Label.color = TabIdleText;
        }
        else
        {
            _builder.Append(StatDisplayFormat.FormatRaw(value).PadLeft(10))
                .Append("   ->  ").Append(StatDisplayFormat.FormatRaw(running).PadLeft(10));
            row.Label.color = BodyText;
        }

        row.Label.text = _builder.ToString();
        row.Background.color = row.Factor == _selectedFactor ? RowSelected : RowIdle;
    }

    private static float GetFactorValue(Factor factor, in WeaponDamageFactors f) => factor switch
    {
        Factor.Base => f.BaseDamage,
        Factor.WeaponLevel => f.LevelMultiplier,
        Factor.UpgradePath => f.PathMultiplier,
        Factor.DamageStat => f.StatDamageMultiplier,
        Factor.AbilityStat => f.AbilityMultiplier,
        Factor.Critical => f.CritMultiplier,
        Factor.Elite => f.EliteMultiplier,
        Factor.Range => f.RangeMultiplier,
        Factor.DamageScale => f.DamageScale,
        Factor.AdditionalScale => f.AdditionalScale,
        _ => 1f
    };

    private static string GetFactorLabel(Factor factor) => factor switch
    {
        Factor.Base => "base damage",
        Factor.WeaponLevel => "x weapon level",
        Factor.UpgradePath => "x upgrade path",
        Factor.DamageStat => "x Damage Multiplier",
        Factor.AbilityStat => "x Ability Damage",
        Factor.Critical => "x critical",
        Factor.Elite => "x elite / boss",
        Factor.Range => "x range",
        Factor.DamageScale => "x damage scale",
        Factor.AdditionalScale => "x additional scale",
        _ => factor.ToString()
    };

    /// <summary>Stat that backs a factor, when the factor is stat-driven.</summary>
    private static StatType? GetFactorStat(Factor factor) => factor switch
    {
        Factor.DamageStat => StatType.DamageMultiplier,
        Factor.AbilityStat => StatType.AbilityDamageMultiplier,
        Factor.Critical => StatType.CriticalDamage,
        Factor.Elite => StatType.EliteDamageMultiplier,
        _ => null
    };

    private void RenderFactorDetail(in WeaponDamageRoll roll, in WeaponDamageFactors factors)
    {
        _builder.Clear();

        string weaponName = roll.Weapon?.Data != null ? roll.Weapon.Data.name : "unknown weapon";
        _builder.Append("<b>").Append(weaponName).Append("</b>");
        if (roll.IsCritical) _builder.Append("   [CRIT]");
        if (roll.EliteOrBoss) _builder.Append("   [ELITE/BOSS]");
        if (roll.IsAbilityDamage) _builder.Append("   [ABILITY]");
        _builder.Append('\n').Append(Mono);
        _builder.Append("resolved damage   ").Append(StatDisplayFormat.FormatRaw(factors.FinalDamage))
            .Append("  ->  rounded to ").Append(Mathf.Max(1, Mathf.RoundToInt(factors.FinalDamage))).Append('\n');
        _builder.Append("The enemy still applies its own damage-taken multiplier\n")
            .Append("(EnemyHealth or the hit zone), so the combat text may differ.\n\n");

        _builder.Append("<b>").Append(GetFactorLabel(_selectedFactor).ToUpperInvariant()).Append("</b>   ")
            .Append(StatDisplayFormat.FormatRaw(GetFactorValue(_selectedFactor, factors))).Append("\n\n");

        AppendFactorExplanation(roll, factors);

        _factorDetailText.text = _builder.ToString();
    }

    private void AppendFactorExplanation(in WeaponDamageRoll roll, in WeaponDamageFactors factors)
    {
        WeaponData data = roll.Weapon?.Data;

        switch (_selectedFactor)
        {
            case Factor.Base:
                _builder.Append("WeaponData.BaseDamage, authored on the weapon asset.\n");
                if (data != null)
                    _builder.Append("asset            ").Append(data.name).Append('\n');
                return;

            case Factor.WeaponLevel:
                _builder.Append("WeaponLevelData.DamageMultiplier for the weapon's current level.\n");
                if (roll.Weapon != null)
                    _builder.Append("weapon level     ").Append(roll.Weapon.Level).Append('\n');
                return;

            case Factor.UpgradePath:
                _builder.Append("WeaponUpgradePathData.DamageMultiplier for the selected path.\n");
                if (roll.Weapon != null)
                {
                    _builder.Append("path             ").Append(roll.Weapon.SelectedPath).Append('\n');
                    _builder.Append("advanced path    ").Append(roll.Weapon.HasAdvancedPath ? "yes" : "no").Append('\n');
                }
                return;

            case Factor.Critical:
                // The weapon multiplier scales the stat, it does not replace it.
                _builder.Append("CriticalDamage stat x the weapon's own crit multiplier.\n\n")
                    .Append("CriticalDamage   ").Append(StatDisplayFormat.FormatRaw(factors.CritDamageStat)).Append('\n')
                    .Append("weapon override  ").Append(StatDisplayFormat.FormatRaw(factors.CritOverride)).Append('\n')
                    .Append("product          ").Append(StatDisplayFormat.FormatRaw(factors.CritMultiplier)).Append("\n\n");
                AppendStatBreakdown(StatType.CriticalDamage);
                _builder.Append('\n');
                AppendStatBreakdown(StatType.CriticalChance);
                return;

            case Factor.Range:
                _builder.Append("Close range (<10m) or long range (>15m) stat.\n")
                    .Append("Between 10m and 15m neither applies.\n\n");
                AppendStatBreakdown(StatType.CloseRangeDamageMultiplier);
                _builder.Append('\n');
                AppendStatBreakdown(StatType.LongRangeDamageMultiplier);
                return;

            case Factor.DamageScale:
                _builder.Append("Per-shot scale set by the weapon: heat bonus, burst segment,\n")
                    .Append("charge level and similar weapon-side modifiers.\n");
                return;

            case Factor.AdditionalScale:
                _builder.Append("Per-impact scale: pierce falloff, explosion distance falloff,\n")
                    .Append("weak-point bonus and similar hit-side modifiers.\n");
                return;

            default:
                StatType? stat = GetFactorStat(_selectedFactor);
                if (stat.HasValue)
                    AppendStatBreakdown(stat);
                return;
        }
    }
}
#endif
