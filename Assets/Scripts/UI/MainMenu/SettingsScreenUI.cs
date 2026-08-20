using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SettingsScreenUI : MonoBehaviour
{
    [Header("Authored category rail")]
    [SerializeField] private Button _controlsCategoryButton;
    [SerializeField] private Button _audioCategoryButton;
    [SerializeField] private Button _feedbackCategoryButton;
    [SerializeField] private GameObject _controlsPanel;
    [SerializeField] private GameObject _audioPanel;
    [SerializeField] private GameObject _feedbackPanel;

    [Header("Authored Controls rows")]
    [SerializeField] private Slider _horizontalSensitivitySlider;
    [SerializeField] private TMP_Text _horizontalSensitivityValue;
    [SerializeField] private Slider _verticalSensitivitySlider;
    [SerializeField] private TMP_Text _verticalSensitivityValue;
    [SerializeField] private Toggle _invertYToggle;
    [SerializeField] private TMP_Text _invertYValue;

    [Header("Authored Audio rows")]
    [SerializeField] private Slider _sfxVolumeSlider;
    [SerializeField] private TMP_Text _sfxVolumeValue;
    [SerializeField] private Slider _musicVolumeSlider;
    [SerializeField] private TMP_Text _musicVolumeValue;

    [Header("Authored Feedback rows")]
    [SerializeField] private Toggle _reducedMotionToggle;
    [SerializeField] private TMP_Text _reducedMotionValue;
    [SerializeField] private Toggle _screenShakeToggle;
    [SerializeField] private TMP_Text _screenShakeValue;
    [SerializeField] private CanvasGroup _screenShakeRow;
    [SerializeField] private Toggle _screenFlashToggle;
    [SerializeField] private TMP_Text _screenFlashValue;

    [Header("Authored reset and failure states")]
    [SerializeField] private Button _resetCategoryButton;
    [SerializeField] private TMP_Text _resetCategoryLabel;
    [SerializeField] private Button _resetAllButton;
    [SerializeField] private TMP_Text _resetAllLabel;
    [SerializeField] private GameObject _settingsUnavailableState;

    [Header("Live presentation consumers")]
    [SerializeField] private MainMenuPresentationController _mainMenuPresentation;
    [SerializeField] private TitleScreenScreenStack _screenStack;

    private UserSettingsService _service;
    private UserSettingsCategory _category = UserSettingsCategory.Controls;
    private bool _resetCategoryArmed;
    private bool _resetAllArmed;
    private bool _listenersBound;

    public UserSettingsCategory CurrentCategory => _category;
    public bool HasRequiredReferences => _controlsCategoryButton != null &&
                                         _audioCategoryButton != null &&
                                         _feedbackCategoryButton != null &&
                                         _controlsPanel != null &&
                                         _audioPanel != null &&
                                         _feedbackPanel != null &&
                                         _horizontalSensitivitySlider != null &&
                                         _verticalSensitivitySlider != null &&
                                         _invertYToggle != null &&
                                         _sfxVolumeSlider != null &&
                                         _musicVolumeSlider != null &&
                                         _reducedMotionToggle != null &&
                                         _screenShakeToggle != null &&
                                         _screenFlashToggle != null;

    private void Awake()
    {
        ConfigureRanges();
        BindControlListeners();
        SetCategory(UserSettingsCategory.Controls, false);
        ResetConfirmationState();
    }

    private void OnEnable()
    {
        BindService();
    }

    private void OnDisable()
    {
        UnbindService();
        ResetConfirmationState();
    }

    private void OnDestroy()
    {
        UnbindService();
        UnbindControlListeners();
    }

    public void Show()
    {
        BindService();
        SetCategory(_category, false);
        SyncFromService();
    }

    public void Hide()
    {
        _service?.FlushPendingSave();
        ResetConfirmationState();
    }

    public void SetCategory(UserSettingsCategory category)
    {
        SetCategory(category, true);
    }

    private void SetCategory(UserSettingsCategory category, bool moveFocus)
    {
        _category = category;
        if (_controlsPanel != null)
            _controlsPanel.SetActive(category == UserSettingsCategory.Controls);
        if (_audioPanel != null)
            _audioPanel.SetActive(category == UserSettingsCategory.Audio);
        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(category == UserSettingsCategory.Feedback);
        ResetConfirmationState();

        if (!moveFocus)
            return;
        Selectable target = category switch
        {
            UserSettingsCategory.Controls => _horizontalSensitivitySlider,
            UserSettingsCategory.Audio => _sfxVolumeSlider,
            UserSettingsCategory.Feedback => _reducedMotionToggle,
            _ => null
        };
        if (target != null)
            target.Select();
    }

    private void BindService()
    {
        UserSettingsService service = UserSettingsService.Instance;
        if (_service == service)
        {
            SetAvailability(_service != null);
            SyncFromService();
            return;
        }

        UnbindService();
        _service = service;
        if (_service != null)
            _service.Changed += HandleSettingsChanged;
        else if (Application.isPlaying)
            Debug.LogError("SettingsScreenUI requires the authored UserSettingsService; settings controls were disabled.", this);

        SetAvailability(_service != null);
        SyncFromService();
    }

    private void UnbindService()
    {
        if (_service != null)
            _service.Changed -= HandleSettingsChanged;
        _service = null;
    }

    private void HandleSettingsChanged(UserSettingsChange change)
    {
        SyncFromService();
    }

    private void SyncFromService()
    {
        if (_service == null)
            return;

        float horizontalSensitivity = _service.HorizontalSensitivity;
        float verticalSensitivity = _service.VerticalSensitivity;
        bool invertY = _service.InvertY;
        float sfxVolume = _service.SfxVolume;
        float musicVolume = _service.MusicVolume;
        bool reducedMotion = _service.ReducedMotion;
        bool screenShake = _service.ScreenShake;
        bool screenFlash = _service.ScreenFlash;

        _horizontalSensitivitySlider?.SetValueWithoutNotify(horizontalSensitivity);
        _verticalSensitivitySlider?.SetValueWithoutNotify(verticalSensitivity);
        _invertYToggle?.SetIsOnWithoutNotify(invertY);
        _sfxVolumeSlider?.SetValueWithoutNotify(sfxVolume);
        _musicVolumeSlider?.SetValueWithoutNotify(musicVolume);
        _reducedMotionToggle?.SetIsOnWithoutNotify(reducedMotion);
        _screenShakeToggle?.SetIsOnWithoutNotify(screenShake);
        _screenFlashToggle?.SetIsOnWithoutNotify(screenFlash);

        SetNumeric(_horizontalSensitivityValue, horizontalSensitivity, true);
        SetNumeric(_verticalSensitivityValue, verticalSensitivity, true);
        SetNumeric(_sfxVolumeValue, sfxVolume * 100f, false);
        SetNumeric(_musicVolumeValue, musicVolume * 100f, false);
        SetToggleText(_invertYValue, invertY);
        SetToggleText(_reducedMotionValue, reducedMotion);
        SetToggleText(_screenShakeValue, screenShake);
        SetToggleText(_screenFlashValue, screenFlash);

        bool shakeRelevant = !reducedMotion;
        if (_screenShakeToggle != null)
            _screenShakeToggle.interactable = shakeRelevant;
        if (_screenShakeRow != null)
        {
            _screenShakeRow.alpha = shakeRelevant ? 1f : 0.5f;
            _screenShakeRow.interactable = shakeRelevant;
        }

        _mainMenuPresentation?.ApplyPreferences(reducedMotion, screenShake, screenFlash);
        _screenStack?.ApplyReducedMotion(reducedMotion);
    }

    private void SetAvailability(bool available)
    {
        if (_settingsUnavailableState != null)
            _settingsUnavailableState.SetActive(!available);

        SetInteractable(_horizontalSensitivitySlider, available);
        SetInteractable(_verticalSensitivitySlider, available);
        SetInteractable(_invertYToggle, available);
        SetInteractable(_sfxVolumeSlider, available);
        SetInteractable(_musicVolumeSlider, available);
        SetInteractable(_reducedMotionToggle, available);
        SetInteractable(_screenShakeToggle, available);
        SetInteractable(_screenFlashToggle, available);
        SetInteractable(_resetCategoryButton, available);
        SetInteractable(_resetAllButton, available);
    }

    private void BindControlListeners()
    {
        if (_listenersBound)
            return;
        _listenersBound = true;
        _controlsCategoryButton?.onClick.AddListener(() => SetCategory(UserSettingsCategory.Controls));
        _audioCategoryButton?.onClick.AddListener(() => SetCategory(UserSettingsCategory.Audio));
        _feedbackCategoryButton?.onClick.AddListener(() => SetCategory(UserSettingsCategory.Feedback));
        _horizontalSensitivitySlider?.onValueChanged.AddListener(SetHorizontalSensitivity);
        _verticalSensitivitySlider?.onValueChanged.AddListener(SetVerticalSensitivity);
        _invertYToggle?.onValueChanged.AddListener(SetInvertY);
        _sfxVolumeSlider?.onValueChanged.AddListener(SetSfxVolume);
        _musicVolumeSlider?.onValueChanged.AddListener(SetMusicVolume);
        _reducedMotionToggle?.onValueChanged.AddListener(SetReducedMotion);
        _screenShakeToggle?.onValueChanged.AddListener(SetScreenShake);
        _screenFlashToggle?.onValueChanged.AddListener(SetScreenFlash);
        _resetCategoryButton?.onClick.AddListener(HandleResetCategory);
        _resetAllButton?.onClick.AddListener(HandleResetAll);
    }

    private void UnbindControlListeners()
    {
        if (!_listenersBound)
            return;
        _listenersBound = false;
        _horizontalSensitivitySlider?.onValueChanged.RemoveListener(SetHorizontalSensitivity);
        _verticalSensitivitySlider?.onValueChanged.RemoveListener(SetVerticalSensitivity);
        _invertYToggle?.onValueChanged.RemoveListener(SetInvertY);
        _sfxVolumeSlider?.onValueChanged.RemoveListener(SetSfxVolume);
        _musicVolumeSlider?.onValueChanged.RemoveListener(SetMusicVolume);
        _reducedMotionToggle?.onValueChanged.RemoveListener(SetReducedMotion);
        _screenShakeToggle?.onValueChanged.RemoveListener(SetScreenShake);
        _screenFlashToggle?.onValueChanged.RemoveListener(SetScreenFlash);
        _resetCategoryButton?.onClick.RemoveListener(HandleResetCategory);
        _resetAllButton?.onClick.RemoveListener(HandleResetAll);
        // Category listeners are static for this authored instance and are released with the object.
    }

    private void SetHorizontalSensitivity(float value) => _service?.SetHorizontalSensitivity(value);
    private void SetVerticalSensitivity(float value) => _service?.SetVerticalSensitivity(value);
    private void SetInvertY(bool value) => _service?.SetInvertY(value);
    private void SetSfxVolume(float value) => _service?.SetSfxVolume(value);
    private void SetMusicVolume(float value) => _service?.SetMusicVolume(value);
    private void SetReducedMotion(bool value) => _service?.SetReducedMotion(value);
    private void SetScreenShake(bool value) => _service?.SetScreenShake(value);
    private void SetScreenFlash(bool value) => _service?.SetScreenFlash(value);

    private void HandleResetCategory()
    {
        if (_service == null)
            return;
        if (!_resetCategoryArmed)
        {
            _resetCategoryArmed = true;
            if (_resetCategoryLabel != null)
                _resetCategoryLabel.text = "CONFIRM RESET";
            return;
        }
        _service.ResetCategory(_category);
        ResetConfirmationState();
    }

    private void HandleResetAll()
    {
        if (_service == null)
            return;
        if (!_resetAllArmed)
        {
            _resetAllArmed = true;
            if (_resetAllLabel != null)
                _resetAllLabel.text = "CONFIRM RESET ALL";
            return;
        }
        _service.ResetAll();
        ResetConfirmationState();
    }

    private void ResetConfirmationState()
    {
        _resetCategoryArmed = false;
        _resetAllArmed = false;
        if (_resetCategoryLabel != null)
            _resetCategoryLabel.text = "RESET CATEGORY";
        if (_resetAllLabel != null)
            _resetAllLabel.text = "RESET ALL";
    }

    private void ConfigureRanges()
    {
        ConfigureSlider(_horizontalSensitivitySlider, UserSettingsData.MinimumSensitivity, UserSettingsData.MaximumSensitivity);
        ConfigureSlider(_verticalSensitivitySlider, UserSettingsData.MinimumSensitivity, UserSettingsData.MaximumSensitivity);
        ConfigureSlider(_sfxVolumeSlider, 0f, 1f);
        ConfigureSlider(_musicVolumeSlider, 0f, 1f);
    }

    private static void ConfigureSlider(Slider slider, float minimum, float maximum)
    {
        if (slider == null)
            return;
        slider.minValue = minimum;
        slider.maxValue = maximum;
    }

    private static void SetNumeric(TMP_Text label, float value, bool showHundredths)
    {
        if (label == null)
            return;
        if (showHundredths)
            label.SetText("{0:2}", value);
        else
            label.SetText("{0:0}", value);
    }

    private static void SetToggleText(TMP_Text label, bool enabled)
    {
        if (label != null)
            label.text = enabled ? "ON" : "OFF";
    }

    private static void SetInteractable(Selectable selectable, bool interactable)
    {
        if (selectable != null)
            selectable.interactable = interactable;
    }
}
