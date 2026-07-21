using UnityEngine;

/// <summary>
/// Punto único de limpieza al terminar Overheat (objetivos) y para QA "Clear all".
/// Al terminar Overheat ya no limpia el swarm orbital: los enemigos comunes quedan
/// para mantener presión; solo se limpian elites de oleada.
/// </summary>
public static class EnemyLifecycleCoordinator
{
    /// <summary>
    /// Fin de Overheat: limpia elites de objetivo. No libera swarm orbital ni zonas.
    /// </summary>
    public static void OnOverheatEnded()
    {
        OverheatEliteWaveSpawner[] elites = Object.FindObjectsByType<OverheatEliteWaveSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < elites.Length; i++)
            elites[i].ClearSpawned();

        EnemyPoolProfiler.RefreshInactiveEnemyCount();
    }

    /// <summary>QA: limpia todo (pools, orbital, zonas, elites).</summary>
    public static void ClearAllForQa()
    {
        EnemyPoolRegistry registry = EnemyPoolRegistry.Instance;
        if (registry == null)
            registry = Object.FindAnyObjectByType<EnemyPoolRegistry>();

        registry?.ReleaseAllActive();

        SwarmEnemyPool legacyPool = Object.FindAnyObjectByType<SwarmEnemyPool>();
        legacyPool?.ReleaseAllActive();

        OrbitalSpawner[] orbitals = Object.FindObjectsByType<OrbitalSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < orbitals.Length; i++)
            orbitals[i].ClearSpawned();

        ZoneSpawner[] zones = Object.FindObjectsByType<ZoneSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < zones.Length; i++)
            zones[i].ClearSpawned();

        OverheatEliteWaveSpawner[] elites = Object.FindObjectsByType<OverheatEliteWaveSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < elites.Length; i++)
            elites[i].ClearSpawned();

        EnemyPoolProfiler.RefreshInactiveEnemyCount();
        EnemyPoolProfiler.ResetSessionCounters();
    }
}
