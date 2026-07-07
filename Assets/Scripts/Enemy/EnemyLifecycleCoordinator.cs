using UnityEngine;

/// <summary>
/// Punto único de limpieza al terminar Overheat y para QA "Clear all".
/// </summary>
public static class EnemyLifecycleCoordinator
{
    public static void OnOverheatEnded()
    {
        EnemyPoolRegistry registry = EnemyPoolRegistry.Instance;
        if (registry == null)
            registry = Object.FindAnyObjectByType<EnemyPoolRegistry>();

        registry?.ReleaseAllActive();

        SwarmEnemyPool legacyPool = Object.FindAnyObjectByType<SwarmEnemyPool>();
        legacyPool?.ReleaseAllActive();

        OrbitalSpawner[] orbitals = Object.FindObjectsByType<OrbitalSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < orbitals.Length; i++)
            orbitals[i].ClearSpawned();

        ZoneSpawner[] zones = Object.FindObjectsByType<ZoneSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < zones.Length; i++)
            zones[i].ClearSpawned();

        OverheatEliteWaveSpawner[] elites = Object.FindObjectsByType<OverheatEliteWaveSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < elites.Length; i++)
            elites[i].ClearSpawned();

        EnemyPoolProfiler.RefreshInactiveEnemyCount();
    }

    public static void ClearAllForQa()
    {
        OnOverheatEnded();
        EnemyPoolProfiler.ResetSessionCounters();
    }
}
