using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class DamageApplicationResultTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }

        _cleanup.Clear();
    }

    [Test]
    public void EnemyHealth_DirectDamageReportsRequestedModifiedAndExactHealthDelta()
    {
        EnemyHealth health = CreateEnemyHealth(100);
        health.gameObject.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);

        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 10,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);

        Assert.That(result.IsAuthoritative, Is.True);
        Assert.That(result.Applied, Is.True);
        Assert.That(result.Blocked, Is.False);
        Assert.That(result.Killed, Is.False);
        Assert.That(result.RequestedDamage, Is.EqualTo(10));
        Assert.That(result.ModifiedDamage, Is.EqualTo(15));
        Assert.That(result.AppliedDamage, Is.EqualTo(15));
        Assert.That(result.HealthBefore, Is.EqualTo(100));
        Assert.That(result.HealthAfter, Is.EqualTo(85));
        Assert.That(health.CurrentHealth, Is.EqualTo(85));
    }

    [Test]
    public void EnemyHealth_OverkillReportsRemainingHealthAndAuthoritativeKill()
    {
        EnemyHealth health = CreateEnemyHealth(3);

        DamageApplicationResult killingResult = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 100,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);
        DamageApplicationResult deadTargetResult = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 10,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);

        Assert.That(killingResult.IsAuthoritative, Is.True);
        Assert.That(killingResult.Applied, Is.True);
        Assert.That(killingResult.Killed, Is.True);
        Assert.That(killingResult.AppliedDamage, Is.EqualTo(3));
        Assert.That(killingResult.HealthBefore, Is.EqualTo(3));
        Assert.That(killingResult.HealthAfter, Is.Zero);

        Assert.That(deadTargetResult.IsAuthoritative, Is.True);
        Assert.That(deadTargetResult.Applied, Is.False);
        Assert.That(deadTargetResult.Blocked, Is.False);
        Assert.That(deadTargetResult.Killed, Is.False);
        Assert.That(deadTargetResult.AppliedDamage, Is.Zero);
        Assert.That(deadTargetResult.HealthBefore, Is.Zero);
        Assert.That(deadTargetResult.HealthAfter, Is.Zero);
    }

    [Test]
    public void EnemyHealth_DirectAndStatusChannelsUseDifferentInvincibilityRules()
    {
        EnemyHealth health = CreateEnemyHealth(100);
        health.SetInvincible(true);

        DamageApplicationResult direct = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 10,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);
        DamageApplicationResult status = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 10,
            channel: DamageChannel.Status,
            statusKind: WeaponStatusKind.JellifiedBurn,
            canTriggerLifesteal: false);

        health.SetInvincible(true, blockDot: true);
        DamageApplicationResult blockedStatus = WeaponDamageApplier.ApplyDamage(
            health,
            requestedDamage: 10,
            channel: DamageChannel.Status,
            statusKind: WeaponStatusKind.Burn,
            canTriggerLifesteal: false);

        Assert.That(direct.IsAuthoritative, Is.True);
        Assert.That(direct.Applied, Is.False);
        Assert.That(direct.Blocked, Is.True);
        Assert.That(direct.AppliedDamage, Is.Zero);
        Assert.That(direct.HealthBefore, Is.EqualTo(100));
        Assert.That(direct.HealthAfter, Is.EqualTo(100));

        Assert.That(status.IsAuthoritative, Is.True);
        Assert.That(status.Applied, Is.True);
        Assert.That(status.Blocked, Is.False);
        Assert.That(status.AppliedDamage, Is.EqualTo(10));
        Assert.That(status.HealthBefore, Is.EqualTo(100));
        Assert.That(status.HealthAfter, Is.EqualTo(90));

        Assert.That(blockedStatus.IsAuthoritative, Is.True);
        Assert.That(blockedStatus.Applied, Is.False);
        Assert.That(blockedStatus.Blocked, Is.True);
        Assert.That(blockedStatus.AppliedDamage, Is.Zero);
        Assert.That(blockedStatus.HealthBefore, Is.EqualTo(90));
        Assert.That(blockedStatus.HealthAfter, Is.EqualTo(90));
    }

    [Test]
    public void LegacyDamageable_RemainsCompatibleButMarksResultNonAuthoritative()
    {
        GameObject target = Track(new GameObject("Legacy Damageable"));
        LegacyDamageable legacy = target.AddComponent<LegacyDamageable>();

        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            legacy,
            requestedDamage: 7,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);

        Assert.That(legacy.LastDamage, Is.EqualTo(7));
        Assert.That(result.IsAuthoritative, Is.False);
        Assert.That(result.Applied, Is.True);
        Assert.That(result.AppliedDamage, Is.EqualTo(7));
        Assert.That(result.Killed, Is.False);
    }

    [Test]
    public void WeaponDamageApplier_LifestealUsesAppliedDamageInsteadOfOverkillRequest()
    {
        GameObject player = Track(new GameObject("Applied Damage Lifesteal Player"));
        PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();
        PlayerStats stats = player.AddComponent<PlayerStats>();
        List<StatDefinition> definitions = new()
        {
            CreateStatDefinition(StatType.MaxHealth, 100f, StatCategory.Defensive),
            CreateStatDefinition(StatType.DamageResistance, 0f, StatCategory.Defensive),
            CreateStatDefinition(StatType.Lifesteal, 0.5f, StatCategory.Defensive)
        };
        SetPrivateField(stats, "_statDefinitions", definitions);
        InvokePrivate(stats, "Awake");
        playerHealth.FullHeal();
        playerHealth.TakeDamage(50);

        EnemyHealth enemy = CreateEnemyHealth(4);
        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            enemy,
            requestedDamage: 40,
            channel: DamageChannel.Direct);

        Assert.That(result.AppliedDamage, Is.EqualTo(4));
        Assert.That(result.Killed, Is.True);
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(52));
    }

    [Test]
    public void DestroyerWeakPoint_ReportsExactOverkillBeforeDeactivation()
    {
        GameObject target = Track(new GameObject("Destroyer Weak Point Result Target"));
        target.SetActive(false);
        DestroyerMouthWeakPoint weakPoint = target.AddComponent<DestroyerMouthWeakPoint>();
        target.SetActive(true);

        DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
            weakPoint,
            requestedDamage: 100,
            channel: DamageChannel.Direct,
            canTriggerLifesteal: false);

        Assert.That(result.IsAuthoritative, Is.True);
        Assert.That(result.AppliedDamage, Is.EqualTo(80));
        Assert.That(result.HealthBefore, Is.EqualTo(80));
        Assert.That(result.HealthAfter, Is.Zero);
        Assert.That(result.Killed, Is.True);
        Assert.That(target.activeSelf, Is.False);
    }

    private EnemyHealth CreateEnemyHealth(int maxHealth)
    {
        GameObject target = Track(new GameObject("Authoritative Damage Target"));
        EnemyHealth health = target.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(maxHealth);
        return health;
    }

    private StatDefinition CreateStatDefinition(StatType type, float baseValue, StatCategory category)
    {
        StatDefinition definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", category);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", false);
        return definition;
    }

    private T Track<T>(T value) where T : Object
    {
        _cleanup.Add(value);
        return value;
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

    private sealed class LegacyDamageable : MonoBehaviour, IDamageable
    {
        public int LastDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            LastDamage = amount;
            return amount > 0;
        }
    }
}
