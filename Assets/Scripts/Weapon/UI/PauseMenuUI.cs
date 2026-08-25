using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PauseMenuUI : MonoBehaviour
{
    private const int PauseCanvasSortingOrder = 32760;

    // Pause-local presentation values. Keep these scoped here so changing the pause
    // presentation cannot unexpectedly restyle the gameplay HUD or run-end screen.
    private static readonly Color CoalOverlay = new(0.018f, 0.026f, 0.022f, 0.88f);
    private static readonly Color DeepSteel = new(0.067f, 0.078f, 0.075f, 0.97f);
    private static readonly Color Plate = new(0.122f, 0.145f, 0.133f, 1f);
    private static readonly Color Bone = new(0.949f, 0.961f, 0.922f, 1f);
    private static readonly Color MutedSteel = new(0.678f, 0.741f, 0.69f, 1f);
    private static readonly Color ScrapGreen = new(0.659f, 0.78f, 0.561f, 1f);
    private static readonly Color WarningRust = new(0.851f, 0.416f, 0.196f, 1f);
    private static readonly Color Danger = new(0.78f, 0.29f, 0.263f, 1f);

    [SerializeField] private PlayerStats _playerStats;
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private LevelUpChoiceUI _levelUpChoiceUi;
    [SerializeField] private CraftingUI _craftingUi;
    [SerializeField] private ThirdPersonCamera _camera;
    [SerializeField] private UserSettingsService _settingsService;
    [SerializeField] private WeaponSandboxDebugUI _sandboxDebugUi;

    private GameObject _root;
    private GameObject _mainActionPanel;
    private GameObject _settingsPanel;
    private TextMeshProUGUI _statsText;
    private TextMeshProUGUI _runStatsText;
    private Button _resumeButton;
    private Button _settingsButton;
    private Button _quitButton;
    private Button _settingsBackButton;
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
    private bool _missingSettingsServiceReported;

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
        BindSettingsService(_settingsService != null ? _settingsService : UserSettingsService.Instance);
        _root.SetActive(false);
    }

    private void OnEnable()
    {
        UserSettingsService.InstanceChanged -= HandleSettingsServiceInstanceChanged;
        UserSettingsService.InstanceChanged += HandleSettingsServiceInstanceChanged;
        PresentationAccessibilityRuntime.Changed -= HandleAccessibilityChanged;
        PresentationAccessibilityRuntime.Changed += HandleAccessibilityChanged;
        if (_root != null)
            BindSettingsService(_settingsService != null ? _settingsService : UserSettingsService.Instance);
    }

    private void OnDisable()
    {
        UserSettingsService.InstanceChanged -= HandleSettingsServiceInstanceChanged;
        PresentationAccessibilityRuntime.Changed -= HandleAccessibilityChanged;
        if (_settingsService != null)
            _settingsService.Changed -= HandleSettingsChanged;
    }

    private void Update()
    {
        if (_isPaused)
            RefreshRunStats();

        if (!WasEscapePressed())
            return;

        if (_isPaused)
        {
            HandlePauseCancel();
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
        if (_playerHealth == null && _playerStats != null)
            _playerHealth = _playerStats.GetComponent<PlayerHealth>();
        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (_levelUpChoiceUi == null)
            _levelUpChoiceUi = FindAnyObjectByType<LevelUpChoiceUI>();
        if (_craftingUi == null)
            _craftingUi = FindAnyObjectByType<CraftingUI>();
        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
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
        SetSettingsView(false, false);
        SetPauseState(true, 0f);
        SyncSettingsFromSources();
        RefreshStats();
        FocusSelectable(_resumeButton);
    }

    private void Resume()
    {
        SetSettingsView(false, false);
        SetPauseState(false, _savedTimeScale > 0.001f ? _savedTimeScale : 1f);
    }

    private void OpenSettings()
    {
        SetSettingsView(true, true);
        SyncSettingsFromSources();
    }

    private void CloseSettings()
    {
        _settingsService?.FlushPendingSave();
        SetSettingsView(false, true);
    }

    private void HandlePauseCancel()
    {
        if (_settingsPanel != null && _settingsPanel.activeSelf)
        {
            CloseSettings();
            return;
        }

        Resume();
    }

    private void ReturnToTitle()
    {
        SetSettingsView(false, false);
        SetPauseState(false, 1f);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneNavigation.LoadTitle())
            return;

        SetPauseState(true, 0f);
        SyncSettingsFromSources();
        RefreshStats();
        FocusSelectable(_resumeButton);
    }

    private void SetPauseState(bool paused, float timeScale)
    {
        if (!paused)
            _settingsService?.FlushPendingSave();
        _isPaused = paused;
        if (!paused)
            ClearPauseSelection();
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
        if (_settingsService == null)
            BindSettingsService(UserSettingsService.Instance);

        if (_settingsService == null)
        {
            SetSettingsControlsInteractable(false);
        }
        else
        {
            if (_hSensSlider != null)
                _hSensSlider.SetValueWithoutNotify(_settingsService.HorizontalSensitivity);
            if (_vSensSlider != null)
                _vSensSlider.SetValueWithoutNotify(_settingsService.VerticalSensitivity);
            if (_invertYToggle != null)
                _invertYToggle.SetIsOnWithoutNotify(_settingsService.InvertY);
            if (_sfxSlider != null)
                _sfxSlider.SetValueWithoutNotify(_settingsService.SfxVolume);
            if (_musicSlider != null)
                _musicSlider.SetValueWithoutNotify(_settingsService.MusicVolume);
            SetSettingsControlsInteractable(true);
        }

        SyncAccessibilityControls(PresentationAccessibilityRuntime.Current);
    }

    private void HandleSettingsServiceInstanceChanged(UserSettingsService service)
    {
        BindSettingsService(service);
    }

    private void BindSettingsService(UserSettingsService service)
    {
        if (_settingsService != null)
            _settingsService.Changed -= HandleSettingsChanged;

        _settingsService = service;
        if (_settingsService == null)
        {
            SetSettingsControlsInteractable(false);
            SyncAccessibilityControls(PresentationAccessibilityRuntime.Current);
            if (!_missingSettingsServiceReported && Application.isPlaying)
            {
                Debug.LogError(
                    "PauseMenuUI: no authored UserSettingsService is available; settings controls were disabled.",
                    this);
                _missingSettingsServiceReported = true;
            }
            return;
        }

        _settingsService.Changed -= HandleSettingsChanged;
        _settingsService.Changed += HandleSettingsChanged;
        SyncReducedMotionToPresentationRuntime();
        SyncSettingsFromSources();
    }

    private void HandleSettingsChanged(UserSettingsChange change)
    {
        if ((change & UserSettingsChange.ReducedMotion) != 0)
            SyncReducedMotionToPresentationRuntime();
        SyncSettingsFromSources();
    }

    private void SyncReducedMotionToPresentationRuntime()
    {
        if (_settingsService == null)
            return;

        PresentationAccessibilityState current = PresentationAccessibilityRuntime.Current;
        if (current.ReducedMotion == _settingsService.ReducedMotion)
            return;

        PersistAccessibility(current.WithReducedMotion(_settingsService.ReducedMotion));
    }

    private void SetSettingsControlsInteractable(bool interactable)
    {
        if (_hSensSlider != null)
            _hSensSlider.interactable = interactable;
        if (_vSensSlider != null)
            _vSensSlider.interactable = interactable;
        if (_invertYToggle != null)
            _invertYToggle.interactable = interactable;
        if (_sfxSlider != null)
            _sfxSlider.interactable = interactable;
        if (_musicSlider != null)
            _musicSlider.interactable = interactable;
    }

    private bool TryGetSettingsService(out UserSettingsService service)
    {
        if (_settingsService == null)
            BindSettingsService(UserSettingsService.Instance);
        service = _settingsService;
        return service != null;
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

        if (_statsText == null)
            return;

        var sb = new StringBuilder();

        if (_playerHealth != null)
            AppendStatLine(sb, "HEALTH", $"{_playerHealth.CurrentHealth} / {_playerHealth.MaxHealth}");
        else
            AppendPlayerStat(sb, "MAX HEALTH", StatType.MaxHealth);

        AppendPlayerStat(sb, "MOVE SPEED", StatType.MovementSpeed);
        AppendPlayerStat(sb, "DAMAGE", StatType.DamageFlat);
        AppendPlayerStat(sb, "DASH CHARGES", StatType.DashCharges);
        AppendPlayerStat(sb, "DASH SPEED", StatType.DashSpeed);

        if (sb.Length == 0)
            sb.Append("PLAYER DATA UNAVAILABLE");

        _statsText.text = sb.ToString();
    }

    private void RefreshRunStats()
    {
        if (_runStatsText == null)
            return;

        _runStatsText.text =
            $"TIME          {RunSessionStats.FormatElapsed()}\n" +
            $"KILLS         {RunCombatStats.EnemiesEliminated}\n" +
            $"BOSS KILLS    {RunSessionStats.BossKills}";
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

        Image overlay = CreateSolidImage(_root.transform, "Overlay", CoalOverlay);
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = true;

        CreateBackgroundRail(_root.transform, "TopRail", true);
        CreateBackgroundRail(_root.transform, "BottomRail", false);
        BuildTitle();
        BuildRunStatsPanel();
        BuildPlayerStatsPanel();
        BuildMainActionPanel();
        BuildSettingsPanel();
        SetSettingsView(false, false);
    }

    private void BuildTitle()
    {
        Image titlePlate = CreateIndustrialPanel(_root.transform, "PauseTitlePlate", new Vector2(440f, 90f), WarningRust);
        SetAnchoredRect(titlePlate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(440f, 90f), new Vector2(0.5f, 1f));

        TextMeshProUGUI title = CreateLabel(titlePlate.transform, "Title", "PAUSED", 48f, TextAlignmentOptions.Center, Bone);
        Stretch(title.rectTransform, new Vector2(20f, 12f), new Vector2(-20f, -12f));
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 7f;
    }

    private void BuildMainActionPanel()
    {
        Image panel = CreateIndustrialPanel(_root.transform, "MainActionPanel", new Vector2(430f, 360f), ScrapGreen);
        _mainActionPanel = panel.gameObject;
        SetAnchoredRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -24f), new Vector2(430f, 360f));

        TextMeshProUGUI header = CreateLabel(panel.transform, "Header", "SYSTEM PAUSED", 16f, TextAlignmentOptions.Center, MutedSteel);
        SetAnchoredRect(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(360f, 30f), new Vector2(0.5f, 1f));
        header.characterSpacing = 5f;

        _resumeButton = CreateIndustrialButton(panel.transform, "ResumeButton", "RESUME", new Vector2(330f, 64f), ScrapGreen);
        SetAnchoredRect((RectTransform)_resumeButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 82f), new Vector2(330f, 64f));
        _resumeButton.onClick.AddListener(Resume);

        _settingsButton = CreateIndustrialButton(panel.transform, "SettingsButton", "SETTINGS", new Vector2(330f, 64f), WarningRust);
        SetAnchoredRect((RectTransform)_settingsButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(330f, 64f));
        _settingsButton.onClick.AddListener(OpenSettings);

        _quitButton = CreateIndustrialButton(panel.transform, "QuitButton", "QUIT", new Vector2(330f, 64f), Danger);
        SetAnchoredRect((RectTransform)_quitButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -82f), new Vector2(330f, 64f));
        _quitButton.onClick.AddListener(ReturnToTitle);

        ConfigureVerticalNavigation(_resumeButton, _settingsButton, _quitButton);
    }

    private void BuildRunStatsPanel()
    {
        Image panel = CreateIndustrialPanel(_root.transform, "RunStatsPanel", new Vector2(380f, 440f), WarningRust);
        SetAnchoredRect(panel.rectTransform, new Vector2(0.2f, 0.5f), new Vector2(0f, -20f), new Vector2(380f, 440f));

        TextMeshProUGUI header = CreatePanelHeader(panel.transform, "RUN STATS");
        header.characterSpacing = 4f;

        GameObject runGo = new("RunStats", typeof(RectTransform));
        runGo.transform.SetParent(panel.transform, false);
        RectTransform runRt = runGo.GetComponent<RectTransform>();
        Stretch(runRt, new Vector2(34f, 34f), new Vector2(-34f, -94f));
        _runStatsText = runGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_runStatsText);
        _runStatsText.fontSize = 22f;
        _runStatsText.fontStyle = FontStyles.Bold;
        _runStatsText.alignment = TextAlignmentOptions.TopLeft;
        _runStatsText.color = Bone;
        _runStatsText.lineSpacing = 34f;
        _runStatsText.raycastTarget = false;
    }

    private void BuildPlayerStatsPanel()
    {
        Image panel = CreateIndustrialPanel(_root.transform, "PlayerStatsPanel", new Vector2(380f, 440f), ScrapGreen);
        SetAnchoredRect(panel.rectTransform, new Vector2(0.8f, 0.5f), new Vector2(0f, -20f), new Vector2(380f, 440f));

        TextMeshProUGUI header = CreatePanelHeader(panel.transform, "PLAYER STATS");
        header.characterSpacing = 4f;

        GameObject contentGo = new("StatsContent", typeof(RectTransform));
        contentGo.transform.SetParent(panel.transform, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        Stretch(contentRt, new Vector2(34f, 34f), new Vector2(-34f, -94f));

        _statsText = contentGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_statsText);
        _statsText.fontSize = 20f;
        _statsText.fontStyle = FontStyles.Bold;
        _statsText.alignment = TextAlignmentOptions.TopLeft;
        _statsText.color = Bone;
        _statsText.lineSpacing = 18f;
        _statsText.textWrappingMode = TextWrappingModes.NoWrap;
        _statsText.overflowMode = TextOverflowModes.Overflow;
        _statsText.raycastTarget = false;
    }

    private void BuildSettingsPanel()
    {
        Image panel = CreateIndustrialPanel(_root.transform, "SettingsPanel", new Vector2(650f, 900f), ScrapGreen);
        _settingsPanel = panel.gameObject;
        SetAnchoredRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(650f, 900f));

        TextMeshProUGUI header = CreatePanelHeader(panel.transform, "SETTINGS");
        header.characterSpacing = 5f;

        float y = -94f;
        _hSensSlider = CreateSettingRow(
            panel.transform,
            "HORIZONTAL SENSITIVITY",
            ref y,
            UserSettingsData.MinimumSensitivity,
            UserSettingsData.MaximumSensitivity,
            UserSettingsData.DefaultHorizontalSensitivity,
            v =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.HorizontalSensitivity = v;
        });
        _vSensSlider = CreateSettingRow(
            panel.transform,
            "VERTICAL SENSITIVITY",
            ref y,
            UserSettingsData.MinimumSensitivity,
            UserSettingsData.MaximumSensitivity,
            UserSettingsData.DefaultVerticalSensitivity,
            v =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.VerticalSensitivity = v;
        });

        _invertYToggle = CreateToggleRow(panel.transform, "INVERT Y", ref y, on =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.InvertY = on;
        });

        _sfxSlider = CreateSettingRow(
            panel.transform,
            "SFX VOLUME",
            ref y,
            0f,
            1f,
            UserSettingsData.DefaultSfxVolume,
            v =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.SfxVolume = v;
        });
        _musicSlider = CreateSettingRow(
            panel.transform,
            "MUSIC VOLUME",
            ref y,
            0f,
            1f,
            UserSettingsData.DefaultMusicVolume,
            v =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.MusicVolume = v;
        });

        CreateSectionHeader(panel.transform, "ACCESSIBILITY", ref y);
        _reducedMotionToggle = CreateToggleRow(panel.transform, "Reduced Motion", ref y, on =>
        {
            if (TryGetSettingsService(out UserSettingsService settings))
                settings.ReducedMotion = on;

            PresentationAccessibilityState current = PresentationAccessibilityRuntime.Current;
            if (current.ReducedMotion != on)
                PersistAccessibility(current.WithReducedMotion(on));
        });
        _reducedShakeToggle = CreateToggleRow(panel.transform, "Reduced Shake", ref y, on =>
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithReducedShake(on)));
        _reducedFlashToggle = CreateToggleRow(panel.transform, "Reduced Flash", ref y, on =>
            PersistAccessibility(PresentationAccessibilityRuntime.Current.WithReducedFlash(on)));
        _combatTextModeDropdown = CreateCombatTextModeRow(panel.transform, ref y, value =>
        {
            CombatTextMode mode = (CombatTextMode)Mathf.Clamp(
                value,
                (int)CombatTextMode.Off,
                (int)CombatTextMode.Full);
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

        _settingsBackButton = CreateIndustrialButton(panel.transform, "BackButton", "BACK", new Vector2(280f, 58f), WarningRust);
        SetAnchoredRect((RectTransform)_settingsBackButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(280f, 58f), new Vector2(0.5f, 0f));
        _settingsBackButton.onClick.AddListener(CloseSettings);

        ConfigureVerticalNavigation(
            _hSensSlider,
            _vSensSlider,
            _invertYToggle,
            _sfxSlider,
            _musicSlider,
            _reducedMotionToggle,
            _reducedShakeToggle,
            _reducedFlashToggle,
            _combatTextModeDropdown,
            _combatTextScaleSlider,
            _settingsBackButton);
        _settingsPanel.SetActive(false);
    }

    private void SetSettingsView(bool showSettings, bool updateFocus)
    {
        if (_mainActionPanel != null)
            _mainActionPanel.SetActive(!showSettings);
        if (_settingsPanel != null)
            _settingsPanel.SetActive(showSettings);

        if (!updateFocus || _root == null || !_root.activeInHierarchy)
            return;

        if (showSettings)
        {
            Selectable firstSetting = _hSensSlider != null && _hSensSlider.IsInteractable()
                ? _hSensSlider
                : _reducedMotionToggle != null && _reducedMotionToggle.IsInteractable()
                    ? _reducedMotionToggle
                    : _settingsBackButton;
            FocusSelectable(firstSetting);
        }
        else
        {
            FocusSelectable(_settingsButton != null && _settingsButton.gameObject.activeInHierarchy
                ? _settingsButton
                : _resumeButton);
        }
    }

    private void AppendPlayerStat(StringBuilder builder, string label, StatType statType)
    {
        if (_playerStats == null || _playerStats.GetDefinition(statType) == null)
            return;

        AppendStatLine(builder, label, _playerStats.GetStat(statType).ToString("0.##"));
    }

    private static void AppendStatLine(StringBuilder builder, string label, string value)
    {
        builder.Append(label.PadRight(15));
        builder.AppendLine(value);
    }

    private static void CreateSectionHeader(Transform parent, string text, ref float y)
    {
        TextMeshProUGUI header = CreateLabel(
            parent,
            text + "Header",
            text,
            17f,
            TextAlignmentOptions.TopLeft,
            MutedSteel);
        SetAnchoredRect(
            header.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0f, y),
            new Vector2(540f, 30f),
            new Vector2(0.5f, 1f));
        header.fontStyle = FontStyles.Bold;
        header.characterSpacing = 3f;

        Image divider = CreateSolidImage(parent, text + "Divider", WarningRust);
        SetAnchoredRect(
            divider.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0f, y - 28f),
            new Vector2(540f, 2f),
            new Vector2(0.5f, 1f));
        y -= 36f;
    }

    private static TextMeshProUGUI CreateSettingValueLabel(Slider slider, string value)
    {
        if (slider == null)
            return null;

        TextMeshProUGUI label = CreateLabel(
            slider.transform.parent,
            "Value",
            value,
            14f,
            TextAlignmentOptions.TopRight,
            ScrapGreen);
        SetAnchoredRect(
            label.rectTransform,
            new Vector2(1f, 1f),
            Vector2.zero,
            new Vector2(140f, 24f),
            new Vector2(1f, 1f));
        label.fontStyle = FontStyles.Bold;
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
        SetAnchoredRect(
            rowRt,
            new Vector2(0.5f, 1f),
            new Vector2(0f, y),
            new Vector2(540f, 48f),
            new Vector2(0.5f, 1f));

        TextMeshProUGUI label = CreateLabel(
            row.transform,
            "Label",
            "COMBAT TEXT",
            15f,
            TextAlignmentOptions.MidlineLeft,
            Bone);
        SetAnchoredRect(
            label.rectTransform,
            new Vector2(0f, 0.5f),
            Vector2.zero,
            new Vector2(220f, 48f),
            new Vector2(0f, 0.5f));
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 2f;

        var dropdownGo = new GameObject("Dropdown", typeof(RectTransform));
        dropdownGo.transform.SetParent(row.transform, false);
        RectTransform dropdownRt = dropdownGo.GetComponent<RectTransform>();
        SetAnchoredRect(
            dropdownRt,
            new Vector2(1f, 0.5f),
            Vector2.zero,
            new Vector2(300f, 38f),
            new Vector2(1f, 0.5f));

        Image background = dropdownGo.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = Color.white;

        TMP_Dropdown dropdown = dropdownGo.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = background;
        ColorBlock colors = dropdown.colors;
        colors.normalColor = Plate;
        colors.highlightedColor = ScrapGreen;
        colors.selectedColor = ScrapGreen;
        colors.pressedColor = WarningRust;
        colors.disabledColor = new Color(0.45f, 0.5f, 0.47f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        dropdown.colors = colors;
        dropdown.options.Clear();
        dropdown.options.Add(new TMP_Dropdown.OptionData("Off"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Important Only"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Full"));

        TextMeshProUGUI caption = CreateLabel(
            dropdownGo.transform,
            "Caption",
            string.Empty,
            14f,
            TextAlignmentOptions.MidlineLeft,
            Bone);
        Stretch(caption.rectTransform, new Vector2(12f, 2f), new Vector2(-34f, -2f));
        dropdown.captionText = caption;

        TextMeshProUGUI arrow = CreateLabel(
            dropdownGo.transform,
            "Arrow",
            "V",
            13f,
            TextAlignmentOptions.Center,
            MutedSteel);
        SetAnchoredRect(
            arrow.rectTransform,
            new Vector2(1f, 0.5f),
            new Vector2(-8f, 0f),
            new Vector2(24f, 38f),
            new Vector2(1f, 0.5f));

        dropdown.template = BuildCombatTextDropdownTemplate(dropdownGo.transform, out TextMeshProUGUI itemLabel);
        dropdown.itemText = itemLabel;
        dropdown.SetValueWithoutNotify((int)CombatTextMode.Full);
        dropdown.onValueChanged.AddListener(onChanged);
        dropdown.RefreshShownValue();

        y -= 54f;
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
        templateImage.color = DeepSteel;
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

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
        itemBackground.color = Color.white;
        Toggle itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        ColorBlock itemColors = itemToggle.colors;
        itemColors.normalColor = Plate;
        itemColors.highlightedColor = ScrapGreen;
        itemColors.selectedColor = ScrapGreen;
        itemColors.pressedColor = WarningRust;
        itemColors.colorMultiplier = 1f;
        itemColors.fadeDuration = 0.08f;
        itemToggle.colors = itemColors;
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
        checkImage.color = ScrapGreen;
        itemToggle.graphic = checkImage;

        itemLabel = CreateLabel(
            item.transform,
            "Item Label",
            string.Empty,
            14f,
            TextAlignmentOptions.MidlineLeft,
            Bone);
        RectTransform itemLabelRt = itemLabel.GetComponent<RectTransform>();
        itemLabelRt.offsetMin = new Vector2(30f, 2f);
        itemLabelRt.offsetMax = new Vector2(-6f, -2f);
        return templateRt;
    }

    private static Slider CreateSettingRow(
        Transform parent,
        string label,
        ref float y,
        float min,
        float max,
        float defaultValue,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        SetAnchoredRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, 62f), new Vector2(0.5f, 1f));

        TextMeshProUGUI lbl = CreateLabel(row.transform, "Label", label, 15f, TextAlignmentOptions.TopLeft, Bone);
        SetAnchoredRect(lbl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(540f, 24f), new Vector2(0f, 1f));
        lbl.fontStyle = FontStyles.Bold;
        lbl.characterSpacing = 2f;

        Slider slider = CreateIndustrialSlider(row.transform, "Slider", new Vector2(540f, 24f), min, max, defaultValue);
        SetAnchoredRect((RectTransform)slider.transform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(540f, 24f), new Vector2(0.5f, 0f));
        slider.onValueChanged.AddListener(onChanged);

        y -= 68f;
        return slider;
    }

    private static Toggle CreateToggleRow(Transform parent, string label, ref float y, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        SetAnchoredRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, 48f), new Vector2(0.5f, 1f));

        var toggleGo = new GameObject("Toggle", typeof(RectTransform));
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.GetComponent<RectTransform>();
        SetAnchoredRect(toggleRt, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(0f, 0.5f));

        var bg = toggleGo.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = Color.white;

        var checkGo = new GameObject("Check", typeof(RectTransform));
        checkGo.transform.SetParent(toggleGo.transform, false);
        var checkRt = checkGo.GetComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(6f, 6f);
        checkRt.offsetMax = new Vector2(-6f, -6f);
        var check = checkGo.AddComponent<Image>();
        check.sprite = HudUiFactory.WhiteSprite;
        check.color = ScrapGreen;
        check.raycastTarget = false;

        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.graphic = check;
        ColorBlock colors = toggle.colors;
        colors.normalColor = Plate;
        colors.highlightedColor = ScrapGreen;
        colors.selectedColor = ScrapGreen;
        colors.pressedColor = WarningRust;
        colors.disabledColor = new Color(0.45f, 0.5f, 0.47f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        toggle.colors = colors;
        toggle.onValueChanged.AddListener(onChanged);

        TextMeshProUGUI lbl = CreateLabel(row.transform, "Label", label, 15f, TextAlignmentOptions.MidlineLeft, Bone);
        Stretch(lbl.rectTransform, new Vector2(48f, 0f), Vector2.zero);
        lbl.fontStyle = FontStyles.Bold;
        lbl.characterSpacing = 2f;

        y -= 54f;
        return toggle;
    }

    private static Slider CreateIndustrialSlider(Transform parent, string name, Vector2 size, float min, float max, float value)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;

        Image background = CreateSolidImage(go.transform, "Background", Plate);
        Stretch(background.rectTransform);

        var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGo.transform.SetParent(go.transform, false);
        RectTransform fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
        Stretch(fillAreaRt, new Vector2(5f, 5f), new Vector2(-5f, -5f));

        Image fill = CreateSolidImage(fillAreaGo.transform, "Fill", ScrapGreen);
        Stretch(fill.rectTransform);

        Image handle = CreateSolidImage(go.transform, "Handle", Color.white);
        RectTransform handleRt = handle.rectTransform;
        handleRt.sizeDelta = new Vector2(16f, 34f);

        Slider slider = go.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;

        ColorBlock colors = slider.colors;
        colors.normalColor = WarningRust;
        colors.highlightedColor = ScrapGreen;
        colors.selectedColor = ScrapGreen;
        colors.pressedColor = WarningRust;
        colors.disabledColor = new Color(0.45f, 0.5f, 0.47f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        slider.colors = colors;
        return slider;
    }

    private static Button CreateIndustrialButton(Transform parent, string name, string label, Vector2 size, Color accent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = size;

        Image plate = go.AddComponent<Image>();
        plate.sprite = HudUiFactory.WhiteSprite;
        plate.color = Color.white;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Plate;
        colors.highlightedColor = accent;
        colors.selectedColor = accent;
        colors.pressedColor = WarningRust;
        colors.disabledColor = new Color(0.4f, 0.44f, 0.42f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Image edge = CreateSolidImage(go.transform, "SelectedEdge", accent);
        RectTransform edgeRt = edge.rectTransform;
        edgeRt.anchorMin = new Vector2(0f, 0f);
        edgeRt.anchorMax = new Vector2(0f, 1f);
        edgeRt.pivot = new Vector2(0f, 0.5f);
        edgeRt.anchoredPosition = Vector2.zero;
        edgeRt.sizeDelta = new Vector2(9f, 0f);

        Image boltLeft = CreateSolidImage(go.transform, "BoltLeft", MutedSteel);
        SetAnchoredRect(boltLeft.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(8f, 8f), new Vector2(0.5f, 0.5f));
        Image boltRight = CreateSolidImage(go.transform, "BoltRight", MutedSteel);
        SetAnchoredRect(boltRight.rectTransform, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(8f, 8f), new Vector2(0.5f, 0.5f));

        TextMeshProUGUI buttonLabel = CreateLabel(go.transform, "Label", label, 25f, TextAlignmentOptions.Center, Bone);
        Stretch(buttonLabel.rectTransform, new Vector2(34f, 4f), new Vector2(-34f, -4f));
        buttonLabel.fontStyle = FontStyles.Bold;
        buttonLabel.characterSpacing = 4f;
        return button;
    }

    private static Image CreateIndustrialPanel(Transform parent, string name, Vector2 size, Color accent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        Image border = go.AddComponent<Image>();
        border.sprite = HudUiFactory.WhiteSprite;
        border.color = accent;
        border.raycastTarget = false;

        Image inner = CreateSolidImage(go.transform, "Plate", DeepSteel);
        Stretch(inner.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));

        Image topEdge = CreateSolidImage(go.transform, "TopEdge", accent);
        RectTransform topEdgeRt = topEdge.rectTransform;
        topEdgeRt.anchorMin = new Vector2(0f, 1f);
        topEdgeRt.anchorMax = new Vector2(1f, 1f);
        topEdgeRt.pivot = new Vector2(0.5f, 1f);
        topEdgeRt.anchoredPosition = new Vector2(0f, -4f);
        topEdgeRt.sizeDelta = new Vector2(-8f, 7f);

        Image corner = CreateSolidImage(go.transform, "CornerNotch", WarningRust);
        SetAnchoredRect(corner.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -19f), new Vector2(24f, 8f));
        corner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        return border;
    }

    private static TextMeshProUGUI CreatePanelHeader(Transform parent, string text)
    {
        TextMeshProUGUI header = CreateLabel(parent, "Header", text, 24f, TextAlignmentOptions.Center, Bone);
        SetAnchoredRect(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(320f, 42f), new Vector2(0.5f, 1f));
        header.fontStyle = FontStyles.Bold;

        Image divider = CreateSolidImage(parent, "HeaderDivider", WarningRust);
        SetAnchoredRect(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(280f, 3f), new Vector2(0.5f, 1f));
        return header;
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent,
        string name,
        string text,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    private static Image CreateSolidImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = HudUiFactory.WhiteSprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void CreateBackgroundRail(Transform parent, string name, bool top)
    {
        Image rail = CreateSolidImage(parent, name, new Color(WarningRust.r, WarningRust.g, WarningRust.b, 0.36f));
        RectTransform rt = rail.rectTransform;
        float anchorY = top ? 1f : 0f;
        rt.anchorMin = new Vector2(0f, anchorY);
        rt.anchorMax = new Vector2(1f, anchorY);
        rt.pivot = new Vector2(0.5f, anchorY);
        rt.anchoredPosition = new Vector2(0f, top ? -18f : 18f);
        rt.sizeDelta = new Vector2(-80f, 4f);
    }

    private static void ConfigureVerticalNavigation(params Selectable[] controls)
    {
        if (controls == null || controls.Length == 0)
            return;

        for (int i = 0; i < controls.Length; i++)
        {
            Selectable current = controls[i];
            if (current == null)
                continue;

            Navigation navigation = current.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = controls[(i - 1 + controls.Length) % controls.Length];
            navigation.selectOnDown = controls[(i + 1) % controls.Length];
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            current.navigation = navigation;
        }
    }

    private static void SetAnchoredRect(
        RectTransform rect,
        Vector2 anchor,
        Vector2 position,
        Vector2 size,
        Vector2? pivot = null)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void FocusSelectable(Selectable selectable)
    {
        if (selectable == null || !selectable.gameObject.activeInHierarchy || !selectable.IsInteractable())
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        eventSystem.SetSelectedGameObject(null);
        eventSystem.SetSelectedGameObject(selectable.gameObject);
    }

    private void ClearPauseSelection()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && _root != null && selected.transform.IsChildOf(_root.transform))
            eventSystem.SetSelectedGameObject(null);
    }
}
