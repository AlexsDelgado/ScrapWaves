using System;
using UnityEngine;

[Serializable]
public sealed class UserSettingsData
{
    public const float DefaultHorizontalSensitivity = 0.12f;
    public const float DefaultVerticalSensitivity = 0.12f;
    public const bool DefaultInvertY = false;
    public const float DefaultSfxVolume = 1f;
    public const float DefaultMusicVolume = 0.45f;
    public const bool DefaultReducedMotion = false;
    public const bool DefaultScreenShake = true;
    public const bool DefaultScreenFlash = true;

    public const float MinimumSensitivity = 0.02f;
    public const float MaximumSensitivity = 0.4f;

    public float HorizontalSensitivity = DefaultHorizontalSensitivity;
    public float VerticalSensitivity = DefaultVerticalSensitivity;
    public bool InvertY = DefaultInvertY;
    public float SfxVolume = DefaultSfxVolume;
    public float MusicVolume = DefaultMusicVolume;
    public bool ReducedMotion = DefaultReducedMotion;
    public bool ScreenShake = DefaultScreenShake;
    public bool ScreenFlash = DefaultScreenFlash;

    public static UserSettingsData CreateDefault() => new();

    public UserSettingsData Clone()
    {
        return new UserSettingsData
        {
            HorizontalSensitivity = HorizontalSensitivity,
            VerticalSensitivity = VerticalSensitivity,
            InvertY = InvertY,
            SfxVolume = SfxVolume,
            MusicVolume = MusicVolume,
            ReducedMotion = ReducedMotion,
            ScreenShake = ScreenShake,
            ScreenFlash = ScreenFlash
        };
    }

    public bool Sanitize()
    {
        bool changed = false;
        changed |= SanitizeRange(
            ref HorizontalSensitivity,
            MinimumSensitivity,
            MaximumSensitivity,
            DefaultHorizontalSensitivity);
        changed |= SanitizeRange(
            ref VerticalSensitivity,
            MinimumSensitivity,
            MaximumSensitivity,
            DefaultVerticalSensitivity);
        changed |= SanitizeRange(ref SfxVolume, 0f, 1f, DefaultSfxVolume);
        changed |= SanitizeRange(ref MusicVolume, 0f, 1f, DefaultMusicVolume);
        return changed;
    }

    public static float SanitizeSensitivity(float value, float fallback)
    {
        return SanitizeFiniteRange(value, MinimumSensitivity, MaximumSensitivity, fallback);
    }

    public static float SanitizeVolume(float value, float fallback)
    {
        return SanitizeFiniteRange(value, 0f, 1f, fallback);
    }

    private static bool SanitizeRange(ref float value, float minimum, float maximum, float fallback)
    {
        float sanitized = SanitizeFiniteRange(value, minimum, maximum, fallback);
        bool changed = !ApproximatelyOrBothNaN(value, sanitized);
        value = sanitized;
        return changed;
    }

    private static float SanitizeFiniteRange(float value, float minimum, float maximum, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = fallback;
        return Mathf.Clamp(value, minimum, maximum);
    }

    private static bool ApproximatelyOrBothNaN(float left, float right)
    {
        return (float.IsNaN(left) && float.IsNaN(right)) || Mathf.Approximately(left, right);
    }
}

public enum UserSettingsCategory
{
    Controls,
    Audio,
    Feedback
}

[Flags]
public enum UserSettingsChange
{
    None = 0,
    HorizontalSensitivity = 1 << 0,
    VerticalSensitivity = 1 << 1,
    InvertY = 1 << 2,
    SfxVolume = 1 << 3,
    MusicVolume = 1 << 4,
    ReducedMotion = 1 << 5,
    ScreenShake = 1 << 6,
    ScreenFlash = 1 << 7,

    Controls = HorizontalSensitivity | VerticalSensitivity | InvertY,
    Audio = SfxVolume | MusicVolume,
    Feedback = ReducedMotion | ScreenShake | ScreenFlash,
    All = Controls | Audio | Feedback
}
