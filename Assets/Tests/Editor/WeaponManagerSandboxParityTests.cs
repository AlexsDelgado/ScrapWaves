using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class WeaponManagerSandboxParityTests
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
    public void EmptyManualAmmo_CyclesImmediatelyAndRestoresAutomaticState()
    {
        WeaponManager manager = CreateWeaponManager(CreateWeapon("Cannon"), CreateWeapon("Rocket"));
        IReadOnlyList<IWeaponBehaviour> equipped = manager.GetEquippedWeapons();

        WeaponInstance first = equipped[0].Runtime;
        WeaponInstance second = equipped[1].Runtime;
        first.CurrentAmmo = 0f;

        InvokePrivate(manager, "UpdateManualWeapon", 0f, Vector3.forward);

        Assert.That(manager.GetCurrentManualWeaponIndex(), Is.EqualTo(1));
        Assert.That(first.State, Is.EqualTo(WeaponState.Automatic));
        Assert.That(second.State, Is.EqualTo(WeaponState.Manual));
        Assert.That(second.CurrentAmmo, Is.EqualTo(second.Data.BaseManualAmmo).Within(0.0001f));
        Assert.That(manager.GetManualCooldownRemaining(), Is.Zero);
    }

    [Test]
    public void AutomaticTick_UsesRuntimeStateInsteadOfCurrentIndex()
    {
        GameObject owner = Track(new GameObject("Weapon Manager Owner"));
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        SetPrivateField(manager, "_targeting", new ConfiguredEnemyTargeting());

        List<IWeaponBehaviour> equipped = ReadPrivate<List<IWeaponBehaviour>>(manager, "_equipped");
        CountingWeapon currentIndexAutomatic = new(WeaponState.Automatic);
        CountingWeapon otherAutomatic = new(WeaponState.Automatic);
        equipped.Add(currentIndexAutomatic);
        equipped.Add(otherAutomatic);
        SetPrivateField(manager, "_currentManualIndex", 0);

        InvokePrivate(manager, "UpdateAutomaticWeapons", 0.2f, Vector3.forward);

        Assert.That(currentIndexAutomatic.AutomaticTicks, Is.EqualTo(1));
        Assert.That(otherAutomatic.AutomaticTicks, Is.EqualTo(1));
    }

    [Test]
    public void CanUseAbility_AllowsRemainingManualAmmoBelowAbilityCost()
    {
        WeaponData weaponData = CreateWeapon("Cannon");
        weaponData.ActiveAbilityAmmoCost = 5f;
        WeaponManager manager = CreateWeaponManager(weaponData);
        WeaponInstance manual = manager.GetCurrentManualWeapon();
        manual.CurrentAmmo = 2f;
        manual.AbilityCooldownTimer = 0f;

        Assert.That(manager.CanUseAbility(), Is.True);
    }

    private WeaponManager CreateWeaponManager(params WeaponData[] weapons)
    {
        GameObject owner = Track(new GameObject("Weapon Manager Owner"));
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateStatDefinitions());
        InvokePrivate(stats, "Awake");

        WeaponManager manager = owner.AddComponent<WeaponManager>();
        SetPrivateField(manager, "_startingWeapons", new List<WeaponData>(weapons));
        InvokePrivate(manager, "Awake");
        return manager;
    }

    private WeaponData CreateWeapon(string name)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.name = name;
        data.WeaponId = name;
        data.DisplayName = name;
        data.BaseDamage = 1f;
        data.BaseAttackRate = 1f;
        data.BaseRange = 10f;
        data.BaseManualAmmo = 3f;
        data.ActiveAbilityAmmoCost = 1f;
        return data;
    }

    private List<StatDefinition> CreateStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AbilityCooldownReduction, 0f)
        };
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
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

    private static T ReadPrivate<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = FindPrivateMethod(target.GetType(), methodName, arguments);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, arguments);
    }

    private static MethodInfo FindPrivateMethod(System.Type type, string methodName, object[] arguments)
    {
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (method.Name != methodName)
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != arguments.Length)
                continue;

            bool matches = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (arguments[i] != null && !parameters[i].ParameterType.IsInstanceOfType(arguments[i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return method;
        }

        return null;
    }

    private sealed class CountingWeapon : IWeaponBehaviour
    {
        public int AutomaticTicks { get; private set; }
        public WeaponInstance Runtime { get; }

        public CountingWeapon(WeaponState state)
        {
            Runtime = new WeaponInstance
            {
                State = state
            };
        }

        public void Setup(WeaponInstance instance, Transform owner, PlayerStats stats, HeatManager heat)
        {
        }

        public void TickAutomatic(float deltaTime, Vector3 aimDirection)
        {
            AutomaticTicks++;
        }

        public void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
        {
        }

        public void UseActiveAbility(Vector3 aimDirection)
        {
        }

        public bool CanCrit() => false;
    }
}
