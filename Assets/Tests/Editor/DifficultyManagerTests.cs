using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DifficultyManagerTests
{
    [Test]
    public void CurrentIntensity_TraversesCurveAtConfiguredRampSpeed()
    {
        GameObject go = new("DifficultyManagerTest");
        DifficultyManager manager = go.AddComponent<DifficultyManager>();
        AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 20f, 1f);

        SetPrivateField(manager, "_intensityOverMinutesAfterStart", curve);
        SetPrivateField(manager, "_difficultyRampSpeedMultiplier", 3.5f);
        SetPrivateField(manager, "_scalingStartDelaySeconds", Time.timeSinceLevelLoad - 120f);

        Assert.That(manager.CurrentIntensity, Is.EqualTo(0.35f).Within(0.001f));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void GetEnemyHealthMultiplier_ReachesConfiguredTenTimesHealthAtFullIntensity()
    {
        GameObject go = new("DifficultyManagerHealthTest");
        DifficultyManager manager = go.AddComponent<DifficultyManager>();

        SetPrivateField(manager, "_intensityOverMinutesAfterStart", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(manager, "_difficultyRampSpeedMultiplier", 1f);
        SetPrivateField(manager, "_scalingStartDelaySeconds", Time.timeSinceLevelLoad - 120f);
        SetPrivateField(manager, "_maxEnemyHealthMultiplier", 10f);

        Assert.That(manager.GetEnemyHealthMultiplier(), Is.EqualTo(10f).Within(0.001f));

        Object.DestroyImmediate(go);
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }
}
