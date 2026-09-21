using System;
using UnityEngine;

/// <summary>
/// Marca una instancia como enemigo-objetivo de la oleada actual (elite de Overheat) y avisa
/// a su spawner si sale de juego SIN morir: devuelta al pool, destruida o limpiada por QA.
///
/// Existe porque el contador de elites solo se decrementaba con <see cref="EnemyHealth.OnDied"/>:
/// cualquier despawn silencioso (p. ej. la culla vertical al subir de piso) dejaba el contador
/// clavado y el Overheat, que no tiene temporizador, colgado para siempre.
/// </summary>
[DisallowMultipleComponent]
public class EliteObjectiveMarker : MonoBehaviour, IEnemySpawnLifecycle
{
    private Action<Transform> _onRemoved;

    /// <summary>True mientras esta instancia sea un objetivo vivo de la oleada.</summary>
    public bool IsTrackedObjective => _onRemoved != null;

    public static EliteObjectiveMarker Bind(GameObject instance, Action<Transform> onRemoved)
    {
        if (instance == null)
            return null;

        EliteObjectiveMarker marker = instance.GetComponent<EliteObjectiveMarker>();
        if (marker == null)
            marker = instance.AddComponent<EliteObjectiveMarker>();

        marker._onRemoved = onRemoved;
        return marker;
    }

    /// <summary>Corta el aviso sin dispararlo (lo usa el spawner cuando él mismo hace la limpieza).</summary>
    public void Unbind() => _onRemoved = null;

    /// <summary>Lease nuevo del pool: la instancia deja de ser objetivo hasta que la vuelvan a marcar.</summary>
    public void OnPoolSpawn() => _onRemoved = null;

    /// <summary>Devuelta al pool. Corre antes del SetActive(false) de EnemyPrefabPool.Release.</summary>
    public void OnPoolDespawn() => NotifyRemoved();

    private void OnDestroy() => NotifyRemoved();

    private void NotifyRemoved()
    {
        Action<Transform> callback = _onRemoved;
        if (callback == null)
            return;

        // Se limpia antes de invocar: el callback puede terminar el Overheat y reentrar acá.
        _onRemoved = null;
        callback(transform);
    }
}
