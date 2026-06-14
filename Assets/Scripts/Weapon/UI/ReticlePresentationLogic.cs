using UnityEngine;

public enum ReticleMode
{
    Hidden,
    WideBrackets,
    CircleDot,
    Mortar,
    RocketLock
}

public static class ReticlePresentationLogic
{
    public static ReticleMode ResolveMode(WeaponType weaponType, bool rocketCharging)
    {
        return weaponType switch
        {
            WeaponType.Flamethrower => ReticleMode.WideBrackets,
            WeaponType.RotatingBlade => ReticleMode.WideBrackets,
            WeaponType.Mortar => ReticleMode.Mortar,
            WeaponType.RocketLauncher when rocketCharging => ReticleMode.RocketLock,
            WeaponType.RocketLauncher => ReticleMode.CircleDot,
            WeaponType.AutomaticCannon => ReticleMode.CircleDot,
            _ => ReticleMode.Hidden
        };
    }

    public static float GetRocketLockProgress(int current, int initial, int maximum)
    {
        int safeInitial = Mathf.Max(0, initial);
        int safeMaximum = Mathf.Max(safeInitial, maximum);
        if (safeMaximum == safeInitial)
            return 0f;

        int clampedCurrent = Mathf.Clamp(current, safeInitial, safeMaximum);
        return Mathf.InverseLerp(safeInitial, safeMaximum, clampedCurrent);
    }
}
