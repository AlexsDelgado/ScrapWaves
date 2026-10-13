using NUnit.Framework;
using System;
using System.Reflection;

public sealed class WeaponStatCapabilitiesTests
{
    [TestCase(WeaponType.AutomaticCannon, typeof(WeaponCannonStatOverrides))]
    [TestCase(WeaponType.Flamethrower, typeof(WeaponFlameStatOverrides))]
    [TestCase(WeaponType.RocketLauncher, typeof(WeaponRocketStatOverrides))]
    [TestCase(WeaponType.Mortar, typeof(WeaponMortarStatOverrides))]
    [TestCase(WeaponType.RotatingBlade, typeof(WeaponBladeStatOverrides))]
    public void EveryTypedControl_HasUnitsAndAtLeastOneAuthoringContext(WeaponType weapon, Type payload)
    {
        foreach (FieldInfo field in payload.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(WeaponOptionalFloat) && field.FieldType != typeof(WeaponOptionalInt)) continue;
            Assert.That(WeaponStatInspectorPolicy.SpecificOverrideUnit(field.Name), Is.Not.Null.And.Not.Empty,
                $"{weapon}/{field.Name}: editable values require explicit units");
            bool visible = false;
            foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
            foreach (WeaponStatMode mode in Enum.GetValues(typeof(WeaponStatMode)))
                visible |= WeaponStatCapabilities.SupportsSpecificField(weapon, path == WeaponUpgradePath.None ? 1 : 6, path, mode, field.Name);
            Assert.That(visible, Is.True, $"{weapon}/{field.Name}: typed controls must have an applicable authoring context");
        }
    }

    [TestCase(WeaponType.RocketLauncher, WeaponUpgradePath.PathB, WeaponStatMode.Ability, WeaponStatField.RangeMeters, false)]
    [TestCase(WeaponType.RocketLauncher, WeaponUpgradePath.PathA, WeaponStatMode.Ability, WeaponStatField.RangeMeters, true)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.PathA, WeaponStatMode.Automatic, WeaponStatField.ExplosionRadiusMeters, false)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.PathA, WeaponStatMode.Manual, WeaponStatField.ExplosionRadiusMeters, false)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.PathA, WeaponStatMode.Ability, WeaponStatField.ExplosionRadiusMeters, true)]
    [TestCase(WeaponType.RotatingBlade, WeaponUpgradePath.PathB, WeaponStatMode.Ability, WeaponStatField.KnockbackStrength, false)]
    [TestCase(WeaponType.Flamethrower, WeaponUpgradePath.PathA, WeaponStatMode.Automatic, WeaponStatField.KnockbackStrength, false)]
    public void OrdinaryControls_OnlyAppearWhereTheirConsumerExists(WeaponType type, WeaponUpgradePath path,
        WeaponStatMode mode, WeaponStatField field, bool expected)
    {
        for (int level = 6; level <= 10; level++)
            Assert.That(WeaponStatCapabilities.SupportsModeField(type, level, path, mode, field), Is.EqualTo(expected),
                $"{type}/{path}/{level}/{mode}/{field}");
    }

    [TestCase(WeaponType.RocketLauncher, "AbilityClusterExplosionRadiusMeters", WeaponUpgradePath.PathA, WeaponStatMode.Ability, false)]
    [TestCase(WeaponType.RocketLauncher, "AbilityClusterTravelRangeMeters", WeaponUpgradePath.PathB, WeaponStatMode.Ability, true)]
    [TestCase(WeaponType.RocketLauncher, "AbilityClusterLifetimeSeconds", WeaponUpgradePath.PathB, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.RocketLauncher, "KineticOrdinaryKnockbackMultiplier", WeaponUpgradePath.PathA, WeaponStatMode.Ability, false)]
    [TestCase(WeaponType.RocketLauncher, "FragmentAdditionalAutomaticRockets", WeaponUpgradePath.PathB, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.Mortar, "GrapeshotConeAngleDegrees", WeaponUpgradePath.PathA, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.Mortar, "GrapeshotAutomaticLandingSpreadMeters", WeaponUpgradePath.PathA, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.Mortar, "GrapeshotAutomaticLandingSpreadMeters", WeaponUpgradePath.PathA, WeaponStatMode.Automatic, true)]
    [TestCase(WeaponType.Mortar, "GrapeshotManualLandingSpreadMeters", WeaponUpgradePath.PathA, WeaponStatMode.Manual, true)]
    [TestCase(WeaponType.Flamethrower, "ContactPuddleRadiusMeters", WeaponUpgradePath.PathA, WeaponStatMode.Ability, true)]
    [TestCase(WeaponType.Flamethrower, "AbilityPuddleRadiusMeters", WeaponUpgradePath.PathA, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.Flamethrower, "NitrogenInitialSlowMultiplier", WeaponUpgradePath.PathB, WeaponStatMode.Ability, false)]
    [TestCase(WeaponType.Flamethrower, "AbilitySlowDurationPerFreezeSecond", WeaponUpgradePath.PathB, WeaponStatMode.Ability, true)]
    [TestCase(WeaponType.AutomaticCannon, "ManualProjectileLifetimeSeconds", WeaponUpgradePath.PathB, WeaponStatMode.Manual, false)]
    [TestCase(WeaponType.RotatingBlade, "ExtraBladeCount", WeaponUpgradePath.PathB, WeaponStatMode.Automatic, false)]
    [TestCase(WeaponType.RotatingBlade, "ThrustLengthMeters", WeaponUpgradePath.PathB, WeaponStatMode.Ability, false)]
    public void UniqueControls_DoNotLeakIntoUnrelatedMechanics(WeaponType type, string field,
        WeaponUpgradePath path, WeaponStatMode mode, bool expected)
    {
        for (int level = 6; level <= 10; level++)
            Assert.That(WeaponStatCapabilities.SupportsSpecificField(type, level, path, mode, field), Is.EqualTo(expected),
                $"{type}/{path}/{level}/{mode}/{field}");
        // Most controls are path-specific. Basic Cannon manual lifetime and Blade thrust remain applicable.
        for (int level = 1; level <= 5; level++)
        {
            Assert.That(WeaponStatCapabilities.SupportsSpecificField(type, level, WeaponUpgradePath.None, mode, field),
                Is.EqualTo(field == "ManualProjectileLifetimeSeconds" || field == "ThrustLengthMeters"),
                $"Basic {type}/{level}/{mode}/{field}");
        }
    }

    [Test]
    public void HiddenCriticalValues_SurviveMortarSwitchOffAndOn()
    {
        var row = WeaponStatProfile.CreateRow(WeaponType.Mortar, 6, WeaponUpgradePath.PathB);
        row.CriticalChance = new WeaponOptionalFloat { Enabled = true, Value = .4f };
        row.CriticalDamageMultiplier = new WeaponOptionalFloat { Enabled = true, Value = 3f };
        row.Mortar.EnableCriticalHits = false;
        Assert.That(WeaponStatInspectorPolicy.ShowsCriticalControls(WeaponType.Mortar, row.Mortar.EnableCriticalHits), Is.False);
        row.Mortar.EnableCriticalHits = true;
        Assert.That(WeaponStatInspectorPolicy.ShowsCriticalControls(WeaponType.Mortar, row.Mortar.EnableCriticalHits), Is.True);
        Assert.That(row.CriticalChance.Enabled && row.CriticalDamageMultiplier.Enabled, Is.True);
        Assert.That(row.CriticalChance.Value, Is.EqualTo(.4f));
        Assert.That(row.CriticalDamageMultiplier.Value, Is.EqualTo(3f));
    }
}
