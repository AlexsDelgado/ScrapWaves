public readonly struct PassiveItemOffer
{
    public PassiveItemOffer(PassiveItemData data, bool isUpgrade, PassiveItemInstance targetInstance)
    {
        Data = data;
        IsUpgrade = isUpgrade;
        TargetInstance = targetInstance;
    }

    public PassiveItemData Data { get; }
    public bool IsUpgrade { get; }
    public PassiveItemInstance TargetInstance { get; }

    public string RouletteKey => IsUpgrade
        ? $"{Data.name}@up@{TargetInstance.Slot}_{TargetInstance.SlotIndex}"
        : Data.name;

    public string DisplayLabel
    {
        get
        {
            if (Data == null)
                return "(null)";
            if (!IsUpgrade)
                return Data.DisplayName;
            return $"{Data.DisplayName} Lv.{TargetInstance.Level + 1}";
        }
    }

    public string Description => IsUpgrade
        ? $"Mejora el objeto equipado (nivel {TargetInstance?.Level ?? 0} → {(TargetInstance?.Level ?? 0) + 1})."
        : $"Equipa en slot {Data?.Slot}.";
}
