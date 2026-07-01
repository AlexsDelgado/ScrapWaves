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
    private float _savedTimeScale = 1f;
    private bool _isPaused;

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
        _root.SetActive(false);
    }

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
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return false;
        if (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible)
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
        var panel = HudUiFactory.CreatePanel(_root.transform, "SettingsPanel", new Vector2(400f, 520f));
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
