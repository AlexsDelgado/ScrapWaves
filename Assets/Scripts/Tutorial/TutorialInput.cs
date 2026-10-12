using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One physical E press belongs to either a tutorial page or world interaction.</summary>
public static class TutorialInput
{
    private static int _consumedFrame = -1;
    public static bool WasConsumed => _consumedFrame == Time.frameCount;
    public static bool Pressed => !WasConsumed && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    public static bool ConsumeAdvance()
    {
        if (!Pressed) return false;
        _consumedFrame = Time.frameCount;
        return true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => _consumedFrame = -1;
}
