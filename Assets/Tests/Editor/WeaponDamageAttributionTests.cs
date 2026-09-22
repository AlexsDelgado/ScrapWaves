using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cubre la cadena de factores que alimenta la pestaña "último impacto" y documenta la
/// semántica de unidades vigente de CriticalChance (no la deseada: fijar el balance es
/// una decisión aparte).
/// </summary>
public sealed class WeaponDamageAttributionTests
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
    public void Factors_MultiplyBackToFinalDamage()
    {
        PlayerStats stats = CreateStats(
            CreateDefinition(StatType.DamageMultiplier, 1.4f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1.25f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2.5f));

        WeaponDamageContext context = new(
            stats,
            CreateWeapon(baseDamage: 5f),
            canCrit: false,
            critMultiplierOverride: 1f,
            damageScale: 1.45f,
            isAbilityDamage: false,
            knockbackScale: 1f);

        WeaponDamageRoll roll = CaptureRoll(() => context.CalculateDamage(eliteOrBoss: true, additionalScale: 0.6f));

        Assert.That(roll.Factors.Product, Is.EqualTo(roll.FinalDamage).Within(0.001f));
        Assert.That(roll.Factors.BaseDamage, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(roll.Factors.StatDamageMultiplier, Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(roll.Factors.EliteMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(roll.Factors.DamageScale, Is.EqualTo(1.45f).Within(0.0001f));
        Assert.That(roll.Factors.AdditionalScale, Is.EqualTo(0.6f).Within(0.0001f));
    }

    [Test]
    public void Factors_NeutralMultipliersAreOneWhenTheyDoNotApply()
    {
        PlayerStats stats = CreateStats(
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1.25f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2.5f));

        WeaponDamageContext context = new(
            stats, CreateWeapon(baseDamage: 5f), canCrit: false, critMultiplierOverride: 2f,
            damageScale: 1f, isAbilityDamage: false, knockbackScale: 1f);

        WeaponDamageRoll roll = CaptureRoll(() => context.CalculateDamage(eliteOrBoss: false));

        Assert.That(roll.IsCritical, Is.False);
        Assert.That(roll.Factors.CritMultiplier, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(roll.Factors.AbilityMultiplier, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(roll.Factors.EliteMultiplier, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(roll.Factors.RangeMultiplier, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(roll.FinalDamage, Is.EqualTo(5f).Within(0.0001f));
    }

    [Test]
    public void Factors_CritMultiplierIsCriticalDamageTimesTheWeaponOverride()
    {
        // Documenta que el "override" del cannon multiplica al stat en lugar de
        // reemplazarlo: 2.5 x 2 = x5. Es el factor exacto del salto 4 -> 20.
        PlayerStats stats = CreateStats(
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1f),
            CreateDefinition(StatType.CriticalChance, 1f),
            CreateDefinition(StatType.CriticalDamage, 2.5f));

        WeaponDamageContext context = new(
            stats, CreateWeapon(baseDamage: 5f), canCrit: true, critMultiplierOverride: 2f,
            damageScale: 1f, isAbilityDamage: false, knockbackScale: 1f);

        WeaponDamageRoll roll = CaptureRoll(() => context.CalculateDamage(eliteOrBoss: false));

        Assert.That(roll.IsCritical, Is.True);
        Assert.That(roll.Factors.CritMultiplier, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(roll.FinalDamage, Is.EqualTo(25f).Within(0.0001f));
    }

    [Test]
    public void RollCrit_TreatsCriticalChanceAsAZeroToOneFraction()
    {
        PlayerStats stats = CreateStats(CreateDefinition(StatType.CriticalChance, 0.25f));
        Random.InitState(1234);

        int crits = 0;
        const int rolls = 10000;
        for (int i = 0; i < rolls; i++)
        {
            if (WeaponDamageResolver.RollCrit(stats))
                crits++;
        }

        Assert.That(crits / (float)rolls, Is.EqualTo(0.25f).Within(0.02f));
    }

    [Test]
    public void RollCrit_ValuesAboveOne_SaturateToAlwaysCritical()
    {
        // Deja por escrito el Mathf.Clamp01 de RollCrit: cualquier valor >= 1 significa
        // 100% de críticos. Es la razón por la que un solo upgrade de nivel de
        // CriticalChance (autorado en puntos porcentuales) vuelve permanente el crítico.
        PlayerStats stats = CreateStats(CreateDefinition(StatType.CriticalChance, 0f));
        stats.AddModifier(new StatModifier(StatType.CriticalChance, 1.04f, StatUpgradeSource.LevelUp));
        Random.InitState(99);

        for (int i = 0; i < 200; i++)
            Assert.That(WeaponDamageResolver.RollCrit(stats), Is.True);

        Assert.That(StatDisplayFormat.IsSaturated(StatType.CriticalChance, stats.GetStat(StatType.CriticalChance)), Is.True);
    }

    private WeaponDamageRoll CaptureRoll(System.Action action)
    {
        WeaponDamageRoll captured = default;
        bool received = false;
        void Handler(WeaponDamageRoll roll)
        {
            captured = roll;
            received = true;
        }

        WeaponDamageResolver.OnDamageResolved += Handler;
        try
        {
            action();
        }
        finally
        {
            WeaponDamageResolver.OnDamageResolved -= Handler;
        }

        Assert.That(received, Is.True, "No se reportó ningún WeaponDamageRoll.");
        return captured;
    }

    private WeaponInstance CreateWeapon(float baseDamage)
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(data);
        data.BaseDamage = baseDamage;
        return new WeaponInstance
        {
            Data = data,
            State = WeaponState.Manual,
            CurrentAmmo = 10f
        };
    }

    private PlayerStats CreateStats(params StatDefinition[] definitions)
    {
        GameObject owner = new("WeaponDamageAttributionOwner");
        _cleanup.Add(owner);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", new List<StatDefinition>(definitions));
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
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
