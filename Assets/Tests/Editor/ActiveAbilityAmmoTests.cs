using NUnit.Framework;
using UnityEngine;

public class ActiveAbilityAmmoTests
{
    [Test]
    public void CanUseAbility_AllowsCast_WhenAmmoBelowCostButCooldownReady()
    {
        var go = new GameObject("WeaponManagerTest");
        var manager = go.AddComponent<WeaponManager>();

        var weaponData = ScriptableObject.CreateInstance<WeaponData>();
        weaponData.BaseManualAmmo = 10f;
        weaponData.ActiveAbilityAmmoCost = 20f;

        manager.AddWeapon(weaponData);
        WeaponInstance manual = manager.GetCurrentManualWeapon();
        manual.CurrentAmmo = 5f;
        manual.AbilityCooldownTimer = 0f;

        Assert.That(manager.CanUseAbility(), Is.True);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(weaponData);
    }

    [Test]
    public void SpendAbilityAmmo_CanDriveAmmoToZero()
    {
        var behaviour = new BasicProjectileWeapon(null, null, null);
        var instance = new WeaponInstance
        {
            Data = ScriptableObject.CreateInstance<WeaponData>(),
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };
        behaviour.Setup(instance, new GameObject("owner").transform, null, null);

        InvokeSpendAbilityAmmo(behaviour, 20f);
        Assert.That(instance.CurrentAmmo, Is.EqualTo(0f));
    }

    private static void InvokeSpendAbilityAmmo(BasicProjectileWeapon behaviour, float amount)
    {
        var method = typeof(BasicProjectileWeapon).GetMethod("SpendAbilityAmmo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Invoke(behaviour, new object[] { amount });
    }
}
