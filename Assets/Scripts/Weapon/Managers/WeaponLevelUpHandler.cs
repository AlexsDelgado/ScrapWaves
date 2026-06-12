using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct WeaponLevelUpOffer
{
    public WeaponLevelUpOffer(WeaponData data, bool isUpgrade, WeaponInstance targetInstance)
    {
        Data = data;
        IsUpgrade = isUpgrade;
        TargetInstance = targetInstance;
    }

    public WeaponData Data { get; }
    public bool IsUpgrade { get; }
    public WeaponInstance TargetInstance { get; }

    public string RouletteKey => IsUpgrade
        ? $"{Data.name}@up@{TargetInstance?.Data?.WeaponId}"
        : Data.name;

    public string DisplayLabel
    {
        get
        {
            if (Data == null)
                return "(null)";
            string name = string.IsNullOrEmpty(Data.DisplayName) ? Data.name : Data.DisplayName;
            if (!IsUpgrade)
                return name;
            return $"{name} Lv.{(TargetInstance?.Level ?? 0) + 1}";
        }
    }
}

[DisallowMultipleComponent]
public class WeaponLevelUpHandler : MonoBehaviour
{
    [SerializeField, Range(2, 3)] private int _choicesOffered = 3;
    [SerializeField] private List<WeaponData> _weaponPool = new();
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private LevelUpChoiceUI _choiceUi;

    private readonly Dictionary<string, int> _rouletteWeights = new();

    private void Awake()
    {
        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();
        if (_choiceUi == null)
            _choiceUi = GetComponent<LevelUpChoiceUI>();
    }

    public IEnumerator PresentAndApplyCoroutine(int newLevel)
    {
        List<WeaponLevelUpOffer> eligible = BuildEligibleOffers();
        if (eligible.Count == 0)
        {
            Debug.LogWarning("WeaponLevelUpHandler: sin armas elegibles.", this);
            yield break;
        }

        List<WeaponLevelUpOffer> offer = BuildWeightedOffer(eligible, Mathf.Clamp(_choicesOffered, 2, 3));
        var uiOptions = new List<LevelUpChoiceOption>(offer.Count);
        for (int i = 0; i < offer.Count; i++)
        {
            WeaponLevelUpOffer weaponOffer = offer[i];
            string description = BuildWeaponOfferDescription(weaponOffer);
            uiOptions.Add(new LevelUpChoiceOption(
                weaponOffer.DisplayLabel,
                description,
                weaponOffer.Data?.Icon,
                HudPlaceholderKind.Weapon));
        }

        int selectedIndex = -1;
        yield return _choiceUi.PresentCoroutine("Elige un arma", uiOptions, index => selectedIndex = index);

        if (selectedIndex < 0 || selectedIndex >= offer.Count)
            yield break;

        WeaponLevelUpOffer chosen = offer[selectedIndex];
        RegisterRouletteChoice(chosen, offer);
        _weaponManager.TryAddOrUpgradeWeapon(chosen.Data);
    }

    private List<WeaponLevelUpOffer> BuildEligibleOffers()
    {
        var offers = new List<WeaponLevelUpOffer>();
        if (_weaponPool == null || _weaponManager == null)
            return offers;

        bool slotsFull = !_weaponManager.CanAddWeapon();

        if (!slotsFull)
        {
            for (int i = 0; i < _weaponPool.Count; i++)
            {
                WeaponData data = _weaponPool[i];
                if (data == null)
                    continue;

                if (_weaponManager.TryGetEquippedWeapon(data, out WeaponInstance instance))
                {
                    if (instance.Level < 10)
                        offers.Add(new WeaponLevelUpOffer(data, true, instance));
                }
                else
                {
                    offers.Add(new WeaponLevelUpOffer(data, false, null));
                }
            }

            return offers;
        }

        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        for (int i = 0; i < equipped.Count; i++)
        {
            WeaponInstance runtime = equipped[i]?.Runtime;
            if (runtime?.Data == null || runtime.Level >= 10)
                continue;
            offers.Add(new WeaponLevelUpOffer(runtime.Data, true, runtime));
        }

        return offers;
    }

    private List<WeaponLevelUpOffer> BuildWeightedOffer(IReadOnlyList<WeaponLevelUpOffer> eligible, int count)
    {
        var pool = new Dictionary<WeaponLevelUpOffer, int>();
        for (int i = 0; i < eligible.Count; i++)
        {
            WeaponLevelUpOffer offer = eligible[i];
            EnsureWeight(offer.RouletteKey);
            pool[offer] = _rouletteWeights[offer.RouletteKey];
        }

        return DynamicRoulette.RollDistinct(pool, count, (a, b) => a.RouletteKey == b.RouletteKey);
    }

    private void RegisterRouletteChoice(WeaponLevelUpOffer chosen, IReadOnlyList<WeaponLevelUpOffer> offer)
    {
        EnsureWeight(chosen.RouletteKey);
        _rouletteWeights[chosen.RouletteKey] = Mathf.Max(1, _rouletteWeights[chosen.RouletteKey] - 1);

        for (int i = 0; i < offer.Count; i++)
        {
            WeaponLevelUpOffer candidate = offer[i];
            if (candidate.RouletteKey == chosen.RouletteKey)
                continue;

            EnsureWeight(candidate.RouletteKey);
            _rouletteWeights[candidate.RouletteKey] += 1;
        }
    }

    private void EnsureWeight(string key)
    {
        if (!_rouletteWeights.ContainsKey(key))
            _rouletteWeights[key] = 5;
    }

    private static string BuildWeaponOfferDescription(WeaponLevelUpOffer offer)
    {
        if (offer.Data == null)
            return string.Empty;

        if (!offer.IsUpgrade)
            return "Nueva arma para tu loadout.";

        int current = offer.TargetInstance?.Level ?? 1;
        return $"Mejora nivel {current} → {current + 1}.";
    }
}
