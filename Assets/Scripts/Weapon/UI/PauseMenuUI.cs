using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PauseMenuUI : MonoBehaviour
{
    private const int PauseCanvasSortingOrder = 32760;

    [SerializeField] private PlayerStats _playerStats;
    [SerializeField] private LevelUpChoiceUI _levelUpChoiceUi;
    [SerializeField] private CraftingUI _craftingUi;
    [SerializeField] private ThirdPersonCamera _camera;
    [SerializeField] private AudioManager _audioManager;
    [SerializeField] private WeaponSandboxDebugUI _sandboxDebugUi;

    private GameObject _root;
    private TextMeshProUGUI _statsText;
    private TextMeshProUGUI _runStatsText;
    private Slider _hSensSlider;
    private Slider _vSensSlider;
    private Toggle _invertYToggle;
    private Slider _sfxSlider;
    private Slider _musicSlider;
    private Toggle _reducedMotionToggle;
    private Toggle _reducedShakeToggle;
    private Toggle _reducedFlashToggle;
    private TMP_Dropdown _combatTextModeDropdown;
    private Slider _combatTextScaleSlider;
    private TextMeshProUGUI _combatTextScaleLabel;
    private float _savedTimeScale = 1f;
    private bool _isPaused;

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
        _root.SetActive(false);
    }

    private void OnEnable() => PresentationAccessibilityRuntime.Changed += HandleAccessibilityChanged;

    private void OnDisable() => PresentationAccessibilityRuntime.Changed -= HandleAccessibilityChanged;

    private void Update()
    {
        if (_isPaused)
            RefreshRunStats();

        if (!WasEscapePressed())
            return;

        if (_isPaused)
        {
            Resume();
            return;
        }

        if (!CanPause())
            return;

        ShowPause();
    }

    private void ResolveRefs()
    {
        if (_playerStats == null)
            _playerStats = FindAnyObjectByType<PlayerStats>();
        if (_levelUpChoiceUi == null)
            _levelUpChoiceUi = FindAnyObjectByType<LevelUpChoiceUI>();
        if (_craftingUi == null)
            _craftingUi = FindAnyObjectByType<CraftingUI>();
        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
        if (_audioManager == null)
            _audioManager = AudioManager.Instance ?? FindAnyObjectByType<AudioManager>();
        if (_sandboxDebugUi == null)
            _sandboxDebugUi = FindAnyObjectByType<WeaponSandboxDebugUI>(FindObjectsInactive.Include);
    }

    private bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private bool CanPause()
    {
        // Player-owned UI components may be spawned after this menu's Awake.
        // Resolve again on Escape so a modal can never be bypassed by scene order.
        ResolveRefs();

        bool modalUiVisible = (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible)
            || (_craftingUi != null && _craftingUi.IsVisible);
        if (modalUiVisible)
        {
            _camera?.SetLookBlockedByUi(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false;
        }

        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return false;
        return true;
    }

    private void ShowPause()
    {
        ResolveRefs();
        _savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
        SetPauseState(true, 0f);
        SyncSettingsFromSources();
        RefreshStats();
    }

    private void Resume()
    {
        SetPauseState(false, _savedTimeScale > 0.001f ? _savedTimeScale : 1f);
    }

    private void ReturnToTitle()
    {
        SetPauseState(false, 1f);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneNavigation.LoadTitle())
            return;

        SetPauseState(true, 0f);
        SyncSettingsFromSources();
        RefreshStats();
    }

    private void SetPauseState(bool paused, float timeScale)
    {
        _isPaused = paused;
        if (_root != null)
            _root.SetActive(paused);
        Time.timeScale = timeScale;
        if (paused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        _camera?.SetLookBlockedByUi(paused);
        _sandboxDebugUi?.SetPauseMenuOpen(paused);
    }

    private void SyncSettingsFromSources()
    {
        if (_camera != null)
        {
            if (_hSensSlider != null)
                _hSensSlider.SetValueWithoutNotify(_camera.HorizontalSensitivity);
            if (_vSensSlider != null)
                _vSensSlider.SetValueWithoutNotify(_camera.VerticalSensitivity);
            if (_invertYToggle != null)
                _invertYToggle.SetIsOnWithoutNotify(_camera.InvertVertical);
        }

        if (_audioManager != null)
        {
            if (_sfxSlider != null)
                _sfxSlider.SetValueWithoutNotify(_audioManager.SfxVolume);
            if (_musicSlider != null)
                _musicSlider.SetValueWithoutNotify(_audioManager.MusicVolume);
        }

        SyncAccessibilityControls(PresentationAccessibilityRuntime.Current);
    }

    private void SyncAccessibilityControls(PresentationAccessibilityState state)
    {
        _reducedMotionToggle?.SetIsOnWithoutNotify(state.ReducedMotion);
        _reducedShakeToggle?.SetIsOnWithoutNotify(state.ReducedShake);
        _reducedFlashToggle?.SetIsOnWithoutNotify(state.ReducedFlash);
        _combatTextModeDropdown?.SetValueWithoutNotify((int)state.CombatText);
        _combatTextModeDropdown?.RefreshShownValue();
        _combatTextScaleSlider?.SetValueWithoutNotify(state.CombatTextScale);
        UpdateCombatTextScaleLabel(state.CombatTextScale);
    }

    private void HandleAccessibilityChanged(PresentationAccessibilityState state)
    {
        if (_isPaused)
            SyncAccessibilityControls(state);
    }

    private static void PersistAccessibility(PresentationAccessibilityState state)
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.SetPresentationAccessibility(state);
            return;
        }

        // Edit-mode previews and isolated testing scenes can still use the authoritative
        // runtime snapshot even when the persistent bootstrap is intentionally absent.
        PresentationAccessibilityRuntime.Apply(state);
    }

    private void UpdateCombatTextScaleLabel(float value)
    {
        if (_combatTextScaleLabel != null)
            _combatTextScaleLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void RefreshStats()
    {
        RefreshRunStats();

        if (_statsText == null || _playerStats == null)
            return;

        var sb = new StringBuilder();
        foreach (StatDefinition definition in _playerStats.GetAllDefinitions())
        {
            if (definition == null)
                continue;
            float value = _playerStats.GetStat(definition.StatType);
            sb.AppendLine($"{StatDisplayNames.GetDisplayName(definition.StatType)}: {value:0.##}");
        }

        _statsText.text = sb.ToString();
    }

    private void RefreshRunStats()
    {
        if (_runStatsText == null)
            return;

        _runStatsText.text =
            $"Time: {RunSessionStats.FormatElapsed()}\n" +
            $"Kills: {RunCombatStats.EnemiesEliminated}";
    }

    private void BuildUi()
    {
        _root = new GameObject("PauseRoot", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        Canvas pauseCanvas = _root.AddComponent<Canvas>();
        pauseCanvas.overrideSorting = true;
        pauseCanvas.sortingOrder = PauseCanvasSortingOrder;
        _root.AddComponent<GraphicRaycaster>();

        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var overlay = HudUiFactory.CreatePanel(_root.transform, "Overlay", Vector2.zero);
        var overlayRt = overlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        overlay.color = new Color(0f, 0f, 0f, 0.72f);
        overlay.raycastTarget = true;

        var title = HudUiFactory.CreateLabel(_root.transform, "Title", "PAUSED", 48f, TextAlignmentOptions.Center);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -32f);
        titleRt.sizeDelta = new Vector2(500f, 64f);
        title.fontStyle = FontStyles.Bold;

        var resumeBtn = HudUiFactory.CreateButton(_root.transform, "Resume", new Vector2(220f, 48f));
        var resumeRt = resumeBtn.GetComponent<RectTransform>();
        resumeRt.anchorMin = new Vector2(0.5f, 0.5f);
        resumeRt.anchorMax = new Vector2(0.5f, 0.5f);
        resumeRt.pivot = new Vector2(0.5f, 0.5f);
        resumeRt.anchoredPosition = new Vector2(0f, -12f);
        resumeBtn.onClick.AddListener(Resume);

        var titleBtn = HudUiFactory.CreateButton(_root.transform, "Main Menu", new Vector2(220f, 48f));
        var titleButtonRt = titleBtn.GetComponent<RectTransform>();
        titleButtonRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleButtonRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleButtonRt.pivot = new Vector2(0.5f, 0.5f);
        titleButtonRt.anchoredPosition = new Vector2(0f, -72f);
        titleBtn.onClick.AddListener(ReturnToTitle);

        BuildSettingsPanel();
        BuildStatsPanel();
    }

    private void BuildSettingsPanel()
    {
        var panel = HudUiFactory.CreatePanel(_root.transform, "SettingsPanel", new Vector2(400f, 760f));
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0.5f);
        panelRt.anchorMax = new Vector2(0f, 0.5f);
        panelRt.pivot = new Vector2(0f, 0.5f);
        panelRt.anchoredPosition = new Vector2(24f, 0f);

        var header = HudUiFactory.CreateLabel(panel.transform, "Header", "Settings", 22f, TextAlignmentOptions.TopLeft);
        var headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0f, 1f);
        headerRt.offsetMin = new Vector2(12f, -40f);
        headerRt.offsetMax = new Vector2(-12f, -8f);
        header.fontStyle = FontStyles.Bold;

        float y = -56f;
        _hSensSlider = CreateSettingRow(panel.transform, "Horizontal Sensitivity", ref y, 0.02f, 0.4f, 0.12f, v =>
        {
            if (_camera != null)
                _camera.HorizontalSensitivity = v;
        });
        _vSensSlider = CreateSettingRow(panel.transform, "Vertical Sensitivity", ref y, 0.02f, 0.4f, 0.12f, v =>
        {
            if (_camera != null)
                _camera.VerticalSensitivity = v;
        });

        _invertYToggle = CreateToggleRow(panel.transform, "Invert Y", ref y, on =>
        {
            if (_camera != null)
                _camera.InvertVertical = on;
        });

        _sfxSlider = CreateSettingRow(panel.transform, "SFX Volume", ref y, 0f, 1f, 1f, v =>
        {
            if (_audioManager != null)
                _audioManager.SfxVolume = v;
        });
        _musicSlider = CreateSettingRow(panel.transform, "Music Volume", ref y, 0f, 1f, 0.45f, v =>
        {
            if (_audioManager != null)
                _audioManager.MusicVolume = v;
        });

        CreateSectionHeader(panel.transform, "Accessibility", ref y);
        _reducedMotionToggle = CreateToggleRow(panel.transform, "Reduced Motion", ref y, on =>
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithReducedMotion(on)));
        _reducedShakeToggle = CreateToggleRow(panel.transform, "Reduced Shake", ref y, on =>
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithReducedShake(on)));
        _reducedFlashToggle = CreateToggleRow(panel.transform, "Reduced Flash", ref y, on =>
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithReducedFlash(on)));
        _combatTextModeDropdown = CreateCombatTextModeRow(panel.transform, ref y, value =>
        {
            CombatTextMode mode = (CombatTextMode)Mathf.Clamp(value, (int)CombatTextMode.Off, (int)CombatTextMode.Full);
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithCombatText(mode));
        });
        _combatTextScaleSlider = CreateSettingRow(
            panel.transform,
            "Combat Text Scale",
            ref y,
            PresentationAccessibilitySettings.MinimumCombatTextScale,
            PresentationAccessibilitySettings.MaximumCombatTextScale,
            1f,
            value =>
            {
                UpdateCombatTextScaleLabel(value);
                PersistAccessibility(PresentationAccessibilityRuntime.Current.WithCombatTextScale(value));
            });
        _combatTextScaleLabel = CreateSettingValueLabel(_combatTextScaleSlider, "100%");
    }

    private void BuildStatsPanel()
    {
        var panel = HudUiFactory.CreatePanel(_root.transform, "StatsPanel", new Vector2(380f, 720f));
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(1f, 0.5f);
        panelRt.anchorMax = new Vector2(1f, 0.5f);
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.anchoredPosition = new Vector2(-24f, 0f);

        var runHeader = HudUiFactory.CreateLabel(panel.transform, "RunHeader", "Run", 22f, TextAlignmentOptions.TopLeft);
        var runHeaderRt = runHeader.GetComponent<RectTransform>();
        runHeaderRt.anchorMin = new Vector2(0f, 1f);
        runHeaderRt.anchorMax = new Vector2(1f, 1f);
        runHeaderRt.pivot = new Vector2(0f, 1f);
        runHeaderRt.offsetMin = new Vector2(12f, -40f);
        runHeaderRt.offsetMax = new Vector2(-12f, -8f);
        runHeader.fontStyle = FontStyles.Bold;

        var runGo = new GameObject("RunStats", typeof(RectTransform));
        runGo.transform.SetParent(panel.transform, false);
        var runRt = runGo.GetComponent<RectTransform>();
        runRt.anchorMin = new Vector2(0f, 1f);
        runRt.anchorMax = new Vector2(1f, 1f);
        runRt.pivot = new Vector2(0f, 1f);
        runRt.offsetMin = new Vector2(12f, -100f);
        runRt.offsetMax = new Vector2(-12f, -48f);
        _runStatsText = runGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_runStatsText);
        _runStatsText.fontSize = 16f;
        _runStatsText.alignment = TextAlignmentOptions.TopLeft;
        _runStatsText.color = HudUiFactory.MutedTextColor;

        var header = HudUiFactory.CreateLabel(panel.transform, "StatsHeader", "Player Stats", 22f, TextAlignmentOptions.TopLeft);
        var headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0f, 1f);
        headerRt.offsetMin = new Vector2(12f, -140f);
        headerRt.offsetMax = new Vector2(-12f, -108f);
        header.fontStyle = FontStyles.Bold;

        var contentGo = new GameObject("StatsContent", typeof(RectTransform));
        contentGo.transform.SetParent(panel.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.offsetMin = new Vector2(12f, 12f);
        contentRt.offsetMax = new Vector2(-12f, -148f);

        _statsText = contentGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_statsText);
        _statsText.fontSize = 15f;
        _statsText.alignment = TextAlignmentOptions.TopLeft;
        _statsText.color = HudUiFactory.MutedTextColor;
        _statsText.enableWordWrapping = true;
        _statsText.overflowMode = TextOverflowModes.Overflow;
        _statsText.raycastTarget = false;
    }

    private static void CreateSectionHeader(Transform parent, string text, ref float y)
    {
        TextMeshProUGUI header = HudUiFactory.CreateLabel(parent, text + "Header", text, 17f, TextAlignmentOptions.TopLeft);
        RectTransform rt = header.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-24f, 30f);
        rt.offsetMin = new Vector2(12f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-12f, rt.offsetMax.y);
        header.fontStyle = FontStyles.Bold;
        header.color = HudUiFactory.MutedTextColor;
        y -= 40f;
    }

    private static TextMeshProUGUI CreateSettingValueLabel(Slider slider, string value)
    {
        if (slider == null)
            return null;

        TextMeshProUGUI label = HudUiFactory.CreateLabel(
            slider.transform.parent,
            "Value",
            value,
            14f,
            TextAlignmentOptions.TopRight);
        RectTransform rt = label.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.65f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(0f, -22f);
        rt.offsetMax = new Vector2(-12f, 0f);
        return label;
    }

    private static TMP_Dropdown CreateCombatTextModeRow(
        Transform parent,
        ref float y,
        UnityEngine.Events.UnityAction<int> onChanged)
    {
        var row = new GameObject("Combat Text", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-24f, 44f);

        TextMeshProUGUI label = HudUiFactory.CreateLabel(
            row.transform,
            "Label",
            "Combat Text",
            14f,
            TextAlignmentOptions.MidlineLeft);
        RectTransform labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(0.42f, 1f);
        labelRt.offsetMin = new Vector2(12f, 0f);
        labelRt.offsetMax = Vector2.zero;

        var dropdownGo = new GameObject("Dropdown", typeof(RectTransform));
        dropdownGo.transform.SetParent(row.transform, false);
        RectTransform dropdownRt = dropdownGo.GetComponent<RectTransform>();
        dropdownRt.anchorMin = new Vector2(0.42f, 0.12f);
        dropdownRt.anchorMax = new Vector2(1f, 0.88f);
        dropdownRt.offsetMin = Vector2.zero;
        dropdownRt.offsetMax = new Vector2(-12f, 0f);

        Image background = dropdownGo.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = HudUiFactory.EmptySlotColor;

        TMP_Dropdown dropdown = dropdownGo.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = background;
        dropdown.options.Clear();
        dropdown.options.Add(new TMP_Dropdown.OptionData("Off"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Important Only"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Full"));

        TextMeshProUGUI caption = HudUiFactory.CreateLabel(
            dropdownGo.transform,
            "Caption",
            string.Empty,
            14f,
            TextAlignmentOptions.MidlineLeft);
        RectTransform captionRt = caption.GetComponent<RectTransform>();
        captionRt.offsetMin = new Vector2(8f, 2f);
        captionRt.offsetMax = new Vector2(-28f, -2f);
        dropdown.captionText = caption;

        TextMeshProUGUI arrow = HudUiFactory.CreateLabel(
            dropdownGo.transform,
            "Arrow",
            "v",
            13f,
            TextAlignmentOptions.Center);
        RectTransform arrowRt = arrow.GetComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0f);
        arrowRt.anchorMax = new Vector2(1f, 1f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.offsetMin = new Vector2(-24f, 0f);
        arrowRt.offsetMax = Vector2.zero;

        dropdown.template = BuildCombatTextDropdownTemplate(dropdownGo.transform, out TextMeshProUGUI itemLabel);
        dropdown.itemText = itemLabel;
        dropdown.SetValueWithoutNotify((int)CombatTextMode.Full);
        dropdown.onValueChanged.AddListener(onChanged);
        dropdown.RefreshShownValue();

        y -= 56f;
        return dropdown;
    }

    private static RectTransform BuildCombatTextDropdownTemplate(
        Transform parent,
        out TextMeshProUGUI itemLabel)
    {
        var template = new GameObject("Template", typeof(RectTransform));
        template.transform.SetParent(parent, false);
        template.SetActive(false);
        RectTransform templateRt = template.GetComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, -2f);
        templateRt.sizeDelta = new Vector2(0f, 96f);

        Image templateImage = template.AddComponent<Image>();
        templateImage.sprite = HudUiFactory.WhiteSprite;
        templateImage.color = new Color(0.08f, 0.09f, 0.1f, 0.98f);
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.sprite = HudUiFactory.WhiteSprite;
        viewportImage.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        scrollRect.viewport = viewportRt;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentRt;

        var item = new GameObject("Item", typeof(RectTransform));
        item.transform.SetParent(content.transform, false);
        Image itemBackground = item.AddComponent<Image>();
        itemBackground.sprite = HudUiFactory.WhiteSprite;
        itemBackground.color = new Color(0.16f, 0.18f, 0.2f, 1f);
        Toggle itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        item.AddComponent<LayoutElement>().minHeight = 30f;

        var checkmark = new GameObject("Item Checkmark", typeof(RectTransform));
        checkmark.transform.SetParent(item.transform, false);
        RectTransform checkRt = checkmark.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0f, 0.2f);
        checkRt.anchorMax = new Vector2(0f, 0.8f);
        checkRt.pivot = new Vector2(0f, 0.5f);
        checkRt.anchoredPosition = new Vector2(8f, 0f);
        checkRt.sizeDelta = new Vector2(14f, 0f);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.sprite = HudUiFactory.WhiteSprite;
        checkImage.color = new Color(0.4f, 0.8f, 1f, 1f);
        itemToggle.graphic = checkImage;

        itemLabel = HudUiFactory.CreateLabel(
            item.transform,
            "Item Label",
            string.Empty,
            14f,
            TextAlignmentOptions.MidlineLeft);
        RectTransform itemLabelRt = itemLabel.GetComponent<RectTransform>();
        itemLabelRt.offsetMin = new Vector2(30f, 2f);
        itemLabelRt.offsetMax = new Vector2(-6f, -2f);
        return templateRt;
    }

    private static Slider CreateSettingRow(Transform parent, string label, ref float y, float min, float max, float defaultValue, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-24f, 56f);

        var lbl = HudUiFactory.CreateLabel(row.transform, "Label", label, 14f, TextAlignmentOptions.TopLeft);
        var lblRt = lbl.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0f, 1f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = new Vector2(12f, -22f);
        lblRt.offsetMax = new Vector2(-12f, 0f);

        var slider = HudUiFactory.CreateSlider(row.transform, "Slider", new Vector2(320f, 24f), min, max, defaultValue);
        var sliderRt = slider.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0f, 0f);
        sliderRt.anchorMax = new Vector2(1f, 0f);
        sliderRt.pivot = new Vector2(0.5f, 0f);
        sliderRt.anchoredPosition = new Vector2(0f, 4f);
        sliderRt.offsetMin = new Vector2(12f, 4f);
        sliderRt.offsetMax = new Vector2(-12f, 28f);
        slider.onValueChanged.AddListener(onChanged);

        y -= 64f;
        return slider;
    }

    private static Toggle CreateToggleRow(Transform parent, string label, ref float y, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-24f, 40f);

        var toggleGo = new GameObject("Toggle", typeof(RectTransform));
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.GetComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0f, 0.5f);
        toggleRt.anchorMax = new Vector2(0f, 0.5f);
        toggleRt.pivot = new Vector2(0f, 0.5f);
        toggleRt.anchoredPosition = new Vector2(12f, 0f);
        toggleRt.sizeDelta = new Vector2(24f, 24f);

        var bg = toggleGo.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = HudUiFactory.EmptySlotColor;

        var checkGo = new GameObject("Check", typeof(RectTransform));
        checkGo.transform.SetParent(toggleGo.transform, false);
        var checkRt = checkGo.GetComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(4f, 4f);
        checkRt.offsetMax = new Vector2(-4f, -4f);
        var check = checkGo.AddComponent<Image>();
        check.sprite = HudUiFactory.WhiteSprite;
        check.color = new Color(0.4f, 0.8f, 1f, 1f);

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        toggle.onValueChanged.AddListener(onChanged);

        var lbl = HudUiFactory.CreateLabel(row.transform, "Label", label, 14f, TextAlignmentOptions.MidlineLeft);
        var lblRt = lbl.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0f, 0f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = new Vector2(44f, 0f);
        lblRt.offsetMax = new Vector2(-12f, 0f);

        y -= 48f;
        return toggle;
    }
}
