using UnityEngine;

/// <summary>
/// Lock de pausa de UI (pausa, crafteo, level-up, fin de run). Distinto de hit-stop:
/// hit-stop también pone <see cref="Time.timeScale"/> a 0, pero no incrementa este contador.
/// </summary>
public static class GameplayPause
{
    private static int _locks;

    public static bool IsUiPaused => _locks > 0;

    public static int LockCount => _locks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _locks = 0;
    }

    public static void Push()
    {
        _locks++;
    }

    public static void Pop()
    {
        if (_locks > 0)
            _locks--;
    }

    public static void SetHeld(ref bool held, bool shouldHold)
    {
        if (shouldHold == held)
            return;

        held = shouldHold;
        if (shouldHold)
            Push();
        else
            Pop();
    }

    public static void Reset()
    {
        _locks = 0;
    }
}
