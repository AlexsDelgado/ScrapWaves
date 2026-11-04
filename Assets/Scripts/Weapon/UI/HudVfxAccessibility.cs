using UnityEngine;

/// <summary>Shared presentation preferences; never changes gameplay or saved settings.</summary>
internal static class HudVfxAccessibility
{
    public static bool StaticEffects
    {
        get
        {
            var state = PresentationAccessibilityRuntime.Current;
            var settings = UserSettingsService.Instance;
            return state.ReducedMotion || state.ReducedFlash ||
                (settings != null && (settings.ReducedMotion || !settings.ScreenFlash));
        }
    }

    public static float DeltaTime => Time.timeScale > 0f ? Mathf.Min(Time.unscaledDeltaTime, .1f) : 0f;
}
