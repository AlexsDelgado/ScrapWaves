#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Guarda el último impacto resuelto para que la vista dev pueda mostrarlo después de pausar.
/// Se suscribe una sola vez al arrancar el juego en lugar de hacerlo desde el panel: el panel
/// se desactiva al cerrar la pausa, y el disparo que interesa ocurre justo antes de abrirla.
/// </summary>
public static class LastHitRecorder
{
    private static WeaponDamageRoll _lastRoll;
    private static bool _hasRoll;
    private static bool _subscribed;

    public static bool HasRoll => _hasRoll;
    public static WeaponDamageRoll LastRoll => _lastRoll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        _hasRoll = false;
        _lastRoll = default;

        if (_subscribed)
            return;

        WeaponDamageResolver.OnDamageResolved += Record;
        _subscribed = true;
    }

    private static void Record(WeaponDamageRoll roll)
    {
        _lastRoll = roll;
        _hasRoll = true;
    }
}
#endif
