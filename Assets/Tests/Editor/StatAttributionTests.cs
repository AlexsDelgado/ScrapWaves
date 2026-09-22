using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Cubre los accessors de atribución que consume la vista dev de stats: enumeración de
/// modificadores sin garbage, totales que reproducen CurrentValue, y etiquetas de origen.
/// </summary>
public sealed class StatAttributionTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }

        _cleanup.Clear();
    }

    [Test]
    public void RuntimeStat_Modifiers_ExposesModifiersInInsertionOrder()
    {
        RuntimeStat stat = new(CreateDefinition(StatType.DamageMultiplier, 1f));
        StatModifier first = new(StatType.DamageMultiplier, 0.2f, StatUpgradeSource.LevelUp);
        StatModifier second = new(StatType.DamageMultiplier, 1.5f, StatUpgradeSource.PassiveItem, null, StatModifierType.Multiplicative);

        stat.AddModifier(first);
        stat.AddModifier(second);

        Assert.That(stat.ModifierCount, Is.EqualTo(2));
        Assert.That(stat.Modifiers[0], Is.SameAs(first));
        Assert.That(stat.Modifiers[1], Is.SameAs(second));
    }

    [Test]
    public void PlayerStats_GetModifiers_ReturnsTheSameListInstanceAcrossCalls()
    {
        // Candado anti-garbage: la vista consulta los 30 stats por refresco. Si alguien
        // "mejora" el accessor con AsReadOnly() o ToArray(), este test falla.
        PlayerStats stats = CreateStats(CreateDefinition(StatType.CriticalChance, 0f));
        stats.AddModifier(new StatModifier(StatType.CriticalChance, 0.1f, StatUpgradeSource.LevelUp));

        IReadOnlyList<StatModifier> first = stats.GetModifiers(StatType.CriticalChance);
        IReadOnlyList<StatModifier> second = stats.GetModifiers(StatType.CriticalChance);

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void PlayerStats_GetModifiers_MissingStat_ReturnsEmptyWithoutLogging()
    {
        PlayerStats stats = CreateStats(CreateDefinition(StatType.MovementSpeed, 5f));

        IReadOnlyList<StatModifier> modifiers = stats.GetModifiers(StatType.CriticalDamage);

        Assert.That(modifiers, Is.Not.Null);
        Assert.That(modifiers.Count, Is.EqualTo(0));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void PlayerStats_GetBaseValue_ReturnsDefinitionBaseBeforeModifiers()
    {
        PlayerStats stats = CreateStats(CreateDefinition(StatType.CriticalDamage, 2.5f));
        stats.AddModifier(new StatModifier(StatType.CriticalDamage, 1f, StatUpgradeSource.LevelUp));

        Assert.That(stats.GetBaseValue(StatType.CriticalDamage), Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(stats.GetStat(StatType.CriticalDamage), Is.EqualTo(3.5f).Within(0.0001f));
    }

    [Test]
    public void RuntimeStat_Totals_ReproduceCurrentValue()
    {
        // Garantiza que la vista nunca derive de la fórmula real de CurrentValue.
        RuntimeStat stat = new(CreateDefinition(StatType.DamageMultiplier, 1f));
        stat.AddModifier(new StatModifier(StatType.DamageMultiplier, 0.4f, StatUpgradeSource.LevelUp));
        stat.AddModifier(new StatModifier(StatType.DamageMultiplier, 0.1f, StatUpgradeSource.PassiveItem));
        stat.AddModifier(new StatModifier(StatType.DamageMultiplier, 1.5f, StatUpgradeSource.Base, null, StatModifierType.Multiplicative));
        stat.AddModifier(new StatModifier(StatType.DamageMultiplier, 1.2f, StatUpgradeSource.TemporaryPowerup, null, StatModifierType.Multiplicative));

        float recomposed = (stat.BaseValue + stat.AdditiveTotal) * stat.MultiplicativeTotal;

        Assert.That(stat.AdditiveTotal, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(stat.MultiplicativeTotal, Is.EqualTo(1.8f).Within(0.0001f));
        Assert.That(recomposed, Is.EqualTo(stat.CurrentValue).Within(0.0001f));
    }

    [Test]
    public void RuntimeStat_MultiplicativeTotal_IsNeutralWithoutMultiplicativeModifiers()
    {
        RuntimeStat stat = new(CreateDefinition(StatType.MovementSpeed, 5f));
        stat.AddModifier(new StatModifier(StatType.MovementSpeed, 2f, StatUpgradeSource.LevelUp));

        Assert.That(stat.MultiplicativeTotal, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(stat.CurrentValue, Is.EqualTo(7f).Within(0.0001f));
    }

    [Test]
    public void StatModifier_ConstructedWithoutLabel_LeavesLabelNull()
    {
        // Compatibilidad hacia atrás: los call sites que no etiquetan siguen compilando.
        StatModifier modifier = new(StatType.MaxHealth, 10f, StatUpgradeSource.LevelUp);

        Assert.That(modifier.Label, Is.Null);
    }

    [Test]
    public void LevelUpModifiers_CarryLevelAndAuthoredBaseAmountInLabel()
    {
        // El label es lo que responde "de qué subida de nivel salió este punto de stat",
        // que es justo lo que no se podía ver desde el juego.
        StatDefinition definition = CreateDefinition(StatType.CriticalChance, 0f,
            upgradeableByLevel: true, levelUpgradeBaseAmount: 0.01f);
        GameObject player = new("LevelUpLabelPlayer");
        _cleanup.Add(player);
        PlayerStats stats = AttachStats(player, definition);
        PlayerStatsLevelUpHandler handler = player.AddComponent<PlayerStatsLevelUpHandler>();
        InvokePrivate(handler, "Awake");

        handler.ApplyLevelUpStats(7);

        IReadOnlyList<StatModifier> modifiers = stats.GetModifiers(StatType.CriticalChance);
        Assert.That(modifiers.Count, Is.GreaterThan(0));
        for (int i = 0; i < modifiers.Count; i++)
        {
            Assert.That(modifiers[i].Source, Is.EqualTo(StatUpgradeSource.LevelUp));
            Assert.That(modifiers[i].Label, Is.Not.Null.And.Contains("Lv7"));
            Assert.That(modifiers[i].Label, Does.Contain("0.01"));
        }
    }

    [Test]
    public void PassiveItemModifiers_CarryItemNameAndLevelInLabel()
    {
        GameObject player = new("PassiveLabelPlayer");
        _cleanup.Add(player);
        PlayerHealth health = player.AddComponent<PlayerHealth>();
        InvokePrivate(health, "Awake");
        health.FullHeal();
        PlayerStats stats = AttachStats(player, CreateDefinition(StatType.CloseRangeDamageMultiplier, 1f));
        PassiveItemManager manager = player.AddComponent<PassiveItemManager>();
        InvokePrivate(manager, "Awake");

        PassiveItemData item = CreateItem("CQB module", PassiveItemSlot.Head, 6,
            StatType.CloseRangeDamageMultiplier, 1.2f, 1.3f);
        // El slot Head tiene capacidad 1, así que el único índice válido es 0.
        Assert.That(manager.TrySetItem(PassiveItemSlot.Head, 0, item, 2), Is.True);

        IReadOnlyList<StatModifier> modifiers = stats.GetModifiers(StatType.CloseRangeDamageMultiplier);
        Assert.That(modifiers.Count, Is.EqualTo(1));
        Assert.That(modifiers[0].Label, Is.EqualTo("CQB module Lv2"));
        Assert.That(StatDisplayFormat.DescribeModifier(modifiers[0]), Is.EqualTo("CQB module Lv2"));
    }

    private PlayerStats CreateStats(params StatDefinition[] definitions)
    {
        GameObject owner = new("StatAttributionTestOwner");
        _cleanup.Add(owner);
        return AttachStats(owner, definitions);
    }

    private PlayerStats AttachStats(GameObject owner, params StatDefinition[] definitions)
    {
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", new List<StatDefinition>(definitions));
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private PassiveItemData CreateItem(string displayName, PassiveItemSlot slot, int maxLevel,
        StatType statType, params float[] valuesPerLevel)
    {
        PassiveItemData data = ScriptableObject.CreateInstance<PassiveItemData>();
        _cleanup.Add(data);
        data.name = displayName;
        SetPrivateField(data, "_displayName", displayName);
        SetPrivateField(data, "_slot", slot);
        SetPrivateField(data, "_maxLevel", maxLevel);
        SetPrivateField(data, "_bonusesPerLevel", new List<PassiveStatBonus>
        {
            new()
            {
                StatType = statType,
                ModifierType = StatModifierType.Multiplicative,
                ValuesPerLevel = valuesPerLevel
            }
        });
        return data;
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue,
        bool upgradeableByLevel = false, float levelUpgradeBaseAmount = 0f)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", upgradeableByLevel);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", true);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", levelUpgradeBaseAmount);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", false);
        return definition;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, arguments);
    }
}
