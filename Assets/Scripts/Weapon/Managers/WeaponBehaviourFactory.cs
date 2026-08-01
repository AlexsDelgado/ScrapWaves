using UnityEngine;

public static class WeaponBehaviourFactory
{
    public static IWeaponBehaviour Create(
        WeaponData data,
        IWeaponTargeting targeting,
        ProjectilePool projectilePool,
        Transform spawn,
        PlayerMovement movement,
        IWeaponPresentationSink presentationSink = null)
    {
        IWeaponBehaviour behaviour = data.WeaponType switch
        {
            WeaponType.AutomaticCannon => new AutomaticCannonWeapon(targeting, projectilePool, spawn),
            WeaponType.Flamethrower => new FlamethrowerWeapon(targeting, projectilePool, spawn, movement),
            WeaponType.RocketLauncher => new RocketLauncherWeapon(targeting, projectilePool, spawn),
            WeaponType.Mortar => new MortarWeapon(targeting, projectilePool, spawn),
            WeaponType.RotatingBlade => new RotatingBladeWeapon(targeting, projectilePool, spawn),
            _ => new BasicProjectileWeapon(targeting, projectilePool, spawn)
        };

        if (behaviour is IWeaponPresentationReceiver presentationReceiver)
            presentationReceiver.SetPresentationSink(presentationSink);

        return behaviour;
    }
}
