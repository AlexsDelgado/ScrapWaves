using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class WeaponUpgradeMathTests
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
    public void GetLevelData_ReturnsExactConfiguredLevel()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 4, WeaponUpgradePath.None);

        WeaponLevelData levelData = WeaponMath.GetLevelData(weapon);

        Assert.That(levelData, Is.Not.Null);
        Assert.That(levelData.Level, Is.EqualTo(4));
        Assert.That(levelData.DamageMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
    }

    [Test]
    public void GetPathData_IgnoresSelectedPathBelowLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 5, WeaponUpgradePath.PathA);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Null);
    }

    [Test]
    public void GetPathData_ReturnsSelectedPathAtLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Not.Null);
        Assert.That(pathData.PathName, Is.EqualTo("Path B"));
        Assert.That(pathData.DamageMultiplier, Is.EqualTo(1.4f).Within(0.0001f));
    }

    [Test]
    public void GetMaxManualAmmo_UsesLevelMultiplierAndPathOverride()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathA);
        weapon.Data.PathA.ManualAmmoOverride = 400f;

        float ammo = WeaponMath.GetMaxManualAmmo(weapon, stats);

        Assert.That(ammo, Is.EqualTo(400f).Within(0.0001f));
    }

    [Test]
    public void GetAttackRateMultiplier_CombinesLevelAndPath()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        float multiplier = WeaponMath.GetAttackRateMultiplier(weapon);

        Assert.That(multiplier, Is.EqualTo(1.5f * 0.8f).Within(0.0001f));
    }

    [Test]
    public void CalculateDamage_AppliesLevelAndPathDamage()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);
        weapon.Data.BaseDamage = 10f;

        float damage = WeaponDamageResolver.CalculateDamage(stats, weapon, eliteOrBoss: false, canCrit: false);

        Assert.That(damage, Is.EqualTo(10f * 1.5f * 1.4f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_ContinuousFireMultiplier_AppliesBasePathBonus()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);

        float multiplier = InvokePrivate<float>(weapon, "GetContinuousFireAttackSpeedMultiplier");

        Assert.That(multiplier, Is.EqualTo(1.25f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_HeadHunterWeakPointScale_ReachesCapAtFullHeat()
    {
        HeatManager heat = CreateHeatManager();
        heat.SetHeat(heat.MaxHeat);
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 10, WeaponUpgradePath.PathB, heat);

        float multiplier = InvokePrivate<float>(weapon, "GetHeadHunterWeakPointScale");

        Assert.That(multiplier, Is.EqualTo(10f).Within(0.0001f));
    }

    private WeaponInstance CreateWeaponInstance(int level, WeaponUpgradePath path)
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(data);
        data.WeaponId = "TestWeapon";
        data.DisplayName = "Test Weapon";
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 4, DamageMultiplier = 1.3f, AttackRateMultiplier = 1.2f, ManualAmmoMultiplier = 1.1f },
            new() { Level = 6, DamageMultiplier = 1.5f, AttackRateMultiplier = 1.5f, ManualAmmoMultiplier = 1.2f }
        };
        data.PathA = new WeaponUpgradePathData
        {
            PathName = "Path A",
            DamageMultiplier = 1.2f,
            AttackRateMultiplier = 1.1f,
            ManualAmmoOverride = -1f
        };
        data.PathB = new WeaponUpgradePathData
        {
            PathName = "Path B",
            DamageMultiplier = 1.4f,
            AttackRateMultiplier = 0.8f,
            ManualAmmoOverride = -1f
        };

        return new WeaponInstance
        {
            Data = data,
            Level = level,
            SelectedPath = path,
            State = WeaponState.Manual
        };
    }

    private PlayerStats CreateStats()
    {
        GameObject owner = new("StatsOwner");
        _cleanup.Add(owner);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateStatDefinitions());
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private AutomaticCannonWeapon CreateAutomaticCannonWeapon(int level, WeaponUpgradePath path, HeatManager heat = null)
    {
        WeaponInstance instance = CreateWeaponInstance(level, path);
        instance.Data.WeaponType = WeaponType.AutomaticCannon;
        AutomaticCannonWeapon weapon = new(null, null, null);
        weapon.Setup(instance, null, null, heat);
        return weapon;
    }

    private HeatManager CreateHeatManager()
    {
        GameObject owner = new("HeatOwner");
        _cleanup.Add(owner);
        HeatManager heat = owner.AddComponent<HeatManager>();
        return heat;
    }

    private List<StatDefinition> CreateStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, 1f)
        };
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

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }

    private static T InvokePrivate<T>(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        return (T)method.Invoke(target, null);
    }
}
