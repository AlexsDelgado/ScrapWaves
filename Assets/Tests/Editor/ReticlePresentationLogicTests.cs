using NUnit.Framework;

public class ReticlePresentationLogicTests
{
    [TestCase(WeaponType.AutomaticCannon, false, ReticleMode.CircleDot)]
    [TestCase(WeaponType.RocketLauncher, false, ReticleMode.CircleDot)]
    [TestCase(WeaponType.RocketLauncher, true, ReticleMode.RocketLock)]
    [TestCase(WeaponType.Mortar, false, ReticleMode.Mortar)]
    [TestCase(WeaponType.Flamethrower, false, ReticleMode.WideBrackets)]
    [TestCase(WeaponType.RotatingBlade, false, ReticleMode.WideBrackets)]
    public void ResolveMode_ReturnsExpectedMode(
        WeaponType weaponType,
        bool rocketCharging,
        ReticleMode expected)
    {
        Assert.That(
            ReticlePresentationLogic.ResolveMode(weaponType, rocketCharging),
            Is.EqualTo(expected));
    }

    [TestCase(5, 5, 10, 0f)]
    [TestCase(7, 5, 10, 0.4f)]
    [TestCase(10, 5, 10, 1f)]
    [TestCase(5, 5, 5, 0f)]
    public void GetRocketLockProgress_NormalizesActualLocks(
        int current,
        int initial,
        int maximum,
        float expected)
    {
        Assert.That(
            ReticlePresentationLogic.GetRocketLockProgress(current, initial, maximum),
            Is.EqualTo(expected).Within(0.0001f));
    }
}
