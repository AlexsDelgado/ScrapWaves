using System.IO;
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
        if (data.WeaponType == WeaponType.RotatingBlade)
        {
            Assert.That(data.PathA.DamageMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(data.PathB.DamageMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(data.PathA.AttackRateMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(data.PathB.AttackRateMultiplier, Is.EqualTo(1f).Within(0.0001f));
        }
        else
        {
            Assert.That(data.PathA.DamageMultiplier, Is.GreaterThan(1f));
            Assert.That(data.PathB.DamageMultiplier, Is.GreaterThan(1f));
        }
    }

    [Test]
    public void AutomaticCannon_HasPathSpecificManualAmmoOverrides()
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset");

        Assert.That(data, Is.Not.Null);
        Assert.That(data.PathA.ManualAmmoOverride, Is.EqualTo(400f).Within(0.0001f));
        Assert.That(data.PathB.ManualAmmoOverride, Is.EqualTo(40f).Within(0.0001f));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset")]
    public void AutomaticCannon_ManualProjectileThroughputIsNotBelowAutomatic(string path)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        AutomaticCannonTuning tuning = data.AutomaticCannon;
        float automaticProjectilesPerSecond = data.BaseAttackRate * tuning.CannonAutoBurstCount;
        float manualProjectilesPerSecond = tuning.CannonManualBurstsPerSecond * tuning.CannonManualBurstCount;
        Assert.That(manualProjectilesPerSecond + 0.0001f, Is.GreaterThanOrEqualTo(automaticProjectilesPerSecond));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/Mortar.asset")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset")]
    public void Mortar_ActiveAbilityAmmoCostMatchesSpec(string path)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.ActiveAbilityAmmoCost, Is.EqualTo(5f).Within(0.0001f));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/Mortar.asset")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset")]
    public void Mortar_ManualHeatSpeedBonusMatchesSpec(string path)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.Mortar.MortarHeatManualSpeedBonus, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void DashCharges_BaseValueMatchesSpec()
    {
        StatDefinition definition = AssetDatabase.LoadAssetAtPath<StatDefinition>("Assets/ScriptableObjects/PlayerSO/Stats/DashCharges.asset");

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.BaseValue, Is.Zero);
    }

    [Test]
    public void WeaponTestingSandbox_DebugVisualizationsStartDisabled()
    {
        string sceneText = File.ReadAllText("Assets/Scenes/WeaponTestingSandbox.unity");
        string[] disabledFields =
        {
            "ShowRuntimeVisuals: 0",
            "ShowTargetingCone: 0",
            "ShowProjectilePaths: 0",
            "ShowExplosionRadius: 0",
            "ShowDamageNumbers: 0",
            "ShowKnockbackVectors: 0",
            "ShowWeaponHitboxes: 0",
            "ShowStatusEffectIcons: 0",
            "ShowDpsWindow: 0"
        };

        foreach (string field in disabledFields)
            Assert.That(sceneText, Does.Contain(field));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset", "Continuous Fire", "Head Hunter", -1f, -1f)]
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
        Assert.That(data.PathA.LevelData, Has.Count.EqualTo(5));
        Assert.That(data.PathB.LevelData, Has.Count.EqualTo(5));
    }
}
