using System;
using UnityEngine;

[Serializable]
public sealed class HitStopController
{
    [SerializeField, Min(0f), Tooltip("Minimum unscaled interval between accepted hit-stop requests.")]
    private float _minimumReplayInterval = 0.035f;

    [SerializeField, Range(0f, 1f), Tooltip("Accessibility multiplier applied when reduced shake/flash feedback is requested.")]
    private float _reducedFeedbackScale = 0.5f;

    [SerializeField, Min(0f), Tooltip("Maximum important-event hit-stop duration under Reduced Motion. Routine requests are suppressed.")]
    private float _reducedMotionImportantDurationCap = 0.015f;

    private float _remaining;
    private float _nextRequestTime;
    private float _restoreTimeScale = 1f;
    private int _activePriority;
    private bool _ownsTimeScale;

    public bool IsActive => _remaining > 0f;
    public float RemainingDuration => _remaining;

    public bool Request(
        float duration,
        int priority,
        bool enabled,
        bool reducedFeedback,
        float now)
    {
        return Request(duration, priority, enabled, reducedFeedback, reducedMotion: false, important: true, now: now);
    }

    /// <summary>
    /// Transitional Reduced Motion overload. Positive-priority cues are treated as important;
    /// callers with semantic context should prefer the explicit overload below.
    /// </summary>
    public bool Request(
        float duration,
        int priority,
        bool enabled,
        bool reducedFeedback,
        bool reducedMotion,
        float now)
    {
        return Request(duration, priority, enabled, reducedFeedback, reducedMotion, important: priority > 0, now: now);
    }

    public bool Request(
        float duration,
        int priority,
        bool enabled,
        bool reducedFeedback,
        bool reducedMotion,
        bool important,
        float now)
    {
        if (!enabled || duration <= 0f || now < _nextRequestTime)
            return false;

        if (reducedMotion && !important)
            return false;

        float scaledDuration = duration * (reducedFeedback ? _reducedFeedbackScale : 1f);
        if (reducedMotion)
            scaledDuration = Mathf.Min(scaledDuration, _reducedMotionImportantDurationCap);
        if (scaledDuration <= 0.0001f)
            return false;

        int clampedPriority = Mathf.Max(0, priority);
        if (IsActive && clampedPriority < _activePriority && scaledDuration <= _remaining)
            return false;

        if (!_ownsTimeScale)
        {
            _restoreTimeScale = Mathf.Max(0f, Time.timeScale);
            _ownsTimeScale = true;
        }

        _remaining = Mathf.Max(_remaining, scaledDuration);
        _activePriority = Mathf.Max(_activePriority, clampedPriority);
        _nextRequestTime = now + _minimumReplayInterval;
        Time.timeScale = 0f;
        return true;
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (!IsActive)
            return;

        _remaining = Mathf.Max(0f, _remaining - Mathf.Max(0f, unscaledDeltaTime));
        if (_remaining <= 0f)
            Restore();
    }

    public void Restore()
    {
        _remaining = 0f;
        _activePriority = 0;
        if (_ownsTimeScale)
            Time.timeScale = _restoreTimeScale;
        _ownsTimeScale = false;
    }

    public void Sanitize()
    {
        _minimumReplayInterval = Mathf.Max(0f, _minimumReplayInterval);
        _reducedFeedbackScale = Mathf.Clamp01(_reducedFeedbackScale);
        _reducedMotionImportantDurationCap = Mathf.Max(0f, _reducedMotionImportantDurationCap);
    }
}
