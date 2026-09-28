using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Fija las reglas de unidades que la vista dev usa para mostrar stats. El cuerpo se movió
/// desde DebugMonitorEditor, así que estos tests también protegen al inspector de debug.
/// </summary>
public sealed class StatDisplayFormatTests
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

    [TestCase(StatType.DamageMultiplier, 1.42f, "1.42x")]
    [TestCase(StatType.CriticalDamage, 2.5f, "2.5x")]
    [TestCase(StatType.CloseRangeDamageMultiplier, 1.8f, "1.8x")]
    public void FormatStat_MultiplierStats_AppendX(StatType type, float value, string expected)
    {
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(type), value), Is.EqualTo(expected));
    }

    [Test]
    public void FormatStat_FractionStats_ScaleByHundred()
    {
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.Lifesteal), 0.25f), Is.EqualTo("25%"));
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.DamageResistance), 0.4f), Is.EqualTo("40%"));
    }

    [Test]
    public void FormatStat_IsPercentageDefinition_ScalesByHundred()
    {
        StatDefinition definition = CreateDefinition(StatType.CriticalChance, isPercentage: true);

        Assert.That(StatDisplayFormat.FormatStat(definition, 0.25f), Is.EqualTo("25%"));
    }

    [Test]
    public void FormatStat_ScavengingAndDoubleDrop_AreAlreadyPercentPoints()
    {
        // Asimetría deliberada heredada del inspector: estos dos se autoran en 0-100.
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.Scavenging), 25f), Is.EqualTo("25%"));
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.DoubleDrop), 75f), Is.EqualTo("75%"));
    }

    [Test]
    public void FormatStat_UnitSuffixesMatchStatSemantics()
    {
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.MovementSpeed), 6.2f), Is.EqualTo("6.2 m/s"));
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.PickupRange), 3f), Is.EqualTo("3 m"));
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.BaseFireInterval), 0.25f), Is.EqualTo("0.25 s"));
        Assert.That(StatDisplayFormat.FormatStat(CreateDefinition(StatType.HealthRegeneration), 1.5f), Is.EqualTo("1.5 HP/s"));
    }

    [Test]
    public void FormatStat_IntegerStats_HaveNoDecimals()
    {
        StatDefinition definition = CreateDefinition(StatType.DashCharges, isInteger: true);

        Assert.That(StatDisplayFormat.FormatStat(definition, 3f), Is.EqualTo("3"));
    }

    [Test]
    public void FormatSignedAndMultiplier_ReadAsBreakdownRows()
    {
        Assert.That(StatDisplayFormat.FormatSigned(1.043f), Is.EqualTo("+1.043"));
        Assert.That(StatDisplayFormat.FormatSigned(-0.2f), Is.EqualTo("-0.200"));
        Assert.That(StatDisplayFormat.FormatMultiplier(1.05f), Is.EqualTo("x1.050"));
    }

    [Test]
    public void DescribeModifier_PrefersLabelThenUnityObjectNameThenSourceEnum()
    {
        StatModifier labelled = new(StatType.MaxHealth, 1f, StatUpgradeSource.LevelUp, null, StatModifierType.Additive, "Lv3 roll");
        Assert.That(StatDisplayFormat.DescribeModifier(labelled), Is.EqualTo("Lv3 roll"));

        StatDefinition namedSource = CreateDefinition(StatType.MaxHealth);
        namedSource.name = "SomeSourceAsset";
        StatModifier fromObject = new(StatType.MaxHealth, 1f, StatUpgradeSource.PassiveItem, namedSource);
        Assert.That(StatDisplayFormat.DescribeModifier(fromObject), Is.EqualTo("SomeSourceAsset"));

        StatModifier bare = new(StatType.MaxHealth, 1f, StatUpgradeSource.TemporaryPowerup);
        Assert.That(StatDisplayFormat.DescribeModifier(bare), Is.EqualTo("TemporaryPowerup"));
    }

    [Test]
    public void EffectiveMaximum_MatchesTheClampsConsumersApply()
    {
        // Espeja WeaponDamageResolver.RollCrit y PlayerStatMath.GetFractionStat.
        Assert.That(StatDisplayFormat.GetEffectiveMaximum(StatType.CriticalChance), Is.EqualTo(1f));
        Assert.That(StatDisplayFormat.GetEffectiveMaximum(StatType.Lifesteal), Is.EqualTo(1f));
        Assert.That(StatDisplayFormat.GetEffectiveMaximum(StatType.DamageResistance), Is.EqualTo(0.95f));
        Assert.That(StatDisplayFormat.GetEffectiveMaximum(StatType.ExtraEliteChance), Is.EqualTo(0.95f));
        Assert.That(StatDisplayFormat.GetEffectiveMaximum(StatType.DamageMultiplier), Is.LessThan(0f));
    }

    [Test]
    public void IsSaturated_FlagsRawValuesAboveTheConsumerClamp()
    {
        Assert.That(StatDisplayFormat.IsSaturated(StatType.CriticalChance, 1.04f), Is.True);
        Assert.That(StatDisplayFormat.IsSaturated(StatType.CriticalChance, 0.25f), Is.False);
        Assert.That(StatDisplayFormat.IsSaturated(StatType.DamageMultiplier, 12f), Is.False);
    }

    private StatDefinition CreateDefinition(StatType type, bool isPercentage = false, bool isInteger = false)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
        SetPrivateField(definition, "<BaseValue>k__BackingField", 0f);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", isPercentage);
        SetPrivateField(definition, "<IsInteger>k__BackingField", isInteger);
        return definition;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
