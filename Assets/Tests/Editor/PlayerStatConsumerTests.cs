using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerStatConsumerTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _cleanup.Count; i++)
            Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void TakeDamage_ReducesIncomingDamageByDamageResistance()
    {
        GameObject player = CreatePlayerWithStats(
            CreateDefinition(StatType.MaxHealth, 100f, StatCategory.Defensive),
            CreateDefinition(StatType.DamageResistance, 0f, StatCategory.Defensive));
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        PlayerStats stats = player.GetComponent<PlayerStats>();
        stats.AddModifier(new StatModifier(StatType.DamageResistance, 0.25f, StatUpgradeSource.LevelUp));

        health.TakeDamage(40);

        Assert.That(health.CurrentHealth, Is.EqualTo(70));
    }

    [Test]
    public void ApplyRegeneration_HealsAfterDamageDelayUsingHealthRegeneration()
    {
        GameObject player = CreatePlayerWithStats(
            CreateDefinition(StatType.MaxHealth, 100f, StatCategory.Defensive),
            CreateDefinition(StatType.HealthRegeneration, 10f, StatCategory.Defensive),
            CreateDefinition(StatType.DamageResistance, 0f, StatCategory.Defensive));
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        health.TakeDamage(50);

        InvokePrivate(health, "ApplyRegeneration", 2f, Time.time + 6f);

        Assert.That(health.CurrentHealth, Is.EqualTo(70));
    }

    [Test]
    public void WeaponDamageApplier_HealsPlayerFromLifestealAfterSuccessfulDamage()
    {
        GameObject player = CreatePlayerWithStats(
            CreateDefinition(StatType.MaxHealth, 100f, StatCategory.Defensive),
            CreateDefinition(StatType.Lifesteal, 0f, StatCategory.Defensive),
            CreateDefinition(StatType.DamageResistance, 0f, StatCategory.Defensive));
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        PlayerStats stats = player.GetComponent<PlayerStats>();
        stats.AddModifier(new StatModifier(StatType.Lifesteal, 0.25f, StatUpgradeSource.PassiveItem));
        playerHealth.TakeDamage(50);

        EnemyHealth enemy = CreateEnemyHealth(100);
        bool applied = WeaponDamageApplier.TryApplyDamage(enemy, 40);

        Assert.That(applied, Is.True);
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(60));
    }

    [Test]
    public void XPPickup_UsesPickupRangeStatAsCollectionRadius()
    {
        GameObject player = CreatePlayerWithStats(
            CreateDefinition(StatType.PickupRange, 5f, StatCategory.Miscellaneous));
        XPPickup pickup = player.AddComponent<XPPickup>();
        PlayerStats stats = player.GetComponent<PlayerStats>();
        stats.AddModifier(new StatModifier(StatType.PickupRange, 3f, StatUpgradeSource.LevelUp));

        Assert.That(pickup.PickupRadius, Is.EqualTo(8f).Within(0.0001f));
    }

    [Test]
    public void EnemySpawnRoulette_AddsExtraEliteChanceToVariantWeights()
    {
        EnemySpawnRouletteConfig config = CreateSpawnConfig();
        PlayerStats stats = CreateStatsOwner(
            CreateDefinition(StatType.ExtraEliteChance, 0f, StatCategory.Miscellaneous));
        stats.AddModifier(new StatModifier(StatType.ExtraEliteChance, 0.2f, StatUpgradeSource.PassiveItem));
        EnemySpawnRoulette roulette = new(config);

        Dictionary<EnemySpawnKind, int> weights = roulette.GetEffectiveWeights(0f, stats);
        float variantChance = weights[EnemySpawnKind.HellfireSlime] / (float)(weights[EnemySpawnKind.JunkSlime] + weights[EnemySpawnKind.HellfireSlime]);

        Assert.That(variantChance, Is.EqualTo(0.3f).Within(0.01f));
    }

    [Test]
    public void RollMaterialDropCount_CombinesScavengingBonusIntoDoubleDropChance()
    {
        PlayerStats stats = CreateStatsOwner(
            CreateDefinition(StatType.Scavenging, 50f, StatCategory.Miscellaneous),
            CreateDefinition(StatType.DoubleDrop, -30f, StatCategory.Miscellaneous));
        stats.AddModifier(new StatModifier(StatType.Scavenging, 40f, StatUpgradeSource.PassiveItem));

        int doubledDrop = PlayerDropMath.RollMaterialDropCount(stats, dropRoll: 0.6f, doubleDropRoll: 0.05f);
        int singleDrop = PlayerDropMath.RollMaterialDropCount(stats, dropRoll: 0.6f, doubleDropRoll: 0.2f);

        Assert.That(doubledDrop, Is.EqualTo(2));
        Assert.That(singleDrop, Is.EqualTo(1));
    }

    private GameObject CreatePlayerWithStats(params StatDefinition[] definitions)
    {
        GameObject player = new("PlayerStatConsumerTest");
        _cleanup.Add(player);
        PlayerHealth health = player.AddComponent<PlayerHealth>();
        CreateStatsOn(player, definitions);
        health.FullHeal();
        return player;
    }

    private PlayerStats CreateStatsOwner(params StatDefinition[] definitions)
    {
        GameObject owner = new("StatsOwner");
        _cleanup.Add(owner);
        return CreateStatsOn(owner, definitions);
    }

    private PlayerStats CreateStatsOn(GameObject owner, params StatDefinition[] definitions)
    {
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", new List<StatDefinition>(definitions));
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private EnemyHealth CreateEnemyHealth(int maxHealth)
    {
        GameObject enemyObject = new("LifestealEnemy");
        _cleanup.Add(enemyObject);
        EnemyHealth enemy = enemyObject.AddComponent<EnemyHealth>();
        enemy.ApplyConfiguredMaxHealth(maxHealth);
        return enemy;
    }

    private EnemySpawnRouletteConfig CreateSpawnConfig()
    {
        EnemySpawnRouletteConfig config = ScriptableObject.CreateInstance<EnemySpawnRouletteConfig>();
        _cleanup.Add(config);
        SetPrivateField(config, "_entries", new[]
        {
            new EnemySpawnRouletteConfig.Entry
            {
                Kind = EnemySpawnKind.JunkSlime,
                BaseWeight = 90,
                BatchSize = 1,
                IsVariant = false
            },
            new EnemySpawnRouletteConfig.Entry
            {
                Kind = EnemySpawnKind.HellfireSlime,
                BaseWeight = 10,
                BatchSize = 1,
                IsVariant = true
            }
        });
        return config;
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue, StatCategory category)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", category);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
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
