using System.Collections.Generic;

public class PassiveItemInventory
{
    private PassiveItemInstance _head;
    private PassiveItemInstance _core;
    private readonly PassiveItemInstance[] _arms = new PassiveItemInstance[2];
    private readonly PassiveItemInstance[] _legs = new PassiveItemInstance[2];

    public PassiveItemInstance Get(PassiveItemSlot slot, int slotIndex)
    {
        return slot switch
        {
            PassiveItemSlot.Head => slotIndex == 0 ? _head : null,
            PassiveItemSlot.Core => slotIndex == 0 ? _core : null,
            PassiveItemSlot.Arm => slotIndex >= 0 && slotIndex < _arms.Length ? _arms[slotIndex] : null,
            PassiveItemSlot.Leg => slotIndex >= 0 && slotIndex < _legs.Length ? _legs[slotIndex] : null,
            _ => null
        };
    }

    public bool IsValidSlotIndex(PassiveItemSlot slot, int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < GetCapacity(slot);
    }

    public int GetFirstFreeSlotIndex(PassiveItemSlot slot)
    {
        int capacity = GetCapacity(slot);
        for (int i = 0; i < capacity; i++)
        {
            if (Get(slot, i) == null)
                return i;
        }

        return -1;
    }

    public bool TryFindInstance(PassiveItemData data, out PassiveItemInstance instance)
    {
        instance = null;
        if (data == null)
            return false;

        if (data.Slot == PassiveItemSlot.Head && _head?.Data == data) { instance = _head; return true; }
        if (data.Slot == PassiveItemSlot.Core && _core?.Data == data) { instance = _core; return true; }

        PassiveItemInstance[] array = data.Slot == PassiveItemSlot.Arm ? _arms : _legs;
        if (data.Slot != PassiveItemSlot.Arm && data.Slot != PassiveItemSlot.Leg)
            return false;

        for (int i = 0; i < array.Length; i++)
        {
            if (array[i]?.Data == data)
            {
                instance = array[i];
                return true;
            }
        }

        return false;
    }

    public bool Contains(PassiveItemInstance instance)
    {
        if (instance == null || !IsValidSlotIndex(instance.Slot, instance.SlotIndex))
            return false;

        return ReferenceEquals(Get(instance.Slot, instance.SlotIndex), instance);
    }

    public int CountEquipped(PassiveItemSlot slot)
    {
        return slot switch
        {
            PassiveItemSlot.Head => _head != null ? 1 : 0,
            PassiveItemSlot.Core => _core != null ? 1 : 0,
            PassiveItemSlot.Arm => CountFilled(_arms),
            PassiveItemSlot.Leg => CountFilled(_legs),
            _ => 0
        };
    }

    public bool HasFreeSlot(PassiveItemSlot slot) => CountEquipped(slot) < GetCapacity(slot);

    public static int GetCapacity(PassiveItemSlot slot) => slot switch
    {
        PassiveItemSlot.Head => 1,
        PassiveItemSlot.Core => 1,
        PassiveItemSlot.Arm => 2,
        PassiveItemSlot.Leg => 2,
        _ => 0
    };

    public bool TryAssign(PassiveItemInstance instance)
    {
        if (instance?.Data == null)
            return false;

        int slotIndex = GetFirstFreeSlotIndex(instance.Data.Slot);
        return TryAssign(instance, slotIndex);
    }

    public bool TryAssign(PassiveItemInstance instance, int slotIndex)
    {
        if (instance?.Data == null || !IsValidSlotIndex(instance.Data.Slot, slotIndex))
            return false;

        if (TryFindInstance(instance.Data, out _) || Get(instance.Data.Slot, slotIndex) != null)
            return false;

        Set(instance.Data.Slot, slotIndex, instance);
        return true;
    }

    public bool TryReplace(PassiveItemSlot slot, int slotIndex, PassiveItemInstance replacement, out PassiveItemInstance previous)
    {
        previous = null;
        if (replacement?.Data == null || replacement.Data.Slot != slot || !IsValidSlotIndex(slot, slotIndex))
            return false;

        if (TryFindInstance(replacement.Data, out PassiveItemInstance duplicate) &&
            !ReferenceEquals(duplicate, Get(slot, slotIndex)))
        {
            return false;
        }

        previous = Get(slot, slotIndex);
        Set(slot, slotIndex, replacement);
        return true;
    }

    public bool TryRemove(PassiveItemSlot slot, int slotIndex, out PassiveItemInstance removed)
    {
        removed = null;
        if (!IsValidSlotIndex(slot, slotIndex))
            return false;

        removed = Get(slot, slotIndex);
        if (removed == null)
            return false;

        Set(slot, slotIndex, null);
        return true;
    }

    public bool Clear()
    {
        bool hadItems = _head != null || _core != null || CountFilled(_arms) > 0 || CountFilled(_legs) > 0;
        _head = null;
        _core = null;
        for (int i = 0; i < _arms.Length; i++)
            _arms[i] = null;
        for (int i = 0; i < _legs.Length; i++)
            _legs[i] = null;
        return hadItems;
    }

    private void Set(PassiveItemSlot slot, int slotIndex, PassiveItemInstance instance)
    {
        if (instance != null)
        {
            instance.Slot = slot;
            instance.SlotIndex = slotIndex;
        }

        switch (slot)
        {
            case PassiveItemSlot.Head:
                _head = instance;
                break;
            case PassiveItemSlot.Core:
                _core = instance;
                break;
            case PassiveItemSlot.Arm:
                _arms[slotIndex] = instance;
                break;
            case PassiveItemSlot.Leg:
                _legs[slotIndex] = instance;
                break;
        }
    }

    public IEnumerable<PassiveItemInstance> GetAllEquipped()
    {
        if (_head != null) yield return _head;
        if (_core != null) yield return _core;
        foreach (PassiveItemInstance arm in _arms)
            if (arm != null) yield return arm;
        foreach (PassiveItemInstance leg in _legs)
            if (leg != null) yield return leg;
    }

    private static int CountFilled(PassiveItemInstance[] slots)
    {
        int count = 0;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) count++;
        return count;
    }

}
