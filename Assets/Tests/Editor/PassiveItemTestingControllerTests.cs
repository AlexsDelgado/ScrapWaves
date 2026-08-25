using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PassiveItemTestingControllerTests
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
    public void Catalog_ExposesProductionPoolGroupedIntoSixDeterministicSlots()
    {
        PassiveItemData head = CreateItem("Head Item", PassiveItemSlot.Head, StatType.DamageMultiplier, 0.1f);
        PassiveItemData arm = CreateItem("Arm Item", PassiveItemSlot.Arm, StatType.AttackSpeedMultiplier, 0.1f);
        TestContext context = CreateContext(head, arm);

        Assert.That(PassiveItemTestingController.Slots, Has.Count.EqualTo(6));
        Assert.That(PassiveItemTestingController.Slots[0].Label, Is.EqualTo("Head"));
        Assert.That(PassiveItemTestingController.Slots[5].Label, Is.EqualTo("Leg 2"));
        CollectionAssert.AreEqual(new[] { head }, context.Controller.GetCompatibleItems(PassiveItemSlot.Head));
        CollectionAssert.AreEqual(new[] { arm }, context.Controller.GetCompatibleItems(PassiveItemSlot.Arm));
        Assert.That(context.Controller.ItemPool, Has.Count.EqualTo(2));
    }

    [Test]
    public void SlotEditing_SupportsExactLevelDowngradeReplacementAndClear()
    {
        PassiveItemData damage = CreateItem("Damage Item", PassiveItemSlot.Head, StatType.DamageMultiplier, 0.1f);
        PassiveItemData replacement = CreateItem("Replacement", PassiveItemSlot.Head, StatType.DamageMultiplier, 0.2f);
        TestContext context = CreateContext(damage, replacement);
        context.Controller.SetPassiveBaselineMode(true);

        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Head, 0, damage, 3), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.3f).Within(0.0001f));
        Assert.That(context.Controller.GetEquipped(PassiveItemSlot.Head, 0).Level, Is.EqualTo(3));

        Assert.That(context.Controller.TrySetLevel(PassiveItemSlot.Head, 0, 6), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.6f).Within(0.0001f));
        Assert.That(context.Controller.TrySetLevel(PassiveItemSlot.Head, 0, 2), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.2f).Within(0.0001f));

        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Head, 0, replacement, 2), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(context.Controller.BuildSlotSummary(PassiveItemSlot.Head, 0), Does.Contain("Replacement Lv.2"));

        Assert.That(context.Controller.TryClearSlot(PassiveItemSlot.Head, 0), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(context.Controller.BuildSlotSummary(PassiveItemSlot.Head, 0), Is.EqualTo("Head: None"));
    }

    [Test]
    public void ArmSlots_RejectDuplicateItemsAndBulkLevelActionsRemainDeterministic()
    {
        PassiveItemData first = CreateItem("First Arm", PassiveItemSlot.Arm, StatType.AttackSpeedMultiplier, 0.05f);
        PassiveItemData second = CreateItem("Second Arm", PassiveItemSlot.Arm, StatType.AmmoMultiplier, 0.1f);
        TestContext context = CreateContext(first, second);
        context.Controller.SetPassiveBaselineMode(true);

        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Arm, 0, first, 1), Is.True);
        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Arm, 1, first, 1), Is.False);
        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Arm, 1, second, 1), Is.True);

        context.Controller.SetAllEquippedLevels(6);

        Assert.That(context.Controller.GetEquipped(PassiveItemSlot.Arm, 0).Level, Is.EqualTo(6));
        Assert.That(context.Controller.GetEquipped(PassiveItemSlot.Arm, 1).Level, Is.EqualTo(6));
        context.Controller.ClearAll();
        Assert.That(context.Controller.GetEquipped(PassiveItemSlot.Arm, 0), Is.Null);
        Assert.That(context.Controller.GetEquipped(PassiveItemSlot.Arm, 1), Is.Null);
    }

    [Test]
    public void BaselineMode_RemovesExactOverrideAndManualModeRestoresItWithWarning()
    {
        PassiveItemData damage = CreateItem("Damage Item", PassiveItemSlot.Head, StatType.DamageMultiplier, 0.1f);
        TestContext context = CreateContext(damage);
        context.Override.DamageMultiplier = 1f;
        context.Override.ApplyOverrides();

        context.Controller.SetPassiveBaselineMode(true);
        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Head, 0, damage, 4), Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(context.Controller.OverridesMaskPassives, Is.False);

        context.Controller.SetPassiveBaselineMode(false);
        Assert.That(context.Override.UseOverrides, Is.True);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(context.Controller.OverridesMaskPassives, Is.True);
        Assert.That(context.Controller.OverrideWarning, Does.Contain("Damage"));

        context.Controller.SetPassiveBaselineMode(true);
        Assert.That(context.Override.UseOverrides, Is.False);
        Assert.That(context.Stats.GetStat(StatType.DamageMultiplier), Is.EqualTo(1.4f).Within(0.0001f));
    }

    [Test]
    public void ScavengerProbe_UsesCanonicalDeterministicDropMath()
    {
        PassiveItemData scavenger = CreateItem("Scavenger", PassiveItemSlot.Head, StatType.Scavenging, 10f);
        TestContext context = CreateContext(scavenger);
        context.Controller.SetPassiveBaselineMode(true);
        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Head, 0, scavenger, 4), Is.True);

        Assert.That(context.Controller.ScavengingDropChance, Is.EqualTo(0.9f).Within(0.0001f));
        Assert.That(context.Controller.DoubleDropChance, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(context.Controller.RunDropProbe(0.6f, 0.05f), Is.EqualTo(2));
        Assert.That(context.Controller.RunDropProbe(0.95f, 0f), Is.Zero);
        Assert.That(context.Controller.DropProbeSummary, Does.Contain("90%"));
    }

    [Test]
    public void HealthAndShieldHelpers_ProvideARepeatableDefensiveScenario()
    {
        PassiveItemData shield = CreateItem("Shield", PassiveItemSlot.Core, StatType.ShieldCharges, 1f);
        TestContext context = CreateContext(shield);
        context.Controller.SetPassiveBaselineMode(true);
        Assert.That(context.Controller.TrySetSlot(PassiveItemSlot.Core, 0, shield, 2), Is.True);
        Assert.That(context.Health.ShieldCharges, Is.EqualTo(2));

        context.Controller.ConsumeShield();
        Assert.That(context.Health.ShieldCharges, Is.EqualTo(1));
        context.Controller.RechargeShield();
        Assert.That(context.Health.ShieldCharges, Is.EqualTo(2));

        Assert.That(context.Controller.TryClearSlot(PassiveItemSlot.Core, 0), Is.True);
        context.Controller.DamagePlayer(25f);
        Assert.That(context.Health.CurrentHealth, Is.EqualTo(75));
        context.Controller.HealPlayerFull();
        Assert.That(context.Health.CurrentHealth, Is.EqualTo(context.Health.MaxHealth));
        Assert.That(context.Controller.HealthShieldSummary, Does.Contain("Shield: 0/0"));
    }

    private TestContext CreateContext(params PassiveItemData[] items)
    {
        GameObject player = Track(new GameObject("Passive Test Player"));
        PlayerHealth health = player.AddComponent<PlayerHealth>();
        PlayerStats stats = player.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateStatDefinitions());
        InvokePrivate(stats, "Awake");
        health.FullHeal();

        PassiveItemManager manager = player.AddComponent<PassiveItemManager>();
        SetPrivateField(manager, "_playerStats", stats);
        PassiveItemLevelUpHandler handler = player.AddComponent<PassiveItemLevelUpHandler>();
        SetPrivateField(handler, "_itemPool", new List<PassiveItemData>(items));

        GameObject tester = Track(new GameObject("Passive Test Controller"));
        WeaponStatOverride statOverride = tester.AddComponent<WeaponStatOverride>();
        statOverride.Bind(stats);
        PassiveItemTestingController controller = tester.AddComponent<PassiveItemTestingController>();
        controller.Bind(null, manager, handler, stats, health, null, statOverride);

        return new TestContext(controller, stats, health, statOverride);
    }

    private List<StatDefinition> CreateStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f, false),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f, false),
            CreateDefinition(StatType.AmmoMultiplier, 1f, false),
            CreateDefinition(StatType.MaxHealth, 100f, true),
            CreateDefinition(StatType.DamageResistance, 0f, false),
            CreateDefinition(StatType.ShieldCharges, 0f, true),
            CreateDefinition(StatType.ShieldRechargeDelay, 5f, false),
            CreateDefinition(StatType.Scavenging, 50f, false),
            CreateDefinition(StatType.DoubleDrop, -30f, false)
        };
    }

    private PassiveItemData CreateItem(
        string displayName,
        PassiveItemSlot slot,
        StatType statType,
        float amountPerLevel)
    {
        PassiveItemData item = Track(ScriptableObject.CreateInstance<PassiveItemData>());
        item.name = displayName.Replace(' ', '_');
        SetPrivateField(item, "_displayName", displayName);
        SetPrivateField(item, "_slot", slot);
        SetPrivateField(item, "_maxLevel", 6);

        var values = new float[6];
        for (int i = 0; i < values.Length; i++)
            values[i] = amountPerLevel * (i + 1);
        SetPrivateField(item, "_bonusesPerLevel", new List<PassiveStatBonus>
        {
            new()
            {
                StatType = statType,
                ModifierType = StatModifierType.Additive,
                ValuesPerLevel = values
            }
        });
        return item;
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue, bool isInteger)
    {
        StatDefinition definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
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

    private T Track<T>(T unityObject) where T : Object
    {
        _cleanup.Add(unityObject);
        return unityObject;
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

    private readonly struct TestContext
    {
        public TestContext(
            PassiveItemTestingController controller,
            PlayerStats stats,
            PlayerHealth health,
            WeaponStatOverride statOverride)
        {
            Controller = controller;
            Stats = stats;
            Health = health;
            Override = statOverride;
        }

        public PassiveItemTestingController Controller { get; }
        public PlayerStats Stats { get; }
        public PlayerHealth Health { get; }
        public WeaponStatOverride Override { get; }
    }
}
