using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public abstract class WeaponSpecificTuning
{
}

[Serializable]
public sealed class AutomaticCannonTuning : WeaponSpecificTuning
{
    public static readonly AutomaticCannonTuning Defaults = new();

    [Min(1)] public int CannonAutoBurstCount = 3;
    [Min(1)] public int CannonManualBurstCount = 5;
    [Min(1)] public int CannonActiveBaseBulletCount = 20;

    [Min(0f)] public float CannonAbilityScatterRadius = 22f;
    [Min(0f)] public float CannonManualLineSpacing = 0.45f;
    [Min(0f)] public float CannonAutoLineSpacing = 0.45f;

    [Min(0.01f)] public float CannonActiveHeatBulletStepPercent = 5f;
    [Min(0.01f)] public float CannonHeatDamageThresholdStepPercent = 25f;
    [Min(0)] public int CannonHeatDamageThresholdCount = 3;
    [Min(0f)] public float CannonHeatDamageBonusPerThreshold = 0.15f;
    [Min(1f)] public float CannonCriticalDamageMultiplierOverride = 2f;
}

[Serializable]
public sealed class RocketLauncherTuning : WeaponSpecificTuning
{
    public static readonly RocketLauncherTuning Defaults = new();

    [Min(1)] public int RocketAutoBaseRocketCount = 2;
    [Min(1)] public int RocketActiveBaseRocketCount = 10;
    [Range(0f, 360f)] public float RocketActiveConeAngle = 90f;

    [Min(0f)] public float RocketAutoExplosionRadius = 1.8f;
    [Min(0f)] public float RocketManualExplosionRadius = 2.4f;
    [Min(0f)] public float RocketActiveExplosionRadius = 2.9f;

    [Range(0f, 1f)] public float RocketAutoExplosionFalloff = 0.45f;
    [Range(0f, 1f)] public float RocketManualExplosionFalloff = 0.35f;
    [Range(0f, 1f)] public float RocketActiveExplosionFalloff = 0.5f;

    [Min(0.01f)] public float RocketAutoSpeedMultiplier = 1f;
    [Min(0.01f)] public float RocketManualSpeedMultiplier = 1.35f;
    [Min(0.01f)] public float RocketActiveSpeedMultiplier = 1.15f;
    [Min(0f)] public float RocketActiveDamageScale = 2f;
}

[CreateAssetMenu(fileName = "WeaponData", menuName = "ScrapWaves/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public string WeaponId;
    public string DisplayName;
    public WeaponType WeaponType = WeaponType.AutomaticCannon;
    public WeaponTargetingMode AutoTargetingMode = WeaponTargetingMode.ClosestInRange;
    public WeaponManualMode ManualMode = WeaponManualMode.AimAtReticle;

    public float BaseDamage = 10f;
    public float BaseAttackRate = 1f;
    public float BaseRange = 12f;
    public float BaseKnockback = 1f;
    public float BaseManualAmmo = 100f;
    public float ActiveAbilityAmmoCost = 20f;

    [SerializeReference] private WeaponSpecificTuning _specificTuning;

    public List<WeaponLevelData> LevelData = new();
    public WeaponUpgradePathData PathA;
    public WeaponUpgradePathData PathB;

    public AutomaticCannonTuning AutomaticCannon => _specificTuning as AutomaticCannonTuning ?? AutomaticCannonTuning.Defaults;
    public RocketLauncherTuning RocketLauncher => _specificTuning as RocketLauncherTuning ?? RocketLauncherTuning.Defaults;

    public static WeaponSpecificTuning CreateSpecificTuning(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.AutomaticCannon => new AutomaticCannonTuning(),
            WeaponType.RocketLauncher => new RocketLauncherTuning(),
            _ => null
        };
    }

    public void EnsureSpecificTuningForCurrentType()
    {
        WeaponSpecificTuning expected = CreateSpecificTuning(WeaponType);
        if (expected == null)
        {
            _specificTuning = null;
            return;
        }

        if (_specificTuning == null || _specificTuning.GetType() != expected.GetType())
            _specificTuning = expected;
    }

    private void OnValidate()
    {
        EnsureSpecificTuningForCurrentType();
    }
}
