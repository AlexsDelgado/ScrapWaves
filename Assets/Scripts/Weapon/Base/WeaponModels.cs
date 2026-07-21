using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeaponStatScaling
{
    public StatType StatType;
    [Range(0f, 1f)] public float Effectiveness = 1f;
}

[Serializable]
public class WeaponBalanceStatRow
{
    public string StatId;
    public WeaponBalanceZone Zone;
    public int Level;
    public float Value;
    public WeaponStatScaling Scaling;
    public string Notes;
}

[Serializable]
public class WeaponUpgradeSpecificStatRow
{
    public string StatId;
    public WeaponUpgradePath Path;
    public int Level;
    public float Value;
    public WeaponStatScaling Scaling;
}

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
    public List<WeaponLevelData> LevelData = new();
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
