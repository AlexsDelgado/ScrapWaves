using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class EnemyReactionTests
{
    [SetUp]
    public void EnableReactions()
    {
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = true,
            ReducedFlash = false,
            Quality = GameFeelQualityLevel.High
        });
    }

    [Test]
    public void EnemyReactionProfile_ResolvesLightHeavyCriticalWeakPointAndKillTiers()
    {
        EnemyReactionProfile profile = ScriptableObject.CreateInstance<EnemyReactionProfile>();
        try
        {
            Assert.That(profile.ResolveTier(Context(damage: 1), 100), Is.EqualTo(EnemyReactionTier.Light));
            Assert.That(profile.ResolveTier(Context(damage: 20), 100), Is.EqualTo(EnemyReactionTier.Heavy));
            Assert.That(profile.ResolveTier(Context(damage: 1, critical: true), 100), Is.EqualTo(EnemyReactionTier.Critical));
            Assert.That(profile.ResolveTier(Context(damage: 1, weakPoint: true), 100), Is.EqualTo(EnemyReactionTier.WeakPoint));
            Assert.That(profile.ResolveTier(Context(damage: 100, kill: true), 100), Is.EqualTo(EnemyReactionTier.Kill));
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void StatusFeedback_UsesJellifiedBurnInsteadOfRegularBurn()
    {
        GameObject target = new("Status Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.JellifiedBurn, 3f);

            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();
            Assert.That(feedback, Is.Not.Null);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.JellifiedBurn));

            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.JellifiedBurn));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void StatusFeedback_KeepsSlowWhileFreezeTemporarilyTakesPriority()
    {
        GameObject target = new("Frozen Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Slow, 5f, 0.7f);
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Freeze, 2f);
            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();

            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.Slow | WeaponStatusMask.Freeze));
            EnemyStatusFeedback.Remove(target.transform, WeaponStatusKind.Freeze);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.Slow));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void AuthoredReactionProfile_IsAvailableThroughResources()
    {
        EnemyReactionProfile profile = AssetDatabase.LoadAssetAtPath<EnemyReactionProfile>(
            "Assets/GameFeel/Resources/EnemyReactionProfile.asset");
        Assert.That(profile, Is.Not.Null);
    }

    [Test]
    public void SlowStatus_StaysOnNestedEnemyInsteadOfLeakingToSceneParent()
    {
        GameObject container = new("Enemy Container");
        GameObject enemy = new("Nested Enemy");
        enemy.transform.SetParent(container.transform);
        enemy.AddComponent<EnemyHealth>();
        try
        {
            WeaponMovementSlowStatus.Apply(enemy.transform, 0.5f, 3f, "Slow");

            Assert.That(enemy.GetComponent<WeaponMovementSlowStatus>(), Is.Not.Null);
            Assert.That(container.GetComponent<WeaponMovementSlowStatus>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(container);
        }
    }

    [Test]
    public void DisablingEnemyReactions_ClearsCosmeticStatusStateOnly()
    {
        GameObject target = new("Disabled Reaction Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();

            EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions { EnemyReactionEnabled = false });

            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.None));
        }
        finally
        {
            Object.DestroyImmediate(target);
            EnableReactions();
        }
    }

    private static WeaponFeedbackContext Context(int damage, bool critical = false, bool weakPoint = false, bool kill = false)
    {
        return new WeaponFeedbackContext(
            weapon: null,
            mode: WeaponFeedbackMode.Automatic,
            normalizedHeat: 0f,
            origin: Vector3.zero,
            direction: Vector3.forward,
            damageAmount: damage,
            isCritical: critical,
            isWeakPoint: weakPoint,
            isKill: kill);
    }
}
