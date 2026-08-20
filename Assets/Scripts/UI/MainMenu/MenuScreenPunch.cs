using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MenuScreenPunch : MonoBehaviour
{
    [Header("Authored animation targets")]
    [SerializeField] private RectTransform _uiPresentationRoot;
    [SerializeField] private RectTransform _showcaseRoot;
    [SerializeField] private Image _navigationFlash;
    [SerializeField] private MainMenuPresentationProfile _profile;

    [Header("Per-instance overrides")]
    [SerializeField] private bool _useProfileValues = true;
    [SerializeField] private Vector2 _distance = new(10f, -7f);
    [SerializeField, Range(0f, 2f)] private float _rotation = 0.55f;
    [SerializeField, Min(0.01f)] private float _duration = 0.1f;
    [SerializeField, Range(0f, 1f)] private float _hoverMultiplier = 0.4f;
    [SerializeField, Range(0f, 1f)] private float _showcaseMultiplier = 0.58f;
    [SerializeField, Range(0f, 0.2f)] private float _flashOpacity = 0.06f;
    [SerializeField, Min(0.01f)] private float _flashDuration = 0.09f;
    [SerializeField, Min(0f)] private float _cooldown = 0.035f;
    [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private Vector2 _uiRestingPosition;
    private Quaternion _uiRestingRotation;
    private Vector2 _showcaseRestingPosition;
    private Quaternion _showcaseRestingRotation;
    private bool _captured;
    private bool _playing;
    private float _elapsed;
    private float _direction = 1f;
    private float _intensity = 1f;
    private float _nextFullPunchTime;
    private bool _reducedMotion;
    private bool _screenShakeEnabled = true;
    private bool _screenFlashEnabled = true;

    private Vector2 Distance => _useProfileValues && _profile != null ? _profile.NavigationPunchDistance : _distance;
    private float Rotation => _useProfileValues && _profile != null ? _profile.NavigationPunchRotation : _rotation;
    private float Duration => _useProfileValues && _profile != null ? _profile.PunchDuration : _duration;
    private float HoverMultiplier => _useProfileValues && _profile != null ? _profile.HoverPunchMultiplier : _hoverMultiplier;
    private float FlashOpacity => _useProfileValues && _profile != null ? _profile.FlashOpacity : _flashOpacity;
    private float FlashDuration => _useProfileValues && _profile != null ? _profile.FlashDuration : _flashDuration;
    private float Cooldown => _useProfileValues && _profile != null ? _profile.PunchCooldown : _cooldown;
    private AnimationCurve Curve => _useProfileValues && _profile != null && _profile.PunchCurve != null ? _profile.PunchCurve : _curve;

    private void Awake()
    {
        CaptureRestingPose();
        SetFlashAlpha(0f);
    }

    private void OnDisable()
    {
        RestoreRestingPose();
        SetFlashAlpha(0f);
        _playing = false;
    }

    private void Update()
    {
        if (!_playing)
            return;

        _elapsed += Time.unscaledDeltaTime;
        float normalized = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, Duration));
        float displacement = Curve != null ? Curve.Evaluate(normalized) : 1f - normalized;
        ApplyPunch(displacement * _intensity * _direction);

        if (_navigationFlash != null)
        {
            float flashNormalized = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, FlashDuration));
            float triangle = 1f - Mathf.Abs(flashNormalized * 2f - 1f);
            SetFlashAlpha(_screenFlashEnabled ? FlashOpacity * triangle * _intensity : 0f);
        }

        if (normalized < 1f)
            return;

        _playing = false;
        RestoreRestingPose();
        SetFlashAlpha(0f);
    }

    public void ApplyPreferences(bool reducedMotion, bool screenShakeEnabled, bool screenFlashEnabled)
    {
        _reducedMotion = reducedMotion;
        _screenShakeEnabled = screenShakeEnabled;
        _screenFlashEnabled = screenFlashEnabled;
        if (_reducedMotion || !_screenShakeEnabled)
            RestoreRestingPose();
        if (!_screenFlashEnabled)
            SetFlashAlpha(0f);
    }

    public void Play(int navigationDirection, bool pointer)
    {
        CaptureRestingPose();
        float now = Time.unscaledTime;
        float intensity = pointer ? HoverMultiplier : 1f;
        if (!pointer && now < _nextFullPunchTime)
            intensity *= 0.45f;
        else if (!pointer)
            _nextFullPunchTime = now + Cooldown;

        _direction = navigationDirection < 0 ? -1f : 1f;
        _intensity = intensity;
        _elapsed = 0f;
        _playing = (!_reducedMotion && _screenShakeEnabled) || _screenFlashEnabled;
        if (!_playing)
        {
            RestoreRestingPose();
            SetFlashAlpha(0f);
        }
    }

    private void CaptureRestingPose()
    {
        if (_captured)
            return;

        if (_uiPresentationRoot != null)
        {
            _uiRestingPosition = _uiPresentationRoot.anchoredPosition;
            _uiRestingRotation = _uiPresentationRoot.localRotation;
        }

        if (_showcaseRoot != null)
        {
            _showcaseRestingPosition = _showcaseRoot.anchoredPosition;
            _showcaseRestingRotation = _showcaseRoot.localRotation;
        }

        _captured = true;
    }

    private void ApplyPunch(float amount)
    {
        bool allowMotion = !_reducedMotion && _screenShakeEnabled;
        if (_uiPresentationRoot != null)
        {
            _uiPresentationRoot.anchoredPosition = allowMotion
                ? _uiRestingPosition + Distance * amount
                : _uiRestingPosition;
            _uiPresentationRoot.localRotation = allowMotion
                ? _uiRestingRotation * Quaternion.Euler(0f, 0f, Rotation * amount)
                : _uiRestingRotation;
        }

        if (_showcaseRoot != null)
        {
            _showcaseRoot.anchoredPosition = allowMotion
                ? _showcaseRestingPosition + Distance * (amount * _showcaseMultiplier)
                : _showcaseRestingPosition;
            _showcaseRoot.localRotation = allowMotion
                ? _showcaseRestingRotation * Quaternion.Euler(0f, 0f, Rotation * amount * _showcaseMultiplier)
                : _showcaseRestingRotation;
        }
    }

    private void RestoreRestingPose()
    {
        if (!_captured)
            return;
        if (_uiPresentationRoot != null)
        {
            _uiPresentationRoot.anchoredPosition = _uiRestingPosition;
            _uiPresentationRoot.localRotation = _uiRestingRotation;
        }
        if (_showcaseRoot != null)
        {
            _showcaseRoot.anchoredPosition = _showcaseRestingPosition;
            _showcaseRoot.localRotation = _showcaseRestingRotation;
        }
    }

    private void SetFlashAlpha(float alpha)
    {
        if (_navigationFlash == null)
            return;
        Color color = _navigationFlash.color;
        color.a = Mathf.Clamp01(alpha);
        _navigationFlash.color = color;
    }
}
