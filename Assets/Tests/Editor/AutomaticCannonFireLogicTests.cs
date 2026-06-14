using NUnit.Framework;
using UnityEngine;

public class AutomaticCannonFireLogicTests
{
    [Test]
    public void GetManualBurstInterval_UsesConfiguredBaseVolleyRate()
    {
        Assert.That(
            AutomaticCannonFireLogic.GetManualBurstInterval(2f, 1f, 1f),
            Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void GetManualBurstInterval_ScalesWithAttackSpeedAndWeaponRate()
    {
        Assert.That(
            AutomaticCannonFireLogic.GetManualBurstInterval(2f, 2f, 1.25f),
            Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void ApplyProjectileScatter_WithZeroSpreadPreservesDirection()
    {
        Vector3 direction = new Vector3(1f, 0.5f, 2f);

        Vector3 result = AutomaticCannonFireLogic.ApplyProjectileScatter(
            direction,
            0f,
            Vector2.one);

        Assert.That(result, Is.EqualTo(direction.normalized));
    }

    [Test]
    public void ApplyProjectileScatter_HorizontalSampleChangesYawWithinSpread()
    {
        Vector3 result = AutomaticCannonFireLogic.ApplyProjectileScatter(
            Vector3.forward,
            1.5f,
            Vector2.right);

        Assert.That(Mathf.Abs(result.x), Is.GreaterThan(0.001f));
        Assert.That(Mathf.Abs(result.y), Is.LessThan(0.0001f));
        Assert.That(Vector3.Angle(Vector3.forward, result), Is.EqualTo(1.5f).Within(0.001f));
    }

    [Test]
    public void ApplyProjectileScatter_VerticalSampleChangesPitchWithinSpread()
    {
        Vector3 result = AutomaticCannonFireLogic.ApplyProjectileScatter(
            Vector3.forward,
            1.5f,
            Vector2.up);

        Assert.That(Mathf.Abs(result.y), Is.GreaterThan(0.001f));
        Assert.That(Mathf.Abs(result.x), Is.LessThan(0.0001f));
        Assert.That(Vector3.Angle(Vector3.forward, result), Is.EqualTo(1.5f).Within(0.001f));
    }
}
