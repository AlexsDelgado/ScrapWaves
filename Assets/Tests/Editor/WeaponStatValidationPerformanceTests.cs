using System;
using NUnit.Framework;

/// <summary>Allocation guard for the mutable validation path; does not assert machine-dependent latency.</summary>
public sealed class WeaponStatValidationPerformanceTests
{
    [TestCase(WeaponType.AutomaticCannon, false)]
    [TestCase(WeaponType.AutomaticCannon, true)]
    [TestCase(WeaponType.Flamethrower, false)]
    [TestCase(WeaponType.Flamethrower, true)]
    [TestCase(WeaponType.RocketLauncher, false)]
    [TestCase(WeaponType.RocketLauncher, true)]
    [TestCase(WeaponType.Mortar, false)]
    [TestCase(WeaponType.Mortar, true)]
    [TestCase(WeaponType.RotatingBlade, false)]
    [TestCase(WeaponType.RotatingBlade, true)]
    public void FifteenRows_ValidateWithoutSteadyStateAllocationAndObserveImmediateMutations(WeaponType type, bool authored)
    {
        var profile = new WeaponStatProfile();
        foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
        for (int level = path == WeaponUpgradePath.None ? 1 : 6; level <= (path == WeaponUpgradePath.None ? 5 : 10); level++)
        {
            var row = WeaponStatProfile.CreateRow(type, level, path);
            if (authored)
            {
                row.Automatic.Damage = Enabled(17f);
                row.Automatic.AttackCyclesPerSecond = Enabled(4f);
                row.Manual.Damage = Enabled(24f);
                row.Manual.AmmoCapacity = Enabled(30f);
                row.Ability.Damage = Enabled(35f);
                row.AbilityCooldownSeconds = Enabled(12f);
            }
            profile.Levels.Add(row);
        }

        for (int i = 0; i < 1000; i++)
            if (!profile.TryValidate(type, out string error)) throw new InvalidOperationException(error);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
            if (!profile.TryValidate(type, out string error)) throw new InvalidOperationException(error);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(bytes, Is.Zero, "New optional payload fields need a typed enabled reader; whole-profile validation must not allocate per stat lookup.");

        profile.Levels[0].Manual.Damage = Enabled(float.NaN);
        Assert.That(profile.TryValidate(type, out _), Is.False, "No stale success cache may hide direct mutable edits.");
        profile.Levels[0].Manual.Damage = Enabled(9f);
        Assert.That(profile.TryValidate(type, out string repairedError), Is.True, repairedError);
    }

    private static WeaponOptionalFloat Enabled(float value) => new() { Enabled = true, Value = value };
}
