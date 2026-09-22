using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class PlayerStats : MonoBehaviour
{
    [SerializeField] private List<StatDefinition> _statDefinitions = new();

    private readonly Dictionary<StatType, RuntimeStat> _stats = new();

    private static readonly StatModifier[] EmptyModifiers = Array.Empty<StatModifier>();

    public event Action<StatType, float> OnStatChanged;

    // Initializes stat dictionary and synchronizes health-derived base values on startup.
    private void Awake()
    {
        InitializeStats();
        SyncBaseMaxHealthFromHealthComponent();
    }

    // Copies PlayerHealth max value into MaxHealth stat base override.
    private void SyncBaseMaxHealthFromHealthComponent()
    {
        if (!TryGetComponent(out PlayerHealth health)) return;
        if (_stats.TryGetValue(StatType.MaxHealth, out RuntimeStat maxHealthStat))
        {
            maxHealthStat.SetBaseValue(health.MaxHealth);
            OnStatChanged?.Invoke(StatType.MaxHealth, maxHealthStat.CurrentValue);
        }
    }

    // Builds runtime stat map from assigned stat definition assets.
    private void InitializeStats()
    {
        _stats.Clear();

        foreach (StatDefinition definition in _statDefinitions)
        {
            if (definition == null) continue;
            if (_stats.ContainsKey(definition.StatType))
            {
                Debug.LogWarning($"PlayerStats: definición duplicada para {definition.StatType}.", this);
                continue;
            }

            _stats.Add(definition.StatType, new RuntimeStat(definition));
        }
    }

    // Returns current stat value or zero with warning if missing.
    public float GetStat(StatType statType)
    {
        if (_stats.TryGetValue(statType, out RuntimeStat stat))
            return stat.CurrentValue;

        Debug.LogWarning($"PlayerStats: falta el stat {statType} en _statDefinitions.", this);
        return 0f;
    }

    // Returns floored integer version of a stat query.
    public int GetStatInt(StatType statType) => Mathf.FloorToInt(GetStat(statType));

    // Returns definition asset for a stat, if configured.
    public StatDefinition GetDefinition(StatType statType)
    {
        return _stats.TryGetValue(statType, out RuntimeStat stat) ? stat.Definition : null;
    }

    // Exposes all configured stat definitions for consumers like level-up logic.
    public IReadOnlyList<StatDefinition> GetAllDefinitions() => _statDefinitions;

    /// <summary>
    /// Modificadores aplicados a un stat, para diagnóstico y atribución.
    /// A diferencia de GetStat no avisa cuando falta el stat: los consumidores recorren
    /// los 30 StatType y llenarían la consola de warnings.
    /// </summary>
    public IReadOnlyList<StatModifier> GetModifiers(StatType statType)
        => _stats.TryGetValue(statType, out RuntimeStat stat) ? stat.Modifiers : EmptyModifiers;

    // Returns the base value of a stat before any modifier, or zero if missing.
    public float GetBaseValue(StatType statType)
        => _stats.TryGetValue(statType, out RuntimeStat stat) ? stat.BaseValue : 0f;

    // Exposes runtime stat state for diagnostic consumers; null-safe via the bool result.
    public bool TryGetRuntimeStat(StatType statType, out RuntimeStat stat)
        => _stats.TryGetValue(statType, out stat);

    // Applies a modifier to one stat and notifies listeners.
    public void AddModifier(StatModifier modifier)
    {
        if (!_stats.TryGetValue(modifier.StatType, out RuntimeStat stat))
        {
            Debug.LogWarning($"PlayerStats: no se puede aplicar modificador, falta {modifier.StatType}.", this);
            return;
        }

        stat.AddModifier(modifier);
        OnStatChanged?.Invoke(modifier.StatType, stat.CurrentValue);
    }

    // Removes all modifiers tied to one specific source object.
    public void RemoveModifiersFromSource(object sourceReference)
    {
        foreach (RuntimeStat stat in _stats.Values)
            stat.RemoveModifiersFromSource(sourceReference);

        RecalculateAllStats();
    }

    // Removes all modifiers that share the same source category.
    public void ClearModifiersFromSourceType(StatUpgradeSource source)
    {
        foreach (RuntimeStat stat in _stats.Values)
            stat.ClearModifiersFromSourceType(source);

        RecalculateAllStats();
    }

    // Broadcasts updated values for every stat after bulk changes.
    private void RecalculateAllStats()
    {
        foreach (KeyValuePair<StatType, RuntimeStat> pair in _stats)
            OnStatChanged?.Invoke(pair.Key, pair.Value.CurrentValue);
    }

    // Compatibilidad temporal con sistemas existentes.
    // Compatibility accessor returning current flat damage as integer.
    public int GetDamage() => Mathf.Max(1, Mathf.RoundToInt(GetStat(StatType.DamageFlat)));
    // Compatibility accessor deriving fire interval from base interval and attack speed.
    public float GetFireInterval()
    {
        float rateMult = Mathf.Max(0.01f, GetStat(StatType.AttackSpeedMultiplier));
        float baseInterval = Mathf.Max(0.05f, GetStat(StatType.BaseFireInterval));
        return baseInterval / rateMult;
    }

    // Compatibility accessor returning movement speed with safety clamp.
    public float GetMoveSpeed() => Mathf.Max(0.1f, GetStat(StatType.MovementSpeed));
    // Compatibility accessor returning total max health as integer.
    public int GetMaxHealthTotal() => Mathf.Max(1, Mathf.RoundToInt(GetStat(StatType.MaxHealth)));

    // Applies temporary attack speed modifier used by runtime effects.
    public void SetRuntimeFireRateMultiplier(float multiplier)
    {
        float delta = Mathf.Max(0.01f, multiplier) - 1f;
        ClearModifiersFromSourceType(StatUpgradeSource.TemporaryEffect);
        AddModifier(new StatModifier(StatType.AttackSpeedMultiplier, delta, StatUpgradeSource.TemporaryEffect, this, label: "Runtime fire rate"));
    }

    // Applies upgrade asset directly using native stat type configuration.
    public void ApplyUpgrade(Upgrade upgrade)
    {
        if (upgrade == null) return;

        AddModifier(new StatModifier(upgrade.TargetStat, upgrade.Value, StatUpgradeSource.LevelUp, upgrade, label: upgrade.name));

        if (upgrade.TargetStat == StatType.MaxHealth && TryGetComponent(out PlayerHealth health))
            health.ApplyMaxHealthIncrease(Mathf.RoundToInt(upgrade.Value));
    }
}