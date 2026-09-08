using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perfil de steering liviano para chase genérico y behaviors no-boss.
/// Valores pensados para sumarse a la dirección (luego normalizar), no metros crudos.
/// </summary>
[System.Serializable]
public struct EnemyMovementProfile
{
    [Min(0f)] public float orbitRadius;
    [Min(0f)] public float weaveAmplitude;
    [Min(0f)] public float weaveFrequency;
    [Min(0f)] public float separationWeight;
    [Min(0.05f)] public float separationRadius;
    [Min(1)] public int maxSeparationSamples;

    public static EnemyMovementProfile MeleeSwarm => new EnemyMovementProfile
    {
        orbitRadius = 1.8f,
        weaveAmplitude = 0.35f,
        weaveFrequency = 1.6f,
        separationWeight = 0.7f,
        separationRadius = 1.25f,
        maxSeparationSamples = 8
    };

    public static EnemyMovementProfile ChargerApproach => new EnemyMovementProfile
    {
        orbitRadius = 0.6f,
        weaveAmplitude = 0.12f,
        weaveFrequency = 1.1f,
        separationWeight = 0.45f,
        separationRadius = 1.1f,
        maxSeparationSamples = 6
    };

    public static EnemyMovementProfile HellfireApproach => new EnemyMovementProfile
    {
        orbitRadius = 1.3f,
        weaveAmplitude = 0.45f,
        weaveFrequency = 2.1f,
        separationWeight = 0.5f,
        separationRadius = 1.2f,
        maxSeparationSamples = 6
    };

    public static EnemyMovementProfile FlyingStandoff => new EnemyMovementProfile
    {
        orbitRadius = 9f,
        weaveAmplitude = 0.4f,
        weaveFrequency = 0.85f,
        separationWeight = 0.35f,
        separationRadius = 2.2f,
        maxSeparationSamples = 6
    };

    public static EnemyMovementProfile BomberEngage => new EnemyMovementProfile
    {
        orbitRadius = 1.5f,
        weaveAmplitude = 0.3f,
        weaveFrequency = 1.3f,
        separationWeight = 0.3f,
        separationRadius = 2f,
        maxSeparationSamples = 6
    };
}

/// <summary>
/// Helpers de persecución con offset, weave y separación por sample (sin OverlapSphere).
/// </summary>
public static class EnemyMovementSteering
{
    private static readonly List<Transform> s_Transforms = new List<Transform>(256);
    private static Vector3[] s_Positions = new Vector3[256];
    private static int s_Count;
    private static int s_CacheFrame = -1;

    /// <summary>Punto en el anillo alrededor del jugador (plano XZ).</summary>
    public static Vector3 GetPursuitPoint(Vector3 playerPos, float slotAngleDeg, float radius)
    {
        if (radius <= 0.0001f)
            return playerPos;

        float rad = slotAngleDeg * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
        return new Vector3(playerPos.x + offset.x, playerPos.y, playerPos.z + offset.z);
    }

    /// <summary>Desplazamiento lateral senoidal (contribución a dirección, no metros).</summary>
    public static Vector3 GetWeaveOffset(Vector3 planarToward, float time, float phase, float amplitude, float frequency)
    {
        if (amplitude <= 0f || frequency <= 0f)
            return Vector3.zero;

        planarToward.y = 0f;
        if (planarToward.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        Vector3 forward = planarToward.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        right.Normalize();
        float wave = Mathf.Sin(time * frequency + phase) * amplitude;
        return right * wave;
    }

    /// <summary>
    /// Refresca el cache de posiciones activas como máximo una vez por frame de render.
    /// Barato frente a OverlapSphere por enemigo.
    /// </summary>
    public static void RefreshPositionCacheIfNeeded()
    {
        int frame = Time.frameCount;
        if (s_CacheFrame == frame)
            return;

        s_CacheFrame = frame;
        EnemyRegistry.CollectActive(s_Transforms);
        int count = s_Transforms.Count;
        if (s_Positions.Length < count)
            s_Positions = new Vector3[Mathf.NextPowerOfTwo(count)];

        int written = 0;
        for (int i = 0; i < count; i++)
        {
            Transform t = s_Transforms[i];
            if (t == null)
                continue;
            s_Positions[written++] = t.position;
        }

        s_Count = written;
    }

    /// <summary>
    /// Separación por muestreo de vecinos en el cache (O(maxSamples), no O(n) completo).
    /// </summary>
    public static Vector3 SampleSeparation(Vector3 selfPos, float radius, int maxSamples, int sampleSeed)
    {
        if (radius <= 0f || maxSamples <= 0 || s_Count <= 1)
            return Vector3.zero;

        RefreshPositionCacheIfNeeded();
        if (s_Count <= 1)
            return Vector3.zero;

        float radiusSqr = radius * radius;
        int step = Mathf.Max(1, s_Count / maxSamples);
        int start = sampleSeed >= 0 ? (sampleSeed % s_Count + s_Count) % s_Count : 0;

        Vector3 sum = Vector3.zero;
        int hits = 0;

        for (int n = 0; n < maxSamples; n++)
        {
            int idx = (start + n * step) % s_Count;
            Vector3 other = s_Positions[idx];
            Vector3 diff = selfPos - other;
            diff.y = 0f;
            float sqr = diff.sqrMagnitude;
            if (sqr < 0.0001f || sqr > radiusSqr)
                continue;

            float dist = Mathf.Sqrt(sqr);
            sum += diff / dist * (1f / (dist + 0.15f));
            hits++;
        }

        if (hits == 0 || sum.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return sum.normalized;
    }

    /// <summary>
    /// Combina chase al pursuit point + weave + separación en una dirección XZ normalizada.
    /// </summary>
    public static Vector3 ComposePlanarMoveDirection(
        Vector3 selfPos,
        Vector3 playerPos,
        float slotAngleDeg,
        float orbitRadius,
        float time,
        float weavePhase,
        float weaveAmplitude,
        float weaveFrequency,
        float separationWeight,
        float separationRadius,
        int maxSeparationSamples,
        int sampleSeed)
    {
        // Cerca del anillo, reducir el offset para que melee pueda cerrar contacto.
        float planarDist = Vector2.Distance(
            new Vector2(selfPos.x, selfPos.z),
            new Vector2(playerPos.x, playerPos.z));
        float effectiveOrbit = orbitRadius;
        if (orbitRadius > 0.01f && planarDist < orbitRadius * 2f)
            effectiveOrbit = orbitRadius * Mathf.Clamp01(planarDist / (orbitRadius * 2f));

        Vector3 pursuit = GetPursuitPoint(playerPos, slotAngleDeg, effectiveOrbit);
        Vector3 toPursuit = pursuit - selfPos;
        toPursuit.y = 0f;

        Vector3 chase = Vector3.zero;
        if (toPursuit.sqrMagnitude > 0.0001f)
            chase = toPursuit.normalized;

        Vector3 weave = GetWeaveOffset(toPursuit.sqrMagnitude > 0.0001f ? toPursuit : chase, time, weavePhase, weaveAmplitude, weaveFrequency);
        Vector3 sep = SampleSeparation(selfPos, separationRadius, maxSeparationSamples, sampleSeed);

        Vector3 move = chase + weave + sep * Mathf.Max(0f, separationWeight);
        move.y = 0f;
        if (move.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return move.normalized;
    }

    public static void RandomizeIdentity(out float slotAngleDeg, out float weavePhase)
    {
        slotAngleDeg = Random.Range(0f, 360f);
        weavePhase = Random.Range(0f, Mathf.PI * 2f);
    }
}
