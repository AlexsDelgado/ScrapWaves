using TMPro;
using UnityEngine;

/// <summary>Scrolls the authored warning while the manager is waiting for grounded preparation.</summary>
[DisallowMultipleComponent]
public sealed class OverheatWarningHud : MonoBehaviour
{
    [Header("Authored view")]
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private PlayerPlatformState _playerPlatformState;
    [SerializeField] private GameObject _root;
    [SerializeField] private TextMeshProUGUI _text;
    [SerializeField, Tooltip("Second saved text view for a continuous marquee. No UI is created at runtime.")]
    private TextMeshProUGUI _repeatText;
    [SerializeField] private CanvasGroup _visibility;
    [SerializeField] private RectTransform _motion;
    [SerializeField] private string _aboveGroundMessage = "OVERHEAT EVENT STARTING - GET TO GROUND LEVEL";
    [SerializeField] private string _groundMessage = "OVERHEAT EVENT STARTING - GET READY";

    [Header("Readability — Canvas units")]
    [SerializeField, Min(1f), Tooltip("Right-to-left travel in Canvas units per gameplay second. Lower values scroll more slowly. This never controls warning lifetime.")]
    private float _scrollSpeed = 180f;
    [SerializeField, Min(16f), Tooltip("Fixed text size for both saved labels. Auto-sizing and wrapping are disabled so a long message scrolls instead of shrinking to fit.")]
    private float _fontSize = 96f;
    [SerializeField, Min(32f), Tooltip("Warning bar height in Canvas units. Anchors, vertical placement, colors and rule artwork remain authored on the UI objects.")]
    private float _barHeight = 160f;
    [SerializeField, Min(0f), Tooltip("Space between consecutive copies of the message in Canvas units. Clamped below the viewport width so the marquee has no empty pass.")]
    private float _messageGap = 120f;

    private OverheatManager _subscribedManager;
    private bool _observedPreparation;
    private bool _hasMotionOrigin;
    private float _motionY;
    private float _elapsedDisplayTime;
    private float _scrollDistance;
    private float _messageWidth;
    private float _repeatDistance;
    private float _lastViewportWidth = -1f;
    private float _lastFontSize = -1f;
    private float _lastBarHeight = -1f;
    private float _lastMessageGap = -1f;
    private string _layoutMessage;

    public float ScrollSpeed => Mathf.Max(1f, _scrollSpeed);
    public float FontSize => Mathf.Max(16f, _fontSize);
    public float BarHeight => Mathf.Max(32f, _barHeight);
    public float ElapsedDisplayTime => _elapsedDisplayTime;

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
    // Keep event subscriptions while disabled so event end/new event cannot reopen stale content.
    private void OnDisable() => SetVisible(false);
    private void OnDestroy() => UnbindLifecycle();

    private void BindLifecycle()
    {
        if (_subscribedManager == _overheatManager) return;
        UnbindLifecycle();
        _subscribedManager = _overheatManager;
        _observedPreparation = false;
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
        _elapsedDisplayTime = _scrollDistance = 0f;
        _observedPreparation = true;
        if (isActiveAndEnabled) Tick(0f);
    }

    private void StopDisplay()
    {
        _observedPreparation = false;
        SetVisible(false);
    }

    private void OnOverheatFinished(OverheatEndReason reason) => StopDisplay();

    private void Tick(float deltaTime)
    {
        BindLifecycle();
        if (_root == null || _text == null || _visibility == null || _motion == null ||
            _overheatManager == null || !_overheatManager.IsPreparing ||
            _playerPlatformState == null || !_playerPlatformState.IsRunActive)
        {
            StopDisplay();
            return;
        }
        if (!_observedPreparation)
        {
            _elapsedDisplayTime = _scrollDistance = 0f;
            _observedPreparation = true;
        }
        // The manager owns the countdown and ground gate. The HUD has no second duration.
        SetMessage(!_overheatManager.IsAwaitingGround);
        EnsureLayout();
        if (!isActiveAndEnabled || !_playerPlatformState.IsSimulationActive)
        {
            SetVisible(false);
            return;
        }

        float elapsed = Mathf.Max(0f, deltaTime);
        _elapsedDisplayTime += elapsed;
        _scrollDistance = Mathf.Repeat(_scrollDistance + elapsed * ScrollSpeed, Mathf.Max(1f, _repeatDistance));
        ApplyMotion();
        SetVisible(true);
    }

    private void SetMessage(bool onGround)
    {
        string message = onGround ? _groundMessage : _aboveGroundMessage;
        if (_text.text != message)
        {
            _text.text = message;
            // A transition immediately shows the new instruction at the left edge, never offscreen.
            _scrollDistance = 0f;
        }
        if (_repeatText != null && _repeatText.text != message) _repeatText.text = message;
    }

    private void CaptureMotionOrigin()
    {
        if (_hasMotionOrigin || _motion == null) return;
        _motionY = _motion.anchoredPosition.y;
        _hasMotionOrigin = true;
    }

    private void EnsureLayout(bool force = false)
    {
        if (_root == null || _root.transform is not RectTransform band || _text == null || _motion == null) return;
        float viewportWidth = Mathf.Max(1f, band.rect.width);
        if (!force && _lastViewportWidth == viewportWidth && _lastFontSize == FontSize &&
            _lastBarHeight == BarHeight && _lastMessageGap == _messageGap && _layoutMessage == _text.text) return;

        CaptureMotionOrigin();
        band.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, BarHeight);
        ConfigureText(_text);
        if (_repeatText != null) ConfigureText(_repeatText);
        _messageWidth = Mathf.Max(viewportWidth, _text.GetPreferredValues(_text.text, Mathf.Infinity, Mathf.Infinity).x + 8f);
        _repeatDistance = _messageWidth + Mathf.Clamp(_messageGap, 0f, viewportWidth * .5f);
        ConfigureRect(_motion);
        if (_repeatText != null) ConfigureRect(_repeatText.rectTransform);
        _lastViewportWidth = viewportWidth;
        _lastFontSize = FontSize;
        _lastBarHeight = BarHeight;
        _lastMessageGap = _messageGap;
        _layoutMessage = _text.text;
    }

    private void ConfigureText(TextMeshProUGUI label)
    {
        label.enableAutoSizing = false;
        label.fontSize = FontSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
    }

    private void ConfigureRect(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.sizeDelta = new Vector2(_messageWidth, BarHeight);
    }

    private void ApplyMotion()
    {
        if (_motion == null) return;
        _motion.anchoredPosition = new Vector2(-_scrollDistance, _motionY);
        if (_repeatText != null)
            _repeatText.rectTransform.anchoredPosition = new Vector2(_repeatDistance - _scrollDistance, _motionY);
    }

    private void SetVisible(bool visible)
    {
        if (_visibility == null) return;
        _visibility.alpha = visible ? 1f : 0f;
        _visibility.interactable = false;
        _visibility.blocksRaycasts = false;
    }

#if UNITY_EDITOR
    /// <summary>Samples the saved continuous marquee without changing manager state or creating UI.</summary>
    public void PreviewAt(float seconds, bool onGround)
    {
        if (Application.isPlaying || _root == null || _text == null || _motion == null || _visibility == null) return;
        SetMessage(onGround);
        EnsureLayout();
        _scrollDistance = Mathf.Repeat(Mathf.Max(0f, seconds) * ScrollSpeed, Mathf.Max(1f, _repeatDistance));
        ApplyMotion();
        SetVisible(seconds >= 0f);
    }

    private void OnValidate()
    {
        _scrollSpeed = Mathf.Max(1f, _scrollSpeed);
        _fontSize = Mathf.Max(16f, _fontSize);
        _barHeight = Mathf.Max(32f, _barHeight);
        _messageGap = Mathf.Max(0f, _messageGap);
        // Defer UI layout until serialization finishes; editing Inspector values previews their geometry.
        UnityEditor.EditorApplication.delayCall -= RefreshEditorLayout;
        UnityEditor.EditorApplication.delayCall += RefreshEditorLayout;
    }

    private void RefreshEditorLayout()
    {
        if (this == null || Application.isPlaying) return;
        EnsureLayout(true);
        ApplyMotion();
    }
#endif
}
