using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class MainMenuItemView : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
{
    [Header("Authored references")]
    [SerializeField] private Button _button;
    [SerializeField] private RectTransform _visualRoot;
    [SerializeField] private CanvasGroup _visualCanvasGroup;
    [SerializeField] private Graphic _plate;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private Graphic _selectedEdge;
    [SerializeField] private GameObject _selectedNotch;
    [SerializeField] private GameObject _developerTag;
    [SerializeField] private ParticleSystem _selectionSparks;
    [SerializeField] private MainMenuPresentationProfile _profile;

    [Header("Per-instance overrides")]
    [SerializeField] private bool _useProfileStateValues = true;
    [SerializeField, Range(0.8f, 1.1f)] private float _unselectedScale = 0.95f;
    [SerializeField, Range(1f, 1.3f)] private float _selectedScale = 1.1f;
    [SerializeField] private Vector2 _selectedOffset = new(-24f, 6f);
    [SerializeField, Min(0.01f)] private float _focusDuration = 0.12f;
    [SerializeField, Range(0.8f, 1f)] private float _pressScaleMultiplier = 0.96f;
    [SerializeField, Min(0.01f)] private float _pressDuration = 0.06f;
    [SerializeField] private AnimationCurve _focusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private Color _normalPlateColor = new(0.122f, 0.145f, 0.133f, 1f);
    [SerializeField] private Color _normalLabelColor = new(0.949f, 0.961f, 0.922f, 0.88f);
    [SerializeField] private Color _selectedPlateColor = new(0.659f, 0.78f, 0.561f, 1f);
    [SerializeField] private Color _selectedLabelColor = new(0.035f, 0.043f, 0.039f, 1f);
    [SerializeField] private bool _developerEntry;

    private MainMenuPresentationController _presentationController;
    private Vector2 _restingPosition;
    private Vector3 _restingScale = Vector3.one;
    private bool _restingPoseCaptured;
    private bool _selected;
    private bool _stateAnimating;
    private float _stateElapsed;
    private Vector2 _stateStartPosition;
    private Vector3 _stateStartScale;
    private Color _stateStartPlateColor;
    private Color _stateStartLabelColor;
    private float _pressRemaining;
    private bool _introAnimating;
    private float _introDelay;
    private float _introElapsed;
    private float _introDuration;
    private Vector2 _introStartPosition;
    private Vector3 _introStartScale;

    public Button Button => _button;
    public RectTransform VisualRoot => _visualRoot;
    public bool IsSelected => _selected;
    public bool IsDeveloperEntry => _developerEntry;
    public string Label => _label != null ? _label.text : name;
    public bool HasRequiredReferences => _button != null && _visualRoot != null && _plate != null && _label != null;
    public bool IsReadable => gameObject.activeInHierarchy &&
                              (_visualCanvasGroup == null || _visualCanvasGroup.alpha >= 0.5f);

    private float UnselectedScale => _useProfileStateValues && _profile != null ? _profile.UnselectedScale : _unselectedScale;
    private float SelectedScale => _useProfileStateValues && _profile != null ? _profile.SelectedScale : _selectedScale;
    private Vector2 SelectedOffset => _useProfileStateValues && _profile != null ? _profile.SelectedOffset : _selectedOffset;
    private float FocusDuration => _useProfileStateValues && _profile != null ? _profile.FocusDuration : _focusDuration;
    private float PressScaleMultiplier => _useProfileStateValues && _profile != null ? _profile.PressScaleMultiplier : _pressScaleMultiplier;
    private float PressDuration => _useProfileStateValues && _profile != null ? _profile.PressDuration : _pressDuration;
    private AnimationCurve FocusCurve => _useProfileStateValues && _profile != null && _profile.FocusCurve != null
        ? _profile.FocusCurve
        : _focusCurve;

    private void Awake()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        CaptureRestingPose();
        if (_developerTag != null)
            _developerTag.SetActive(_developerEntry);
    }

    private void OnEnable()
    {
        CaptureRestingPose();
    }

    private void Update()
    {
        if (_visualRoot == null)
            return;

        float deltaTime = Time.unscaledDeltaTime;
        if (_introAnimating)
        {
            TickIntro(deltaTime);
            return;
        }

        if (_pressRemaining > 0f)
        {
            _pressRemaining = Mathf.Max(0f, _pressRemaining - deltaTime);
            float baseScale = _selected ? SelectedScale : UnselectedScale;
            float pressedScale = baseScale * PressScaleMultiplier;
            float normalized = 1f - _pressRemaining / Mathf.Max(0.001f, PressDuration);
            float recovery = normalized < 0.5f
                ? Mathf.Lerp(baseScale, pressedScale, normalized * 2f)
                : Mathf.Lerp(pressedScale, baseScale, (normalized - 0.5f) * 2f);
            _visualRoot.localScale = Vector3.Scale(_restingScale, Vector3.one * recovery);
            if (_pressRemaining <= 0f)
                BeginStateAnimation();
            return;
        }

        if (_stateAnimating)
            TickState(deltaTime);
    }

    public void SetPresentationController(MainMenuPresentationController controller)
    {
        _presentationController = controller;
    }

    public void SetSelected(bool selected, bool immediate)
    {
        _selected = selected;
        if (_selectedNotch != null)
            _selectedNotch.SetActive(selected);
        if (_selectedEdge != null)
            _selectedEdge.gameObject.SetActive(selected);

        if (immediate)
        {
            _stateAnimating = false;
            ApplyState(1f);
            return;
        }

        BeginStateAnimation();
        if (selected && _selectionSparks != null)
            _selectionSparks.Play(true);
    }

    public void PlayPressed()
    {
        if (_button == null || !_button.IsInteractable())
            return;

        _stateAnimating = false;
        _pressRemaining = PressDuration;
    }

    public void PrepareIntro(Vector2 offset, float scale, float delay, float duration)
    {
        CaptureRestingPose();
        if (_visualRoot == null)
            return;

        _introDelay = Mathf.Max(0f, delay);
        _introDuration = Mathf.Max(0.01f, duration);
        _introElapsed = 0f;
        _introStartPosition = _restingPosition + offset;
        _introStartScale = Vector3.Scale(_restingScale, Vector3.one * Mathf.Max(0.01f, scale));
        _visualRoot.anchoredPosition = _introStartPosition;
        _visualRoot.localScale = _introStartScale;
        if (_visualCanvasGroup != null)
            _visualCanvasGroup.alpha = 0f;
        _introAnimating = true;
        _stateAnimating = false;
    }

    public void CompleteIntroImmediately()
    {
        _introAnimating = false;
        if (_visualCanvasGroup != null)
            _visualCanvasGroup.alpha = 1f;
        ApplyState(1f);
    }

    public void OnSelect(BaseEventData eventData)
    {
        _presentationController?.NotifySelected(this, false);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (_presentationController == null)
            SetSelected(false, false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button == null || !_button.IsActive() || !_button.IsInteractable())
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject != gameObject)
        {
            _presentationController?.NotifyPointerFocus(this);
            eventSystem.SetSelectedGameObject(gameObject, eventData);
        }
    }

    private void CaptureRestingPose()
    {
        if (_restingPoseCaptured || _visualRoot == null)
            return;

        _restingPosition = _visualRoot.anchoredPosition;
        // Guard against a degenerate (zero) scale read: in player builds the visual
        // root can report a zero localScale on the first frame, which would otherwise
        // be baked into every state/intro pose and leave the item permanently invisible.
        Vector3 capturedScale = _visualRoot.localScale;
        _restingScale = capturedScale.x != 0f && capturedScale.y != 0f && capturedScale.z != 0f
            ? capturedScale
            : Vector3.one;
        _restingPoseCaptured = true;
    }

    private void BeginStateAnimation()
    {
        if (_visualRoot == null)
            return;

        _stateStartPosition = _visualRoot.anchoredPosition;
        _stateStartScale = _visualRoot.localScale;
        _stateStartPlateColor = _plate != null ? _plate.color : Color.white;
        _stateStartLabelColor = _label != null ? _label.color : Color.white;
        _stateElapsed = 0f;
        _stateAnimating = true;
    }

    private void TickState(float deltaTime)
    {
        _stateElapsed += deltaTime;
        float normalized = Mathf.Clamp01(_stateElapsed / Mathf.Max(0.01f, FocusDuration));
        float curved = FocusCurve != null ? FocusCurve.Evaluate(normalized) : normalized;
        ApplyState(curved);
        if (normalized >= 1f)
            _stateAnimating = false;
    }

    private void ApplyState(float t)
    {
        if (_visualRoot == null)
            return;

        Vector2 targetPosition = _restingPosition + (_selected ? SelectedOffset : Vector2.zero);
        float scale = _selected ? SelectedScale : UnselectedScale;
        Vector3 targetScale = Vector3.Scale(_restingScale, Vector3.one * scale);

        if (_stateAnimating && t < 1f)
        {
            _visualRoot.anchoredPosition = Vector2.LerpUnclamped(_stateStartPosition, targetPosition, t);
            _visualRoot.localScale = Vector3.LerpUnclamped(_stateStartScale, targetScale, t);
            if (_plate != null)
                _plate.color = Color.LerpUnclamped(_stateStartPlateColor, _selected ? _selectedPlateColor : _normalPlateColor, t);
            if (_label != null)
                _label.color = Color.LerpUnclamped(_stateStartLabelColor, _selected ? _selectedLabelColor : _normalLabelColor, t);
            return;
        }

        _visualRoot.anchoredPosition = targetPosition;
        _visualRoot.localScale = targetScale;
        if (_plate != null)
            _plate.color = _selected ? _selectedPlateColor : _normalPlateColor;
        if (_label != null)
            _label.color = _selected ? _selectedLabelColor : _normalLabelColor;
    }

    private void TickIntro(float deltaTime)
    {
        _introElapsed += deltaTime;
        if (_introElapsed < _introDelay)
            return;

        float normalized = Mathf.Clamp01((_introElapsed - _introDelay) / _introDuration);
        AnimationCurve curve = _profile != null ? _profile.EntranceCurve : null;
        float curved = curve != null ? curve.Evaluate(normalized) : normalized;
        Vector2 targetPosition = _restingPosition + (_selected ? SelectedOffset : Vector2.zero);
        float targetScaleValue = _selected ? SelectedScale : UnselectedScale;
        Vector3 targetScale = Vector3.Scale(_restingScale, Vector3.one * targetScaleValue);
        _visualRoot.anchoredPosition = Vector2.LerpUnclamped(_introStartPosition, targetPosition, curved);
        _visualRoot.localScale = Vector3.LerpUnclamped(_introStartScale, targetScale, curved);
        if (_visualCanvasGroup != null)
            _visualCanvasGroup.alpha = normalized;

        if (normalized < 1f)
            return;

        _introAnimating = false;
        if (_visualCanvasGroup != null)
            _visualCanvasGroup.alpha = 1f;
        ApplyState(1f);
    }
}
