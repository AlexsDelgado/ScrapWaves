[System.Serializable]
public class PassiveItemInstance
{
    public PassiveItemData Data;
    public int Level = 1;
    public PassiveItemSlot Slot;
    public int SlotIndex;

    public bool CanUpgrade => Data != null && Level < Data.MaxLevel;
}
