using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Consumer-level mutation coverage for the Flame authoring rows. All data is test-owned.</summary>
public sealed class WeaponFlameStatAuthoringTests
{
    private const int InitialHealth = 1000;
    private readonly List<Object> _cleanup = new();
    private readonly List<FlamethrowerWeapon> _weapons = new();
    private readonly List<Transform> _registered = new();
    private SaveManager _previousSaveManager;
    private UnityEngine.Random.State _randomState;
    private float _timeScale;

    [SetUp]
    public void SetUp()
    {
        _previousSaveManager = SaveManager.Instance;
        SetSaveManager(null);
        _randomState = UnityEngine.Random.state;
        _timeScale = Time.timeScale;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Time.timeScale = 1f;
        GameplayPause.Reset();
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Flame stat challenge tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var weapon in _weapons) weapon.ClearManualAreas();
        _weapons.Clear();
        foreach (var root in _registered) if (root != null) EnemyRegistry.Unregister(root);
        _registered.Clear();
        // Edit Mode does not reliably dispatch OnDestroy for these never-awoken visuals.
        foreach (var visual in Object.FindObjectsByType<EnemyStatusVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Call(visual, "OnDestroy");
            SetField(visual, "_counted", false);
        }
        EnemyStatusFeedback.ClearAllActive();
        DestroyAll<FlamethrowerFuelPuddle>();
        DestroyAll<FlamethrowerCueVfx>();
        DestroyAll<WeaponStatusShardVfx>();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        SetSaveManager(_previousSaveManager);
        UnityEngine.Random.state = _randomState;
        Time.timeScale = _timeScale;
        GameplayPause.Reset();
    }

    [TestCase(WeaponStatMode.Automatic, 2f)]
    [TestCase(WeaponStatMode.Automatic, 5f)]
    [TestCase(WeaponStatMode.Manual, 7f)]
    [TestCase(WeaponStatMode.Manual, 11f)]
    public void Travel_UsesOnlyTheEmittedModesAuthoredRangeAfterSwitch(WeaponStatMode mode, float distance)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        var row = AddRow(runtime);
        row.Automatic.RangeMeters = Enabled(mode == WeaponStatMode.Automatic ? distance : 17f);
        row.Manual.RangeMeters = Enabled(mode == WeaponStatMode.Manual ? distance : 19f);
        row.Ability.ExplosionRadiusMeters = Enabled(23f);
        Emit(weapon, runtime, mode);
        Assert.That(weapon.ManualAreas.ActiveCount, Is.EqualTo(1));
        runtime.State = mode == WeaponStatMode.Manual ? WeaponState.Automatic : WeaponState.Manual;
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(distance).Within(.004f));
        Assert.That(weapon.ManualAreas.GetVelocity(0).magnitude, Is.LessThan(.002f));
        Assert.That(weapon.ManualAreas.GetMode(0), Is.EqualTo(mode == WeaponStatMode.Manual
            ? WeaponFeedbackMode.Manual : WeaponFeedbackMode.Automatic));
    }

    [TestCase(WeaponStatMode.Automatic, 11)]
    [TestCase(WeaponStatMode.Automatic, 29)]
    [TestCase(WeaponStatMode.Manual, 23)]
    [TestCase(WeaponStatMode.Manual, 43)]
    public void ContactDamage_UsesSourceModeAfterSwitchAndLeavesOtherInstanceInherited(WeaponStatMode mode, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Automatic.Damage = Enabled(mode == WeaponStatMode.Automatic ? damage : 71f);
        row.Manual.Damage = Enabled(mode == WeaponStatMode.Manual ? damage : 83f);
        row.Ability.Damage = Enabled(97f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, mode);
        runtime.State = mode == WeaponStatMode.Manual ? WeaponState.Automatic : WeaponState.Manual;
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - damage));
        weapon.ClearManualAreas();
        target.gameObject.SetActive(false);

        var inherited = CreateWeapon(WeaponUpgradePath.None, out var other, out var otherStats);
        otherStats.transform.position = Vector3.right * 30f;
        other.Data.BaseRange = 0f;
        var untouched = Target(otherStats.transform.position);
        Emit(inherited, other, mode);
        inherited.ManualAreas.Simulate(.01f);
        Assert.That(untouched.CurrentHealth, Is.EqualTo(InitialHealth - 10), "A second data instance must retain its existing damage.");
    }

    [TestCase(WeaponStatMode.Automatic, 2f, 2)]
    [TestCase(WeaponStatMode.Automatic, 4f, 3)]
    [TestCase(WeaponStatMode.Manual, 2f, 2)]
    [TestCase(WeaponStatMode.Manual, 4f, 3)]
    public void Cadence_AuthoredCyclesChangeActualContactTickCount(WeaponStatMode mode, float cycles, int hits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Automatic.Damage = Enabled(7f);
        row.Manual.Damage = Enabled(7f);
        row.Automatic.AttackCyclesPerSecond = Enabled(mode == WeaponStatMode.Automatic ? cycles : 9f);
        row.Manual.AttackCyclesPerSecond = Enabled(mode == WeaponStatMode.Manual ? cycles : 9f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, mode);
        weapon.ManualAreas.Simulate(.74f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 7 * hits));
    }

    [TestCase(2f, 17)]
    [TestCase(6f, 41)]
    public void Q_AuthoredDamageAndRadiusReachOnlyTargetsInsideTheActualWave(float radius, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.Flamethrower.FlameActiveDamageScale = 3f;
        var row = AddRow(runtime);
        row.Manual.Damage = Enabled(101f);
        row.Ability.Damage = Enabled(damage);
        row.Ability.ExplosionRadiusMeters = Enabled(radius);
        row.AbilityCooldownSeconds = Enabled(4f);
        var inside = Target(Vector3.right * (radius - .1f), register: true);
        var outside = Target(Vector3.forward * (radius + .1f), register: true);
        weapon.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(60f));
        Assert.That(runtime.AbilityCooldownTimer, Is.EqualTo(4f));
        runtime.State = WeaponState.Automatic;
        weapon.ActiveWave.Simulate(.6f);
        Assert.That(weapon.ActiveWave.Clock.Radius, Is.EqualTo(radius).Within(.001f));
        Assert.That(inside.CurrentHealth, Is.EqualTo(InitialHealth - damage), "Q damage replaces the resolved weapon value before player modifiers.");
        Assert.That(outside.CurrentHealth, Is.EqualTo(InitialHealth));
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(inside.CurrentHealth, Is.EqualTo(InitialHealth - damage), "One Q wave must damage a receiver once.");
    }

    [TestCase(0f, 31)]
    [TestCase(1f, 62)]
    public void Q_CriticalControlsChangeHpWithoutChangingBurnDamage(float chance, int directDamage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        var row = AddRow(runtime);
        row.Ability.Damage = Enabled(31f);
        row.CriticalChance = Enabled(chance);
        row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        row.CriticalDamageMultiplier = Enabled(2f);
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - directDamage));
        var burn = target.GetComponent<FlamethrowerBurnStatus>();
        Assert.That(burn, Is.Not.Null);
        burn.Tick(.21f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - directDamage - 4), "Burn keeps its own base scale and never inherits direct Q damage or critical rolls.");
    }

    [Test]
    public void Q_CapturedAdvancedKeySurvivesLevelAndPathChangeWhileTheWaveTravels()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        var source = AddRow(runtime);
        source.Ability.Damage = Enabled(37f);
        source.Ability.ExplosionRadiusMeters = Enabled(4f);
        source.Flame.AbilityPuddleRadiusMeters = Enabled(0f);
        var otherPath = AddRow(runtime, 6, WeaponUpgradePath.PathB);
        otherPath.Ability.Damage = Enabled(89f);
        var basic = AddRow(runtime, 5, WeaponUpgradePath.None);
        basic.Ability.Damage = Enabled(73f);
        var target = Target(Vector3.right * 3f, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        runtime.Level = 5;
        runtime.SelectedPath = WeaponUpgradePath.PathB;
        runtime.State = WeaponState.Automatic;
        weapon.ActiveWave.Simulate(.6f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 37));
        Assert.That(target.GetComponent<FlamethrowerBurnStatus>(), Is.Not.Null);
        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.Null);
    }

    [TestCase(1, WeaponUpgradePath.None, 10)]
    [TestCase(5, WeaponUpgradePath.None, 13)]
    [TestCase(6, WeaponUpgradePath.PathA, 17)]
    [TestCase(6, WeaponUpgradePath.PathB, 23)]
    [TestCase(10, WeaponUpgradePath.PathA, 31)]
    [TestCase(10, WeaponUpgradePath.PathB, 43)]
    public void Progression_OnlyExactCurrentRowChangesActualContactHp(int level, WeaponUpgradePath path, int expected)
    {
        var weapon = CreateWeapon(path, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        runtime.Level = level;
        foreach (var key in new[] { (5, WeaponUpgradePath.None, 13), (6, WeaponUpgradePath.PathA, 17),
            (6, WeaponUpgradePath.PathB, 23), (10, WeaponUpgradePath.PathA, 31), (10, WeaponUpgradePath.PathB, 43) })
            AddRow(runtime, key.Item1, key.Item2).Manual.Damage = Enabled(key.Item3);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - expected));
    }

    [Test]
    public void DisabledValues_PreserveLegacyTravelAndQDamageDespiteStoredHiddenNumbers()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        var row = AddRow(runtime);
        row.Automatic.RangeMeters.Value = 100f;
        row.Manual.RangeMeters.Value = 100f;
        row.Ability.Damage.Value = 100f;
        row.Ability.ExplosionRadiusMeters.Value = 100f;
        Emit(weapon, runtime, WeaponStatMode.Automatic);
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(4.2f).Within(.004f));
        weapon.ClearManualAreas();
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f).Within(.004f));
        weapon.ClearManualAreas();
        var target = Target(Vector3.right * 5f, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        Assert.That(weapon.ActiveWave.Clock.FinalRadius, Is.EqualTo(6f));
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
    }

    [Test]
    public void Jellified_ContactAndQFootprintsHaveIndependentRadiusAndDuration()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Flame.ContactPuddleRadiusMeters = Enabled(1.25f);
        row.Flame.ContactPuddleDurationSeconds = Enabled(3.5f);
        row.Flame.AbilityPuddleRadiusMeters = Enabled(4f);
        row.Flame.AbilityPuddleDurationSeconds = Enabled(7.25f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        var contact = Puddles().Single();
        Assert.That(contact.GameplayRadius, Is.EqualTo(1.25f));
        Assert.That(Field<float>(contact, "_remainingDuration"), Is.EqualTo(3.5f));
        weapon.ClearManualAreas();
        target.gameObject.SetActive(false);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        var ability = Puddles().Single(p => p != contact);
        Assert.That(ability.GameplayRadius, Is.EqualTo(4f));
        Assert.That(Field<float>(ability, "_remainingDuration"), Is.EqualTo(7.25f));
        Assert.That(contact.GameplayRadius, Is.EqualTo(1.25f));
        Assert.That(Field<float>(contact, "_remainingDuration"), Is.EqualTo(3.5f));
        AssertPuddleReach(contact, 1.25f);
        AssertPuddleReach(ability, 4f);
    }

    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public void Jellified_ExplicitZeroRadiusOrDurationSuppressesOnlyThatPuddle(bool ability, bool zeroRadius)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        if (ability)
        {
            row.Flame.AbilityPuddleRadiusMeters = Enabled(zeroRadius ? 0f : 2f);
            row.Flame.AbilityPuddleDurationSeconds = Enabled(zeroRadius ? 3f : 0f);
            weapon.UseActiveAbility(Vector3.forward);
            weapon.ActiveWave.Simulate(.6f);
            Assert.That(Puddles(), Is.Empty);
            var target = Target(Vector3.zero);
            Emit(weapon, runtime, WeaponStatMode.Manual);
            weapon.ManualAreas.Simulate(.01f);
            Assert.That(Puddles(), Has.Length.EqualTo(1));
            Assert.That(target.CurrentHealth, Is.LessThan(InitialHealth));
        }
        else
        {
            row.Flame.ContactPuddleRadiusMeters = Enabled(zeroRadius ? 0f : 2f);
            row.Flame.ContactPuddleDurationSeconds = Enabled(zeroRadius ? 3f : 0f);
            var target = Target(Vector3.zero);
            Emit(weapon, runtime, WeaponStatMode.Manual);
            weapon.ManualAreas.Simulate(.01f);
            Assert.That(Puddles(), Is.Empty);
            Assert.That(target.GetComponent<FlamethrowerBurnStatus>(), Is.Not.Null, "Suppressing a footprint must preserve burn on its receiver.");
            weapon.ClearManualAreas();
            target.gameObject.SetActive(false);
            weapon.UseActiveAbility(Vector3.forward);
            weapon.ActiveWave.Simulate(.6f);
            Assert.That(Puddles(), Has.Length.EqualTo(1));
        }
    }

    [TestCase(.5f)]
    [TestCase(3f)]
    public void Nitrogen_QFreezeUsesAuthoredDurationAndKeepsDoubleLengthSlow(float seconds)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        AddRow(runtime).Flame.AbilityFreezeDurationSeconds = Enabled(seconds);
        var target = Target(Vector3.right, register: true);
        var movement = target.gameObject.AddComponent<SimpleFollow>();
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        var freeze = target.GetComponent<WeaponMovementFreezeStatus>();
        var slow = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        Assert.That(freeze, Is.Not.Null);
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(seconds));
        Assert.That(movement.enabled, Is.False);
        Assert.That(slow.SpeedMultiplier, Is.EqualTo(.1f));
        Assert.That(Field<float>(slow, "_remainingDuration"), Is.EqualTo(seconds * 2f));
        freeze.Simulate(seconds - .01f);
        slow.Simulate(seconds - .01f);
        Assert.That(movement.enabled, Is.False);
        freeze.Simulate(.02f);
        slow.Simulate(.02f);
        Assert.That(freeze == null, Is.True);
        Assert.That(movement.enabled, Is.True, "Ending freeze restores the originally enabled movement component.");
        Assert.That(slow.SpeedMultiplier, Is.EqualTo(.1f), "The longer slow survives freeze removal.");
        slow.Simulate(seconds);
        Assert.That(slow == null, Is.True);
        Assert.That(WeaponMovementSlowStatus.GetSpeedMultiplier(target.transform), Is.EqualTo(1f));
    }

    [Test]
    public void Nitrogen_ExplicitZeroFreezePreservesDirectDamageWithoutDisablingMovement()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        AddRow(runtime).Flame.AbilityFreezeDurationSeconds = Enabled(0f);
        var target = Target(Vector3.right, register: true);
        var movement = target.gameObject.AddComponent<SimpleFollow>();
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.Null);
        Assert.That(WeaponMovementSlowStatus.GetSpeedMultiplier(target.transform), Is.EqualTo(1f));
        Assert.That(movement.enabled, Is.True);
    }

    [Test]
    public void Nitrogen_InheritedFreezeKeepsBurnDurationGuard()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.Flamethrower.FlameBurnDuration = 0f;
        AddRow(runtime).Flame.AbilityFreezeDurationSeconds.Value = 99f;
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.Null);
        Assert.That(target.GetComponent<WeaponMovementSlowStatus>(), Is.Null);
    }

    [TestCase(WeaponStatMode.Manual, 2f)]
    [TestCase(WeaponStatMode.Manual, 6f)]
    [TestCase(WeaponStatMode.Ability, 2f)]
    [TestCase(WeaponStatMode.Ability, 6f)]
    public void Knockback_AuthoredStrengthChangesActualDisplacementWithExistingEligibleModeScales(WeaponStatMode mode, float strength)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameManualKnockbackScale = .5f;
        runtime.Data.Flamethrower.FlameActiveKnockbackScale = 1.5f;
        var row = AddRow(runtime);
        row.Manual.Damage = Enabled(16f);
        row.Ability.Damage = Enabled(16f);
        row.Manual.KnockbackStrength = Enabled(mode == WeaponStatMode.Manual ? strength : 11f);
        row.Ability.KnockbackStrength = Enabled(mode == WeaponStatMode.Ability ? strength : 13f);
        var target = Target(mode == WeaponStatMode.Ability ? Vector3.right : Vector3.zero, register: mode == WeaponStatMode.Ability);
        if (mode == WeaponStatMode.Ability)
        {
            weapon.UseActiveAbility(Vector3.forward);
            runtime.State = WeaponState.Automatic;
            weapon.ActiveWave.Simulate(.2f);
        }
        else
        {
            Emit(weapon, runtime, mode);
            runtime.State = WeaponState.Automatic;
            weapon.ManualAreas.Simulate(.01f);
        }
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 16));
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        Assert.That(receiver, Is.Not.Null);
        float eligibleScale = mode == WeaponStatMode.Manual ? .5f : 1.5f;
        Assert.That(receiver.ConsumeDisplacement(.1f).magnitude,
            Is.EqualTo(strength * 2f * eligibleScale * .1f).Within(.001f),
            "The authored base impulse still receives player, damage, and eligible-mode factors once.");
    }

    [Test]
    public void Automatic_DoesNotAcquireManualOrQKnockback()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Manual.KnockbackStrength = Enabled(50f);
        row.Ability.KnockbackStrength = Enabled(50f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Automatic);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10));
        Assert.That(target.GetComponent<EnemyKnockbackReceiver>(), Is.Null);
    }

    [TestCase(WeaponStatField.Damage)]
    [TestCase(WeaponStatField.RangeMeters)]
    [TestCase(WeaponStatField.AttackCyclesPerSecond)]
    public void Schema_RejectsExplicitZeroForPositiveOnlyContactFields(WeaponStatField field)
    {
        CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        var row = AddRow(runtime);
        if (field == WeaponStatField.Damage) row.Manual.Damage = Enabled(0f);
        if (field == WeaponStatField.RangeMeters) row.Manual.RangeMeters = Enabled(0f);
        if (field == WeaponStatField.AttackCyclesPerSecond) row.Manual.AttackCyclesPerSecond = Enabled(0f);
        Assert.That(runtime.Data.StatProfile.TryValidate(out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Nitrogen_ExplicitFreezeControlWorksWhenLegacyBurnDurationIsZero()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.Flamethrower.FlameBurnDuration = 0f;
        AddRow(runtime).Flame.AbilityFreezeDurationSeconds = Enabled(.75f);
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        var freeze = target.GetComponent<WeaponMovementFreezeStatus>();
        Assert.That(freeze, Is.Not.Null);
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.75f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Jellified_AuthoredPuddleDurationEndsActualDamageAndReleasesFallback(bool ability)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Flame.ContactPuddleDurationSeconds = Enabled(.4f);
        row.Flame.AbilityPuddleDurationSeconds = Enabled(.9f);
        float duration = ability ? .9f : .4f;
        if (ability)
        {
            weapon.UseActiveAbility(Vector3.forward);
            weapon.ActiveWave.Simulate(.6f);
        }
        else
        {
            var contactTarget = Target(Vector3.zero);
            Emit(weapon, runtime, WeaponStatMode.Manual);
            weapon.ManualAreas.Simulate(.01f);
            weapon.ClearManualAreas();
            contactTarget.gameObject.SetActive(false);
        }
        var puddle = Puddles().Single();
        var target = Target(Vector3.right * .2f);
        puddle.Simulate(duration - .01f);
        Assert.That(target.CurrentHealth, Is.LessThan(InitialHealth));
        int beforeExpiry = target.CurrentHealth;
        puddle.Simulate(.02f);
        Assert.That(puddle == null, Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeExpiry), "An expired puddle cannot apply a last overdue tick.");
    }
    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void Q_ExplicitZeroRadiusSpendsAmmoAndStartsCooldownWithoutWaveDamageOrPuddle(WeaponUpgradePath path)
    {
        var weapon = CreateWeapon(path, out var runtime, out _);
        var row = AddRow(runtime);
        row.Ability.ExplosionRadiusMeters = Enabled(0f);
        row.AbilityCooldownSeconds = Enabled(3.25f);
        var atOrigin = Target(Vector3.zero, register: true);
        var nearby = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(60f));
        Assert.That(runtime.AbilityCooldownTimer, Is.EqualTo(3.25f));
        Assert.That(weapon.ActiveWave, Is.Null);
        Assert.That(Object.FindObjectsByType<FlamethrowerActiveWave>(FindObjectsSortMode.None), Is.Empty);
        Assert.That(Puddles(), Is.Empty);
        Assert.That(atOrigin.CurrentHealth, Is.EqualTo(InitialHealth));
        Assert.That(nearby.CurrentHealth, Is.EqualTo(InitialHealth));
        weapon.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(60f), "The committed zero-radius cast still obeys cooldown gating.");
    }

    [TestCase(WeaponStatMode.Automatic, false, 2)]
    [TestCase(WeaponStatMode.Automatic, true, 8)]
    [TestCase(WeaponStatMode.Manual, false, 2)]
    [TestCase(WeaponStatMode.Manual, true, 2)]
    public void AuthoredCadence_AppliesPlayerSpeedAndOnlyAutomaticOverheatRatio(WeaponStatMode mode, bool hot, int expectedHits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out var stats);
        ConfigureRateFixture(weapon, runtime, stats, hot);
        var row = AddRow(runtime);
        row.Manual.Damage = Enabled(7f);
        row.Automatic.Damage = Enabled(7f);
        row.Manual.AttackCyclesPerSecond = Enabled(2f);
        row.Automatic.AttackCyclesPerSecond = Enabled(2f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, mode);
        weapon.ManualAreas.Simulate(.49f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - expectedHits * 7));
    }

    [TestCase(WeaponStatMode.Automatic, false, 1)]
    [TestCase(WeaponStatMode.Automatic, true, 3)]
    [TestCase(WeaponStatMode.Manual, false, 2)]
    [TestCase(WeaponStatMode.Manual, true, 2)]
    public void InheritedCadence_PreservesExistingIntervalsDespitePlayerSpeed(WeaponStatMode mode, bool hot, int expectedHits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out var stats);
        ConfigureRateFixture(weapon, runtime, stats, hot);
        var row = AddRow(runtime);
        row.Manual.Damage = Enabled(7f);
        row.Automatic.Damage = Enabled(7f);
        row.Manual.AttackCyclesPerSecond.Value = 90f;
        row.Automatic.AttackCyclesPerSecond.Value = 90f;
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, mode);
        weapon.ManualAreas.Simulate(.49f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - expectedHits * 7));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Jellified_SubLegacyMinimumPuddleUsesActualAuthoredRadiusAndLifetime(bool ability)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Flame.ContactPuddleRadiusMeters = Enabled(.02f);
        row.Flame.ContactPuddleDurationSeconds = Enabled(.03f);
        row.Flame.AbilityPuddleRadiusMeters = Enabled(.02f);
        row.Flame.AbilityPuddleDurationSeconds = Enabled(.03f);
        if (ability)
        {
            weapon.UseActiveAbility(Vector3.forward);
            weapon.ActiveWave.Simulate(.6f);
        }
        else
        {
            var contactTarget = Target(Vector3.zero);
            Emit(weapon, runtime, WeaponStatMode.Manual);
            weapon.ManualAreas.Simulate(.01f);
            weapon.ClearManualAreas();
            contactTarget.gameObject.SetActive(false);
        }
        var puddle = Puddles().Single();
        Assert.That(puddle.GameplayRadius, Is.EqualTo(.02f).Within(.00001f));
        Assert.That(Field<float>(puddle, "_remainingDuration"), Is.EqualTo(.03f).Within(.00001f));
        var inside = Target(puddle.transform.position + Vector3.right * .01f);
        var outside = Target(puddle.transform.position + Vector3.forward * .06f);
        inside.GetComponent<SphereCollider>().radius = .001f;
        outside.GetComponent<SphereCollider>().radius = .001f;
        Physics.SyncTransforms();
        puddle.Simulate(.01f);
        Assert.That(inside.CurrentHealth, Is.LessThan(InitialHealth));
        Assert.That(outside.CurrentHealth, Is.EqualTo(InitialHealth), "The gameplay query must not silently expand to the legacy 0.1 m minimum.");
        puddle.Simulate(.021f);
        Assert.That(puddle == null, Is.True, "The authored duration must not silently expand to the legacy 0.1 s minimum.");
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void HiddenAdvancedPayloads_ArePreservedWithoutLeakingAcrossPaths(WeaponUpgradePath path)
    {
        var weapon = CreateWeapon(path, out var runtime, out _);
        var row = AddRow(runtime);
        if (path == WeaponUpgradePath.PathA)
        {
            row.Flame.ContactPuddleRadiusMeters = Enabled(.3f);
            row.Flame.ContactPuddleDurationSeconds = Enabled(.7f);
            row.Flame.AbilityPuddleRadiusMeters = Enabled(.4f);
            row.Flame.AbilityPuddleDurationSeconds = Enabled(.8f);
        }
        if (path == WeaponUpgradePath.PathB)
            row.Flame.AbilityFreezeDurationSeconds = Enabled(.75f);
        if (path != WeaponUpgradePath.PathA)
            AddRow(runtime, 6, WeaponUpgradePath.PathA).Flame.AbilityPuddleRadiusMeters = Enabled(33f);
        if (path != WeaponUpgradePath.PathB)
            AddRow(runtime, 6, WeaponUpgradePath.PathB).Flame.AbilityFreezeDurationSeconds = Enabled(29f);
        string profileBefore = JsonUtility.ToJson(runtime.Data.StatProfile);
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        if (path == WeaponUpgradePath.PathA)
        {
            Assert.That(Puddles(), Has.Length.EqualTo(2));
            Assert.That(Puddles().Select(p => p.GameplayRadius).OrderBy(r => r), Is.EqualTo(new[] { .3f, .4f }));
        }
        else Assert.That(Puddles(), Is.Empty);
        var freeze = target.GetComponent<WeaponMovementFreezeStatus>();
        if (path == WeaponUpgradePath.PathB)
        {
            Assert.That(freeze, Is.Not.Null);
            Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.75f));
        }
        else Assert.That(freeze, Is.Null);
        Assert.That(JsonUtility.ToJson(runtime.Data.StatProfile), Is.EqualTo(profileBefore));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Nitrogen_RepeatedAuthoredFreezeRefreshExtendsButDoesNotShortenOrRecacheMovement(bool originallyEnabled)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        var row = AddRow(runtime);
        row.AbilityCooldownSeconds = Enabled(0f);
        row.Flame.AbilityFreezeDurationSeconds = Enabled(.5f);
        var target = Target(Vector3.right, register: true);
        var movement = target.gameObject.AddComponent<SimpleFollow>();
        movement.enabled = originallyEnabled;
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        var freeze = target.GetComponent<WeaponMovementFreezeStatus>();
        freeze.Simulate(.3f);
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.2f).Within(.0001f));

        weapon.ClearManualAreas();
        runtime.CurrentAmmo = 100f;
        row.Flame.AbilityFreezeDurationSeconds = Enabled(.1f);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.SameAs(freeze));
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.2f).Within(.0001f));

        weapon.ClearManualAreas();
        runtime.CurrentAmmo = 100f;
        row.Flame.AbilityFreezeDurationSeconds = Enabled(.75f);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 60));
        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.SameAs(freeze));
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.75f));
        freeze.Simulate(.74f);
        Assert.That(movement.enabled, Is.False);
        freeze.Simulate(.02f);
        Assert.That(freeze == null, Is.True);
        Assert.That(movement.enabled, Is.EqualTo(originallyEnabled));
    }

    [TestCase(1.25f, 3.5f, 4f, 7.25f)]
    [TestCase(2.25f, 5.5f, 4f, 7.25f)]
    [TestCase(1.25f, 3.5f, 6f, 9.25f)]
    public void Jellified_QReceiverContactsAndCentralFootprintResolveIndependentSettings(
        float contactRadius, float contactDuration, float centralRadius, float centralDuration)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        var row = AddRow(runtime);
        row.Flame.ContactPuddleRadiusMeters = Enabled(contactRadius);
        row.Flame.ContactPuddleDurationSeconds = Enabled(contactDuration);
        row.Flame.AbilityPuddleRadiusMeters = Enabled(centralRadius);
        row.Flame.AbilityPuddleDurationSeconds = Enabled(centralDuration);
        var target = Target(Vector3.right * 3f, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        var puddles = Puddles();
        Assert.That(puddles, Has.Length.EqualTo(2));
        var contact = puddles.Single(p => p.transform.position.x > 2f);
        var central = puddles.Single(p => p.transform.position.x < 1f);
        Assert.That(contact.GameplayRadius, Is.EqualTo(contactRadius));
        Assert.That(Field<float>(contact, "_remainingDuration"), Is.EqualTo(contactDuration));
        Assert.That(central.GameplayRadius, Is.EqualTo(centralRadius));
        Assert.That(Field<float>(central, "_remainingDuration"), Is.EqualTo(centralDuration));
        var activePreview = weapon.CaptureDiagnostics().Sections.Single(section => section.Name == "Active Ability");
        Assert.That(activePreview.Values.Single(value => value.Label == "Contact puddle radius").Value,
            Is.EqualTo(contact.GameplayRadius.ToString("0.###", CultureInfo.InvariantCulture) + " m"));
        Assert.That(activePreview.Values.Single(value => value.Label == "Contact puddle duration").Value,
            Is.EqualTo(Field<float>(contact, "_remainingDuration").ToString("0.###", CultureInfo.InvariantCulture) + " s"));
        Assert.That(activePreview.Values.Single(value => value.Label == "Fuel puddle radius").Value,
            Is.EqualTo(central.GameplayRadius.ToString("0.###", CultureInfo.InvariantCulture) + " m"));
        Assert.That(activePreview.Values.Single(value => value.Label == "Fuel puddle duration").Value,
            Is.EqualTo(Field<float>(central, "_remainingDuration").ToString("0.###", CultureInfo.InvariantCulture) + " s"));
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        AssertPuddleReach(contact, contactRadius);
        AssertPuddleReach(central, centralRadius);
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    public void Nitrogen_AuthoredRampChangesActualMovementAtEachHitAndExpires(WeaponStatMode mode)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnDuration = 0f;
        var row = AddRow(runtime);
        row.Automatic.AttackCyclesPerSecond = Enabled(2f);
        row.Manual.AttackCyclesPerSecond = Enabled(2f);
        row.Flame.NitrogenInitialSlowMultiplier = Enabled(.8f);
        row.Flame.NitrogenMinimumSlowMultiplier = Enabled(.2f);
        row.Flame.NitrogenHitsToMinimum = new WeaponOptionalInt { Enabled = true, Value = 3 };
        row.Flame.NitrogenSlowDurationSeconds = Enabled(4f);
        var target = Target(Vector3.zero);
        var movement = AddMovingFollower(target);
        Emit(weapon, runtime, mode);
        foreach (float expected in new[] { .8f, .5f, .2f })
        {
            weapon.ManualAreas.Simulate(expected == .8f ? .01f : .5f);
            var slow = target.GetComponent<WeaponMovementSlowStatus>();
            Assert.That(slow, Is.Not.Null);
            Assert.That(slow.SpeedMultiplier, Is.EqualTo(expected).Within(.0001f));
            Assert.That(Field<float>(slow, "_remainingDuration"), Is.EqualTo(4f));
            AssertActualMovement(movement, expected);
        }
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 30));
        weapon.ClearManualAreas();
        target.GetComponent<WeaponMovementSlowStatus>().Simulate(4.01f);
        AssertActualMovement(movement, 1f);
    }

    [TestCase(0f)]
    [TestCase(.25f)]
    [TestCase(1f)]
    public void Nitrogen_OneHitRampUsesConfiguredMinimumForActualMovement(float minimum)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Flame.NitrogenInitialSlowMultiplier = Enabled(1f);
        row.Flame.NitrogenMinimumSlowMultiplier = Enabled(minimum);
        row.Flame.NitrogenHitsToMinimum = new WeaponOptionalInt { Enabled = true, Value = 1 };
        var target = Target(Vector3.zero);
        var movement = AddMovingFollower(target);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10), "The ramp must come from a real contact hit.");
        var slow = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(slow, Is.Not.Null);
        Assert.That(slow.SpeedMultiplier, Is.EqualTo(minimum).Within(.0001f));
        AssertActualMovement(movement, minimum);
    }

    [Test]
    public void Nitrogen_ZeroOrdinarySlowDurationPreservesContactDamageAndFullMovement()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        AddRow(runtime).Flame.NitrogenSlowDurationSeconds = Enabled(0f);
        var target = Target(Vector3.zero);
        var movement = AddMovingFollower(target);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10));
        Assert.That(target.GetComponent<WeaponMovementSlowStatus>(), Is.Null);
        AssertActualMovement(movement, 1f);
    }

    [TestCase(.4f, 3f)]
    [TestCase(.7f, 5f)]
    public void Nitrogen_QSlowStrengthAndDurationAreIndependentControls(float multiplier, float secondsPerFreeze)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        runtime.Data.Flamethrower.FlameBurnDuration = 0f;
        var row = AddRow(runtime);
        row.Flame.AbilityFreezeDurationSeconds = Enabled(.5f);
        row.Flame.AbilitySlowMultiplier = Enabled(multiplier);
        row.Flame.AbilitySlowDurationPerFreezeSecond = Enabled(secondsPerFreeze);
        var target = Target(Vector3.right, register: true);
        var movement = AddMovingFollower(target);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        var freeze = target.GetComponent<WeaponMovementFreezeStatus>();
        var slow = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(movement.enabled, Is.False);
        Assert.That(Field<float>(freeze, "_remainingDuration"), Is.EqualTo(.5f));
        Assert.That(Field<float>(slow, "_remainingDuration"), Is.EqualTo(.5f * secondsPerFreeze));
        freeze.Simulate(.51f);
        slow.Simulate(.51f);
        Assert.That(movement.enabled, Is.True);
        AssertActualMovement(movement, multiplier);
        slow.Simulate(.5f * secondsPerFreeze);
        AssertActualMovement(movement, 1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
    }

    [Test]
    public void Nitrogen_ZeroQSlowRatioKeepsFreezeButCreatesNoSlow()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var runtime, out _);
        var row = AddRow(runtime);
        row.Flame.AbilityFreezeDurationSeconds = Enabled(.5f);
        row.Flame.AbilitySlowDurationPerFreezeSecond = Enabled(0f);
        var target = Target(Vector3.right, register: true);
        var movement = AddMovingFollower(target);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(movement.enabled, Is.False);
        Assert.That(target.GetComponent<WeaponMovementSlowStatus>(), Is.Null);
        target.GetComponent<WeaponMovementFreezeStatus>().Simulate(.51f);
        AssertActualMovement(movement, 1f);
    }

    [TestCase(2.5f, 10)]
    [TestCase(5f, 20)]
    public void Jellified_BurnFactorChangesActualStatusHpWithoutChangingDirectHit(float factor, int burnDamage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        var row = AddRow(runtime);
        row.Flame.JellifiedBurnDamageMultiplier = Enabled(factor);
        row.Flame.ContactPuddleRadiusMeters = Enabled(0f);
        row.CriticalChance = Enabled(1f);
        row.CriticalDamageMultiplier = Enabled(2f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        weapon.ClearManualAreas();
        target.GetComponent<FlamethrowerBurnStatus>().Tick(.21f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20 - burnDamage));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Jellified_BurnDurationFactorKeepsPuddleFallbackLinkAndAllowsIndependentLifetime(bool explicitPuddles)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnDuration = 3f;
        var row = AddRow(runtime);
        row.Flame.JellifiedBurnDurationMultiplier = Enabled(4f);
        if (explicitPuddles)
        {
            row.Flame.ContactPuddleDurationSeconds = Enabled(7f);
            row.Flame.AbilityPuddleDurationSeconds = Enabled(8f);
        }
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        var burn = target.GetComponent<FlamethrowerBurnStatus>();
        Assert.That(Field<float>(burn, "_remainingDuration"), Is.EqualTo(12f));
        var contact = Puddles().Single();
        Assert.That(Field<float>(contact, "_remainingDuration"), Is.EqualTo(explicitPuddles ? 7f : 12f));
        weapon.ClearManualAreas();
        target.gameObject.SetActive(false);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        var central = Puddles().Single(p => p != contact);
        Assert.That(Field<float>(central, "_remainingDuration"), Is.EqualTo(explicitPuddles ? 8f : 12f));
    }

    [Test]
    public void FlameGeometryUsesResolvedConsumerValuesAndDoesNotChangeRuntimeOrRandomness()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out var stats);
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.Null);
        var row = AddRow(runtime);
        row.Automatic.RangeMeters = Enabled(3f);
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.Null);
        row.Manual.RangeMeters = Enabled(8f);
        row.Ability.ExplosionRadiusMeters = Enabled(5f);
        row.Flame.AbilityPuddleRadiusMeters = Enabled(7f);
        stats.AddModifier(new StatModifier(StatType.ProjectileAreaSize, 2f, StatUpgradeSource.TemporaryEffect,
            modifierType: StatModifierType.Multiplicative));
        string before = JsonUtility.ToJson(runtime);
        var randomBefore = UnityEngine.Random.state;
        float next = UnityEngine.Random.value;
        UnityEngine.Random.state = randomBefore;
        var geometry = weapon.CaptureGeometry();
        Assert.That(UnityEngine.Random.value, Is.EqualTo(next));
        Assert.That(JsonUtility.ToJson(runtime), Is.EqualTo(before));
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.EqualTo(16f));
        var automatic = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Automatic);
        var manual = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Manual);
        Assert.That(automatic.RangeMeters, Is.EqualTo(6f));
        Assert.That(automatic.Direction, Is.EqualTo(WeaponGeometryDirection.OwnerForward));
        Assert.That(manual.RangeMeters, Is.EqualTo(16f));
        Assert.That(manual.Anchor, Is.EqualTo(WeaponGeometryAnchor.Muzzle));
        Assert.That(geometry.Guides.Where(g => g.Mode == WeaponStatMode.Ability).Select(g => g.RadiusMeters),
            Is.EquivalentTo(new[] { 10f, 14f }));
    }

    [TestCase("NitrogenInitialSlowMultiplier", -0.1f)]
    [TestCase("NitrogenMinimumSlowMultiplier", 1.1f)]
    [TestCase("NitrogenSlowDurationSeconds", 121f)]
    [TestCase("AbilitySlowMultiplier", 1.1f)]
    [TestCase("AbilitySlowDurationPerFreezeSecond", 101f)]
    [TestCase("JellifiedBurnDamageMultiplier", 0f)]
    [TestCase("JellifiedBurnDurationMultiplier", 1001f)]
    public void FlamePayloadRejectsOutOfContractValues(string field, float value)
    {
        var payload = new WeaponFlameStatOverrides();
        typeof(WeaponFlameStatOverrides).GetField(field).SetValue(payload, Enabled(value));
        Assert.That(payload.TryValidate(out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase(0)]
    [TestCase(1025)]
    public void NitrogenRampRejectsInvalidHitCounts(int count)
    {
        var payload = new WeaponFlameStatOverrides
        { NitrogenHitsToMinimum = new WeaponOptionalInt { Enabled = true, Value = count } };
        Assert.That(payload.TryValidate(out _), Is.False);
    }

    [Test]
    public void NitrogenRampValidatesMinimumAgainstInheritedCounterpart()
    {
        var payload = new WeaponFlameStatOverrides { NitrogenMinimumSlowMultiplier = Enabled(.6f) };
        Assert.That(payload.TryValidate(out _), Is.False);
        payload.NitrogenInitialSlowMultiplier = Enabled(.8f);
        Assert.That(payload.TryValidate(out _), Is.True);
    }

    private SimpleFollow AddMovingFollower(EnemyHealth target)
    {
        var movement = target.gameObject.AddComponent<SimpleFollow>();
        Call(movement, "Awake");
        var destination = Track(new GameObject("Flame slow movement destination"));
        destination.transform.position = target.transform.position + Vector3.right * 100f;
        movement.SetTarget(destination.transform);
        SetField(movement, "_orbitRadius", 0f);
        SetField(movement, "_weaveAmplitude", 0f);
        SetField(movement, "_separationWeight", 0f);
        var body = target.GetComponent<Rigidbody>();
        // Match the dynamic enemy body. Disabling collision detection excludes the
        // target collider from the stream's overlap query and invalidates contact tests.
        body.useGravity = false;
        body.isKinematic = false;
        body.detectCollisions = true;
        body.linearDamping = 0f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        Physics.SyncTransforms();
        return movement;
    }

    private static void AssertActualMovement(SimpleFollow movement, float speedMultiplier)
    {
        Assert.That(movement.enabled, Is.True);
        Assert.That(EnemyVerticalEngagement.IsDisengaged(movement), Is.False);
        // Push is covered separately; measure the actual steering contribution of slow.
        movement.GetComponent<EnemyKnockbackReceiver>()?.ClearPending();
        SimulationMode previous = Physics.simulationMode;
        var body = movement.GetComponent<Rigidbody>();
        Vector3 origin = body.position;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            movement.BrainFixedUpdate(.1f);
            Physics.Simulate(.1f);
            Assert.That(Vector3.Distance(body.position, origin),
                Is.EqualTo(movement.AuthoredMoveSpeed * DebugSpeedTool.EnemyScale * speedMultiplier * .1f).Within(.003f));
        }
        finally
        {
            body.position = origin;
            movement.transform.position = origin;
            Physics.SyncTransforms();
            Physics.simulationMode = previous;
        }
    }

    [TestCase(.5f, 1.5f)]
    [TestCase(3f, 9f)]
    public void Jellified_BurnDurationFactorChangesTheActualLastDamageTick(float factor, float duration)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out _);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnDuration = 3f;
        var row = AddRow(runtime);
        row.Flame.JellifiedBurnDurationMultiplier = Enabled(factor);
        row.Flame.ContactPuddleRadiusMeters = Enabled(0f);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        weapon.ClearManualAreas();
        var burn = target.GetComponent<FlamethrowerBurnStatus>();
        burn.Tick(duration - .01f);
        Assert.That(target.CurrentHealth, Is.LessThan(InitialHealth - 10));
        Assert.That(burn == null, Is.False);
        int beforeExpiry = target.CurrentHealth;
        burn.Tick(.02f);
        Assert.That(burn == null, Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeExpiry), "Expiry must prevent an overdue final status tick.");
    }

    [TestCase(0f, .8f)]
    [TestCase(1.2f, 1.2f)]
    [TestCase(2.5f, 2.5f)]
    public void LegacyVisualDuration_DrivesActualWaveAndRenderedLifetime(float authoredSeconds, float expectedSeconds)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        runtime.Data.Flamethrower.FlameActiveVisualDuration = authoredSeconds;
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        var wave = weapon.ActiveWave;
        var visual = Object.FindObjectsByType<FlamethrowerCueVfx>(FindObjectsSortMode.None).Single();
        Assert.That(wave.Clock.VisualDuration, Is.EqualTo(expectedSeconds).Within(.0001f));
        Assert.That(visual.ActiveVisualLifetime, Is.EqualTo(expectedSeconds).Within(.0001f));
        wave.Simulate(.3f);
        visual.SampleActive(100f); // A bound presentation reads the authoritative wave, not this caller time.
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        Assert.That(visual.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled), Is.True);
        Assert.That(visual.GameplayLifetimeComplete, Is.False);
        wave.Simulate(expectedSeconds - .3f + .01f);
        visual.SampleActive(0f);
        Assert.That(wave.Clock.Complete, Is.True);
        Assert.That(visual.GameplayLifetimeComplete, Is.True);
        Assert.That(visual.GetComponentsInChildren<MeshRenderer>().All(r => !r.enabled), Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20), "Visual lifetime must not extend the damaging sweep.");
    }

    [TestCase(.25f, 6, 1f)]
    [TestCase(1.5f, 6, 1.5f)]
    [TestCase(.25f, 10, 1.5f)]
    [TestCase(1.5f, 10, 1f)]
    public void LegacyFuelPuddleRadius_ChangesContactHitsWithoutChangingCentralFootprint(float authoredRadius, int level, float area)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var runtime, out var stats);
        runtime.Level = level;
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameFuelPuddleRadius = authoredRadius;
        stats.AddModifier(new StatModifier(StatType.ProjectileAreaSize, area, StatUpgradeSource.TemporaryEffect,
            modifierType: StatModifierType.Multiplicative));
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        var contact = Puddles().Single();
        float levelScale = level / 6f;
        float expectedRadius = authoredRadius * area * levelScale;
        Assert.That(contact.GameplayRadius, Is.EqualTo(expectedRadius).Within(.0001f));
        weapon.ClearManualAreas();
        target.gameObject.SetActive(false);
        AssertPuddleReach(contact, expectedRadius);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        var central = Puddles().Single(p => p != contact);
        float expectedCentralRadius = 6f * 1.2f * area * .5f * levelScale;
        Assert.That(central.GameplayRadius, Is.EqualTo(expectedCentralRadius).Within(.0001f),
            "The central Q footprint inherits Q radius, not the receiver-contact radius control.");
        AssertPuddleReach(central, expectedCentralRadius);
    }

    [TestCase(WeaponStatMode.Automatic, 0f)]
    [TestCase(WeaponStatMode.Automatic, .25f)]
    [TestCase(WeaponStatMode.Automatic, 1.5f)]
    [TestCase(WeaponStatMode.Manual, 0f)]
    [TestCase(WeaponStatMode.Manual, .25f)]
    [TestCase(WeaponStatMode.Manual, 1.5f)]
    public void LegacyHeatRangeMultiplier_ChangesActualModeTravelAtFullHeat(WeaponStatMode mode, float heatMultiplier)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out var stats);
        runtime.Data.Flamethrower.FlameManualRangeHeatMultiplier = heatMultiplier;
        stats.AddModifier(new StatModifier(StatType.ProjectileAreaSize, 1.5f, StatUpgradeSource.TemporaryEffect,
            modifierType: StatModifierType.Multiplicative));
        var heat = stats.gameObject.AddComponent<HeatManager>();
        SetField(heat, "_currentHeat", heat.TotalHeatCapacity);
        weapon.Setup(runtime, stats.transform, stats, heat);
        Emit(weapon, runtime, mode);
        weapon.ManualAreas.Simulate(1f);
        float expected = 7f * 1.5f * (1f + heatMultiplier);
        if (mode == WeaponStatMode.Automatic) expected *= .6f;
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(expected).Within(.005f));
        Assert.That(weapon.ManualAreas.GetVelocity(0).magnitude, Is.LessThan(.003f));
    }

    private static void ConfigureRateFixture(FlamethrowerWeapon weapon, WeaponInstance runtime, PlayerStats stats, bool hot)
    {
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameManualTickInterval = .4f;
        runtime.Data.Flamethrower.FlameAutoTickInterval = .8f;
        runtime.Data.Flamethrower.FlameOverheatAutoTickInterval = .2f;
        runtime.Data.Flamethrower.FlameOverheatTickThresholdPercent = 80f;
        stats.AddModifier(new StatModifier(StatType.AttackSpeedMultiplier, 2f, StatUpgradeSource.TemporaryEffect,
            modifierType: StatModifierType.Multiplicative));
        var heat = stats.gameObject.AddComponent<HeatManager>();
        if (hot) SetField(heat, "_currentHeat", heat.TotalHeatCapacity);
        weapon.Setup(runtime, stats.transform, stats, heat);
    }
    private void AssertPuddleReach(FlamethrowerFuelPuddle puddle, float radius)
    {
        var inside = Target(puddle.transform.position + Vector3.right * (radius - .2f));
        var outside = Target(puddle.transform.position + Vector3.forward * (radius + .3f));
        SetField(puddle, "_tickTimer", 0f);
        puddle.Simulate(.01f);
        Assert.That(inside.CurrentHealth, Is.LessThan(InitialHealth), "Authored footprint must damage a receiver inside its actual radial query.");
        Assert.That(outside.CurrentHealth, Is.EqualTo(InitialHealth));
        inside.gameObject.SetActive(false);
        outside.gameObject.SetActive(false);
    }

    private FlamethrowerWeapon CreateWeapon(WeaponUpgradePath path, out WeaponInstance runtime, out PlayerStats stats)
    {
        var owner = Track(new GameObject("Flame stat owner"));
        stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction,
            StatType.CloseRangeDamageMultiplier, StatType.LongRangeDamageMultiplier })
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField", type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        Call(stats, "Awake");
        var data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.Flamethrower;
        data.WeaponId = "flame-stat-test-" + Guid.NewGuid().ToString("N");
        data.EnsureSpecificTuningForCurrentType();
        data.BaseDamage = 10f;
        data.BaseRange = 7f;
        data.BaseManualAmmo = 100f;
        data.ActiveAbilityAmmoCost = 40f;
        data.SkillCooldown = 14f;
        data.Flamethrower.FlameBurnDamageScale = .4f;
        data.Flamethrower.FlameBurnTickInterval = .2f;
        data.Flamethrower.FlameActiveDamageScale = 2f;
        data.Flamethrower.FlameActiveRadius = 6f;
        data.Flamethrower.FlameActiveExpansionDuration = .6f;
        data.Flamethrower.FlameAreaLifetime = 1.5f;
        data.Flamethrower.FlameAreaTimeToStop = 1f;
        data.Flamethrower.FlameAutomaticRangeMultiplier = .6f;
        data.StatProfile = new WeaponStatProfile();
        runtime = new WeaponInstance { Data = data, Level = path == WeaponUpgradePath.None ? 1 : 6,
            SelectedPath = path, State = WeaponState.Manual, CurrentAmmo = 100f };
        var weapon = new FlamethrowerWeapon(null, null, owner.transform, null);
        weapon.Setup(runtime, owner.transform, stats, null);
        _weapons.Add(weapon);
        return weapon;
    }

    private static WeaponStatLevelRow AddRow(WeaponInstance runtime, int? level = null, WeaponUpgradePath? path = null)
    {
        var row = new WeaponStatLevelRow { WeaponType = WeaponType.Flamethrower, Level = level ?? runtime.Level,
            Path = path ?? runtime.SelectedPath, Automatic = new WeaponStatModeValues(), Manual = new WeaponStatModeValues(),
            Ability = new WeaponStatModeValues(), Flame = new WeaponFlameStatOverrides(),
            CriticalChance = Enabled(0f), CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer };
        runtime.Data.StatProfile.Levels.Add(row);
        return row;
    }

    private static void Emit(FlamethrowerWeapon weapon, WeaponInstance runtime, WeaponStatMode mode)
    {
        runtime.State = mode == WeaponStatMode.Manual ? WeaponState.Manual : WeaponState.Automatic;
        if (mode == WeaponStatMode.Manual) weapon.TickManual(.01f, Vector3.forward, true);
        else weapon.TickAutomatic(.01f, Vector3.forward);
    }

    private EnemyHealth Target(Vector3 position, bool register = false)
    {
        var root = Track(new GameObject("Flame stat target"));
        root.transform.position = position;
        root.AddComponent<SphereCollider>().radius = .1f;
        var health = root.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(InitialHealth);
        if (register) { EnemyRegistry.Register(root.transform); _registered.Add(root.transform); }
        Physics.SyncTransforms();
        return health;
    }

    private static FlamethrowerFuelPuddle[] Puddles() => Object.FindObjectsByType<FlamethrowerFuelPuddle>(FindObjectsSortMode.None);
    private static WeaponOptionalFloat Enabled(float value) => new() { Enabled = true, Value = value };
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void DestroyAll<T>() where T : Component
    {
        foreach (var component in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (component != null) Object.DestroyImmediate(component.gameObject);
    }
    private static void SetSaveManager(SaveManager manager) => typeof(SaveManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] arguments) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

    // Appendix B: mutate the legacy global itself, with optional per-level controls disabled.
    [TestCase(.5f, 5)]
    [TestCase(3f, 30)]
    public void LegacyAbilityDamageScale_ChangesActualQHpAndLeavesContactDamageUnchanged(float scale, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameActiveDamageScale = scale;
        var target = Target(Vector3.right, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.2f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - damage));
        target.gameObject.SetActive(false);
        runtime.Data.BaseRange = 0f;
        var contact = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(contact.CurrentHealth, Is.EqualTo(InitialHealth - 10));
    }

    [TestCase(WeaponUpgradePath.None, 3f, 3f)]
    [TestCase(WeaponUpgradePath.None, 9f, 9f)]
    [TestCase(WeaponUpgradePath.PathA, 3f, 3.6f)]
    [TestCase(WeaponUpgradePath.PathA, 9f, 10.8f)]
    [TestCase(WeaponUpgradePath.PathB, 3f, 2.7f)]
    [TestCase(WeaponUpgradePath.PathB, 9f, 8.1f)]
    public void LegacyAbilityRadius_ChangesActualQHitBoundaryWithExistingPathComposition(
        WeaponUpgradePath path, float authoredRadius, float resolvedRadius)
    {
        var weapon = CreateWeapon(path, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameActiveRadius = authoredRadius;
        var inside = Target(Vector3.right * (resolvedRadius - .2f), register: true);
        var outside = Target(Vector3.forward * (resolvedRadius + .3f), register: true);
        weapon.UseActiveAbility(Vector3.forward);
        weapon.ActiveWave.Simulate(.6f);
        Assert.That(weapon.ActiveWave.Clock.Radius, Is.EqualTo(resolvedRadius).Within(.002f));
        Assert.That(inside.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        Assert.That(outside.CurrentHealth, Is.EqualTo(InitialHealth));
        inside.gameObject.SetActive(false);
        outside.gameObject.SetActive(false);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f).Within(.003f));
    }

    [TestCase(.3f)]
    [TestCase(1.2f)]
    public void LegacyAbilityExpansionDuration_ChangesActualArrivalAndIsCapturedAtCast(float seconds)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameActiveExpansionDuration = seconds;
        var target = Target(Vector3.right * 3f, register: true);
        weapon.UseActiveAbility(Vector3.forward);
        runtime.Data.Flamethrower.FlameActiveExpansionDuration = 10f;
        weapon.ActiveWave.Simulate(seconds * .5f - .02f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth));
        weapon.ActiveWave.Simulate(.04f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
        weapon.ActiveWave.Simulate(seconds);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 20));
    }

    [TestCase(.2f)]
    [TestCase(.9f)]
    public void LegacyAutomaticRangeFraction_ChangesAutomaticMotionWithoutChangingManual(float fraction)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameAutomaticRangeMultiplier = fraction;
        Emit(weapon, runtime, WeaponStatMode.Automatic);
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f * fraction).Within(.003f));
        weapon.ClearManualAreas();
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f).Within(.003f));
    }

    [TestCase(nameof(FlamethrowerTuning.FlameAutoTickInterval), WeaponStatMode.Automatic, 0f, .2f, 4)]
    [TestCase(nameof(FlamethrowerTuning.FlameAutoTickInterval), WeaponStatMode.Automatic, 0f, .8f, 1)]
    [TestCase(nameof(FlamethrowerTuning.FlameManualTickInterval), WeaponStatMode.Manual, 0f, .2f, 4)]
    [TestCase(nameof(FlamethrowerTuning.FlameManualTickInterval), WeaponStatMode.Manual, 0f, .8f, 1)]
    [TestCase(nameof(FlamethrowerTuning.FlameOverheatAutoTickInterval), WeaponStatMode.Automatic, 1f, .2f, 4)]
    [TestCase(nameof(FlamethrowerTuning.FlameOverheatAutoTickInterval), WeaponStatMode.Automatic, 1f, .8f, 1)]
    public void LegacyTickInterval_ChangesOnlyItsModeAndHeatClocks(string field, WeaponStatMode mode,
        float heatFraction, float interval, int hits)
    {
        Action<FlamethrowerTuning> change = tuning => typeof(FlamethrowerTuning).GetField(field).SetValue(tuning, interval);
        Assert.That(MeasureLegacyContactHits(mode, heatFraction, change), Is.EqualTo(hits));
        var otherMode = mode == WeaponStatMode.Manual ? WeaponStatMode.Automatic : WeaponStatMode.Manual;
        Assert.That(MeasureLegacyContactHits(otherMode, heatFraction, change), Is.EqualTo(1));
        if (field == nameof(FlamethrowerTuning.FlameOverheatAutoTickInterval))
            Assert.That(MeasureLegacyContactHits(WeaponStatMode.Automatic, 0f, change), Is.EqualTo(1));
        if (field == nameof(FlamethrowerTuning.FlameAutoTickInterval))
            Assert.That(MeasureLegacyContactHits(WeaponStatMode.Automatic, 1f, change), Is.EqualTo(1));
    }

    [TestCase(60f, 4)]
    [TestCase(80f, 1)]
    public void LegacyOverheatThreshold_ChangesAutomaticClockAtTheSameHeatAndLeavesManualUnchanged(float threshold, int hits)
    {
        Action<FlamethrowerTuning> change = tuning =>
        {
            tuning.FlameAutoTickInterval = .8f;
            tuning.FlameOverheatAutoTickInterval = .2f;
            tuning.FlameOverheatTickThresholdPercent = threshold;
        };
        Assert.That(MeasureLegacyContactHits(WeaponStatMode.Automatic, .7f, change), Is.EqualTo(hits));
        Assert.That(MeasureLegacyContactHits(WeaponStatMode.Manual, .7f, change), Is.EqualTo(1));
    }

    private int MeasureLegacyContactHits(WeaponStatMode mode, float heatFraction, Action<FlamethrowerTuning> change)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out var stats);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        var tuning = runtime.Data.Flamethrower;
        tuning.FlameAutoTickInterval = .9f;
        tuning.FlameManualTickInterval = .9f;
        tuning.FlameOverheatAutoTickInterval = .9f;
        tuning.FlameOverheatTickThresholdPercent = 75f;
        change(tuning);
        var heat = stats.gameObject.AddComponent<HeatManager>();
        // Flame thresholds consume the segmented display fraction, not the linear point ratio.
        float heatPoints = heatFraction <= .8f
            ? heat.PointsFirstSegment * (heatFraction / .8f)
            : heat.PointsFirstSegment + heat.PointsSecondSegment * ((heatFraction - .8f) / .2f);
        SetField(heat, "_currentHeat", heatPoints);
        Assert.That(heat.NormalizedHeat, Is.EqualTo(heatFraction).Within(.0001f),
            "The cadence fixture must place heat at the requested displayed percentage.");
        weapon.Setup(runtime, stats.transform, stats, heat);
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, mode);
        weapon.ManualAreas.Simulate(.74f);
        int damage = InitialHealth - target.CurrentHealth;
        weapon.ClearManualAreas();
        target.gameObject.SetActive(false);
        Assert.That(damage % 10, Is.Zero);
        return damage / 10;
    }

    [TestCase(WeaponStatMode.Manual, .2f, 2)]
    [TestCase(WeaponStatMode.Manual, .8f, 8)]
    [TestCase(WeaponStatMode.Ability, .2f, 2)]
    [TestCase(WeaponStatMode.Ability, .8f, 8)]
    public void LegacyBurnDamageScale_ChangesActualBurnHpAndPreservesDirectDamage(WeaponStatMode mode, float scale, int burnDamage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnDamageScale = scale;
        var target = Target(mode == WeaponStatMode.Ability ? Vector3.right : Vector3.zero, register: mode == WeaponStatMode.Ability);
        if (mode == WeaponStatMode.Ability)
        {
            weapon.UseActiveAbility(Vector3.forward);
            weapon.ActiveWave.Simulate(.2f);
        }
        else
        {
            Emit(weapon, runtime, mode);
            weapon.ManualAreas.Simulate(.01f);
            weapon.ClearManualAreas();
        }
        int directDamage = mode == WeaponStatMode.Ability ? 20 : 10;
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - directDamage));
        target.GetComponent<FlamethrowerBurnStatus>().Tick(.21f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - directDamage - burnDamage));
    }

    [TestCase(.1f, 4)]
    [TestCase(.4f, 1)]
    public void LegacyBurnTickInterval_ChangesStatusTickCountWithoutChangingContactClock(float interval, int statusHits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnTickInterval = interval;
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.41f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10));
        weapon.ClearManualAreas();
        target.GetComponent<FlamethrowerBurnStatus>().Tick(.41f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10 - 4 * statusHits));
    }

    [TestCase(.35f, 1)]
    [TestCase(.85f, 4)]
    public void LegacyBurnDuration_ChangesLastActualDamageTickWithoutChangingDirectHit(float duration, int statusHits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        runtime.Data.Flamethrower.FlameBurnDuration = duration;
        var target = Target(Vector3.zero);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.01f);
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10));
        weapon.ClearManualAreas();
        for (int i = 0; i < 6; i++)
        {
            var burn = target.GetComponent<FlamethrowerBurnStatus>();
            if (burn != null) burn.Tick(.21f);
        }
        Assert.That(target.CurrentHealth, Is.EqualTo(InitialHealth - 10 - 4 * statusHits));
        Assert.That(target.GetComponent<FlamethrowerBurnStatus>(), Is.Null);
    }

    [TestCase(0f)]
    [TestCase(4f)]
    [TestCase(20f)]
    public void LegacyManualAmmoDrain_ChangesHeldConsumptionWithoutChangingAutomaticOrQCost(float unitsPerSecond)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameManualAmmoPerSecond = unitsPerSecond;
        weapon.TickManual(.25f, Vector3.forward, true);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(100f - unitsPerSecond * .25f).Within(.001f));
        Assert.That(weapon.ActiveManualAreaCount, Is.GreaterThan(0));
        float afterManual = runtime.CurrentAmmo;
        Emit(weapon, runtime, WeaponStatMode.Automatic);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(afterManual));
        runtime.State = WeaponState.Manual;
        weapon.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(afterManual - 40f).Within(.001f));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void LegacyAreaStoppingTime_ChangesActualMotionAndKeepsFinalRangeAndGrowthIndependent(float seconds)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.Flamethrower.FlameAreaLifetime = 3f;
        runtime.Data.Flamethrower.FlameAreaTimeToStop = seconds;
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.25f);
        float progress = .25f / seconds;
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f * (2f * progress - progress * progress)).Within(.003f));
        Assert.That(weapon.ManualAreas.GetRadius(0), Is.EqualTo(.42875f).Within(.002f));
        weapon.ManualAreas.Simulate(seconds);
        Assert.That(weapon.ManualAreas.GetCenter(0).z, Is.EqualTo(7f).Within(.003f));
        Assert.That(weapon.ManualAreas.GetVelocity(0).magnitude, Is.LessThan(.003f));
    }

    [TestCase(true, .3f, false)]
    [TestCase(true, 1.1f, true)]
    [TestCase(false, .4f, false)]
    [TestCase(false, 1.2f, true)]
    public void LegacyAreaRadius_ChangesActualTriggerAndContactHitBoundary(bool initial, float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        var tuning = runtime.Data.Flamethrower;
        tuning.FlameAreaInitialRadius = .2f;
        tuning.FlameAreaFinalRadius = 1.4f;
        tuning.FlameAreaGrowthDuration = .4f;
        tuning.FlameManualTickInterval = .4f;
        if (initial) tuning.FlameAreaInitialRadius = radius;
        else tuning.FlameAreaFinalRadius = radius;
        var target = Target(Vector3.right * .8f);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        float elapsed = initial ? .01f : .41f;
        weapon.ManualAreas.Simulate(elapsed);
        float expectedRadius = initial ? Mathf.Lerp(radius, 1.4f, .025f) : radius;
        Assert.That(weapon.ManualAreas.GetRadius(0), Is.EqualTo(expectedRadius).Within(.002f));
        var trigger = weapon.ManualAreas.GetComponentInChildren<SphereCollider>();
        Assert.That(trigger.radius * trigger.transform.lossyScale.x, Is.EqualTo(expectedRadius).Within(.002f));
        Assert.That(target.CurrentHealth, Is.EqualTo(hit ? InitialHealth - 10 : InitialHealth));
    }

    [TestCase(.2f, 1.2f, true)]
    [TestCase(.8f, .575f, false)]
    public void LegacyAreaGrowthDuration_ChangesActualArrivalAtDamageBoundary(float seconds, float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var runtime, out _);
        AddRow(runtime);
        runtime.Data.BaseRange = 0f;
        var tuning = runtime.Data.Flamethrower;
        tuning.FlameAreaInitialRadius = .2f;
        tuning.FlameAreaFinalRadius = 1.2f;
        tuning.FlameAreaGrowthDuration = seconds;
        tuning.FlameManualTickInterval = .3f;
        var target = Target(Vector3.right * .8f);
        Emit(weapon, runtime, WeaponStatMode.Manual);
        weapon.ManualAreas.Simulate(.3f);
        Assert.That(weapon.ManualAreas.GetRadius(0), Is.EqualTo(radius).Within(.002f));
        Assert.That(target.CurrentHealth, Is.EqualTo(hit ? InitialHealth - 10 : InitialHealth));
        Assert.That(weapon.ManualAreas.GetCenter(0), Is.EqualTo(Vector3.zero));
    }

}
