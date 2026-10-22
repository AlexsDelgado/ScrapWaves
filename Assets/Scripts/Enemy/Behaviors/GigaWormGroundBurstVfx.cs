using UnityEngine;

/// <summary>Decorative scrap disturbed by underground travel and the worm's ground entry.</summary>
public static class GigaWormGroundBurstVfx
{
    private const string ResourcePath = "VFX/StalkerScrapBurst";
    private static StalkerScrapBurstVfx s_instance;
    private static GameObject s_prefab;
    private static bool s_warnedMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance = null;
        s_prefab = null;
        s_warnedMissing = false;
    }

    public static void Spawn(Vector3 groundPoint, int pieceCount = -1, Collider groundSurface = null) =>
        Emit(groundPoint, false, pieceCount, groundSurface);
    public static void SpawnEntry(Vector3 groundPoint, int pieceCount = -1) => Emit(groundPoint, true, pieceCount);

    public static void SpawnOnSurface(Vector3 sample, LayerMask groundMask, Collider occupiedSupport = null)
    {
        // Only cosmetic trail/warning placement uses this preference. The gameplay
        // ground query, emergence trajectory and accepted dive-entry burst stay intact.
        Ray ray = new(sample + Vector3.up * 30f, Vector3.down);
        // A confirmed floor occupied by the player can be a Default-layer sky platform.
        // Keep that support instead of projecting through it onto terrain underneath.
        if (occupiedSupport != null && occupiedSupport.Raycast(ray, out RaycastHit supported, 80f))
        {
            Spawn(supported.point, groundSurface: occupiedSupport);
            return;
        }
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        int terrainMask = terrainLayer >= 0 ? groundMask.value & (1 << terrainLayer) : 0;
        bool found = Physics.Raycast(ray, out RaycastHit hit, 80f, terrainMask, QueryTriggerInteraction.Ignore);
        if (!found)
            found = Physics.Raycast(ray, out hit, 80f, groundMask, QueryTriggerInteraction.Ignore);
        if (found) Spawn(hit.point, groundSurface: hit.collider);
    }

    private static void Emit(Vector3 groundPoint, bool entry, int pieceCount, Collider groundSurface = null)
    {
        if (s_instance == null)
        {
            if (s_prefab == null) s_prefab = Resources.Load<GameObject>(ResourcePath);
            if (s_prefab == null)
            {
                if (!s_warnedMissing) Debug.LogWarning("Missing Stalker scrap-burst presentation prefab: " + ResourcePath);
                s_warnedMissing = true;
                return;
            }
            // Scene-local mesh particles reuse a bounded buffer; no per-piece objects,
            // rigidbodies, colliders, material instances, damage or pickup behavior.
            s_instance = Object.Instantiate(s_prefab).GetComponent<StalkerScrapBurstVfx>();
        }
        if (s_instance != null) s_instance.Emit(groundPoint, entry, pieceCount, groundSurface);
    }
}
