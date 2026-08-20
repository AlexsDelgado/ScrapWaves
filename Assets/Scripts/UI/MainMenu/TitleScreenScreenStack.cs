using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum TitleScreenLocalState
{
    MainMenu,
    Objectives,
    Settings,
    QuitConfirmation
}

[DisallowMultipleComponent]
public sealed class TitleScreenScreenStack : MonoBehaviour, ICancelHandler
{
    [Serializable]
    private sealed class LocalScreenBinding
    {
        [SerializeField] private TitleScreenLocalState _state;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _animationRoot;
        [SerializeField] private Button _backButton;
        [SerializeField] private Selectable _initialFocus;

        [NonSerialized] public Vector2 RestingPosition;

        public TitleScreenLocalState State => _state;
        public CanvasGroup CanvasGroup => _canvasGroup;
        public RectTransform AnimationRoot => _animationRoot != null
            ? _animationRoot
            : _canvasGroup != null ? _canvasGroup.transform as RectTransform : null;
        public Button BackButton => _backButton;
        public Selectable InitialFocus => _initialFocus;
        public GameObject GameObject => _canvasGroup != null ? _canvasGroup.gameObject : null;
        public bool IsValid => _state != TitleScreenLocalState.MainMenu && _canvasGroup != null;
    }

    [Header("Authored local screens")]
    [SerializeField] private LocalScreenBinding _objectives;
    [SerializeField] private LocalScreenBinding _settings;
    [SerializeField] private LocalScreenBinding _quitConfirmation;
    [SerializeField] private ObjectivesMenuUI _objectivesPresenter;
    [SerializeField] private MainMenuPresentationController _mainMenuPresentation;
    [SerializeField] private MenuAudioFeedback _audioFeedback;
    [SerializeField] private MainMenuPresentationProfile _profile;
    [SerializeField, Tooltip("Authored UI/Cancel action shared with InputSystemUIInputModule.")]
    private InputActionReference _cancelAction;

    [Header("Per-instance transition")]
    [SerializeField, Min(0.01f)] private float _openDuration = 0.22f;
    [SerializeField, Min(0.01f)] private float _closeDuration = 0.18f;
    [SerializeField] private Vector2 _startOffset = new(90f, 0f);
    [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private LocalScreenBinding[] _bindings;
    private LocalScreenBinding _activeBinding;
    private GameObject _returnFocus;
    private Coroutine _transition;
    private Coroutine _focusRestore;
    private bool _externalInputLocked;
    private bool _localTransitionLocked;
    private bool _reducedMotion;
    private int _lastCancelFrame = -1;

    public event Action<TitleScreenLocalState> ScreenOpened;
    public event Action<TitleScreenLocalState> ScreenClosed;

    public TitleScreenLocalState CurrentState => _activeBinding != null
        ? _activeBinding.State
        : TitleScreenLocalState.MainMenu;
    public bool IsInputLocked => _externalInputLocked || _localTransitionLocked;
    public bool HasValidBindings => _objectives != null && _objectives.IsValid &&
                                    _objectivesPresenter != null &&
                                    _settings != null && _settings.IsValid &&
                                    _quitConfirmation != null && _quitConfirmation.IsValid;

    private float OpenDuration => _profile != null ? _profile.LocalScreenOpenDuration : _openDuration;
    private float CloseDuration => _profile != null ? _profile.LocalScreenCloseDuration : _closeDuration;
    private Vector2 StartOffset => _profile != null ? _profile.LocalScreenStartOffset : _startOffset;
    private AnimationCurve Curve => _profile != null && _profile.LocalScreenCurve != null ? _profile.LocalScreenCurve : _curve;

    private void Awake()
    {
        _bindings = new[] { _objectives, _settings, _quitConfirmation };
        for (int i = 0; i < _bindings.Length; i++)
        {
            LocalScreenBinding binding = _bindings[i];
            if (binding == null || !binding.IsValid)
                continue;
            RectTransform root = binding.AnimationRoot;
            if (root != null)
                binding.RestingPosition = root.anchoredPosition;
            binding.BackButton?.onClick.AddListener(CloseCurrent);
            SetVisibleImmediate(binding, false, false);
        }
    }

    private void OnEnable()
    {
        SubscribeCancelAction();
    }

    private void OnDisable()
    {
        UnsubscribeCancelAction();
    }

    private void OnDestroy()
    {
        UnsubscribeCancelAction();
        if (_bindings == null)
            return;
        for (int i = 0; i < _bindings.Length; i++)
            _bindings[i]?.BackButton?.onClick.RemoveListener(CloseCurrent);
    }

    public void OnCancel(BaseEventData eventData)
    {
        if (TryHandleCancel())
            eventData?.Use();
    }

    public bool OpenObjectives(GameObject returnFocus) => Open(TitleScreenLocalState.Objectives, returnFocus);
    public bool OpenSettings(GameObject returnFocus) => Open(TitleScreenLocalState.Settings, returnFocus);
    public bool OpenQuitConfirmation(GameObject returnFocus) => Open(TitleScreenLocalState.QuitConfirmation, returnFocus);

    public bool Open(TitleScreenLocalState state, GameObject returnFocus)
    {
        if (state == TitleScreenLocalState.MainMenu || _activeBinding != null || IsInputLocked)
            return false;

        LocalScreenBinding binding = FindBinding(state);
        if (binding == null || !binding.IsValid)
        {
            Debug.LogError($"TitleScreenScreenStack cannot open {state}: its authored screen binding is missing.", this);
            return false;
        }

        _activeBinding = binding;
        _returnFocus = returnFocus;
        if (_focusRestore != null)
        {
            StopCoroutine(_focusRestore);
            _focusRestore = null;
        }
        _mainMenuPresentation?.SetMainMenuDimmed(true, _reducedMotion);
        if (_transition != null)
            StopCoroutine(_transition);
        _transition = StartCoroutine(AnimateOpen(binding));
        return true;
    }

    public void CloseCurrent()
    {
        if (_activeBinding == null || IsInputLocked)
            return;
        if (_transition != null)
            StopCoroutine(_transition);
        _transition = StartCoroutine(AnimateClose(_activeBinding));
    }

    public void SetInputLocked(bool locked)
    {
        _externalInputLocked = locked;
        if (_activeBinding?.CanvasGroup != null)
        {
            bool interactive = !IsInputLocked && _activeBinding.CanvasGroup.alpha >= 0.999f;
            _activeBinding.CanvasGroup.interactable = interactive;
            _activeBinding.CanvasGroup.blocksRaycasts = interactive;
        }
    }

    public void ApplyReducedMotion(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
    }

    private IEnumerator AnimateOpen(LocalScreenBinding binding)
    {
        _localTransitionLocked = true;
        SetVisibleImmediate(binding, true, false);
        CanvasGroup group = binding.CanvasGroup;
        RectTransform root = binding.AnimationRoot;
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (root != null)
            root.anchoredPosition = binding.RestingPosition + (_reducedMotion ? Vector2.zero : StartOffset);

        float duration = _reducedMotion ? Mathf.Min(0.08f, OpenDuration) : OpenDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float curved = _reducedMotion ? normalized : Curve.Evaluate(normalized);
            if (group != null)
                group.alpha = normalized;
            if (root != null)
                root.anchoredPosition = Vector2.LerpUnclamped(
                    binding.RestingPosition + (_reducedMotion ? Vector2.zero : StartOffset),
                    binding.RestingPosition,
                    curved);
            yield return null;
        }

        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = !_externalInputLocked;
            group.blocksRaycasts = !_externalInputLocked;
        }
        if (root != null)
            root.anchoredPosition = binding.RestingPosition;
        _localTransitionLocked = false;
        _transition = null;
        if (!IsInputLocked && (binding.State != TitleScreenLocalState.Objectives || _objectivesPresenter == null))
            Focus(binding.InitialFocus);
        _audioFeedback?.PlayLocalOpen();
        ScreenOpened?.Invoke(binding.State);
    }

    private IEnumerator AnimateClose(LocalScreenBinding binding)
    {
        _localTransitionLocked = true;
        if (binding.CanvasGroup != null)
        {
            binding.CanvasGroup.interactable = false;
            binding.CanvasGroup.blocksRaycasts = false;
        }

        float duration = _reducedMotion ? Mathf.Min(0.08f, CloseDuration) : CloseDuration;
        float elapsed = 0f;
        RectTransform root = binding.AnimationRoot;
        Vector2 target = binding.RestingPosition + (_reducedMotion ? Vector2.zero : StartOffset);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float curved = _reducedMotion ? normalized : Curve.Evaluate(normalized);
            if (binding.CanvasGroup != null)
                binding.CanvasGroup.alpha = 1f - normalized;
            if (root != null)
                root.anchoredPosition = Vector2.LerpUnclamped(binding.RestingPosition, target, curved);
            yield return null;
        }

        TitleScreenLocalState closedState = binding.State;
        SetVisibleImmediate(binding, false, false);
        _activeBinding = null;
        _mainMenuPresentation?.SetMainMenuDimmed(false, _reducedMotion);
        _localTransitionLocked = false;
        _transition = null;
        GameObject focusTarget = _returnFocus;
        _returnFocus = null;
        if (focusTarget != null && (IsInputLocked || !TryFocus(focusTarget)))
            _focusRestore = StartCoroutine(FocusWhenReadable(focusTarget));
        _audioFeedback?.PlayLocalClose();
        ScreenClosed?.Invoke(closedState);
    }

    private LocalScreenBinding FindBinding(TitleScreenLocalState state)
    {
        if (_bindings == null)
            return null;
        for (int i = 0; i < _bindings.Length; i++)
        {
            LocalScreenBinding binding = _bindings[i];
            if (binding != null && binding.State == state)
                return binding;
        }
        return null;
    }

    private void SubscribeCancelAction()
    {
        InputAction action = _cancelAction != null ? _cancelAction.action : null;
        if (action == null)
            return;
        action.performed -= HandleCancelPerformed;
        action.performed += HandleCancelPerformed;
    }

    private void UnsubscribeCancelAction()
    {
        InputAction action = _cancelAction != null ? _cancelAction.action : null;
        if (action != null)
            action.performed -= HandleCancelPerformed;
    }

    private void HandleCancelPerformed(InputAction.CallbackContext context)
    {
        TryHandleCancel();
    }

    private bool TryHandleCancel()
    {
        if (_activeBinding == null || IsInputLocked || _lastCancelFrame == Time.frameCount)
            return false;

        _lastCancelFrame = Time.frameCount;
        if (_activeBinding.State == TitleScreenLocalState.Objectives && _objectivesPresenter != null)
        {
            _objectivesPresenter.HandleBackRequested();
            return true;
        }

        CloseCurrent();
        return true;
    }

    private static void SetVisibleImmediate(LocalScreenBinding binding, bool visible, bool interactive)
    {
        if (binding == null || binding.GameObject == null)
            return;
        binding.GameObject.SetActive(visible);
        if (binding.CanvasGroup != null)
        {
            binding.CanvasGroup.alpha = visible ? 1f : 0f;
            binding.CanvasGroup.interactable = visible && interactive;
            binding.CanvasGroup.blocksRaycasts = visible && interactive;
        }
        if (!visible && binding.AnimationRoot != null)
            binding.AnimationRoot.anchoredPosition = binding.RestingPosition;
    }

    private static void Focus(Selectable selectable)
    {
        Focus(selectable != null && selectable.gameObject.activeInHierarchy && selectable.IsInteractable()
            ? selectable.gameObject
            : null);
    }

    private IEnumerator FocusWhenReadable(GameObject target)
    {
        while (target != null && (IsInputLocked || !CanFocus(target)))
            yield return null;

        TryFocus(target);
        _focusRestore = null;
    }

    private static bool TryFocus(GameObject target)
    {
        if (!CanFocus(target))
            return false;
        Focus(target);
        return true;
    }

    private static bool CanFocus(GameObject target)
    {
        if (target == null || !target.activeInHierarchy)
            return false;
        Selectable selectable = target.GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
    }

    private static void Focus(GameObject target)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;
        if (target != null && target.activeInHierarchy)
            eventSystem.SetSelectedGameObject(target);
        else
            eventSystem.SetSelectedGameObject(null);
    }

}
