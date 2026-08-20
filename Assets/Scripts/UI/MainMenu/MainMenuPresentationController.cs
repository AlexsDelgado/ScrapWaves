using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MainMenuPresentationController : MonoBehaviour
{
    [Header("Authored references")]
    [SerializeField] private MainMenuPresentationProfile _profile;
    [SerializeField] private RectTransform _titleRoot;
    [SerializeField] private CanvasGroup _titleCanvasGroup;
    [SerializeField] private RectTransform _mainMenuRoot;
    [SerializeField] private CanvasGroup _mainMenuCanvasGroup;
    [SerializeField] private MainMenuItemView[] _orderedItems;
    [SerializeField] private MenuScreenPunch _screenPunch;
    [SerializeField] private ScrapMenuBackgroundController _background;
    [SerializeField] private MenuAudioFeedback _audioFeedback;

    [Header("Behavior")]
    [SerializeField] private bool _playIntroOnEnable = true;
    [SerializeField] private bool _allowNavigationDuringIntroTail = true;
    [SerializeField] private Vector2 _menuHiddenOffset = new(-110f, 0f);
    [SerializeField, Range(0f, 1f)] private float _menuDimmedAlpha = 0.16f;

    private MainMenuItemView _currentItem;
    private int _currentIndex = -1;
    private Vector2 _titleRestingPosition;
    private Vector3 _titleRestingScale;
    private Quaternion _titleRestingRotation;
    private Vector2 _menuRestingPosition;
    private bool _posesCaptured;
    private bool _introPlaying;
    private float _introElapsed;
    private bool _menuTransitionPlaying;
    private float _menuTransitionElapsed;
    private float _menuTransitionDuration;
    private float _menuTransitionStartAlpha;
    private float _menuTransitionTargetAlpha;
    private Vector2 _menuTransitionStartPosition;
    private Vector2 _menuTransitionTargetPosition;
    private bool _reducedMotion;
    private bool _screenShakeEnabled = true;
    private bool _screenFlashEnabled = true;
    private bool _inputLocked;

    public MainMenuItemView CurrentItem => _currentItem;
    public bool IsIntroPlaying => _introPlaying;
    public bool IsInteractive => _mainMenuCanvasGroup == null || _mainMenuCanvasGroup.interactable;
    public MainMenuItemView[] OrderedItems => _orderedItems;

    public event Action InteractionBecameAvailable;

    private void Awake()
    {
        CaptureAuthoredPoses();
        BindItems();
        if (_playIntroOnEnable)
            SetMenuInteraction(false);
    }

    private void OnEnable()
    {
        CaptureAuthoredPoses();
    }

    private void Start()
    {
        if (_playIntroOnEnable)
            PlayIntro();
        else
            CompleteIntroImmediately();
    }

    private void Update()
    {
        if (_introPlaying)
            TickIntro();
        if (_menuTransitionPlaying)
            TickMenuTransition();
    }

    public void NotifySelected(MainMenuItemView item, bool pointer)
    {
        if (item == null || item.Button == null || !item.Button.IsActive() || !item.Button.IsInteractable())
            return;

        int nextIndex = IndexOf(item);
        if (nextIndex < 0)
            return;
        if (_currentItem == item)
            return;

        int direction = _currentIndex < 0 || nextIndex >= _currentIndex ? 1 : -1;
        _currentItem = item;
        _currentIndex = nextIndex;

        for (int i = 0; i < _orderedItems.Length; i++)
        {
            MainMenuItemView current = _orderedItems[i];
            if (current != null)
                current.SetSelected(current == item, false);
        }

        if (!_introPlaying)
        {
            _screenPunch?.Play(direction, pointer);
            _audioFeedback?.PlayNavigation();
        }
    }

    public void NotifyPointerFocus(MainMenuItemView item)
    {
        NotifySelected(item, true);
    }

    public void PlayConfirm(MainMenuItemView item)
    {
        if (item == null)
            return;
        item.PlayPressed();
        _audioFeedback?.PlayConfirm();
    }

    public void PlayReject()
    {
        _audioFeedback?.PlayReject();
    }

    public void PlayIntro()
    {
        CaptureAuthoredPoses();
        if (_reducedMotion)
        {
            CompleteIntroImmediately();
            return;
        }

        _introElapsed = 0f;
        _introPlaying = true;
        if (_titleRoot != null && _profile != null)
        {
            _titleRoot.anchoredPosition = _titleRestingPosition + _profile.TitleStartOffset;
            _titleRoot.localScale = Vector3.Scale(_titleRestingScale, Vector3.one * _profile.TitleStartScale);
            _titleRoot.localRotation = _titleRestingRotation * Quaternion.Euler(0f, 0f, 4f);
        }
        if (_titleCanvasGroup != null)
            _titleCanvasGroup.alpha = 0f;

        float itemDuration = _profile != null ? _profile.ItemDuration : 0.27f;
        float itemStagger = _profile != null ? _profile.ItemStagger : 0.05f;
        Vector2 itemOffset = _profile != null ? _profile.ItemStartOffset : new Vector2(-130f, 0f);
        float itemScale = _profile != null ? _profile.ItemStartScale : 0.8f;
        for (int i = 0; i < _orderedItems.Length; i++)
        {
            MainMenuItemView item = _orderedItems[i];
            if (item != null && item.gameObject.activeInHierarchy)
                item.PrepareIntro(itemOffset, itemScale, i * itemStagger, itemDuration);
        }

        if (_mainMenuCanvasGroup != null)
        {
            _mainMenuCanvasGroup.alpha = 1f;
            SetMenuInteraction(false);
        }
    }

    public void CompleteIntroImmediately()
    {
        CaptureAuthoredPoses();
        _introPlaying = false;
        if (_titleRoot != null)
        {
            _titleRoot.anchoredPosition = _titleRestingPosition;
            _titleRoot.localScale = _titleRestingScale;
            _titleRoot.localRotation = _titleRestingRotation;
        }
        if (_titleCanvasGroup != null)
            _titleCanvasGroup.alpha = 1f;
        if (_orderedItems != null)
        {
            for (int i = 0; i < _orderedItems.Length; i++)
                _orderedItems[i]?.CompleteIntroImmediately();
        }
        if (_mainMenuCanvasGroup != null)
        {
            _mainMenuCanvasGroup.alpha = 1f;
            SetMenuInteraction(!_inputLocked);
        }
    }

    public void SetInputLocked(bool locked)
    {
        _inputLocked = locked;
        if (_mainMenuCanvasGroup == null)
            return;

        SetMenuInteraction(CanEnableInteraction());
    }

    public void SetMainMenuDimmed(bool dimmed, bool immediate)
    {
        CaptureAuthoredPoses();
        float targetAlpha = dimmed ? _menuDimmedAlpha : 1f;
        Vector2 targetPosition = _menuRestingPosition + (dimmed ? _menuHiddenOffset : Vector2.zero);
        if (immediate || _reducedMotion)
        {
            _menuTransitionPlaying = false;
            ApplyMenuTransition(targetAlpha, targetPosition);
            return;
        }

        _menuTransitionElapsed = 0f;
        _menuTransitionDuration = dimmed
            ? (_profile != null ? _profile.LocalScreenOpenDuration : 0.22f)
            : (_profile != null ? _profile.LocalScreenCloseDuration : 0.18f);
        _menuTransitionStartAlpha = _mainMenuCanvasGroup != null ? _mainMenuCanvasGroup.alpha : 1f;
        _menuTransitionTargetAlpha = targetAlpha;
        _menuTransitionStartPosition = _mainMenuRoot != null ? _mainMenuRoot.anchoredPosition : Vector2.zero;
        _menuTransitionTargetPosition = targetPosition;
        _menuTransitionPlaying = true;
        SetMenuInteraction(false);
    }

    public void ApplyPreferences(bool reducedMotion, bool screenShakeEnabled, bool screenFlashEnabled)
    {
        _reducedMotion = reducedMotion;
        _screenShakeEnabled = screenShakeEnabled;
        _screenFlashEnabled = screenFlashEnabled;
        _screenPunch?.ApplyPreferences(reducedMotion, screenShakeEnabled, screenFlashEnabled);
        _background?.ApplyReducedMotion(reducedMotion);
        if (reducedMotion && _introPlaying)
            CompleteIntroImmediately();
    }

    private void TickIntro()
    {
        _introElapsed += Time.unscaledDeltaTime;
        float titleDuration = _profile != null ? _profile.TitleDuration : 0.31f;
        float normalized = Mathf.Clamp01(_introElapsed / Mathf.Max(0.01f, titleDuration));
        AnimationCurve curve = _profile != null ? _profile.EntranceCurve : null;
        float curved = curve != null ? curve.Evaluate(normalized) : normalized;
        if (_titleRoot != null)
        {
            Vector2 startPosition = _titleRestingPosition + (_profile != null ? _profile.TitleStartOffset : new Vector2(-60f, 0f));
            Vector3 startScale = Vector3.Scale(_titleRestingScale, Vector3.one * (_profile != null ? _profile.TitleStartScale : 0.82f));
            _titleRoot.anchoredPosition = Vector2.LerpUnclamped(startPosition, _titleRestingPosition, curved);
            _titleRoot.localScale = Vector3.LerpUnclamped(startScale, _titleRestingScale, curved);
            _titleRoot.localRotation = Quaternion.SlerpUnclamped(
                _titleRestingRotation * Quaternion.Euler(0f, 0f, 4f),
                _titleRestingRotation,
                curved);
        }
        if (_titleCanvasGroup != null)
            _titleCanvasGroup.alpha = normalized;

        if (_allowNavigationDuringIntroTail && FirstActiveItemIsReadable())
            SetMenuInteraction(CanEnableInteraction());

        float itemEnd = (_profile != null ? _profile.ItemDuration : 0.27f) +
                        Mathf.Max(0, _orderedItems.Length - 1) * (_profile != null ? _profile.ItemStagger : 0.05f);
        if (_introElapsed < Mathf.Max(titleDuration, itemEnd))
            return;

        _introPlaying = false;
        SetMenuInteraction(CanEnableInteraction());
    }

    private void TickMenuTransition()
    {
        _menuTransitionElapsed += Time.unscaledDeltaTime;
        float normalized = Mathf.Clamp01(_menuTransitionElapsed / Mathf.Max(0.01f, _menuTransitionDuration));
        AnimationCurve curve = _profile != null ? _profile.LocalScreenCurve : null;
        float curved = curve != null ? curve.Evaluate(normalized) : normalized;
        ApplyMenuTransition(
            Mathf.LerpUnclamped(_menuTransitionStartAlpha, _menuTransitionTargetAlpha, curved),
            Vector2.LerpUnclamped(_menuTransitionStartPosition, _menuTransitionTargetPosition, curved));
        if (normalized >= 1f)
        {
            _menuTransitionPlaying = false;
            SetMenuInteraction(CanEnableInteraction());
        }
    }

    private void ApplyMenuTransition(float alpha, Vector2 position)
    {
        if (_mainMenuCanvasGroup != null)
        {
            _mainMenuCanvasGroup.alpha = alpha;
            SetMenuInteraction(CanEnableInteraction());
        }
        if (_mainMenuRoot != null)
            _mainMenuRoot.anchoredPosition = position;
    }

    private bool CanEnableInteraction()
    {
        if (_inputLocked || _mainMenuCanvasGroup == null || _mainMenuCanvasGroup.alpha <= 0.99f)
            return false;
        if (_menuTransitionPlaying)
            return false;
        if (!_introPlaying)
            return true;
        return _allowNavigationDuringIntroTail && FirstActiveItemIsReadable();
    }

    private bool FirstActiveItemIsReadable()
    {
        if (_orderedItems == null)
            return false;
        for (int i = 0; i < _orderedItems.Length; i++)
        {
            MainMenuItemView item = _orderedItems[i];
            if (item != null && item.gameObject.activeInHierarchy)
                return item.IsReadable;
        }
        return false;
    }

    private void SetMenuInteraction(bool interactive)
    {
        if (_mainMenuCanvasGroup == null)
            return;

        bool becameAvailable = interactive &&
                               (!_mainMenuCanvasGroup.interactable || !_mainMenuCanvasGroup.blocksRaycasts);
        _mainMenuCanvasGroup.interactable = interactive;
        _mainMenuCanvasGroup.blocksRaycasts = interactive;
        if (becameAvailable)
            InteractionBecameAvailable?.Invoke();
    }

    private void CaptureAuthoredPoses()
    {
        if (_posesCaptured)
            return;
        if (_titleRoot != null)
        {
            _titleRestingPosition = _titleRoot.anchoredPosition;
            _titleRestingScale = _titleRoot.localScale;
            _titleRestingRotation = _titleRoot.localRotation;
        }
        if (_mainMenuRoot != null)
            _menuRestingPosition = _mainMenuRoot.anchoredPosition;
        _posesCaptured = true;
    }

    private void BindItems()
    {
        if (_orderedItems == null)
            return;
        for (int i = 0; i < _orderedItems.Length; i++)
        {
            MainMenuItemView item = _orderedItems[i];
            if (item == null)
                continue;
            item.SetPresentationController(this);
            item.SetSelected(false, true);
        }
    }

    private int IndexOf(MainMenuItemView item)
    {
        if (_orderedItems == null)
            return -1;
        for (int i = 0; i < _orderedItems.Length; i++)
        {
            if (_orderedItems[i] == item)
                return i;
        }
        return -1;
    }
}
