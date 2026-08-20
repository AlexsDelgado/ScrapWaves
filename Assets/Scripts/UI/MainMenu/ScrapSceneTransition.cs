using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-180)]
public sealed class ScrapSceneTransition : MonoBehaviour
{
    [Serializable]
    public sealed class RectTransformTarget
    {
        [SerializeField] private RectTransform _target;
        [SerializeField] private Vector2 _coveredAnchoredPosition;
        [SerializeField] private float _coveredRotation;

        private Vector2 _openAnchoredPosition;
        private Vector3 _openLocalEulerAngles;
        private bool _hasOpenPose;

        public RectTransformTarget()
        {
        }

        public RectTransformTarget(
            RectTransform target,
            Vector2 coveredAnchoredPosition,
            float coveredRotation = 0f)
        {
            _target = target;
            _coveredAnchoredPosition = coveredAnchoredPosition;
            _coveredRotation = coveredRotation;
        }

        internal bool IsAssigned => _target != null;

        internal bool CacheOpenPose()
        {
            if (_target == null)
                return false;

            _openAnchoredPosition = _target.anchoredPosition;
            _openLocalEulerAngles = _target.localEulerAngles;
            _hasOpenPose = true;
            return true;
        }

        internal void Apply(float coveredAmount)
        {
            if (_target == null || !_hasOpenPose)
                return;

            _target.anchoredPosition = Vector2.LerpUnclamped(
                _openAnchoredPosition,
                _coveredAnchoredPosition,
                coveredAmount);

            Vector3 rotation = _openLocalEulerAngles;
            rotation.z = Mathf.LerpAngle(_openLocalEulerAngles.z, _coveredRotation, coveredAmount);
            _target.localEulerAngles = rotation;
        }
    }

    private enum TransitionState
    {
        Idle,
        Warning,
        Covering,
        WaitingForScene,
        Revealing
    }

    [Header("Authored overlay references")]
    [SerializeField] private CanvasGroup _overlay;
    [SerializeField] private RectTransformTarget _warningBlade = new();
    [SerializeField] private RectTransformTarget[] _coverPlates = Array.Empty<RectTransformTarget>();
    [SerializeField] private RectTransform _impactShakeRoot;
    [SerializeField] private Graphic _impactFlash;
    [SerializeField] private ParticleSystem _impactParticles;
    [SerializeField] private AudioSource _audioSource;

    [Header("Authored audio")]
    [SerializeField] private AudioClip _warningClip;
    [SerializeField] private AudioClip _impactClip;
    [SerializeField] private AudioClip _revealClip;
    [SerializeField, Range(0f, 1f)] private float _warningVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float _impactVolume = 0.95f;
    [SerializeField, Range(0f, 1f)] private float _revealVolume = 0.7f;

    [Header("Timing (unscaled seconds)")]
    [SerializeField, Min(0f)] private float _warningBladeDuration = 0.1f;
    [SerializeField, Min(0f)] private float _coverDuration = 0.38f;
    [SerializeField, Min(0f)] private float _minimumCoveredHold = 0.08f;
    [SerializeField, Min(0f)] private float _revealDuration = 0.3f;
    [SerializeField, Min(0.01f)] private float _sceneLoadTimeout = 15f;
    [SerializeField, Min(0f)] private float _reducedMotionFadeDuration = 0.1f;

    [Header("Motion")]
    [SerializeField] private AnimationCurve _coverCurve = new(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.72f, 1.035f, 3f, 0f),
        new Keyframe(1f, 1f, 0f, 0f));
    [SerializeField] private AnimationCurve _revealCurve = new(
        new Keyframe(0f, 0f, 0f, 4f),
        new Keyframe(1f, 1f, 0f, 0f));
    [SerializeField] private Vector2 _impactShakeDistance = new(9f, -6f);
    [SerializeField, Range(0f, 2f)] private float _impactShakeRotation = 0.6f;
    [SerializeField, Min(0f)] private float _impactFeedbackDuration = 0.065f;
    [SerializeField] private AnimationCurve _impactShakeCurve = new(
        new Keyframe(0f, 1f),
        new Keyframe(0.45f, -0.18f),
        new Keyframe(1f, 0f));

    [Header("Flash and particles")]
    [SerializeField, Range(0f, 0.2f)] private float _impactFlashOpacity = 0.08f;
    [SerializeField, Min(0)] private int _impactParticleCount = 14;
    [SerializeField, Min(0)] private int _reducedMotionImpactParticleCount = 4;

    private static ScrapSceneTransition _instance;

    private TransitionState _state;
    private SceneDestination _destination;
    private string _expectedSceneName;
    private float _phaseElapsed;
    private bool _sceneLoadedObserved;
    private bool _routeInvoked;
    private bool _usesReducedMotion;
    private bool _ownsSingleton;
    private bool _authoredReferencesValid;
    private bool _validationErrorLogged;
    private Vector2 _impactOpenAnchoredPosition;
    private Vector3 _impactOpenLocalEulerAngles;
    private Color _impactFlashBaseColor;

    public static ScrapSceneTransition Instance => _instance;
    public bool IsTransitioning => _state != TransitionState.Idle;
    public SceneDestination Destination => _destination;

    public event Action<bool> TransitioningChanged;

    internal Func<SceneDestination, bool> Route { get; set; } = SceneNavigation.Load;
    internal Func<bool> ReducedMotion { get; set; } = ReadReducedMotion;
    internal Func<float> SfxVolume { get; set; } = ReadSfxVolume;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogError(
                "ScrapSceneTransition: multiple authored transition overlays were found. " +
                "Keep exactly one persistent transition root.",
                this);
            HideDuplicateOverlay();
            gameObject.SetActive(false);
            return;
        }

        _instance = this;
        _ownsSingleton = true;
        CacheAuthoredState();
        SetIdleVisualState();

        if (Application.isPlaying)
            DontDestroyOnLoad(transform.root.gameObject);
    }

    private void OnEnable()
    {
        if (_ownsSingleton)
            SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (_ownsSingleton && IsTransitioning)
            CompleteTransition();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (_instance == this)
            _instance = null;
    }

    private void Update()
    {
        if (IsTransitioning)
            Advance(Time.unscaledDeltaTime);
    }

    public bool TryLoad(SceneDestination destination)
    {
        if (!isActiveAndEnabled || IsTransitioning)
            return false;

        if (!ValidateAuthoredReferences())
            return false;

        try
        {
            _expectedSceneName = SceneNavigation.GetSceneName(destination);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Debug.LogError(
                $"ScrapSceneTransition cannot route unknown destination '{destination}'. {exception.Message}",
                this);
            return false;
        }

        _destination = destination;
        _routeInvoked = false;
        _sceneLoadedObserved = false;
        _usesReducedMotion = IsReducedMotionEnabled();
        _phaseElapsed = 0f;

        ResetImpactFeedback();
        SetOverlayActive(true);
        if (_usesReducedMotion)
        {
            // Reduced Motion avoids the full-screen jaw travel. The authored plates
            // snap behind a transparent input blocker, then their already-opaque
            // covered composition fades in before routing.
            ApplyReducedMotionCover(0f);
            _state = TransitionState.Covering;
        }
        else
        {
            ApplyWarningBlade(0f);
            ApplyCoverPlates(0f);
            _state = TransitionState.Warning;
        }

        PlayOneShot(_warningClip, _warningVolume);
        TransitioningChanged?.Invoke(true);
        return true;
    }

    internal void AdvanceForTesting(float unscaledDeltaTime)
    {
        Advance(unscaledDeltaTime);
    }

    internal void NotifySceneLoadedForTesting()
    {
        if (_state == TransitionState.WaitingForScene)
            _sceneLoadedObserved = true;
    }

    private void Advance(float unscaledDeltaTime)
    {
        float remaining = Mathf.Max(0f, unscaledDeltaTime);

        // One update can cross short or zero-duration phases. The guard prevents a
        // malformed configuration from making this state machine spin forever.
        for (int guard = 0; guard < 8 && IsTransitioning; guard++)
        {
            switch (_state)
            {
                case TransitionState.Warning:
                    if (!AdvanceTimedPhase(
                            ref remaining,
                            _warningBladeDuration,
                            ApplyWarningBlade))
                    {
                        return;
                    }

                    _state = TransitionState.Covering;
                    _phaseElapsed = 0f;
                    break;

                case TransitionState.Covering:
                    float coverDuration = _usesReducedMotion
                        ? _reducedMotionFadeDuration
                        : _coverDuration;
                    Action<float> coverAction = _usesReducedMotion
                        ? ApplyReducedMotionCover
                        : ApplyCoverPlates;
                    if (!AdvanceTimedPhase(ref remaining, coverDuration, coverAction))
                    {
                        return;
                    }

                    EnterFullyCoveredState();
                    break;

                case TransitionState.WaitingForScene:
                    if (!AdvanceCoveredHold(ref remaining))
                        return;
                    break;

                case TransitionState.Revealing:
                    float revealDuration = _usesReducedMotion
                        ? _reducedMotionFadeDuration
                        : _revealDuration;
                    Action<float> revealAction = _usesReducedMotion
                        ? ApplyReducedMotionReveal
                        : ApplyReveal;
                    if (!AdvanceTimedPhase(ref remaining, revealDuration, revealAction))
                    {
                        return;
                    }

                    CompleteTransition();
                    return;

                default:
                    return;
            }
        }
    }

    private bool AdvanceTimedPhase(ref float remaining, float duration, Action<float> apply)
    {
        if (duration <= 0f)
        {
            apply(1f);
            return true;
        }

        float step = Mathf.Min(remaining, Mathf.Max(0f, duration - _phaseElapsed));
        _phaseElapsed += step;
        remaining -= step;

        float progress = Mathf.Clamp01(_phaseElapsed / duration);
        apply(progress);
        if (_phaseElapsed + 0.00001f < duration)
            return false;

        _phaseElapsed = duration;
        apply(1f);
        return true;
    }

    private bool AdvanceCoveredHold(ref float remaining)
    {
        float targetTime = _sceneLoadedObserved
            ? Mathf.Max(0f, _minimumCoveredHold)
            : Mathf.Max(_minimumCoveredHold, _sceneLoadTimeout);

        if (_phaseElapsed + 0.00001f < targetTime)
        {
            float step = Mathf.Min(remaining, targetTime - _phaseElapsed);
            _phaseElapsed += step;
            remaining -= step;
            UpdateImpactFeedback(_phaseElapsed);

            if (_phaseElapsed + 0.00001f < targetTime)
                return false;
        }

        if (!_sceneLoadedObserved)
        {
            Debug.LogError(
                $"ScrapSceneTransition timed out waiting for '{_expectedSceneName}' to report sceneLoaded. " +
                "The current scene will be uncovered.",
                this);
        }

        BeginReveal();
        return true;
    }

    private void EnterFullyCoveredState()
    {
        ApplyWarningBlade(1f);
        ApplyCoverPlates(1f);
        _state = TransitionState.WaitingForScene;
        _phaseElapsed = 0f;
        _sceneLoadedObserved = false;
        BeginImpactFeedback();

        if (_routeInvoked)
            return;

        _routeInvoked = true;
        bool loadStarted;

        try
        {
            Func<SceneDestination, bool> route = Route ?? SceneNavigation.Load;
            loadStarted = route(_destination);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"ScrapSceneTransition failed to start route '{_destination}': {exception.Message}",
                this);
            loadStarted = false;
        }

        if (!loadStarted)
        {
            Debug.LogError(
                $"ScrapSceneTransition could not load '{_expectedSceneName}'. " +
                "The current scene will be uncovered.",
                this);
            BeginReveal();
        }
    }

    private void BeginReveal()
    {
        if (_state == TransitionState.Revealing || _state == TransitionState.Idle)
            return;

        ResetImpactFeedback();
        _state = TransitionState.Revealing;
        _phaseElapsed = 0f;
        PlayOneShot(_revealClip, _revealVolume);
    }

    private void CompleteTransition()
    {
        SetIdleVisualState();
        _state = TransitionState.Idle;
        _phaseElapsed = 0f;
        _sceneLoadedObserved = false;
        _routeInvoked = false;
        _usesReducedMotion = false;
        _expectedSceneName = null;
        TransitioningChanged?.Invoke(false);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_state != TransitionState.WaitingForScene ||
            !string.Equals(scene.name, _expectedSceneName, StringComparison.Ordinal))
        {
            return;
        }

        _sceneLoadedObserved = true;
    }

    private void ApplyWarningBlade(float progress)
    {
        float amount = EvaluateMotion(_coverCurve, progress);
        _warningBlade?.Apply(amount);
    }

    private void ApplyCoverPlates(float progress)
    {
        float amount = EvaluateMotion(_coverCurve, progress);
        if (_coverPlates == null)
            return;

        for (int index = 0; index < _coverPlates.Length; index++)
            _coverPlates[index]?.Apply(amount);
    }

    private void ApplyReveal(float progress)
    {
        float revealAmount = EvaluateMotion(_revealCurve, progress);
        float coveredAmount = 1f - revealAmount;

        _warningBlade?.Apply(coveredAmount);
        if (_coverPlates != null)
        {
            for (int index = 0; index < _coverPlates.Length; index++)
                _coverPlates[index]?.Apply(coveredAmount);
        }

        if (_overlay != null)
            _overlay.alpha = 1f;
    }

    private void ApplyReducedMotionCover(float progress)
    {
        _warningBlade?.Apply(1f);
        if (_coverPlates != null)
        {
            for (int index = 0; index < _coverPlates.Length; index++)
                _coverPlates[index]?.Apply(1f);
        }

        if (_overlay != null)
            _overlay.alpha = Mathf.Clamp01(progress);
    }

    private void ApplyReducedMotionReveal(float progress)
    {
        _warningBlade?.Apply(1f);
        if (_coverPlates != null)
        {
            for (int index = 0; index < _coverPlates.Length; index++)
                _coverPlates[index]?.Apply(1f);
        }

        if (_overlay != null)
            _overlay.alpha = 1f - Mathf.Clamp01(progress);
    }

    private float EvaluateMotion(AnimationCurve curve, float progress)
    {
        progress = Mathf.Clamp01(progress);
        if (IsReducedMotionEnabled() || curve == null || curve.length == 0)
            return progress;

        return curve.Evaluate(progress);
    }

    private void BeginImpactFeedback()
    {
        PlayOneShot(_impactClip, _impactVolume);

        if (_impactParticles != null)
        {
            int count = _usesReducedMotion || IsReducedMotionEnabled()
                ? _reducedMotionImpactParticleCount
                : _impactParticleCount;
            if (count > 0)
                _impactParticles.Emit(count);
        }

        UpdateImpactFeedback(0f);
    }

    private void UpdateImpactFeedback(float elapsed)
    {
        float progress = _impactFeedbackDuration <= 0f
            ? 1f
            : Mathf.Clamp01(elapsed / _impactFeedbackDuration);

        if (_impactShakeRoot != null)
        {
            float shake = 0f;
            if (!_usesReducedMotion && !IsReducedMotionEnabled() && IsScreenShakeEnabled())
            {
                shake = _impactShakeCurve == null || _impactShakeCurve.length == 0
                    ? 1f - progress
                    : _impactShakeCurve.Evaluate(progress);
            }

            _impactShakeRoot.anchoredPosition = _impactOpenAnchoredPosition + _impactShakeDistance * shake;
            Vector3 rotation = _impactOpenLocalEulerAngles;
            rotation.z += _impactShakeRotation * shake;
            _impactShakeRoot.localEulerAngles = rotation;
        }

        if (_impactFlash != null)
        {
            Color color = _impactFlashBaseColor;
            color.a = IsScreenFlashEnabled()
                ? _impactFlashOpacity * (1f - progress)
                : 0f;
            _impactFlash.color = color;
        }
    }

    private void ResetImpactFeedback()
    {
        if (_impactShakeRoot != null)
        {
            _impactShakeRoot.anchoredPosition = _impactOpenAnchoredPosition;
            _impactShakeRoot.localEulerAngles = _impactOpenLocalEulerAngles;
        }

        if (_impactFlash != null)
        {
            Color color = _impactFlashBaseColor;
            color.a = 0f;
            _impactFlash.color = color;
        }
    }

    private void CacheAuthoredState()
    {
        _authoredReferencesValid = true;

        if (_overlay == null)
            _authoredReferencesValid = false;

        if (_warningBlade == null || !_warningBlade.CacheOpenPose())
            _authoredReferencesValid = false;

        if (_coverPlates == null || _coverPlates.Length == 0)
        {
            _authoredReferencesValid = false;
        }
        else
        {
            for (int index = 0; index < _coverPlates.Length; index++)
            {
                RectTransformTarget target = _coverPlates[index];
                if (target == null || !target.CacheOpenPose())
                    _authoredReferencesValid = false;
            }
        }

        if (_impactShakeRoot != null)
        {
            _impactOpenAnchoredPosition = _impactShakeRoot.anchoredPosition;
            _impactOpenLocalEulerAngles = _impactShakeRoot.localEulerAngles;
        }

        if (_impactFlash != null)
            _impactFlashBaseColor = _impactFlash.color;
    }

    private bool ValidateAuthoredReferences()
    {
        bool valid = _authoredReferencesValid &&
                     _overlay != null &&
                     _warningBlade != null &&
                     _warningBlade.IsAssigned &&
                     _coverPlates != null &&
                     _coverPlates.Length > 0;

        if (valid)
        {
            for (int index = 0; index < _coverPlates.Length; index++)
            {
                if (_coverPlates[index] == null || !_coverPlates[index].IsAssigned)
                {
                    valid = false;
                    break;
                }
            }
        }

        if (valid || _validationErrorLogged)
            return valid;

        _validationErrorLogged = true;
        Debug.LogError(
            "ScrapSceneTransition is missing required authored references. Assign Overlay, " +
            "Warning Blade, and at least one Cover Plate in the Inspector. No fallback " +
            "transition objects will be created.",
            this);
        return false;
    }

    private void SetIdleVisualState()
    {
        ApplyWarningBlade(0f);
        ApplyCoverPlates(0f);
        ResetImpactFeedback();

        if (_overlay == null)
            return;

        _overlay.alpha = 0f;
        _overlay.interactable = false;
        _overlay.blocksRaycasts = false;
    }

    private void SetOverlayActive(bool active)
    {
        if (_overlay == null)
            return;

        _overlay.alpha = active ? 1f : 0f;
        _overlay.interactable = false;
        _overlay.blocksRaycasts = active;
    }

    private void HideDuplicateOverlay()
    {
        if (_overlay == null)
            return;

        _overlay.alpha = 0f;
        _overlay.interactable = false;
        _overlay.blocksRaycasts = false;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (_audioSource != null && clip != null)
            _audioSource.PlayOneShot(clip, GetEffectiveOneShotVolume(volume));
    }

    internal float GetEffectiveOneShotVolumeForTesting(float authoredVolume)
    {
        return GetEffectiveOneShotVolume(authoredVolume);
    }

    private float GetEffectiveOneShotVolume(float authoredVolume)
    {
        Func<float> volumeProvider = SfxVolume ?? ReadSfxVolume;
        return Mathf.Clamp01(authoredVolume) * Mathf.Clamp01(volumeProvider());
    }

    private bool IsReducedMotionEnabled()
    {
        Func<bool> preference = ReducedMotion ?? ReadReducedMotion;
        return preference();
    }

    private static bool ReadReducedMotion()
    {
        UserSettingsService settings = UserSettingsService.Instance;
        return settings != null && settings.ReducedMotion;
    }

    private static bool IsScreenShakeEnabled()
    {
        UserSettingsService settings = UserSettingsService.Instance;
        return settings == null || settings.ScreenShake;
    }

    private static bool IsScreenFlashEnabled()
    {
        UserSettingsService settings = UserSettingsService.Instance;
        return settings == null || settings.ScreenFlash;
    }

    private static float ReadSfxVolume()
    {
        UserSettingsService settings = UserSettingsService.Instance;
        return settings != null ? settings.SfxVolume : 1f;
    }
}
