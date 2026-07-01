using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ManualWeaponFireCooldownTests
{
    private readonly List<Object> cleanupObjects = new();

    [SetUp]
    public void SetUp()
    {
        cleanupObjects.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanupObjects.Count - 1; i >= 0; i--)
        {
            Object cleanupObject = cleanupObjects[i];
            if (cleanupObject != null)
            {
                Object.DestroyImmediate(cleanupObject);
            }
        }

        cleanupObjects.Clear();
    }

    [Test]
    public void TickManual_DecrementsFireTimer_WhenNotFiring()
    {
        var owner = TrackForCleanup(new GameObject("Owner"));
        var spawn = TrackForCleanup(new GameObject("Spawn"));
        spawn.transform.position = Vector3.zero;

        var weaponStats = owner.AddComponent<PlayerStats>();
        SetStatDefinitions(weaponStats, CreateAttackSpeedDefinitions());
        InvokePrivate(weaponStats, "Awake");

        WeaponData weaponData = TrackForCleanup(ScriptableObject.CreateInstance<WeaponData>());
        weaponData.BaseAttackRate = 1f;
        weaponData.BaseManualAmmo = 10f;
        weaponData.BaseRange = 10f;

        WeaponInstance runtime = new()
        {
            Data = weaponData,
            State = WeaponState.Manual,
            CurrentAmmo = 10f
        };

        var weapon = new TestManualWeapon(spawn.transform);
        weapon.Setup(runtime, owner.transform, weaponStats, null);
        weapon.SetFireTimerForTest(0.5f);

        weapon.TickManual(0.2f, Vector3.forward, false);

        Assert.That(weapon.GetFireTimerForTest(), Is.EqualTo(0.3f).Within(0.0001f));
    }

    [Test]
    public void TickManual_ClickAfterIdleCooldown_SpendsAmmoAndResetsTimer()
    {
        var owner = TrackForCleanup(new GameObject("Owner"));
        var spawn = TrackForCleanup(new GameObject("Spawn"));
        spawn.transform.position = Vector3.zero;

        var weaponStats = owner.AddComponent<PlayerStats>();
        SetStatDefinitions(weaponStats, CreateAttackSpeedDefinitions());
        InvokePrivate(weaponStats, "Awake");

        WeaponData weaponData = TrackForCleanup(ScriptableObject.CreateInstance<WeaponData>());
        weaponData.BaseAttackRate = 2f;
        weaponData.BaseManualAmmo = 10f;
        weaponData.BaseRange = 10f;

        WeaponInstance runtime = new()
        {
            Data = weaponData,
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };

        var weapon = new TestManualWeapon(spawn.transform);
        weapon.Setup(runtime, owner.transform, weaponStats, null);
        weapon.SetFireTimerForTest(0.1f);

        weapon.TickManual(0.2f, Vector3.forward, false);

        Assert.That(runtime.CurrentAmmo, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(weapon.GetFireTimerForTest(), Is.EqualTo(0f).Within(0.0001f));

        weapon.TickManual(0f, Vector3.forward, true);

        Assert.That(runtime.CurrentAmmo, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(weapon.GetFireTimerForTest(), Is.EqualTo(0.5f).Within(0.0001f));
    }

    private List<StatDefinition> CreateAttackSpeedDefinitions()
    {
        StatDefinition attackSpeed = TrackForCleanup(ScriptableObject.CreateInstance<StatDefinition>());
        SetAutoProperty(attackSpeed, "<StatType>k__BackingField", StatType.AttackSpeedMultiplier);
        SetAutoProperty(attackSpeed, "<Category>k__BackingField", StatCategory.Offensive);
        SetAutoProperty(attackSpeed, "<BaseValue>k__BackingField", 1f);
        SetAutoProperty(attackSpeed, "<UpgradeableByLevel>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<UpgradeableByItems>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetAutoProperty(attackSpeed, "<IsPercentage>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<IsInteger>k__BackingField", false);
        return new List<StatDefinition> { attackSpeed };
    }

    private T TrackForCleanup<T>(T unityObject) where T : Object
    {
        cleanupObjects.Add(unityObject);
        return unityObject;
    }

    private static void SetStatDefinitions(PlayerStats stats, List<StatDefinition> definitions)
    {
        FieldInfo field = typeof(PlayerStats).GetField("_statDefinitions", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(stats, definitions);
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(instance, null);
    }

    private static void SetAutoProperty(object instance, string backingFieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(instance, value);
    }

    private sealed class TestManualWeapon : BasicProjectileWeapon
    {
        public TestManualWeapon(Transform spawn)
            : base(null, null, spawn)
        {
        }

        public void SetFireTimerForTest(float value) => FireTimer = value;
        public float GetFireTimerForTest() => FireTimer;
    }
}
