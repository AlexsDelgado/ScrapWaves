using UnityEngine;

/// <summary>
/// Caída simple por gravedad hasta tocar el layer de suelo, para pickups dropeados por enemigos
/// que pueden morir en el aire (p. ej. enemigos voladores) y no deben quedar flotando en el punto
/// exacto de la muerte. No usa Rigidbody: los drops son objetos puramente kinematic (imán +
/// recolección), así que esto solo mueve el transform.
/// </summary>
public static class PickupGroundFall
{
    private const float Gravity = -20f;
    private const float GroundProbeHeight = 5f;
    private const float GroundProbeExtraDistance = 5f;

    /// <summary>
    /// Avanza la caída un frame. Devuelve true cuando aterrizó (a partir de ahí dejar de llamarla
    /// y retomar el comportamiento normal del pickup).
    /// </summary>
    public static bool Tick(ref Vector3 position, ref float verticalVelocity, float deltaTime, float groundOffset, LayerMask groundMask)
    {
        verticalVelocity += Gravity * deltaTime;
        position.y += verticalVelocity * deltaTime;

        Vector3 rayOrigin = new Vector3(position.x, position.y + GroundProbeHeight, position.z);
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundProbeHeight + GroundProbeExtraDistance, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        float groundY = hit.point.y + groundOffset;
        if (position.y > groundY)
            return false;

        position.y = groundY;
        verticalVelocity = 0f;
        return true;
    }
}
