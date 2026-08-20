using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-180)]
public sealed class UserSettingsApplier : MonoBehaviour
{
    [SerializeField] private UserSettingsService _settingsService;

    private readonly List<ThirdPersonCamera> _cameras = new();
    private readonly List<AudioManager> _audioManagers = new();
    private readonly List<WeaponPresentationController> _weaponPresentationControllers = new();
    private readonly List<PlayerCombatFeedback> _combatFeedbackViews = new();
    private bool _missingServiceReported;

    private void OnEnable()
    {
        UserSettingsService.InstanceChanged += HandleServiceInstanceChanged;
        ThirdPersonCamera.BecameAvailable += RegisterCamera;
        AudioManager.BecameAvailable += RegisterAudioManager;
        WeaponPresentationController.BecameAvailable += RegisterWeaponPresentation;
        PlayerCombatFeedback.BecameAvailable += RegisterCombatFeedback;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        BindService(_settingsService != null ? _settingsService : UserSettingsService.Instance);
        DiscoverTargets();
    }

    private void OnDisable()
    {
        UserSettingsService.InstanceChanged -= HandleServiceInstanceChanged;
        ThirdPersonCamera.BecameAvailable -= RegisterCamera;
        AudioManager.BecameAvailable -= RegisterAudioManager;
        WeaponPresentationController.BecameAvailable -= RegisterWeaponPresentation;
        PlayerCombatFeedback.BecameAvailable -= RegisterCombatFeedback;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (_settingsService != null)
            _settingsService.Changed -= HandleSettingsChanged;
    }

    private void HandleServiceInstanceChanged(UserSettingsService service)
    {
        BindService(service);
        if (service != null)
            ApplyAll();
    }

    private void BindService(UserSettingsService service)
    {
        if (_settingsService == service)
        {
            if (_settingsService != null)
            {
                _settingsService.Changed -= HandleSettingsChanged;
                _settingsService.Changed += HandleSettingsChanged;
            }
            return;
        }

        if (_settingsService != null)
            _settingsService.Changed -= HandleSettingsChanged;

        _settingsService = service;
        if (_settingsService != null)
        {
            _settingsService.Changed -= HandleSettingsChanged;
            _settingsService.Changed += HandleSettingsChanged;
        }
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        if (_settingsService == null)
            BindService(UserSettingsService.Instance);
        DiscoverTargets();
    }

    private void HandleSettingsChanged(UserSettingsChange changed)
    {
        if ((changed & UserSettingsChange.Controls) != 0 ||
            (changed & (UserSettingsChange.ReducedMotion | UserSettingsChange.ScreenShake)) != 0)
        {
            ApplyCameras();
        }

        if ((changed & UserSettingsChange.Audio) != 0)
            ApplyAudioManagers();

        if ((changed & UserSettingsChange.Feedback) != 0)
        {
            ApplyWeaponPresentationControllers();
            ApplyCombatFeedbackViews();
            ApplyEnemyReactionPreferences();
        }
    }

    private void DiscoverTargets()
    {
        RegisterRange(_cameras, FindObjectsByType<ThirdPersonCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        RegisterRange(_audioManagers, FindObjectsByType<AudioManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        RegisterRange(
            _weaponPresentationControllers,
            FindObjectsByType<WeaponPresentationController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        RegisterRange(
            _combatFeedbackViews,
            FindObjectsByType<PlayerCombatFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        ApplyAll();
    }

    public void ApplyAll()
    {
        if (!CanApply())
            return;

        ApplyCameras();
        ApplyAudioManagers();
        ApplyWeaponPresentationControllers();
        ApplyCombatFeedbackViews();
        ApplyEnemyReactionPreferences();
    }

    private bool CanApply()
    {
        if (_settingsService != null)
            return true;

        BindService(UserSettingsService.Instance);
        if (_settingsService != null)
            return true;

        if (!_missingServiceReported && Application.isPlaying)
        {
            Debug.LogError(
                "UserSettingsApplier: no authored UserSettingsService is available; live settings targets were left unchanged.",
                this);
            _missingServiceReported = true;
        }
        return false;
    }

    private void RegisterCamera(ThirdPersonCamera camera)
    {
        Register(_cameras, camera);
        if (CanApply() && camera != null)
            Apply(camera);
    }

    private void RegisterAudioManager(AudioManager audioManager)
    {
        Register(_audioManagers, audioManager);
        if (CanApply() && audioManager != null)
            Apply(audioManager);
    }

    private void RegisterWeaponPresentation(WeaponPresentationController controller)
    {
        Register(_weaponPresentationControllers, controller);
        if (CanApply() && controller != null)
            Apply(controller);
    }

    private void RegisterCombatFeedback(PlayerCombatFeedback feedback)
    {
        Register(_combatFeedbackViews, feedback);
        if (CanApply() && feedback != null)
            Apply(feedback);
    }

    private void ApplyCameras()
    {
        for (int i = _cameras.Count - 1; i >= 0; i--)
        {
            ThirdPersonCamera camera = _cameras[i];
            if (camera == null)
            {
                _cameras.RemoveAt(i);
                continue;
            }
            Apply(camera);
        }
    }

    private void ApplyAudioManagers()
    {
        for (int i = _audioManagers.Count - 1; i >= 0; i--)
        {
            AudioManager audioManager = _audioManagers[i];
            if (audioManager == null)
            {
                _audioManagers.RemoveAt(i);
                continue;
            }
            Apply(audioManager);
        }
    }

    private void ApplyWeaponPresentationControllers()
    {
        for (int i = _weaponPresentationControllers.Count - 1; i >= 0; i--)
        {
            WeaponPresentationController controller = _weaponPresentationControllers[i];
            if (controller == null)
            {
                _weaponPresentationControllers.RemoveAt(i);
                continue;
            }
            Apply(controller);
        }
    }

    private void ApplyCombatFeedbackViews()
    {
        for (int i = _combatFeedbackViews.Count - 1; i >= 0; i--)
        {
            PlayerCombatFeedback feedback = _combatFeedbackViews[i];
            if (feedback == null)
            {
                _combatFeedbackViews.RemoveAt(i);
                continue;
            }
            Apply(feedback);
        }
    }

    private void ApplyEnemyReactionPreferences()
    {
        EnemyReactionRuntime.ApplyUserPreferences(
            _settingsService.ReducedMotion,
            _settingsService.ScreenFlash);
    }

    private void Apply(ThirdPersonCamera camera)
    {
        camera.HorizontalSensitivity = _settingsService.HorizontalSensitivity;
        camera.VerticalSensitivity = _settingsService.VerticalSensitivity;
        camera.InvertVertical = _settingsService.InvertY;
        camera.ScreenShakeEnabled = _settingsService.ScreenShake && !_settingsService.ReducedMotion;
    }

    private void Apply(AudioManager audioManager)
    {
        audioManager.SfxVolume = _settingsService.SfxVolume;
        audioManager.MusicVolume = _settingsService.MusicVolume;
    }

    private void Apply(WeaponPresentationController controller)
    {
        controller.ApplyUserFeedbackPreferences(
            _settingsService.ReducedMotion,
            _settingsService.ScreenShake,
            _settingsService.ScreenFlash);
    }

    private void Apply(PlayerCombatFeedback feedback)
    {
        feedback.ApplyUserFeedbackPreferences(
            _settingsService.ReducedMotion,
            _settingsService.ScreenFlash);
    }

    private static void Register<T>(List<T> targets, T target) where T : Object
    {
        if (target != null && !targets.Contains(target))
            targets.Add(target);
    }

    private static void RegisterRange<T>(List<T> targets, T[] discovered) where T : Object
    {
        for (int i = 0; i < discovered.Length; i++)
            Register(targets, discovered[i]);
    }
}
