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

    [Test]
    public void AutomaticCannon_HasPathSpecificManualAmmoOverrides()
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset");

        Assert.That(data, Is.Not.Null);
        Assert.That(data.PathA.ManualAmmoOverride, Is.EqualTo(400f).Within(0.0001f));
        Assert.That(data.PathB.ManualAmmoOverride, Is.EqualTo(40f).Within(0.0001f));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset", "Continuous Fire", "Head Hunter", 400f, 40f)]
    [TestCase("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset", "Kinetic Explosion", "Fragmentation Cap", -1f, -1f)]
    [TestCase("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset", "Jellified Fuel", "Liquid Nitrogen", -1f, -1f)]
    [TestCase("Assets/ScriptableObjects/WeaponSO/Mortar.asset", "Grapeshot", "Multi-Charged Shells", -1f, -1f)]
    [TestCase("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset", "Multi-Blade", "Atomic Sharpness", -1f, -1f)]
    public void ProductionWeapon_HasConfiguredPathNamesAndOverrides(
        string path,
        string expectedPathA,
        string expectedPathB,
        float expectedAmmoA,
        float expectedAmmoB)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.PathA.PathName, Is.EqualTo(expectedPathA));
        Assert.That(data.PathB.PathName, Is.EqualTo(expectedPathB));
        Assert.That(data.PathA.ManualAmmoOverride, Is.EqualTo(expectedAmmoA).Within(0.0001f));
        Assert.That(data.PathB.ManualAmmoOverride, Is.EqualTo(expectedAmmoB).Within(0.0001f));
    }
}
