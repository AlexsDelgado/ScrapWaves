using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class AutomaticWeaponMountTests
{
    private readonly List<Object> _cleanup = new();
    private readonly List<Transform> _registeredEnemies = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _registeredEnemies.Count; i++)
            EnemyRegistry.Unregister(_registeredEnemies[i]);
        _registeredEnemies.Clear();

        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }
        _cleanup.Clear();
    }

    [Test]
    public void ManualCycle_SwapsOnlyVacatedMount()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);

        BasicProjectileWeapon first = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon second = CreateWeapon(owner.transform, WeaponType.RocketLauncher);
        BasicProjectileWeapon third = CreateWeapon(owner.transform, WeaponType.Mortar);

        controller.AddWeapon(first, true);
        controller.AddWeapon(second, false);
        controller.AddWeapon(third, false);

        AutomaticWeaponMount secondMount = controller.GetAssignedMount(second);
        AutomaticWeaponMount thirdMount = controller.GetAssignedMount(third);
        Assert.That(first.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(secondMount, Is.Not.Null);
        Assert.That(thirdMount, Is.Not.Null.And.Not.SameAs(secondMount));

        controller.SetManualWeapon(second);

        Assert.That(second.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(controller.GetAssignedMount(first), Is.SameAs(secondMount));
        Assert.That(controller.GetAssignedMount(third), Is.SameAs(thirdMount));

        controller.SetManualWeapon(third);

        Assert.That(third.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(controller.GetAssignedMount(first), Is.SameAs(secondMount));
        Assert.That(controller.GetAssignedMount(second), Is.SameAs(thirdMount));
    }

    [Test]
    public void AutomaticBlade_ReservesMountButHidesPlaceholderRenderers()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);

        BasicProjectileWeapon manual = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon blade = CreateWeapon(owner.transform, WeaponType.RotatingBlade);
        controller.AddWeapon(manual, true);
        controller.AddWeapon(blade, false);

        AutomaticWeaponMount mount = controller.GetAssignedMount(blade);
        Assert.That(mount, Is.Not.Null);
        Assert.That(mount.GetComponentsInChildren<Renderer>(false), Is.Empty);
    }

    [Test]
    public void Full360Targeting_AcquiresEnemyBehindOwner_WhileCameraConeDoesNot()
    {
        GameObject owner = Track(new GameObject("Owner"));
        owner.transform.rotation = Quaternion.identity;
        GameObject enemy = Track(new GameObject("Enemy Behind"));
        enemy.transform.position = new Vector3(0f, 0f, -5f);
        EnemyRegistry.Register(enemy.transform);
        _registeredEnemies.Add(enemy.transform);

        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.AutoTargetingMode = WeaponTargetingMode.ClosestInRange;
        WeaponInstance runtime = new() { Data = data };
        ConfiguredEnemyTargeting targeting = new();

        data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.CameraCone;
        bool coneFound = targeting.TryGetTarget(runtime, owner.transform, 10f, Vector3.forward, out _);

        data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.Full360;
        bool fullCircleFound = targeting.TryGetTarget(runtime, owner.transform, 10f, Vector3.forward, out Transform target);

        Assert.That(coneFound, Is.False);
        Assert.That(fullCircleFound, Is.True);
        Assert.That(target, Is.SameAs(enemy.transform));
    }

    [Test]
    public void AutomaticFlamethrowerDirection_UsesBodyForward()
    {
        GameObject owner = Track(new GameObject("Owner"));
        owner.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Transform spawn = CreateChild(owner.transform, "Spawn", Vector3.zero);
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.Flamethrower;
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        FlamethrowerWeapon weapon = new(null, null, spawn, null);
        weapon.Setup(runtime, owner.transform, null, null);

        MethodInfo method = typeof(FlamethrowerWeapon).GetMethod(
            "GetAutomaticFlameDirection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Vector3 direction = (Vector3)method.Invoke(weapon, null);

        Assert.That(Vector3.Angle(direction, owner.transform.forward), Is.LessThan(0.01f));
    }

    [Test]
    public void PlayerPrefab_WiresMainFirePointAndMountController()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);

        WeaponManager manager = prefab.GetComponent<WeaponManager>();
        PlayerWeaponMountController mounts = prefab.GetComponent<PlayerWeaponMountController>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(mounts, Is.Not.Null);
        Assert.That(manager.GetProjectileSpawn(), Is.Not.Null);
        Assert.That(manager.GetProjectileSpawn().name, Is.EqualTo("Main Weapon Fire Point"));
    }

    [Test]
    public void ExistingDirectionalWeaponAssets_DefaultToFull360()
    {
        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");
        WeaponData rocket = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");

        Assert.That(cannon, Is.Not.Null);
        Assert.That(rocket, Is.Not.Null);
        Assert.That(cannon.AutomaticAimConstraint, Is.EqualTo(WeaponAutomaticAimConstraint.Full360));
        Assert.That(rocket.AutomaticAimConstraint, Is.EqualTo(WeaponAutomaticAimConstraint.Full360));
    }

    [Test]
    public void CannonOriginChange_CancelsPendingBurstBeforeRoleSwap()
    {
        GameObject owner = Track(new GameObject("Owner"));
        Transform firstOrigin = CreateChild(owner.transform, "Shoulder", Vector3.zero);
        Transform secondOrigin = CreateChild(owner.transform, "Main", Vector3.forward);
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.AutomaticCannon;
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        AutomaticCannonWeapon weapon = new(null, null, firstOrigin);
        weapon.Setup(runtime, owner.transform, null, null);
        SetPrivateField(weapon, "_lineBurstActive", true);
        SetPrivateField(weapon, "_lineBurstRemaining", 2);

        weapon.SetFireOrigin(new WeaponFireOriginBinding(secondOrigin));

        Assert.That(ReadPrivate<bool>(weapon, "_lineBurstActive"), Is.False);
        Assert.That(ReadPrivate<int>(weapon, "_lineBurstRemaining"), Is.Zero);
        Assert.That(weapon.FireOrigin.Muzzle, Is.SameAs(secondOrigin));
    }

    private BasicProjectileWeapon CreateWeapon(Transform owner, WeaponType type)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        BasicProjectileWeapon weapon = new(null, null, owner);
        weapon.Setup(runtime, owner, null, null);
        return weapon;
    }

    private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }

    private T Track<T>(T value) where T : Object
    {
        _cleanup.Add(value);
        return value;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private static T ReadPrivate<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(target);
    }
}
