using NUnit.Framework;
using UnityEditor;

public class SandboxWeaponUpgradeDataTests
{
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset", "Continuous Fire", "Head Hunter")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_RocketLauncher.asset", "Kinetic Explosion", "Fragmentation Cap")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset", "Jellified Fuel", "Liquid Nitrogen")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset", "Grapeshot", "Multi-Charged Shells")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_RotatingBlade.asset", "Multi-Blade", "Atomic Sharpness")]
    public void SandboxWeapon_HasLevelAndPathData(string path, string expectedPathA, string expectedPathB)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.LevelData, Has.Count.EqualTo(10));
        Assert.That(data.LevelData[0].Level, Is.EqualTo(1));
        Assert.That(data.LevelData[9].Level, Is.EqualTo(10));
        Assert.That(data.PathA, Is.Not.Null);
        Assert.That(data.PathB, Is.Not.Null);
        Assert.That(data.PathA.PathName, Is.EqualTo(expectedPathA));
        Assert.That(data.PathB.PathName, Is.EqualTo(expectedPathB));
        Assert.That(data.PathA.DamageMultiplier, Is.GreaterThan(1f));
        Assert.That(data.PathB.DamageMultiplier, Is.GreaterThan(1f));
    }
}
