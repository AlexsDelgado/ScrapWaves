using NUnit.Framework;
using UnityEngine;

public class MortarTrajectoryTests
{
    [Test]
    public void Evaluate_ReachesConfiguredTargetAtOne()
    {
        Vector3 start = new Vector3(0f, 1f, 0f);
        Vector3 target = new Vector3(0f, 1f, 10f);

        Assert.That(
            MortarTrajectory.Evaluate(start, target, 7f, 1f),
            Is.EqualTo(target));
    }

    [Test]
    public void Evaluate_ContinuesPastTargetAfterOne()
    {
        Vector3 point = MortarTrajectory.Evaluate(
            Vector3.zero,
            Vector3.forward * 10f,
            7f,
            2f);

        Assert.That(point.z, Is.EqualTo(20f).Within(0.0001f));
        Assert.That(point.y, Is.LessThan(0f));
    }

    [Test]
    public void GetMaximumNormalizedTime_MatchesShellFailsafe()
    {
        Assert.That(
            MortarTrajectory.GetMaximumNormalizedTime(0.5f),
            Is.EqualTo(10f).Within(0.0001f));
    }
}
