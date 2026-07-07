using UnityEngine;

public static class OrbitalSpawnPlacement
{
    private static readonly string[] DirectionLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static string GetDirectionLabel(int directionIndex)
    {
        if (directionIndex < 0 || directionIndex >= DirectionLabels.Length)
            return "?";
        return DirectionLabels[directionIndex];
    }

    public static float GetDirectionAngleDegrees(int directionIndex)
    {
        return directionIndex * 45f;
    }

    public static int PickRandomDirectionIndex()
    {
        return Random.Range(0, 8);
    }

    public static bool TrySpawnAtOrbitalPoint(
        Transform player,
        GameObject prefab,
        int directionIndex,
        float minRadius,
        float maxRadius,
        float spawnHeightOffset,
        LayerMask groundRaycastMask,
        LayerMask fallbackGroundRaycastMask,
        LayerMask overlapSolidMask,
        float raycastStartHeight,
        float raycastMaxDistance,
        float maxAbsSpawnSurfaceDeltaY,
        float surfaceSeparation,
        int maxProjectionIterations,
        float resolveStepUp,
        float resolveStepOut,
        out GameObject instance,
        out Vector3 spawnPosition,
        out string placementLog)
    {
        instance = null;
        spawnPosition = Vector3.zero;
        placementLog = "no player or prefab";

        if (player == null || prefab == null)
            return false;

        float angleRad = GetDirectionAngleDegrees(directionIndex) * Mathf.Deg2Rad;
        float radius = Random.Range(minRadius, maxRadius);
        Vector3 offset = new Vector3(
            Mathf.Sin(angleRad) * radius,
            spawnHeightOffset,
            Mathf.Cos(angleRad) * radius);

        Vector3 ringPos = player.position + offset;

        if (!TrySpawnGrounded(
                prefab,
                ringPos,
                groundRaycastMask,
                fallbackGroundRaycastMask,
                overlapSolidMask,
                raycastStartHeight,
                raycastMaxDistance,
                maxAbsSpawnSurfaceDeltaY,
                surfaceSeparation,
                maxProjectionIterations,
                resolveStepUp,
                resolveStepOut,
                out instance,
                out spawnPosition))
        {
            placementLog = "ground resolve failed";
            return false;
        }

        Transform root = instance.transform;
        Vector3 toPlayer = player.position - root.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            root.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

        placementLog = $"dir={GetDirectionLabel(directionIndex)} angle={GetDirectionAngleDegrees(directionIndex):0}° pos={spawnPosition}";
        return true;
    }

    /// <summary>
    /// Instancia <paramref name="prefab"/> en <paramref name="desiredPosition"/> resolviendo
    /// el apoyo en el suelo si el prefab tiene <see cref="CharacterController"/>. No orienta el
    /// objeto. Reutilizado por el spawner orbital y el de zona.
    /// </summary>
    public static bool TrySpawnGrounded(
        GameObject prefab,
        Vector3 desiredPosition,
        LayerMask groundRaycastMask,
        LayerMask fallbackGroundRaycastMask,
        LayerMask overlapSolidMask,
        float raycastStartHeight,
        float raycastMaxDistance,
        float maxAbsSpawnSurfaceDeltaY,
        float surfaceSeparation,
        int maxProjectionIterations,
        float resolveStepUp,
        float resolveStepOut,
        out GameObject instance,
        out Vector3 spawnPosition)
    {
        instance = null;
        spawnPosition = desiredPosition;

        if (prefab == null)
            return false;

        bool fromPool = EnemyPoolRegistry.UseEnemyPool
            && EnemyPoolRegistry.Instance != null
            && EnemyPoolRegistry.Instance.TryGet(prefab, out instance);

        if (!fromPool)
        {
            instance = Object.Instantiate(prefab);
            EnemyPoolProfiler.RegisterInstantiate();
        }

        Transform root = instance.transform;

        CharacterController cc = instance.GetComponent<CharacterController>();
        if (cc == null)
        {
            root.SetPositionAndRotation(desiredPosition, Quaternion.identity);
            spawnPosition = desiredPosition;
            return true;
        }

        if (!SpawnGroundUtility.TryResolveFootPosition(
                new Vector3(desiredPosition.x, 0f, desiredPosition.z),
                root,
                cc,
                desiredPosition.y,
                maxAbsSpawnSurfaceDeltaY,
                groundRaycastMask,
                fallbackGroundRaycastMask,
                overlapSolidMask,
                raycastStartHeight,
                raycastMaxDistance,
                surfaceSeparation,
                maxProjectionIterations,
                resolveStepUp,
                resolveStepOut,
                out Vector3 foot))
        {
            if (fromPool && EnemyPoolRegistry.Instance != null)
                EnemyPoolRegistry.Instance.Release(instance);
            else
            {
                Object.Destroy(instance);
                EnemyPoolProfiler.RegisterDestroy();
            }

            instance = null;
            return false;
        }

        root.SetPositionAndRotation(foot, Quaternion.identity);
        spawnPosition = foot;
        return true;
    }
}
