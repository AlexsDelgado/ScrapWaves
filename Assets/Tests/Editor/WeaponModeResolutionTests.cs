using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Shared replacement, action snapshot, and schema-boundary regression checks.</summary>
public sealed class WeaponModeResolutionTests
{
    private readonly List<Object> _cleanup = new();
    private UnityEngine.Random.State _random;

    [SetUp] public void SetUp() => _random = UnityEngine.Random.state;
    [TearDown] public void TearDown()
    {
        foreach (Transform target in _consumerRegistered) if (target != null) EnemyRegistry.Unregister(target);
        _consumerRegistered.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        UnityEngine.Random.state = _random;
        if (_consumerTrackerInstalled) typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _consumerPreviousTracker);
        _consumerTrackerInstalled = false;
        _consumerFixtureIndex = 0;
    }

    [Test]
    public void AuthoredDamage_ReplacesNeutralLevelPathAndModeFactorsBeforePlayerAndDynamicScaling()
    {
        var instance = Instance(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        instance.Data.BaseDamage = 10f;
        instance.Data.PathA.DamageMultiplier = 3f;
        instance.Data.PathA.LevelData = new List<WeaponLevelData> { new() { Level = 6, DamageMultiplier = 2f } };
        var row = AddRow(instance);
        row.Ability.Damage = Enabled(13f);
        PlayerStats stats = Stats(damage: 2f, ability: 3f);
        var authored = Context(stats, instance, WeaponStatMode.Ability, dynamicScale: .5f, weaponScale: 4f);
        Assert.That(authored.EstimateDamage(false), Is.EqualTo(39));
        Assert.That(authored.LevelMultiplier, Is.EqualTo(1f));
        Assert.That(authored.PathMultiplier, Is.EqualTo(1f));
        Assert.That(authored.WithScales(2f, 1f).EstimateDamage(false), Is.EqualTo(78));
        row.Ability.Damage.Enabled = false;
        var inherited = Context(stats, instance, WeaponStatMode.Ability, dynamicScale: .5f, weaponScale: 4f);
        Assert.That(inherited.EstimateDamage(false), Is.EqualTo(720));
        Assert.That(authored.EstimateDamage(false), Is.EqualTo(39), "A launched action retains its authored neutral damage.");
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void CapturedModeAndAuthoredKnockback_SurviveModePathLevelAndProfileChanges(WeaponStatMode mode)
    {
        var instance = Instance(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        var row = AddRow(instance);
        var values = WeaponStatAuthoringResolver.GetModeValues(row, mode);
        values.Damage = Enabled(16f);
        values.KnockbackStrength = Enabled(3f);
        var context = Context(Stats(), instance, mode);
        instance.State = WeaponState.Manual;
        instance.Level = 10;
        instance.SelectedPath = WeaponUpgradePath.PathB;
        instance.Data.BaseDamage = 1000f;
        instance.Data.BaseKnockback = 1000f;
        values.Damage = Enabled(999f);
        values.KnockbackStrength = Enabled(999f);
        Assert.That(context.Mode, Is.EqualTo(mode));
        Assert.That(context.EstimateDamage(false), Is.EqualTo(16));
        Assert.That(context.CalculateKnockback(16), Is.EqualTo(6f));
        var copy = context.WithScales(.5f, .5f);
        Assert.That(copy.Mode, Is.EqualTo(mode));
        Assert.That(copy.EstimateDamage(false), Is.EqualTo(8));
        Assert.That(copy.CalculateKnockback(16), Is.EqualTo(3f));
    }

    [Test]
    public void DisabledKnockback_PreservesImpactTimeLegacyStrengthSampling()
    {
        var instance = Instance(WeaponType.AutomaticCannon);
        instance.Data.BaseKnockback = 2f;
        var context = Context(Stats(), instance, WeaponStatMode.Automatic);
        Assert.That(context.KnockbackStrengthIsAuthored, Is.False);
        Assert.That(context.CalculateKnockback(16), Is.EqualTo(4f));
        instance.Data.BaseKnockback = 5f;
        Assert.That(context.CalculateKnockback(16), Is.EqualTo(10f));
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void NoCritChannel_WithAuthoredChance_DoesNotConsumeRandomSample(WeaponStatMode mode)
    {
        var instance = Instance(WeaponType.AutomaticCannon);
        var row = AddRow(instance);
        row.CriticalChance = Enabled(1f);
        row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        var state = UnityEngine.Random.state;
        var context = Context(Stats(), instance, mode);
        Assert.That(context.IsCritical, Is.False);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(state));
    }

    [Test]
    public void MortarCriticalOptIn_PreservesHiddenValuesAndSnapshotsOffChannelWithoutRandomness()
    {
        var instance = Instance(WeaponType.Mortar);
        var row = AddRow(instance);
        row.CriticalChance = Enabled(1f);
        row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        row.CriticalDamageMultiplier = Enabled(3f);
        Assert.That(instance.Data.StatProfile.TryValidate(out var error), Is.True, error);
        var stats = Stats();
        var state = UnityEngine.Random.state;
        var off = Context(stats, instance, WeaponStatMode.Ability, canCrit: true);
        Assert.That(off.CanCrit, Is.False);
        Assert.That(off.IsCritical, Is.False);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(state));
        row.Mortar.EnableCriticalHits = true;
        var on = Context(stats, instance, WeaponStatMode.Ability, canCrit: true);
        Assert.That(on.IsCritical, Is.True);
        Assert.That(on.EstimateDamage(false), Is.EqualTo(off.EstimateDamage(false) * 6));
        Assert.That(off.IsCritical, Is.False);
        row.Mortar.EnableCriticalHits = false;
        Assert.That(row.CriticalChance.Value, Is.EqualTo(1f));
        Assert.That(row.CriticalDamageMultiplier.Value, Is.EqualTo(3f));
    }

    [Test]
    public void AuthoredAmmo_ReplacesLevelFactorButPreservesPathPlayerScalingEligibility()
    {
        var instance = Instance(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        instance.Data.PathA.ManualAmmoOverride = -1f;
        instance.Data.PathA.LevelData = new List<WeaponLevelData> { new() { Level = 6, ManualAmmoMultiplier = 7f } };
        var row = AddRow(instance);
        row.Manual.AmmoCapacity = Enabled(23f);
        Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(46f));
        instance.Data.PathA.ManualAmmoOverride = 100f;
        Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(23f));
        row.Manual.AmmoCapacity.Value = 0f;
        Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.Zero);
    }

    [Test]
    public void SpecificPathValidation_RejectsInertCurrentPayloadButPreservesForeignPayload()
    {
        var instance = Instance(WeaponType.AutomaticCannon);
        var row = AddRow(instance);
        row.Cannon.ContinuousAbilityBulletsPerSecond = Enabled(10f);
        Assert.That(instance.Data.StatProfile.TryValidate(out var error), Is.False);
        StringAssert.Contains("ContinuousAbilityBulletsPerSecond", error);
        row.Cannon.ContinuousAbilityBulletsPerSecond.Enabled = false;
        row.Blade.AtomicDashRangeMeters = Enabled(float.NaN);
        Assert.That(instance.Data.StatProfile.TryValidate(out error), Is.True, error);
    }

    [Test]
    public void SecondaryDamageChannel_CanBypassAuthoredDirectDamageWithoutLosingItsCapturedMode()
    {
        var instance = Instance(WeaponType.Flamethrower);
        var row = AddRow(instance);
        row.Manual.Damage = Enabled(42f);
        var stats = Stats();
        var direct = Context(stats, instance, WeaponStatMode.Manual);
        var burn = new WeaponDamageContext(stats, instance, false, 1f, .5f, false, 0f,
            mode: WeaponStatMode.Manual, useModeDamageOverride: false);
        Assert.That(direct.EstimateDamage(false), Is.EqualTo(42));
        Assert.That(burn.EstimateDamage(false), Is.EqualTo(5));
        Assert.That(burn.HasAuthoredDamage, Is.False);
        Assert.That(burn.Mode, Is.EqualTo(WeaponStatMode.Manual));
    }

    [Test]
    public void FreshRows_DoNotShareModeOrTypedPayloadInstances()
    {
        var first = WeaponStatProfile.CreateRow(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        var second = WeaponStatProfile.CreateRow(WeaponType.AutomaticCannon, 7, WeaponUpgradePath.PathA);
        first.Manual.Damage = Enabled(42f);
        first.Cannon.ContinuousAbilityBulletsPerSecond = Enabled(12f);
        Assert.That(second.Manual.Damage.Enabled, Is.False);
        Assert.That(first.Automatic.Damage.Enabled, Is.False);
        Assert.That(second.Cannon.ContinuousAbilityBulletsPerSecond.Enabled, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReportedFactors_RetainActionProvenanceAfterSwitchUpgradeAndContextCopies(bool authored)
    {
        var instance = Instance(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        var row = AddRow(instance);
        row.Manual.Damage = new WeaponOptionalFloat { Enabled = authored, Value = 17f };
        var context = Context(Stats(), instance, WeaponStatMode.Manual)
            .WithScales(.5f, 1f).WithFeedbackMetadata(7, DamageFeedbackKind.Direct);
        instance.Level = 10;
        instance.SelectedPath = WeaponUpgradePath.PathB;
        instance.State = WeaponState.Automatic;
        instance.Data.WeaponType = WeaponType.RocketLauncher;
        WeaponDamageRoll captured = default;
        void Capture(WeaponDamageRoll roll) => captured = roll;
        WeaponDamageResolver.OnDamageResolved += Capture;
        try { context.CalculateDamageValue(false); }
        finally { WeaponDamageResolver.OnDamageResolved -= Capture; }
        Assert.That(context.SourceWeaponType, Is.EqualTo(WeaponType.AutomaticCannon));
        Assert.That(context.SourceLevel, Is.EqualTo(6));
        Assert.That(context.SourcePath, Is.EqualTo(WeaponUpgradePath.PathA));
        Assert.That(captured.Factors.SourceWeaponType, Is.EqualTo(context.SourceWeaponType));
        Assert.That(captured.Factors.SourceLevel, Is.EqualTo(6));
        Assert.That(captured.Factors.SourcePath, Is.EqualTo(WeaponUpgradePath.PathA));
        Assert.That(captured.Factors.Mode, Is.EqualTo(WeaponStatMode.Manual));
        Assert.That(captured.Factors.HasAuthoredDamage, Is.EqualTo(authored));
        Assert.That(captured.Factors.Product, Is.EqualTo(captured.FinalDamage).Within(.0001f));
    }

    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    [TestCase(WeaponType.RotatingBlade)]
    public void AbilityAmmoCost_ReplacesEffectiveLegacyAndPreservesExactKeyAndCapacityIsolation(WeaponType type)
    {
        var instance = Instance(type, 6, WeaponUpgradePath.PathA);
        instance.Data.ActiveAbilityAmmoCost = 19f;
        var row = AddRow(instance);
        row.Manual.AmmoCapacity = Enabled(50f);
        float legacy = type == WeaponType.AutomaticCannon ? 80f : 19f;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.EqualTo(legacy));
        row.AbilityAmmoCost = Enabled(7f);
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.EqualTo(7f));
        Assert.That(WeaponStatAuthoringResolver.ResolveAbilityAmmoCost(instance).IsAuthored, Is.True);
        Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 1f), Is.EqualTo(50f));
        row.AbilityAmmoCost.Value = 0f;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.Zero);
        instance.SelectedPath = WeaponUpgradePath.PathB;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.EqualTo(19f));
        instance.SelectedPath = WeaponUpgradePath.PathA;
        instance.Level = 7;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.EqualTo(legacy));
        instance.Level = 6;
        row.AbilityAmmoCost.Enabled = false;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(instance), Is.EqualTo(legacy));
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void AbilityAmmoCost_RejectsInvalidEnabledValuesButPreservesDisabledStorage(float value)
    {
        var instance = Instance(WeaponType.AutomaticCannon);
        var row = AddRow(instance);
        row.AbilityAmmoCost = Enabled(value);
        Assert.That(instance.Data.StatProfile.TryValidate(out var error), Is.False);
        StringAssert.Contains("AbilityAmmoCost", error);
        row.AbilityAmmoCost.Enabled = false;
        Assert.That(instance.Data.StatProfile.TryValidate(out error), Is.True, error);
    }
    [Test]
    public void InvalidEligibleAbilityProfile_FailsBeforeAmmoOrCooldownChanges()
    {
        var instance = Instance(WeaponType.AutomaticCannon);
        instance.State = WeaponState.Manual;
        instance.CurrentAmmo = 40f;
        instance.AbilityCooldownTimer = 0f;
        AddRow(instance).Ability.Damage = Enabled(float.NaN);
        var behaviour = new BasicProjectileWeapon(null, null, null);
        behaviour.Setup(instance, null, Stats(), null);
        Assert.Throws<InvalidOperationException>(() => behaviour.UseActiveAbility(Vector3.forward));
        Assert.That(instance.CurrentAmmo, Is.EqualTo(40f));
        Assert.That(instance.AbilityCooldownTimer, Is.Zero);
    }
    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void CriticalComposition_ChangesActualRollAndDamageWithTheSameSampleAndPlayerSeverity(WeaponStatMode mode)
    {
        UnityEngine.Random.InitState(19037);
        var before = UnityEngine.Random.state;
        float sample = UnityEngine.Random.value;
        var after = UnityEngine.Random.state;
        Assert.That(sample, Is.InRange(.0001f, .9999f));
        var stats = Stats(chance: sample * .75f);
        var instance = Instance(WeaponType.AutomaticCannon);
        var row = AddRow(instance);
        row.CriticalChance = Enabled(sample * .5f);
        row.CriticalDamageMultiplier = Enabled(3f);
        row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        UnityEngine.Random.state = before;
        var replacement = Context(stats, instance, mode, canCrit: true);
        Assert.That(replacement.IsCritical, Is.False);
        Assert.That(replacement.CalculateDamageValue(false, report: false), Is.EqualTo(10f));
        Assert.That(UnityEngine.Random.state, Is.EqualTo(after));

        row.CriticalChanceComposition = WeaponCriticalChanceComposition.AddToPlayer;
        UnityEngine.Random.state = before;
        var addition = Context(stats, instance, mode, canCrit: true);
        Assert.That(addition.IsCritical, Is.True);
        Assert.That(addition.CritMultiplier, Is.EqualTo(6f), "The authored factor3 composes with player severity2.");
        Assert.That(addition.CalculateDamageValue(false, report: false), Is.EqualTo(60f));
        Assert.That(UnityEngine.Random.state, Is.EqualTo(after));

        row.CriticalChance.Enabled = false;
        UnityEngine.Random.state = before;
        var inherited = Context(stats, instance, mode, canCrit: true);
        Assert.That(inherited.IsCritical, Is.False);
        Assert.That(inherited.CalculateDamageValue(false, report: false), Is.EqualTo(10f));
        Assert.That(addition.IsCritical, Is.True, "Composition changes cannot rewrite a fired action.");
        Assert.That(replacement.IsCritical, Is.False);
    }
    [TestCase("base", WeaponUpgradePath.None)]
    [TestCase("level", WeaponUpgradePath.None)]
    [TestCase("base", WeaponUpgradePath.PathA)]
    [TestCase("level", WeaponUpgradePath.PathA)]
    [TestCase("path", WeaponUpgradePath.PathA)]
    [TestCase("base", WeaponUpgradePath.PathB)]
    [TestCase("level", WeaponUpgradePath.PathB)]
    [TestCase("path", WeaponUpgradePath.PathB)]
    public void InheritedDamageControls_ChangeActualCannonImpactAfterPlayerComposition(string field, WeaponUpgradePath path)
    {
        var f = ConsumerFixture(WeaponType.AutomaticCannon, path);
        var target = ConsumerTarget(f, 4f);
        WeaponLevelData selected = SelectedConsumerLevel(f.Runtime);
        WeaponUpgradePathData selectedPath = path == WeaponUpgradePath.PathB ? f.Runtime.Data.PathB : f.Runtime.Data.PathA;
        foreach (float value in new[] { 1f, 3f })
        {
            if (field == "base") f.Runtime.Data.BaseDamage = value;
            else if (field == "level") selected.DamageMultiplier = value;
            else selectedPath.DamageMultiplier = value;
            int expected = Mathf.RoundToInt(f.Runtime.Data.BaseDamage * selected.DamageMultiplier
                * (path == WeaponUpgradePath.None ? 1f : selectedPath.DamageMultiplier) * 2f);
            Assert.That(FireConsumerCannonHit(f, target), Is.EqualTo(expected), field + " must reach actual health dispatch.");
        }
    }

    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void InheritedDamageTransition_UsesSelectedLevelSixInsteadOfAccumulatingLevelFive(WeaponUpgradePath path)
    {
        var f = ConsumerFixture(WeaponType.AutomaticCannon, WeaponUpgradePath.None);
        var target = ConsumerTarget(f, 4f);
        f.Runtime.Data.LevelData[0].DamageMultiplier = 7f;
        Assert.That(FireConsumerCannonHit(f, target), Is.EqualTo(140));
        f.Runtime.Level = 6;
        f.Runtime.SelectedPath = path;
        Assert.That(FireConsumerCannonHit(f, target), Is.EqualTo(120),
            "Level6 path row2 and path factor3 replace level5 factor7; base-level6 sentinel19 is also ignored.");
        f.Runtime.Level = 5;
        f.Runtime.SelectedPath = WeaponUpgradePath.None;
        Assert.That(FireConsumerCannonHit(f, target), Is.EqualTo(140));
    }

    [TestCase("base", WeaponUpgradePath.None)]
    [TestCase("level", WeaponUpgradePath.None)]
    [TestCase("base", WeaponUpgradePath.PathA)]
    [TestCase("level", WeaponUpgradePath.PathA)]
    [TestCase("path", WeaponUpgradePath.PathA)]
    [TestCase("base", WeaponUpgradePath.PathB)]
    [TestCase("level", WeaponUpgradePath.PathB)]
    [TestCase("path", WeaponUpgradePath.PathB)]
    public void InheritedCadenceControls_ChangeActualRocketEmissionBoundary(string field, WeaponUpgradePath path)
    {
        var f = ConsumerFixture(WeaponType.RocketLauncher, path);
        WeaponLevelData selected = SelectedConsumerLevel(f.Runtime);
        WeaponUpgradePathData selectedPath = path == WeaponUpgradePath.PathB ? f.Runtime.Data.PathB : f.Runtime.Data.PathA;
        foreach (float value in new[] { 1f, 4f })
        {
            if (field == "base") f.Runtime.Data.BaseAttackRate = value;
            else if (field == "level") selected.AttackRateMultiplier = value;
            else selectedPath.AttackRateMultiplier = value;
            float interval = 1f / (f.Runtime.Data.BaseAttackRate * selected.AttackRateMultiplier
                * (path == WeaponUpgradePath.None ? 1f : selectedPath.AttackRateMultiplier) * 2f);
            var weapon = new RocketLauncherWeapon(null, f.Pool, f.Owner);
            weapon.Setup(f.Runtime, f.Owner, f.Stats, null);
            int before = f.Pool.ActiveLeasedCount;
            weapon.TickManual(0f, Vector3.forward, true);
            Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(before + 1));
            weapon.TickManual(interval * .9f, Vector3.forward, true);
            Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(before + 1), "Cannot emit before composed cadence boundary.");
            weapon.TickManual(interval * .2f, Vector3.forward, true);
            Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(before + 2), "Must emit once the composed boundary is crossed.");
        }
    }

    [TestCase("base", WeaponUpgradePath.None)]
    [TestCase("level", WeaponUpgradePath.None)]
    [TestCase("base", WeaponUpgradePath.PathA)]
    [TestCase("level", WeaponUpgradePath.PathA)]
    [TestCase("path", WeaponUpgradePath.PathA)]
    [TestCase("base", WeaponUpgradePath.PathB)]
    [TestCase("level", WeaponUpgradePath.PathB)]
    [TestCase("path", WeaponUpgradePath.PathB)]
    public void InheritedAmmoControls_ChangeManagerRefillAndActualShotExhaustion(string field, WeaponUpgradePath path)
    {
        var f = ConsumerFixture(WeaponType.RocketLauncher, path);
        f.Stats.gameObject.SetActive(false);
        var manager = f.Stats.gameObject.AddComponent<WeaponManager>();
        Set(manager, "_stats", f.Stats);
        Set(manager, "_projectilePool", f.Pool);
        Set(manager, "_projectileSpawn", f.Owner);
        Assert.That(manager.AddWeapon(f.Runtime.Data), Is.True);
        var runtime = manager.GetCurrentManualWeapon();
        runtime.Level = f.Runtime.Level;
        runtime.SelectedPath = path;
        WeaponLevelData selected = SelectedConsumerLevel(runtime);
        WeaponUpgradePathData selectedPath = path == WeaponUpgradePath.PathB ? runtime.Data.PathB : runtime.Data.PathA;
        IWeaponBehaviour weapon = manager.GetCurrentManualBehaviour();
        foreach (float value in new[] { 1f, 3f })
        {
            if (field == "base") runtime.Data.BaseManualAmmo = value;
            else if (field == "level") selected.ManualAmmoMultiplier = value;
            else selectedPath.ManualAmmoOverride = value;
            float expected = field == "path" ? value : runtime.Data.BaseManualAmmo * selected.ManualAmmoMultiplier * 2f;
            runtime.CurrentAmmo = 0f;
            manager.RefillManualAmmoAndResetActiveCooldown();
            Assert.That(runtime.CurrentAmmo, Is.EqualTo(expected),
                "The selected legacy path override bypasses both level and player capacity factors.");
            int before = f.Pool.ActiveLeasedCount;
            for (int shot = 0; shot < expected; shot++) weapon.TickManual(100f, Vector3.forward, true);
            Assert.That(runtime.CurrentAmmo, Is.Zero);
            Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(before + (int)expected));
            weapon.TickManual(100f, Vector3.forward, true);
            Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(before + (int)expected), "Empty ammo must prevent another actual projectile.");
            manager.RefillManualAmmoAndResetActiveCooldown();
            Assert.That(runtime.CurrentAmmo, Is.EqualTo(expected), "Repeated refill preserves the same effective capacity.");
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void InheritedBaseRange_ChangesCannonAcquisitionWithoutChangingManualReach(WeaponUpgradePath path)
    {
        var f = ConsumerFixture(WeaponType.AutomaticCannon, path);
        var target = ConsumerTarget(f, 4f);
        f.Runtime.State = WeaponState.Automatic;
        foreach (float range in new[] { 2f, 6f, 2f })
        {
            f.Runtime.Data.BaseRange = range;
            var weapon = new AutomaticCannonWeapon(new ConfiguredEnemyTargeting(), f.Pool, f.Owner);
            weapon.Setup(f.Runtime, f.Owner, f.Stats, null);
            int before = f.Pool.ActiveLeasedCount;
            weapon.TickAutomatic(0f, Vector3.forward);
            Assert.That(f.Pool.ActiveLeasedCount - before, Is.EqualTo(range > 4f ? 1 : 0));
            foreach (Projectile projectile in f.Container.GetComponentsInChildren<Projectile>())
                if (projectile.gameObject.activeSelf)
                    Assert.That((bool)ConsumerCall(projectile, "TryConsumeSweptWorldCollision", f.Owner.position, Vector3.forward * 5f), Is.True);
        }
        f.Runtime.State = WeaponState.Manual;
        Assert.That(FireConsumerCannonHit(f, target), Is.GreaterThan(0),
            "Legacy Cannon manual reach remains independent from automatic acquisition range.");
    }

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(3f)]
    public void InheritedBaseKnockback_ChangesActualTargetDisplacementAtImpact(float strength)
    {
        var f = ConsumerFixture(WeaponType.AutomaticCannon, WeaponUpgradePath.None);
        f.Runtime.Data.BaseDamage = 8f;
        SelectedConsumerLevel(f.Runtime).DamageMultiplier = 1f;
        var target = ConsumerTarget(f, 4f);
        var receiver = target.gameObject.AddComponent<EnemyKnockbackReceiver>();
        f.Runtime.Data.BaseKnockback = strength;
        Assert.That(FireConsumerCannonHit(f, target), Is.EqualTo(16));
        Vector3 displacement = receiver.ConsumeDisplacement(.1f);
        Assert.That(displacement.z, Is.EqualTo(strength * 2f * 2f * .1f).Within(.0001f));
        Assert.That(displacement.x, Is.Zero.Within(.0001f));
        Assert.That(displacement.y, Is.Zero.Within(.0001f));
    }

    private readonly List<Transform> _consumerRegistered = new();
    private ChallengeProgressTracker _consumerPreviousTracker;
    private bool _consumerTrackerInstalled;
    private int _consumerFixtureIndex;
    private sealed class ConsumerHarness
    {
        public WeaponInstance Runtime;
        public PlayerStats Stats;
        public Transform Owner;
        public Transform Container;
        public ProjectilePool Pool;
    }
    private sealed class ConsumerDamageTarget : MonoBehaviour, IDamageable
    {
        public int TotalDamage;
        public bool ApplyDamage(int amount) { TotalDamage += amount; return true; }
    }
    private ConsumerHarness ConsumerFixture(WeaponType type, WeaponUpgradePath path)
    {
        if (!_consumerTrackerInstalled)
        {
            _consumerPreviousTracker = ChallengeProgressTracker.Instance;
            var tracker = Track(new GameObject("Shared proof damage tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
            _consumerTrackerInstalled = true;
        }
        var stats = Stats(damage: 2f, speed: 2f, ammo: 2f, knockback: 2f);
        stats.transform.position = new Vector3(12000f + 100f * _consumerFixtureIndex++, 12000f, 12000f);
        var instance = Instance(type, path == WeaponUpgradePath.None ? 5 : 6, path);
        instance.State = WeaponState.Manual;
        instance.CurrentAmmo = 1000f;
        instance.Data.BaseRange = 8f;
        instance.Data.BaseAttackRate = 2f;
        instance.Data.BaseManualAmmo = 4f;
        instance.Data.BaseKnockback = 0f;
        instance.Data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 5, DamageMultiplier = 2f, AttackRateMultiplier = 2f, ManualAmmoMultiplier = 2f },
            new() { Level = 6, DamageMultiplier = 19f, AttackRateMultiplier = 19f, ManualAmmoMultiplier = 19f }
        };
        foreach (WeaponUpgradePathData data in new[] { instance.Data.PathA, instance.Data.PathB })
        {
            data.DamageMultiplier = 3f;
            data.AttackRateMultiplier = 3f;
            data.ManualAmmoOverride = -1f;
            data.LevelData = new List<WeaponLevelData> { new()
                { Level = 6, DamageMultiplier = 2f, AttackRateMultiplier = 2f, ManualAmmoMultiplier = 2f } };
        }
        if (type == WeaponType.AutomaticCannon)
        {
            instance.Data.AutomaticCannon.CannonManualBurstCount = 1;
            instance.Data.AutomaticCannon.CannonAutoBurstCount = 1;
            instance.Data.AutomaticCannon.CannonBurstProjectileScatterDegrees = 0f;
            instance.Data.AutomaticCannon.CannonAutoAccuracySpreadDegrees = 0f;
        }
        var prefab = Track(new GameObject("Shared proof projectile prefab"));
        prefab.SetActive(false);
        prefab.AddComponent<Rigidbody>();
        prefab.AddComponent<SphereCollider>().radius = .04f;
        ConsumerCall(prefab.AddComponent<Projectile>(), "Awake");
        var holder = Track(new GameObject("Shared proof pool"));
        holder.SetActive(false);
        var pool = holder.AddComponent<ProjectilePool>();
        var container = Track(new GameObject("Shared proof projectiles")).transform;
        Set(pool, "_projectilePrefab", prefab);
        Set(pool, "_container", container);
        Set(pool, "_initialPoolSize", 1);
        Set(pool, "_maxPoolSize", 128);
        ConsumerCall(pool, "Awake");
        return new ConsumerHarness { Runtime = instance, Stats = stats, Owner = stats.transform, Pool = pool, Container = container };
    }
    private ConsumerDamageTarget ConsumerTarget(ConsumerHarness f, float distance)
    {
        var go = Track(new GameObject("Shared proof target"));
        go.transform.position = f.Owner.position + Vector3.forward * distance;
        go.AddComponent<SphereCollider>().radius = .1f;
        var target = go.AddComponent<ConsumerDamageTarget>();
        EnemyRegistry.Register(go.transform);
        _consumerRegistered.Add(go.transform);
        Physics.SyncTransforms();
        return target;
    }
    private int FireConsumerCannonHit(ConsumerHarness f, ConsumerDamageTarget target)
    {
        var weapon = new AutomaticCannonWeapon(null, f.Pool, f.Owner);
        weapon.Setup(f.Runtime, f.Owner, f.Stats, null);
        int before = target.TotalDamage;
        weapon.TickManual(0f, Vector3.forward, true);
        if (f.Runtime.SelectedPath == WeaponUpgradePath.PathB)
        {
            ConsumerCall(weapon, "TickHeadHunterPendingImpacts", 100f);
            foreach (Projectile visual in f.Container.GetComponentsInChildren<Projectile>())
                if (visual.gameObject.activeSelf) ConsumerCall(visual, "DespawnOrDestroy");
        }
        else
        {
            Projectile[] active = f.Container.GetComponentsInChildren<Projectile>();
            Assert.That(active.Length, Is.EqualTo(1), "A one-round action should create one live projectile.");
            Assert.That((bool)ConsumerCall(active[0], "TryConsumeSweptWorldCollision", f.Owner.position, Vector3.forward * 5f), Is.True);
        }
        return target.TotalDamage - before;
    }
    private static WeaponLevelData SelectedConsumerLevel(WeaponInstance instance) => instance.SelectedPath switch
    {
        WeaponUpgradePath.PathA => instance.Data.PathA.LevelData[0],
        WeaponUpgradePath.PathB => instance.Data.PathB.LevelData[0],
        _ => instance.Data.LevelData[0]
    };
    private static object ConsumerCall(object target, string name, params object[] arguments)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method != null) return method.Invoke(target, arguments);
        }
        throw new MissingMethodException(name);
    }

    private static WeaponDamageContext Context(PlayerStats stats, WeaponInstance instance, WeaponStatMode mode,
        float dynamicScale = 1f, float weaponScale = 1f, bool canCrit = false) => new(stats, instance,
        canCrit, 1f, dynamicScale, mode == WeaponStatMode.Ability, 1f, mode: mode, weaponDamageScale: weaponScale);

    private WeaponInstance Instance(WeaponType type, int level = 1, WeaponUpgradePath path = WeaponUpgradePath.None)
    {
        var data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.BaseDamage = 10f;
        data.EnsureSpecificTuningForCurrentType();
        // CreateInstance does not materialize legacy path payloads; real tuned assets do.
        data.PathA ??= new WeaponUpgradePathData();
        data.PathB ??= new WeaponUpgradePathData();
        return new WeaponInstance { Data = data, Level = level, SelectedPath = path };
    }

    private static WeaponStatLevelRow AddRow(WeaponInstance instance)
    {
        var row = WeaponStatProfile.CreateRow(instance.Data.WeaponType, instance.Level, instance.SelectedPath);
        instance.Data.StatProfile.Levels.Add(row);
        return row;
    }

    private PlayerStats Stats(float damage = 1f, float ability = 1f, float chance = 0f, float speed = 1f, float ammo = 1f, float knockback = 1f)
    {
        var owner = Track(new GameObject("Mode resolver stats"));
        var stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier,
            StatType.AbilityDamageMultiplier, StatType.Knockback, StatType.CriticalChance, StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier,
            StatType.ProjectileAreaSize, StatType.CloseRangeDamageMultiplier, StatType.LongRangeDamageMultiplier })
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            Set(definition, "<StatType>k__BackingField", type);
            Set(definition, "<BaseValue>k__BackingField", type == StatType.DamageMultiplier ? damage
                : type == StatType.AbilityDamageMultiplier ? ability : type == StatType.CriticalChance ? chance
                : type == StatType.CriticalDamage ? 2f : type == StatType.AttackSpeedMultiplier ? speed
                : type == StatType.AmmoMultiplier ? ammo : type == StatType.Knockback ? knockback : 1f);
            definitions.Add(definition);
        }
        Set(stats, "_statDefinitions", definitions);
        typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
        return stats;
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static WeaponOptionalFloat Enabled(float value) => new() { Enabled = true, Value = value };
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}
