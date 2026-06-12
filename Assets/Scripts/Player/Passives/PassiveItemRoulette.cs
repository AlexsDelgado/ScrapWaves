using System;
using System.Collections.Generic;

public class PassiveItemRoulette
{
    private readonly Dictionary<string, int> _weights = new();

    public void EnsureWeight(string key, int initialWeight = 5)
    {
        if (!_weights.ContainsKey(key))
            _weights[key] = initialWeight;
    }

    public List<PassiveItemOffer> BuildOffer(IReadOnlyList<PassiveItemOffer> eligible, int count)
    {
        var result = new List<PassiveItemOffer>();
        if (eligible == null || eligible.Count == 0)
            return result;

        var pool = new Dictionary<PassiveItemOffer, int>();
        for (int i = 0; i < eligible.Count; i++)
        {
            PassiveItemOffer offer = eligible[i];
            EnsureWeight(offer.RouletteKey);
            pool[offer] = _weights[offer.RouletteKey];
        }

        int want = Math.Min(count, pool.Count);
        var picked = DynamicRoulette.RollDistinct(pool, want, OffersEqual);

        for (int i = 0; i < picked.Count; i++)
            result.Add(picked[i]);

        return result;
    }

    public void RegisterChoice(PassiveItemOffer chosen, IReadOnlyList<PassiveItemOffer> offer)
    {
        EnsureWeight(chosen.RouletteKey);
        _weights[chosen.RouletteKey] = Math.Max(1, _weights[chosen.RouletteKey] - 1);

        for (int i = 0; i < offer.Count; i++)
        {
            PassiveItemOffer candidate = offer[i];
            if (OffersEqual(candidate, chosen))
                continue;

            EnsureWeight(candidate.RouletteKey);
            _weights[candidate.RouletteKey] += 1;
        }
    }

    private static bool OffersEqual(PassiveItemOffer a, PassiveItemOffer b) =>
        a.RouletteKey == b.RouletteKey;
}
