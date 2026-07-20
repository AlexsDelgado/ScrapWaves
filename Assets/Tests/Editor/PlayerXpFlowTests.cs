using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerXpFlowTests
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
    public void AddExperience_FirstLevelRequiresTenXpAndCarriesOverflow()
    {
        PlayerXP xp = CreatePlayerXp();
        List<int> levelUps = new();
        xp.OnLevelUp += levelUps.Add;

        xp.AddExperience(15);

        Assert.That(xp.CurrentLevel, Is.EqualTo(2));
        Assert.That(xp.XpTowardsNext, Is.EqualTo(5));
        Assert.That(xp.XpRequiredForCurrentLevel, Is.EqualTo(12));
        Assert.That(levelUps, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void AddExperience_UsesPreviousLevelCostMultiplierForMultiLevelGain()
    {
        PlayerXP xp = CreatePlayerXp();
        List<int> levelUps = new();
        xp.OnLevelUp += levelUps.Add;

        xp.AddExperience(22);

        Assert.That(xp.CurrentLevel, Is.EqualTo(3));
        Assert.That(xp.XpTowardsNext, Is.Zero);
        Assert.That(xp.XpRequiredForCurrentLevel, Is.EqualTo(15));
        Assert.That(levelUps, Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void AddExperience_StopsAtLevelCapAndClearsOverflow()
    {
        PlayerXP xp = CreatePlayerXp(levelCap: 3);
        List<int> levelUps = new();
        xp.OnLevelUp += levelUps.Add;

        xp.AddExperience(1000);

        Assert.That(xp.CurrentLevel, Is.EqualTo(3));
        Assert.That(xp.IsAtLevelCap, Is.True);
        Assert.That(xp.XpTowardsNext, Is.Zero);
        Assert.That(xp.XpRequiredForCurrentLevel, Is.Zero);
        Assert.That(xp.NormalizedProgressToNextLevel, Is.EqualTo(1f));
        Assert.That(levelUps, Is.EqualTo(new[] { 2, 3 }));
    }

    private PlayerXP CreatePlayerXp(int firstLevelRequirement = 10, float multiplier = 1.2f, int levelCap = 36)
    {
        GameObject owner = new("PlayerXpFlowTest");
        _cleanup.Add(owner);
        PlayerXP xp = owner.AddComponent<PlayerXP>();
        SetPrivateField(xp, "_firstLevelXpRequirement", firstLevelRequirement);
        SetPrivateField(xp, "_experienceCostMultiplier", multiplier);
        SetPrivateField(xp, "_levelCap", levelCap);
        InvokePrivate(xp, "Awake");
        return xp;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }
}
