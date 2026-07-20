using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatType
{
    MovementSpeed, JumpHeight, AirJumps, DashCharges, DashSpeed,
    DamageMultiplier, DamageFlat, EliteDamageMultiplier, AttackSpeedMultiplier, ProjectileAreaSize, CriticalChance, CriticalDamage, Knockback, AmmoMultiplier,
    MaxHealth, HealthRegeneration, Lifesteal, DamageResistance,
    PickupRange, ExtraEliteChance, Scavenging, DoubleDrop,
    BaseFireInterval,
    AbilityDamageMultiplier,
    AbilityCooldownReduction
}

public enum StatCategory { Mobility, Offensive, Defensive, Miscellaneous }
public enum StatUpgradeSource { Base, LevelUp, PassiveItem, Weapon, TemporaryEffect }

[CreateAssetMenu(menuName = "ScrapWaves/Stats/Stat Definition")]
public class StatDefinition : ScriptableObject
{
    [field: SerializeField] public StatType StatType { get; private set; }
    [field: SerializeField] public StatCategory Category { get; private set; }
    [field: SerializeField] public float BaseValue { get; private set; }
    [field: SerializeField] public bool UpgradeableByLevel { get; private set; }
    [field: SerializeField] public bool UpgradeableByItems { get; private set; }
    [field: SerializeField] public float LevelUpgradeBaseAmount { get; private set; }
    [field: SerializeField] public bool IsPercentage { get; private set; }
    [field: SerializeField] public bool IsInteger { get; private set; }
}

[Serializable]
public class StatModifier
{
    public StatType StatType;
    public float Value;
    public StatUpgradeSource Source;
    public object SourceReference;

    // Builds a modifier record with value, source type, and optional source reference.
    public StatModifier(StatType statType, float value, StatUpgradeSource source, object sourceReference = null)
    {
        StatType = statType;
        Value = value;
        Source = source;
        SourceReference = sourceReference;
    }
}

[Serializable]
public class RuntimeStat
{
    [SerializeField] private StatDefinition _definition;
    [SerializeField] private float _baseOverride;
    [SerializeField] private bool _useBaseOverride;

    private readonly List<StatModifier> _modifiers = new();

    public StatDefinition Definition => _definition;
    public float BaseValue => _useBaseOverride ? _baseOverride : _definition.BaseValue;

    public float CurrentValue
    {
        get
        {
            float value = BaseValue;
            foreach (StatModifier modifier in _modifiers) value += modifier.Value;
            if (_definition.IsInteger) value = Mathf.Floor(value);
            return value;
        }
    }

    // Creates runtime stat state from a configured stat definition asset.
    public RuntimeStat(StatDefinition definition) => _definition = definition;
    // Overrides base stat value, useful for syncing external authoritative values.
    public void SetBaseValue(float value) { _baseOverride = value; _useBaseOverride = true; }
    // Adds one modifier affecting current stat value calculations.
    public void AddModifier(StatModifier modifier) => _modifiers.Add(modifier);
    // Removes modifiers associated with one specific source object instance.
    public void RemoveModifiersFromSource(object sourceReference) => _modifiers.RemoveAll(m => m.SourceReference == sourceReference);
    // Removes all modifiers belonging to a source category.
    public void ClearModifiersFromSourceType(StatUpgradeSource source) => _modifiers.RemoveAll(m => m.Source == source);
}
