using System;
using UnityEngine;

/// <summary>
/// Authoritative process-wide presentation accessibility state. Consumers either read
/// <see cref="Current"/> during initialization or subscribe to <see cref="Changed"/>.
/// </summary>
public static class PresentationAccessibilityRuntime
{
    private static PresentationAccessibilityState s_current = PresentationAccessibilityState.Default;

    public static PresentationAccessibilityState Current => s_current;

    public static event Action<PresentationAccessibilityState> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBeforeRuntimeLoad()
    {
        s_current = PresentationAccessibilityState.Default;
        Changed = null;
    }

    public static void Apply(PresentationAccessibilitySettings settings)
    {
        Apply(settings != null ? settings.ToState() : PresentationAccessibilityState.Default);
    }

    public static void Apply(PresentationAccessibilityState state)
    {
        PresentationAccessibilityState sanitized = new(
            state.ReducedMotion,
            state.ReducedShake,
            state.ReducedFlash,
            state.CombatText,
            state.CombatTextScale);

        if (s_current == sanitized)
            return;

        s_current = sanitized;
        Changed?.Invoke(s_current);
    }
}
