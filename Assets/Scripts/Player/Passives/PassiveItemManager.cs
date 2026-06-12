using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PassiveItemManager : MonoBehaviour
{
    [SerializeField] private PlayerStats _playerStats;

    private readonly PassiveItemInventory _inventory = new();

    public PassiveItemInventory Inventory => _inventory;
    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (_playerStats == null)
            _playerStats = GetComponent<PlayerStats>();
    }

    public List<PassiveItemOffer> BuildEligibleOffers(IReadOnlyList<PassiveItemData> pool)
    {
        var offers = new List<PassiveItemOffer>();
        if (pool == null)
            return offers;

        AddSlotOffers(offers, pool, PassiveItemSlot.Head);
        AddSlotOffers(offers, pool, PassiveItemSlot.Torso);
        AddSlotOffers(offers, pool, PassiveItemSlot.Arm);
        AddSlotOffers(offers, pool, PassiveItemSlot.Leg);

        return offers;
    }

    public bool TryApplyOffer(PassiveItemOffer offer)
    {
        if (offer.Data == null)
            return false;

        if (offer.IsUpgrade)
            return TryUpgrade(offer.TargetInstance);

        return TryEquip(offer.Data);
    }

    public bool TryEquip(PassiveItemData data)
    {
        if (data == null || !_inventory.HasFreeSlot(data.Slot))
            return false;

        if (_inventory.TryFindInstance(data, out _))
            return false;

        var instance = new PassiveItemInstance
        {
            Data = data,
            Level = 1
        };

        if (!_inventory.TryAssign(instance))
            return false;

        ApplyModifiersForInstance(instance, 0);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool TryUpgrade(PassiveItemInstance instance)
    {
        if (instance == null || !instance.CanUpgrade)
            return false;

        int previousLevel = instance.Level;
        instance.Level++;
        ApplyModifiersForInstance(instance, previousLevel);
        OnInventoryChanged?.Invoke();
        return true;
    }

    private void AddSlotOffers(List<PassiveItemOffer> offers, IReadOnlyList<PassiveItemData> pool, PassiveItemSlot slot)
    {
        int capacity = PassiveItemInventory.GetCapacity(slot);
        int equipped = _inventory.CountEquipped(slot);

        if (equipped >= capacity)
        {
            foreach (PassiveItemInstance instance in _inventory.GetAllEquipped())
            {
                if (instance.Slot != slot || !instance.CanUpgrade)
                    continue;
                offers.Add(new PassiveItemOffer(instance.Data, true, instance));
            }

            return;
        }

        if (equipped == 0)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                PassiveItemData data = pool[i];
                if (data != null && data.Slot == slot)
                    offers.Add(new PassiveItemOffer(data, false, null));
            }

            return;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            PassiveItemData data = pool[i];
            if (data != null && data.Slot == slot && _inventory.HasFreeSlot(slot))
            {
                if (!_inventory.TryFindInstance(data, out _))
                    offers.Add(new PassiveItemOffer(data, false, null));
            }
        }

        foreach (PassiveItemInstance instance in _inventory.GetAllEquipped())
        {
            if (instance.Slot != slot || !instance.CanUpgrade)
                continue;
            offers.Add(new PassiveItemOffer(instance.Data, true, instance));
        }
    }

    private void ApplyModifiersForInstance(PassiveItemInstance instance, int previousLevel)
    {
        if (_playerStats == null || instance?.Data == null)
            return;

        float oldMaxHealthBonus = GetMaxHealthBonus(instance.Data, previousLevel);
        _playerStats.RemoveModifiersFromSource(instance);

        IReadOnlyList<PassiveStatBonus> bonuses = instance.Data.BonusesPerLevel;
        for (int i = 0; i < bonuses.Count; i++)
        {
            PassiveStatBonus bonus = bonuses[i];
            float value = bonus.ValuePerLevel * instance.Level;
            if (Mathf.Approximately(value, 0f))
                continue;

            _playerStats.AddModifier(new StatModifier(
                bonus.StatType,
                value,
                StatUpgradeSource.PassiveItem,
                instance));
        }

        float newMaxHealthBonus = GetMaxHealthBonus(instance.Data, instance.Level);
        int healthDelta = Mathf.RoundToInt(newMaxHealthBonus - oldMaxHealthBonus);
        if (healthDelta > 0 && TryGetComponent(out PlayerHealth health))
            health.ApplyMaxHealthIncrease(healthDelta);
    }

    private static float GetMaxHealthBonus(PassiveItemData data, int level)
    {
        if (data == null || level <= 0)
            return 0f;

        float total = 0f;
        IReadOnlyList<PassiveStatBonus> bonuses = data.BonusesPerLevel;
        for (int i = 0; i < bonuses.Count; i++)
        {
            if (bonuses[i].StatType == StatType.MaxHealth)
                total += bonuses[i].ValuePerLevel * level;
        }

        return total;
    }
}
