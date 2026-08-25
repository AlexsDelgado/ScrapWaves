using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PassiveItemLevelUpHandler : MonoBehaviour
{
    [SerializeField, Range(2, 3)] private int _choicesOffered = 3;
    [SerializeField] private List<PassiveItemData> _itemPool = new();
    [SerializeField] private PassiveItemManager _passiveItemManager;
    [SerializeField] private LevelUpChoiceUI _choiceUi;

    private readonly PassiveItemRoulette _roulette = new();

    /// <summary>Unfiltered production pool, used by deterministic testing tools.</summary>
    public IReadOnlyList<PassiveItemData> ItemPool => _itemPool;

    private void Awake()
    {
        if (_passiveItemManager == null)
            _passiveItemManager = GetComponent<PassiveItemManager>();
        if (_choiceUi == null)
            _choiceUi = GetComponent<LevelUpChoiceUI>();
    }

    public IEnumerator PresentAndApplyCoroutine(int newLevel)
    {
        List<PassiveItemData> unlockedPool = _itemPool;
        if (SaveManager.Instance != null)
        {
            unlockedPool = new List<PassiveItemData>(_itemPool.Count);
            for (int i = 0; i < _itemPool.Count; i++)
            {
                PassiveItemData data = _itemPool[i];
                if (data != null && SaveManager.Instance.IsUnlocked(data))
                    unlockedPool.Add(data);
            }
        }

        List<PassiveItemOffer> eligible = _passiveItemManager.BuildEligibleOffers(unlockedPool);
        if (eligible.Count == 0)
        {
            Debug.LogWarning("PassiveItemLevelUpHandler: sin ofertas elegibles. Revisa el pool de pasivos.", this);
            yield break;
        }

        List<PassiveItemOffer> offer = _roulette.BuildOffer(eligible, Mathf.Clamp(_choicesOffered, 2, 3));
        var uiOptions = new List<LevelUpChoiceOption>(offer.Count);
        for (int i = 0; i < offer.Count; i++)
        {
            PassiveItemOffer passiveOffer = offer[i];
            uiOptions.Add(new LevelUpChoiceOption(
                passiveOffer.DisplayLabel,
                PassiveItemUiText.BuildOfferDescription(passiveOffer),
                passiveOffer.Data?.Icon,
                PassiveItemUiText.GetPlaceholderKind(passiveOffer.Data)));
        }

        int selectedIndex = -1;
        yield return _choiceUi.PresentCoroutine("Elige un objeto", uiOptions, index => selectedIndex = index);

        if (selectedIndex < 0 || selectedIndex >= offer.Count)
            yield break;

        PassiveItemOffer chosen = offer[selectedIndex];
        _roulette.RegisterChoice(chosen, offer);
        _passiveItemManager.TryApplyOffer(chosen);
    }
}
