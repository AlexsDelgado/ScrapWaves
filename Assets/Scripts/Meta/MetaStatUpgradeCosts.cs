using UnityEngine;

/// <summary>
/// Costos placeholder de meta-upgrades de stats (Spec no lista precios).
/// </summary>
[CreateAssetMenu(fileName = "MetaStatUpgradeCosts", menuName = "ScrapWaves/Meta/Meta Stat Upgrade Costs")]
public class MetaStatUpgradeCosts : ScriptableObject
{
    [SerializeField, Min(1)] private int _baseCost = 25;
    [SerializeField, Min(1f)] private float _costGrowth = 1.35f;
    [SerializeField, Min(1)] private int _itemUpgradeBaseCost = 40;
    [SerializeField, Min(1f)] private float _itemUpgradeGrowth = 1.5f;

    public int GetStatUpgradeCost(int nextLevel)
    {
        nextLevel = Mathf.Clamp(nextLevel, 1, 10);
        return Mathf.Max(1, Mathf.RoundToInt(_baseCost * Mathf.Pow(_costGrowth, nextLevel - 1)));
    }

    public int GetItemUpgradeCost(int nextLevel)
    {
        nextLevel = Mathf.Clamp(nextLevel, 1, 3);
        return Mathf.Max(1, Mathf.RoundToInt(_itemUpgradeBaseCost * Mathf.Pow(_itemUpgradeGrowth, nextLevel - 1)));
    }
}
