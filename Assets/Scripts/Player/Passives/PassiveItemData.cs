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
public class PassiveItemData : ScriptableObject, IUnlockable
{
    [SerializeField] private string _displayName;
    [SerializeField] private PassiveItemSlot _slot;
    [SerializeField, Min(1)] private int _maxLevel = 5;
    [SerializeField] private Sprite _icon;
    [SerializeField] private List<PassiveStatBonus> _bonusesPerLevel = new();

    [Header("Meta / Desbloqueo")]
    [SerializeField, Tooltip("Vacío = usa el nombre del asset como ID de desbloqueo.")]
    private string _unlockId;
    [SerializeField, Tooltip("Todo el contenido existente arranca desbloqueado; tildar en falso solo en ítems nuevos que deban pasar por el sistema de logros/tienda.")]
    private bool _unlockedFromStart = true;
    [SerializeField] private UnlockRequirement _requirement;

    public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
    public PassiveItemSlot Slot => _slot;
    public int MaxLevel => _maxLevel;
    public Sprite Icon => _icon;
    public IReadOnlyList<PassiveStatBonus> BonusesPerLevel => _bonusesPerLevel;

    public string UnlockId => string.IsNullOrEmpty(_unlockId) ? name : _unlockId;
    public bool UnlockedFromStart => _unlockedFromStart;
    public UnlockRequirement Requirement => _requirement;
}
