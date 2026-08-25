using System;
using UnityEngine;

[Serializable]
public sealed class CameraFeedbackController
{
    [SerializeField, Range(0f, 1f), Tooltip("Master scale for profile-driven combat impulses.")]
    private float _masterScale = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("Additional multiplier used by reduced-shake accessibility mode.")]
    private float _reducedShakeScale = 0.25f;

    [SerializeField, Min(0f), Tooltip("Minimum interval between high-frequency weapon impulse requests.")]
    private float _minimumImpulseInterval = 0.025f;

    [SerializeField, Min(0.1f), Tooltip("World-space distance at which impact camera feedback reaches zero.")]
    private float _maximumImpactDistance = 28f;

    private ThirdPersonCamera _camera;
    private Transform _listener;
    private float _nextImpulseTime;

    public void Bind(ThirdPersonCamera camera)
    {
        _camera = camera;
        _listener = camera != null ? camera.transform : null;
    }

    public bool Request(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        WeaponHeatPresentationSettings heat,
        bool enabled,
        bool reducedShake,
        float now)
    {
        return Request(cueData, in context, heat, enabled, reducedShake, reducedMotion: false, now: now);
    }

    public bool Request(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        WeaponHeatPresentationSettings heat,
        bool enabled,
        bool reducedShake,
        bool reducedMotion,
        float now)
    {
        if (!enabled || _camera == null || cueData == null || now < _nextImpulseTime)
            return false;

        float scale = _masterScale * Mathf.Clamp01(context.EventIntensity);
        if (reducedShake)
            scale *= _reducedShakeScale;

        if (heat != null)
            scale *= Mathf.Max(0f, heat.CameraVibration.Evaluate(context.NormalizedHeat));

        if (context.ImpactPosition != default && _listener != null)
        {
            float distance = Vector3.Distance(_listener.position, context.ImpactPosition);
            scale *= 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, _maximumImpactDistance));
        }

        if (scale <= 0.0001f)
            return false;

        bool accepted = _camera.AddPresentationImpulse(
            cueData.CameraPositionImpulse * scale,
            cueData.CameraRotationImpulse * scale,
            cueData.CameraFovKick * scale,
            reducedMotion);
        if (accepted)
            _nextImpulseTime = now + Mathf.Max(_minimumImpulseInterval, cueData.CameraMinReplayInterval);
        return accepted;
    }

    public void Clear()
    {
        _nextImpulseTime = 0f;
        _camera?.ClearPresentationImpulses();
    }

    public void Sanitize()
    {
        _masterScale = Mathf.Clamp01(_masterScale);
        _reducedShakeScale = Mathf.Clamp01(_reducedShakeScale);
        _minimumImpulseInterval = Mathf.Max(0f, _minimumImpulseInterval);
        _maximumImpactDistance = Mathf.Max(0.1f, _maximumImpactDistance);
    }
}
