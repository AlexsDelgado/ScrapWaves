using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class WeaponLegacyStatVisibilityTests
{
    private static readonly WeaponStatAuthoringMode[] Modes =
        { WeaponStatAuthoringMode.Automatic, WeaponStatAuthoringMode.Manual, WeaponStatAuthoringMode.Ability };

    private static bool Visible(WeaponType type, string field, int level, WeaponUpgradePath path,
        WeaponStatAuthoringMode mode) => WeaponStatInspectorPolicy.ShowsSpecificField(type, field, level, path, mode);

    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
    public void Cannon_HeadHunterHidesBurstCountHeatDamageAndBurstTiming(int level)
    {
        string[] fields = {
            nameof(AutomaticCannonTuning.CannonAutoBurstCount), nameof(AutomaticCannonTuning.CannonManualBurstCount),
            nameof(AutomaticCannonTuning.CannonAutoLineSpacing), nameof(AutomaticCannonTuning.CannonManualLineSpacing),
            nameof(AutomaticCannonTuning.CannonBurstShotInterval), nameof(AutomaticCannonTuning.CannonAutoAccuracySpreadDegrees),
            nameof(AutomaticCannonTuning.CannonBurstProjectileScatterDegrees),
            nameof(AutomaticCannonTuning.CannonHeatDamageThresholdStepPercent),
            nameof(AutomaticCannonTuning.CannonHeatDamageThresholdCount),
            nameof(AutomaticCannonTuning.CannonHeatDamageBonusPerThreshold)
        };
        foreach (string field in fields)
            foreach (WeaponStatAuthoringMode mode in Modes)
                Assert.That(Visible(WeaponType.AutomaticCannon, field, level, WeaponUpgradePath.PathB, mode), Is.False, field + "/" + mode);
        // These rate sources are still used by Head Hunter; a broad "hide all burst-named fields" rule is wrong.
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAutoBurstsPerSecond), level,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Automatic), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonManualBurstsPerSecond), level,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Manual), Is.True);
    }

    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
    public void Cannon_ContinuousKeepsInheritedCadenceCountSourcesButHidesWithinBurstDelay(int level)
    {
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAutoBurstCount), level,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Automatic), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonManualBurstCount), level,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Manual), Is.True);
        Assert.That(WeaponStatInspectorPolicy.SpecificFieldLabel(WeaponType.AutomaticCannon,
            nameof(AutomaticCannonTuning.CannonAutoBurstCount), level, WeaponUpgradePath.PathA,
            WeaponStatAuthoringMode.Automatic, "Auto Burst Count"), Does.Contain("cadence"));
        foreach (string field in new[] { nameof(AutomaticCannonTuning.CannonAutoLineSpacing),
            nameof(AutomaticCannonTuning.CannonManualLineSpacing), nameof(AutomaticCannonTuning.CannonBurstShotInterval) })
            foreach (WeaponStatAuthoringMode mode in Modes)
                Assert.That(Visible(WeaponType.AutomaticCannon, field, level, WeaponUpgradePath.PathA, mode), Is.False, field);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAutoAccuracySpreadDegrees), level,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Automatic), Is.True, "Single Continuous bullets still have accuracy spread");
    }

    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
    public void Cannon_AdvancedAbilitiesHideBasicCountButContinuousKeepsScatter(int level)
    {
        foreach (WeaponUpgradePath path in new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
            foreach (string field in new[] { nameof(AutomaticCannonTuning.CannonActiveBaseBulletCount),
                nameof(AutomaticCannonTuning.CannonActiveHeatBulletStepPercent) })
                Assert.That(Visible(WeaponType.AutomaticCannon, field, level, path, WeaponStatAuthoringMode.Ability), Is.False, field);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAbilityScatterRadius), level,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAbilityScatterRadius), level,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.False);
    }

    [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
    public void Cannon_BasicKeepsActualBurstAndAbilityControls(int level)
    {
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonAutoBurstCount), level,
            WeaponUpgradePath.None, WeaponStatAuthoringMode.Automatic), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonManualLineSpacing), level,
            WeaponUpgradePath.None, WeaponStatAuthoringMode.Manual), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonActiveHeatBulletStepPercent), level,
            WeaponUpgradePath.None, WeaponStatAuthoringMode.Ability), Is.True);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonBurstShotInterval), level,
            WeaponUpgradePath.None, WeaponStatAuthoringMode.Ability), Is.False);
    }

    [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
    public void Rocket_FragmentAbilityHidesLockOnlyControlsButKeepsChildLegacySources(int level)
    {
        foreach (string field in new[] {
            nameof(RocketLauncherTuning.RocketActiveBaseRocketCount), nameof(RocketLauncherTuning.RocketActiveInitialTargetCount),
            nameof(RocketLauncherTuning.RocketActiveTargetLockInterval), nameof(RocketLauncherTuning.RocketActiveConeAngle),
            nameof(RocketLauncherTuning.RocketActiveTargetMarkerRadius) })
        {
            Assert.That(Visible(WeaponType.RocketLauncher, field, level, WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.False, field);
            Assert.That(Visible(WeaponType.RocketLauncher, field, level, WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability), Is.True, field);
        }
        foreach (string field in new[] { nameof(RocketLauncherTuning.RocketManualExplosionRadius),
            nameof(RocketLauncherTuning.RocketManualExplosionFalloff), nameof(RocketLauncherTuning.RocketManualSpeedMultiplier) })
        {
            Assert.That(Visible(WeaponType.RocketLauncher, field, level, WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.True, field);
            Assert.That(Visible(WeaponType.RocketLauncher, field, level, WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability), Is.False, field);
            Assert.That(WeaponStatInspectorPolicy.SpecificFieldLabel(WeaponType.RocketLauncher, field, level,
                WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability, field), Does.Contain("child"));
        }
        Assert.That(Visible(WeaponType.RocketLauncher, nameof(RocketLauncherTuning.RocketActiveDamageScale), level,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.True);
        Assert.That(Visible(WeaponType.RocketLauncher, nameof(RocketLauncherTuning.RocketActiveSpeedMultiplier), level,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.True);
    }

    [Test]
    public void Flame_RetiredStreamDurationNeverAppearsAndBasicAutomaticDoesNotShowBurn()
    {
        foreach (var key in LegalKeys())
            foreach (WeaponStatAuthoringMode mode in Modes)
                foreach (string dormant in new[] { nameof(FlamethrowerTuning.FlameVisualDuration),
                    nameof(FlamethrowerTuning.FlameAutoConeAngle), nameof(FlamethrowerTuning.FlameManualConeAngle) })
                    Assert.That(Visible(WeaponType.Flamethrower, dormant, key.level, key.path, mode), Is.False, dormant);
        foreach (string field in new[] { nameof(FlamethrowerTuning.FlameBurnDuration),
            nameof(FlamethrowerTuning.FlameBurnTickInterval), nameof(FlamethrowerTuning.FlameBurnDamageScale) })
        {
            Assert.That(Visible(WeaponType.Flamethrower, field, 5, WeaponUpgradePath.None, WeaponStatAuthoringMode.Automatic), Is.False, field);
            Assert.That(Visible(WeaponType.Flamethrower, field, 5, WeaponUpgradePath.None, WeaponStatAuthoringMode.Manual), Is.True, field);
            Assert.That(Visible(WeaponType.Flamethrower, field, 6, WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Automatic), Is.True, field);
        }
        Assert.That(Visible(WeaponType.Flamethrower, nameof(FlamethrowerTuning.FlameBurnDuration), 6,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.True, "Inherited Nitrogen status guard is a real legacy dependency");
        Assert.That(Visible(WeaponType.Flamethrower, nameof(FlamethrowerTuning.FlameBurnTickInterval), 6,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.False);
    }

    [Test]
    public void Mortar_DropAbilityHidesArcAndOrdinaryGrapeshotHidesParentFalloff()
    {
        foreach (var key in LegalKeys())
        {
            Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarArcHeight), key.level, key.path,
                WeaponStatAuthoringMode.Ability), Is.False);
            Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarArcHeight), key.level, key.path,
                WeaponStatAuthoringMode.Manual), Is.True);
            foreach (WeaponStatAuthoringMode mode in Modes)
                Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarActiveShellCount), key.level, key.path, mode), Is.False,
                    "Unused legacy count remains quarantined; typed ActiveShellCount owns functioning control");
        }
        Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarExplosionFalloff), 6,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Automatic), Is.False);
        Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarExplosionFalloff), 6,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability), Is.True);
        Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarManualExplosionRadius), 6,
            WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Manual), Is.True, "Legacy geometry source still controls Grapeshot distribution/drop geometry");
    }

    [Test]
    public void Blade_ManualRangeSourceStaysVisibleForInheritedQButNotOrbit()
    {
        foreach (var key in LegalKeys())
        {
            Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.BladeManualRange), key.level,
                key.path, WeaponStatAuthoringMode.Automatic), Is.False);
            Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.BladeManualRange), key.level,
                key.path, WeaponStatAuthoringMode.Manual), Is.True);
            Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.BladeManualRange), key.level,
                key.path, WeaponStatAuthoringMode.Ability), Is.True);
        }
        Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.BladeVisualDuration), 6,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.False);
        Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.BladeVisualDuration), 6,
            WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Manual), Is.True);
    }

    [TestCase("ContinuousModeHeatSpeedBonus", "speed fraction / heat step")]
    [TestCase("HeadHunterPierceDamageLossPerTarget", "damage fraction / target")]
    [TestCase("FragmentConeRangePerExplosionRadius", "m per blast-radius m")]
    [TestCase("RotationsPerSecond", "rotations / s")]
    [TestCase("AtomicRangePerHitMultiplier", "base-range fraction / hit")]
    [TestCase("GrapeshotAbilityHeatProjectilesPerSecond", "extra shells / s at full heat")]
    public void PayloadUnitsDescribeConsumerDimensions(string field, string expectedUnit)
        => Assert.That(WeaponStatInspectorPolicy.SpecificOverrideUnit(field), Is.EqualTo(expectedUnit));

    [Test]
    public void FalloffLabelsDescribeLinearEdgeLossNotExponent()
    {
        string rocket = WeaponStatInspectorPolicy.SpecificFieldLabel(WeaponType.RocketLauncher,
            nameof(RocketLauncherTuning.RocketManualExplosionFalloff), 1, WeaponUpgradePath.None,
            WeaponStatAuthoringMode.Manual, "Manual Explosion Falloff");
        Assert.That(rocket, Does.Contain("edge damage loss"));
        Assert.That(rocket, Does.Not.Contain("exponent"));
        Assert.That(WeaponStatInspectorPolicy.SpecificFieldLabel(WeaponType.Mortar,
            nameof(MortarTuning.MortarExplosionFalloff), 1, WeaponUpgradePath.None,
            WeaponStatAuthoringMode.Manual, "Explosion Falloff"), Does.Contain("fraction 0-1"));
    }

    private static IEnumerable<(int level, WeaponUpgradePath path)> LegalKeys()
    {
        for (int level = 1; level <= 5; level++) yield return (level, WeaponUpgradePath.None);
        foreach (WeaponUpgradePath path in new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
            for (int level = 6; level <= 10; level++) yield return (level, path);
    }
}
