using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PassiveItemManagerTests
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
    public void ExactLevelUpDownAndRemoval_DoNotStackOrRemoveUnrelatedModifiers()
    {
        GameObject player = CreatePlayer(CreateDefinition(StatType.MovementSpeed, 10f));
        PlayerStats stats = player.GetComponent<PlayerStats>();
        PassiveItemManager manager = player.GetComponent<PassiveItemManager>();
        object unrelatedSource = new();
        stats.AddModifier(new StatModifier(
            StatType.MovementSpeed, 4f, StatUpgradeSource.PassiveItem, unrelatedSource));
        PassiveItemData item = CreateItem("Bionic Boots", PassiveItemSlot.Leg, 3,
            Bonus(StatType.MovementSpeed, 2f, 5f, 8f));
        int eventCount = 0;
        manager.OnInventoryChanged += () => eventCount++;

        Assert.That(manager.TrySetItem(PassiveItemSlot.Leg, 1, item, 1), Is.True);
        Assert.That(stats.GetStat(StatType.MovementSpeed), Is.EqualTo(16f).Within(0.0001f));
        Assert.That(manager.TrySetLevel(PassiveItemSlot.Leg, 1, 3), Is.True);
        Assert.That(stats.GetStat(StatType.MovementSpeed), Is.EqualTo(22f).Within(0.0001f));
        Assert.That(manager.TrySetLevel(PassiveItemSlot.Leg, 1, 2), Is.True);
        Assert.That(stats.GetStat(StatType.MovementSpeed), Is.EqualTo(19f).Within(0.0001f));
        Assert.That(manager.TryUnequip(PassiveItemSlot.Leg, 1), Is.True);

        Assert.That(stats.GetStat(StatType.MovementSpeed), Is.EqualTo(14f).Within(0.0001f));
        Assert.That(eventCount, Is.EqualTo(4));
        Assert.That(manager.Inventory.Get(PassiveItemSlot.Leg, 1), Is.Null);
    }

    [Test]
    public void ReplacementAndClear_EmitOneEventPerLogicalMutation_AndRejectDuplicates()
    {
        GameObject player = CreatePlayer(
            CreateDefinition(StatType.DamageFlat, 0f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f));
        PassiveItemManager manager = player.GetComponent<PassiveItemManager>();
        PassiveItemData damage = CreateItem("Honed Weaponry", PassiveItemSlot.Arm, 6,
            Bonus(StatType.DamageFlat, 2f));
        PassiveItemData speed = CreateItem("Belt Driven", PassiveItemSlot.Arm, 6,
            Bonus(StatType.AttackSpeedMultiplier, 0.2f));
        int eventCount = 0;
        manager.OnInventoryChanged += () => eventCount++;

        Assert.That(manager.TrySetItem(PassiveItemSlot.Arm, 0, damage, 1), Is.True);
        Assert.That(manager.TrySetItem(PassiveItemSlot.Arm, 1, damage, 1), Is.False);
        Assert.That(eventCount, Is.EqualTo(1));

        Assert.That(manager.TrySetItem(PassiveItemSlot.Arm, 0, speed, 1), Is.True);
        Assert.That(eventCount, Is.EqualTo(2), "Replacement must not expose an intermediate removal event.");
        Assert.That(manager.Inventory.Get(PassiveItemSlot.Arm, 0).Data, Is.SameAs(speed));

        Assert.That(manager.TrySetItem(PassiveItemSlot.Arm, 1, damage, 1), Is.True);
        int beforeClear = eventCount;
        Assert.That(manager.ClearAll(), Is.True);
        Assert.That(eventCount, Is.EqualTo(beforeClear + 1));
        Assert.That(manager.Inventory.CountEquipped(PassiveItemSlot.Arm), Is.Zero);
        Assert.That(manager.ClearAll(), Is.False);
        Assert.That(eventCount, Is.EqualTo(beforeClear + 1));
    }

    [Test]
    public void MaxHealthAndShield_AreReversibleWhenLevelDropsAndItemIsRemoved()
    {
        GameObject player = CreatePlayer(
            CreateDefinition(StatType.MaxHealth, 100f, true),
            CreateDefinition(StatType.ShieldCharges, 0f, true),
            CreateDefinition(StatType.ShieldRechargeDelay, 0f));
        PassiveItemManager manager = player.GetComponent<PassiveItemManager>();
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        PassiveItemData core = CreateItem("Plated Shield Core", PassiveItemSlot.Core, 2,
            Bonus(StatType.MaxHealth, 20f, 50f),
            Bonus(StatType.ShieldCharges, 1f, 3f),
            Bonus(StatType.ShieldRechargeDelay, 10f, 4f));

        Assert.That(manager.TrySetItem(PassiveItemSlot.Core, 0, core, 2), Is.True);
        Assert.That(health.MaxHealth, Is.EqualTo(150));
        Assert.That(health.CurrentHealth, Is.EqualTo(150));
        Assert.That(health.MaxShieldCharges, Is.EqualTo(3));
        Assert.That(health.ShieldCharges, Is.EqualTo(3));

        SetPrivateField(health, "_shieldCharges", 0);
        health.RefillShields();
        Assert.That(health.ShieldCharges, Is.EqualTo(3));

        SetPrivateField(health, "_currentHealth", 140);
        Assert.That(manager.TrySetLevel(PassiveItemSlot.Core, 0, 1), Is.True);
        Assert.That(health.MaxHealth, Is.EqualTo(120));
        Assert.That(health.CurrentHealth, Is.EqualTo(120));
        Assert.That(health.MaxShieldCharges, Is.EqualTo(1));
        Assert.That(health.ShieldCharges, Is.EqualTo(1));

        Assert.That(manager.TryUnequip(PassiveItemSlot.Core, 0), Is.True);
        Assert.That(health.MaxHealth, Is.EqualTo(100));
        Assert.That(health.CurrentHealth, Is.EqualTo(100));
        Assert.That(health.MaxShieldCharges, Is.Zero);
        Assert.That(health.ShieldCharges, Is.Zero);
    }

    [Test]
    public void PassiveMovementChanges_RefillAirJumpAndDashResources()
    {
        GameObject player = CreatePlayer(
            CreateDefinition(StatType.AirJumps, 0f, true),
            CreateDefinition(StatType.DashCharges, 0f, true));
        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        PassiveItemManager manager = player.GetComponent<PassiveItemManager>();
        PassiveItemData legs = CreateItem("Explosive Boosters", PassiveItemSlot.Leg, 1,
            Bonus(StatType.AirJumps, 2f),
            Bonus(StatType.DashCharges, 3f));

        Assert.That(manager.TrySetItem(PassiveItemSlot.Leg, 0, legs, 1), Is.True);
        Assert.That(movement.RemainingAirJumps, Is.EqualTo(2));
        Assert.That(movement.CurrentDashCharges, Is.EqualTo(3));

        Assert.That(manager.TryUnequip(PassiveItemSlot.Leg, 0), Is.True);
        Assert.That(movement.RemainingAirJumps, Is.Zero);
        Assert.That(movement.CurrentDashCharges, Is.Zero);
    }

    [Test]
    public void InvalidExactLevel_DoesNotMutateOrNotify()
    {
        GameObject player = CreatePlayer(CreateDefinition(StatType.DamageFlat, 0f));
        PassiveItemManager manager = player.GetComponent<PassiveItemManager>();
        PassiveItemData item = CreateItem("Damage", PassiveItemSlot.Arm, 2,
            Bonus(StatType.DamageFlat, 2f, 4f));
        Assert.That(manager.TrySetItem(PassiveItemSlot.Arm, 0, item, 1), Is.True);
        int eventCount = 0;
        manager.OnInventoryChanged += () => eventCount++;

        Assert.That(manager.TrySetLevel(PassiveItemSlot.Arm, 0, 0), Is.False);
        Assert.That(manager.TrySetLevel(PassiveItemSlot.Arm, 0, 3), Is.False);
        Assert.That(manager.Inventory.Get(PassiveItemSlot.Arm, 0).Level, Is.EqualTo(1));
        Assert.That(eventCount, Is.Zero);
    }

    private GameObject CreatePlayer(params StatDefinition[] definitions)
    {
        GameObject player = new("Passive Manager Test Player");
        _cleanup.Add(player);
        PlayerHealth health = player.AddComponent<PlayerHealth>();
        InvokePrivate(health, "Awake");
        health.FullHeal();
        PlayerStats stats = player.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", new List<StatDefinition>(definitions));
        InvokePrivate(stats, "Awake");
        PassiveItemManager manager = player.AddComponent<PassiveItemManager>();
        InvokePrivate(manager, "Awake");
        return player;
    }

    private PassiveItemData CreateItem(string displayName, PassiveItemSlot slot, int maxLevel,
        params PassiveStatBonus[] bonuses)
    {
        PassiveItemData data = ScriptableObject.CreateInstance<PassiveItemData>();
        _cleanup.Add(data);
        data.name = displayName;
        SetPrivateField(data, "_displayName", displayName);
        SetPrivateField(data, "_slot", slot);
        SetPrivateField(data, "_maxLevel", maxLevel);
        SetPrivateField(data, "_bonusesPerLevel", new List<PassiveStatBonus>(bonuses));
        return data;
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue, bool isInteger = false)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Miscellaneous);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", true);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", isInteger);
        return definition;
    }

    private static PassiveStatBonus Bonus(StatType statType, params float[] values)
    {
        return new PassiveStatBonus
        {
            StatType = statType,
            ModifierType = StatModifierType.Additive,
            ValuesPerLevel = values
        };
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }
}
