using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Escalado permanente de spawn según cuántos Overheats ya terminaron.
/// Se apila encima del heat actual y no se apaga cuando ese escalado está suprimido.
/// </summary>
public class OverheatCycleSpawnScalingTests
{
    private HeatManager _manager;
    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("OverheatCycleSpawnScalingTest");
        _manager = _go.AddComponent<HeatManager>();
        SetPrivateField(_manager, "_completedCycleIntervalStep", 0.1f);
        SetPrivateField(_manager, "_completedCycleIntervalFloor", 0.3f);
        SetPrivateField(_manager, "_completedCycleBatchStep", 0.1f);
        SetPrivateField(_manager, "_completedCycleBatchCeiling", 2f);
        SetPrivateField(_manager, "_completedCycleHealthStep", 0.1f);
        SetPrivateField(_manager, "_completedCycleHealthCeiling", 2f);
    }

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            Object.DestroyImmediate(_go);

        OverheatSwarmBoost.SetIntensity(false);
        OverheatSwarmBoost.ClearExitPressure();
    }

    [Test]
    public void ZeroCycles_ReturnsNeutralScales()
    {
        Assert.That(_manager.CompletedOverheatCycles, Is.EqualTo(0));
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void FirstThreeCycles_StepIntervalDownAndBatchAndHealthUp()
    {
        _manager.ApplyEscalationAfterOverheat();
        Assert.That(_manager.CompletedOverheatCycles, Is.EqualTo(1));
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.9f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(1.1f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(1.1f).Within(0.0001f));

        _manager.ApplyEscalationAfterOverheat();
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(1.2f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(1.2f).Within(0.0001f));

        _manager.ApplyEscalationAfterOverheat();
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(1.3f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(1.3f).Within(0.0001f));
    }

    [Test]
    public void IntervalFloorAndBatchHealthCeilings_ClampPastTheLinearRun()
    {
        SetPrivateField(_manager, "_completedOverheatCycles", 8);
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.3f).Within(0.0001f));

        SetPrivateField(_manager, "_completedOverheatCycles", 11);
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void SuppressedHeatScaling_DoesNotNeutralizeCycleScales()
    {
        SetPrivateField(_manager, "_spawnScalingOverHeatRatio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(_manager, "_pointsToReachDisplay80", 100f);
        SetPrivateField(_manager, "_pointsFromDisplay80To100", 100f);
        SetPrivateField(_manager, "_spawnIntervalScaleAtFullHeat", 0.5f);
        _manager.SetHeat(200f);
        _manager.ApplyEscalationAfterOverheat();
        _manager.ApplyEscalationAfterOverheat();
        _manager.SetSpawnScalingSuppressed(true);

        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleBatchScale(), Is.EqualTo(1.2f).Within(0.0001f));
        Assert.That(_manager.GetCompletedCycleHealthScale(), Is.EqualTo(1.2f).Within(0.0001f));
    }

    [Test]
    public void ResetHeatProgress_ClearsCompletedCycles()
    {
        _manager.ApplyEscalationAfterOverheat();
        _manager.ApplyEscalationAfterOverheat();

        _manager.ResetHeatProgressAndEscalation();

        Assert.That(_manager.CompletedOverheatCycles, Is.EqualTo(0));
        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void AssignedProfile_TakesPrecedenceOverFallbackSteps()
    {
        SpawnBalanceProfile profile = ScriptableObject.CreateInstance<SpawnBalanceProfile>();
        SetPrivateField(profile, "_completedCycleIntervalStep", 0.2f);
        SetPrivateField(profile, "_completedCycleIntervalFloor", 0.3f);
        _manager.SetProfile(profile);
        _manager.ApplyEscalationAfterOverheat();

        Assert.That(_manager.GetCompletedCycleIntervalScale(), Is.EqualTo(0.8f).Within(0.0001f));

        Object.DestroyImmediate(profile);
    }

    [Test]
    public void ApplySpawnModifiers_MultipliesPrefabHealthByCompletedCycles_WhenTimeScaleIsNeutral()
    {
        _manager.ApplyEscalationAfterOverheat();
        _manager.ApplyEscalationAfterOverheat();

        GameObject difficultyGo = new GameObject("DifficultyForCycleHealth");
        DifficultyManager difficulty = difficultyGo.AddComponent<DifficultyManager>();
        SetPrivateField(difficulty, "_scalingStartDelaySeconds", Time.timeSinceLevelLoad + 10000f);
        SetPrivateField(difficulty, "_scaleEnemyHealth", true);

        GameObject enemyGo = new GameObject("CycleHealthEnemy");
        EnemyHealth health = enemyGo.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(100);

        Assert.That(difficulty.GetEnemyHealthMultiplier(), Is.EqualTo(1f).Within(0.0001f));
        difficulty.ApplySpawnModifiers(enemyGo);

        Assert.That(health.MaxHealth, Is.EqualTo(120));

        Object.DestroyImmediate(enemyGo);
        Object.DestroyImmediate(difficultyGo);
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"no existe el campo {fieldName}");
        field.SetValue(instance, value);
    }
}
