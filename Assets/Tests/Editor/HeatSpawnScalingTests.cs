using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cubre el escalado de spawn por heat: el ratio lineal que alimenta la curva, los dos
/// multiplicadores, la supresión durante la fase de Overheat y la precedencia del profile.
/// </summary>
public class HeatSpawnScalingTests
{
    private HeatManager _manager;
    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("HeatSpawnScalingTest");
        _manager = _go.AddComponent<HeatManager>();

        // Dos tramos iguales: es la configuración de GameplayScene y la que hace que
        // el 80 % de la barra caiga exactamente en ratio 0.5.
        SetPrivateField(_manager, "_pointsToReachDisplay80", 100f);
        SetPrivateField(_manager, "_pointsFromDisplay80To100", 100f);
    }

    [TearDown]
    public void TearDown()
    {
        // HeatManager.OnEnable setea el static Instance; sin esto se filtra a otras suites.
        if (_go != null)
            Object.DestroyImmediate(_go);

        OverheatSwarmBoost.SetIntensity(false);
        OverheatSwarmBoost.ClearExitPressure();
    }

    [Test]
    public void HeatRatio_IsLinear_AndDivergesFromNormalizedHeatAtTheKnee()
    {
        _manager.SetHeat(100f);

        // 100 de 200 puntos: la barra visible marca 80 % pero el ratio lineal es 0.5.
        Assert.That(_manager.NormalizedHeat, Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(_manager.HeatRatio, Is.EqualTo(0.5f).Within(0.0001f));

        _manager.SetHeat(50f);
        Assert.That(_manager.HeatRatio, Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(_manager.NormalizedHeat, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void Multipliers_FollowTheCurveOverHeatRatio()
    {
        SetPrivateField(_manager, "_spawnScalingOverHeatRatio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(_manager, "_maxSpawnCountMultiplierAtFullHeat", 3f);
        SetPrivateField(_manager, "_spawnIntervalScaleAtFullHeat", 0.5f);

        _manager.SetHeat(0f);
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(1f).Within(0.0001f));

        _manager.SetHeat(100f); // ratio 0.5 → intensidad 0.5
        Assert.That(_manager.CurrentSpawnIntensity, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(0.75f).Within(0.0001f));

        _manager.SetHeat(200f); // ratio 1 → intensidad 1
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(3f).Within(0.0001f));
        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void SuppressedScaling_ReturnsNeutralMultipliers()
    {
        SetPrivateField(_manager, "_spawnScalingOverHeatRatio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(_manager, "_maxSpawnCountMultiplierAtFullHeat", 3f);
        SetPrivateField(_manager, "_spawnIntervalScaleAtFullHeat", 0.5f);
        _manager.SetHeat(200f);

        _manager.SetSpawnScalingSuppressed(true);

        Assert.That(_manager.IsSpawnScalingSuppressed, Is.True);
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(1f).Within(0.0001f));

        // La intensidad sigue reflejando el heat real; lo que se neutraliza son los multiplicadores.
        Assert.That(_manager.CurrentSpawnIntensity, Is.EqualTo(1f).Within(0.0001f));

        _manager.SetSpawnScalingSuppressed(false);
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(3f).Within(0.0001f));
    }

    [Test]
    public void EmptyCurve_FallsBackToNeutralMultipliersWithoutThrowing()
    {
        SetPrivateField(_manager, "_spawnScalingOverHeatRatio", new AnimationCurve());
        SetPrivateField(_manager, "_maxSpawnCountMultiplierAtFullHeat", 3f);
        _manager.SetHeat(200f);

        Assert.That(_manager.CurrentSpawnIntensity, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_manager.GetSpawnIntervalScale(), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void AssignedProfile_TakesPrecedenceOverFallbackFields()
    {
        SetPrivateField(_manager, "_spawnScalingOverHeatRatio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(_manager, "_maxSpawnCountMultiplierAtFullHeat", 3f);

        SpawnBalanceProfile profile = ScriptableObject.CreateInstance<SpawnBalanceProfile>();
        SetPrivateField(profile, "_pointsToReachDisplay80", 100f);
        SetPrivateField(profile, "_pointsFromDisplay80To100", 100f);
        SetPrivateField(profile, "_spawnScalingOverHeatRatio", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        SetPrivateField(profile, "_maxSpawnCountMultiplierAtFullHeat", 5f);

        _manager.SetHeat(200f);
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(3f).Within(0.0001f), "sin profile debe usar el fallback");

        _manager.SetProfile(profile);
        Assert.That(_manager.GetSpawnCountMultiplier(), Is.EqualTo(5f).Within(0.0001f), "con profile debe mandar el profile");

        Object.DestroyImmediate(profile);
    }

    [Test]
    public void ResetHeatProgress_ClearsSuppression()
    {
        _manager.SetSpawnScalingSuppressed(true);
        _manager.ResetHeatProgressAndEscalation();

        Assert.That(_manager.IsSpawnScalingSuppressed, Is.False);
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"no existe el campo {fieldName}");
        field.SetValue(instance, value);
    }
}
