using UnityEngine;

/// <summary>
/// Contadores ligeros para medir presión de alloc/spawn en QA y Profiler.
/// </summary>
public static class EnemyPoolProfiler
{
    public static int InstantiateCount { get; private set; }
    public static int DestroyCount { get; private set; }
    public static int PoolGetCount { get; private set; }
    public static int PoolReleaseCount { get; private set; }

    public static int RegistryActiveCount => EnemyRegistry.ActiveCount;
    public static int InactiveEnemyObjects { get; private set; }

    public static void RegisterInstantiate() => InstantiateCount++;
    public static void RegisterDestroy() => DestroyCount++;
    public static void RegisterPoolGet() => PoolGetCount++;
    public static void RegisterPoolRelease() => PoolReleaseCount++;

    public static void ResetSessionCounters()
    {
        InstantiateCount = 0;
        DestroyCount = 0;
        PoolGetCount = 0;
        PoolReleaseCount = 0;
    }

    public static void RefreshInactiveEnemyCount()
    {
        int count = 0;
        EnemyHealth[] all = Object.FindObjectsByType<EnemyHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].gameObject.activeInHierarchy)
                count++;
        }

        InactiveEnemyObjects = count;
    }
}
