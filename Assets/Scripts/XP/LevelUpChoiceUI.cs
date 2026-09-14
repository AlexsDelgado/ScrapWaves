using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct LevelUpChoiceOption
{
    public LevelUpChoiceOption(string displayName, string description = null, Sprite icon = null,
        HudPlaceholderKind placeholder = HudPlaceholderKind.None, WeaponData weapon = null)
    {
        DisplayName = displayName;
        Description = description;
        Icon = icon;
        Placeholder = placeholder;
        Weapon = weapon;
    }

    public string DisplayName { get; }
    public string Description { get; }
    public Sprite Icon { get; }
    public HudPlaceholderKind Placeholder { get; }
    public WeaponData Weapon { get; }
}

[DisallowMultipleComponent]
public class LevelUpChoiceUI : MonoBehaviour
{
    [SerializeField] private bool _pauseWhileChoosing = true;
    [SerializeField] private ThirdPersonCamera _thirdPersonCamera;
    [SerializeField] private ChoiceMenuView _levelUpView;
    [SerializeField] private ChoiceMenuView _weaponSelectionView;
    [SerializeField] private RunMenuContent _content;

    private ChoiceMenuView _activeView;
    private ThirdPersonCamera _resolvedCamera;
    private float _previousTimeScale = 1f;
    private Action<int> _onSelected;
    private IReadOnlyList<LevelUpChoiceOption> _currentOptions;
    private bool _isVisible;
    private bool _holdsUiPause;

    public bool IsVisible => _isVisible;

    private void Awake()
    {
        _levelUpView?.Hide();
        if (_weaponSelectionView != _levelUpView)
            _weaponSelectionView?.Hide();
    }

    public IEnumerator PresentCoroutine(string title, IReadOnlyList<LevelUpChoiceOption> options,
        Action<int> onComplete)
    {
        // The level-up heading and instructions are authored on this menu prefab.
        return PresentChoiceCoroutine(_levelUpView, null, null, null, options, onComplete);
    }

    public IEnumerator PresentWeaponSelectionCoroutine(string title, IReadOnlyList<LevelUpChoiceOption> options,
        Action<int> onComplete)
    {
        string subtitle = options != null ? $"Choose 1 of {options.Count} weapons" : string.Empty;
        return PresentChoiceCoroutine(_weaponSelectionView, null, subtitle, null, options, onComplete);
    }

    private IEnumerator PresentChoiceCoroutine(ChoiceMenuView view, string title, string subtitle, string footer,
        IReadOnlyList<LevelUpChoiceOption> options, Action<int> onComplete)
    {
        bool done = false;
        int selectedIndex = -1;
        ShowChoice(view, title, subtitle, footer, options, index =>
        {
            selectedIndex = index;
            done = true;
        });

        while (!done)
            yield return null;

        onComplete?.Invoke(selectedIndex);
    }

    public void Show(string title, IReadOnlyList<LevelUpChoiceOption> options, Action<int> onSelected)
    {
        ShowChoice(_levelUpView, title, "Choose one upgrade", "Select an upgrade to continue", options, onSelected);
    }

    private void ShowChoice(ChoiceMenuView view, string title, string subtitle, string footer,
        IReadOnlyList<LevelUpChoiceOption> options, Action<int> onSelected)
    {
        if (_isVisible || options == null || options.Count == 0)
        {
            onSelected?.Invoke(-1);
            return;
        }

        if (view == null || !view.CanPresent(options.Count))
        {
            Debug.LogWarning("LevelUpChoiceUI: assign an authored choice view with enough configured cards.", this);
            onSelected?.Invoke(-1);
            return;
        }

        _activeView = view;
        _currentOptions = options;
        _onSelected = onSelected;
        _isVisible = true;
        view.Show(title, subtitle, footer, options, _content, OnOptionClicked);
        SetCameraBlocked(true);

        if (_pauseWhileChoosing)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            GameplayPause.SetHeld(ref _holdsUiPause, true);
        }
    }

    private void OnOptionClicked(int index)
    {
        if (!_isVisible || _currentOptions == null || index < 0 || index >= _currentOptions.Count)
            return;

        CompleteChoice(index);
    }

    private void CompleteChoice(int index)
    {
        Action<int> onSelected = _onSelected;
        _onSelected = null;
        _currentOptions = null;
        Hide();
        onSelected?.Invoke(index);
    }

    private void Hide()
    {
        _isVisible = false;
        _activeView?.Hide();
        _activeView = null;
        SetCameraBlocked(false);
        if (_holdsUiPause)
        {
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
            GameplayPause.SetHeld(ref _holdsUiPause, false);
        }
    }

    private void OnDisable()
    {
        if (_isVisible)
            CompleteChoice(-1);
        else
            GameplayPause.SetHeld(ref _holdsUiPause, false);
    }

    private void SetCameraBlocked(bool blocked)
    {
        if (_resolvedCamera == null)
        {
            _resolvedCamera = _thirdPersonCamera != null
                ? _thirdPersonCamera
                : FindFirstObjectByType<ThirdPersonCamera>();
        }

        _resolvedCamera?.SetLookBlockedByUi(blocked);
    }
}
