using UnityEngine;

/// <summary>
/// Punto unico para los efectos que los enemigos aplican sobre el jugador y que
/// todavia no estan implementados en el dominio del player (empuje, stun,
/// quemadura/DoT). Los comportamientos de enemigos llaman a esta API limpia y,
/// cuando se decida implementar los efectos en <c>PlayerMovement</c>/
/// <c>PlayerHealth</c>, solo hay que rellenar estos metodos en un solo sitio.
///
/// El dano directo NO pasa por aca: se aplica via <see cref="PlayerHealth.TakeDamage"/>.
/// </summary>
public static class PlayerCombatHooks
{
    /// <summary>Si esta activo, los hooks aun-no-implementados loguean en consola (util en QA).</summary>
    public static bool LogPendingEffects = true;

    /// <summary>
    /// Empuje horizontal al jugador desde <paramref name="impactOrigin"/> con la
    /// fuerza dada (Chaser/Shocker). TODO: aplicar impulso al Rigidbody del player.
    /// </summary>
    public static void TryPush(Vector3 impactOrigin, float force)
    {
        if (force <= 0f)
            return;

        // TODO: implementar empuje real sobre PlayerMovement (impulso al Rigidbody).
        if (LogPendingEffects)
            Debug.Log($"[PlayerCombatHooks] TryPush(force={force:0.#}) - pendiente de implementar.");
    }

    /// <summary>
    /// Aturde al jugador durante <paramref name="seconds"/> (Shocker). TODO: gate de
    /// input/movimiento en PlayerMovement durante la duracion.
    /// </summary>
    public static void TryStun(float seconds)
    {
        if (seconds <= 0f)
            return;

        // TODO: implementar stun real (bloquear input/aceleracion del player).
        if (LogPendingEffects)
            Debug.Log($"[PlayerCombatHooks] TryStun(seconds={seconds:0.#}) - pendiente de implementar.");
    }

    /// <summary>
    /// Quemadura (dano por segundo) durante <paramref name="seconds"/> (Hellfire).
    /// TODO: DoT real que ignore los i-frames globales del player.
    /// </summary>
    public static void TryBurn(float seconds, int damagePerSecond)
    {
        if (seconds <= 0f || damagePerSecond <= 0)
            return;

        // TODO: implementar DoT real (tick que evite los i-frames de PlayerHealth).
        if (LogPendingEffects)
            Debug.Log($"[PlayerCombatHooks] TryBurn(seconds={seconds:0.#}, dps={damagePerSecond}) - pendiente de implementar.");
    }
}
