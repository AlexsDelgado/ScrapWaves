using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PassiveStatBonus
{
    public StatType StatType;
    public float ValuePerLevel;
}

[CreateAssetMenu(fileName = "PassiveItem", menuName = "ScrapWaves/Passives/Passive Item")]
public class PassiveItemData : ScriptableObject
{
    [SerializeField] private string _displayName;
    [SerializeField] private PassiveItemSlot _slot;
    [SerializeField, Min(1)] private int _maxLevel = 5;
    [SerializeField] private Sprite _icon;
    [SerializeField] private List<PassiveStatBonus> _bonusesPerLevel = new();

    public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
    public PassiveItemSlot Slot => _slot;
    public int MaxLevel => _maxLevel;
    public Sprite Icon => _icon;
    public IReadOnlyList<PassiveStatBonus> BonusesPerLevel => _bonusesPerLevel;
}
