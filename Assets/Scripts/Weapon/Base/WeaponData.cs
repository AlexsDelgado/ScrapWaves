using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [Min(0f)] public float CannonAutoAccuracySpreadDegrees = 6f;

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

    [Range(0f, 1f)] public float RocketAutoExplosionFalloff = 0.85f;
    [Range(0f, 1f)] public float RocketManualExplosionFalloff = 0.85f;
    [Range(0f, 1f)] public float RocketActiveExplosionFalloff = 0.85f;

    [Min(0.01f)] public float RocketAutoSpeedMultiplier = 1f;
    [Min(0.01f)] public float RocketManualSpeedMultiplier = 1.35f;
    [Min(0.01f)] public float RocketActiveSpeedMultiplier = 1.15f;
    [Min(0f)] public float RocketActiveDamageScale = 2f;
}

[Serializable]
public sealed class FlamethrowerTuning : WeaponSpecificTuning
{
    public static readonly FlamethrowerTuning Defaults = new();

    [Range(1f, 180f)] public float FlameAutoConeAngle = 45f;
    [Range(1f, 180f)] public float FlameManualConeAngle = 38f;
    [Min(1)] public int FlameMaxTargetsPerTick = 64;

    [Min(0.05f)] public float FlameHoseRadius = 0.75f;
    [Min(2)] public int FlameHoseSegmentCount = 12;
    [Min(0.01f)] public float FlameHoseNearFollow = 28f;
    [Min(0.01f)] public float FlameHoseFarFollow = 2.25f;
    [Min(0f)] public float FlameHoseTurbulence = 0.08f;

    [Min(0.01f)] public float FlameAutoTickInterval = 1f;
    [Min(0.01f)] public float FlameOverheatAutoTickInterval = 0.5f;
    [Range(0f, 100f)] public float FlameOverheatTickThresholdPercent = 75f;
    [Min(0.01f)] public float FlameManualTickInterval = 0.5f;
    [Min(0f)] public float FlameManualAmmoPerSecond = 10f;
    [Min(0f)] public float FlameManualRangeHeatMultiplier = 0.75f;

    [Min(0f)] public float FlameBurnDuration = 3f;
    [Min(0.01f)] public float FlameBurnTickInterval = 0.5f;
    [Min(0f)] public float FlameBurnDamageScale = 0.5f;

    [Min(0f)] public float FlameActiveRadius = 6f;
    [Min(0f)] public float FlameActiveDamageScale = 2f;
    [Min(0f)] public float FlameManualKnockbackScale = 0.25f;
    [Min(0f)] public float FlameActiveKnockbackScale = 3f;
    [Min(0f)] public float FlameVisualDuration = 0.18f;
    [Min(0f)] public float FlameActiveVisualDuration = 0.45f;
}

[Serializable]
public sealed class MortarTuning : WeaponSpecificTuning
{
    public static readonly MortarTuning Defaults = new();

    [Min(0f)] public float MortarAutoExplosionRadius = 2.6f;
    [Min(0f)] public float MortarManualExplosionRadius = 2.9f;
    [Min(0f)] public float MortarActiveExplosionRadius = 3.2f;

    [Min(0f)] public float MortarAutoAccuracyRadius = 3.8f;
    [Min(0f)] public float MortarManualAccuracyRadius = 0.75f;
    [Min(0f)] public float MortarBarrageRadius = 6f;

    [Min(0.05f)] public float MortarShellTravelTime = 0.9f;
    [Min(0.05f)] public float MortarManualTravelTime = 0.55f;
    [Min(0.05f)] public float MortarActiveTravelTime = 0.7f;

    [Min(0f)] public float MortarExplosionFalloff = 0.75f;
    [Min(0f)] public float MortarArcHeight = 7f;
    [Min(1)] public int MortarActiveShellCount = 8;
    [Min(0f)] public float MortarActiveDamageScale = 1.4f;
    [Min(0f)] public float MortarHeatFireRateBonusAbove50 = 0.75f;
    [Min(0f)] public float MortarHeatManualSpeedBonus = 0.75f;
}

[Serializable]
public sealed class RotatingBladeTuning : WeaponSpecificTuning
{
    public static readonly RotatingBladeTuning Defaults = new();

    [Min(0f)] public float BladeOrbitRadius = 2.2f;
    [FormerlySerializedAs("BladeContactRadius"), Min(0f)] public float BladeHitRadius = 0.6f;
    [FormerlySerializedAs("BladeContactTickInterval"), Min(0.01f)] public float BladeAutoDamageInterval = 0.25f;
    [FormerlySerializedAs("BladeSpinDegreesPerSecond"), Min(0f)] public float BladeBaseSpinDegreesPerSecond = 240f;

    [FormerlySerializedAs("BladeManualSlashAngle"), Range(1f, 180f)] public float BladeManualConeAngle = 85f;
    [FormerlySerializedAs("BladeManualSlashRange"), Min(0f)] public float BladeManualRange = 2.4f;
    [Min(0.01f)] public float BladeManualCooldown = 0.18f;
    [Min(0f)] public float BladeManualMaxHeatDamageBonus = 1f;

    [Min(0f)] public float BladeAutoKnockbackScale = 1.5f;
    [Min(0f)] public float BladeAutoMaxHeatKnockbackBonus = 0.5f;
    [Min(0f)] public float BladeManualKnockbackScale = 1f;
    [Min(0f)] public float BladeActiveKnockbackScale = 1.25f;

    [Min(1f)] public float BladeActiveBaseRangeMultiplier = 5f;
    [Min(1f)] public float BladeActiveMaxRangeMultiplier = 10f;
    [Min(0.01f)] public float BladeActiveHeatStepPercent = 20f;
    [FormerlySerializedAs("BladeActiveThrustWidth"), Min(0f)] public float BladeActiveLineWidth = 0.8f;
    [FormerlySerializedAs("BladeActiveDamageScale"), Min(0f)] public float BladeActiveDamageScale = 1.5f;
    [Min(0.01f)] public float BladeVisualDuration = 0.16f;
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
    public FlamethrowerTuning Flamethrower => _specificTuning as FlamethrowerTuning ?? FlamethrowerTuning.Defaults;
    public MortarTuning Mortar => _specificTuning as MortarTuning ?? MortarTuning.Defaults;
    public RotatingBladeTuning RotatingBlade => _specificTuning as RotatingBladeTuning ?? RotatingBladeTuning.Defaults;

    public static WeaponSpecificTuning CreateSpecificTuning(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.AutomaticCannon => new AutomaticCannonTuning(),
            WeaponType.Flamethrower => new FlamethrowerTuning(),
            WeaponType.RocketLauncher => new RocketLauncherTuning(),
            WeaponType.Mortar => new MortarTuning(),
            WeaponType.RotatingBlade => new RotatingBladeTuning(),
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
