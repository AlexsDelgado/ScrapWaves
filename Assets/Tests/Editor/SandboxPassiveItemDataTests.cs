using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SandboxPassiveItemDataTests
{
    private const string PassiveFolder = "Assets/ScriptableObjects/PlayerSO/Passives";
    private const string PlayerPrefabPath = "Assets/Prefabs/player.prefab";
    private const string SandboxScenePath = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";

    [Test]
    public void BasePassiveCatalog_HasExpectedSlotsLevelsAndUniqueIds()
    {
        List<PassiveItemData> items = LoadBasePassives();

        Assert.That(items, Has.Count.EqualTo(17));
        Assert.That(items.Select(item => item.UnlockId).Distinct().Count(), Is.EqualTo(items.Count));
        Assert.That(items.Count(item => item.Slot == PassiveItemSlot.Head), Is.EqualTo(4));
        Assert.That(items.Count(item => item.Slot == PassiveItemSlot.Core), Is.EqualTo(4));
        Assert.That(items.Count(item => item.Slot == PassiveItemSlot.Arm), Is.EqualTo(5));
        Assert.That(items.Count(item => item.Slot == PassiveItemSlot.Leg), Is.EqualTo(4));

        foreach (PassiveItemData item in items)
        {
            Assert.That(item, Is.Not.Null);
            Assert.That(item.MaxLevel, Is.EqualTo(6), item.name);
            Assert.That(item.BonusesPerLevel, Is.Not.Empty, item.name);
            Assert.That(item.DisplayName, Does.Not.Contain("Rusted").IgnoreCase, item.name);
            Assert.That(item.DisplayName, Does.Not.Contain("Pure").IgnoreCase, item.name);

            foreach (PassiveStatBonus bonus in item.BonusesPerLevel)
            {
                Assert.That(bonus.ValuesPerLevel, Is.Not.Null, $"{item.name}: {bonus.StatType}");
                Assert.That(bonus.ValuesPerLevel, Has.Length.EqualTo(item.MaxLevel), $"{item.name}: {bonus.StatType}");
            }
        }
    }

    [Test]
    public void ProductionPlayerPool_MatchesTheSeventeenBaseAssets()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(playerPrefab, Is.Not.Null);

        PassiveItemLevelUpHandler handler = playerPrefab.GetComponent<PassiveItemLevelUpHandler>();
        Assert.That(handler, Is.Not.Null);

        List<PassiveItemData> expected = LoadBasePassives();
        Assert.That(handler.ItemPool, Has.Count.EqualTo(expected.Count));
        CollectionAssert.AreEquivalent(expected, handler.ItemPool);
    }

    [Test]
    public void ProductionPlayer_DefinesEveryStatUsedByBasePassives()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(playerPrefab, Is.Not.Null);

        PlayerStats stats = playerPrefab.GetComponent<PlayerStats>();
        Assert.That(stats, Is.Not.Null);
        HashSet<StatType> configuredStats = stats.GetAllDefinitions()
            .Where(definition => definition != null)
            .Select(definition => definition.StatType)
            .ToHashSet();

        foreach (PassiveItemData item in LoadBasePassives())
        {
            foreach (PassiveStatBonus bonus in item.BonusesPerLevel)
                Assert.That(configuredStats, Does.Contain(bonus.StatType), $"{item.name}: {bonus.StatType}");
        }
    }

    [Test]
    public void WeaponTestingSandbox_ContainsPassiveTestingController()
    {
        string sceneText = System.IO.File.ReadAllText(SandboxScenePath);

        Assert.That(sceneText, Does.Contain("Assembly-CSharp::PassiveItemTestingController"));
    }

    private static List<PassiveItemData> LoadBasePassives()
    {
        return AssetDatabase.FindAssets("t:PassiveItemData", new[] { PassiveFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<PassiveItemData>)
            .Where(item => item != null)
            .OrderBy(item => item.name)
            .ToList();
    }
}
