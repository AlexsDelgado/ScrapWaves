using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WeaponSandboxDebugUI : MonoBehaviour
{
    private WeaponTestingSandboxManager _sandbox;
    private Canvas _canvas;
    private TextMeshProUGUI _loadoutText;
    private TextMeshProUGUI _heatText;
    private TextMeshProUGUI _metricsText;
    private TextMeshProUGUI _statsText;
    private TMP_Dropdown[] _slotDropdowns;
    private TMP_Dropdown _levelSlotDropdown;
    private TMP_Dropdown _levelDropdown;
    private TMP_Dropdown _pathDropdown;
    private Slider _heatSlider;
    private readonly StringBuilder _sb = new(1024);
    private float _nextRefreshTime;
    private bool _isRefreshing;

    public void Bind(WeaponTestingSandboxManager sandbox)
    {
        _sandbox = sandbox;
    }

    private void Start()
    {
        if (_sandbox == null)
            _sandbox = GetComponent<WeaponTestingSandboxManager>();

        BuildUi();
        RefreshAllStaticSelections();
    }

    private void LateUpdate()
    {
        if (_sandbox == null || Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + 0.12f;
        RefreshDynamicText();
    }

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("WeaponSandboxDebugUI");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject panel = CreateUiObject("Panel", canvasGo.transform);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0f, 0.5f);
        panelRt.anchoredPosition = new Vector2(12f, 0f);
        panelRt.sizeDelta = new Vector2(520f, -24f);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.03f, 0.035f, 0.04f, 0.88f);

        ScrollRect scroll = panel.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        GameObject viewport = CreateUiObject("Viewport", panel.transform);
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(8f, 8f);
        viewportRt.offsetMax = new Vector2(-8f, -8f);
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0f);
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        scroll.viewport = viewportRt;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRt;

        BuildLoadoutSection(content.transform);
        BuildStatOverrideSection(content.transform);
        BuildHeatSection(content.transform);
        BuildRuntimeControlSection(content.transform);
        BuildEnemySpawnSection(content.transform);
        BuildMetricsSection(content.transform);
        BuildDebugTogglesSection(content.transform);
    }

    private void BuildLoadoutSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Player Weapon Loadout");
        _loadoutText = CreateText(section, "LoadoutText", "", 18, TextAlignmentOptions.TopLeft);

        _slotDropdowns = new TMP_Dropdown[WeaponTestingSandboxManager.WeaponSlots];
        for (int i = 0; i < _slotDropdowns.Length; i++)
        {
            int slot = i;
            _slotDropdowns[i] = CreateDropdown(section, $"Slot {i + 1}", WeaponOptions(), 1, value =>
            {
                if (_isRefreshing)
                    return;
                _sandbox.SetWeaponSlot(slot, WeaponTypeFromDropdown(value));
                RefreshAllStaticSelections();
            });
        }

        _levelSlotDropdown = CreateDropdown(section, "Upgrade Slot", new List<string> { "Slot 1", "Slot 2", "Slot 3" }, 0, _ => RefreshLevelPathSelections());
        _levelDropdown = CreateDropdown(section, "Weapon Level", LevelOptions(), 0, _ => ApplySelectedLevelPath());
        _pathDropdown = CreateDropdown(section, "Upgrade Path", new List<string> { "None", "Path A", "Path B" }, 0, _ => ApplySelectedLevelPath());
    }

    private void BuildStatOverrideSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Stat Override");
        _statsText = CreateText(section, "StatsText", "", 16, TextAlignmentOptions.TopLeft);
        WeaponStatOverride stats = _sandbox.StatOverride;

        CreateStatSlider(section, "Damage Multiplier", 0f, 5f, stats.DamageMultiplier, v => { stats.DamageMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Elite Damage Multiplier", 0f, 5f, stats.EliteDamageMultiplier, v => { stats.EliteDamageMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Attack Speed Multiplier", 0.1f, 5f, stats.AttackSpeedMultiplier, v => { stats.AttackSpeedMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Projectile / Area Size Multiplier", 0.1f, 5f, stats.ProjectileAreaSizeMultiplier, v => { stats.ProjectileAreaSizeMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Critical Chance", 0f, 1f, stats.CriticalChance, v => { stats.CriticalChance = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Critical Damage Multiplier", 1f, 6f, stats.CriticalDamageMultiplier, v => { stats.CriticalDamageMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Knockback Multiplier", 0f, 5f, stats.KnockbackMultiplier, v => { stats.KnockbackMultiplier = v; stats.ApplyOverrides(); });
        CreateStatSlider(section, "Ammo Multiplier", 0f, 5f, stats.AmmoMultiplier, v => { stats.AmmoMultiplier = v; stats.ApplyOverrides(); });
        CreateButton(section, "Reset Stats To Default", () =>
        {
            stats.ResetToDefaults();
            RebuildUi();
        });
    }

    private void BuildHeatSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Heat Control");
        _heatText = CreateText(section, "HeatText", "", 18, TextAlignmentOptions.TopLeft);

        Transform row = CreateRow(section, "HeatButtons");
        CreateButton(row, "Set Heat 0%", () => SetHeat(0f));
        CreateButton(row, "Set Heat 25%", () => SetHeat(25f));
        CreateButton(row, "Set Heat 50%", () => SetHeat(50f));
        CreateButton(row, "Set Heat 75%", () => SetHeat(75f));
        CreateButton(row, "Set Heat 100%", () => SetHeat(100f));

        _heatSlider = CreateSlider(section, "Heat Slider", 0f, 100f, 0f, value =>
        {
            if (_isRefreshing)
                return;
            _sandbox.HeatOverride.SetHeatPercent(value);
        });
    }

    private void BuildRuntimeControlSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Weapon Runtime Controls");
        Transform row1 = CreateRow(section, "RuntimeRow1");
        CreateButton(row1, "Force Automatic Mode", _sandbox.ForceAutomaticMode);
        CreateButton(row1, "Force Manual Mode", _sandbox.ForceManualMode);
        Transform row2 = CreateRow(section, "RuntimeRow2");
        CreateButton(row2, "Refill Ammo", _sandbox.RefillAmmo);
        CreateButton(row2, "Empty Ammo", _sandbox.EmptyAmmo);
        CreateButton(row2, "Cycle To Next Weapon", _sandbox.CycleToNextWeapon);
        Transform row3 = CreateRow(section, "RuntimeRow3");
        CreateButton(row3, "Use Active Ability", _sandbox.UseActiveAbility);
        CreateButton(row3, "Reset Weapon Cooldowns", _sandbox.ResetWeaponCooldowns);
    }

    private void BuildEnemySpawnSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Enemy Spawn");
        WeaponDummySpawner spawner = _sandbox.Spawner;

        Transform row1 = CreateRow(section, "SpawnRow1");
        CreateButton(row1, "Spawn Single Dummy", spawner.SpawnSingleDummy);
        CreateButton(row1, "Spawn Group", spawner.SpawnGroup);
        CreateButton(row1, "Spawn Moving Targets", spawner.SpawnMovingTargets);
        Transform row2 = CreateRow(section, "SpawnRow2");
        CreateButton(row2, "Spawn Elite Dummy", spawner.SpawnEliteDummy);
        CreateButton(row2, "Spawn Boss Dummy", spawner.SpawnBossDummy);
        CreateButton(row2, "Clear Enemies", spawner.ClearEnemies);
        CreateButton(row2, "Respawn Current Test", spawner.RespawnCurrentTest);
        Transform row3 = CreateRow(section, "FormationRow");
        CreateButton(row3, "Line Formation", () => spawner.SpawnGroup(WeaponSandboxFormation.Line));
        CreateButton(row3, "Circle Formation", () => spawner.SpawnGroup(WeaponSandboxFormation.Circle));
        CreateButton(row3, "Packed Group", () => spawner.SpawnGroup(WeaponSandboxFormation.PackedGroup));
        CreateButton(row3, "Spread Group", () => spawner.SpawnGroup(WeaponSandboxFormation.SpreadGroup));
        CreateButton(row3, "Random Formation", () => spawner.SpawnGroup(WeaponSandboxFormation.Random));

        CreateIntField(section, "Enemy Health", spawner.EnemyHealth, value => spawner.EnemyHealth = Mathf.Max(1, value));
        CreateFloatField(section, "Enemy Movement Speed", spawner.EnemyMovementSpeed, value => spawner.EnemyMovementSpeed = Mathf.Max(0f, value));
        CreateDropdown(section, "Enemy Type", new List<string> { "Normal", "Elite", "Boss" }, 0, value => spawner.EnemyType = (WeaponEnemyKind)value);
        CreateIntField(section, "Enemy Count", spawner.EnemyCount, value => spawner.EnemyCount = Mathf.Max(1, value));
        CreateFloatField(section, "Enemy Spacing", spawner.EnemySpacing, value => spawner.EnemySpacing = Mathf.Max(0.25f, value));
        CreateDropdown(section, "Movement Pattern", new List<string> { "Left-Right", "Toward Player", "Away From Player", "Circle Around Player", "Random Wandering" }, 0, value =>
        {
            spawner.CurrentMovementPattern = value switch
            {
                1 => WeaponSandboxMovementPattern.TowardPlayer,
                2 => WeaponSandboxMovementPattern.AwayFromPlayer,
                3 => WeaponSandboxMovementPattern.CircleAroundPlayer,
                4 => WeaponSandboxMovementPattern.RandomWander,
                _ => WeaponSandboxMovementPattern.LeftRight
            };
        });
    }

    private void BuildMetricsSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Metrics");
        _metricsText = CreateText(section, "MetricsText", "", 16, TextAlignmentOptions.TopLeft);
        Transform row = CreateRow(section, "MetricsButtons");
        CreateButton(row, "Reset Metrics", _sandbox.Metrics.ResetMetrics);
        CreateButton(row, "Export Metrics To Console", _sandbox.Metrics.ExportToConsole);
    }

    private void BuildDebugTogglesSection(Transform parent)
    {
        Transform section = CreateSection(parent, "Debug Visualization");
        WeaponDebugGizmos gizmos = _sandbox.DebugGizmos;
        CreateToggle(section, "Show Targeting Cone", gizmos.ShowTargetingCone, value => gizmos.ShowTargetingCone = value);
        CreateToggle(section, "Show Projectile Paths", gizmos.ShowProjectilePaths, value => gizmos.ShowProjectilePaths = value);
        CreateToggle(section, "Show Explosion Radius", gizmos.ShowExplosionRadius, value => gizmos.ShowExplosionRadius = value);
        CreateToggle(section, "Show Damage Numbers", gizmos.ShowDamageNumbers, value => gizmos.ShowDamageNumbers = value);
        CreateToggle(section, "Show Knockback Vectors", gizmos.ShowKnockbackVectors, value => gizmos.ShowKnockbackVectors = value);
        CreateToggle(section, "Show Weapon Hitboxes", gizmos.ShowWeaponHitboxes, value => gizmos.ShowWeaponHitboxes = value);
        CreateToggle(section, "Show Status Effect Icons", gizmos.ShowStatusEffectIcons, value => gizmos.ShowStatusEffectIcons = value);
        CreateToggle(section, "Show DPS Window", gizmos.ShowDpsWindow, value => gizmos.ShowDpsWindow = value);
    }

    private void RefreshDynamicText()
    {
        _isRefreshing = true;
        WeaponInstance current = _sandbox.CurrentManualWeapon;
        _sb.Clear();
        for (int i = 0; i < WeaponTestingSandboxManager.WeaponSlots; i++)
        {
            WeaponInstance weapon = _sandbox.GetWeaponInSlot(i);
            _sb.Append("Equipped Weapon Slot ").Append(i + 1).Append(": ").AppendLine(weapon?.Data != null ? weapon.Data.DisplayName : "None");
        }
        _sb.Append("Current Manual Weapon: ").AppendLine(current?.Data != null ? current.Data.DisplayName : "None");
        _sb.Append("Current Weapon State: ").AppendLine(current != null ? current.State.ToString() : "None");
        _sb.Append("Current Ammo: ").AppendLine(current != null ? current.CurrentAmmo.ToString("0.#") : "0");
        _sb.Append("Max Ammo: ").AppendLine(current != null ? _sandbox.GetMaxAmmo(current).ToString("0.#") : "0");
        _sb.Append("Current Weapon Level: ").AppendLine(current != null ? current.Level.ToString() : "-");
        _sb.Append("Current Upgrade Path: ").AppendLine(FormatPath(current));
        if (_loadoutText != null)
            _loadoutText.text = _sb.ToString();

        if (_heatText != null)
            _heatText.text = $"Current Heat %: {_sandbox.HeatOverride.NormalizedHeat * 100f:0.#}";
        if (_heatSlider != null)
            _heatSlider.value = _sandbox.HeatOverride.NormalizedHeat * 100f;

        WeaponStatOverride stats = _sandbox.StatOverride;
        if (_statsText != null && stats != null)
        {
            _statsText.text =
                $"Damage {stats.DamageMultiplier:0.##} | Elite {stats.EliteDamageMultiplier:0.##}\n" +
                $"Attack Speed {stats.AttackSpeedMultiplier:0.##} | Size {stats.ProjectileAreaSizeMultiplier:0.##}\n" +
                $"Crit {stats.CriticalChance:P0} x{stats.CriticalDamageMultiplier:0.##} | Knockback {stats.KnockbackMultiplier:0.##} | Ammo {stats.AmmoMultiplier:0.##}";
        }

        WeaponTestMetrics metrics = _sandbox.Metrics;
        if (_metricsText != null && metrics != null)
        {
            _metricsText.text =
                $"Total Damage Dealt: {metrics.TotalDamage:0.#}\n" +
                $"Damage Per Second: {metrics.DamagePerSecond:0.#}\n" +
                $"Damage Per Shot: {metrics.DamagePerShot:0.#}\n" +
                $"Critical Hits: {metrics.CriticalHits}\n" +
                $"Critical Hit Rate: {metrics.CriticalHitRate:P1}\n" +
                $"Enemies Killed: {metrics.EnemiesKilled}\n" +
                $"Average Time To Kill: {metrics.AverageTimeToKill:0.###}s\n" +
                $"Ammo Spent: {metrics.AmmoConsumed}\n" +
                $"Damage Per Ammo: {metrics.DamagePerAmmo:0.###}\n" +
                $"Active Ability Uses: {metrics.ActiveAbilityUses}\n" +
                $"Knockback Distance: {metrics.AverageKnockbackDistance:0.###}m\n" +
                $"Status Effects Applied: {metrics.StatusEffectsApplied}";
        }
        _isRefreshing = false;
    }

    private void RefreshAllStaticSelections()
    {
        if (_slotDropdowns == null)
            return;

        _isRefreshing = true;
        for (int i = 0; i < _slotDropdowns.Length; i++)
        {
            WeaponInstance weapon = _sandbox.GetWeaponInSlot(i);
            _slotDropdowns[i].value = weapon?.Data == null ? 0 : DropdownFromWeaponType(weapon.Data.WeaponType);
        }
        RefreshLevelPathSelections();
        _isRefreshing = false;
    }

    private void RefreshLevelPathSelections()
    {
        if (_levelSlotDropdown == null || _levelDropdown == null || _pathDropdown == null)
            return;

        WeaponInstance weapon = _sandbox.GetWeaponInSlot(_levelSlotDropdown.value);
        _isRefreshing = true;
        _levelDropdown.value = weapon != null ? Mathf.Clamp(weapon.Level, 1, 10) - 1 : 0;
        _pathDropdown.value = weapon != null ? weapon.SelectedPath switch
        {
            WeaponUpgradePath.PathA => 1,
            WeaponUpgradePath.PathB => 2,
            _ => 0
        } : 0;
        _isRefreshing = false;
    }

    private void ApplySelectedLevelPath()
    {
        if (_isRefreshing)
            return;

        int slot = _levelSlotDropdown.value;
        int level = _levelDropdown.value + 1;
        WeaponUpgradePath path = _pathDropdown.value switch
        {
            1 => WeaponUpgradePath.PathA,
            2 => WeaponUpgradePath.PathB,
            _ => WeaponUpgradePath.None
        };
        _sandbox.ApplyWeaponLevelAndPath(slot, level, path);
        RefreshLevelPathSelections();
    }

    private void SetHeat(float percent)
    {
        _sandbox.HeatOverride.SetHeatPercent(percent);
        RefreshDynamicText();
    }

    private void RebuildUi()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        BuildUi();
        RefreshAllStaticSelections();
    }

    private Transform CreateSection(Transform parent, string title)
    {
        GameObject section = CreateUiObject(title, parent);
        Image image = section.AddComponent<Image>();
        image.color = new Color(0.12f, 0.13f, 0.15f, 0.92f);
        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        CreateText(section.transform, title + "Title", title, 20, TextAlignmentOptions.Left);
        return section.transform;
    }

    private Transform CreateRow(Transform parent, string name)
    {
        GameObject row = CreateUiObject(name, parent);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = row.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return row.transform;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = CreateUiObject(name, parent);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = CreateUiObject(label, parent);
        Image image = go.AddComponent<Image>();
        image.sprite = GetWhiteSprite();
        image.color = new Color(0.24f, 0.28f, 0.32f, 1f);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        TextMeshProUGUI text = CreateText(go.transform, "Label", label, 14, TextAlignmentOptions.Center);
        RectTransform textRt = text.rectTransform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(4f, 2f);
        textRt.offsetMax = new Vector2(-4f, -2f);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 30f;
        return button;
    }

    private TMP_Dropdown CreateDropdown(Transform parent, string label, List<string> options, int value, UnityEngine.Events.UnityAction<int> onChanged)
    {
        Transform row = CreateRow(parent, label + "Row");
        CreateText(row, label + "Label", label, 14, TextAlignmentOptions.Left);
        GameObject go = CreateUiObject(label, row);
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.2f, 0.23f, 1f);
        TMP_Dropdown dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.options.Clear();
        for (int i = 0; i < options.Count; i++)
            dropdown.options.Add(new TMP_Dropdown.OptionData(options[i]));
        dropdown.value = Mathf.Clamp(value, 0, Mathf.Max(0, options.Count - 1));
        dropdown.onValueChanged.AddListener(onChanged);
        TextMeshProUGUI caption = CreateText(go.transform, "Label", "", 14, TextAlignmentOptions.Center);
        Stretch(caption.rectTransform, new Vector2(8f, 2f), new Vector2(-24f, -2f));
        dropdown.captionText = caption;
        dropdown.template = BuildDropdownTemplate(go.transform, out TextMeshProUGUI itemLabel);
        dropdown.itemText = itemLabel;
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 30f;
        layout.preferredWidth = 220f;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private Slider CreateSlider(Transform parent, string label, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        Transform row = CreateRow(parent, label + "Row");
        CreateText(row, label + "Label", label, 14, TextAlignmentOptions.Left);
        GameObject go = CreateUiObject(label, row);
        Slider slider = go.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChanged);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(220f, 24f);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 24f;
        layout.preferredWidth = 220f;
        BuildSliderVisuals(slider);
        return slider;
    }

    private void CreateStatSlider(Transform parent, string label, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        TextMeshProUGUI valueLabel = null;
        Slider slider = CreateSlider(parent, label, min, max, value, sliderValue =>
        {
            valueLabel.text = sliderValue.ToString("0.##");
            onChanged(sliderValue);
        });
        valueLabel = CreateText(slider.transform.parent, label + "Value", value.ToString("0.##"), 14, TextAlignmentOptions.Right);
    }

    private void CreateToggle(Transform parent, string label, bool value, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        Transform row = CreateRow(parent, label + "Row");
        GameObject go = CreateUiObject(label, row);
        Toggle toggle = go.AddComponent<Toggle>();
        Image background = go.AddComponent<Image>();
        background.sprite = GetWhiteSprite();
        background.color = new Color(0.18f, 0.2f, 0.23f, 1f);
        GameObject checkmark = CreateUiObject("Checkmark", go.transform);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.sprite = GetWhiteSprite();
        checkImage.color = new Color(0.25f, 0.85f, 0.45f, 1f);
        RectTransform checkRt = checkmark.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.2f, 0.2f);
        checkRt.anchorMax = new Vector2(0.8f, 0.8f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        toggle.targetGraphic = background;
        toggle.graphic = checkImage;
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(onChanged);
        LayoutElement toggleLayout = go.AddComponent<LayoutElement>();
        toggleLayout.minWidth = 24f;
        toggleLayout.preferredWidth = 24f;
        toggleLayout.minHeight = 24f;
        CreateText(row, label + "Label", label, 14, TextAlignmentOptions.Left);
    }

    private void CreateIntField(Transform parent, string label, int value, System.Action<int> onChanged)
    {
        CreateInputField(parent, label, value.ToString(), text =>
        {
            if (int.TryParse(text, out int parsed))
                onChanged(parsed);
        });
    }

    private void CreateFloatField(Transform parent, string label, float value, System.Action<float> onChanged)
    {
        CreateInputField(parent, label, value.ToString("0.##"), text =>
        {
            if (float.TryParse(text, out float parsed))
                onChanged(parsed);
        });
    }

    private void CreateInputField(Transform parent, string label, string value, UnityEngine.Events.UnityAction<string> onChanged)
    {
        Transform row = CreateRow(parent, label + "Row");
        CreateText(row, label + "Label", label, 14, TextAlignmentOptions.Left);
        GameObject go = CreateUiObject(label, row);
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.2f, 0.23f, 1f);
        TMP_InputField field = go.AddComponent<TMP_InputField>();
        TextMeshProUGUI text = CreateText(go.transform, "Text", value, 14, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, new Vector2(6f, 2f), new Vector2(-6f, -2f));
        field.textComponent = text;
        field.text = value;
        field.onEndEdit.AddListener(onChanged);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 30f;
        layout.preferredWidth = 180f;
    }

    private void BuildSliderVisuals(Slider slider)
    {
        GameObject background = CreateUiObject("Background", slider.transform);
        Image bg = background.AddComponent<Image>();
        bg.sprite = GetWhiteSprite();
        bg.color = new Color(0.12f, 0.13f, 0.15f, 1f);
        RectTransform bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.35f);
        bgRt.anchorMax = new Vector2(1f, 0.65f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        GameObject fillArea = CreateUiObject("Fill Area", slider.transform);
        RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = Vector2.zero;
        fillAreaRt.offsetMax = Vector2.zero;

        GameObject fill = CreateUiObject("Fill", fillArea.transform);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.sprite = GetWhiteSprite();
        fillImage.color = new Color(0.2f, 0.65f, 1f, 1f);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        GameObject handle = CreateUiObject("Handle", slider.transform);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.sprite = GetWhiteSprite();
        handleImage.color = Color.white;
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(12f, 24f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImage;
    }

    private RectTransform BuildDropdownTemplate(Transform parent, out TextMeshProUGUI itemLabel)
    {
        GameObject template = CreateUiObject("Template", parent);
        template.SetActive(false);
        Image templateImage = template.AddComponent<Image>();
        templateImage.color = new Color(0.08f, 0.09f, 0.1f, 0.98f);
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        RectTransform templateRt = template.GetComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, -2f);
        templateRt.sizeDelta = new Vector2(0f, 180f);

        GameObject viewport = CreateUiObject("Viewport", template.transform);
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        Stretch(viewportRt, Vector2.zero, Vector2.zero);
        scrollRect.viewport = viewportRt;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentRt;

        GameObject item = CreateUiObject("Item", content.transform);
        Toggle toggle = item.AddComponent<Toggle>();
        Image itemBg = item.AddComponent<Image>();
        itemBg.color = new Color(0.16f, 0.18f, 0.2f, 1f);
        toggle.targetGraphic = itemBg;
        LayoutElement itemLayout = item.AddComponent<LayoutElement>();
        itemLayout.minHeight = 28f;

        GameObject checkmark = CreateUiObject("Item Checkmark", item.transform);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.sprite = GetWhiteSprite();
        checkImage.color = new Color(0.25f, 0.85f, 0.45f, 1f);
        RectTransform checkRt = checkmark.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0f, 0.25f);
        checkRt.anchorMax = new Vector2(0f, 0.75f);
        checkRt.pivot = new Vector2(0f, 0.5f);
        checkRt.anchoredPosition = new Vector2(8f, 0f);
        checkRt.sizeDelta = new Vector2(14f, 0f);
        toggle.graphic = checkImage;

        itemLabel = CreateText(item.transform, "Item Label", "", 14, TextAlignmentOptions.Left);
        Stretch(itemLabel.rectTransform, new Vector2(30f, 2f), new Vector2(-6f, -2f));
        return templateRt;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static Sprite GetWhiteSprite()
    {
        Texture2D tex = Texture2D.whiteTexture;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static List<string> WeaponOptions()
    {
        return new List<string> { "None", "Flamethrower", "Rocket Launcher", "Mortar", "Automatic Cannon", "Rotating Blade" };
    }

    private static List<string> LevelOptions()
    {
        List<string> levels = new();
        for (int i = 1; i <= 10; i++)
            levels.Add(i.ToString());
        return levels;
    }

    private static WeaponType? WeaponTypeFromDropdown(int value)
    {
        return value switch
        {
            1 => WeaponType.Flamethrower,
            2 => WeaponType.RocketLauncher,
            3 => WeaponType.Mortar,
            4 => WeaponType.AutomaticCannon,
            5 => WeaponType.RotatingBlade,
            _ => null
        };
    }

    private static int DropdownFromWeaponType(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.Flamethrower => 1,
            WeaponType.RocketLauncher => 2,
            WeaponType.Mortar => 3,
            WeaponType.AutomaticCannon => 4,
            WeaponType.RotatingBlade => 5,
            _ => 0
        };
    }

    private static string FormatPath(WeaponInstance weapon)
    {
        if (weapon == null || weapon.SelectedPath == WeaponUpgradePath.None || !weapon.HasAdvancedPath)
            return "None";
        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);
        if (pathData != null && !string.IsNullOrWhiteSpace(pathData.PathName))
            return pathData.PathName;
        return weapon.SelectedPath.ToString();
    }
}
