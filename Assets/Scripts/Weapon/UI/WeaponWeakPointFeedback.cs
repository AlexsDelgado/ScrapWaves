using System;

public static class WeaponWeakPointFeedback
{
    public static event Action WeakPointHit;

    public static void NotifyWeakPointHit()
    {
        WeakPointHit?.Invoke();
    }
}
