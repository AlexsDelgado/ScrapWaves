using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TitleScreenController : MonoBehaviour
{
    [Header("Authored production controls")]
    [SerializeField] private Button _playButton;
    [SerializeField] private Button _tutorialButton;
    [SerializeField] private Button _objectivesButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _quitButton;
    [SerializeField] private Button _quitConfirmButton;

    [Header("Authored developer controls")]
    [SerializeField] private GameObject _developerRoot;
    [SerializeField] private Button _weaponSandboxButton;
    [SerializeField] private Button _enemiesTestingButton;
    [SerializeField, Tooltip("Enables the separated DEV destination section. Keep disabled for normal builds.")]
    private bool _includeTestingButtons;

    [Header("Authored screen and presentation systems")]
    [SerializeField] private EventSystem _eventSystem;
    [SerializeField] private TitleScreenScreenStack _screenStack;
    [SerializeField] private MainMenuPresentationController _presentation;
    [SerializeField] private ObjectivesMenuUI _objectivesScreen;
    [SerializeField] private SettingsScreenUI _settingsScreen;
    [SerializeField] private ScrapSceneTransition _sceneTransition;
    [SerializeField] private UserSettingsService _settingsService;

    [Header("Optional demonstration leaderboard")]
    [SerializeField] private DemoLeaderboardSettings _demoLeaderboardSettings;
    [SerializeField] private DemoLeaderboardView _leaderboardView;
    [SerializeField] private PlayerNameEntryView _playerNameEntry;

    [Header("Authored item views")]
    [SerializeField] private MainMenuItemView _playItem;
    [SerializeField] private MainMenuItemView _objectivesItem;
    [SerializeField] private MainMenuItemView _settingsItem;
    [SerializeField] private MainMenuItemView _quitItem;
    [SerializeField] private MainMenuItemView _weaponSandboxItem;
    [SerializeField] private MainMenuItemView _enemiesTestingItem;

    private bool _wired;
    private bool _initialFocusApplied;
    private bool _nameEntryRequested;
    private bool _tutorialRequested;
    private bool _launchRequested;

    public bool IncludeTestingButtons => _includeTestingButtons;
    public Button PlayButton => _playButton;
    public Button TutorialButton => _tutorialButton;
    public Button ObjectivesButton => _objectivesButton;
    public Button SettingsButton => _settingsButton;
    public Button QuitButton => _quitButton;
    public TitleScreenScreenStack ScreenStack => _screenStack;
    public bool DemoLeaderboardEnabled => _demoLeaderboardSettings != null && _demoLeaderboardSettings.Enabled;

    private void Awake()
    {
        ShowCursorForMenu();
        DemoLeaderboardSession.ResetForMenu();
        if (UserSettingsService.Instance != null)
            _settingsService = UserSettingsService.Instance;
        if (ScrapSceneTransition.Instance != null)
            _sceneTransition = ScrapSceneTransition.Instance;
        ValidateAuthoredReferences();
        WireButtons();
        ApplyDestinationVisibility();
        if (_screenStack != null)
        {
            _screenStack.ScreenClosed += HandleScreenClosed;
            _screenStack.ScreenOpened += HandleScreenOpened;
        }
        if (_playerNameEntry != null)
            _playerNameEntry.Confirmed += HandlePlayerNameConfirmed;
        if (_demoLeaderboardSettings != null)
            _demoLeaderboardSettings.Changed += HandleDemoLeaderboardChanged;
        ApplyLeaderboardVisibility();
        if (_presentation != null)
            _presentation.InteractionBecameAvailable += HandlePresentationInteractionAvailable;
        if (_sceneTransition != null)
            _sceneTransition.TransitioningChanged += HandleTransitioningChanged;
        if (_settingsService != null)
        {
            _settingsService.Changed += HandleSettingsChanged;
            ApplyFeedbackPreferences();
        }
    }

    private void Start()
    {
        TryApplyInitialFocus();
    }

    private void OnDestroy()
    {
        if (_screenStack != null)
        {
            _screenStack.ScreenClosed -= HandleScreenClosed;
            _screenStack.ScreenOpened -= HandleScreenOpened;
        }
        if (_playerNameEntry != null)
            _playerNameEntry.Confirmed -= HandlePlayerNameConfirmed;
        if (_demoLeaderboardSettings != null)
            _demoLeaderboardSettings.Changed -= HandleDemoLeaderboardChanged;
        if (_presentation != null)
            _presentation.InteractionBecameAvailable -= HandlePresentationInteractionAvailable;
        if (_sceneTransition != null)
            _sceneTransition.TransitioningChanged -= HandleTransitioningChanged;
        if (_settingsService != null)
            _settingsService.Changed -= HandleSettingsChanged;
        UnwireButtons();
    }

    public void SetTestingButtonsVisible(bool visible)
    {
        _includeTestingButtons = visible;
        ApplyDestinationVisibility();
    }

    private void OpenObjectives()
    {
        if (_objectivesScreen == null || _screenStack == null)
        {
            _presentation?.PlayReject();
            return;
        }

        _presentation?.PlayConfirm(_objectivesItem);
        if (_screenStack.OpenObjectives(_objectivesButton.gameObject))
            _objectivesScreen.Show();
    }

    private void OpenSettings()
    {
        if (_settingsScreen == null || _screenStack == null)
        {
            _presentation?.PlayReject();
            return;
        }

        _presentation?.PlayConfirm(_settingsItem);
        if (_screenStack.OpenSettings(_settingsButton.gameObject))
            _settingsScreen.Show();
    }

    private void OpenQuitConfirmation()
    {
        if (_screenStack == null)
        {
            _presentation?.PlayReject();
            return;
        }

        _presentation?.PlayConfirm(_quitItem);
        _screenStack.OpenQuitConfirmation(_quitButton.gameObject);
    }

    public void PlayTutorial()
    {
        RequestPlay(true);
    }

    private void LoadPlay()
    {
        RequestPlay(false);
    }

    private void RequestPlay(bool tutorial)
    {
        if (_launchRequested || _nameEntryRequested ||
            (_sceneTransition != null && _sceneTransition.IsTransitioning) ||
            (_screenStack != null && (_screenStack.IsInputLocked || _screenStack.CurrentState != TitleScreenLocalState.MainMenu)))
            return;

        _presentation?.PlayConfirm(_playItem);
        _tutorialRequested = tutorial;
        if (!DemoLeaderboardEnabled)
        {
            BeginPlayLaunch(null);
            return;
        }

        if (_playerNameEntry == null || !_playerNameEntry.HasRequiredReferences ||
            _screenStack == null || !_screenStack.HasPlayerNameEntryBinding)
        {
            Debug.LogError("TitleScreenController: the enabled demo leaderboard requires an authored name-entry screen.", this);
            _tutorialRequested = false;
            _presentation?.PlayReject();
            return;
        }

        _playerNameEntry.Prepare();
        Button source = tutorial ? _tutorialButton : _playButton;
        _nameEntryRequested = _screenStack.OpenPlayerNameEntry(source != null ? source.gameObject : null);
        if (!_nameEntryRequested)
            _tutorialRequested = false;
    }

    private void HandlePlayerNameConfirmed(string playerName)
    {
        if (!_nameEntryRequested || _launchRequested || !DemoLeaderboardEnabled ||
            _screenStack == null || _screenStack.IsInputLocked ||
            _screenStack.CurrentState != TitleScreenLocalState.PlayerNameEntry)
            return;
        BeginPlayLaunch(playerName);
    }

    private void BeginPlayLaunch(string playerName)
    {
        _launchRequested = true;
        DemoLeaderboardSession.PrepareMenuLaunch(DemoLeaderboardEnabled ? playerName : null);
        if (_tutorialRequested)
            TutorialDirector.RequestReplay();
        try
        {
            if (!RequestSceneLoad(SceneDestination.Play))
                HandlePlayLaunchFailed();
        }
        catch (Exception exception)
        {
            Debug.LogError($"TitleScreenController: unable to start gameplay. {exception.Message}", this);
            HandlePlayLaunchFailed();
        }
    }

    private void HandlePlayLaunchFailed()
    {
        _launchRequested = false;
        DemoLeaderboardSession.CancelPendingLaunch();
        if (_tutorialRequested)
            TutorialDirector.CancelReplayRequest();
        if (_nameEntryRequested && DemoLeaderboardEnabled)
            _playerNameEntry?.ShowLaunchError();
        else
        {
            _tutorialRequested = false;
            if (_nameEntryRequested)
                _screenStack?.CloseCurrent();
        }
    }

    private void HandleScreenOpened(TitleScreenLocalState state)
    {
        if (state != TitleScreenLocalState.PlayerNameEntry)
            return;
        if (!DemoLeaderboardEnabled)
        {
            _screenStack.CloseCurrent();
            return;
        }
        _playerNameEntry?.ActivateInput();
    }

    private void HandleDemoLeaderboardChanged(bool enabled)
    {
        ApplyLeaderboardVisibility();
        if (enabled)
            return;
        DemoLeaderboardSession.CancelPendingLaunch();
        if (_nameEntryRequested && !_launchRequested)
        {
            _tutorialRequested = false;
            _screenStack?.CloseCurrent();
        }
    }

    private void ApplyLeaderboardVisibility()
    {
        if (_leaderboardView == null)
            return;
        _leaderboardView.SetVisible(DemoLeaderboardEnabled);
        // Edit-mode authoring validation must not access the user's demonstration save.
        if (DemoLeaderboardEnabled && Application.isPlaying)
            _leaderboardView.RefreshFromStore();
    }

    private void LoadWeaponSandbox()
    {
        _presentation?.PlayConfirm(_weaponSandboxItem);
        RequestSceneLoad(SceneDestination.WeaponSandbox);
    }

    private void LoadEnemiesTesting()
    {
        _presentation?.PlayConfirm(_enemiesTestingItem);
        RequestSceneLoad(SceneDestination.EnemiesTesting);
    }

    private void ConfirmQuit()
    {
        SceneNavigation.QuitApplication();
    }

    private bool RequestSceneLoad(SceneDestination destination)
    {
        if (_sceneTransition != null)
        {
            if (_sceneTransition.TryLoad(destination))
                return true;
            if (_sceneTransition.IsTransitioning)
                return false;
        }

        Debug.LogError("TitleScreenController: the authored ScrapSceneTransition is missing or unavailable; loading directly.", this);
        return SceneNavigation.Load(destination);
    }

    private void HandleScreenClosed(TitleScreenLocalState state)
    {
        switch (state)
        {
            case TitleScreenLocalState.Objectives:
                _objectivesScreen?.Hide();
                break;
            case TitleScreenLocalState.Settings:
                _settingsScreen?.Hide();
                break;
            case TitleScreenLocalState.PlayerNameEntry:
                _playerNameEntry?.ResetEntry();
                _nameEntryRequested = false;
                if (!_launchRequested)
                {
                    _tutorialRequested = false;
                    DemoLeaderboardSession.CancelPendingLaunch();
                }
                break;
        }

        if (_eventSystem != null && _eventSystem.currentSelectedGameObject == null)
            Focus(_playButton);
    }

    private void HandleSettingsChanged(UserSettingsChange change)
    {
        if ((change & UserSettingsChange.Feedback) != 0)
            ApplyFeedbackPreferences();
    }

    private void HandlePresentationInteractionAvailable()
    {
        TryApplyInitialFocus();
    }

    private void TryApplyInitialFocus()
    {
        if (!_initialFocusApplied && Focus(_playButton))
            _initialFocusApplied = true;
    }

    private void HandleTransitioningChanged(bool transitioning)
    {
        _presentation?.SetInputLocked(transitioning);
        _screenStack?.SetInputLocked(transitioning);
        if (!transitioning && _launchRequested && gameObject.scene.IsValid() &&
            gameObject.scene.name == SceneNavigation.TitleSceneName &&
            SceneManager.GetActiveScene() == gameObject.scene)
            HandlePlayLaunchFailed();
    }

    private void ApplyFeedbackPreferences()
    {
        if (_settingsService == null)
            return;
        _presentation?.ApplyPreferences(
            _settingsService.ReducedMotion,
            _settingsService.ScreenShake,
            _settingsService.ScreenFlash);
        _screenStack?.ApplyReducedMotion(_settingsService.ReducedMotion);
    }

    private void WireButtons()
    {
        if (_wired)
            return;
        _wired = true;
        Wire(_playButton, LoadPlay);
        Wire(_objectivesButton, OpenObjectives);
        Wire(_settingsButton, OpenSettings);
        Wire(_quitButton, OpenQuitConfirmation);
        Wire(_quitConfirmButton, ConfirmQuit);
        Wire(_weaponSandboxButton, LoadWeaponSandbox);
        Wire(_enemiesTestingButton, LoadEnemiesTesting);
    }

    private void UnwireButtons()
    {
        if (!_wired)
            return;
        _wired = false;
        Unwire(_playButton, LoadPlay);
        Unwire(_objectivesButton, OpenObjectives);
        Unwire(_settingsButton, OpenSettings);
        Unwire(_quitButton, OpenQuitConfirmation);
        Unwire(_quitConfirmButton, ConfirmQuit);
        Unwire(_weaponSandboxButton, LoadWeaponSandbox);
        Unwire(_enemiesTestingButton, LoadEnemiesTesting);
    }

    private void ApplyDestinationVisibility()
    {
        if (_developerRoot != null)
            _developerRoot.SetActive(_includeTestingButtons);
        else
        {
            if (_weaponSandboxButton != null)
                _weaponSandboxButton.gameObject.SetActive(_includeTestingButtons);
            if (_enemiesTestingButton != null)
                _enemiesTestingButton.gameObject.SetActive(_includeTestingButtons);
        }

        bool quitSupported = SupportsApplicationQuit();
        if (_quitButton != null)
            _quitButton.gameObject.SetActive(quitSupported);

        ConfigureNavigation();
        if (_eventSystem != null &&
            _eventSystem.currentSelectedGameObject != null &&
            !_eventSystem.currentSelectedGameObject.activeInHierarchy)
        {
            Focus(_playButton);
        }
    }

    private void ConfigureNavigation()
    {
        List<Button> destinations = new(6);
        AddVisibleDestination(destinations, _playButton);
        AddVisibleDestination(destinations, _settingsButton);
        AddVisibleDestination(destinations, _objectivesButton);
        AddVisibleDestination(destinations, _quitButton);
        AddVisibleDestination(destinations, _tutorialButton);
        if (_includeTestingButtons)
        {
            AddVisibleDestination(destinations, _weaponSandboxButton);
            AddVisibleDestination(destinations, _enemiesTestingButton);
        }

        for (int index = 0; index < destinations.Count; index++)
        {
            Navigation navigation = destinations[index].navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = index > 0 ? destinations[index - 1] : null;
            navigation.selectOnDown = index + 1 < destinations.Count ? destinations[index + 1] : null;
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            destinations[index].navigation = navigation;
        }
    }

    private static void AddVisibleDestination(List<Button> destinations, Button button)
    {
        if (button != null && button.gameObject.activeInHierarchy && button.IsInteractable())
            destinations.Add(button);
    }

    private void ValidateAuthoredReferences()
    {
        ValidateButton(_playButton, nameof(_playButton));
        ValidateButton(_objectivesButton, nameof(_objectivesButton));
        ValidateButton(_settingsButton, nameof(_settingsButton));
        if (SupportsApplicationQuit())
        {
            ValidateButton(_quitButton, nameof(_quitButton));
            ValidateButton(_quitConfirmButton, nameof(_quitConfirmButton));
        }
        if (_eventSystem == null)
            Debug.LogError("TitleScreenController: authored EventSystem reference is missing. Runtime will not create one.", this);
        if (_screenStack == null)
        {
            DisableWithError(_objectivesButton, nameof(_screenStack));
            DisableWithError(_settingsButton, nameof(_screenStack));
        }
        if (_objectivesScreen == null)
            DisableWithError(_objectivesButton, nameof(_objectivesScreen));
        if (_settingsScreen == null)
            DisableWithError(_settingsButton, nameof(_settingsScreen));
        if (_sceneTransition == null)
            Debug.LogError("TitleScreenController: authored scene transition reference is missing; scene routes will use the direct fallback.", this);
        if (_settingsService == null)
            Debug.LogError("TitleScreenController: authored UserSettingsService reference is missing; feedback preferences cannot be applied.", this);
        if (DemoLeaderboardEnabled)
        {
            if (_leaderboardView == null || !_leaderboardView.HasRequiredReferences)
                Debug.LogError("TitleScreenController: enabled demo leaderboard is missing its twenty authored rows.", this);
            if (_playerNameEntry == null || !_playerNameEntry.HasRequiredReferences ||
                _screenStack == null || !_screenStack.HasPlayerNameEntryBinding)
            {
                DisableWithError(_playButton, nameof(_playerNameEntry));
                DisableWithError(_tutorialButton, nameof(_playerNameEntry));
            }
        }
    }

    private void DisableWithError(Button button, string missingField)
    {
        Debug.LogError($"TitleScreenController: required authored field '{missingField}' is missing; the affected action was disabled.", this);
        if (button != null)
            button.interactable = false;
    }

    private void ValidateButton(Button button, string fieldName)
    {
        if (button == null)
            Debug.LogError($"TitleScreenController: required authored button '{fieldName}' is missing. Runtime UI construction is disabled.", this);
    }

    private bool Focus(Button button)
    {
        if (_eventSystem == null || button == null || !button.gameObject.activeInHierarchy || !button.IsInteractable())
            return false;
        _eventSystem.SetSelectedGameObject(button.gameObject);
        return true;
    }

    private static bool SupportsApplicationQuit()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return false;
#else
        return true;
#endif
    }

    private static void Wire(Button button, UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private static void Unwire(Button button, UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    private static void ShowCursorForMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
