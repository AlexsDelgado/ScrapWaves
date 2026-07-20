using UnityEngine;

public static class WeaponBehaviourFactory
{
    public static IWeaponBehaviour Create(
        WeaponData data,
        IWeaponTargeting targeting,
        ProjectilePool projectilePool,
        Transform spawn,
        PlayerMovement movement)
    {
        return data.WeaponType switch
        {
            WeaponType.AutomaticCannon => new AutomaticCannonWeapon(targeting, projectilePool, spawn),
            WeaponType.Flamethrower => new FlamethrowerWeapon(targeting, projectilePool, spawn, movement),
            WeaponType.RocketLauncher => new RocketLauncherWeapon(targeting, projectilePool, spawn),
            WeaponType.Mortar => new MortarWeapon(targeting, projectilePool, spawn),
            WeaponType.RotatingBlade => new RotatingBladeWeapon(targeting, projectilePool, spawn),
            _ => new BasicProjectileWeapon(targeting, projectilePool, spawn)
        };
    }
}
