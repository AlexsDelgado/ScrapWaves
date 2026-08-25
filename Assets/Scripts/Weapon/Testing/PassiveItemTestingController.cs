using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PassiveItemTestingController : MonoBehaviour
{
    public readonly struct SlotDescriptor
    {
        public SlotDescriptor(PassiveItemSlot slot, int slotIndex, string label)
        {
            Slot = slot;
            SlotIndex = slotIndex;
            Label = label;
        }

        public PassiveItemSlot Slot { get; }
        public int SlotIndex { get; }
        public string Label { get; }
    }

    private static readonly SlotDescriptor[] SlotDefinitions =
    {
        new(PassiveItemSlot.Head, 0, "Head"),
        new(PassiveItemSlot.Core, 0, "Core"),
        new(PassiveItemSlot.Arm, 0, "Arm 1"),
        new(PassiveItemSlot.Arm, 1, "Arm 2"),
        new(PassiveItemSlot.Leg, 0, "Leg 1"),
        new(PassiveItemSlot.Leg, 1, "Leg 2")
    };
    private static readonly IReadOnlyList<SlotDescriptor> ReadOnlySlotDefinitions = Array.AsReadOnly(SlotDefinitions);

    private static readonly HashSet<StatType> ExactOverrideStats = new()
    {
        StatType.DamageMultiplier,
        StatType.EliteDamageMultiplier,
        StatType.AttackSpeedMultiplier,
        StatType.ProjectileAreaSize,
        StatType.CriticalChance,
        StatType.CriticalDamage,
        StatType.Knockback,
        StatType.AmmoMultiplier
    };

    private static readonly IReadOnlyList<PassiveItemData> EmptyItemList = Array.Empty<PassiveItemData>();

    private readonly List<PassiveItemData> _itemPool = new();
    private readonly Dictionary<PassiveItemSlot, List<PassiveItemData>> _itemsBySlot = new();
    private readonly List<StatType> _summaryStats = new();
    private readonly StringBuilder _summaryBuilder = new(512);

    private WeaponTestingSandboxManager _sandbox;
    private PassiveItemManager _passiveItemManager;
    private PlayerStats _playerStats;
    private PlayerHealth _playerHealth;
    private PlayerMovement _playerMovement;
    private WeaponStatOverride _statOverride;
    private bool _passiveBaselineMode;

    public static IReadOnlyList<SlotDescriptor> Slots => ReadOnlySlotDefinitions;
    public IReadOnlyList<PassiveItemData> ItemPool => _itemPool;
    public bool IsBound => _passiveItemManager != null && _playerStats != null;
    public bool PassiveBaselineMode => _passiveBaselineMode;
    public float ScavengingDropChance => PlayerDropMath.GetScavengingDropChance(_playerStats);
    public float DoubleDropChance => PlayerDropMath.GetDoubleDropChance(_playerStats);

    public bool OverridesMaskPassives
    {
        get
        {
            if (_passiveBaselineMode || _statOverride == null || !_statOverride.UseOverrides || _passiveItemManager == null)
                return false;

            foreach (PassiveItemInstance instance in _passiveItemManager.Inventory.GetAllEquipped())
            {
                if (instance?.Data == null)
                    continue;

                IReadOnlyList<PassiveStatBonus> bonuses = instance.Data.BonusesPerLevel;
                for (int i = 0; i < bonuses.Count; i++)
                {
                    if (ExactOverrideStats.Contains(bonuses[i].StatType))
                        return true;
                }
            }

            return false;
        }
    }

    public string OverrideWarning
    {
        get
        {
            if (!OverridesMaskPassives)
                return string.Empty;

            _summaryStats.Clear();
            CollectMaskedStats(_summaryStats);
            _summaryBuilder.Clear();
            _summaryBuilder.Append("Manual weapon overrides mask: ");
            for (int i = 0; i < _summaryStats.Count; i++)
            {
                if (i > 0)
                    _summaryBuilder.Append(", ");
                _summaryBuilder.Append(StatDisplayNames.GetDisplayName(_summaryStats[i]));
            }

            _summaryBuilder.Append(". Use Passive Baseline to measure their effects.");
            return _summaryBuilder.ToString();
        }
    }

    public string EffectiveStatsSummary
    {
        get
        {
            if (_playerStats == null)
                return "Player stats unavailable.";

            _summaryStats.Clear();
            CollectEquippedStats(_summaryStats);
            if (_summaryStats.Count == 0)
                return "No passive stat bonuses equipped.";

            _summaryBuilder.Clear();
            for (int i = 0; i < _summaryStats.Count; i++)
            {
                if (i > 0)
                    _summaryBuilder.Append(" | ");

                StatType statType = _summaryStats[i];
                _summaryBuilder.Append(StatDisplayNames.GetDisplayName(statType))
                    .Append(": ")
                    .Append(_playerStats.GetStat(statType).ToString("0.###"));
            }

            return _summaryBuilder.ToString();
        }
    }

    public string HealthShieldSummary
    {
        get
        {
            if (_playerHealth == null)
                return "Health unavailable.";

            _summaryBuilder.Clear();
            _summaryBuilder.Append("Health: ")
                .Append(_playerHealth.CurrentHealth)
                .Append('/')
                .Append(_playerHealth.MaxHealth)
                .Append(" | Shield: ")
                .Append(_playerHealth.ShieldCharges)
                .Append('/')
                .Append(_playerHealth.MaxShieldCharges);

            if (_playerMovement != null)
            {
                _summaryBuilder.Append(" | Dash: ")
                    .Append(_playerMovement.CurrentDashCharges)
                    .Append('/')
                    .Append(_playerMovement.MaxDashCharges);
            }

            return _summaryBuilder.ToString();
        }
    }

    public string DropProbeSummary
    {
        get
        {
            const float dropRoll = 0.5f;
            const float doubleDropRoll = 0.5f;
            int result = RunDropProbe(dropRoll, doubleDropRoll);
            return $"Material drop: {ScavengingDropChance * 100f:0.#}% | Double on success: {DoubleDropChance * 100f:0.#}% | Probe rolls 0.50/0.50: {result}";
        }
    }

    public void Bind(
        WeaponTestingSandboxManager sandbox,
        PassiveItemManager passiveItemManager,
        PassiveItemLevelUpHandler levelUpHandler,
        PlayerStats playerStats,
        PlayerHealth playerHealth,
        PlayerMovement playerMovement,
        WeaponStatOverride statOverride)
    {
        _sandbox = sandbox;
        _passiveItemManager = passiveItemManager;
        _playerStats = playerStats;
        _playerHealth = playerHealth;
        _playerMovement = playerMovement;
        _statOverride = statOverride;
        _passiveBaselineMode = _statOverride == null || !_statOverride.UseOverrides;

        RebuildItemPool(levelUpHandler != null ? levelUpHandler.ItemPool : null);
    }

    public IReadOnlyList<PassiveItemData> GetCompatibleItems(PassiveItemSlot slot)
    {
        return _itemsBySlot.TryGetValue(slot, out List<PassiveItemData> items) ? items : EmptyItemList;
    }

    public PassiveItemInstance GetEquipped(PassiveItemSlot slot, int slotIndex)
    {
        if (_passiveItemManager == null || !IsValidSlot(slot, slotIndex))
            return null;

        return _passiveItemManager.Inventory.Get(slot, slotIndex);
    }

    public bool TrySetSlot(PassiveItemSlot slot, int slotIndex, PassiveItemData data, int level)
    {
        if (_passiveItemManager == null || !IsValidSlot(slot, slotIndex))
            return false;

        if (data == null)
            return TryClearSlot(slot, slotIndex);

        if (data.Slot != slot || !_itemPool.Contains(data))
            return false;

        int targetLevel = Mathf.Clamp(level, 1, data.MaxLevel);
        if (!_passiveItemManager.TrySetItem(slot, slotIndex, data, targetLevel))
            return false;

        RefreshAfterPassiveMutation();
        return true;
    }

    public bool TrySetLevel(PassiveItemSlot slot, int slotIndex, int level)
    {
        PassiveItemInstance instance = GetEquipped(slot, slotIndex);
        if (instance?.Data == null)
            return false;

        int targetLevel = Mathf.Clamp(level, 1, instance.Data.MaxLevel);
        bool changed = targetLevel != instance.Level;
        if (!_passiveItemManager.TrySetLevel(slot, slotIndex, targetLevel))
            return false;

        if (changed)
            RefreshAfterPassiveMutation();
        return true;
    }

    public bool TryClearSlot(PassiveItemSlot slot, int slotIndex)
    {
        if (_passiveItemManager == null || !IsValidSlot(slot, slotIndex))
            return false;

        if (!_passiveItemManager.TryUnequip(slot, slotIndex))
            return false;

        RefreshAfterPassiveMutation();
        return true;
    }

    public void ClearAll()
    {
        if (_passiveItemManager == null)
            return;

        if (_passiveItemManager.ClearAll())
            RefreshAfterPassiveMutation();
        else
            ResetScenario();
    }

    public void SetAllEquippedLevels(int level)
    {
        if (_passiveItemManager == null)
            return;

        bool changed = false;
        for (int i = 0; i < SlotDefinitions.Length; i++)
        {
            SlotDescriptor descriptor = SlotDefinitions[i];
            PassiveItemInstance instance = GetEquipped(descriptor.Slot, descriptor.SlotIndex);
            if (instance?.Data == null)
                continue;

            int targetLevel = Mathf.Clamp(level, 1, instance.Data.MaxLevel);
            if (targetLevel == instance.Level)
                continue;

            changed |= _passiveItemManager.TrySetLevel(descriptor.Slot, descriptor.SlotIndex, targetLevel);
        }

        if (changed)
            RefreshAfterPassiveMutation();
    }

    public void SetPassiveBaselineMode(bool enabled)
    {
        _passiveBaselineMode = enabled || _statOverride == null;
        if (_statOverride != null)
        {
            _statOverride.UseOverrides = !_passiveBaselineMode;
            _statOverride.ApplyOverrides();
        }

        ResetScenario();
    }

    public string BuildSlotSummary(PassiveItemSlot slot, int slotIndex)
    {
        string label = GetSlotLabel(slot, slotIndex);
        PassiveItemInstance instance = GetEquipped(slot, slotIndex);
        if (instance?.Data == null)
            return $"{label}: None";

        _summaryBuilder.Clear();
        _summaryBuilder.Append(label)
            .Append(": ")
            .Append(instance.Data.DisplayName)
            .Append(" Lv.")
            .Append(instance.Level)
            .Append(" | ");
        AppendBonusSummary(_summaryBuilder, instance.Data, instance.Level);
        return _summaryBuilder.ToString();
    }

    public void DamagePlayer(float amount)
    {
        if (_playerHealth == null || amount <= 0f)
            return;

        _playerHealth.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(amount)));
    }

    public int RunDropProbe(float dropRoll, float doubleDropRoll)
    {
        return PlayerDropMath.RollMaterialDropCount(
            _playerStats,
            Mathf.Clamp01(dropRoll),
            Mathf.Clamp01(doubleDropRoll));
    }

    public void HealPlayerFull()
    {
        _playerHealth?.FullHeal();
    }

    public void ConsumeShield()
    {
        if (_playerHealth == null || _playerHealth.ShieldCharges <= 0)
            return;

        _playerHealth.TakeDamage(1);
    }

    public void RechargeShield()
    {
        _playerHealth?.RefillShields();
    }

    public void ResetScenario()
    {
        _playerHealth?.FullHeal();
        RechargeShield();
        _playerMovement?.RefreshPassiveResources();
        _sandbox?.RefillAllAmmo();
        _sandbox?.ResetWeaponCooldowns();
        _sandbox?.Metrics?.ResetMetrics();
    }

    private void RefreshAfterPassiveMutation()
    {
        if (!_passiveBaselineMode && _statOverride != null)
        {
            _statOverride.UseOverrides = true;
            _statOverride.ApplyOverrides();
        }

        ResetScenario();
    }

    private void RebuildItemPool(IReadOnlyList<PassiveItemData> source)
    {
        _itemPool.Clear();
        _itemsBySlot.Clear();
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            PassiveItemData data = source[i];
            if (data == null || _itemPool.Contains(data))
                continue;

            _itemPool.Add(data);
            if (!_itemsBySlot.TryGetValue(data.Slot, out List<PassiveItemData> compatible))
            {
                compatible = new List<PassiveItemData>();
                _itemsBySlot.Add(data.Slot, compatible);
            }

            compatible.Add(data);
        }
    }

    private void CollectEquippedStats(List<StatType> destination)
    {
        if (_passiveItemManager == null)
            return;

        foreach (PassiveItemInstance instance in _passiveItemManager.Inventory.GetAllEquipped())
        {
            if (instance?.Data == null)
                continue;

            IReadOnlyList<PassiveStatBonus> bonuses = instance.Data.BonusesPerLevel;
            for (int i = 0; i < bonuses.Count; i++)
                AddUniqueSorted(destination, bonuses[i].StatType);
        }
    }

    private void CollectMaskedStats(List<StatType> destination)
    {
        if (_passiveItemManager == null)
            return;

        foreach (PassiveItemInstance instance in _passiveItemManager.Inventory.GetAllEquipped())
        {
            if (instance?.Data == null)
                continue;

            IReadOnlyList<PassiveStatBonus> bonuses = instance.Data.BonusesPerLevel;
            for (int i = 0; i < bonuses.Count; i++)
            {
                StatType statType = bonuses[i].StatType;
                if (ExactOverrideStats.Contains(statType))
                    AddUniqueSorted(destination, statType);
            }
        }
    }

    private static void AddUniqueSorted(List<StatType> destination, StatType statType)
    {
        if (destination.Contains(statType))
            return;

        destination.Add(statType);
        destination.Sort((left, right) => left.CompareTo(right));
    }

    private static void AppendBonusSummary(StringBuilder builder, PassiveItemData data, int level)
    {
        bool wroteBonus = false;
        IReadOnlyList<PassiveStatBonus> bonuses = data.BonusesPerLevel;
        for (int i = 0; i < bonuses.Count; i++)
        {
            PassiveStatBonus bonus = bonuses[i];
            float value = bonus.GetValueForLevel(level);
            if (Mathf.Approximately(value, 0f))
                continue;

            if (wroteBonus)
                builder.Append(", ");
            builder.Append(bonus.ModifierType == StatModifierType.Multiplicative ? 'x' : '+')
                .Append(value.ToString("0.###"))
                .Append(' ')
                .Append(StatDisplayNames.GetDisplayName(bonus.StatType));
            wroteBonus = true;
        }

        if (!wroteBonus)
            builder.Append("No configured bonus");
    }

    private static bool IsValidSlot(PassiveItemSlot slot, int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < PassiveItemInventory.GetCapacity(slot);
    }

    private static string GetSlotLabel(PassiveItemSlot slot, int slotIndex)
    {
        for (int i = 0; i < SlotDefinitions.Length; i++)
        {
            SlotDescriptor descriptor = SlotDefinitions[i];
            if (descriptor.Slot == slot && descriptor.SlotIndex == slotIndex)
                return descriptor.Label;
        }

        return $"{slot} {slotIndex + 1}";
    }
}
