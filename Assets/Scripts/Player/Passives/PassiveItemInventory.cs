using System.Collections.Generic;

public class PassiveItemInventory
{
    private PassiveItemInstance _head;
    private PassiveItemInstance _torso;
    private readonly PassiveItemInstance[] _arms = new PassiveItemInstance[2];
    private readonly PassiveItemInstance[] _legs = new PassiveItemInstance[2];

    public PassiveItemInstance Get(PassiveItemSlot slot, int slotIndex)
    {
        return slot switch
        {
            PassiveItemSlot.Head => _head,
            PassiveItemSlot.Torso => _torso,
            PassiveItemSlot.Arm => slotIndex >= 0 && slotIndex < _arms.Length ? _arms[slotIndex] : null,
            PassiveItemSlot.Leg => slotIndex >= 0 && slotIndex < _legs.Length ? _legs[slotIndex] : null,
            _ => null
        };
    }

    public bool TryFindInstance(PassiveItemData data, out PassiveItemInstance instance)
    {
        instance = null;
        if (data == null)
            return false;

        if (data.Slot == PassiveItemSlot.Head && _head?.Data == data) { instance = _head; return true; }
        if (data.Slot == PassiveItemSlot.Torso && _torso?.Data == data) { instance = _torso; return true; }

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

    public int CountEquipped(PassiveItemSlot slot)
    {
        return slot switch
        {
            PassiveItemSlot.Head => _head != null ? 1 : 0,
            PassiveItemSlot.Torso => _torso != null ? 1 : 0,
            PassiveItemSlot.Arm => CountFilled(_arms),
            PassiveItemSlot.Leg => CountFilled(_legs),
            _ => 0
        };
    }

    public bool HasFreeSlot(PassiveItemSlot slot) => CountEquipped(slot) < GetCapacity(slot);

    public static int GetCapacity(PassiveItemSlot slot) => slot switch
    {
        PassiveItemSlot.Head => 1,
        PassiveItemSlot.Torso => 1,
        PassiveItemSlot.Arm => 2,
        PassiveItemSlot.Leg => 2,
        _ => 0
    };

    public bool TryAssign(PassiveItemInstance instance)
    {
        if (instance?.Data == null)
            return false;

        PassiveItemSlot slot = instance.Data.Slot;
        if (!HasFreeSlot(slot))
            return false;

        switch (slot)
        {
            case PassiveItemSlot.Head:
                if (_head != null) return false;
                instance.Slot = slot;
                instance.SlotIndex = 0;
                _head = instance;
                return true;
            case PassiveItemSlot.Torso:
                if (_torso != null) return false;
                instance.Slot = slot;
                instance.SlotIndex = 0;
                _torso = instance;
                return true;
            case PassiveItemSlot.Arm:
                return TryAssignToArray(_arms, slot, instance);
            case PassiveItemSlot.Leg:
                return TryAssignToArray(_legs, slot, instance);
            default:
                return false;
        }
    }

    public IEnumerable<PassiveItemInstance> GetAllEquipped()
    {
        if (_head != null) yield return _head;
        if (_torso != null) yield return _torso;
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

    private static bool TryAssignToArray(PassiveItemInstance[] slots, PassiveItemSlot slot, PassiveItemInstance instance)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                continue;

            instance.Slot = slot;
            instance.SlotIndex = i;
            slots[i] = instance;
            return true;
        }

        return false;
    }
}
