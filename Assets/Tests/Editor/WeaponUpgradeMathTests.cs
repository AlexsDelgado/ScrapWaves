using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class WeaponUpgradeMathTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _cleanup.Count; i++)
            Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void GetLevelData_ReturnsExactConfiguredLevel()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 4, WeaponUpgradePath.None);

        WeaponLevelData levelData = WeaponMath.GetLevelData(weapon);

        Assert.That(levelData, Is.Not.Null);
        Assert.That(levelData.Level, Is.EqualTo(4));
        Assert.That(levelData.DamageMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
    }

    [Test]
    public void GetPathData_IgnoresSelectedPathBelowLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 5, WeaponUpgradePath.PathA);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Null);
    }

    [Test]
    public void GetPathData_ReturnsSelectedPathAtLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Not.Null);
        Assert.That(pathData.PathName, Is.EqualTo("Path B"));
        Assert.That(pathData.DamageMultiplier, Is.EqualTo(1.4f).Within(0.0001f));
    }

    [Test]
    public void GetLevelData_UsesSelectedPathLevelDataWhenConfigured()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 7, WeaponUpgradePath.PathB);
        weapon.Data.PathB.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 7, DamageMultiplier = 3.25f, AttackRateMultiplier = 0.65f, ManualAmmoMultiplier = 0.45f }
        };

        WeaponLevelData levelData = WeaponMath.GetLevelData(weapon);

        Assert.That(levelData, Is.Not.Null);
        Assert.That(levelData.Level, Is.EqualTo(7));
        Assert.That(levelData.DamageMultiplier, Is.EqualTo(3.25f).Within(0.0001f));
        Assert.That(WeaponMath.GetAttackRateMultiplier(weapon), Is.EqualTo(0.65f * 0.8f).Within(0.0001f));
    }

    [Test]
    public void GetMaxManualAmmo_UsesLevelMultiplierAndPathOverride()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathA);
        weapon.Data.PathA.ManualAmmoOverride = 400f;

        float ammo = WeaponMath.GetMaxManualAmmo(weapon, stats);

        Assert.That(ammo, Is.EqualTo(400f).Within(0.0001f));
    }

    [Test]
    public void GetAttackRateMultiplier_CombinesLevelAndPath()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        float multiplier = WeaponMath.GetAttackRateMultiplier(weapon);

        Assert.That(multiplier, Is.EqualTo(1.5f * 0.8f).Within(0.0001f));
    }

    [Test]
    public void CalculateDamage_AppliesLevelAndPathDamage()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);
        weapon.Data.BaseDamage = 10f;

        float damage = WeaponDamageResolver.CalculateDamage(stats, weapon, eliteOrBoss: false, canCrit: false);

        Assert.That(damage, Is.EqualTo(10f * 1.5f * 1.4f).Within(0.0001f));
    }

    [Test]
    public void CalculateDamage_AppliesAbilityDamageMultiplierOnlyForAbilityDamage()
    {
        PlayerStats stats = CreateStats();
        stats.AddModifier(new StatModifier(StatType.AbilityDamageMultiplier, 0.5f, StatUpgradeSource.PassiveItem));
        WeaponInstance weapon = CreateWeaponInstance(level: 1, WeaponUpgradePath.None);
        weapon.Data.BaseDamage = 10f;

        float regularDamage = WeaponDamageResolver.CalculateDamage(stats, weapon, eliteOrBoss: false, canCrit: false);
        float abilityDamage = WeaponDamageResolver.CalculateDamage(stats, weapon, eliteOrBoss: false, canCrit: false, isAbilityDamage: true);

        Assert.That(regularDamage, Is.EqualTo(10f).Within(0.0001f));
        Assert.That(abilityDamage, Is.EqualTo(15f).Within(0.0001f));
    }

    [Test]
    public void GetAbilityCooldownDuration_AppliesCooldownReductionStat()
    {
        PlayerStats stats = CreateStats();
        stats.AddModifier(new StatModifier(StatType.AbilityCooldownReduction, 0.25f, StatUpgradeSource.PassiveItem));
        WeaponInstance weapon = CreateWeaponInstance(level: 1, WeaponUpgradePath.None);
        weapon.Data.SkillCooldown = 8f;

        float cooldown = WeaponMath.GetAbilityCooldownDuration(weapon, stats);

        Assert.That(cooldown, Is.EqualTo(6f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_ContinuousFireMultiplier_AppliesBasePathBonus()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);

        float multiplier = InvokePrivate<float>(weapon, "GetContinuousFireAttackSpeedMultiplier");

        Assert.That(multiplier, Is.EqualTo(1.25f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_AutomaticFireInterval_UsesExposedAutoCadenceAndPathMultipliers()
    {
        AutomaticCannonWeapon baseWeapon = CreateAutomaticCannonWeapon(level: 1, WeaponUpgradePath.None);
        NormalizeAttackRateMultipliers(baseWeapon.Runtime.Data);
        baseWeapon.Runtime.Data.BaseAttackRate = 99f;
        baseWeapon.Runtime.Data.AutomaticCannon.CannonAutoBurstsPerSecond = 4f;

        float baseInterval = InvokePrivate<float>(
            baseWeapon,
            "GetAutomaticFireInterval",
            baseWeapon.Runtime.Data.AutomaticCannon);

        AutomaticCannonWeapon continuous = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);
        NormalizeAttackRateMultipliers(continuous.Runtime.Data);
        continuous.Runtime.Data.AutomaticCannon.CannonAutoBurstsPerSecond = 4f;
        continuous.Runtime.Data.AutomaticCannon.ContinuousFireAutoAttackSpeedMultiplier = 2f;

        float continuousInterval = InvokePrivate<float>(
            continuous,
            "GetAutomaticFireInterval",
            continuous.Runtime.Data.AutomaticCannon);

        AutomaticCannonWeapon headHunter = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathB);
        NormalizeAttackRateMultipliers(headHunter.Runtime.Data);
        headHunter.Runtime.Data.AutomaticCannon.CannonAutoBurstsPerSecond = 4f;
        headHunter.Runtime.Data.AutomaticCannon.HeadHunterAutoAttackSpeedMultiplier = 3f;

        float headHunterInterval = InvokePrivate<float>(
            headHunter,
            "GetAutomaticFireInterval",
            headHunter.Runtime.Data.AutomaticCannon);

        Assert.That(baseInterval, Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(continuousInterval, Is.EqualTo(1f / 24f).Within(0.0001f));
        Assert.That(headHunterInterval, Is.EqualTo(1f / 12f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_ContinuousFire_ReplacesBurstsAndDefinesActiveBarrage()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);
        AutomaticCannonTuning tuning = weapon.Runtime.Data.AutomaticCannon;

        Assert.That(InvokePrivate<int>(weapon, "GetAutomaticShotCount", tuning), Is.EqualTo(1));
        Assert.That(InvokePrivate<int>(weapon, "GetManualShotCount", tuning), Is.EqualTo(1));
        Assert.That(InvokePrivate<float>(weapon, "GetContinuousFireManualAttackSpeedMultiplier"), Is.EqualTo(6.25f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetContinuousFireActiveDuration"), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetContinuousFireActiveBulletsPerSecond"), Is.EqualTo(40f).Within(0.0001f));
        Assert.That(InvokePrivate<int>(weapon, "GetContinuousFireActiveBulletCount"), Is.EqualTo(80));
        Assert.That(InvokePrivate<float>(weapon, "GetActiveAbilityAmmoCost"), Is.EqualTo(80f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_ContinuousFire_EmitsMoreRoundsPerSecondThanReplacedBursts()
    {
        AutomaticCannonWeapon baseline = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.None);
        AutomaticCannonWeapon continuous = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);
        NormalizeAttackRateMultipliers(baseline.Runtime.Data);
        NormalizeAttackRateMultipliers(continuous.Runtime.Data);

        AutomaticCannonTuning baseTuning = baseline.Runtime.Data.AutomaticCannon;
        AutomaticCannonTuning continuousTuning = continuous.Runtime.Data.AutomaticCannon;
        baseTuning.CannonAutoBurstsPerSecond = 5f;
        baseTuning.CannonAutoBurstCount = 3;
        baseTuning.CannonManualBurstsPerSecond = 3f;
        baseTuning.CannonManualBurstCount = 5;
        continuousTuning.CannonAutoBurstsPerSecond = 5f;
        continuousTuning.CannonAutoBurstCount = 3;
        continuousTuning.CannonManualBurstsPerSecond = 3f;
        continuousTuning.CannonManualBurstCount = 5;
        continuousTuning.ContinuousFireAutoAttackSpeedMultiplier = 1.25f;
        float baseInterval = InvokePrivate<float>(baseline, "GetAutomaticFireInterval", baseTuning);
        float continuousInterval = InvokePrivate<float>(continuous, "GetAutomaticFireInterval", continuousTuning);
        float baseRoundsPerSecond = InvokePrivate<int>(baseline, "GetAutomaticShotCount", baseTuning) / baseInterval;
        float continuousRoundsPerSecond = InvokePrivate<int>(continuous, "GetAutomaticShotCount", continuousTuning) / continuousInterval;
        float baseManualRoundsPerSecond = baseTuning.CannonManualBurstsPerSecond * baseTuning.CannonManualBurstCount;
        float continuousManualRoundsPerSecond = continuousTuning.CannonManualBurstsPerSecond *
                                                InvokePrivate<float>(continuous, "GetContinuousFireManualAttackSpeedMultiplier");

        Assert.That(baseRoundsPerSecond, Is.EqualTo(15f).Within(0.001f));
        Assert.That(continuousRoundsPerSecond, Is.EqualTo(18.75f).Within(0.001f));
        Assert.That(continuousRoundsPerSecond, Is.GreaterThan(baseRoundsPerSecond));
        Assert.That(continuousManualRoundsPerSecond, Is.GreaterThan(baseManualRoundsPerSecond));
    }

    [Test]
    public void AutomaticCannon_ContinuousFireActive_StartsTimedBarrage()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);
        weapon.Runtime.CurrentAmmo = 100f;

        weapon.UseActiveAbility(Vector3.forward);

        Assert.That(ReadPrivate<bool>(weapon, "_continuousFireActive"), Is.True);
        Assert.That(ReadPrivate<float>(weapon, "_continuousFireActiveRemainingDuration"), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(ReadPrivate<int>(weapon, "_continuousFireActiveShotsRemaining"), Is.EqualTo(80));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(20f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_ContinuousFireActive_TracksLatestAimDirection()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathA);
        weapon.Runtime.CurrentAmmo = 100f;

        weapon.UseActiveAbility(Vector3.forward);
        InvokePrivate<bool>(weapon, "TickContinuousFireActive", 0.1f, Vector3.right);

        Vector3 activeDirection = ReadPrivate<Vector3>(weapon, "_continuousFireActiveDirection");
        Assert.That(activeDirection.x, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(activeDirection.z, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_HeadHunterWeakPointScale_ReachesCapAtFullHeat()
    {
        HeatManager heat = CreateHeatManager();
        heat.SetHeat(heat.MaxHeat);
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 10, WeaponUpgradePath.PathB, heat);

        float multiplier = InvokePrivate<float>(weapon, "GetHeadHunterWeakPointScale");

        Assert.That(multiplier, Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_HeadHunter_UsesPiercingFalloffAndChargeGate()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathB);

        Assert.That(InvokePrivate<int>(weapon, "GetHeadHunterPierceLimit"), Is.EqualTo(10));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterPierceDamageScale", 0), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterPierceDamageScale", 5), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterActiveChargeSeconds"), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(InvokePrivate<int>(weapon, "GetHeadHunterActivePierceLimit"), Is.EqualTo(int.MaxValue));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterActivePierceRange"), Is.GreaterThanOrEqualTo(1000f));
    }

    [Test]
    public void AutomaticCannon_HeadHunterActivePierceRange_IgnoresProjectileAreaSize()
    {
        PlayerStats stats = CreateStats(projectileAreaSize: 2f);
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathB, stats: stats);
        weapon.Runtime.Data.BaseRange = 1200f;

        float range = InvokePrivate<float>(weapon, "GetHeadHunterActivePierceRange");

        Assert.That(range, Is.EqualTo(1200f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_AutomaticTargetRange_IgnoresProjectileAreaSize()
    {
        GameObject spawn = new("Cannon Spawn");
        _cleanup.Add(spawn);
        CapturingTargeting targeting = new(returnTarget: false);
        PlayerStats stats = CreateStats(projectileAreaSize: 2f);
        WeaponInstance instance = CreateWeaponInstance(level: 1, WeaponUpgradePath.None);
        instance.Data.WeaponType = WeaponType.AutomaticCannon;
        instance.Data.BaseRange = 12f;
        instance.State = WeaponState.Automatic;

        AutomaticCannonWeapon weapon = new(targeting, null, spawn.transform);
        weapon.Setup(instance, null, stats, null);

        weapon.TickAutomatic(1f, Vector3.forward);

        Assert.That(targeting.LastRange, Is.EqualTo(12f).Within(0.0001f));
    }

    [Test]
    public void AutomaticCannon_HeadHunterDamageScale_MatchesSpec()
    {
        AutomaticCannonWeapon weapon = CreateAutomaticCannonWeapon(level: 6, WeaponUpgradePath.PathB);

        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Normal, 0, false, false), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Elite, 0, false, false), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Boss, 0, false, false), Is.EqualTo(3f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Elite, 1, false, false), Is.EqualTo(1.8f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Normal, 0, true, false), Is.EqualTo(5f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetHeadHunterDamageScale", WeaponEnemyKind.Normal, 5, false, true), Is.EqualTo(5f).Within(0.0001f));
    }

    [Test]
    public void RocketLauncher_KineticExplosion_UsesSpecRadiusAndKnockback()
    {
        RocketLauncherWeapon weapon = CreateRocketLauncherWeapon(level: 6, WeaponUpgradePath.PathA);
        RocketLauncherWeapon baseline = CreateRocketLauncherWeapon(level: 6, WeaponUpgradePath.None);

        float radius = InvokePrivate<float>(weapon, "GetPathAdjustedExplosionRadius", 2f);
        float baselineKnockback = InvokePrivate<float>(baseline, "GetPathAdjustedKnockbackScale", false);
        float regularKnockback = InvokePrivate<float>(weapon, "GetPathAdjustedKnockbackScale", false);
        float activeKnockback = InvokePrivate<float>(weapon, "GetPathAdjustedKnockbackScale", true);

        Assert.That(radius, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(baselineKnockback, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(regularKnockback, Is.EqualTo(3f).Within(0.0001f));
        Assert.That(activeKnockback, Is.EqualTo(baselineKnockback * 0.5f).Within(0.0001f));
    }

    [Test]
    public void RocketLauncher_FragmentationCap_UsesSpecRadiusKnockbackAndConeDamage()
    {
        RocketLauncherWeapon weapon = CreateRocketLauncherWeapon(level: 6, WeaponUpgradePath.PathB);

        float radius = InvokePrivate<float>(weapon, "GetPathAdjustedExplosionRadius", 2f);
        float knockback = InvokePrivate<float>(weapon, "GetPathAdjustedKnockbackScale", false);
        float fragmentDamageScale = InvokePrivate<float>(weapon, "GetFragmentDamageScale", false);
        float activeFragmentDamageScale = InvokePrivate<float>(weapon, "GetFragmentDamageScale", true);
        float manualConeRange = InvokePrivate<float>(weapon, "GetFragmentConeRange", 1f, false);
        float activeConeRange = InvokePrivate<float>(weapon, "GetFragmentConeRange", 1f, true);
        int clusterCount = InvokePrivate<int>(weapon, "GetFragmentClusterRocketCount");
        float clusterDamageScale = InvokePrivate<float>(weapon, "GetFragmentClusterDamageScale");

        Assert.That(radius, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(knockback, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(fragmentDamageScale, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(manualConeRange, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(activeFragmentDamageScale, Is.Zero);
        Assert.That(activeConeRange, Is.Zero);
        Assert.That(weapon.Runtime.Data.RocketLauncher.RocketActiveDamageScale, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(clusterCount, Is.EqualTo(20));
        Assert.That(clusterDamageScale, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void RocketLauncher_AutomaticTargetRange_IgnoresProjectileAreaSize()
    {
        GameObject spawn = new("Rocket Spawn");
        _cleanup.Add(spawn);
        CapturingTargeting targeting = new(returnTarget: false);
        PlayerStats stats = CreateStats(projectileAreaSize: 2f);
        WeaponInstance instance = CreateWeaponInstance(level: 1, WeaponUpgradePath.None);
        instance.Data.WeaponType = WeaponType.RocketLauncher;
        instance.Data.EnsureSpecificTuningForCurrentType();
        instance.Data.BaseRange = 20f;
        instance.State = WeaponState.Automatic;

        RocketLauncherWeapon weapon = new(targeting, null, spawn.transform);
        weapon.Setup(instance, null, stats, null);

        weapon.TickAutomatic(1f, Vector3.forward);

        Assert.That(targeting.LastRange, Is.EqualTo(20f).Within(0.0001f));
    }

    [Test]
    public void MortarManualFireInterval_IgnoresAutomaticHeatFireRateBonus()
    {
        HeatManager heat = CreateHeatManager();
        heat.SetHeat(heat.MaxHeat);
        PlayerStats stats = CreateStats();
        MortarWeapon weapon = CreateMortarWeapon(heat, stats);
        weapon.Runtime.Data.BaseAttackRate = 1f;
        weapon.Runtime.Data.Mortar.MortarHeatFireRateBonusAbove50 = 0.75f;
        weapon.Runtime.Data.Mortar.MortarHeatManualSpeedBonus = 0.5f;

        float interval = InvokePrivate<float>(weapon, "GetManualFireInterval");

        Assert.That(interval, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void MortarManualTravelTime_UsesHalfHeatAsSpeedBonus()
    {
        HeatManager heat = CreateHeatManager();
        heat.SetHeat(heat.MaxHeat);
        MortarWeapon weapon = CreateMortarWeapon(heat, stats: null);
        MortarTuning tuning = weapon.Runtime.Data.Mortar;
        tuning.MortarManualTravelTime = 0.6f;

        float travelTime = InvokePrivate<float>(weapon, "GetManualTravelTime", tuning);

        Assert.That(travelTime, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_MultiBladeCount_ScalesWithPathLevel()
    {
        RotatingBladeWeapon levelSix = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathA);
        RotatingBladeWeapon levelSeven = CreateRotatingBladeWeapon(level: 7, WeaponUpgradePath.PathA);
        RotatingBladeWeapon levelTen = CreateRotatingBladeWeapon(level: 10, WeaponUpgradePath.PathA);
        RotatingBladeWeapon atomic = CreateRotatingBladeWeapon(level: 10, WeaponUpgradePath.PathB);

        Assert.That(InvokePrivate<int>(levelSix, "GetBladeCount"), Is.EqualTo(2));
        Assert.That(InvokePrivate<int>(levelSeven, "GetBladeCount"), Is.EqualTo(3));
        Assert.That(InvokePrivate<int>(levelTen, "GetBladeCount"), Is.EqualTo(6));
        Assert.That(InvokePrivate<int>(atomic, "GetBladeCount"), Is.EqualTo(1));
    }

    [Test]
    public void RotatingBlade_BladeCenterOffsetsMultipleBladesAroundOwner()
    {
        GameObject owner = new("BladeOwner");
        _cleanup.Add(owner);
        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathA, owner.transform);
        SetPrivateField(weapon, "_spinAngle", 0f);

        RotatingBladeTuning tuning = weapon.Runtime.Data.RotatingBlade;
        Vector3 first = InvokePrivate<Vector3>(weapon, "GetBladeCenter", tuning, 0, 2);
        Vector3 second = InvokePrivate<Vector3>(weapon, "GetBladeCenter", tuning, 1, 2);

        Assert.That(first.z, Is.GreaterThan(0f));
        Assert.That(second.z, Is.LessThan(0f));
        Assert.That(Vector3.Distance(first, second), Is.EqualTo(tuning.BladeOrbitRadius * 2f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_BladeCenterDoesNotFollowOwnerRotation()
    {
        GameObject owner = new("BladeOwner");
        _cleanup.Add(owner);
        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathA, owner.transform);
        SetPrivateField(weapon, "_spinAngle", 0f);

        RotatingBladeTuning tuning = weapon.Runtime.Data.RotatingBlade;
        owner.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        Vector3 forwardFacingCenter = InvokePrivate<Vector3>(weapon, "GetBladeCenter", tuning, 0, 1);

        owner.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        Vector3 sidewaysFacingCenter = InvokePrivate<Vector3>(weapon, "GetBladeCenter", tuning, 0, 1);

        Assert.That(Vector3.Distance(sidewaysFacingCenter, forwardFacingCenter), Is.LessThan(0.0001f));
    }

    [Test]
    public void RotatingBlade_AtomicSharpness_DoublesDamageAndRemovesKnockback()
    {
        RotatingBladeWeapon atomic = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathB);
        RotatingBladeWeapon baseline = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathA);

        Assert.That(InvokePrivate<float>(atomic, "GetAtomicSharpnessDamageScale"), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(atomic, "GetAtomicSharpnessKnockbackScale", 1.25f), Is.Zero);
        Assert.That(InvokePrivate<float>(baseline, "GetAtomicSharpnessDamageScale"), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(baseline, "GetAtomicSharpnessKnockbackScale", 1.25f), Is.EqualTo(1.25f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_AtomicSharpness_SpinsFasterThanBase()
    {
        RotatingBladeWeapon atomic = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathB);
        RotatingBladeWeapon baseline = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathA);

        Assert.That(InvokePrivate<float>(atomic, "GetAtomicSharpnessSpinMultiplier"), Is.GreaterThan(1f));
        Assert.That(InvokePrivate<float>(baseline, "GetAtomicSharpnessSpinMultiplier"), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_MultiBlade_StagesHitsAndOnlyFinalSwingKnocksBack()
    {
        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 10, WeaponUpgradePath.PathA);

        Assert.That(InvokePrivate<float>(weapon, "GetMultiBladeActionInterval"), Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(InvokePrivate<bool>(weapon, "ShouldApplyMultiBladeKnockback", 0, 3), Is.False);
        Assert.That(InvokePrivate<bool>(weapon, "ShouldApplyMultiBladeKnockback", 2, 3), Is.True);
    }

    [Test]
    public void RotatingBlade_AtomicSharpnessActive_UsesDashDamageAndIFrameWindow()
    {
        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathB);
        RotatingBladeTuning tuning = weapon.Runtime.Data.RotatingBlade;
        float dashSegmentSeconds = InvokePrivate<float>(weapon, "GetAtomicActiveDashSegmentSeconds");
        float dashDuration = InvokePrivate<float>(weapon, "GetAtomicDashDurationForHitCount", 3);

        Assert.That(InvokePrivate<float>(weapon, "GetAtomicActiveDamageScale"), Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(dashSegmentSeconds, Is.EqualTo(0.16f).Within(0.0001f));
        Assert.That(dashDuration, Is.EqualTo(dashSegmentSeconds * 4f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetAtomicActivePostDashInvulnerabilitySeconds"), Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetAtomicActiveInvulnerabilityDuration", dashDuration), Is.EqualTo(dashDuration + 0.25f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetAtomicDashRangeForHitCount", 7f, 3, tuning), Is.EqualTo(28f).Within(0.0001f));
        Assert.That(InvokePrivate<float>(weapon, "GetAtomicDashBaseRange", tuning), Is.EqualTo(7.2f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_AtomicSharpnessDashRange_UsesExposedMultipliers()
    {
        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 6, WeaponUpgradePath.PathB);
        RotatingBladeTuning tuning = weapon.Runtime.Data.RotatingBlade;
        tuning.AtomicDashBaseRangeMultiplier = 2f;
        tuning.AtomicDashRangePerHitMultiplier = 0.5f;

        float baseRange = InvokePrivate<float>(weapon, "GetAtomicDashBaseRange", tuning);
        float resetRange = InvokePrivate<float>(weapon, "GetAtomicDashRangeForHitCount", baseRange, 3, tuning);

        Assert.That(baseRange, Is.EqualTo(4.8f).Within(0.0001f));
        Assert.That(resetRange, Is.EqualTo(12f).Within(0.0001f));
    }

    [Test]
    public void RotatingBlade_HorizontalAimDirection_UsesCameraFacingWhenReticleVectorFlipsBackward()
    {
        GameObject cameraGo = new("Sword Aim Camera");
        _cleanup.Add(cameraGo);
        Camera camera = cameraGo.AddComponent<Camera>();
        cameraGo.tag = "MainCamera";
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.65f, 1f).normalized, Vector3.up);

        RotatingBladeWeapon weapon = CreateRotatingBladeWeapon(level: 1, WeaponUpgradePath.None);

        Vector3 direction = InvokePrivate<Vector3>(weapon, "GetHorizontalAimDirection", new Vector3(0f, -0.8f, -1f));

        Assert.That(direction.z, Is.GreaterThan(0.99f));
        Assert.That(Mathf.Abs(direction.x), Is.LessThan(0.01f));
    }

    private WeaponInstance CreateWeaponInstance(int level, WeaponUpgradePath path)
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(data);
        data.WeaponId = "TestWeapon";
        data.DisplayName = "Test Weapon";
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 4, DamageMultiplier = 1.3f, AttackRateMultiplier = 1.2f, ManualAmmoMultiplier = 1.1f },
            new() { Level = 6, DamageMultiplier = 1.5f, AttackRateMultiplier = 1.5f, ManualAmmoMultiplier = 1.2f }
        };
        data.PathA = new WeaponUpgradePathData
        {
            PathName = "Path A",
            DamageMultiplier = 1.2f,
            AttackRateMultiplier = 1.1f,
            ManualAmmoOverride = -1f
        };
        data.PathB = new WeaponUpgradePathData
        {
            PathName = "Path B",
            DamageMultiplier = 1.4f,
            AttackRateMultiplier = 0.8f,
            ManualAmmoOverride = -1f
        };

        return new WeaponInstance
        {
            Data = data,
            Level = level,
            SelectedPath = path,
            State = WeaponState.Manual
        };
    }

    private PlayerStats CreateStats(float projectileAreaSize = 1f)
    {
        GameObject owner = new("StatsOwner");
        _cleanup.Add(owner);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateStatDefinitions(projectileAreaSize));
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private AutomaticCannonWeapon CreateAutomaticCannonWeapon(int level, WeaponUpgradePath path, HeatManager heat = null, PlayerStats stats = null)
    {
        WeaponInstance instance = CreateWeaponInstance(level, path);
        instance.Data.WeaponType = WeaponType.AutomaticCannon;
        AutomaticCannonWeapon weapon = new(null, null, null);
        weapon.Setup(instance, null, stats, heat);
        return weapon;
    }

    private RocketLauncherWeapon CreateRocketLauncherWeapon(int level, WeaponUpgradePath path)
    {
        WeaponInstance instance = CreateWeaponInstance(level, path);
        instance.Data.WeaponType = WeaponType.RocketLauncher;
        instance.Data.EnsureSpecificTuningForCurrentType();
        RocketLauncherWeapon weapon = new(null, null, null);
        weapon.Setup(instance, null, null, null);
        return weapon;
    }

    private RotatingBladeWeapon CreateRotatingBladeWeapon(int level, WeaponUpgradePath path, Transform owner = null)
    {
        WeaponInstance instance = CreateWeaponInstance(level, path);
        instance.Data.WeaponType = WeaponType.RotatingBlade;
        instance.Data.EnsureSpecificTuningForCurrentType();
        RotatingBladeWeapon weapon = new(null, null, null);
        weapon.Setup(instance, owner, null, null);
        return weapon;
    }

    private static void NormalizeAttackRateMultipliers(WeaponData data)
    {
        for (int i = 0; i < data.LevelData.Count; i++)
            data.LevelData[i].AttackRateMultiplier = 1f;

        data.PathA.AttackRateMultiplier = 1f;
        data.PathB.AttackRateMultiplier = 1f;
    }

    private HeatManager CreateHeatManager()
    {
        GameObject owner = new("HeatOwner");
        _cleanup.Add(owner);
        HeatManager heat = owner.AddComponent<HeatManager>();
        return heat;
    }

    private MortarWeapon CreateMortarWeapon(HeatManager heat, PlayerStats stats)
    {
        WeaponInstance instance = CreateWeaponInstance(level: 1, WeaponUpgradePath.None);
        instance.Data.WeaponType = WeaponType.Mortar;
        instance.Data.EnsureSpecificTuningForCurrentType();
        MortarWeapon weapon = new(null, null, null);
        weapon.Setup(instance, null, stats, heat);
        return weapon;
    }

    private List<StatDefinition> CreateStatDefinitions(float projectileAreaSize = 1f)
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, projectileAreaSize),
            CreateDefinition(StatType.AbilityDamageMultiplier, 1f),
            CreateDefinition(StatType.AbilityCooldownReduction, 0f)
        };
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = FindPrivateMethod(target.GetType(), methodName, System.Array.Empty<object>());
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }

    private static T InvokePrivate<T>(object target, string methodName)
    {
        MethodInfo method = FindPrivateMethod(target.GetType(), methodName, System.Array.Empty<object>());
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        return (T)method.Invoke(target, null);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = FindPrivateMethod(target.GetType(), methodName, arguments);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        return (T)method.Invoke(target, arguments);
    }

    private static MethodInfo FindPrivateMethod(System.Type type, string methodName, object[] arguments)
    {
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != methodName)
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != arguments.Length)
                continue;

            bool matches = true;
            for (int j = 0; j < parameters.Length; j++)
            {
                if (arguments[j] == null)
                    continue;

                if (!parameters[j].ParameterType.IsInstanceOfType(arguments[j]))
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

    private static T ReadPrivate<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private sealed class CapturingTargeting : IWeaponTargeting
    {
        private readonly bool _returnTarget;

        public float LastRange { get; private set; } = -1f;

        public CapturingTargeting(bool returnTarget)
        {
            _returnTarget = returnTarget;
        }

        public bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target)
        {
            LastRange = range;
            target = null;
            return _returnTarget;
        }
    }
}
