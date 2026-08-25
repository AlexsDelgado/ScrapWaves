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
        AddSlotOffers(offers, pool, PassiveItemSlot.Core);
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
        if (data == null)
            return false;

        int slotIndex = _inventory.GetFirstFreeSlotIndex(data.Slot);
        if (slotIndex < 0)
            return false;

        return TrySetItem(data.Slot, slotIndex, data, 1);
    }

    public bool TryEquip(PassiveItemData data, int slotIndex)
    {
        return data != null && TrySetItem(data.Slot, slotIndex, data, 1);
    }

    /// <summary>
    /// Assigns an item and exact level to one physical slot. Passing null removes the item.
    /// Replacement and stat reconciliation are exposed as one logical inventory change.
    /// </summary>
    public bool TrySetItem(PassiveItemSlot slot, int slotIndex, PassiveItemData data, int level = 1)
    {
        if (data == null)
            return TryUnequip(slot, slotIndex);

        if (data.Slot != slot || !_inventory.IsValidSlotIndex(slot, slotIndex) ||
            level < 1 || level > data.MaxLevel)
        {
            return false;
        }

        PassiveItemInstance current = _inventory.Get(slot, slotIndex);
        if (current?.Data == data)
            return TrySetLevel(current, level);

        if (_inventory.TryFindInstance(data, out _))
            return false;

        int previousEffectiveMaxHealth = GetEffectiveMaxHealth();
        float previousRawMaxHealthBonus = GetMaxHealthBonus(current?.Data, current?.Level ?? 0);
        var replacement = new PassiveItemInstance { Data = data, Level = level };
        if (!_inventory.TryReplace(slot, slotIndex, replacement, out PassiveItemInstance removed))
            return false;

        if (removed != null && _playerStats != null)
            _playerStats.RemoveModifiersFromSource(removed);
        ApplyModifiersForInstance(replacement);

        float newRawMaxHealthBonus = GetMaxHealthBonus(data, level);
        SynchronizeDependentState(previousEffectiveMaxHealth,
            Mathf.RoundToInt(newRawMaxHealthBonus - previousRawMaxHealthBonus));
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool TryUpgrade(PassiveItemInstance instance)
    {
        return instance != null && instance.CanUpgrade && TrySetLevel(instance, instance.Level + 1);
    }

    public bool TrySetLevel(PassiveItemSlot slot, int slotIndex, int level)
    {
        return TrySetLevel(_inventory.Get(slot, slotIndex), level);
    }

    public bool TrySetLevel(PassiveItemInstance instance, int level)
    {
        if (instance?.Data == null || !_inventory.Contains(instance) ||
            level < 1 || level > instance.Data.MaxLevel)
        {
            return false;
        }

        if (instance.Level == level)
            return true;

        int previousEffectiveMaxHealth = GetEffectiveMaxHealth();
        int previousLevel = instance.Level;
        instance.Level = level;
        ApplyModifiersForInstance(instance);
        float oldRawBonus = GetMaxHealthBonus(instance.Data, previousLevel);
        float newRawBonus = GetMaxHealthBonus(instance.Data, level);
        SynchronizeDependentState(previousEffectiveMaxHealth, Mathf.RoundToInt(newRawBonus - oldRawBonus));
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool TryUnequip(PassiveItemSlot slot, int slotIndex)
    {
        PassiveItemInstance current = _inventory.Get(slot, slotIndex);
        if (current == null)
            return false;

        int previousEffectiveMaxHealth = GetEffectiveMaxHealth();
        int fallbackHealthDelta = -Mathf.RoundToInt(GetMaxHealthBonus(current.Data, current.Level));
        if (!_inventory.TryRemove(slot, slotIndex, out PassiveItemInstance removed))
            return false;

        if (_playerStats != null)
            _playerStats.RemoveModifiersFromSource(removed);
        SynchronizeDependentState(previousEffectiveMaxHealth, fallbackHealthDelta);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool ClearAll()
    {
        var equipped = new List<PassiveItemInstance>(_inventory.GetAllEquipped());
        if (equipped.Count == 0)
            return false;

        int previousEffectiveMaxHealth = GetEffectiveMaxHealth();
        float removedRawMaxHealth = 0f;
        for (int i = 0; i < equipped.Count; i++)
        {
            PassiveItemInstance instance = equipped[i];
            removedRawMaxHealth += GetMaxHealthBonus(instance.Data, instance.Level);
            if (_playerStats != null)
                _playerStats.RemoveModifiersFromSource(instance);
        }

        _inventory.Clear();
        SynchronizeDependentState(previousEffectiveMaxHealth, -Mathf.RoundToInt(removedRawMaxHealth));
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

    private void ApplyModifiersForInstance(PassiveItemInstance instance)
    {
        if (_playerStats == null || instance?.Data == null)
            return;

        _playerStats.RemoveModifiersFromSource(instance);

        IReadOnlyList<PassiveStatBonus> bonuses = instance.Data.BonusesPerLevel;
        for (int i = 0; i < bonuses.Count; i++)
        {
            PassiveStatBonus bonus = bonuses[i];
            float value = bonus.GetValueForLevel(instance.Level);
            if (Mathf.Approximately(value, 0f))
                continue;

            _playerStats.AddModifier(new StatModifier(
                bonus.StatType,
                value,
                StatUpgradeSource.PassiveItem,
                instance,
                bonus.ModifierType));
        }
    }

    private int GetEffectiveMaxHealth()
    {
        if (_playerStats != null && _playerStats.GetDefinition(StatType.MaxHealth) != null)
            return _playerStats.GetMaxHealthTotal();

        return TryGetComponent(out PlayerHealth health) ? health.MaxHealth : 0;
    }

    private void SynchronizeDependentState(int previousEffectiveMaxHealth, int fallbackHealthDelta)
    {
        int healthDelta = fallbackHealthDelta;
        if (_playerStats != null && _playerStats.GetDefinition(StatType.MaxHealth) != null)
            healthDelta = _playerStats.GetMaxHealthTotal() - previousEffectiveMaxHealth;

        if (TryGetComponent(out PlayerHealth health))
        {
            health.ApplyMaxHealthDelta(healthDelta);

            health.SetShieldConfig(
                GetConfiguredStatInt(StatType.ShieldCharges),
                GetConfiguredStat(StatType.ShieldRechargeDelay));
        }

        if (TryGetComponent(out PlayerMovement movement))
            movement.RefreshPassiveResources();
    }

    private float GetConfiguredStat(StatType statType)
    {
        return _playerStats != null && _playerStats.GetDefinition(statType) != null
            ? _playerStats.GetStat(statType)
            : 0f;
    }

    private int GetConfiguredStatInt(StatType statType)
    {
        return _playerStats != null && _playerStats.GetDefinition(statType) != null
            ? _playerStats.GetStatInt(statType)
            : 0;
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
                total += bonuses[i].GetValueForLevel(level);
        }

        return total;
    }
}
