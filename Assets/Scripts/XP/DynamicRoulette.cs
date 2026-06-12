using System;
using System.Collections.Generic;
using UnityEngine;

public static class DynamicRoulette
{
    public static T Roll<T>(IReadOnlyDictionary<T, int> weights)
    {
        int totalWeight = 0;
        foreach (KeyValuePair<T, int> pair in weights)
            totalWeight += Mathf.Max(0, pair.Value);

        if (totalWeight <= 0)
            throw new InvalidOperationException("DynamicRoulette: no hay peso disponible.");

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int current = 0;

        foreach (KeyValuePair<T, int> pair in weights)
        {
            current += Mathf.Max(0, pair.Value);
            if (roll < current)
                return pair.Key;
        }

        foreach (T key in weights.Keys)
            return key;

        throw new InvalidOperationException("DynamicRoulette: fallo al resolver tirada.");
    }

    public static List<T> RollDistinct<T>(Dictionary<T, int> weights, int count, Func<T, T, bool> equals)
    {
        var result = new List<T>();
        if (weights == null || weights.Count == 0 || count <= 0)
            return result;

        var scratch = new Dictionary<T, int>(weights);
        int want = Mathf.Min(count, scratch.Count);

        for (int i = 0; i < want; i++)
        {
            T picked = Roll(scratch);
            result.Add(picked);
            scratch.Remove(picked);
        }

        return result;
    }

    public static void ApplyChoiceResult<T>(Dictionary<T, int> weights, T chosen, IReadOnlyList<T> offer, Func<T, T, bool> equals)
    {
        if (weights.ContainsKey(chosen))
            weights[chosen] = Mathf.Max(1, weights[chosen] - 1);

        for (int i = 0; i < offer.Count; i++)
        {
            T candidate = offer[i];
            if (equals(candidate, chosen))
                continue;
            if (!weights.ContainsKey(candidate))
                weights[candidate] = 5;
            weights[candidate] += 1;
        }
    }
}
