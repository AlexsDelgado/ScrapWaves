using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Appendix A C01-C13/M01-M17: real emission, collision and shell consumers.</summary>
public class CannonMortarStatConsumerTests
{
    private readonly List<Object> _objects = new();
    private readonly List<Transform> _registered = new();
    private UnityEngine.Random.State _random;
    private ChallengeProgressTracker _previousChallengeTracker;

    [SetUp] public void Before()
    {
        _random = UnityEngine.Random.state; UnityEngine.Random.InitState(3271);
        _previousChallengeTracker = ChallengeProgressTracker.Instance;
        // EditMode AddComponent does not run this gameplay singleton's Awake.
        // Isolate test damage reporting without touching any existing tracker state.
        var tracker = Keep(new GameObject("Stat consumer challenge tracker")).AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
    }
    [TearDown] public void After()
    {
        foreach (Transform target in _registered) if (target != null) EnemyRegistry.Unregister(target);
        _registered.Clear();
        DestroyTransient<MortarShellImpact>(); DestroyTransient<ExplosionRadiusVfx>();
        DestroyTransient<WeaponUpgradeVfx>(); DestroyTransient<HeadHunterChargeVfx>();
        for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear(); UnityEngine.Random.state = _random;
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, _previousChallengeTracker);
    }

    public static IEnumerable<TestCaseData> AllStates()
    {
        foreach (WeaponType type in new[] { WeaponType.AutomaticCannon, WeaponType.Mortar })
        for (int level = 1; level <= 10; level++)
        foreach (WeaponUpgradePath path in level <= 5 ? new[] { WeaponUpgradePath.None }
            : new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
        foreach (WeaponStatMode mode in Enum.GetValues(typeof(WeaponStatMode)))
            yield return new TestCaseData(type, level, path, mode);
    }

    [TestCaseSource(nameof(AllStates))]
    public void C01_C08_M01_M10_AllProgressionModes_AuthoredDamageReachesHealthWithoutLegacyFactors(
        WeaponType type, int level, WeaponUpgradePath path, WeaponStatMode mode)
    {
        Fixture f = Create(type, level, path);
        if (type == WeaponType.Mortar && path == WeaponUpgradePath.PathA)
            f.Row.Mortar.GrapeshotPelletCount = I(1);
        TestTarget target = Target(Vector3.forward * 4f);
        foreach (float value in new[] { 17f, 31f })
        {
            WeaponStatAuthoringResolver.GetModeValues(f.Row, mode).Damage = F(value);
            int before = target.TotalDamage;
            EmitAndHit(f, mode, target);
            float expected = value;
            if (type == WeaponType.Mortar && path == WeaponUpgradePath.PathA && mode != WeaponStatMode.Ability)
                expected *= 0.5f;
            if (type == WeaponType.AutomaticCannon && path == WeaponUpgradePath.PathB && mode == WeaponStatMode.Ability)
                expected *= 5f;
            Assert.That(target.TotalDamage - before, Is.EqualTo(Mathf.RoundToInt(expected)),
                "The selected mode's absolute value must replace old level/path/Q factors before damage reaches health.");
        }
    }

    [TestCase(WeaponUpgradePath.None, WeaponStatMode.Automatic)]
    [TestCase(WeaponUpgradePath.None, WeaponStatMode.Manual)]
    [TestCase(WeaponUpgradePath.None, WeaponStatMode.Ability)]
    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Automatic)]
    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Manual)]
    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Ability)]
    [TestCase(WeaponUpgradePath.PathB, WeaponStatMode.Automatic)]
    [TestCase(WeaponUpgradePath.PathB, WeaponStatMode.Manual)]
    [TestCase(WeaponUpgradePath.PathB, WeaponStatMode.Ability)]
    public void M12_M13_CriticalCapabilityIsOptInAndPersistsThroughChildren(
        WeaponUpgradePath path, WeaponStatMode mode)
    {
        Fixture f = Create(WeaponType.Mortar, path == WeaponUpgradePath.None ? 1 : 6, path);
        WeaponStatAuthoringResolver.GetModeValues(f.Row, mode).Damage = F(20f);
        if (path == WeaponUpgradePath.PathA) f.Row.Mortar.GrapeshotPelletCount = I(1);
        f.Row.CriticalChance = F(1f); f.Row.CriticalDamageMultiplier = F(1.5f);
        TestTarget target = Target(Vector3.forward * 4f);
        int ordinary = path == WeaponUpgradePath.PathA && mode != WeaponStatMode.Ability ? 10 : 20;
        EmitAndHit(f, mode, target);
        Assert.That(target.TotalDamage, Is.EqualTo(ordinary), "Default off must preserve Mortar's no-crit behavior.");
        string diagnosticMode = mode == WeaponStatMode.Automatic ? "Automatic" : mode == WeaponStatMode.Manual ? "Manual" : "Active Ability";
        Assert.That(Diagnostic(f, diagnosticMode, "Damage / hit (non-critical)"), Is.EqualTo(ordinary.ToString()));
        f.Row.Mortar.EnableCriticalHits = true;
        EmitAndHit(f, mode, target);
        Assert.That(target.TotalDamage, Is.EqualTo(ordinary * 4), "Weapon factor 1.5 x player factor 2 applies once.");
        Assert.That(Diagnostic(f, diagnosticMode, "Damage / hit (critical)"), Is.EqualTo((ordinary * 3).ToString()));
        f.Row.Mortar.EnableCriticalHits = false;
        EmitAndHit(f, mode, target);
        Assert.That(target.TotalDamage, Is.EqualTo(ordinary * 5));
        Assert.That(f.Row.CriticalChance.Value, Is.EqualTo(1f), "Disabling capability must preserve hidden authoring.");
    }

    [TestCase(WeaponUpgradePath.None)] [TestCase(WeaponUpgradePath.PathA)] [TestCase(WeaponUpgradePath.PathB)]
    public void C04_ManualRangeIsOptIn_ActualProjectileOrPierceStopsAtBoundary(WeaponUpgradePath path)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, path == WeaponUpgradePath.None ? 1 : 6, path);
        f.Row.Manual.Damage = F(12f);
        f.Runtime.State = WeaponState.Manual;
        TestTarget target = Target(Vector3.forward * 4f);
        if (path == WeaponUpgradePath.PathB)
        {
            f.Row.Manual.RangeMeters = F(2f);
            f.Weapon.TickManual(0f, Vector3.forward, true);
            f.Weapon.TickManual(5f, Vector3.forward, false);
            Assert.That(target.TotalDamage, Is.Zero);
            f.Row.Manual.RangeMeters = F(6f);
            f.Weapon.TickManual(0f, Vector3.forward, true);
            f.Weapon.TickManual(5f, Vector3.forward, false);
            Assert.That(target.TotalDamage, Is.EqualTo(12));
        }
        else
        {
            f.Row.Manual.RangeMeters = F(2f);
            f.Weapon.TickManual(0f, Vector3.forward, true);
            Projectile bullet = ActiveProjectiles(f).Single();
            bullet.GetComponent<Rigidbody>().position = Vector3.forward * 2.01f;
            Physics.SyncTransforms(); Call(bullet, "FixedUpdate");
            Assert.That(bullet.gameObject.activeSelf, Is.False, "Actual travel cutoff returns the bullet to the pool.");
            f.Row.Manual.RangeMeters = default;
            f.Weapon.TickManual(10f, Vector3.forward, true);
            bullet = ActiveProjectiles(f).Single();
            Assert.That(Field<float>(bullet, "_maxTravelDistance"), Is.Zero, "Inherit restores unlimited distance, still bounded by legacy lifetime.");
        }
    }

    [Test]
    public void C04_ManualTravelAndLifetimeAreIndependentActualTerminationLimits()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        f.Row.Manual.RangeMeters = F(80f);
        Set(f.Row.Cannon, "ManualProjectileLifetimeSeconds", F(0.25f));
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Projectile bullet = ActiveProjectiles(f).Single();
        Assert.That(Field<float>(bullet, "_maxTravelDistance"), Is.EqualTo(80f));
        Set(bullet, "_elapsed", 0.251f); Call(bullet, "Update");
        Assert.That(bullet.gameObject.activeSelf, Is.False, "The time limit can terminate a bullet before its maximum travel distance.");
        f.Row.Manual.RangeMeters = F(1f); Set(f.Row.Cannon, "ManualProjectileLifetimeSeconds", F(120f));
        f.Weapon.TickManual(10f, Vector3.forward, true);
        bullet = ActiveProjectiles(f).Single();
        Assert.That(Field<float>(bullet, "_activeMaxLifetime"), Is.EqualTo(120f));
        bullet.GetComponent<Rigidbody>().position = Vector3.forward * 1.01f;
        Physics.SyncTransforms(); Call(bullet, "FixedUpdate");
        Assert.That(bullet.gameObject.activeSelf, Is.False, "The distance limit terminates a bullet before a longer lifetime expires.");
        f.Row.Manual.RangeMeters = default; Set(f.Row.Cannon, "ManualProjectileLifetimeSeconds", default(WeaponOptionalFloat));
        f.Weapon.TickManual(10f, Vector3.forward, true);
        bullet = ActiveProjectiles(f).Single();
        Assert.That(Field<float>(bullet, "_maxTravelDistance"), Is.Zero);
        Assert.That(Field<float>(bullet, "_activeMaxLifetime"), Is.EqualTo(f.Pool.ProjectileLifetime), "Pool reuse clears the previous action's authored limits.");
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C03_C05_M04_M07_ManualCadenceCountsLaunchesAndDoesNotReuseAutomaticRate(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        f.Runtime.State = WeaponState.Manual;
        f.Row.Automatic.AttackCyclesPerSecond = F(9f);
        f.Row.Manual.AttackCyclesPerSecond = F(2f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
        f.Weapon.TickManual(0.49f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
        f.Weapon.TickManual(0.011f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(2));
        Assert.That(f.Runtime.CurrentAmmo, Is.EqualTo(998f));
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C02_M02_AcquisitionBoundaryUsesAutomaticRangeAndLeavesManualRangeIndependent(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        f.Runtime.State = WeaponState.Automatic;
        Target(Vector3.forward * 6f);
        f.Row.Automatic.RangeMeters = F(4f); f.Row.Manual.RangeMeters = F(25f);
        f.Weapon.TickAutomatic(0f, Vector3.forward);
        Assert.That(LaunchCount(f), Is.Zero);
        f.Row.Automatic.RangeMeters = F(7f);
        f.Weapon.TickAutomatic(0f, Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
        if (type == WeaponType.AutomaticCannon)
            Assert.That(Field<float>(ActiveProjectiles(f).Single(), "_maxTravelDistance"), Is.Zero,
                "Acquisition range must not accidentally cap automatic projectile travel.");
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C03_M04_AutomaticCadenceCountsLaunchesIndependentOfManualRate(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        f.Runtime.State = WeaponState.Automatic; Target(Vector3.forward * 4f);
        f.Row.Automatic.AttackCyclesPerSecond = F(4f); f.Row.Manual.AttackCyclesPerSecond = F(11f);
        f.Weapon.TickAutomatic(0f, Vector3.forward);
        f.Weapon.TickAutomatic(0.249f, Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
        f.Weapon.TickAutomatic(0.002f, Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(2));
    }

    [TestCase(0f, 0)] [TestCase(3f, 6)] [TestCase(7f, 14)]
    public void C12_ContinuousAbilityRateControlsActualEmissionAndLongFrameCatchup(float rate, int expected)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        f.Runtime.State = WeaponState.Manual;
        f.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(rate);
        f.Row.Cannon.ContinuousAbilityDurationSeconds = F(2f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        f.Weapon.TickManual(5f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(expected));
        Assert.That(Field<bool>(f.Weapon, "_continuousFireActive"), Is.False);
        f.Weapon.TickManual(5f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(expected), "Completed abilities cannot emit extra bullets.");
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C03_C05_M04_M07_SmallPositiveRateReachesSchedulerWithoutHiddenMinimum(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        f.Row.Manual.AttackCyclesPerSecond = F(0.01f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        f.Weapon.TickManual(99f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
        f.Weapon.TickManual(1.01f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(2));
    }

    [Test]
    public void M04_M07_DiagnosticsRemainModeSpecificWhenOnlyOppositeModeIsAuthored()
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Row.Manual.AttackCyclesPerSecond = F(7f);
        f.Runtime.State = WeaponState.Manual;
        Assert.That(Diagnostic(f, "Automatic", "Actions / ticks per second"), Is.EqualTo("3"));
        Assert.That(Diagnostic(f, "Manual", "Actions / ticks per second"), Is.EqualTo("7"));
        f.Row.Manual.AttackCyclesPerSecond = default;
        f.Row.Automatic.AttackCyclesPerSecond = F(9f);
        f.Runtime.State = WeaponState.Automatic;
        Assert.That(Diagnostic(f, "Manual", "Actions / ticks per second"), Is.EqualTo("3"));
        Assert.That(Diagnostic(f, "Automatic", "Actions / ticks per second"), Is.EqualTo("9"));
        f.Row.Ability.Damage = F(17f);
        Assert.That(Diagnostic(f, "Active Ability", "Damage / hit (non-critical)"), Is.EqualTo("17"),
            "The inherited Q damage factor must be replaced before the same authored damage reaches diagnostics and health.");
    }

    [TestCase(WeaponType.AutomaticCannon, WeaponUpgradePath.None, 0f)]
    [TestCase(WeaponType.AutomaticCannon, WeaponUpgradePath.PathA, 7f)]
    [TestCase(WeaponType.AutomaticCannon, WeaponUpgradePath.PathA, 0f)]
    [TestCase(WeaponType.AutomaticCannon, WeaponUpgradePath.PathB, 11f)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.None, 0f)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.PathA, 7f)]
    [TestCase(WeaponType.Mortar, WeaponUpgradePath.PathB, 11f)]
    public void C06_M08_AbilityAmmoAuthoringMatchesActualDrainAndDisplayedCost(
        WeaponType type, WeaponUpgradePath path, float cost)
    {
        Fixture f = Create(type, path == WeaponUpgradePath.None ? 1 : 6, path);
        if (type == WeaponType.Mortar) Floor();
        Set(f.Row, "AbilityAmmoCost", F(cost));
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(f.Runtime), Is.EqualTo(cost));
        Assert.That(Diagnostic(f, "Active Ability", "Ammo / action"), Is.EqualTo(cost.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        f.Weapon.UseActiveAbility(Vector3.forward);
        Assert.That(f.Runtime.CurrentAmmo, Is.EqualTo(1000f - cost));
        Set(f.Row, "AbilityAmmoCost", default(WeaponOptionalFloat));
        float inherited = type == WeaponType.AutomaticCannon && path == WeaponUpgradePath.PathA ? 80f : 20f;
        Assert.That(WeaponMath.GetActiveAbilityAmmoCost(f.Runtime), Is.EqualTo(inherited));
        Assert.That(f.Row.Manual.AmmoCapacity.Enabled, Is.False, "Ability cost is independent of manual capacity.");
    }

    [Test]
    public void C12_ContinuousEmissionRetainsImmediateFirstBulletAndTimedSpacing()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        f.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(4f);
        f.Row.Cannon.ContinuousAbilityDurationSeconds = F(1f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        f.Weapon.TickManual(0f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(1));
        f.Weapon.TickManual(0.249f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(1));
        f.Weapon.TickManual(0.002f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(2));
        f.Weapon.TickManual(0.75f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(4));
    }

    [TestCase(0, 0)] [TestCase(2, 2)] [TestCase(9, 9)]
    public void M09_ActiveShellCountAndBarrageRadiusReachSpawnedShells(int count, int expected)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        Floor(); f.Runtime.State = WeaponState.Manual;
        f.Row.Mortar.ActiveShellCount = I(count);
        f.Row.Mortar.BarrageRadiusMeters = F(2.5f);
        f.Row.Ability.ExplosionRadiusMeters = F(0.6f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact[] shells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(shells.Length, Is.EqualTo(expected));
        foreach (MortarShellImpact shell in shells)
        {
            Vector3 point = Field<Vector3>(shell, "_target");
            Assert.That(new Vector2(point.x, point.z - f.Data.BaseRange).magnitude, Is.LessThanOrEqualTo(2.501f));
            Assert.That(Field<float>(shell, "_explosionRadius"), Is.EqualTo(0.6f));
        }
        Assert.That(f.Runtime.AbilityCooldownTimer, Is.GreaterThan(0f));
    }

    [TestCase(0f, 2f, 0)] [TestCase(3f, 2f, 6)] [TestCase(4.5f, 2f, 9)]
    public void M17_GrapeshotRainRateAndDurationControlActualShellCountAndImpactSpacing(float rate, float duration, int expected)
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        Floor(); f.Runtime.State = WeaponState.Manual;
        f.Row.Mortar.GrapeshotAbilityProjectilesPerSecond = F(rate);
        f.Row.Mortar.GrapeshotAbilityDurationSeconds = F(duration);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact[] shells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(shells.Length, Is.EqualTo(expected));
        float[] arrivals = shells.Select(s => Field<float>(s, "_travelTime")).OrderBy(v => v).ToArray();
        for (int i = 1; i < arrivals.Length; i++)
            Assert.That(arrivals[i] - arrivals[i - 1], Is.EqualTo(duration / expected).Within(0.0001f));
    }

    [TestCase(1)] [TestCase(2)] [TestCase(4)]
    public void M15_MultiChargedCountIncludesInitialExplosionAndDelayReachesScheduler(int total)
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathB);
        f.Row.Manual.Damage = F(11f); f.Row.Mortar.MultiChargedTotalExplosions = I(total);
        f.Row.Mortar.MultiChargedRepeatDelaySeconds = F(0.37f);
        TestTarget target = Target(Vector3.forward * 4f);
        MortarShellImpact shell = EmitMortar(f, WeaponStatMode.Manual, target.transform.position);
        Call(shell, "Detonate", target.transform.position);
        for (int i = 1; i < total; i++)
        {
            Assert.That(Field<float>(shell, "_repeatExplosionTimer"), Is.EqualTo(0.37f));
            Set(shell, "_repeatExplosionTimer", -0.01f);
            Call(shell, "TickRepeatExplosions");
        }
        Assert.That(target.TotalDamage, Is.EqualTo(total * 11));
        if (shell != null) Assert.That(Field<int>(shell, "_remainingRepeatExplosions"), Is.Zero);
    }

    [TestCase(0)] [TestCase(2)] [TestCase(7)]
    public void M16_GrapeshotPelletCountIsTotalActualChildrenAndNeverAffectsAbility(int total)
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        f.Row.Mortar.GrapeshotPelletCount = I(total);
        MortarShellImpact parent = EmitMortar(f, WeaponStatMode.Manual, Vector3.forward * 4f);
        Call(parent, "Detonate", Vector3.up * 2f);
        Assert.That(Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Length, Is.EqualTo(total));
        DestroyTransient<MortarShellImpact>();
        MortarShellImpact ability = EmitMortar(f, WeaponStatMode.Ability, Vector3.forward * 4f);
        Assert.That(Field<MortarUpgradePayload>(ability, "_payload").UseGrapeshot, Is.False);
    }

    [TestCase(0f)] [TestCase(0.25f)] [TestCase(2.5f)] [TestCase(10000f)]
    public void M03_M06_GrapeshotSpreadChangesChildTargetsWithoutChangingPelletBlast(float spread)
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        f.Row.Mortar.GrapeshotPelletCount = I(8);
        Set(f.Row.Mortar, "GrapeshotAutomaticLandingSpreadMeters", F(9f));
        Set(f.Row.Mortar, "GrapeshotManualLandingSpreadMeters", F(spread));
        MortarShellImpact parent = EmitMortar(f, WeaponStatMode.Manual, Vector3.forward * 4f);
        Call(parent, "Detonate", Vector3.up * 3f);
        MortarShellImpact[] children = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(children.Length, Is.EqualTo(8));
        foreach (MortarShellImpact child in children)
        {
            Vector3 target = Field<Vector3>(child, "_target");
            Assert.That(new Vector2(target.x, target.z - 4f).magnitude, Is.LessThanOrEqualTo(spread + 0.0001f));
            Assert.That(Field<float>(child, "_explosionRadius"), Is.EqualTo(0.16875f).Within(0.0001f),
                "The landing distribution and each pellet's blast remain distinct.");
        }
        if (spread > 0f)
            Assert.That(children.Max(c => { Vector3 p = Field<Vector3>(c, "_target"); return new Vector2(p.x, p.z - 4f).magnitude; }),
                Is.GreaterThan(spread * 0.1f), "A nonzero spread must actually move child targets.");
        var footprint = f.Weapon.GetType().GetProperty("ManualFootprintRadius");
        Assert.That(footprint, Is.Not.Null);
        Assert.That((float)footprint.GetValue(f.Weapon), Is.EqualTo(spread));
        DestroyTransient<MortarShellImpact>();
        parent = EmitMortar(f, WeaponStatMode.Automatic, Vector3.forward * 4f);
        Call(parent, "Detonate", Vector3.up * 3f);
        children = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(children.Max(c => { Vector3 p = Field<Vector3>(c, "_target"); return new Vector2(p.x, p.z).magnitude; }),
            Is.GreaterThan(3f), "The independent automatic landing distribution must retain its 9 m setting.");
    }

    [Test]
    public void M03_M06_GrapeshotAreaModifierAppliesOnceToCapturedSpreadAndReticle()
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        f.Row.Mortar.GrapeshotPelletCount = I(1);
        Set(f.Row.Mortar, "GrapeshotManualLandingSpreadMeters", F(2.5f));
        PlayerStats stats = Field<PlayerStats>(f.Weapon, "Stats");
        Set(stats.GetDefinition(StatType.ProjectileAreaSize), "<BaseValue>k__BackingField", 2f);
        Call(stats, "Awake");
        MortarShellImpact parent = EmitMortar(f, WeaponStatMode.Manual, Vector3.forward * 4f);
        MortarUpgradePayload payload = Field<MortarUpgradePayload>(parent, "_payload");
        Assert.That(Field<float?>(payload, "GrapeshotLandingSpreadMeters"), Is.EqualTo(5f));
        Assert.That((float)f.Weapon.GetType().GetProperty("ManualFootprintRadius").GetValue(f.Weapon), Is.EqualTo(5f));
        Assert.That(Diagnostic(f, "Manual", "Grapeshot target spread"), Is.EqualTo("5 m"));
        Assert.That(f.Weapon.CaptureDiagnostics().Sections.Single(s => s.Name == "Manual").Values
            .Any(v => v.Label == "Explosion radius" || v.Label == "Grapeshot cone"), Is.False);
    }

    [TestCase(0f)] [TestCase(0.01f)] [TestCase(0.7f)]
    public void M03_M06_AuthoredRadiusRingHasExactBoundaryIncludingZero(float radius)
    {
        GameObject shell = Keep(new GameObject("Exact mortar indicator"));
        GameObject landing = Keep(new GameObject("Landing root")); landing.transform.SetParent(shell.transform);
        GameObject ring = Keep(new GameObject("Blast boundary")); ring.transform.SetParent(landing.transform);
        MortarShellVfx vfx = shell.AddComponent<MortarShellVfx>();
        Set(vfx, "_landingRoot", landing); Set(vfx, "_blastRadiusRing", ring.transform); Set(vfx, "_cached", false);
        vfx.Configure(MortarShellVisualStyle.Base, Vector3.zero, Vector3.up, radius, 0.5f, 0f, false, true);
        Assert.That(ring.transform.localScale.x, Is.EqualTo(radius * 2f));
        Assert.That(ring.transform.localScale.z, Is.EqualTo(radius * 2f));
    }

    [TestCase(0f)] [TestCase(0.001f)] [TestCase(0.01f)] [TestCase(0.7f)]
    public void M03_M06_FallbackExplosionBoundaryMatchesEffectivePlayerScaledRadius(float radius)
    {
        GameObject go = Keep(new GameObject("Exact fallback explosion boundary"));
        ExplosionRadiusVfx vfx = go.AddComponent<ExplosionRadiusVfx>();
        vfx.ActivateFromPool(Vector3.zero, radius, 0.2f, Color.yellow, null);
        LineRenderer ring = Field<LineRenderer>(vfx, "_outerRing");
        Assert.That(ring.positionCount, Is.GreaterThan(3));
        for (int i = 0; i < ring.positionCount; i++)
            Assert.That(ring.GetPosition(i).magnitude, Is.EqualTo(radius).Within(0.000001f));
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C02_C04_M02_M05_GeometryAndAimUseActualModeRangeAndKeepInheritedConvergence(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        Assert.That(f.Weapon.GetManualAimConvergenceDistance(), Is.Null);
        if (type == WeaponType.AutomaticCannon)
            Assert.That(f.Weapon.CaptureGeometry().Guides.Any(g => g.Mode == WeaponStatMode.Manual), Is.False,
                "An inherited unlimited projectile distance must not acquire an invented finite guide.");
        f.Row.Manual.RangeMeters = F(3.25f); f.Row.Automatic.RangeMeters = F(7f);
        Assert.That(f.Weapon.GetManualAimConvergenceDistance(), Is.EqualTo(3.25f));
        var guides = f.Weapon.CaptureGeometry().Guides;
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Manual && g.Purpose == WeaponGeometryPurpose.ProjectilePath).RangeMeters,
            Is.EqualTo(type == WeaponType.Mortar ? f.Data.Mortar.MortarManualMaximumRange : 3.25f),
            "Mortar's pitch range guide is independent of the legacy Q convergence override.");
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Automatic && g.Anchor == WeaponGeometryAnchor.Owner).RadiusMeters,
            Is.EqualTo(7f));
        f.Row.Manual.RangeMeters = default;
        Assert.That(f.Weapon.GetManualAimConvergenceDistance(), Is.Null);
    }

    [Test]
    public void M03_M06_M09_GrapeshotGeometrySeparatesLandingSpreadFromAbilityBlastAndBarrage()
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        Set(f.Row.Mortar, "GrapeshotAutomaticLandingSpreadMeters", F(8f));
        Set(f.Row.Mortar, "GrapeshotManualLandingSpreadMeters", F(2f));
        f.Row.Mortar.BarrageRadiusMeters = F(4f); f.Row.Ability.ExplosionRadiusMeters = F(0.5f);
        f.Row.Manual.RangeMeters = F(3f);
        var guides = f.Weapon.CaptureGeometry().Guides;
        Assert.That(guides.Any(g => g.Mode != WeaponStatMode.Ability && g.Purpose == WeaponGeometryPurpose.Explosion), Is.False);
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Manual && g.Purpose == WeaponGeometryPurpose.Targeting).RadiusMeters, Is.EqualTo(2f));
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Automatic && g.Anchor == WeaponGeometryAnchor.Muzzle).RadiusMeters, Is.EqualTo(8f));
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Ability && g.Purpose == WeaponGeometryPurpose.Targeting).RadiusMeters, Is.EqualTo(4f));
        Assert.That(guides.Single(g => g.Mode == WeaponStatMode.Ability && g.Purpose == WeaponGeometryPurpose.Explosion).RadiusMeters, Is.EqualTo(0.5f));
        Assert.That(guides.Where(g => g.Mode == WeaponStatMode.Ability).All(g => g.ForwardOffsetMeters == f.Data.BaseRange), Is.True);
    }

    [TestCase(-20f, 5f)] [TestCase(0f, 18f)] [TestCase(7.5f, 22f)]
    public void M05_M06_ManualPitchControlsAndVisibleBlastUseIndependentRangeAndRadius(float pitch, float range)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        Floor();
        f.AimCamera.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);
        f.Runtime.State = WeaponState.Manual;
        f.Row.Manual.RangeMeters = F(3.25f); f.Row.Manual.ExplosionRadiusMeters = F(0.7f);
        f.Row.Automatic.RangeMeters = F(12f); f.Row.Automatic.ExplosionRadiusMeters = F(5f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Assert.That(Field<Vector3>(shell, "_target").z, Is.EqualTo(range).Within(0.0001f));
        Assert.That(Field<float>(shell, "_explosionRadius"), Is.EqualTo(0.7f));
        var reticle = (IMortarReticleStatus)f.Weapon;
        Assert.That(reticle.ManualRange, Is.EqualTo(22f));
        Assert.That(reticle.ManualExplosionRadius, Is.EqualTo(0.7f));
        TestTarget inside = Target(Vector3.forward * range + Vector3.right * 0.6f);
        TestTarget outside = Target(Vector3.forward * range + Vector3.left * 0.8f);
        Call(shell, "Detonate", Vector3.forward * range);
        Assert.That(inside.TotalDamage, Is.GreaterThan(0)); Assert.That(outside.TotalDamage, Is.Zero);
    }

    [TestCase(0f, 1f)] [TestCase(0.01f, 0.1f)]
    public void M03_M06_ZeroAndSmallPlayerScaledBlastKeepExactPointOrSphereHitBoundary(float radius, float area)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Row.Manual.Damage = F(10f); f.Row.Manual.ExplosionRadiusMeters = F(radius);
        PlayerStats stats = Field<PlayerStats>(f.Weapon, "Stats");
        Set(stats.GetDefinition(StatType.ProjectileAreaSize), "<BaseValue>k__BackingField", area);
        Call(stats, "Awake");
        TestTarget center = Target(Vector3.forward * 4f);
        TestTarget outside = Target(Vector3.forward * 4f + Vector3.right * 0.04f);
        MortarShellImpact shell = EmitMortar(f, WeaponStatMode.Manual, center.transform.position);
        Assert.That(Field<float>(shell, "_explosionRadius"), Is.EqualTo(radius * area));
        Call(shell, "Detonate", center.transform.position);
        Assert.That(center.TotalDamage, Is.EqualTo(10), "Zero radius is a point overlap, not disabled damage.");
        Assert.That(outside.TotalDamage, Is.Zero);
        Assert.That(((IMortarReticleStatus)f.Weapon).ManualExplosionRadius, Is.EqualTo(radius * area));
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C01_M01_AdjacentLevelsAndPathTransitionsNeverBorrowAuthoredDamage(WeaponType type)
    {
        Fixture f = Create(type, 5, WeaponUpgradePath.None);
        f.Row.Manual.Damage = F(13f);
        var a6 = WeaponStatProfile.CreateRow(type, 6, WeaponUpgradePath.PathA); a6.Manual.Damage = F(22f);
        var a7 = WeaponStatProfile.CreateRow(type, 7, WeaponUpgradePath.PathA); a7.Manual.Damage = F(34f);
        var a8 = WeaponStatProfile.CreateRow(type, 8, WeaponUpgradePath.PathA); a8.Manual.Damage = F(46f);
        var b6 = WeaponStatProfile.CreateRow(type, 6, WeaponUpgradePath.PathB); b6.Manual.Damage = F(29f);
        var b7 = WeaponStatProfile.CreateRow(type, 7, WeaponUpgradePath.PathB); b7.Manual.Damage = F(41f);
        foreach (var row in new[] { a6, a7, a8, b6, b7 })
        {
            if (type == WeaponType.Mortar && row.Path == WeaponUpgradePath.PathA) row.Mortar.GrapeshotPelletCount = I(1);
            f.Data.StatProfile.Levels.Add(row);
        }
        TestTarget target = Target(Vector3.forward * 4f);
        foreach (var row in new[] { f.Row, a6, a7, a8, b7, b6, f.Row })
        {
            f.Runtime.Level = row.Level; f.Runtime.SelectedPath = row.Path;
            int before = target.TotalDamage;
            EmitAndHit(f, WeaponStatMode.Manual, target);
            float scale = type == WeaponType.Mortar && row.Path == WeaponUpgradePath.PathA ? 0.5f : 1f;
            Assert.That(target.TotalDamage - before, Is.EqualTo(Mathf.RoundToInt(row.Manual.Damage.Value * scale)));
        }
    }

    [TestCase(WeaponType.AutomaticCannon)] [TestCase(WeaponType.Mortar)]
    public void C11_M14_ZeroKnockbackRemovesActualImpulseAndOtherModeRetainsStrength(WeaponType type)
    {
        Fixture f = Create(type, 1, WeaponUpgradePath.None);
        f.Row.Manual.Damage = F(20f); f.Row.Automatic.Damage = F(20f);
        f.Row.Manual.KnockbackStrength = F(0f); f.Row.Automatic.KnockbackStrength = F(1f);
        TestTarget target = Target(Vector3.forward * 4f);
        EmitAndHit(f, WeaponStatMode.Manual, target);
        Assert.That(target.GetComponent<EnemyKnockbackReceiver>(), Is.Null);
        EmitAndHit(f, WeaponStatMode.Automatic, target);
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        Assert.That(receiver, Is.Not.Null); Assert.That(receiver.ConsumeDisplacement(0.1f).magnitude, Is.GreaterThan(0f));
    }

    [Test]
    public void C13_WeakPointAndAbilityMultiplierReachHealthWhileAutomaticIgnoresWeakPoint()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Row.Manual.Damage = F(10f); f.Row.Automatic.Damage = F(10f); f.Row.Ability.Damage = F(10f);
        f.Row.Cannon.HeadHunterWeakPointMultiplier = F(3f); f.Row.Cannon.HeadHunterWeakPointMaximum = F(4f);
        TestTarget target = Target(Vector3.forward * 4f); target.name = "Stat WeakPoint target";
        f.Runtime.State = WeaponState.Manual;
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, 1, 20f, true, false, null);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 10f);
        Assert.That(target.TotalDamage, Is.EqualTo(30));
        EmitAndHit(f, WeaponStatMode.Automatic, target);
        Assert.That(target.TotalDamage, Is.EqualTo(40));
        EmitAndHit(f, WeaponStatMode.Ability, target);
        Assert.That(target.TotalDamage, Is.EqualTo(70));
        f.Row.Cannon.HeadHunterWeakPointMultiplier = F(9f);
        EmitAndHit(f, WeaponStatMode.Ability, target);
        Assert.That(target.TotalDamage, Is.EqualTo(110), "The configured cap remains independent of the base factor.");
    }

    [TestCase(1, 0.25f, 10, 0, 0)]
    [TestCase(2, 0.25f, 10, 8, 0)]
    [TestCase(3, 0.25f, 10, 8, 5)]
    [TestCase(3, 0f, 10, 10, 10)]
    public void C13_HeadHunterPierceCountAndLossChangeActualTargetsAndDamage(
        int count, float loss, int first, int second, int third)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Runtime.State = WeaponState.Automatic; f.Row.Automatic.Damage = F(10f);
        f.Row.Cannon.HeadHunterPierceTargetCount = I(count);
        f.Row.Cannon.HeadHunterPierceDamageLossPerTarget = F(loss);
        TestTarget a = Target(Vector3.forward * 2f), b = Target(Vector3.forward * 4f), c = Target(Vector3.forward * 6f);
        f.Weapon.TickAutomatic(0f, Vector3.forward);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(new[] { a.TotalDamage, b.TotalDamage, c.TotalDamage }, Is.EqualTo(new[] { first, second, third }));
    }

    [TestCase(1, WeaponStatMode.Automatic)] [TestCase(2, WeaponStatMode.Automatic)]
    [TestCase(1, WeaponStatMode.Manual)] [TestCase(2, WeaponStatMode.Manual)]
    public void C13_AuthoredPierceCountOrdersBeforeTruncationDespiteShuffledRegistration(int count, WeaponStatMode mode)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Runtime.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        f.Row.Automatic.Damage = F(10f); f.Row.Manual.Damage = F(10f);
        f.Row.Cannon.HeadHunterPierceTargetCount = I(count);
        f.Row.Cannon.HeadHunterPierceDamageLossPerTarget = F(0.25f);
        TestTarget far = Target(Vector3.forward * 6f);
        TestTarget near = Target(Vector3.forward * 2f);
        TestTarget middle = Target(Vector3.forward * 4f);
        if (mode == WeaponStatMode.Automatic) f.Weapon.TickAutomatic(0f, Vector3.forward);
        else f.Weapon.TickManual(0f, Vector3.forward, true);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(new[] { near.TotalDamage, middle.TotalDamage, far.TotalDamage },
            Is.EqualTo(count == 1 ? new[] { 10, 0, 0 } : new[] { 10, 8, 0 }));
        Assert.That(Field<List<Transform>>(f.Weapon, "_piercingTargets"),
            Is.EqualTo(count == 1 ? new[] { near.transform } : new[] { near.transform, middle.transform }));
    }

    [Test]
    public void C13_DisablingAuthoredPierceCountRestoresInheritedTargetSetAndOrder()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Runtime.State = WeaponState.Automatic; f.Row.Automatic.Damage = F(10f);
        TestTarget near = Target(Vector3.forward * 2f), middle = Target(Vector3.forward * 4f), far = Target(Vector3.forward * 6f);
        f.Row.Cannon.HeadHunterPierceTargetCount = I(2);
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, (int)Call(f.Weapon, "GetHeadHunterPierceLimit"), 20f, false, false, null);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(new[] { near.TotalDamage, middle.TotalDamage, far.TotalDamage }, Is.EqualTo(new[] { 10, 9, 0 }));
        f.Row.Cannon.HeadHunterPierceTargetCount.Enabled = false;
        Assert.That(f.Row.Cannon.HeadHunterPierceTargetCount.Value, Is.EqualTo(2), "Hidden authored value is preserved.");
        Assert.That((int)Call(f.Weapon, "GetHeadHunterPierceLimit"), Is.EqualTo(10));
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, (int)Call(f.Weapon, "GetHeadHunterPierceLimit"), 20f, false, false, null);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(Field<List<Transform>>(f.Weapon, "_piercingTargets"), Is.EqualTo(new[] { far.transform, middle.transform, near.transform }),
            "Inherited collinear selection keeps the published reverse-registration tie order.");
        Assert.That(new[] { near.TotalDamage, middle.TotalDamage, far.TotalDamage }, Is.EqualTo(new[] { 18, 18, 10 }));
    }

    [Test]
    public void C13_LossOnlyAuthoringPreservesInheritedTargetSetAndOrderBeyondDefaultCap()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Runtime.State = WeaponState.Automatic; f.Row.Automatic.Damage = F(10f);
        TestTarget[] targets = Enumerable.Range(1, 13).Select(i => Target(Vector3.forward * i * 2f)).ToArray();
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, (int)Call(f.Weapon, "GetHeadHunterPierceLimit"), 40f, false, false, null);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Transform[] inherited = Field<List<Transform>>(f.Weapon, "_piercingTargets").ToArray();
        int[] previousDamage = targets.Select(t => t.TotalDamage).ToArray();
        Assert.That(inherited, Is.EqualTo(targets.Skip(3).Reverse().Select(t => t.transform).ToArray()));
        f.Row.Cannon.HeadHunterPierceDamageLossPerTarget = F(1f);
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, (int)Call(f.Weapon, "GetHeadHunterPierceLimit"), 40f, false, false, null);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(Field<List<Transform>>(f.Weapon, "_piercingTargets"), Is.EqualTo(inherited));
        Assert.That(targets.Select((t, i) => t.TotalDamage - previousDamage[i]).ToArray(),
            Is.EqualTo(new[] { 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 10 }),
            "Only the loss changes; inherited count/order and the existing one-damage floor remain intact.");
    }

    [TestCase(0f, 3f, 0)] [TestCase(0.25f, 6f, 10)] [TestCase(2f, 6f, 10)]
    public void C13_HeadHunterAbilityChargeAndRangeReachActualQueuedImpact(float charge, float range, int damage)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Row.Ability.Damage = F(10f);
        f.Row.Cannon.HeadHunterWeakPointMultiplier = F(1f);
        f.Row.Cannon.HeadHunterAbilityChargeSeconds = F(charge);
        f.Row.Cannon.HeadHunterAbilityPierceRangeMeters = F(range);
        TestTarget target = Target(Vector3.forward * 4f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        if (charge > 0f)
        {
            f.Weapon.TickManual(charge * 0.5f, Vector3.forward, false);
            Assert.That(Field<bool>(f.Weapon, "_headHunterActiveCharging"), Is.True);
            Assert.That(target.TotalDamage, Is.Zero);
        }
        f.Weapon.TickManual(charge * 0.5f + 0.001f, Vector3.forward, false);
        Assert.That(Field<bool>(f.Weapon, "_headHunterActiveCharging"), Is.False);
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 1f);
        Assert.That(target.TotalDamage, Is.EqualTo(damage));
        Assert.That(f.Runtime.AbilityCooldownTimer, Is.GreaterThan(0f));
    }

    [TestCase(0.2499f, 20)] [TestCase(0.25f, 30)] [TestCase(0.7499f, 40)] [TestCase(0.75f, 50)]
    public void C13_AuthoredHeatStepChangesActualAbilityDamageAtThreshold(float heat, int expected)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        SetHeat(f, heat); f.Row.Ability.Damage = F(10f);
        f.Row.Cannon.HeadHunterWeakPointMultiplier = F(2f);
        f.Row.Cannon.HeadHunterWeakPointHeatStep = F(0.25f);
        f.Row.Cannon.HeadHunterWeakPointMaximum = F(9f);
        TestTarget target = Target(Vector3.forward * 4f);
        EmitAndHit(f, WeaponStatMode.Ability, target);
        Assert.That(target.TotalDamage, Is.EqualTo(expected));
    }

    [Test]
    public void M04_M07_ManualCadenceIgnoresHeatWhileTravelTimeRetainsHeatBonus()
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); SetHeat(f, 1f);
        f.Row.Manual.AttackCyclesPerSecond = F(2f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Assert.That(Field<float>(shell, "_travelTime"), Is.EqualTo(0.55f / 1.5f).Within(0.0001f));
        f.Weapon.TickManual(0.49f, Vector3.forward, true); Assert.That(LaunchCount(f), Is.EqualTo(1));
        f.Weapon.TickManual(0.011f, Vector3.forward, true); Assert.That(LaunchCount(f), Is.EqualTo(2));
    }

    [Test]
    public void C12_HeatDurationStepsReachActualContinuousEmissionCount()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA); SetHeat(f, 1f);
        f.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(2f);
        f.Row.Cannon.ContinuousAbilityDurationSeconds = F(1f);
        f.Row.Cannon.ContinuousAbilityHeatDurationSteps = F(3f);
        f.Weapon.UseActiveAbility(Vector3.forward); f.Weapon.TickManual(5f, Vector3.forward, false);
        Assert.That(f.Pool.ActiveLeasedCount, Is.EqualTo(8));
    }

    [Test]
    public void M17_SeparateHeatRateAddsToRainWithoutChangingOrdinaryPelletCount()
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA); SetHeat(f, 0.4f); Floor();
        f.Row.Mortar.GrapeshotAbilityProjectilesPerSecond = F(3f);
        f.Row.Mortar.GrapeshotAbilityDurationSeconds = F(2f);
        f.Row.Mortar.GrapeshotAbilityHeatProjectilesPerSecond = F(5f);
        f.Row.Mortar.GrapeshotPelletCount = I(1);
        f.Weapon.UseActiveAbility(Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(10));
        DestroyTransient<MortarShellImpact>();
        MortarShellImpact parent = EmitMortar(f, WeaponStatMode.Manual, Vector3.forward * 4f);
        Call(parent, "Detonate", Vector3.up * 3f);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
    }

    [Test]
    public void C11_HeadHunterDelayedImpactRetainsAuthoredKnockbackAcrossModeAndLevelChanges()
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        f.Row.Manual.Damage = F(10f); f.Row.Manual.KnockbackStrength = F(1f);
        TestTarget target = Target(Vector3.forward * 4f);
        Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, 1, 20f, false, false, null);
        f.Row.Manual.KnockbackStrength = F(0f); f.Runtime.Level = 7; f.Runtime.State = WeaponState.Automatic;
        Call(f.Weapon, "TickHeadHunterPendingImpacts", 10f);
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        Assert.That(receiver, Is.Not.Null);
        Assert.That(receiver.ConsumeDisplacement(0.1f).magnitude, Is.GreaterThan(0f));
    }

    [Test]
    public void M12_CritToggleDoesNotRewriteAlreadyLaunchedShellContext()
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Row.Manual.Damage = F(10f); f.Row.CriticalChance = F(1f); f.Row.Mortar.EnableCriticalHits = true;
        TestTarget target = Target(Vector3.forward * 4f);
        MortarShellImpact shell = EmitMortar(f, WeaponStatMode.Manual, target.transform.position);
        f.Row.Mortar.EnableCriticalHits = false; f.Runtime.State = WeaponState.Automatic;
        Call(shell, "Detonate", target.transform.position);
        Assert.That(target.TotalDamage, Is.EqualTo(20), "Crit is rolled at launch and survives later capability/mode edits.");
        EmitAndHit(f, WeaponStatMode.Manual, target);
        Assert.That(target.TotalDamage, Is.EqualTo(30));
    }

    [TestCase(WeaponStatMode.Automatic, 0.5f, 0.25f, 2.4f)]
    [TestCase(WeaponStatMode.Automatic, 0.25f, 0.25f, 2.8f)]
    [TestCase(WeaponStatMode.Automatic, 0.25f, 0f, 2f)]
    [TestCase(WeaponStatMode.Manual, 0.5f, 0.25f, 2.4f)]
    [TestCase(WeaponStatMode.Manual, 0.25f, 0.25f, 2.8f)]
    [TestCase(WeaponStatMode.Manual, 0.25f, 0f, 2f)]
    public void C03_C05_ContinuousHeatStepAndBonusChangeActualCadenceWithoutChangingAbility(
        WeaponStatMode mode, float step, float bonus, float expectedRate)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        SetHeat(f, 0.5f); Target(Vector3.forward * 4f);
        f.Data.AutomaticCannon.ContinuousFireAutoAttackSpeedMultiplier = 1.25f;
        f.Row.Automatic.AttackCyclesPerSecond = F(2f); f.Row.Manual.AttackCyclesPerSecond = F(2f);
        f.Row.Cannon.ContinuousModeHeatStep = F(step);
        f.Row.Cannon.ContinuousModeHeatSpeedBonus = F(bonus);
        f.Runtime.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        Action<float> tick = dt =>
        {
            if (mode == WeaponStatMode.Automatic) f.Weapon.TickAutomatic(dt, Vector3.forward);
            else f.Weapon.TickManual(dt, Vector3.forward, true);
        };
        tick(0f); tick(0.999f / expectedRate);
        Assert.That(LaunchCount(f), Is.EqualTo(1), "The ordinary action must wait for the new heat-adjusted interval.");
        tick(0.002f / expectedRate);
        Assert.That(LaunchCount(f), Is.EqualTo(2));

        f.Runtime.State = WeaponState.Manual;
        f.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(3f);
        f.Row.Cannon.ContinuousAbilityDurationSeconds = F(1f);
        f.Row.Cannon.ContinuousAbilityHeatDurationSteps = F(0f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        f.Weapon.TickManual(2f, Vector3.forward, false);
        Assert.That(LaunchCount(f), Is.EqualTo(5), "Ordinary heat cadence controls must not leak into Q emission.");
    }

    [TestCase(WeaponStatMode.Automatic)] [TestCase(WeaponStatMode.Manual)] [TestCase(WeaponStatMode.Ability)]
    public void C13_HeadHunterClassMultipliersChangeActualHealthAndRemainClassSpecific(WeaponStatMode mode)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB);
        WeaponStatAuthoringResolver.GetModeValues(f.Row, mode).Damage = F(10f);
        f.Row.Cannon.HeadHunterWeakPointMultiplier = F(1f);
        TestTarget target = Target(Vector3.forward * 4f);
        foreach (var factors in new[] { (elite: 2.5f, boss: 4f), (elite: 7f, boss: 4f), (elite: 7f, boss: 1.2f), (elite: 0f, boss: 0f) })
        {
            f.Row.Cannon.HeadHunterEliteDamageMultiplier = F(factors.elite);
            f.Row.Cannon.HeadHunterBossDamageMultiplier = F(factors.boss);
            foreach (WeaponEnemyKind kind in Enum.GetValues(typeof(WeaponEnemyKind)))
            {
                target.name = "Stat " + kind + " target";
                Assert.That(WeaponEnemyClassifier.GetKind(target.transform), Is.EqualTo(kind));
                int before = target.TotalDamage;
                EmitAndHit(f, mode, target);
                float factor = kind == WeaponEnemyKind.Elite ? factors.elite : kind == WeaponEnemyKind.Boss ? factors.boss : 1f;
                Assert.That(target.TotalDamage - before, Is.EqualTo(Mathf.Max(1, Mathf.RoundToInt(10f * factor))),
                    "Each class consumes only its own factor; zero preserves the existing one-damage minimum.");
            }
        }
    }

    [TestCase(WeaponUpgradePath.None, 0f, 2)] [TestCase(WeaponUpgradePath.None, 2f, 3)]
    [TestCase(WeaponUpgradePath.None, 6f, 5)] [TestCase(WeaponUpgradePath.PathB, 0f, 2)]
    [TestCase(WeaponUpgradePath.PathB, 2f, 3)] [TestCase(WeaponUpgradePath.PathB, 6f, 5)]
    public void M09_AbilityHeatShellBonusChangesActualVolleyWithoutChangingOrdinaryShellCount(
        WeaponUpgradePath path, float bonus, int expected)
    {
        Fixture f = Create(WeaponType.Mortar, path == WeaponUpgradePath.None ? 1 : 6, path);
        SetHeat(f, 0.5f); Floor();
        f.Row.Mortar.ActiveShellCount = I(2);
        f.Row.Mortar.AbilityHeatShellBonus = F(bonus);
        f.Row.Mortar.BarrageRadiusMeters = F(0f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(expected));
        DestroyTransient<MortarShellImpact>();
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(1), "The Q heat count cannot multiply an ordinary manual action.");
    }

    [TestCase(0f, 3)] [TestCase(0.4f, 1)] [TestCase(2f, 1)]
    public void M09_BasicShellSpacingChangesActualTerrainImpactSchedule(float spacing, int firstWaveHits)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); Floor();
        f.Row.Ability.Damage = F(10f); f.Row.Mortar.ActiveShellCount = I(3);
        f.Row.Mortar.AbilityHeatShellBonus = F(0f); f.Row.Mortar.BarrageRadiusMeters = F(0f);
        f.Row.Mortar.BasicAbilityShellSpacingSeconds = F(spacing);
        f.Data.Mortar.MortarActiveDropHeight = 10f; f.Data.Mortar.MortarActiveTravelTime = 0.8f;
        TestTarget target = Target(Vector3.forward * f.Data.BaseRange);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact[] shells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(shells.Length, Is.EqualTo(3));
        foreach (MortarShellImpact shell in shells)
        {
            AdvanceMortarTo(shell, 0.85f);
        }
        Assert.That(target.TotalDamage, Is.EqualTo(firstWaveHits * 10),
            "At the same elapsed time the spacing must change how many shells actually hit terrain and apply damage.");
        foreach (MortarShellImpact shell in Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None))
        {
            AdvanceMortarTo(shell, Field<float>(shell, "_travelTime") + 0.05f);
        }
        Assert.That(target.TotalDamage, Is.EqualTo(30), "Spacing changes timing, not the promised shell count or per-shell damage.");
    }

    [TestCase(0f)] [TestCase(0.25f)] [TestCase(2f)]
    public void M16_GrapeshotDamageMultiplierReachesChildrenWithoutChangingAbilityOrOtherPath(float factor)
    {
        Fixture f = Create(WeaponType.Mortar, 6, WeaponUpgradePath.PathA);
        f.Row.Manual.Damage = F(12f); f.Row.Automatic.Damage = F(20f); f.Row.Ability.Damage = F(19f);
        f.Row.Mortar.GrapeshotPelletCount = I(1);
        f.Row.Mortar.GrapeshotDamageMultiplier = F(factor);
        TestTarget target = Target(Vector3.forward * 4f);
        foreach (WeaponStatMode mode in Enum.GetValues(typeof(WeaponStatMode)))
        {
            int before = target.TotalDamage;
            EmitAndHit(f, mode, target);
            int expected = mode == WeaponStatMode.Ability ? 19
                : Mathf.Max(1, Mathf.RoundToInt((mode == WeaponStatMode.Manual ? 12f : 20f) * factor));
            Assert.That(target.TotalDamage - before, Is.EqualTo(expected));
            string diagnosticMode = mode == WeaponStatMode.Automatic ? "Automatic" : mode == WeaponStatMode.Manual ? "Manual" : "Active Ability";
            Assert.That(Diagnostic(f, diagnosticMode, "Damage / hit (non-critical)"), Is.EqualTo(expected.ToString()));
            if (mode != WeaponStatMode.Ability)
            {
                Assert.That(Diagnostic(f, diagnosticMode, "Damage unit"), Is.EqualTo("Per pellet; parent shell deals no damage"));
                Assert.That(Diagnostic(f, diagnosticMode, "Source damage before split (non-critical)"),
                    Is.EqualTo(mode == WeaponStatMode.Manual ? "12" : "20"));
            }
        }
        var other = WeaponStatProfile.CreateRow(WeaponType.Mortar, 6, WeaponUpgradePath.PathB);
        other.Manual.Damage = F(12f); f.Data.StatProfile.Levels.Add(other);
        f.Runtime.SelectedPath = WeaponUpgradePath.PathB;
        int prior = target.TotalDamage;
        EmitAndHit(f, WeaponStatMode.Manual, target);
        Assert.That(target.TotalDamage - prior, Is.EqualTo(12), "Switching paths cannot retain the old child damage scale.");
    }

    [TestCase(2f, 0.25f)] [TestCase(10f, 0.25f)] [TestCase(2f, 1.25f)] [TestCase(10f, 1.25f)]
    public void MortarLegacyDropHeightAndTravelTimeReachSpawnAndTerrainImpact(float dropHeight, float travelTime)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); Floor();
        f.Row.Ability.Damage = F(10f); f.Row.Mortar.ActiveShellCount = I(1);
        f.Row.Mortar.BarrageRadiusMeters = F(0f);
        f.Data.Mortar.MortarActiveDropHeight = dropHeight; f.Data.Mortar.MortarActiveTravelTime = travelTime;
        TestTarget target = Target(Vector3.forward * f.Data.BaseRange);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Assert.That(shell.transform.position.y - Field<Vector3>(shell, "_target").y, Is.EqualTo(dropHeight).Within(0.0001f));
        AdvanceMortarTo(shell, travelTime * 0.25f);
        Assert.That(target.TotalDamage, Is.Zero);
        Assert.That(shell.transform.position.y, Is.GreaterThan(0.2f));
        AdvanceMortarTo(shell, travelTime + 0.05f);
        Assert.That(target.TotalDamage, Is.EqualTo(10));
    }

    [TestCase(0.05f, false)] [TestCase(0.3f, true)]
    public void MortarLegacyCollisionRadiusChangesActualTerrainSweep(float collisionRadius, bool shouldHit)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Data.Mortar.MortarShellCollisionRadius = collisionRadius;
        GameObject wall = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0.3f, 0f, 2f);
        wall.transform.localScale = new Vector3(0.1f, 2f, 0.2f);
        Physics.SyncTransforms();
        MortarShellImpact shell = EmitMortar(f, WeaponStatMode.Manual, Vector3.forward * 4f);
        AdvanceMortarTo(shell, 0.3f);
        Assert.That(shell == null, Is.EqualTo(shouldHit),
            "Only the larger swept sphere reaches the off-axis terrain blocker and detonates.");
    }

    [TestCase(WeaponStatMode.Automatic, 0.4f, false)] [TestCase(WeaponStatMode.Automatic, 1.5f, true)]
    [TestCase(WeaponStatMode.Manual, 0.4f, false)] [TestCase(WeaponStatMode.Manual, 1.5f, true)]
    [TestCase(WeaponStatMode.Ability, 0.4f, false)] [TestCase(WeaponStatMode.Ability, 1.5f, true)]
    public void MortarLegacyExplosionRadiiReachActualModeBlastWhenOverridesAreDisabled(
        WeaponStatMode mode, float radius, bool sideTargetHit)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Row.Automatic.Damage = F(10f); f.Row.Manual.Damage = F(10f); f.Row.Ability.Damage = F(10f);
        if (mode == WeaponStatMode.Automatic)
        {
            f.Data.Mortar.MortarAutoExplosionRadius = radius;
            f.Data.Mortar.MortarActiveExplosionRadius = 7f;
            Target(Vector3.forward * 4f);
            f.Runtime.State = WeaponState.Automatic;
            f.Weapon.TickAutomatic(0f, Vector3.forward);
        }
        else if (mode == WeaponStatMode.Manual)
        {
            f.Data.Mortar.MortarManualExplosionRadius = radius;
            f.Data.Mortar.MortarAutoExplosionRadius = 7f;
            f.Weapon.TickManual(0f, Vector3.forward, true);
        }
        else
        {
            f.Data.Mortar.MortarActiveExplosionRadius = radius;
            f.Data.Mortar.MortarAutoExplosionRadius = 7f;
            f.Row.Mortar.ActiveShellCount = I(1); f.Row.Mortar.BarrageRadiusMeters = F(0f);
            Floor(); f.Weapon.UseActiveAbility(Vector3.forward);
        }
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Vector3 center = Field<Vector3>(shell, "_target");
        TestTarget side = Target(center + Vector3.right * 0.75f);
        Assert.That(Field<float>(shell, "_explosionRadius"), Is.EqualTo(radius));
        Call(shell, "Detonate", center);
        Assert.That(side.TotalDamage, Is.EqualTo(sideTargetHit ? 10 : 0));
        string otherMode = mode == WeaponStatMode.Automatic ? "Active Ability" : "Automatic";
        Assert.That(Diagnostic(f, otherMode, "Explosion radius"), Is.EqualTo("7 m"));
    }

    [TestCase(0f)] [TestCase(3f)]
    public void MortarLegacyBarrageRadiusChangesActualTargetsWithoutChangingShellBlastOrManual(float radius)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); Floor();
        f.Data.Mortar.MortarBarrageRadius = radius; f.Data.Mortar.MortarActiveExplosionRadius = 0.5f;
        f.Row.Mortar.ActiveShellCount = I(8);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact[] shells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
        Assert.That(shells.Length, Is.EqualTo(8));
        float maximumDistance = 0f;
        foreach (MortarShellImpact shell in shells)
        {
            Vector3 target = Field<Vector3>(shell, "_target");
            float distance = new Vector2(target.x, target.z - f.Data.BaseRange).magnitude;
            maximumDistance = Mathf.Max(maximumDistance, distance);
            Assert.That(distance, Is.LessThanOrEqualTo(radius + 0.0001f));
            Assert.That(Field<float>(shell, "_explosionRadius"), Is.EqualTo(0.5f));
        }
        if (radius > 0f) Assert.That(maximumDistance, Is.GreaterThan(radius * 0.1f));
        DestroyTransient<MortarShellImpact>();
        f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact manual = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Assert.That(Field<Vector3>(manual, "_target").x, Is.Zero);
        Assert.That(Field<Vector3>(manual, "_target").z, Is.EqualTo(f.Data.Mortar.MortarManualLevelRange));
    }

    [TestCase(0.4f, 10)] [TestCase(1.5f, 0)]
    public void MortarLegacyAutomaticTravelTimeChangesActualTerrainImpactTime(float travelTime, int damageAtHalfSecond)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); Floor();
        f.Data.Mortar.MortarShellTravelTime = travelTime; f.Data.Mortar.MortarArcHeight = 3f;
        Field<Transform>(f.Weapon, "Owner").position = Vector3.up; // Start above terrain, as a real muzzle does.
        f.Row.Automatic.Damage = F(10f); f.Runtime.State = WeaponState.Automatic;
        TestTarget target = Target(Vector3.forward * 4f);
        f.Weapon.TickAutomatic(0f, Vector3.forward);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        for (float elapsed = 0.01f; elapsed <= 0.5001f && shell != null; elapsed += 0.01f)
        {
            AdvanceMortarTo(shell, elapsed);
        }
        Assert.That(target.TotalDamage, Is.EqualTo(damageAtHalfSecond));
        for (float elapsed = 0.51f; elapsed <= travelTime + 0.1001f && shell != null; elapsed += 0.01f)
        {
            AdvanceMortarTo(shell, elapsed);
        }
        Assert.That(target.TotalDamage, Is.EqualTo(10), "The automatic travel setting changes arrival timing, not damage.");
    }

    [TestCase(2, 25f, 0.2499f, 2)] [TestCase(2, 25f, 0.25f, 3)]
    [TestCase(7, 25f, 0.25f, 8)] [TestCase(2, 50f, 0.25f, 2)]
    public void CannonLegacyAbilityCountAndHeatStepChangeActualVolleyWithoutChangingManualOrContinuousQ(
        int baseCount, float heatStepPercent, float heat, int expected)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None); SetHeat(f, heat);
        f.Data.AutomaticCannon.CannonActiveBaseBulletCount = baseCount;
        f.Data.AutomaticCannon.CannonActiveHeatBulletStepPercent = heatStepPercent;
        f.Weapon.UseActiveAbility(Vector3.forward);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(expected));
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(expected + 1), "Q count controls do not change the manual burst.");

        Fixture continuous = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA); SetHeat(continuous, heat);
        continuous.Data.AutomaticCannon.CannonActiveBaseBulletCount = baseCount;
        continuous.Data.AutomaticCannon.CannonActiveHeatBulletStepPercent = heatStepPercent;
        continuous.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(4f);
        continuous.Row.Cannon.ContinuousAbilityDurationSeconds = F(0.5f);
        continuous.Row.Cannon.ContinuousAbilityHeatDurationSteps = F(0f);
        continuous.Weapon.UseActiveAbility(Vector3.forward);
        continuous.Weapon.TickManual(0.5f, Vector3.forward, false);
        Assert.That(ActiveProjectiles(continuous), Has.Length.EqualTo(2), "Advanced Q retains its own emission schedule.");
    }

    [TestCase(WeaponUpgradePath.None, 0f)] [TestCase(WeaponUpgradePath.None, 20f)]
    [TestCase(WeaponUpgradePath.PathA, 0f)] [TestCase(WeaponUpgradePath.PathA, 20f)]
    public void CannonLegacyAbilityScatterChangesActualDirectionsWithoutChangingManualOrVolleySize(WeaponUpgradePath path, float degrees)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, path == WeaponUpgradePath.None ? 1 : 6, path);
        f.Data.AutomaticCannon.CannonAbilityScatterRadius = degrees;
        f.Data.AutomaticCannon.CannonActiveBaseBulletCount = 12;
        if (path == WeaponUpgradePath.PathA)
        {
            f.Row.Cannon.ContinuousAbilityBulletsPerSecond = F(24f);
            f.Row.Cannon.ContinuousAbilityDurationSeconds = F(0.5f);
            f.Row.Cannon.ContinuousAbilityHeatDurationSteps = F(0f);
        }
        UnityEngine.Random.InitState(1827);
        f.Weapon.UseActiveAbility(Vector3.forward);
        if (path == WeaponUpgradePath.PathA) f.Weapon.TickManual(0.5f, Vector3.forward, false);
        Projectile[] ability = ActiveProjectiles(f);
        Assert.That(ability, Has.Length.EqualTo(12));
        Assert.That(Diagnostic(f, "Active Ability", "Projectile scatter"), Is.EqualTo(degrees.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " degrees"));
        float maximumAngle = ability.Max(p => Vector3.Angle(Vector3.forward, Field<Vector3>(p, "_direction")));
        if (degrees == 0f) Assert.That(maximumAngle, Is.LessThan(0.001f));
        else
        {
            Assert.That(maximumAngle, Is.GreaterThan(2f));
            Assert.That(maximumAngle, Is.LessThanOrEqualTo(degrees + 0.01f));
        }
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Projectile manual = ActiveProjectiles(f).Single(p => !ability.Contains(p));
        Assert.That(Vector3.Angle(Vector3.forward, Field<Vector3>(manual, "_direction")), Is.LessThan(0.001f));
    }

    [TestCase(25f, 3, 0.5f, 0.2499f, 10)] [TestCase(25f, 3, 0.5f, 0.25f, 15)]
    [TestCase(50f, 3, 0.5f, 0.25f, 10)] [TestCase(25f, 1, 0.5f, 0.75f, 15)]
    [TestCase(25f, 3, 0.5f, 0.75f, 25)] [TestCase(25f, 3, 0f, 0.75f, 10)]
    public void CannonLegacyHeatDamageControlsChangeActualAutomaticHpWithoutChangingManualAbilityOrHeadHunter(
        float stepPercent, int thresholdCount, float bonus, float heat, int expected)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None); SetHeat(f, heat);
        f.Data.AutomaticCannon.CannonHeatDamageThresholdStepPercent = stepPercent;
        f.Data.AutomaticCannon.CannonHeatDamageThresholdCount = thresholdCount;
        f.Data.AutomaticCannon.CannonHeatDamageBonusPerThreshold = bonus;
        f.Row.Automatic.Damage = F(10f); f.Row.Manual.Damage = F(10f); f.Row.Ability.Damage = F(10f);
        TestTarget target = Target(Vector3.forward * 4f);
        f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward);
        Projectile automatic = ActiveProjectiles(f).Single();
        Assert.That((bool)Call(automatic, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 5f), Is.True);
        Assert.That(target.TotalDamage, Is.EqualTo(expected));
        EmitAndHit(f, WeaponStatMode.Manual, target);
        EmitAndHit(f, WeaponStatMode.Ability, target);
        Assert.That(target.TotalDamage, Is.EqualTo(expected + 20));

        Fixture headHunter = Create(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB); SetHeat(headHunter, heat);
        headHunter.Data.AutomaticCannon.CannonHeatDamageThresholdStepPercent = stepPercent;
        headHunter.Data.AutomaticCannon.CannonHeatDamageThresholdCount = thresholdCount;
        headHunter.Data.AutomaticCannon.CannonHeatDamageBonusPerThreshold = bonus;
        headHunter.Row.Automatic.Damage = F(10f);
        EmitAndHit(headHunter, WeaponStatMode.Automatic, target);
        Assert.That(target.TotalDamage, Is.EqualTo(expected + 30), "Head Hunter automatic damage has no ordinary heat factor.");
    }

    [TestCase(WeaponStatMode.Automatic)] [TestCase(WeaponStatMode.Manual)] [TestCase(WeaponStatMode.Ability)]
    public void CannonLegacyCriticalFactorChangesActualHpAndAuthoredReplacementShieldsItsRow(WeaponStatMode mode)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        f.Row.Automatic.Damage = F(10f); f.Row.Manual.Damage = F(10f); f.Row.Ability.Damage = F(10f);
        f.Row.CriticalChance = F(1f);
        TestTarget target = Target(Vector3.forward * 4f);
        foreach (float factor in new[] { 2f, 4f })
        {
            f.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride = factor;
            int previous = target.TotalDamage; EmitAndHit(f, mode, target);
            Assert.That(target.TotalDamage - previous, Is.EqualTo(20f * factor), "Player critical severity 2 multiplies the weapon factor once.");
        }
        f.Row.CriticalDamageMultiplier = F(3f);
        f.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride = 9f;
        int beforeAuthored = target.TotalDamage; EmitAndHit(f, mode, target);
        Assert.That(target.TotalDamage - beforeAuthored, Is.EqualTo(60));
        Fixture mortar = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        mortar.Row.Manual.Damage = F(10f); EmitAndHit(mortar, WeaponStatMode.Manual, target);
        Assert.That(target.TotalDamage, Is.EqualTo(beforeAuthored + 70), "The other weapon keeps its default non-critical channel.");
    }

    [TestCase(WeaponStatMode.Automatic, 0f)] [TestCase(WeaponStatMode.Automatic, 6f)]
    [TestCase(WeaponStatMode.Manual, 0f)] [TestCase(WeaponStatMode.Manual, 6f)]
    public void MortarLegacyArcHeightChangesActualMidflightPositionWithoutChangingVerticalAbility(WeaponStatMode mode, float height)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Data.Mortar.MortarArcHeight = height;
        Field<Transform>(f.Weapon, "Owner").position = Vector3.up * 4f;
        if (mode == WeaponStatMode.Automatic)
        {
            Target(Vector3.up * 4f + Vector3.forward * 4f);
            f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward);
        }
        else f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Vector3 start = Field<Vector3>(shell, "_start"), end = Field<Vector3>(shell, "_target");
        AdvanceMortarTo(shell, Field<float>(shell, "_travelTime") * 0.5f);
        Assert.That(shell, Is.Not.Null);
        if (mode == WeaponStatMode.Manual && height == 0f)
            Assert.That(shell.transform.position.y, Is.GreaterThan((start.y + end.y) * 0.5f),
                "Manual fire retains a mortar arc across elevation changes even when the authored height is zero.");
        else
            Assert.That(shell.transform.position.y, Is.EqualTo((start.y + end.y) * 0.5f + height).Within(0.0001f));
        Assert.That(shell.transform.position.z, Is.EqualTo((start.z + end.z) * 0.5f).Within(0.0001f));
        DestroyTransient<MortarShellImpact>(); Floor();
        f.Runtime.State = WeaponState.Manual; f.Row.Mortar.ActiveShellCount = I(1); f.Row.Mortar.BarrageRadiusMeters = F(0f);
        f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact ability = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        start = Field<Vector3>(ability, "_start"); end = Field<Vector3>(ability, "_target");
        AdvanceMortarTo(ability, Field<float>(ability, "_travelTime") * 0.5f);
        Assert.That(ability.transform.position.y, Is.EqualTo((start.y + end.y) * 0.5f).Within(0.0001f));
        Assert.That(ability.transform.position.z, Is.EqualTo(start.z).Within(0.0001f));
    }

    [TestCase(WeaponStatMode.Manual, 0f, 2)] [TestCase(WeaponStatMode.Manual, 3.6f, 1)]
    [TestCase(WeaponStatMode.Automatic, 0f, 2)] [TestCase(WeaponStatMode.Automatic, 3.6f, 1)]
    public void CannonLegacyModeSpacingChangesActualDelayedEmissionWithoutChangingOppositeBurst(WeaponStatMode mode, float spacing, int earlyCount)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        f.Data.AutomaticCannon.CannonManualBurstCount = 2; f.Data.AutomaticCannon.CannonAutoBurstCount = 2;
        f.Data.AutomaticCannon.CannonManualLineSpacing = mode == WeaponStatMode.Manual ? spacing : 0f;
        f.Data.AutomaticCannon.CannonAutoLineSpacing = mode == WeaponStatMode.Automatic ? spacing : 0f;
        f.Data.AutomaticCannon.CannonBurstShotInterval = 0.01f;
        Target(Vector3.forward * 4f);
        if (mode == WeaponStatMode.Manual) f.Weapon.TickManual(0f, Vector3.forward, true);
        else { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(1));
        Call(f.Weapon, "TickPendingLineBurst", 0.049f);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(earlyCount));
        Call(f.Weapon, "TickPendingLineBurst", 0.152f);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(2));
        Set(f.Weapon, "FireTimer", 0f);
        if (mode == WeaponStatMode.Manual) { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        else { f.Runtime.State = WeaponState.Manual; f.Weapon.TickManual(0f, Vector3.forward, true); }
        Call(f.Weapon, "TickPendingLineBurst", 0.049f);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(4), "The other mode retains its own separation setting.");
    }

    [TestCase(0f)] [TestCase(20f)]
    public void CannonLegacyAutomaticAccuracyChangesEmittedDirectionWithoutChangingManual(float degrees)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        f.Data.AutomaticCannon.CannonAutoAccuracySpreadDegrees = degrees;
        Target(Vector3.forward * 4f); UnityEngine.Random.InitState(421);
        f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward);
        Projectile automatic = ActiveProjectiles(f).Single();
        float angle = Vector3.Angle(Vector3.forward, Field<Vector3>(automatic, "_direction"));
        if (degrees == 0f) Assert.That(angle, Is.LessThan(0.001f));
        else { Assert.That(angle, Is.GreaterThan(0.01f)); Assert.That(angle, Is.LessThanOrEqualTo(degrees + 0.01f)); }
        f.Runtime.State = WeaponState.Manual; Set(f.Weapon, "FireTimer", 0f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        Projectile manual = ActiveProjectiles(f).Single(p => p != automatic);
        Assert.That(Vector3.Angle(Vector3.forward, Field<Vector3>(manual, "_direction")), Is.LessThan(0.001f));
    }

    [TestCase(WeaponStatMode.Automatic, 0f)] [TestCase(WeaponStatMode.Automatic, 2f)]
    [TestCase(WeaponStatMode.Manual, 0f)] [TestCase(WeaponStatMode.Manual, 2f)]
    public void MortarLegacyAccuracyChangesAutomaticLandingWhileManualRemainsPrecise(WeaponStatMode mode, float radius)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Data.Mortar.MortarAutoAccuracyRadius = mode == WeaponStatMode.Automatic ? radius : 0f;
        f.Data.Mortar.MortarManualAccuracyRadius = mode == WeaponStatMode.Manual ? radius : 0f;
        Target(Vector3.forward * 4f); UnityEngine.Random.InitState(572);
        if (mode == WeaponStatMode.Automatic) { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        else f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Vector3 aim = Vector3.forward * (mode == WeaponStatMode.Automatic ? 4f : f.Data.Mortar.MortarManualLevelRange);
        float displacement = Vector3.ProjectOnPlane(Field<Vector3>(shell, "_target") - aim, Vector3.up).magnitude;
        if (radius == 0f || mode == WeaponStatMode.Manual) Assert.That(displacement, Is.LessThan(0.0001f));
        else { Assert.That(displacement, Is.GreaterThan(0.01f)); Assert.That(displacement, Is.LessThanOrEqualTo(radius + 0.0001f)); }
        DestroyTransient<MortarShellImpact>(); Set(f.Weapon, "FireTimer", 0f);
        if (mode == WeaponStatMode.Automatic) { f.Runtime.State = WeaponState.Manual; f.Weapon.TickManual(0f, Vector3.forward, true); }
        else { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        MortarShellImpact other = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Vector3 otherAim = Vector3.forward * (mode == WeaponStatMode.Automatic ? f.Data.Mortar.MortarManualLevelRange : 4f);
        Assert.That(Vector3.ProjectOnPlane(Field<Vector3>(other, "_target") - otherAim, Vector3.up).magnitude, Is.LessThan(0.0001f));
    }

    [TestCase(WeaponStatMode.Automatic, 0f, 100)] [TestCase(WeaponStatMode.Automatic, 1f, 50)]
    [TestCase(WeaponStatMode.Manual, 0f, 100)] [TestCase(WeaponStatMode.Manual, 1f, 50)]
    [TestCase(WeaponStatMode.Ability, 0f, 100)] [TestCase(WeaponStatMode.Ability, 1f, 50)]
    public void MortarLegacyFalloffChangesActualPeripheralHpButKeepsCenterDamage(WeaponStatMode mode, float falloff, int edgeDamage)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None);
        f.Data.Mortar.MortarExplosionFalloff = falloff;
        f.Data.Mortar.MortarAutoExplosionRadius = f.Data.Mortar.MortarManualExplosionRadius = f.Data.Mortar.MortarActiveExplosionRadius = 2f;
        f.Row.Automatic.Damage = F(100f); f.Row.Manual.Damage = F(100f); f.Row.Ability.Damage = F(100f);
        TestTarget center = null;
        if (mode == WeaponStatMode.Automatic)
        {
            center = Target(Vector3.forward * 4f);
            f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward);
        }
        else if (mode == WeaponStatMode.Manual) f.Weapon.TickManual(0f, Vector3.forward, true);
        else
        {
            Floor(); f.Row.Mortar.ActiveShellCount = I(1); f.Row.Mortar.BarrageRadiusMeters = F(0f);
            f.Weapon.UseActiveAbility(Vector3.forward);
        }
        MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Vector3 target = Field<Vector3>(shell, "_target");
        center ??= Target(target);
        TestTarget edge = Target(target + Vector3.right);
        Call(shell, "Detonate", target);
        Assert.That(center.TotalDamage, Is.EqualTo(100));
        Assert.That(edge.TotalDamage, Is.EqualTo(edgeDamage));
    }

    [Test]
    public void MortarLegacyAbilityDamageScaleChangesActualHpWhileAuthoredAbilityAndManualStayIndependent()
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); Floor();
        f.Row.Mortar.ActiveShellCount = I(1); f.Row.Mortar.BarrageRadiusMeters = F(0f);
        TestTarget target = Target(Vector3.forward * f.Data.BaseRange);
        foreach (float scale in new[] { 1f, 3f })
        {
            f.Data.Mortar.MortarActiveDamageScale = scale; f.Runtime.AbilityCooldownTimer = 0f;
            int previous = target.TotalDamage; f.Weapon.UseActiveAbility(Vector3.forward);
            MortarShellImpact shell = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
            Call(shell, "Detonate", Field<Vector3>(shell, "_target"));
            Assert.That(target.TotalDamage - previous, Is.EqualTo(20f * scale));
            DestroyTransient<MortarShellImpact>();
        }
        f.Row.Ability.Damage = F(23f); f.Data.Mortar.MortarActiveDamageScale = 99f; f.Runtime.AbilityCooldownTimer = 0f;
        int beforeAuthored = target.TotalDamage; f.Weapon.UseActiveAbility(Vector3.forward);
        MortarShellImpact authored = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        Call(authored, "Detonate", Field<Vector3>(authored, "_target")); DestroyTransient<MortarShellImpact>();
        Assert.That(target.TotalDamage - beforeAuthored, Is.EqualTo(23));
        f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact manual = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        target.transform.position = Field<Vector3>(manual, "_target");
        Physics.SyncTransforms();
        Call(manual, "Detonate", Field<Vector3>(manual, "_target"));
        Assert.That(target.TotalDamage - beforeAuthored, Is.EqualTo(43));
    }

    [TestCase(WeaponStatMode.Automatic, 1, 0f)] [TestCase(WeaponStatMode.Automatic, 4, 0f)]
    [TestCase(WeaponStatMode.Automatic, 4, 20f)] [TestCase(WeaponStatMode.Manual, 1, 0f)]
    [TestCase(WeaponStatMode.Manual, 4, 0f)] [TestCase(WeaponStatMode.Manual, 4, 20f)]
    public void CannonLegacyBurstCountsAndProjectileScatterReachActualEmissionWithModeCountIsolation(
        WeaponStatMode mode, int count, float scatter)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        f.Data.AutomaticCannon.CannonAutoBurstCount = mode == WeaponStatMode.Automatic ? count : 1;
        f.Data.AutomaticCannon.CannonManualBurstCount = mode == WeaponStatMode.Manual ? count : 1;
        f.Data.AutomaticCannon.CannonBurstProjectileScatterDegrees = scatter;
        Target(Vector3.forward * 4f); UnityEngine.Random.InitState(915);
        if (mode == WeaponStatMode.Automatic) { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        else f.Weapon.TickManual(0f, Vector3.forward, true);
        Call(f.Weapon, "TickPendingLineBurst", 1f);
        Projectile[] burst = ActiveProjectiles(f);
        Assert.That(burst, Has.Length.EqualTo(count));
        float maximumAngle = burst.Max(p => Vector3.Angle(Vector3.forward, Field<Vector3>(p, "_direction")));
        if (scatter == 0f) Assert.That(maximumAngle, Is.LessThan(0.001f));
        else { Assert.That(maximumAngle, Is.GreaterThan(0.01f)); Assert.That(maximumAngle, Is.LessThanOrEqualTo(scatter + 0.01f)); }
        Set(f.Weapon, "FireTimer", 0f);
        if (mode == WeaponStatMode.Automatic) { f.Runtime.State = WeaponState.Manual; f.Weapon.TickManual(0f, Vector3.forward, true); }
        else { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); }
        Call(f.Weapon, "TickPendingLineBurst", 1f);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(count + 1), "The opposite mode retains its independently authored count.");
    }

    [TestCase(WeaponStatMode.Automatic, 2f, 1)] [TestCase(WeaponStatMode.Automatic, 4f, 2)]
    [TestCase(WeaponStatMode.Manual, 2f, 1)] [TestCase(WeaponStatMode.Manual, 4f, 2)]
    public void CannonLegacyModeCadenceChangesActualCycleTimingWithoutChangingOppositeMode(WeaponStatMode mode, float rate, int count)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        foreach (WeaponLevelData level in f.Data.LevelData) level.AttackRateMultiplier = 1f;
        f.Data.AutomaticCannon.CannonAutoBurstsPerSecond = mode == WeaponStatMode.Automatic ? rate : 2f;
        f.Data.AutomaticCannon.CannonManualBurstsPerSecond = mode == WeaponStatMode.Manual ? rate : 2f;
        Target(Vector3.forward * 4f);
        if (mode == WeaponStatMode.Automatic)
        {
            f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); f.Weapon.TickAutomatic(0.3f, Vector3.forward);
        }
        else { f.Weapon.TickManual(0f, Vector3.forward, true); f.Weapon.TickManual(0.3f, Vector3.forward, true); }
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(count));
        Set(f.Weapon, "FireTimer", 0f);
        if (mode == WeaponStatMode.Automatic)
        {
            f.Runtime.State = WeaponState.Manual; f.Weapon.TickManual(0f, Vector3.forward, true); f.Weapon.TickManual(0.3f, Vector3.forward, true);
        }
        else { f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); f.Weapon.TickAutomatic(0.3f, Vector3.forward); }
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(count + 1));
    }

    [TestCase(WeaponUpgradePath.PathA, 1f, 1)] [TestCase(WeaponUpgradePath.PathA, 2f, 2)]
    [TestCase(WeaponUpgradePath.PathB, 1f, 1)] [TestCase(WeaponUpgradePath.PathB, 2f, 2)]
    public void CannonLegacyAutomaticPathSpeedChangesActualShotsWithoutChangingManualTiming(WeaponUpgradePath path, float speed, int count)
    {
        Fixture f = Create(WeaponType.AutomaticCannon, 6, path);
        foreach (WeaponLevelData level in f.Data.LevelData) level.AttackRateMultiplier = 1f;
        f.Data.PathA.AttackRateMultiplier = f.Data.PathB.AttackRateMultiplier = 1f;
        f.Data.AutomaticCannon.CannonAutoBurstsPerSecond = f.Data.AutomaticCannon.CannonManualBurstsPerSecond = 2f;
        if (path == WeaponUpgradePath.PathA) f.Data.AutomaticCannon.ContinuousFireAutoAttackSpeedMultiplier = speed;
        else f.Data.AutomaticCannon.HeadHunterAutoAttackSpeedMultiplier = speed;
        Target(Vector3.forward * 4f);
        f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); f.Weapon.TickAutomatic(0.3f, Vector3.forward);
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(count));
        f.Runtime.State = WeaponState.Manual; Set(f.Weapon, "FireTimer", 0f);
        f.Weapon.TickManual(0f, Vector3.forward, true);
        f.Weapon.TickManual(0.3f, Vector3.forward, false); f.Weapon.TickManual(0f, Vector3.forward, true);
        int manualCount = path == WeaponUpgradePath.PathA ? 1 : 2;
        Assert.That(ActiveProjectiles(f), Has.Length.EqualTo(count + manualCount), "The automatic path speed factor does not alter manual cadence.");
    }

    [TestCase(0.5f, 3f, 1)] [TestCase(1f, 0f, 1)] [TestCase(1f, 3f, 2)]
    public void MortarLegacyAutomaticHeatRateChangesActualLaunchTimingWithoutChangingManual(float heat, float bonus, int count)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); SetHeat(f, heat);
        f.Data.Mortar.MortarHeatFireRateBonusAbove50 = bonus;
        f.Row.Automatic.AttackCyclesPerSecond = F(2f); f.Row.Manual.AttackCyclesPerSecond = F(2f);
        Target(Vector3.forward * 4f);
        f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward); f.Weapon.TickAutomatic(0.2f, Vector3.forward);
        Assert.That(LaunchCount(f), Is.EqualTo(count));
        DestroyTransient<MortarShellImpact>(); f.Runtime.State = WeaponState.Manual; Set(f.Weapon, "FireTimer", 0f);
        f.Weapon.TickManual(0f, Vector3.forward, true); f.Weapon.TickManual(0.2f, Vector3.forward, true);
        Assert.That(LaunchCount(f), Is.EqualTo(1));
    }

    [TestCase(1f, 0f, 1f, 1f)] [TestCase(2f, 0f, 1f, 2f)]
    [TestCase(1f, 3f, 1f, 0.25f)] [TestCase(1f, 3f, 0f, 1f)]
    public void MortarLegacyManualTravelAndHeatSpeedChangeActualFlightWithoutChangingAutoOrCadence(
        float travel, float bonus, float heat, float expectedDuration)
    {
        Fixture f = Create(WeaponType.Mortar, 1, WeaponUpgradePath.None); SetHeat(f, heat);
        f.Data.Mortar.MortarManualTravelTime = travel; f.Data.Mortar.MortarHeatManualSpeedBonus = bonus;
        f.Data.Mortar.MortarShellTravelTime = 0.7f; f.Row.Manual.AttackCyclesPerSecond = F(2f);
        Field<Transform>(f.Weapon, "Owner").position = Vector3.up * 4f;
        f.Weapon.TickManual(0f, Vector3.forward, true);
        MortarShellImpact manual = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        AdvanceMortarTo(manual, 0.1f);
        Assert.That(manual.transform.position.z, Is.EqualTo(f.Data.Mortar.MortarManualLevelRange * 0.1f / expectedDuration).Within(0.0001f));
        f.Weapon.TickManual(0.2f, Vector3.forward, true); Assert.That(LaunchCount(f), Is.EqualTo(1));
        DestroyTransient<MortarShellImpact>(); Set(f.Weapon, "FireTimer", 0f);
        Target(Vector3.up * 4f + Vector3.forward * 4f);
        f.Runtime.State = WeaponState.Automatic; f.Weapon.TickAutomatic(0f, Vector3.forward);
        MortarShellImpact automatic = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Single();
        AdvanceMortarTo(automatic, 0.1f);
        Assert.That(automatic.transform.position.z, Is.EqualTo(4f * 0.1f / 0.7f).Within(0.0001f));
    }

    private sealed class Fixture
    {
        public WeaponData Data; public WeaponStatLevelRow Row; public WeaponInstance Runtime;
        public BasicProjectileWeapon Weapon; public ProjectilePool Pool; public Transform Container; public Camera AimCamera;
    }

    private Fixture Create(WeaponType type, int level, WeaponUpgradePath path)
    {
        GameObject owner = Keep(new GameObject("Stat consumer owner"));
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType stat in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction,
            StatType.CloseRangeDamageMultiplier, StatType.LongRangeDamageMultiplier })
        {
            StatDefinition definition = Keep(ScriptableObject.CreateInstance<StatDefinition>());
            Set(definition, "<StatType>k__BackingField", stat);
            Set(definition, "<BaseValue>k__BackingField", stat == StatType.CriticalDamage ? 2f
                : stat == StatType.CriticalChance || stat == StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        Set(stats, "_statDefinitions", definitions); Call(stats, "Awake");
        WeaponData data = Keep(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type; data.WeaponId = "StatConsumer"; data.BaseDamage = 10f;
        data.BaseRange = 8f; data.BaseAttackRate = 1f; data.BaseManualAmmo = 1000f;
        data.ActiveAbilityAmmoCost = 20f; data.SkillCooldown = 3f; data.BaseKnockback = 0f;
        data.EnsureSpecificTuningForCurrentType();
        data.LevelData = new List<WeaponLevelData>();
        for (int i = 1; i <= 10; i++) data.LevelData.Add(new WeaponLevelData
            { Level = i, DamageMultiplier = 2f, AttackRateMultiplier = 3f, ManualAmmoMultiplier = 1f });
        data.PathA = new WeaponUpgradePathData { DamageMultiplier = 3f, AttackRateMultiplier = 4f };
        data.PathB = new WeaponUpgradePathData { DamageMultiplier = 3f, AttackRateMultiplier = 4f };
        if (type == WeaponType.AutomaticCannon)
        { data.AutomaticCannon.CannonManualBurstCount = 1; data.AutomaticCannon.CannonAutoBurstCount = 1;
          data.AutomaticCannon.CannonBurstProjectileScatterDegrees = 0f; }
        else
        { data.Mortar.MortarManualAccuracyRadius = 0f; data.Mortar.MortarAutoAccuracyRadius = 0f;
          data.Mortar.MortarExplosionFalloff = 0f; data.Mortar.MortarActiveDamageScale = 3f; }
        data.StatProfile = new WeaponStatProfile();
        WeaponStatLevelRow row = WeaponStatProfile.CreateRow(type, level, path); data.StatProfile.Levels.Add(row);
        WeaponInstance runtime = new() { Data = data, Level = level, SelectedPath = path, CurrentAmmo = 1000f, State = WeaponState.Manual };
        var f = new Fixture { Data = data, Row = row, Runtime = runtime };
        if (type == WeaponType.AutomaticCannon)
        {
            GameObject prefab = Keep(new GameObject("Consumer projectile prefab")); prefab.SetActive(false);
            prefab.AddComponent<Rigidbody>(); prefab.AddComponent<SphereCollider>().radius = 0.04f;
            Projectile projectile = prefab.AddComponent<Projectile>(); Call(projectile, "Awake");
            GameObject poolObject = Keep(new GameObject("Consumer projectile pool")); poolObject.SetActive(false);
            f.Container = Keep(new GameObject("Consumer projectile instances")).transform;
            f.Pool = poolObject.AddComponent<ProjectilePool>(); Set(f.Pool, "_projectilePrefab", prefab);
            Set(f.Pool, "_container", f.Container); Set(f.Pool, "_initialPoolSize", 1); Set(f.Pool, "_maxPoolSize", 2048);
            Call(f.Pool, "Awake");
            f.Weapon = new AutomaticCannonWeapon(new ConfiguredEnemyTargeting(), f.Pool, owner.transform);
        }
        else
        {
            f.AimCamera = Keep(new GameObject("Stat mortar aim camera")).AddComponent<Camera>();
            f.AimCamera.enabled = false;
            Set(owner.AddComponent<ReticleAimProvider>(), "_aimCamera", f.AimCamera);
            // Manual launch now requires terrain at the pitch-selected range. Keep this tile
            // away from the 0-8m collision and ability fixtures unless they request Floor().
            GameObject landing = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            landing.name = "Stat manual landing terrain";
            landing.transform.position = new Vector3(0f, -0.1f, data.Mortar.MortarManualLevelRange);
            landing.transform.localScale = new Vector3(20f, 0.2f, 12f);
            Physics.SyncTransforms();
            f.Weapon = new MortarWeapon(null, null, owner.transform);
        }
        f.Weapon.Setup(runtime, owner.transform, stats, null);
        return f;
    }

    private void EmitAndHit(Fixture f, WeaponStatMode mode, TestTarget target)
    {
        f.Runtime.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        if (f.Data.WeaponType == WeaponType.Mortar)
        {
            MortarShellImpact shell = EmitMortar(f, mode, target.transform.position);
            bool children = Field<MortarUpgradePayload>(shell, "_payload").UseGrapeshot;
            Call(shell, "Detonate", target.transform.position);
            if (children)
                foreach (MortarShellImpact child in Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None))
                    Call(child, "Detonate", target.transform.position);
            DestroyTransient<MortarShellImpact>();
        }
        else if (f.Runtime.SelectedPath == WeaponUpgradePath.PathB)
        {
            Call(f.Weapon, "FireHeadHunterPiercingLine", Vector3.forward, 1, 20f, false, mode == WeaponStatMode.Ability, null);
            Call(f.Weapon, "TickHeadHunterPendingImpacts", 10f);
        }
        else
        {
            WeaponFeedbackMode feedback = mode == WeaponStatMode.Ability ? WeaponFeedbackMode.Active
                : mode == WeaponStatMode.Manual ? WeaponFeedbackMode.Manual : WeaponFeedbackMode.Automatic;
            bool spawned = (bool)Call(f.Weapon, "TryFireCannonProjectile", Vector3.zero, Vector3.forward, 1f, false,
                WeaponPresentationCue.None, mode == WeaponStatMode.Ability, false,
                ProjectilePresentationArchetypeId.Default, 0, DamageFeedbackKind.Direct, feedback);
            Assert.That(spawned, Is.True);
            Projectile bullet = ActiveProjectiles(f).Last();
            Assert.That((bool)Call(bullet, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 5f), Is.True);
        }
    }

    private MortarShellImpact EmitMortar(Fixture f, WeaponStatMode mode, Vector3 target)
    {
        bool ability = mode == WeaponStatMode.Ability;
        WeaponFeedbackMode feedback = ability ? WeaponFeedbackMode.Active
            : mode == WeaponStatMode.Manual ? WeaponFeedbackMode.Manual : WeaponFeedbackMode.Automatic;
        Assert.That((bool)Call(f.Weapon, "FireShell", Vector3.zero, target, ability ? 3f : 1f,
            1f, 0f, 0.5f, 0f, false, feedback, ability, ability), Is.True);
        return Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Last();
    }
    private static void SetHeat(Fixture f, float normalized)
    {
        Transform owner = Field<Transform>(f.Weapon, "Owner");
        HeatManager heat = owner.gameObject.AddComponent<HeatManager>();
        Set(heat, "_currentHeat", normalized <= 0.8f ? normalized / 0.8f * 50f : 50f + (normalized - 0.8f) / 0.2f * 50f);
        f.Weapon.Setup(f.Runtime, owner, Field<PlayerStats>(f.Weapon, "Stats"), heat);
        Assert.That(heat.NormalizedHeat, Is.EqualTo(normalized).Within(0.00001f));
    }
    private TestTarget Target(Vector3 point)
    {
        GameObject go = Keep(new GameObject("Stat target")); go.transform.position = point;
        go.AddComponent<SphereCollider>().radius = 0.03f;
        TestTarget target = go.AddComponent<TestTarget>();
        EnemyRegistry.Register(go.transform); _registered.Add(go.transform); Physics.SyncTransforms(); return target;
    }
    private void Floor()
    {
        GameObject floor = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); floor.name = "Stat test terrain";
        floor.transform.position = new Vector3(0f, -0.1f, 8f); floor.transform.localScale = new Vector3(100f, 0.2f, 100f);
        Physics.SyncTransforms();
    }
    private static Projectile[] ActiveProjectiles(Fixture f) => f.Container.GetComponentsInChildren<Projectile>().Where(p => p.gameObject.activeSelf).ToArray();
    private static string Diagnostic(Fixture f, string section, string label) => f.Weapon.CaptureDiagnostics()
        .Sections.Single(s => s.Name == section).Values.Single(v => v.Label == label).Value;
    private static int LaunchCount(Fixture f) => f.Pool != null ? f.Pool.ActiveLeasedCount
        : Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None).Length;
    private static void AdvanceMortarTo(MortarShellImpact shell, float elapsed)
        => Call(shell, "Tick", elapsed - Field<float>(shell, "_elapsed"));
    private static WeaponOptionalFloat F(float value) => new() { Enabled = true, Value = value };
    private static WeaponOptionalInt I(int value) => new() { Enabled = true, Value = value };
    private T Keep<T>(T value) where T : Object { _objects.Add(value); return value; }
    private static void DestroyTransient<T>() where T : Component
    { foreach (T value in Object.FindObjectsByType<T>(FindObjectsSortMode.None)) if (value != null) Object.DestroyImmediate(value.gameObject); }
    private static FieldInfo FindField(Type type, string name)
    { while (type != null) { var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (field != null) return field; type = type.BaseType; } throw new MissingFieldException(name); }
    private static T Field<T>(object target, string name) => (T)FindField(target.GetType(), name).GetValue(target);
    private static void Set(object target, string name, object value) => FindField(target.GetType(), name).SetValue(target, value);
    private static object Call(object target, string name, params object[] arguments)
    {
        MethodInfo method = null;
        for (Type type = target.GetType(); type != null && method == null; type = type.BaseType)
            method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length >= arguments.Length
                    && m.GetParameters().Skip(arguments.Length).All(p => p.IsOptional));
        Assert.That(method, Is.Not.Null, name);
        object[] complete = new object[method.GetParameters().Length];
        Array.Copy(arguments, complete, arguments.Length);
        for (int i = arguments.Length; i < complete.Length; i++) complete[i] = Type.Missing;
        return method.Invoke(target, complete);
    }
    private sealed class TestTarget : MonoBehaviour, IDamageable
    { public int TotalDamage; public bool ApplyDamage(int amount) { TotalDamage += amount; return true; } }
}
