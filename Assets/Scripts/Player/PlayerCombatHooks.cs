using UnityEngine;

/// <summary>
/// Punto unico para los efectos que los enemigos aplican sobre el jugador (empuje,
/// stun, quemadura/DoT). Los comportamientos de enemigos llaman a esta API limpia y
/// aqui se reenvia a los componentes reales del player (<see cref="PlayerMovement"/>
/// y <see cref="PlayerHealth"/>), que se resuelven y cachean.
///
/// El dano directo NO pasa por aca: se aplica via <see cref="PlayerHealth.TakeDamage"/>.
/// </summary>
public static class PlayerCombatHooks
{
    /// <summary>Si esta activo, loguea cuando no se encuentra el componente del player (debug QA).</summary>
    public static bool LogMissingTargets;

    private static PlayerMovement _movement;
    private static PlayerHealth _health;

    private static PlayerMovement Movement
    {
        get
        {
            if (_movement == null)
                _movement = ResolveFromPlayer<PlayerMovement>();
            return _movement;
        }
    }

    private static PlayerHealth Health
    {
        get
        {
            if (_health == null)
                _health = ResolveFromPlayer<PlayerHealth>();
            return _health;
        }
    }

    private static T ResolveFromPlayer<T>() where T : Component
    {
        Transform player = PlayerMovement.PlayerTransform;
        if (player != null)
        {
            T found = player.GetComponentInParent<T>();
            if (found != null)
                return found;
        }

        return Object.FindAnyObjectByType<T>();
    }

    /// <summary>Empuje horizontal al jugador desde <paramref name="impactOrigin"/> (Chaser/Shocker).</summary>
    public static void TryPush(Vector3 impactOrigin, float force)
    {
        if (force <= 0f)
            return;

        PlayerMovement movement = Movement;
        if (movement != null)
            movement.ApplyKnockback(impactOrigin, force);
        else if (LogMissingTargets)
            Debug.LogWarning("[PlayerCombatHooks] TryPush: no se encontro PlayerMovement.");
    }

    /// <summary>Aturde al jugador durante <paramref name="seconds"/> (Shocker).</summary>
    public static void TryStun(float seconds)
    {
        if (seconds <= 0f)
            return;

        PlayerMovement movement = Movement;
        if (movement != null)
            movement.ApplyStun(seconds);
        else if (LogMissingTargets)
            Debug.LogWarning("[PlayerCombatHooks] TryStun: no se encontro PlayerMovement.");
    }

    /// <summary>Quemadura (dano por segundo) durante <paramref name="seconds"/> (Hellfire). Ignora i-frames.</summary>
    public static void TryBurn(float seconds, int damagePerSecond)
    {
        if (seconds <= 0f || damagePerSecond <= 0)
            return;

        PlayerHealth health = Health;
        if (health != null)
            health.ApplyBurn(seconds, damagePerSecond);
        else if (LogMissingTargets)
            Debug.LogWarning("[PlayerCombatHooks] TryBurn: no se encontro PlayerHealth.");
    }
}
