using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lleva la cuenta de los enemigos dormidos por altura (congelados y ocultos, pero presentes)
/// y aplica un tope para que una partida larga no acumule cientos de GameObjects.
///
/// Al pasarse del tope se liberan al pool los más lejanos al jugador primero, que son los que
/// menos probabilidad tienen de volver a verse, priorizando además los prefabs cuyo pool está
/// sin stock: el cuello de botella real no es el techo global de enemigos activos sino el
/// tamaño de pool POR PREFAB, y si los dormidos se concentran en un tipo ese tipo deja de
/// spawnear en silencio.
/// </summary>
public static class EnemyDormancyRegistry
{
    private const int DefaultMaxDormant = 150;

    private static readonly List<EnemyVerticalEngagement> s_Dormant = new(192);
    private static readonly List<EnemyVerticalEngagement> s_EvictionBuffer = new(64);
    private static int s_MaxDormant = DefaultMaxDormant;

    public static int DormantCount => s_Dormant.Count;

    public static int MaxDormant
    {
        get => s_MaxDormant;
        set
        {
            s_MaxDormant = Mathf.Max(0, value);
            EnforceCap();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_Dormant.Clear();
        s_EvictionBuffer.Clear();
        s_MaxDormant = DefaultMaxDormant;
    }

    public static void Register(EnemyVerticalEngagement engagement)
    {
        if (engagement == null || s_Dormant.Contains(engagement))
            return;

        s_Dormant.Add(engagement);
        EnforceCap();
    }

    public static void Unregister(EnemyVerticalEngagement engagement)
    {
        if (engagement == null)
            return;

        s_Dormant.Remove(engagement);
    }

    /// <summary>Para lecturas de QA: cuántos dormidos hay de un prefab concreto.</summary>
    public static int CountForPrefab(GameObject prefab)
    {
        if (prefab == null)
            return 0;

        int count = 0;
        for (int i = 0; i < s_Dormant.Count; i++)
        {
            if (s_Dormant[i] != null && SourcePrefabOf(s_Dormant[i]) == prefab)
                count++;
        }

        return count;
    }

    private static void EnforceCap()
    {
        PruneDestroyed();

        int excess = s_Dormant.Count - s_MaxDormant;
        if (excess <= 0)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        Vector3 reference = player != null ? player.position : Vector3.zero;

        s_EvictionBuffer.Clear();
        s_EvictionBuffer.AddRange(s_Dormant);

        // Primero los prefabs sin stock en el pool, después por distancia descendente.
        s_EvictionBuffer.Sort((a, b) =>
        {
            int starvedCompare = IsPoolStarved(b).CompareTo(IsPoolStarved(a));
            if (starvedCompare != 0)
                return starvedCompare;

            float da = (a.transform.position - reference).sqrMagnitude;
            float db = (b.transform.position - reference).sqrMagnitude;
            return db.CompareTo(da);
        });

        for (int i = 0; i < s_EvictionBuffer.Count && excess > 0; i++)
        {
            EnemyVerticalEngagement victim = s_EvictionBuffer[i];
            if (victim == null)
                continue;

            s_Dormant.Remove(victim);
            excess--;

            // Despawn restaura el estado congelado vía IEnemySpawnLifecycle.OnPoolDespawn,
            // así que la instancia vuelve sana al pool.
            if (victim.TryGetComponent(out SwarmPooledEnemy pooled) && pooled.IsBound)
            {
                pooled.Despawn();
                continue;
            }

            victim.ExitDormancy();
            EnemyPoolProfiler.RegisterDestroy();
            Object.Destroy(victim.gameObject);
        }

        s_EvictionBuffer.Clear();
    }

    private static void PruneDestroyed()
    {
        for (int i = s_Dormant.Count - 1; i >= 0; i--)
        {
            if (s_Dormant[i] == null)
                s_Dormant.RemoveAt(i);
        }
    }

    private static bool IsPoolStarved(EnemyVerticalEngagement engagement)
    {
        GameObject prefab = SourcePrefabOf(engagement);
        if (prefab == null || EnemyPoolRegistry.Instance == null)
            return false;

        return !EnemyPoolRegistry.Instance.HasAvailableInstance(prefab);
    }

    private static GameObject SourcePrefabOf(EnemyVerticalEngagement engagement)
    {
        if (engagement == null)
            return null;

        return engagement.TryGetComponent(out SwarmPooledEnemy pooled) ? pooled.SourcePrefab : null;
    }
}
