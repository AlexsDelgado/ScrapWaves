using TMPro;
using UnityEngine;

public enum OverheatWarningLifetimeMode
{
    Cycles,
    Duration
}

/// <summary>Moves and presents the saved warning view; never creates UI or animation objects at runtime.</summary>
[DisallowMultipleComponent]
public sealed class OverheatWarningHud : MonoBehaviour
{
    [Header("Authored view")]
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private PlayerPlatformState _playerPlatformState;
    [SerializeField] private GameObject _root;
    [SerializeField] private TextMeshProUGUI _text;
    [SerializeField] private CanvasGroup _visibility;
    [SerializeField] private RectTransform _motion;
    [SerializeField] private string _aboveGroundMessage = "OVERHEAT EVENT STARTING - GET TO GROUND LEVEL";
    [SerializeField] private string _groundMessage = "OVERHEAT EVENT STARTING - GET READY";

    [Header("Motion")]
    [SerializeField, Min(.1f), Tooltip("Gameplay seconds per complete right-to-left pass. The Travel Curve can add easing or a center hold.")]
    private float _cycleSeconds = 2.4f;
    [SerializeField, Tooltip("Normalized cycle time (0 to 1) to horizontal travel: +1 is beyond the right edge, 0 is centered, -1 is beyond the left edge.")]
    private AnimationCurve _travelCurve = AnimationCurve.Linear(0f, 1f, 1f, -1f);
    [SerializeField, Min(0f), Tooltip("Extra distance beyond the band edges, in Canvas units.")]
    private float _offscreenPadding = 32f;

    [Header("Display lifetime")]
    [SerializeField] private OverheatWarningLifetimeMode _lifetimeMode = OverheatWarningLifetimeMode.Cycles;
    [SerializeField, Min(1), Tooltip("Complete motion cycles per Overheat event when Lifetime Mode is Cycles.")]
    private int _cycleCount = 2;
    [SerializeField, Min(.1f), Tooltip("Gameplay seconds on screen when Lifetime Mode is Duration. May end during a cycle.")]
    private float _durationSeconds = 4.8f;

    private OverheatManager _subscribedManager;
    private float _elapsedDisplayTime;
    private bool _observedPreparation;
    private bool _displayActive;
    private bool _hasExhaustedBudget;
    private bool _hasMotionOrigin;
    private float _motionY;

    public float CycleSeconds => Mathf.Max(.1f, _cycleSeconds);
    public float DisplayBudgetSeconds => _lifetimeMode == OverheatWarningLifetimeMode.Cycles
        ? CycleSeconds * Mathf.Max(1, _cycleCount) : Mathf.Max(.1f, _durationSeconds);
    public float ElapsedDisplayTime => _elapsedDisplayTime;
    public int CompletedCycles => Mathf.FloorToInt(_elapsedDisplayTime / CycleSeconds);
    public bool HasExhaustedBudget => _hasExhaustedBudget;

    private void Awake()
    {
        BindLifecycle();
        CaptureMotionOrigin();
    }

    private void OnEnable()
    {
        BindLifecycle();
        CaptureMotionOrigin();
        Tick(0f);
    }

    private void Update() => Tick(Time.deltaTime);

    // Keep observing event boundaries while the component is disabled. Re-enabling the
    // same event must not grant another budget, and a genuinely new event must reset it.
    private void OnDisable() => SetVisible(false);
    private void OnDestroy() => UnbindLifecycle();

    private void BindLifecycle()
    {
        if (_subscribedManager == _overheatManager) return;
        UnbindLifecycle();
        _subscribedManager = _overheatManager;
        _observedPreparation = _displayActive = false;
        if (_subscribedManager == null) return;
        _subscribedManager.OnOverheatStarted += BeginDisplay;
        _subscribedManager.OnPreparationFinished += StopDisplay;
        _subscribedManager.OnOverheatFinished += OnOverheatFinished;
    }

    private void UnbindLifecycle()
    {
        if (_subscribedManager == null) return;
        _subscribedManager.OnOverheatStarted -= BeginDisplay;
        _subscribedManager.OnPreparationFinished -= StopDisplay;
        _subscribedManager.OnOverheatFinished -= OnOverheatFinished;
        _subscribedManager = null;
    }

    private void BeginDisplay()
    {
        _elapsedDisplayTime = 0f;
        _hasExhaustedBudget = false;
        _observedPreparation = _displayActive = true;
        if (isActiveAndEnabled) Tick(0f);
    }

    private void StopDisplay()
    {
        _displayActive = false;
        SetVisible(false);
    }

    private void OnOverheatFinished(OverheatEndReason reason)
    {
        _observedPreparation = false;
        StopDisplay();
    }

    private void Tick(float deltaTime)
    {
        if (_root == null || _text == null || _visibility == null || _motion == null) return;
        if (_overheatManager == null || !_overheatManager.IsPreparing)
        {
            if (_overheatManager == null || !_overheatManager.IsOverheating) _observedPreparation = false;
            StopDisplay();
            return;
        }
        // Covers a view first enabled after the start event without restarting an
        // already observed event when text, pause state, or component visibility changes.
        if (!_observedPreparation)
        {
            _elapsedDisplayTime = 0f;
            _hasExhaustedBudget = false;
            _observedPreparation = _displayActive = true;
        }
        if (_playerPlatformState == null || !_playerPlatformState.IsRunActive)
        {
            StopDisplay();
            return;
        }
        SetMessage(_playerPlatformState.IsBaseSupported);
        if (!isActiveAndEnabled || !_displayActive || _hasExhaustedBudget || !_playerPlatformState.IsSimulationActive)
        {
            SetVisible(false);
            return;
        }

        float budget = DisplayBudgetSeconds;
        _elapsedDisplayTime = Mathf.Min(budget, _elapsedDisplayTime + Mathf.Max(0f, deltaTime));
        ApplyMotion(_elapsedDisplayTime);
        if (_elapsedDisplayTime >= budget)
        {
            _hasExhaustedBudget = true;
            StopDisplay();
            return;
        }
        SetVisible(true);
    }

    private void SetMessage(bool onGround)
    {
        string message = onGround ? _groundMessage : _aboveGroundMessage;
        if (_text.text != message) _text.text = message;
    }

    private void CaptureMotionOrigin()
    {
        if (_hasMotionOrigin || _motion == null) return;
        _motionY = _motion.anchoredPosition.y;
        _hasMotionOrigin = true;
    }

    private void ApplyMotion(float elapsed)
    {
        if (_root.transform is not RectTransform band || _travelCurve == null || _travelCurve.length == 0) return;
        CaptureMotionOrigin();
        float phase = Mathf.Repeat(Mathf.Max(0f, elapsed) / CycleSeconds, 1f);
        float distance = (band.rect.width + _motion.rect.width) * .5f + Mathf.Max(0f, _offscreenPadding);
        _motion.anchoredPosition = new Vector2(_travelCurve.Evaluate(phase) * distance, _motionY);
    }

    private void SetVisible(bool visible)
    {
        if (_visibility == null) return;
        _visibility.alpha = visible ? 1f : 0f;
        _visibility.interactable = false;
        _visibility.blocksRaycasts = false;
    }

#if UNITY_EDITOR
    /// <summary>Samples the authored motion for editor rendering without altering the event's lifetime.</summary>
    public void PreviewAt(float seconds, bool onGround)
    {
        if (Application.isPlaying || _root == null || _text == null || _motion == null || _visibility == null) return;
        SetMessage(onGround);
        ApplyMotion(seconds);
        SetVisible(seconds >= 0f && seconds < DisplayBudgetSeconds);
    }

    private void OnValidate()
    {
        _cycleSeconds = Mathf.Max(.1f, _cycleSeconds);
        _cycleCount = Mathf.Max(1, _cycleCount);
        _durationSeconds = Mathf.Max(.1f, _durationSeconds);
        _offscreenPadding = Mathf.Max(0f, _offscreenPadding);
    }
#endif
}
