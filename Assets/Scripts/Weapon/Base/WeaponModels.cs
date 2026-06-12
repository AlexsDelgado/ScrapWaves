using System;

[Serializable]
public class WeaponLevelData
{
    public int Level = 1;
    public float DamageMultiplier = 1f;
    public float AttackRateMultiplier = 1f;
    public float ManualAmmoMultiplier = 1f;
}

[Serializable]
public class WeaponUpgradePathData
{
    public string PathName;
    public float DamageMultiplier = 1f;
    public float AttackRateMultiplier = 1f;
    public float ManualAmmoOverride = -1f;
}

[Serializable]
public class WeaponInstance
{
    public WeaponData Data;
    public int Level = 1;
    public WeaponUpgradePath SelectedPath = WeaponUpgradePath.None;
    public WeaponState State = WeaponState.Automatic;
    public float CurrentAmmo;
    public float ManualCooldownTimer;
    public float AbilityCooldownTimer;

    public bool HasAdvancedPath => Level >= 6;
}