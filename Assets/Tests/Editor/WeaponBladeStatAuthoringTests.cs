using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Consumer-level Blade stat checks. Production tuning assets are never edited.</summary>
public sealed class WeaponBladeStatAuthoringTests
{
    private const int TargetHealth = 10000;
    private readonly List<Object> _cleanup = new();
    private readonly List<EnemyHealth> _targets = new();
    private readonly List<RotatingBladeWeapon> _weapons = new();
    private UnityEngine.Random.State _random;

    [SetUp]
    public void SetUp()
    {
        _random = UnityEngine.Random.state;
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Blade stat challenge tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (EnemyHealth target in _targets)
            if (target != null) EnemyRegistry.Unregister(target.transform);
        foreach (RotatingBladeWeapon weapon in _weapons)
        {
            var vfx = Field<RotatingBladeVfx>(weapon, "_vfx");
            if (vfx != null) Object.DestroyImmediate(vfx.gameObject);
        }
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear(); _targets.Clear(); _weapons.Clear();
        UnityEngine.Random.state = _random;
    }

    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.None)]
    [TestCase(WeaponStatMode.Manual, WeaponUpgradePath.None)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.None)]
    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.PathA)]
    [TestCase(WeaponStatMode.Manual, WeaponUpgradePath.PathA)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.PathA)]
    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.PathB)]
    [TestCase(WeaponStatMode.Manual, WeaponUpgradePath.PathB)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.PathB)]
    public void ModeDamage_ChangesActualHealthAndMatchesDiagnosticsWithoutDoublePathFactors(WeaponStatMode mode, WeaponUpgradePath path)
    {
        foreach (float amount in new[] { 7f, 23f })
        {
            var weapon = CreateWeapon(path, out var row);
            SetSingleBladeGeometry(row);
            Mode(row, mode).Damage = Enabled(amount);
            var target = Target(Owner(weapon).position + Vector3.forward * 2f);
            int expected = Mathf.Max(1, Mathf.RoundToInt(amount)); // Existing minimum damage contract.
            Fire(weapon, mode);
            Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(expected), $"{mode}/{path}/{amount}");
            Assert.That(Number(weapon, ModeName(mode), "Damage / hit (non-critical)"), Is.EqualTo(expected));
            string otherMode = mode == WeaponStatMode.Ability ? "Manual" : "Active Ability";
            float otherDamage = Number(weapon, otherMode, "Damage / hit (non-critical)");
            Assert.That(otherDamage, Is.EqualTo(mode == WeaponStatMode.Ability && path == WeaponUpgradePath.PathB ? 20f
                : mode == WeaponStatMode.Ability ? 10f : 15f));
            target.gameObject.SetActive(false);
        }
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void CriticalControls_ChangeActualHealthInEveryModeWithAtomicDamageOverride(WeaponStatMode mode)
    {
        foreach (float chance in new[] { 0f, 1f })
        foreach (float severity in new[] { 1.5f, 3f })
        {
            var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
            SetSingleBladeGeometry(row);
            Mode(row, mode).Damage = Enabled(8f);
            row.CriticalChance = Enabled(chance);
            row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
            row.CriticalDamageMultiplier = Enabled(severity);
            var target = Target(Owner(weapon).position + Vector3.forward * 2f);
            Fire(weapon, mode);
            int expected = Mathf.RoundToInt(8f * (chance == 1f ? 2f * severity : 1f));
            Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(expected));
            Assert.That(Number(weapon, ModeName(mode), chance == 1f ? "Damage / hit (critical)" : "Damage / hit (non-critical)"), Is.EqualTo(expected));
            target.gameObject.SetActive(false);
        }
    }

    [TestCase(2f, false)]
    [TestCase(4f, true)]
    public void ManualRange_ChangesRealSlashBoundaryOnlyWhenIndependentQIsAuthored(float range, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Manual.RangeMeters = Enabled(range);
        row.Blade.ThrustLengthMeters = Enabled(9f);
        var target = Target(Owner(weapon).position + Vector3.forward * 3f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(range));
        Assert.That(Number(weapon, "Active Ability", "Range"), Is.EqualTo(9f));
    }

    [TestCase(0f, false)]
    [TestCase(2f, true)]
    [TestCase(4f, false)]
    public void OrbitRadius_MovesActualAutomaticContactWithoutChangingManualRange(float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(radius);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.1f);
        row.Blade.RotationsPerSecond = Enabled(0f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(radius));
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(3f));
    }

    [TestCase(0f, false)]
    [TestCase(.1f, false)]
    [TestCase(.5f, true)]
    public void AutomaticHitRadius_ChangesRealContactEnvelope(float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(radius);
        row.Blade.RotationsPerSecond = Enabled(0f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2.3f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Automatic", "Hit radius"), Is.EqualTo(radius));
        var vfx = Field<RotatingBladeVfx>(weapon, "_vfx");
        Assert.That(vfx.VisiblePhysicalBladeCount, Is.EqualTo(radius > 0f ? 1 : 0));
        if (radius > 0f)
        {
            var line = vfx.transform.Find("Blade Contact 1").GetComponent<LineRenderer>();
            Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(radius * 2f).Within(.001f));
        }
    }

    [TestCase(0f, false, 0f)]
    [TestCase(.5f, false, 45f)]
    [TestCase(1f, true, 90f)]
    [TestCase(1000f, true, 0f)]
    public void RotationRate_ControlsAngularMotionAndFullRevolutionsStillHit(float rotations, bool hit, float angle)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.05f);
        row.Blade.RotationsPerSecond = Enabled(rotations);
        row.Automatic.AttackCyclesPerSecond = Enabled(4f);
        var target = Target(Owner(weapon).position + Vector3.right * 2f);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(.25f, Vector3.forward);
        Assert.That(weapon.SpinAngle, Is.EqualTo(angle).Within(.001f));
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Automatic", "Action / tick interval"), Is.EqualTo(.25f));
        Assert.That(Number(weapon, "Automatic", "Rotations per second"), Is.EqualTo(rotations));
    }

    [Test]
    public void ExtremeRotation_StaysFiniteAndDoesNotMultiplyDamageByRevolutionCount()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.05f);
        row.Blade.RotationsPerSecond = Enabled(float.MaxValue);
        var target = Target(Owner(weapon).position + Vector3.back * 2f);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(.5f, Vector3.forward);
        Assert.That(float.IsNaN(weapon.SpinAngle) || float.IsInfinity(weapon.SpinAngle), Is.False);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10));
    }

    [TestCase(WeaponStatMode.Automatic, 2f, 1)]
    [TestCase(WeaponStatMode.Automatic, 4f, 2)]
    [TestCase(WeaponStatMode.Manual, 2f, 1)]
    [TestCase(WeaponStatMode.Manual, 4f, 2)]
    public void ContactOrSwingRate_ControlsActualHitTimesIndependentlyOfRotation(WeaponStatMode mode, float rate, int count)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        Mode(row, mode).AttackCyclesPerSecond = Enabled(rate);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, mode);
        if (mode == WeaponStatMode.Automatic) weapon.TickAutomatic(.25f, Vector3.forward);
        else weapon.TickManual(.25f, Vector3.forward, true);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10 * count));
        Assert.That(weapon.SpinAngle, Is.Zero);
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(mode == WeaponStatMode.Manual ? 100 - count : 100));
        Mode(row, mode).AttackCyclesPerSecond = Enabled(8f);
        if (mode == WeaponStatMode.Automatic) weapon.TickAutomatic(1f, Vector3.forward);
        else weapon.TickManual(1f, Vector3.forward, true);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10 * (count + 1)), "A changed cadence is consumed without recreating the weapon.");
    }

    [TestCase(WeaponUpgradePath.None, 2f, false)]
    [TestCase(WeaponUpgradePath.None, 5f, true)]
    [TestCase(WeaponUpgradePath.PathB, 0f, false)]
    [TestCase(WeaponUpgradePath.PathB, 2f, false)]
    [TestCase(WeaponUpgradePath.PathB, 5f, true)]
    public void ThrustOrDashLength_ChangesActualHitReachAndVisualEndpoint(WeaponUpgradePath path, float length, bool hit)
    {
        var weapon = CreateWeapon(path, out var row);
        if (path == WeaponUpgradePath.PathB)
        {
            row.Blade.AtomicDashRangeMeters = Enabled(length);
            row.Blade.AtomicRangePerHitMultiplier = Enabled(0f);
        }
        else row.Blade.ThrustLengthMeters = Enabled(length);
        var target = Target(Owner(weapon).position + Vector3.forward * 4f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        var vfx = Field<RotatingBladeVfx>(weapon, "_vfx");
        var line = vfx.transform.Find("Blade Thrust").GetComponent<LineRenderer>();
        Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(length).Within(.001f));
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(3f));
    }

    [TestCase(WeaponUpgradePath.None, 0f, false)]
    [TestCase(WeaponUpgradePath.None, .2f, false)]
    [TestCase(WeaponUpgradePath.None, .8f, true)]
    [TestCase(WeaponUpgradePath.PathB, 0f, false)]
    [TestCase(WeaponUpgradePath.PathB, .2f, false)]
    [TestCase(WeaponUpgradePath.PathB, .8f, true)]
    public void AbilityRadius_IsActualQueryRadiusWithoutLegacyHalfWidthConversion(WeaponUpgradePath path, float radius, bool hit)
    {
        var weapon = CreateWeapon(path, out var row);
        if (path == WeaponUpgradePath.PathB)
        {
            row.Blade.AtomicDashRangeMeters = Enabled(4f);
            row.Blade.AtomicRangePerHitMultiplier = Enabled(0f);
            row.Blade.AtomicHitRadiusMeters = Enabled(radius);
        }
        else
        {
            row.Blade.ThrustLengthMeters = Enabled(4f);
            row.Blade.AbilityHitRadiusMeters = Enabled(radius);
        }
        var target = Target(Owner(weapon).position + Vector3.forward * 2f + Vector3.right * .5f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Active Ability", "Hit radius"), Is.EqualTo(radius));
        Assert.That(Number(weapon, "Active Ability", "Line width"), Is.EqualTo(radius * 2f));
        var ribbon = Field<RotatingBladeVfx>(weapon, "_vfx").GetComponentsInChildren<MeshFilter>(true)
            .Single(filter => filter.name.StartsWith("Blade Thrust Ribbon"));
        Assert.That(ribbon.sharedMesh.vertices.Max(vertex => Mathf.Abs(vertex.x)), Is.EqualTo(radius).Within(.001f));
        Assert.That(ribbon.GetComponent<MeshRenderer>().enabled, Is.EqualTo(radius > 0f));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(6)]
    public void ExtraBladeCount_ChangesQueuedHitsAndCooldownCommitWithoutChargingExtraAmmo(int extra)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var row);
        row.Blade.ExtraBladeCount = new WeaponOptionalInt { Enabled = true, Value = extra };
        row.Blade.MultiBladeActionIntervalSeconds = Enabled(.2f);
        row.Blade.MultiBladeSpreadAngleDegrees = Enabled(0f);
        row.Blade.ThrustLengthMeters = Enabled(4f);
        row.Ability.Damage = Enabled(6f);
        row.Automatic.Damage = Enabled(90f);
        row.AbilityCooldownSeconds = Enabled(9f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(6));
        if (extra > 0)
        {
            Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.Zero);
            weapon.TickManual(.1f, Vector3.back, false);
            Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(6));
            weapon.Runtime.State = WeaponState.Automatic;
            for (int i = 0; i < extra; i++) weapon.TickAutomatic(.2f, Vector3.back);
        }
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(6 * (1 + extra)), "Queued Q hits retain Ability mode after switching to Automatic.");
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(98f));
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.EqualTo(9f));
        Assert.That(Number(weapon, "Automatic", "Hits / projectiles per action"), Is.EqualTo(1 + extra));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void KnockbackStrength_ChangesActualImpulseAndAtomicStillCannotPush(float strength)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Manual.KnockbackStrength = Enabled(strength);
        row.Manual.Damage = Enabled(16f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        Vector3 displacement = receiver != null ? receiver.ConsumeDisplacement(.1f) : Vector3.zero;
        Assert.That(displacement.magnitude, Is.EqualTo(strength * 2f * .1f).Within(.001f));
        var atomic = CreateWeapon(WeaponUpgradePath.PathB, out var atomicRow);
        atomicRow.Manual.KnockbackStrength = new WeaponOptionalFloat { Value = strength };
        var atomicTarget = Target(Owner(atomic).position + Vector3.forward * 2f);
        Fire(atomic, WeaponStatMode.Manual);
        Assert.That(atomicTarget.GetComponent<EnemyKnockbackReceiver>(), Is.Null);
        atomicRow.Manual.KnockbackStrength.Enabled = true;
        Assert.That(atomic.Runtime.Data.StatProfile.TryValidate(out _), Is.False, "Atomic push remains an unsupported control.");
    }

    [Test]
    public void ExactLevelPathRows_IsolateGeometryCountsAndBasicToBothAdvancedTransitions()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var basic);
        basic.Level = weapon.Runtime.Level = 5;
        basic.Blade.OrbitRadiusMeters = Enabled(5f);
        basic.Blade.ExtraBladeCount = new WeaponOptionalInt { Value = 30 };
        var a6 = AddRow(weapon, 6, WeaponUpgradePath.PathA, 6f, 2);
        var a7 = AddRow(weapon, 7, WeaponUpgradePath.PathA, 7f, 3);
        AddRow(weapon, 8, WeaponUpgradePath.PathA, 8f, 4);
        AddRow(weapon, 7, WeaponUpgradePath.PathB, 17f, 50);
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(5f));
        Assert.That(Number(weapon, "Automatic", "Hits / projectiles per action"), Is.EqualTo(1));
        weapon.Runtime.Level = 6; weapon.Runtime.SelectedPath = WeaponUpgradePath.PathA;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(6f));
        weapon.Runtime.Level = 7;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(7f));
        a7.Blade.OrbitRadiusMeters = Enabled(70f);
        weapon.Runtime.Level = 8;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(8f));
        weapon.Runtime.Level = 7; weapon.Runtime.SelectedPath = WeaponUpgradePath.PathB;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(17f));
        Assert.That(Number(weapon, "Automatic", "Hits / projectiles per action"), Is.EqualTo(1));
        weapon.Runtime.Level = 6;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(2f), "Missing B6 inherits current tuning, not A6 or Basic5.");
        weapon.Runtime.Level = 5;
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(5f));
        Assert.That(a6.Blade.OrbitRadiusMeters.Value, Is.EqualTo(6f));
    }

    [Test]
    public void InheritedGeometry_PreservesLegacyAreaGrowthAndDistinctAbilityWidthConversions()
    {
        foreach (WeaponUpgradePath path in new[] { WeaponUpgradePath.None, WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
        {
            var weapon = CreateWeapon(path, out var row, area: 2f);
            var tuning = weapon.Runtime.Data.RotatingBlade;
            Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(tuning.BladeOrbitRadius * 2f));
            Assert.That(Number(weapon, "Automatic", "Hit radius"), Is.EqualTo(tuning.BladeHitRadius * 2f + tuning.BladeOrbitRadius));
            Assert.That(Number(weapon, "Active Ability", "Hit radius"), Is.EqualTo(tuning.BladeActiveLineWidth * 2f * (path == WeaponUpgradePath.PathB ? .65f : .5f)));
            row.Blade.AutomaticHitRadiusMeters = Enabled(.7f);
            if (path == WeaponUpgradePath.PathB) row.Blade.AtomicHitRadiusMeters = Enabled(.8f);
            else row.Blade.AbilityHitRadiusMeters = Enabled(.8f);
            Assert.That(Number(weapon, "Automatic", "Hit radius"), Is.EqualTo(1.4f));
            Assert.That(Number(weapon, "Active Ability", "Hit radius"), Is.EqualTo(1.6f));
        }
    }

    [TestCase(WeaponStatField.Damage)]
    [TestCase(WeaponStatField.RangeMeters)]
    [TestCase(WeaponStatField.AttackCyclesPerSecond)]
    public void GenericZeroControls_FailValidationBeforeAnyAction(WeaponStatField field)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        if (field == WeaponStatField.Damage) row.Manual.Damage = Enabled(0f);
        else if (field == WeaponStatField.RangeMeters) row.Manual.RangeMeters = Enabled(0f);
        else row.Manual.AttackCyclesPerSecond = Enabled(0f);
        Assert.That(weapon.Runtime.Data.StatProfile.TryValidate(out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.Throws<InvalidOperationException>(() => Fire(weapon, WeaponStatMode.Manual));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(100f));
    }

    [Test]
    public void BladePayload_RejectsInvalidEnabledValuesAndRetainsDisabledSentinels()
    {
        var payload = new WeaponBladeStatOverrides { RotationsPerSecond = new WeaponOptionalFloat { Value = float.NaN } };
        Assert.That(payload.TryValidate(out _), Is.True);
        payload.RotationsPerSecond.Enabled = true;
        Assert.That(payload.TryValidate(out _), Is.False);
        payload.RotationsPerSecond = Enabled(float.MaxValue);
        payload.ExtraBladeCount = new WeaponOptionalInt { Enabled = true, Value = 63 };
        Assert.That(payload.TryValidate(out _), Is.True);
        payload.ExtraBladeCount.Value = 64;
        Assert.That(payload.TryValidate(out _), Is.False);
    }


    [TestCase(10f, false)]
    [TestCase(90f, true)]
    public void ManualConeAngle_ChangesHitSelectionAtFixedRange(float angle, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.ManualConeAngleDegrees = Enabled(angle);
        var target = Target(Owner(weapon).position + Quaternion.AngleAxis(30f, Vector3.up) * Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Manual", "Cone angle"), Is.EqualTo(angle));
    }

    [TestCase(0f, 3)]
    [TestCase(20f, 1)]
    public void MultiBladeSpread_ChangesTheScheduledSwingDirections(float spread, int hits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var row);
        row.Blade.ExtraBladeCount = new WeaponOptionalInt { Enabled = true, Value = 2 };
        row.Blade.ManualConeAngleDegrees = Enabled(10f);
        row.Blade.MultiBladeSpreadAngleDegrees = Enabled(spread);
        row.Blade.MultiBladeActionIntervalSeconds = Enabled(.1f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        weapon.TickManual(.11f, Vector3.back, false);
        weapon.TickManual(.11f, Vector3.back, false);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(hits * 10));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(99f));
    }

    [TestCase(0f, 20)]
    [TestCase(.1f, 20)]
    [TestCase(.3f, 10)]
    public void MultiBladeInterval_ChangesActualSecondSwingTime(float seconds, int damageAtPointOne)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathA, out var row);
        row.Blade.ExtraBladeCount = new WeaponOptionalInt { Enabled = true, Value = 1 };
        row.Blade.MultiBladeSpreadAngleDegrees = Enabled(0f);
        row.Blade.MultiBladeActionIntervalSeconds = Enabled(seconds);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10));
        weapon.TickManual(.1f, Vector3.forward, false);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damageAtPointOne));
        weapon.TickManual(.3f, Vector3.forward, false);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(20));
    }

    [TestCase(0f, false)]
    [TestCase(.5f, true)]
    [TestCase(2f, true)]
    public void AtomicPerHitRange_ChangesTheTargetsReachedByTheSameDash(float extension, bool secondHit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        row.Blade.AtomicDashRangeMeters = Enabled(2f);
        row.Blade.AtomicHitRadiusMeters = Enabled(.1f);
        row.Blade.AtomicRangePerHitMultiplier = Enabled(extension);
        var first = Target(Owner(weapon).position + Vector3.forward);
        var second = Target(Owner(weapon).position + Vector3.forward * 2.8f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(first.CurrentHealth < TargetHealth, Is.True);
        Assert.That(second.CurrentHealth < TargetHealth, Is.EqualTo(secondHit));
    }

    [TestCase(0f, 0f)]
    [TestCase(.1f, .25f)]
    [TestCase(.3f, .5f)]
    public void AtomicDashTiming_ChangesActualMovementAndInvulnerabilityTimers(float segment, float postInvulnerability)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        var owner = Owner(weapon).gameObject;
        owner.AddComponent<Rigidbody>().useGravity = false;
        var movement = owner.AddComponent<PlayerMovement>();
        typeof(PlayerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        var health = owner.AddComponent<PlayerHealth>();
        row.Blade.AtomicDashRangeMeters = Enabled(3f);
        row.Blade.AtomicDashSegmentSeconds = Enabled(segment);
        row.Blade.AtomicPostDashInvulnerabilitySeconds = Enabled(postInvulnerability);
        row.Blade.AtomicRangePerHitMultiplier = Enabled(0f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(Field<float>(movement, "_dashTimer"), Is.EqualTo(segment).Within(.001f));
        Assert.That(Field<float>(health, "_invulnerableUntil"),
            Is.EqualTo(segment + postInvulnerability > 0f ? segment + postInvulnerability + Time.time : 0f).Within(.001f));
    }

    [TestCase(0f, 12)]
    [TestCase(1f, 24)]
    public void ManualAuthoredDamage_PreservesDynamicHeatMultiplier(float normalizedHeat, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        var heat = Track(new GameObject("Blade stat heat")).AddComponent<HeatManager>();
        SetField(heat, "_currentHeat", normalizedHeat > 0f ? heat.TotalHeatCapacity : 0f);
        weapon.Setup(weapon.Runtime, Owner(weapon), Field<PlayerStats>(weapon, "Stats"), heat);
        row.Manual.Damage = Enabled(12f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damage));
        Assert.That(Number(weapon, "Manual", "Damage / hit (non-critical)"), Is.EqualTo(damage));
    }

    [Test]
    public void InvalidAbilityProfile_FailsBeforeSpendingAmmoOrCreatingEffects()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Ability.Damage = Enabled(0f);
        Assert.Throws<InvalidOperationException>(() => weapon.UseActiveAbility(Vector3.forward));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(100f));
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.Zero);
        Assert.That(Field<RotatingBladeVfx>(weapon, "_vfx"), Is.Null);
    }

    [TestCase(WeaponUpgradePath.None, 3f)]
    [TestCase(WeaponUpgradePath.PathB, 9f)]
    public void ManualRowRange_IsIndependentWhileQStillInheritsTheLegacyGlobalSource(WeaponUpgradePath path, float legacyAbilityRange)
    {
        var weapon = CreateWeapon(path, out var row);
        row.Manual.RangeMeters = Enabled(2f);
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(2f));
        Assert.That(Number(weapon, "Active Ability", "Range"), Is.EqualTo(legacyAbilityRange));
        weapon.Runtime.Data.RotatingBlade.BladeManualRange = 4f;
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(2f));
        Assert.That(Number(weapon, "Active Ability", "Range"), Is.EqualTo(path == WeaponUpgradePath.PathB ? 12f : 4f));
        var target = Target(Owner(weapon).position + Vector3.forward * 3.5f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.True);
    }

    [TestCase(0f)]
    [TestCase(2f)]
    [TestCase(5f)]
    public void ManualAmmoCapacity_DrivesActualExhaustionAndManagerRefill(float capacity)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Manual.AmmoCapacity = Enabled(capacity);
        var manager = Owner(weapon).gameObject.AddComponent<WeaponManager>();
        SetField(manager, "_stats", Field<PlayerStats>(weapon, "Stats"));
        Field<List<IWeaponBehaviour>>(manager, "_equipped").Add(weapon);
        manager.RefillManualAmmoAndResetActiveCooldown();
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(capacity));
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        for (int i = 0; i < 7; i++) weapon.TickManual(1f, Vector3.forward, true);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10 * capacity));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.Zero);
        typeof(WeaponManager).GetMethod("StartManualMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, new object[] { 0 });
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(capacity));
        Assert.That(Number(weapon, "Runtime", "Maximum manual ammo"), Is.EqualTo(capacity));
        weapon.TickManual(1f, Vector3.forward, true);
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(Mathf.Max(0f, capacity - 1)));
    }

    [TestCase(WeaponStatMode.Automatic, 144)]
    [TestCase(WeaponStatMode.Manual, 144)]
    [TestCase(WeaponStatMode.Ability, 216)]
    public void CombinedPlayerBonuses_ApplyOnceAfterAuthoredNeutralValues(WeaponStatMode mode, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        SetSingleBladeGeometry(row);
        var stats = Field<PlayerStats>(weapon, "Stats");
        foreach (var pair in new[] { (StatType.DamageMultiplier, 2f), (StatType.AbilityDamageMultiplier, 1.5f),
            (StatType.ProjectileAreaSize, 1.5f), (StatType.AttackSpeedMultiplier, 2f),
            (StatType.AmmoMultiplier, 2f), (StatType.AbilityCooldownReduction, .25f) })
        {
            Assert.That(stats.TryGetRuntimeStat(pair.Item1, out RuntimeStat value), Is.True);
            value.SetBaseValue(pair.Item2);
        }
        Mode(row, mode).Damage = Enabled(12f);
        row.Automatic.AttackCyclesPerSecond = Enabled(4f);
        row.Manual.AttackCyclesPerSecond = Enabled(4f);
        row.Manual.AmmoCapacity = Enabled(10f);
        row.AbilityCooldownSeconds = Enabled(8f);
        row.CriticalChance = Enabled(1f);
        row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        row.CriticalDamageMultiplier = Enabled(3f);
        var target = Target(Owner(weapon).position + Vector3.forward * 3f);
        Fire(weapon, mode);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damage));
        Assert.That(Number(weapon, ModeName(mode), "Damage / hit (critical)"), Is.EqualTo(damage));
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(3f));
        Assert.That(Number(weapon, "Automatic", "Action / tick interval"), Is.EqualTo(.125f));
        Assert.That(Number(weapon, "Runtime", "Maximum manual ammo"), Is.EqualTo(20f));
        Assert.That(Number(weapon, "Active Ability", "Ability cooldown duration"), Is.EqualTo(6f));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void AuthoredAtomicMotion_TravelsItsResolvedFinalRangeOverTheResolvedDuration(float extension)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        var owner = Owner(weapon).gameObject;
        var body = owner.AddComponent<Rigidbody>();
        body.useGravity = false;
        var movement = owner.AddComponent<PlayerMovement>();
        typeof(PlayerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        row.Blade.AtomicDashRangeMeters = Enabled(3f);
        row.Blade.AtomicRangePerHitMultiplier = Enabled(extension);
        row.Blade.AtomicDashSegmentSeconds = Enabled(.2f);
        var target = Target(owner.transform.position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.True);
        Assert.That(Field<float>(movement, "_dashTimer"), Is.EqualTo(.4f).Within(.001f));
        Assert.That(body.linearVelocity.magnitude * Field<float>(movement, "_dashTimer"),
            Is.EqualTo(3f * (1f + extension)).Within(.001f));
    }

    [TestCase(0f, false)]
    [TestCase(.5f, false)]
    [TestCase(1f, true)]
    [TestCase(180f, true)]
    [TestCase(181f, false)]
    public void ManualConeValidation_RejectsUnsupportedAnglesRatherThanClampingVfx(float angle, bool valid)
    {
        var payload = new WeaponBladeStatOverrides { ManualConeAngleDegrees = Enabled(angle) };
        Assert.That(payload.TryValidate(out _), Is.EqualTo(valid));
        payload.ManualConeAngleDegrees.Enabled = false;
        Assert.That(payload.TryValidate(out _), Is.True, "Disabled values are retained without changing legacy visuals.");
    }
    [Test]
    public void AuthoredAtomicOverlap_RestartsDashWindowAndTravelsTheNewRangeWithoutClearingInvulnerability()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        var owner = Owner(weapon).gameObject;
        var body = owner.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.linearDamping = 0f;
        var movement = owner.AddComponent<PlayerMovement>();
        typeof(PlayerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        var health = owner.AddComponent<PlayerHealth>();
        row.AbilityCooldownSeconds = Enabled(0f);
        row.Blade.AtomicDashRangeMeters = Enabled(8f);
        row.Blade.AtomicDashSegmentSeconds = Enabled(.5f);
        row.Blade.AtomicPostDashInvulnerabilitySeconds = Enabled(.25f);
        Fire(weapon, WeaponStatMode.Ability);
        float protectedUntil = Field<float>(health, "_invulnerableUntil");
        Assert.That(Field<float>(movement, "_dashTimer"), Is.EqualTo(.5f));
        row.Blade.AtomicDashRangeMeters = Enabled(2f);
        row.Blade.AtomicDashSegmentSeconds = Enabled(.125f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(Field<float>(movement, "_dashTimer"), Is.EqualTo(.125f));
        Assert.That(Field<float>(health, "_invulnerableUntil"), Is.EqualTo(protectedUntil));
        float oldStep = Time.fixedDeltaTime;
        SimulationMode oldPhysicsMode = Physics.simulationMode;
        try
        {
            // EditMode cannot create a runtime local-physics scene. Use the same
            // explicit simulation path as the existing movement and Flame fixtures.
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();
            Vector3 start = body.position;
            Time.fixedDeltaTime = .03125f;
            var timer = typeof(PlayerMovement).GetMethod("HandleDashTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 4; i++)
            {
                Physics.Simulate(Time.fixedDeltaTime);
                timer.Invoke(movement, null);
            }
            Assert.That(Vector3.Distance(body.position, start), Is.EqualTo(2f).Within(.002f));
            Assert.That(movement.IsDashing, Is.False, "The timed dash ends; existing post-dash momentum is unchanged.");
        }
        finally
        {
            Time.fixedDeltaTime = oldStep;
            Physics.simulationMode = oldPhysicsMode;
        }
    }

    [Test]
    public void InheritedAtomicOverlap_KeepsTheExistingLongerDashTimer()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out var row);
        var owner = Owner(weapon).gameObject;
        owner.AddComponent<Rigidbody>().useGravity = false;
        var movement = owner.AddComponent<PlayerMovement>();
        typeof(PlayerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
        row.AbilityCooldownSeconds = Enabled(0f);
        var target = Target(owner.transform.position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Ability);
        float firstTimer = Field<float>(movement, "_dashTimer");
        EnemyRegistry.Unregister(target.transform);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(firstTimer, Is.EqualTo(.32f).Within(.001f));
        Assert.That(Field<float>(movement, "_dashTimer"), Is.EqualTo(firstTimer));
        Assert.That(movement.CurrentVelocity.magnitude, Is.EqualTo(9f / .16f).Within(.001f));
    }
    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(7f)]
    public void AbilityAmmoCost_ChangesActualSpendWithoutChangingManualCapacityOrOtherRows(float cost)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.AbilityAmmoCost = Enabled(cost);
        row.Manual.AmmoCapacity = Enabled(40f);
        var nextRow = new WeaponStatLevelRow { WeaponType = WeaponType.RotatingBlade, Level = 2, Path = WeaponUpgradePath.None };
        nextRow.AbilityAmmoCost = Enabled(4f);
        weapon.Runtime.Data.StatProfile.Levels.Add(nextRow);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(100f - cost));
        Assert.That(Number(weapon, "Active Ability", "Ammo / action"), Is.EqualTo(cost));
        Assert.That(Number(weapon, "Runtime", "Maximum manual ammo"), Is.EqualTo(40f));
        weapon.Runtime.Level = 2;
        weapon.Runtime.AbilityCooldownTimer = 0f;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(96f - cost));
        Assert.That(Number(weapon, "Active Ability", "Ammo / action"), Is.EqualTo(4f));
        Assert.That(Number(weapon, "Runtime", "Maximum manual ammo"), Is.EqualTo(100f));
    }
    [TestCase(.25f)]
    [TestCase(1f)]
    [TestCase(1000f)]
    public void AuthoredSpin_NarrowContactRadiusDoesNotTunnelBetweenPartialOrFullArcChords(float rotations)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.0001f);
        row.Blade.RotationsPerSecond = Enabled(rotations);
        Vector3 radial = Quaternion.AngleAxis(1f, Vector3.up) * Vector3.forward;
        var onArc = Target(Owner(weapon).position + radial * 2f);
        var outside = Target(Owner(weapon).position + radial * 2.01f);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(1f, Vector3.forward);
        Assert.That(TargetHealth - onArc.CurrentHealth, Is.EqualTo(10));
        Assert.That(outside.CurrentHealth, Is.EqualTo(TargetHealth));
    }
    [Test]
    public void InheritedFullRotation_RetainsLegacyEndpointSampling()
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        weapon.Runtime.Data.RotatingBlade.BladeBaseSpinDegreesPerSecond = 360f;
        row.Blade.RotationsPerSecond = new WeaponOptionalFloat { Value = 1000f };
        var target = Target(Owner(weapon).position + Vector3.right * 2f);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(1f, Vector3.forward);
        Assert.That(weapon.SpinAngle, Is.Zero);
        Assert.That(target.CurrentHealth, Is.EqualTo(TargetHealth), "The new arc correction requires an explicit spin override.");
    }
    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void GeometrySnapshot_UsesActualModeDimensionsAndExplicitManualConvergenceOnly(WeaponUpgradePath path)
    {
        var weapon = CreateWeapon(path, out var row, 2f);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.3f);
        row.Manual.RangeMeters = Enabled(5f);
        if (path == WeaponUpgradePath.PathB)
        {
            row.Blade.AtomicDashRangeMeters = Enabled(3f);
            row.Blade.AtomicHitRadiusMeters = Enabled(.4f);
        }
        else
        {
            row.Blade.ThrustLengthMeters = Enabled(3f);
            row.Blade.AbilityHitRadiusMeters = Enabled(.4f);
        }
        var beforeRandom = UnityEngine.Random.state;
        WeaponGeometrySnapshot geometry = weapon.CaptureGeometry();
        var sweep = geometry.Guides.Single(g => g.Shape == WeaponGeometryShape.SweptCircle);
        Assert.That(sweep.RangeMeters, Is.EqualTo(Number(weapon, "Automatic", "Range")));
        Assert.That(sweep.RadiusMeters, Is.EqualTo(Number(weapon, "Automatic", "Hit radius")));
        var manual = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Manual);
        Assert.That(manual.RangeMeters, Is.EqualTo(10f));
        Assert.That(manual.Direction, Is.EqualTo(WeaponGeometryDirection.HorizontalAim));
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.EqualTo(10f));
        var ability = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Ability);
        Assert.That(ability.RangeMeters, Is.EqualTo(Number(weapon, "Active Ability", "Range")));
        Assert.That(ability.RadiusMeters, Is.EqualTo(Number(weapon, "Active Ability", "Hit radius")));
        Assert.That(ability.Direction, Is.EqualTo(path == WeaponUpgradePath.PathB
            ? WeaponGeometryDirection.HorizontalAim : WeaponGeometryDirection.Aim));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(100f));
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.Zero);
        Assert.That(Field<RotatingBladeVfx>(weapon, "_vfx"), Is.Null);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeRandom));
        row.Manual.RangeMeters.Enabled = false;
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.Null);
        Assert.That(weapon.CaptureGeometry().Guides.Single(g => g.Mode == WeaponStatMode.Manual).RangeMeters, Is.EqualTo(6f));
    }
    [TestCase(WeaponUpgradePath.None, 0f, 1)]
    [TestCase(WeaponUpgradePath.None, 2.5f, 25)]
    [TestCase(WeaponUpgradePath.PathA, 0f, 1)]
    [TestCase(WeaponUpgradePath.PathA, 2.5f, 25)]
    [TestCase(WeaponUpgradePath.PathB, 0f, 15)]
    [TestCase(WeaponUpgradePath.PathB, 2.5f, 15)]
    public void LegacyActiveDamageScale_MutatesActualThrustDamageAndAtomicRetainsItsOwnScale(WeaponUpgradePath path, float scale, int damage)
    {
        var weapon = CreateWeapon(path, out var row);
        SetSingleBladeGeometry(row);
        weapon.Runtime.Data.RotatingBlade.BladeActiveDamageScale = scale;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damage));
        Assert.That(Number(weapon, "Active Ability", "Damage / hit (non-critical)"), Is.EqualTo(damage));
        int beforeManual = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(beforeManual - target.CurrentHealth, Is.EqualTo(path == WeaponUpgradePath.PathB ? 20 : 10));
        row.Ability.Damage = Enabled(7f);
        weapon.Runtime.AbilityCooldownTimer = 0f;
        int beforeAuthored = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(beforeAuthored - target.CurrentHealth, Is.EqualTo(7), "An explicit Q neutral damage replaces the inherited mode scale.");
    }

    [TestCase(20f, 15f, true)]
    [TestCase(40f, 9f, false)]
    [TestCase(80f, 6f, false)]
    public void LegacyActiveHeatStepPercent_MutatesActualThrustReachAndEndpoint(float stepPercent, float range, bool hitsDistantTarget)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        var heat = AttachLegacyControlHeat(weapon, false);
        SetField(heat, "_currentHeat", heat.PointsFirstSegment);
        Assert.That(heat.NormalizedHeat, Is.EqualTo(.8f));
        weapon.Runtime.Data.RotatingBlade.BladeActiveHeatStepPercent = stepPercent;
        weapon.Runtime.Data.RotatingBlade.BladeActiveMaxRangeMultiplier = 10f;
        var distant = Target(Owner(weapon).position + Vector3.forward * 10f);
        var insideEnd = Target(Owner(weapon).position + Vector3.forward * (range - .1f));
        var outsideEnd = Target(Owner(weapon).position + Vector3.forward * (range + .4f));
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(distant.CurrentHealth < TargetHealth, Is.EqualTo(hitsDistantTarget));
        Assert.That(insideEnd.CurrentHealth < TargetHealth, Is.True);
        Assert.That(outsideEnd.CurrentHealth, Is.EqualTo(TargetHealth));
        var line = Field<RotatingBladeVfx>(weapon, "_vfx").transform.Find("Blade Thrust").GetComponent<LineRenderer>();
        Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(range).Within(.001f));
        Assert.That(Number(weapon, "Active Ability", "Range"), Is.EqualTo(range));
        Assert.That(Number(weapon, "Manual", "Range"), Is.EqualTo(3f));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void LegacyAutoKnockbackScale_MutatesConsumedAutomaticImpulseOnly(float scale)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        weapon.Runtime.Data.BaseKnockback = 1f;
        weapon.Runtime.Data.RotatingBlade.BladeAutoKnockbackScale = scale;
        weapon.Runtime.Data.RotatingBlade.BladeManualKnockbackScale = .5f;
        row.Automatic.Damage = row.Manual.Damage = Enabled(16f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(16));
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.2f * scale).Within(.001f));
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.1f).Within(.001f));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void LegacyAutoMaxHeatKnockbackBonus_MutatesHotImpulseAndLeavesColdImpulseUnchanged(float bonus)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        var heat = AttachLegacyControlHeat(weapon, true);
        weapon.Runtime.Data.BaseKnockback = 1f;
        weapon.Runtime.Data.RotatingBlade.BladeAutoKnockbackScale = .25f;
        weapon.Runtime.Data.RotatingBlade.BladeAutoMaxHeatKnockbackBonus = bonus;
        row.Automatic.Damage = Enabled(16f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.2f * (.25f + bonus)).Within(.001f));
        SetField(heat, "_currentHeat", 0f);
        weapon.TickAutomatic(1f, Vector3.forward);
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.05f).Within(.001f));
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(32));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void LegacyManualKnockbackScale_MutatesConsumedSlashImpulseOnly(float scale)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        weapon.Runtime.Data.BaseKnockback = 1f;
        weapon.Runtime.Data.RotatingBlade.BladeManualKnockbackScale = scale;
        weapon.Runtime.Data.RotatingBlade.BladeAutoKnockbackScale = .5f;
        row.Automatic.Damage = row.Manual.Damage = Enabled(16f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(16));
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.2f * scale).Within(.001f));
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(.1f).Within(.001f));
    }

    [TestCase(0f, 10)]
    [TestCase(1f, 20)]
    [TestCase(3f, 40)]
    public void LegacyManualMaxHeatDamageBonus_MutatesActualHotSlashDamageOnly(float bonus, int damage)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        AttachLegacyControlHeat(weapon, true);
        weapon.Runtime.Data.RotatingBlade.BladeManualMaxHeatDamageBonus = bonus;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damage));
        Assert.That(Number(weapon, "Manual", "Damage / hit (non-critical)"), Is.EqualTo(damage));
        int beforeAuto = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(beforeAuto - target.CurrentHealth, Is.EqualTo(10));
        int beforeAbility = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(beforeAbility - target.CurrentHealth, Is.EqualTo(15));
    }

    [TestCase(WeaponStatMode.Automatic, .5f, 1)]
    [TestCase(WeaponStatMode.Automatic, .1f, 2)]
    [TestCase(WeaponStatMode.Manual, .5f, 1)]
    [TestCase(WeaponStatMode.Manual, .1f, 2)]
    public void LegacyModeIntervals_ChangeActualHitTimesWithoutChangingTheOtherMode(WeaponStatMode mode, float interval, int hits)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        SetSingleBladeGeometry(row);
        var tuning = weapon.Runtime.Data.RotatingBlade;
        tuning.BladeAutoDamageInterval = mode == WeaponStatMode.Automatic ? interval : .5f;
        tuning.BladeManualCooldown = mode == WeaponStatMode.Manual ? interval : .5f;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, mode);
        if (mode == WeaponStatMode.Automatic) weapon.TickAutomatic(.25f, Vector3.forward);
        else weapon.TickManual(.25f, Vector3.forward, true);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10 * hits));
        Assert.That(Number(weapon, ModeName(mode), "Action / tick interval"), Is.EqualTo(interval));
        int beforeOther = target.CurrentHealth;
        var other = mode == WeaponStatMode.Automatic ? WeaponStatMode.Manual : WeaponStatMode.Automatic;
        Fire(weapon, other);
        if (other == WeaponStatMode.Automatic) weapon.TickAutomatic(.25f, Vector3.forward);
        else weapon.TickManual(.25f, Vector3.forward, true);
        Assert.That(beforeOther - target.CurrentHealth, Is.EqualTo(10));
        Assert.That(weapon.SpinAngle, Is.Zero);
    }

    [TestCase(WeaponUpgradePath.None, 120f, 12f, 10)]
    [TestCase(WeaponUpgradePath.None, 360f, 36f, 10)]
    [TestCase(WeaponUpgradePath.PathB, 120f, 18f, 20)]
    [TestCase(WeaponUpgradePath.PathB, 360f, 54f, 20)]
    public void LegacySpinRate_ChangesOrbitMotionAndPreservesAtomicFactorsUntilExplicitlyReplaced(WeaponUpgradePath path, float degrees, float angle, int damage)
    {
        var weapon = CreateWeapon(path, out var row);
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.05f);
        weapon.Runtime.Data.RotatingBlade.BladeBaseSpinDegreesPerSecond = degrees;
        var target = Target(Owner(weapon).position + Quaternion.AngleAxis(angle * .5f, Vector3.up) * Vector3.forward * 2f);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(.1f, Vector3.forward);
        Assert.That(weapon.SpinAngle, Is.EqualTo(angle).Within(.001f));
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(damage), "Inherited Atomic automatic damage remains twice base damage.");
        float inheritedInterval = Number(weapon, "Automatic", "Action / tick interval");
        row.Blade.RotationsPerSecond = Enabled(.25f);
        weapon.TickAutomatic(.1f, Vector3.forward);
        Assert.That(weapon.SpinAngle, Is.EqualTo(angle + 9f).Within(.001f), "Explicit spin replaces the Atomic multiplier as well as the global spin.");
        Assert.That(Number(weapon, "Automatic", "Action / tick interval"), Is.EqualTo(inheritedInterval));
    }

    [TestCase(1f, false)]
    [TestCase(2f, true)]
    public void LegacyOrbitRadius_MovesActualContactWithoutChangingSlashReach(float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.RotationsPerSecond = Enabled(0f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.1f);
        weapon.Runtime.Data.RotatingBlade.BladeOrbitRadius = radius;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(radius));
        int beforeManual = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(beforeManual - target.CurrentHealth, Is.EqualTo(10));
    }

    [TestCase(.1f, false)]
    [TestCase(.6f, true)]
    public void LegacyHitRadius_ChangesContactEnvelopeWithoutChangingThrustWidth(float radius, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.RotationsPerSecond = Enabled(0f);
        weapon.Runtime.Data.RotatingBlade.BladeHitRadius = radius;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f + Vector3.right * .4f);
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Automatic", "Hit radius"), Is.EqualTo(radius));
        int beforeAbility = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeAbility), "Q retains its separate 0.2 m contact radius.");
    }

    [TestCase(20f, false)]
    [TestCase(80f, true)]
    public void LegacyManualConeAngle_ChangesActualSlashSelectionWithoutChangingQ(float angle, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        weapon.Runtime.Data.RotatingBlade.BladeManualConeAngle = angle;
        var target = Target(Owner(weapon).position + Quaternion.AngleAxis(30f, Vector3.up) * Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Manual", "Cone angle"), Is.EqualTo(angle));
        int beforeAbility = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeAbility));
    }

    [TestCase(2f, false)]
    [TestCase(4f, true)]
    public void LegacyManualRange_ChangesRealSlashAndLinkedThrustButLeavesOrbitIndependent(float range, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out var row);
        row.Blade.RotationsPerSecond = Enabled(0f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.1f);
        weapon.Runtime.Data.RotatingBlade.BladeManualRange = range;
        var target = Target(Owner(weapon).position + Vector3.forward * 3f);
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(hit ? 10 : 0));
        int beforeAbility = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(beforeAbility - target.CurrentHealth, Is.EqualTo(hit ? 15 : 0));
        int beforeAuto = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Automatic);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeAuto));
        Assert.That(Number(weapon, "Automatic", "Range"), Is.EqualTo(2f));
    }

    [TestCase(1f, 3f, false)]
    [TestCase(3f, 9f, true)]
    public void LegacyActiveBaseRangeMultiplier_ChangesColdThrustReachAndVisibleEndpoint(float multiplier, float range, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        weapon.Runtime.Data.RotatingBlade.BladeActiveBaseRangeMultiplier = multiplier;
        weapon.Runtime.Data.RotatingBlade.BladeActiveMaxRangeMultiplier = 10f;
        var target = Target(Owner(weapon).position + Vector3.forward * 5f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        AssertLegacyThrustEndpoint(weapon, range);
        int beforeManual = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeManual));
    }

    [TestCase(1f, 3f, false)]
    [TestCase(3f, 9f, true)]
    public void LegacyActiveMaxRangeMultiplier_CapsActualHotThrustAndLeavesColdThrustUnchanged(float maximum, float range, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        var heat = AttachLegacyControlHeat(weapon, true);
        weapon.Runtime.Data.RotatingBlade.BladeActiveMaxRangeMultiplier = maximum;
        var target = Target(Owner(weapon).position + Vector3.forward * 5f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        AssertLegacyThrustEndpoint(weapon, range);
        int beforeCold = target.CurrentHealth;
        SetField(heat, "_currentHeat", 0f);
        weapon.Runtime.AbilityCooldownTimer = 0f;
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeCold));
        AssertLegacyThrustEndpoint(weapon, 3f);
    }

    [TestCase(WeaponUpgradePath.None, .4f, false)]
    [TestCase(WeaponUpgradePath.None, 1f, false)]
    [TestCase(WeaponUpgradePath.None, 2f, true)]
    [TestCase(WeaponUpgradePath.PathB, .4f, false)]
    [TestCase(WeaponUpgradePath.PathB, 1f, true)]
    [TestCase(WeaponUpgradePath.PathB, 2f, true)]
    public void LegacyActiveLineWidth_ChangesActualThrustAndAtomicContactWithDistinctRadiusFactors(WeaponUpgradePath path, float width, bool hit)
    {
        var weapon = CreateWeapon(path, out _);
        weapon.Runtime.Data.RotatingBlade.BladeActiveLineWidth = width;
        var target = Target(Owner(weapon).position + Vector3.forward * 2f + Vector3.right * .55f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        Assert.That(Number(weapon, "Active Ability", "Hit radius"), Is.EqualTo(width * (path == WeaponUpgradePath.PathB ? .65f : .5f)).Within(.001f));
        int beforeManual = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(beforeManual - target.CurrentHealth, Is.EqualTo(path == WeaponUpgradePath.PathB ? 20 : 10));
    }

    [TestCase(WeaponUpgradePath.None, 0f)]
    [TestCase(WeaponUpgradePath.None, 2f)]
    [TestCase(WeaponUpgradePath.PathB, 0f)]
    [TestCase(WeaponUpgradePath.PathB, 2f)]
    public void LegacyActiveKnockbackScale_ChangesConsumedThrustImpulseAndAtomicRemainsExcluded(WeaponUpgradePath path, float scale)
    {
        var weapon = CreateWeapon(path, out var row);
        SetSingleBladeGeometry(row);
        weapon.Runtime.Data.BaseKnockback = 1f;
        weapon.Runtime.Data.RotatingBlade.BladeActiveKnockbackScale = scale;
        weapon.Runtime.Data.RotatingBlade.BladeManualKnockbackScale = .5f;
        row.Ability.Damage = row.Manual.Damage = Enabled(16f);
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(16));
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(path == WeaponUpgradePath.PathB ? 0f : .2f * scale).Within(.001f));
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(ConsumeLegacyControlKnockback(target), Is.EqualTo(path == WeaponUpgradePath.PathB ? 0f : .1f).Within(.001f));
    }

    [TestCase(.5f, 1.5f, false)]
    [TestCase(2f, 6f, true)]
    public void LegacyAtomicBaseRangeMultiplier_ChangesActualDashReachAndEndpointOnly(float multiplier, float range, bool hit)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out _);
        weapon.Runtime.Data.RotatingBlade.AtomicDashBaseRangeMultiplier = multiplier;
        weapon.Runtime.Data.RotatingBlade.AtomicDashRangePerHitMultiplier = 0f;
        var target = Target(Owner(weapon).position + Vector3.forward * 4f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(target.CurrentHealth < TargetHealth, Is.EqualTo(hit));
        AssertLegacyThrustEndpoint(weapon, range);
        int beforeManual = target.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(target.CurrentHealth, Is.EqualTo(beforeManual));
    }

    [TestCase(0f, false, 3f)]
    [TestCase(1f, true, 9f)]
    public void LegacyAtomicRangePerHitMultiplier_ChangesActualChainedHitsAndFinalEndpoint(float extension, bool secondHit, float finalRange)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.PathB, out _);
        weapon.Runtime.Data.RotatingBlade.AtomicDashBaseRangeMultiplier = 1f;
        weapon.Runtime.Data.RotatingBlade.AtomicDashRangePerHitMultiplier = extension;
        var first = Target(Owner(weapon).position + Vector3.forward * 2f);
        var second = Target(Owner(weapon).position + Vector3.forward * 5f);
        Fire(weapon, WeaponStatMode.Ability);
        Assert.That(TargetHealth - first.CurrentHealth, Is.EqualTo(15));
        Assert.That(TargetHealth - second.CurrentHealth, Is.EqualTo(secondHit ? 15 : 0));
        AssertLegacyThrustEndpoint(weapon, finalRange);
        int beforeManual = second.CurrentHealth;
        Fire(weapon, WeaponStatMode.Manual);
        Assert.That(second.CurrentHealth, Is.EqualTo(beforeManual));
    }

    [TestCase(.1f, false)]
    [TestCase(.4f, true)]
    public void LegacyVisualDuration_ChangesVisibleSlashLifetimeWithoutChangingDamageOrCadence(float seconds, bool stillVisible)
    {
        var weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        weapon.Runtime.Data.RotatingBlade.BladeVisualDuration = seconds;
        float interval = Number(weapon, "Manual", "Action / tick interval");
        var target = Target(Owner(weapon).position + Vector3.forward * 2f);
        Fire(weapon, WeaponStatMode.Manual);
        var vfx = Field<RotatingBladeVfx>(weapon, "_vfx");
        var line = vfx.transform.Find("Blade Slash 1").GetComponent<LineRenderer>();
        Assert.That(line.enabled, Is.True);
        typeof(RotatingBladeVfx).GetMethod("TickSlashLines", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(vfx, new object[] { .2f });
        typeof(RotatingBladeVfx).GetMethod("TickMeshPulses", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(vfx, new[] { Field<object>(vfx, "_slashSurfaces"), (object).2f, 0f });
        Assert.That(line.enabled, Is.EqualTo(stillVisible));
        Assert.That(vfx.ActiveSlashSurfaceCount, Is.EqualTo(stillVisible ? 1 : 0));
        Assert.That(TargetHealth - target.CurrentHealth, Is.EqualTo(10));
        Assert.That(Number(weapon, "Manual", "Action / tick interval"), Is.EqualTo(interval));
    }

    private static void AssertLegacyThrustEndpoint(RotatingBladeWeapon weapon, float range)
    {
        var line = Field<RotatingBladeVfx>(weapon, "_vfx").transform.Find("Blade Thrust").GetComponent<LineRenderer>();
        Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(range).Within(.001f));
    }


    private HeatManager AttachLegacyControlHeat(RotatingBladeWeapon weapon, bool maximum)
    {
        var heat = Track(new GameObject("Blade legacy control heat")).AddComponent<HeatManager>();
        SetField(heat, "_currentHeat", maximum ? heat.TotalHeatCapacity : 0f);
        weapon.Setup(weapon.Runtime, Owner(weapon), Field<PlayerStats>(weapon, "Stats"), heat);
        return heat;
    }

    private static float ConsumeLegacyControlKnockback(EnemyHealth target)
    {
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        if (receiver == null) return 0f;
        float distance = receiver.ConsumeDisplacement(.1f).magnitude;
        receiver.ClearPending();
        return distance;
    }

    private RotatingBladeWeapon CreateWeapon(WeaponUpgradePath path, out WeaponStatLevelRow row, float area = 1f)
    {
        var owner = Track(new GameObject("Blade stat owner"));
        owner.transform.position = new Vector3(1000f + _weapons.Count * 100f, 1000f, 1000f);
        var stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction })
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField", type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f
                : type == StatType.CriticalDamage ? 2f : type == StatType.ProjectileAreaSize ? area : 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
        var data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.RotatingBlade;
        data.WeaponId = "blade-stat-test";
        data.EnsureSpecificTuningForCurrentType();
        data.BaseDamage = 10f; data.BaseManualAmmo = 100f; data.ActiveAbilityAmmoCost = 2f; data.SkillCooldown = 4f;
        data.RotatingBlade.BladeOrbitRadius = 2f;
        data.RotatingBlade.BladeManualRange = 3f;
        data.RotatingBlade.BladeActiveBaseRangeMultiplier = 1f;
        data.RotatingBlade.BladeActiveMaxRangeMultiplier = 1f;
        data.RotatingBlade.BladeActiveLineWidth = .4f;
        var runtime = new WeaponInstance { Data = data, Level = path == WeaponUpgradePath.None ? 1 : 7,
            SelectedPath = path, State = WeaponState.Manual, CurrentAmmo = 100f };
        row = new WeaponStatLevelRow { WeaponType = WeaponType.RotatingBlade, Level = runtime.Level, Path = path };
        data.StatProfile.Levels.Add(row);
        var weapon = new RotatingBladeWeapon(null, null, null);
        weapon.Setup(runtime, owner.transform, stats, null);
        _weapons.Add(weapon);
        return weapon;
    }

    private static void SetSingleBladeGeometry(WeaponStatLevelRow row)
    {
        row.Blade.OrbitRadiusMeters = Enabled(2f);
        row.Blade.AutomaticHitRadiusMeters = Enabled(.2f);
        row.Blade.RotationsPerSecond = Enabled(0f);
        if (row.Path == WeaponUpgradePath.PathB)
        {
            row.Blade.AtomicDashRangeMeters = Enabled(3f);
            row.Blade.AtomicRangePerHitMultiplier = Enabled(0f);
        }
        else row.Blade.ThrustLengthMeters = Enabled(3f);
        if (row.Path == WeaponUpgradePath.PathA)
            row.Blade.ExtraBladeCount = new WeaponOptionalInt { Enabled = true, Value = 0 };
    }
    private static WeaponStatLevelRow AddRow(RotatingBladeWeapon weapon, int level, WeaponUpgradePath path, float orbit, int extra)
    {
        var row = new WeaponStatLevelRow { WeaponType = WeaponType.RotatingBlade, Level = level, Path = path };
        row.Blade.OrbitRadiusMeters = Enabled(orbit);
        row.Blade.ExtraBladeCount = new WeaponOptionalInt { Enabled = path == WeaponUpgradePath.PathA, Value = extra };
        weapon.Runtime.Data.StatProfile.Levels.Add(row);
        return row;
    }
    private EnemyHealth Target(Vector3 position)
    {
        var target = Track(new GameObject("Blade stat target"));
        target.transform.position = position;
        var health = target.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(TargetHealth);
        EnemyRegistry.Register(target.transform);
        _targets.Add(health);
        return health;
    }
    private static void Fire(RotatingBladeWeapon weapon, WeaponStatMode mode)
    {
        weapon.Runtime.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        if (mode == WeaponStatMode.Automatic) weapon.TickAutomatic(0f, Vector3.forward);
        else if (mode == WeaponStatMode.Manual) weapon.TickManual(0f, Vector3.forward, true);
        else weapon.UseActiveAbility(Vector3.forward);
    }
    private static WeaponStatModeValues Mode(WeaponStatLevelRow row, WeaponStatMode mode) =>
        mode == WeaponStatMode.Automatic ? row.Automatic : mode == WeaponStatMode.Manual ? row.Manual : row.Ability;
    private static string ModeName(WeaponStatMode mode) => mode == WeaponStatMode.Automatic ? "Automatic" : mode == WeaponStatMode.Manual ? "Manual" : "Active Ability";
    private static WeaponOptionalFloat Enabled(float value) => new() { Enabled = true, Value = value };
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static Transform Owner(RotatingBladeWeapon weapon) => Field<Transform>(weapon, "Owner");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static float Number(RotatingBladeWeapon weapon, string section, string label) => float.Parse(weapon.CaptureDiagnostics().Sections
        .Single(s => s.Name == section).Values.Single(v => v.Label == label).Value.Split(' ')[0].TrimEnd('%', 'x'), CultureInfo.InvariantCulture);
}
