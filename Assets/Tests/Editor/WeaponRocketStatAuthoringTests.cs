using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WeaponRocketStatAuthoringTests
{
    private readonly List<Object> _cleanup = new();
    private readonly List<Transform> _targets = new();
    private WeaponInstance _instance;
    private WeaponStatLevelRow _row;
    private RocketLauncherWeapon _weapon;
    private ProjectilePool _pool;
    private Transform _container;
    private Transform _muzzle;
    private PlayerStats _stats;
    private UnityEngine.Random.State _randomState;

    [SetUp]
    public void SetUp()
    {
        _randomState = UnityEngine.Random.state;
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Rocket stat tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
    }

    [TearDown]
    public void TearDown()
    {
        _weapon?.CancelActiveAbility();
        foreach (Transform target in _targets) if (target != null) EnemyRegistry.Unregister(target);
        _targets.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--) if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        UnityEngine.Random.state = _randomState;
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
    public void R01_R09_ModeDamageChangesActualMainExplosionHealthWithoutCrossModeLeak(WeaponStatMode mode, WeaponUpgradePath path)
    {
        Create(path);
        if (path == WeaponUpgradePath.PathA) _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        WeaponStatModeValues values = Values(mode);
        foreach (float damage in new[] { 13f, 29f })
        {
            values.Damage = Set(damage);
            Projectile projectile = Fire(mode);
            Assert.That(projectile, Is.Not.Null);
            int before = target.CurrentHealth;
            Explode(projectile, target.transform.position);
            Assert.That(before - target.CurrentHealth, Is.EqualTo(damage));
            ReleaseAll();
        }
        WeaponStatMode other = mode == WeaponStatMode.Manual ? WeaponStatMode.Automatic : WeaponStatMode.Manual;
        Assert.That(Values(other).Damage.Enabled, Is.False);
        Projectile inherited = Fire(other);
        int previous = target.CurrentHealth;
        Explode(inherited, target.transform.position);
        Assert.That(previous - target.CurrentHealth, Is.EqualTo(10), "Other mode still uses its independent inherited base.");
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void R03_R05_R08_RadiusMovesActualBlastBoundaryAndIsCapturedAtLaunch(WeaponStatMode mode)
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        var inside = Target(_muzzle.position + Vector3.right * 1.8f);
        var outside = Target(_muzzle.position + Vector3.right * 2.2f);
        Values(mode).ExplosionRadiusMeters = Set(2f);
        Projectile shot = Fire(mode);
        Values(mode).ExplosionRadiusMeters = Set(9f);
        Explode(shot, _muzzle.position);
        Assert.That(inside.CurrentHealth, Is.EqualTo(9990));
        Assert.That(outside.CurrentHealth, Is.EqualTo(10000));
        ReleaseAll();
        shot = Fire(mode);
        Explode(shot, _muzzle.position);
        Assert.That(outside.CurrentHealth, Is.LessThan(10000));
    }

    [Test]
    public void R02_AcquisitionBoundaryAndExplicitArcTravelHaveIndependentRealConsumers()
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        _row.Automatic.RangeMeters = Set(3f);
        Assert.That(Fire(WeaponStatMode.Automatic), Is.Null);
        _row.Automatic.RangeMeters = Set(5f);
        _row.Rocket.AutomaticProjectileTravelRangeMeters = Set(1.25f);
        Projectile shot = Fire(WeaponStatMode.Automatic);
        Assert.That(shot, Is.Not.Null);
        Assert.That(shot.UsesAutomaticRocketTrajectory, Is.True);
        Assert.That(Read<float>(shot, "_maxTravelDistance"), Is.EqualTo(1.25f));
        Field(shot, "_replaceExplosionVfx", true);
        Call(shot, "FixedUpdate");
        for (int i = 0; i < 30 && shot.gameObject.activeSelf; i++) Call(shot, "FixedUpdate");
        Assert.That(shot.gameObject.activeSelf, Is.False, "The actual flight update detonates at authored arc distance.");
        Assert.That(_row.Manual.RangeMeters.Enabled, Is.False);
    }

    [TestCase(.005f)]
    [TestCase(2f)]
    [TestCase(7f)]
    public void R04_ManualTravelLimitReachesProjectileDetonationWithoutChangingAcquisition(float range)
    {
        Create();
        _row.Manual.RangeMeters = Set(range);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Assert.That(Read<float>(shot, "_maxTravelDistance"), Is.EqualTo(range));
        Field(shot, "_replaceExplosionVfx", true);
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(99f), "A tiny valid range still launches before consuming its one shot.");
        AdvanceFlight(shot, 100);
        Assert.That(shot.gameObject.activeSelf, Is.False);
        Assert.That(Vector3.Distance(shot.transform.position, _muzzle.position + Vector3.forward * range), Is.LessThan(.003f));
        Assert.That(_row.Automatic.RangeMeters.Enabled, Is.False);
    }

    [TestCase(WeaponStatMode.Automatic, 2f)]
    [TestCase(WeaponStatMode.Automatic, .02f)]
    [TestCase(WeaponStatMode.Automatic, 4f)]
    [TestCase(WeaponStatMode.Manual, 2f)]
    [TestCase(WeaponStatMode.Manual, .02f)]
    [TestCase(WeaponStatMode.Manual, 4f)]
    public void R07_CycleRateChangesRealLaunchScheduleNotVolleyCount(WeaponStatMode mode, float cycles)
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        Values(mode).AttackCyclesPerSecond = Set(cycles);
        _instance.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        Tick(mode, 0f);
        Assert.That(_pool.ActiveLeasedCount, Is.EqualTo(1));
        Tick(mode, 1f / cycles - .001f);
        Assert.That(_pool.ActiveLeasedCount, Is.EqualTo(1));
        Tick(mode, .002f);
        Assert.That(_pool.ActiveLeasedCount, Is.EqualTo(2));
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(mode == WeaponStatMode.Manual ? 98f : 100f));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(2f)]
    public void R13_ManualStrengthReachesActualImpulseAndKeepsDamageDependentFormula(float strength)
    {
        Create();
        _row.Manual.Damage = Set(25f);
        _row.Manual.KnockbackStrength = Set(strength);
        var target = Target(_muzzle.position + Vector3.right * .2f);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Explode(shot, _muzzle.position);
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        float distance = receiver != null ? receiver.ConsumeDisplacement(.1f).magnitude : 0f;
        Assert.That(distance, Is.EqualTo(strength * (1f + 5f * .25f) * .1f).Within(.0001f));
        Assert.That(target.CurrentHealth, Is.EqualTo(9975));
    }

    [TestCase(2f, 1.5f)]
    [TestCase(7f, 2f)]
    public void R14_R15_KineticStatusChangesSubsequentHealthLossAndExpiresOnRealStatusClock(float duration, float multiplier)
    {
        Create(WeaponUpgradePath.PathA);
        _row.Rocket.KineticDebuffDurationSeconds = Set(duration);
        _row.Rocket.KineticDamageTakenMultiplier = Set(multiplier);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Explode(shot, target.transform.position);
        var status = target.GetComponent<WeaponDamageAmplifierStatus>();
        Assert.That(status, Is.Not.Null);
        int before = target.CurrentHealth;
        WeaponDamageApplier.ApplyDamage(target, 10);
        Assert.That(before - target.CurrentHealth, Is.EqualTo(10 * multiplier));
        Assert.That(Call<bool>(status, "AdvanceLifetime", duration - .01f), Is.False);
        Assert.That(status.Multiplier, Is.EqualTo(multiplier));
        Assert.That(Call<bool>(status, "AdvanceLifetime", .02f), Is.True);
        before = target.CurrentHealth;
        WeaponDamageApplier.ApplyDamage(target, 10);
        Assert.That(before - target.CurrentHealth, Is.EqualTo(10));
    }

    [TestCase(0f, 2f)]
    [TestCase(3f, 1f)]
    public void R14_R15_NeutralStatusControlsDisableApplication(float duration, float multiplier)
    {
        Create(WeaponUpgradePath.PathA);
        _row.Rocket.KineticDebuffDurationSeconds = Set(duration);
        _row.Rocket.KineticDamageTakenMultiplier = Set(multiplier);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        Explode(Fire(WeaponStatMode.Manual), target.transform.position);
        Assert.That(target.GetComponent<WeaponDamageAmplifierStatus>(), Is.Null);
        Assert.That(target.CurrentHealth, Is.EqualTo(9990));
    }

    [TestCase(2f, 45f, 1.8f, 10f, true)]
    [TestCase(2f, 45f, 2.2f, 10f, false)]
    [TestCase(4f, 45f, 3f, 10f, true)]
    [TestCase(4f, 20f, 3f, 15f, false)]
    [TestCase(4f, 60f, 3f, 25f, true)]
    [TestCase(0f, 60f, 3f, 25f, false)]
    [TestCase(4f, 0f, 3f, 0f, false)]
    public void R16_FragmentRangeAndAngleControlActualConeHits(float multiplier, float angle, float distance, float targetAngle, bool hit)
    {
        Create(WeaponUpgradePath.PathB);
        _row.Manual.ExplosionRadiusMeters = Set(1f);
        _row.Rocket.FragmentConeRangePerExplosionRadius = Set(multiplier);
        _row.Rocket.FragmentConeAngleDegrees = Set(angle);
        Vector3 offset = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * distance;
        var target = Target(_muzzle.position + offset);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Explode(shot, _muzzle.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(hit ? 9990 : 10000));
    }

    [Test]
    public void QClusterCapturesIndependentChildGeometryDamageAndPathAcrossSwitchAndPoolReuse()
    {
        Create(WeaponUpgradePath.PathB);
        _row.Ability.Damage = Set(40f);
        _row.Ability.ExplosionRadiusMeters = Set(3f);
        _row.Manual.Damage = Set(90f);
        _row.Manual.ExplosionRadiusMeters = Set(9f);
        _row.Rocket.AbilityClusterExplosionRadiusMeters = Set(.75f);
        _row.Rocket.AbilityClusterTravelRangeMeters = Set(2.5f);
        _row.Rocket.FragmentConeRangePerExplosionRadius = Set(6f);
        _row.Rocket.FragmentConeAngleDegrees = Set(30f);
        Projectile parent = Fire(WeaponStatMode.Ability);
        Assert.That(Read<bool>(parent, "_useFragmentCone"), Is.False);
        _instance.Level = 5;
        _instance.SelectedPath = WeaponUpgradePath.None;
        _instance.State = WeaponState.Automatic;
        _row.Ability.Damage = Set(400f);
        Explode(parent, _muzzle.position + Vector3.forward * 5f);
        var children = Active().Where(p => p != parent).ToArray();
        Assert.That(children.Length, Is.EqualTo(20));
        Projectile child = children[0];
        Assert.That(Read<float>(child, "_explosionRadius"), Is.EqualTo(.75f));
        Assert.That(Read<float>(child, "_maxTravelDistance"), Is.EqualTo(2.5f));
        Assert.That(Read<float>(child, "_fragmentConeRange"), Is.EqualTo(4.5f));
        Assert.That(Read<float>(child, "_fragmentConeAngle"), Is.EqualTo(30f));
        var target = Target(_muzzle.position + Vector3.back * 5f);
        Explode(child, target.transform.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(9980), "Child retains Q40 x existing cluster0.5, independent of manual90 and laterQ400.");
        ReleaseAll();
        _instance.State = WeaponState.Manual;
        Projectile plain = Fire(WeaponStatMode.Manual);
        Assert.That(Read<bool>(plain, "_useFragmentCone"), Is.False);
        Assert.That(Read<bool>(plain, "_useExplosionCluster"), Is.False);
        Assert.That(Read<bool>(plain, "_applyDamageAmplifierOnExplosion"), Is.False);
    }

    [Test]
    public void FragmentFeedbackCopiesRetainCapturedGeometryPathLevelAndMode()
    {
        Create(WeaponUpgradePath.PathB);
        var context = new WeaponFeedbackContext(_instance, WeaponFeedbackMode.Active, 0f, Vector3.zero,
            Vector3.forward, fragmentConeRange: 6f, fragmentConeAngle: 70f);
        _instance.Level = 5; _instance.SelectedPath = WeaponUpgradePath.None; _instance.State = WeaponState.Automatic;
        context = context.WithExplosionRadius(2f).WithIntensity(2f).WithDirection(Vector3.right)
            .WithDamageMetadata(40f, 1, DamageFeedbackKind.Fragment)
            .WithImpact(Vector3.one, Vector3.up, 10, false, false, false, null, WeaponEnemyKind.Normal, ImpactSurfaceType.Default);
        Assert.That(context.UpgradePath, Is.EqualTo(WeaponUpgradePath.PathB));
        Assert.That(context.WeaponLevel, Is.EqualTo(6));
        Assert.That(context.Mode, Is.EqualTo(WeaponFeedbackMode.Active));
        var presentation = WeaponPresentationContext.FromFeedback(WeaponPresentationCue.RocketFragmentImpact,
            in context, null, null, GameFeelQualityLevel.High, false);
        Assert.That(presentation.FragmentConeRange, Is.EqualTo(6f));
        Assert.That(presentation.FragmentConeAngle, Is.EqualTo(70f));
        Assert.That(presentation.HasFragmentGeometry, Is.True);
    }

    [TestCase(WeaponStatMode.Automatic, 0f)]
    [TestCase(WeaponStatMode.Manual, 0f)]
    [TestCase(WeaponStatMode.Ability, 0f)]
    [TestCase(WeaponStatMode.Automatic, 1f)]
    [TestCase(WeaponStatMode.Manual, 1f)]
    [TestCase(WeaponStatMode.Ability, 1f)]
    public void R11_R12_CritAndPlayerBonusesReachHealthOnceAndRemainCaptured(WeaponStatMode mode, float chance)
    {
        Create();
        PlayerValue(StatType.DamageMultiplier, 2f);
        PlayerValue(StatType.CriticalChance, 1f);
        PlayerValue(StatType.CriticalDamage, 2f);
        PlayerValue(StatType.AbilityDamageMultiplier, 4f);
        _instance.Data.RocketLauncher.RocketActiveDamageScale = 3f;
        _row.CriticalChance = Set(chance);
        _row.CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer;
        _row.CriticalDamageMultiplier = Set(3f);
        Values(mode).Damage = Set(13f);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        Projectile shot = Fire(mode);
        _row.CriticalChance = Set(1f - chance);
        _row.CriticalDamageMultiplier = Set(30f);
        Values(mode).Damage = Set(130f);
        Explode(shot, target.transform.position);
        int expected = 13 * 2 * (mode == WeaponStatMode.Ability ? 4 : 1) * (chance > 0f ? 6 : 1);
        Assert.That(10000 - target.CurrentHealth, Is.EqualTo(expected));
    }

    [TestCase(0f)]
    [TestCase(2f)]
    [TestCase(5f)]
    public void R06_AuthoredCapacityDrivesRealRefillAndExhaustionWithoutChangingQCost(float capacity)
    {
        Create();
        _row.Manual.AmmoCapacity = Set(capacity);
        var managerObject = Track(new GameObject("Rocket refill manager")); managerObject.SetActive(false);
        var manager = managerObject.AddComponent<WeaponManager>();
        Field(manager, "_stats", _stats);
        Read<List<IWeaponBehaviour>>(manager, "_equipped").Add(_weapon);
        manager.RefillManualAmmoAndResetActiveCooldown();
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(capacity));
        for (int i = 0; i <= capacity; i++) _weapon.TickManual(2f, Vector3.forward, true);
        Assert.That(_pool.ActiveLeasedCount, Is.EqualTo((int)capacity));
        Assert.That(_instance.CurrentAmmo, Is.Zero);
        Assert.That(_instance.Data.ActiveAbilityAmmoCost, Is.EqualTo(10f));
        manager.RefillManualAmmoAndResetActiveCooldown();
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(capacity));
    }

    [TestCase(0f)]
    [TestCase(8f)]
    [TestCase(10000f)]
    public void R10_QHoldCancelCommitAndTimerUseAuthoredCooldown(float cooldown)
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        _row.AbilityCooldownSeconds = Set(cooldown);
        PlayerValue(StatType.AbilityCooldownReduction, .5f);
        _weapon.BeginActiveAbility(Vector3.forward);
        Assert.That(_weapon.IsActiveAbilityCharging, Is.True);
        Assert.That(_instance.AbilityCooldownTimer, Is.Zero);
        _weapon.CancelActiveAbility();
        Assert.That(_instance.AbilityCooldownTimer, Is.Zero);
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(100f));
        _weapon.UseActiveAbility(Vector3.forward);
        Assert.That(_instance.AbilityCooldownTimer, Is.EqualTo(cooldown * .5f));
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(90f));
        if (cooldown > 0f)
        {
            _weapon.BeginActiveAbility(Vector3.forward);
            Assert.That(_weapon.IsActiveAbilityCharging, Is.False);
            typeof(WeaponManager).GetMethod("TickAbilityCooldown", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { _instance, cooldown * .5f });
            _weapon.BeginActiveAbility(Vector3.forward);
            Assert.That(_weapon.IsActiveAbilityCharging, Is.True);
            _weapon.CancelActiveAbility();
        }
    }

    [Test]
    public void ExactA7RowChangesActualHealthOnlyAtItsOwnKeyIncludingFiveToSixTransitions()
    {
        Create(WeaponUpgradePath.PathA);
        _instance.Data.StatProfile.Levels.Clear();
        var row = WeaponStatProfile.CreateRow(WeaponType.RocketLauncher, 7, WeaponUpgradePath.PathA);
        row.Manual.Damage = Set(29f);
        row.Rocket.KineticDamageTakenMultiplier = Set(1f);
        _instance.Data.StatProfile.Levels.Add(row);
        foreach (var key in new[] { (5, WeaponUpgradePath.None, 10), (6, WeaponUpgradePath.PathA, 12),
            (7, WeaponUpgradePath.PathA, 29), (8, WeaponUpgradePath.PathA, 12),
            (7, WeaponUpgradePath.PathB, 10), (6, WeaponUpgradePath.PathB, 10) })
        {
            _instance.Level = key.Item1; _instance.SelectedPath = key.Item2;
            var target = Target(_muzzle.position + Vector3.forward * 4f);
            Explode(Fire(WeaponStatMode.Manual), target.transform.position);
            Assert.That(10000 - target.CurrentHealth, Is.EqualTo(key.Item3), $"{key.Item1}/{key.Item2}");
            target.gameObject.SetActive(false); ReleaseAll();
        }
    }

    [TestCase("AutomaticProjectileTravelRangeMeters", 0f)]
    [TestCase("AbilityProjectileTravelRangeMeters", 0f)]
    [TestCase("AbilityClusterExplosionRadiusMeters", 0f)]
    [TestCase("AbilityClusterTravelRangeMeters", .01f)]
    [TestCase("KineticDamageTakenMultiplier", .5f)]
    [TestCase("FragmentConeAngleDegrees", .5f)]
    [TestCase("FragmentConeAngleDegrees", 181f)]
    public void InvalidRocketBoundaryFailsClearlyWithoutHiddenClamping(string field, float value)
    {
        var payload = new WeaponRocketStatOverrides();
        typeof(WeaponRocketStatOverrides).GetField(field).SetValue(payload, Set(value));
        Assert.That(payload.TryValidate(out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void FragmentVfxUsesCapturedConeAndRestoresItsInheritedMeshAfterReuse()
    {
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset");
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.RocketFragmentImpact, out var cue), Is.True);
        var instance = Track(Object.Instantiate(cue.VfxPrefab));
        var vfx = instance.GetComponent<RocketLauncherCueVfx>();
        Transform root = instance.transform.Find("Forward Mini Explosions");
        var filter = root.GetComponentInChildren<MeshFilter>(true);
        Mesh original = filter.sharedMesh;
        var context = new WeaponPresentationContext(cue.Cue, null, Vector3.zero, Vector3.forward,
            explosionRadius: 2f, fragmentConeRange: 7f, fragmentConeAngle: 70f);
        vfx.ApplyContext(in context);
        Assert.That(root.localScale, Is.EqualTo(Vector3.one * 7f));
        Assert.That(filter.sharedMesh, Is.Not.SameAs(original));
        context = new WeaponPresentationContext(cue.Cue, null, Vector3.zero, Vector3.forward,
            explosionRadius: 2f, fragmentConeRange: 0f, fragmentConeAngle: 0f);
        vfx.ApplyContext(in context);
        Assert.That(root.gameObject.activeSelf, Is.False);
        context = new WeaponPresentationContext(cue.Cue, null, Vector3.zero, Vector3.forward, explosionRadius: 2f);
        vfx.ApplyContext(in context);
        Assert.That(root.gameObject.activeSelf, Is.True);
        Assert.That(root.localScale, Is.EqualTo(Vector3.one * 8f));
        Assert.That(filter.sharedMesh, Is.SameAs(original));
    }

    [TestCase(WeaponStatMode.Automatic)]
    [TestCase(WeaponStatMode.Manual)]
    [TestCase(WeaponStatMode.Ability)]
    public void AuthoredLifetimeExpiresOnActualClockAndRemainsIndependentOfRangeAndOtherModes(WeaponStatMode mode)
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        _row.Manual.RangeMeters = Set(100f);
        _row.Rocket.AutomaticProjectileTravelRangeMeters = Set(100f);
        _row.Rocket.AbilityProjectileTravelRangeMeters = Set(100f);
        string lifetimeField = mode + "ProjectileLifetimeSeconds";
        typeof(WeaponRocketStatOverrides).GetField(lifetimeField).SetValue(_row.Rocket, Set(.1f));
        Projectile shot = Fire(mode);
        Assert.That(shot.ActiveLifetimeSeconds, Is.EqualTo(.1f));
        Field(shot, "_replaceExplosionVfx", true);
        float scale = Mathf.Max(.05f, DebugSpeedTool.ProjectileScale);
        Call(shot, "AdvanceLifetime", .09f / scale);
        Assert.That(shot.gameObject.activeSelf, Is.True);
        Call(shot, "AdvanceLifetime", .02f / scale);
        Assert.That(shot.gameObject.activeSelf, Is.False);
        typeof(WeaponRocketStatOverrides).GetField(lifetimeField).SetValue(_row.Rocket, default(WeaponOptionalFloat));
        Projectile reused = Fire(WeaponStatMode.Manual);
        Assert.That(reused.ActiveLifetimeSeconds, Is.EqualTo(_pool.ProjectileLifetime));
        Assert.That(Read<float>(reused, "_maxTravelDistance"), Is.EqualTo(100f), "Range does not silently extend the pool lifetime.");
    }

    [Test]
    public void LongerIndependentLifetimeLetsRealManualFlightReachItsAuthoredMaximum()
    {
        Create();
        _row.Manual.RangeMeters = Set(7f);
        _row.Rocket.ManualProjectileLifetimeSeconds = Set(10f);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Field(shot, "_replaceExplosionVfx", true);
        AdvanceFlight(shot, 200, advanceLifetime: true);
        Assert.That(shot.gameObject.activeSelf, Is.False);
        Assert.That(Vector3.Distance(shot.transform.position, _muzzle.position + Vector3.forward * 7f), Is.LessThan(.003f));
    }

    [TestCase(WeaponUpgradePath.None, 0f)]
    [TestCase(WeaponUpgradePath.None, 7f)]
    [TestCase(WeaponUpgradePath.None, 33f)]
    [TestCase(WeaponUpgradePath.PathB, 0f)]
    [TestCase(WeaponUpgradePath.PathB, 7f)]
    public void AuthoredQCostChangesRealSpendAndFreeQWorksWithEmptyAmmo(WeaponUpgradePath path, float cost)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        _row.AbilityAmmoCost = Set(cost);
        _instance.CurrentAmmo = cost == 0f ? 0f : 100f;
        float before = _instance.CurrentAmmo;
        Assert.That(Fire(WeaponStatMode.Ability), Is.Not.Null);
        Assert.That(_instance.CurrentAmmo, Is.EqualTo(before - cost));
        Assert.That(_row.Manual.AmmoCapacity.Enabled, Is.False);
        Assert.That(_instance.Data.BaseManualAmmo, Is.EqualTo(100f));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void FragmentAdditionalRocketCountChangesActualVolleyAndNeverBasic(int extra)
    {
        Create(WeaponUpgradePath.PathB);
        _instance.Data.RocketLauncher.RocketAutoBurstsPerSecond = .02f;
        Target(_muzzle.position + Vector3.forward * 4f);
        _row.Automatic.AttackCyclesPerSecond = Set(.02f);
        _row.Rocket.FragmentAdditionalAutomaticRockets = SetInt(extra);
        Fire(WeaponStatMode.Automatic);
        Tick(WeaponStatMode.Automatic, 1f);
        Assert.That(Active().Length, Is.EqualTo(1 + extra));
        ReleaseAll();
        _instance.Level = 5; _instance.SelectedPath = WeaponUpgradePath.None;
        Fire(WeaponStatMode.Automatic);
        Tick(WeaponStatMode.Automatic, .5f);
        Assert.That(Active().Length, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void ClusterCountDamageSpeedFalloffLifetimeAndFlightAreCapturedBeforeSwitch(int count)
    {
        Create(WeaponUpgradePath.PathB);
        _row.Ability.Damage = Set(40f);
        _row.Rocket.AbilityClusterRocketCount = SetInt(count);
        _row.Rocket.AbilityClusterDamageMultiplier = Set(.25f);
        _row.Rocket.AbilityClusterSpeedMultiplier = Set(2f);
        _row.Rocket.AbilityClusterFalloff = Set(1f);
        _row.Rocket.FragmentDamageMultiplier = Set(0f);
        _row.Rocket.AbilityClusterLifetimeSeconds = Set(.1f);
        _row.Rocket.AbilityClusterExplosionRadiusMeters = Set(2f);
        _row.Rocket.AbilityClusterTravelRangeMeters = Set(.1f);
        Projectile parent = Fire(WeaponStatMode.Ability);
        _instance.SelectedPath = WeaponUpgradePath.PathA;
        _row.Rocket.AbilityClusterRocketCount = SetInt(8);
        Explode(parent, _muzzle.position);
        Projectile[] children = Active().Where(p => p != parent).ToArray();
        Assert.That(children.Length, Is.EqualTo(count));
        if (count == 0) return;
        Projectile child = children[0];
        Assert.That(child.ActiveSpeed, Is.EqualTo(child.AuthoredSpeed * 2f * DebugSpeedTool.ProjectileScale).Within(.001f));
        Assert.That(child.ActiveLifetimeSeconds, Is.EqualTo(.1f));
        Assert.That(Read<bool>(child, "_clampTravelEndpoint"), Is.True);
        var target = Target(_muzzle.position + Vector3.right);
        Explode(child, _muzzle.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(9995), "Q40 x child0.25 x half-radius falloff0.5.");
        Field(child, "_replaceExplosionVfx", true);
        Call(child, "AdvanceLifetime", .11f / Mathf.Max(.05f, DebugSpeedTool.ProjectileScale));
        Assert.That(child.gameObject.activeSelf, Is.False);
    }

    [TestCase(WeaponStatMode.Manual, 0f)]
    [TestCase(WeaponStatMode.Manual, 2f)]
    [TestCase(WeaponStatMode.Ability, 0f)]
    [TestCase(WeaponStatMode.Ability, 2f)]
    public void FragmentDamageMultiplierChangesOnlyRealShrapnelDamage(WeaponStatMode mode, float multiplier)
    {
        Create(WeaponUpgradePath.PathB);
        _row.Manual.ExplosionRadiusMeters = Set(1f);
        _row.Rocket.AbilityClusterExplosionRadiusMeters = Set(1f);
        _row.Rocket.AbilityClusterRocketCount = SetInt(1);
        _row.Rocket.FragmentDamageMultiplier = Set(multiplier);
        Projectile shot = Fire(mode);
        if (mode == WeaponStatMode.Ability)
        {
            Projectile parent = shot; Explode(parent, _muzzle.position);
            shot = Active().Single(p => p != parent);
        }
        Vector3 direction = Read<Vector3>(shot, "_direction");
        var fragment = Target(_muzzle.position + direction * 2f);
        var blast = Target(_muzzle.position + Vector3.Cross(Vector3.up, direction) * .2f);
        Explode(shot, _muzzle.position);
        int mainDamage = mode == WeaponStatMode.Ability ? 5 : 10;
        Assert.That(fragment.CurrentHealth, Is.EqualTo(10000 - mainDamage * multiplier));
        Assert.That(blast.CurrentHealth, Is.EqualTo(10000 - mainDamage));
    }

    [TestCase(WeaponUpgradePath.PathA, .5f, false)]
    [TestCase(WeaponUpgradePath.PathA, 2f, true)]
    [TestCase(WeaponUpgradePath.PathB, .5f, false)]
    [TestCase(WeaponUpgradePath.PathB, 2f, true)]
    public void PathRadiusMultiplierMovesActualInheritedBlastButCannotMultiplyAuthoredReplacement(WeaponUpgradePath path, float multiplier, bool hit)
    {
        Create(path);
        _instance.Data.RocketLauncher.RocketManualExplosionRadius = 2f;
        if (path == WeaponUpgradePath.PathA)
        {
            _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
            _row.Rocket.KineticExplosionRadiusMultiplier = Set(multiplier);
        }
        else
        {
            _row.Rocket.FragmentDamageMultiplier = Set(0f);
            _row.Rocket.FragmentOrdinaryExplosionRadiusMultiplier = Set(multiplier);
        }
        var target = Target(_muzzle.position + Vector3.right * 1.5f);
        Explode(Fire(WeaponStatMode.Manual), _muzzle.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(hit ? 9990 : 10000));
        ReleaseAll();
        _row.Manual.ExplosionRadiusMeters = Set(1f);
        int before = target.CurrentHealth;
        Explode(Fire(WeaponStatMode.Manual), _muzzle.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(before));
    }

    [TestCase(0f, 100)]
    [TestCase(.5f, 80)]
    [TestCase(1.5f, 50)]
    public void KineticFalloffMultiplierChangesRealEdgeDamageWithExistingFinalClamp(float multiplier, int expected)
    {
        Create(WeaponUpgradePath.PathA);
        _row.Manual.Damage = Set(100f); _row.Manual.ExplosionRadiusMeters = Set(2f);
        _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
        _row.Rocket.KineticExplosionFalloffMultiplier = Set(multiplier);
        _instance.Data.RocketLauncher.RocketManualExplosionFalloff = .8f;
        var target = Target(_muzzle.position + Vector3.right);
        Explode(Fire(WeaponStatMode.Manual), _muzzle.position);
        Assert.That(10000 - target.CurrentHealth, Is.EqualTo(expected));
    }

    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Manual, 0f)]
    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Manual, 2f)]
    [TestCase(WeaponUpgradePath.PathA, WeaponStatMode.Ability, 2f)]
    [TestCase(WeaponUpgradePath.PathB, WeaponStatMode.Manual, 2f)]
    [TestCase(WeaponUpgradePath.PathB, WeaponStatMode.Ability, 2f)]
    public void PathKnockbackMultiplierChangesActualImpulseInItsMode(WeaponUpgradePath path, WeaponStatMode mode, float multiplier)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        if (path == WeaponUpgradePath.PathA)
        {
            _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
            _row.Rocket.KineticOrdinaryKnockbackMultiplier = Set(mode == WeaponStatMode.Manual ? multiplier : 99f);
            _row.Rocket.KineticAbilityKnockbackMultiplier = Set(mode == WeaponStatMode.Ability ? multiplier : 99f);
        }
        else _row.Rocket.FragmentKnockbackMultiplier = Set(multiplier);
        var target = Target(_muzzle.position + Vector3.right * .2f);
        Explode(Fire(mode), _muzzle.position);
        var receiver = target.GetComponent<EnemyKnockbackReceiver>();
        float distance = receiver != null ? receiver.ConsumeDisplacement(.1f).magnitude : 0f;
        Assert.That(distance, Is.EqualTo(multiplier * (1f + Mathf.Sqrt(10f) * .25f) * .1f).Within(.0001f));
    }

    [Test]
    public void AuthoredChildRadiusHonorsPlayerAreaBelowLegacyClusterFloor()
    {
        Create(WeaponUpgradePath.PathB);
        _row.Rocket.AbilityClusterRocketCount = SetInt(1);
        _row.Rocket.AbilityClusterExplosionRadiusMeters = Set(.05f);
        PlayerValue(StatType.ProjectileAreaSize, .02f);
        Projectile parent = Fire(WeaponStatMode.Ability);
        Explode(parent, _muzzle.position);
        Projectile child = Active().Single(p => p != parent);
        Assert.That(Read<float>(child, "_explosionRadius"), Is.EqualTo(.001f).Within(.000001f));
        ReleaseAll();
        _row.Rocket.AbilityClusterExplosionRadiusMeters = default;
        _instance.Data.RocketLauncher.RocketManualExplosionRadius = .05f;
        parent = Fire(WeaponStatMode.Ability); Explode(parent, _muzzle.position);
        child = Active().Single(p => p != parent);
        Assert.That(Read<float>(child, "_explosionRadius"), Is.EqualTo(.05f), "Disabled authoring retains existing post-area child clamp.");
    }

    [TestCase("AutomaticProjectileLifetimeSeconds", 0f)]
    [TestCase("ManualProjectileLifetimeSeconds", 121f)]
    [TestCase("AbilityProjectileLifetimeSeconds", float.PositiveInfinity)]
    [TestCase("AbilityClusterLifetimeSeconds", .01f)]
    [TestCase("AbilityClusterSpeedMultiplier", 0f)]
    [TestCase("AbilityClusterDamageMultiplier", 0f)]
    [TestCase("AbilityClusterFalloff", 1.01f)]
    public void AdditionalFloatBoundariesRejectClearly(string field, float value) => InvalidRocketBoundaryFailsClearlyWithoutHiddenClamping(field, value);

    [TestCase(0, true)]
    [TestCase(1024, true)]
    [TestCase(1025, false)]
    [TestCase(-1, false)]
    public void CountBoundsAreValidatedWithoutAllocatingTheirProjectiles(int count, bool valid)
    {
        var payload = new WeaponRocketStatOverrides
        { FragmentAdditionalAutomaticRockets = SetInt(count), AbilityClusterRocketCount = SetInt(count) };
        Assert.That(payload.TryValidate(out _), Is.EqualTo(valid));
    }

    [TestCase(1f)]
    [TestCase(70f)]
    [TestCase(180f)]
    public void AuthoredFragmentVfxVerticesStayInsideCapturedConeThroughoutPopAnimation(float angle)
    {
        var profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset");
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.RocketFragmentImpact, out var cue), Is.True);
        var instance = Track(Object.Instantiate(cue.VfxPrefab));
        var vfx = instance.GetComponent<RocketLauncherCueVfx>();
        Transform root = instance.transform.Find("Forward Mini Explosions");
        var context = new WeaponPresentationContext(cue.Cue, null, Vector3.zero, Vector3.forward,
            explosionRadius: 2f, fragmentConeRange: 7f, fragmentConeAngle: angle);
        vfx.ApplyContext(in context);
        bool hasVisibleWidth = false;
        foreach (float frame in new[] { .1f, .25f, .4f, .6f, .8f })
        {
            Call(vfx, "ApplyFrame", frame);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Vector3 first = default; bool started = false;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    float radial = new Vector2(p.x, p.z).magnitude;
                    Assert.That(radial, Is.LessThanOrEqualTo(1.0001f), "The root's exact 7m scale owns the range.");
                    if (radial > .00001f)
                        Assert.That(Mathf.Abs(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg), Is.LessThanOrEqualTo(angle * .5f + .02f));
                    if (started && Vector3.Distance(p, first) > .0001f) hasVisibleWidth = true;
                    if (!started) { first = p; started = true; }
                }
            }
        }
        Assert.That(hasVisibleWidth, Is.True, "Caching while OnEnable scales minis to zero must not collapse their meshes.");
    }

    [Test]
    public void GeometryAndManualAimReadTheSameModeRangeAndPlayerScaledBlastAsActualShot()
    {
        Create(WeaponUpgradePath.PathB);
        Assert.That(_weapon.GetManualAimConvergenceDistance(), Is.Null);
        _row.Manual.RangeMeters = Set(7f);
        _row.Manual.ExplosionRadiusMeters = Set(2f);
        _row.Rocket.FragmentConeRangePerExplosionRadius = Set(3f);
        _row.Rocket.FragmentConeAngleDegrees = Set(60f);
        PlayerValue(StatType.ProjectileAreaSize, 1.5f);
        Assert.That(_weapon.GetManualAimConvergenceDistance(), Is.EqualTo(7f));
        WeaponGeometrySnapshot geometry = _weapon.CaptureGeometry();
        WeaponGeometryGuide line = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Manual && g.Purpose == WeaponGeometryPurpose.ProjectilePath);
        WeaponGeometryGuide blast = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Manual && g.Purpose == WeaponGeometryPurpose.Explosion);
        WeaponGeometryGuide cone = geometry.Guides.Single(g => g.Mode == WeaponStatMode.Manual && g.Purpose == WeaponGeometryPurpose.Hitbox);
        Projectile shot = Fire(WeaponStatMode.Manual);
        Assert.That(line.RangeMeters, Is.EqualTo(Read<float>(shot, "_maxTravelDistance")));
        Assert.That(blast.RadiusMeters, Is.EqualTo(Read<float>(shot, "_explosionRadius")));
        Assert.That(blast.ForwardOffsetMeters, Is.EqualTo(7f));
        Assert.That(cone.RangeMeters, Is.EqualTo(Read<float>(shot, "_fragmentConeRange")));
        Assert.That(cone.FullAngleDegrees, Is.EqualTo(Read<float>(shot, "_fragmentConeAngle")));
        Assert.That(geometry.Guides.Any(g => g.Mode == WeaponStatMode.Ability && g.Purpose == WeaponGeometryPurpose.Targeting), Is.False);
    }

    [Test]
    public void CollisionBeforeAuthoredEndpointStillDetonatesAtRealObstacle()
    {
        Create();
        _row.Manual.RangeMeters = Set(7f);
        var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = _muzzle.position + Vector3.forward * 2f;
        wall.transform.localScale = new Vector3(2f, 2f, .1f);
        Physics.SyncTransforms();
        Projectile shot = Fire(WeaponStatMode.Manual);
        Field(shot, "_replaceExplosionVfx", true);
        AdvanceFlight(shot, 100);
        Assert.That(shot.gameObject.activeSelf, Is.False);
        Assert.That(shot.transform.position.z - _muzzle.position.z, Is.LessThan(2.01f));
        Assert.That(shot.transform.position.z - _muzzle.position.z, Is.GreaterThan(1f));
    }

    [TestCase(WeaponStatMode.Automatic, 1f, false)]
    [TestCase(WeaponStatMode.Automatic, 3f, true)]
    [TestCase(WeaponStatMode.Ability, 1f, false)]
    [TestCase(WeaponStatMode.Ability, 3f, true)]
    public void LegacyAutomaticAndAbilityRadiusControlsMoveRealInheritedBlastBoundary(WeaponStatMode mode, float radius, bool hit)
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        if (mode == WeaponStatMode.Automatic) _instance.Data.RocketLauncher.RocketAutoExplosionRadius = radius;
        else _instance.Data.RocketLauncher.RocketActiveExplosionRadius = radius;
        var target = Target(_muzzle.position + Vector3.right * 1.5f);
        Explode(Fire(mode), _muzzle.position);
        Assert.That(target.CurrentHealth, Is.EqualTo(hit ? 9990 : 10000));
        Assert.That(Values(mode).ExplosionRadiusMeters.Enabled, Is.False);
    }

    [Test]
    public void LegacyAutomaticSpeedMultiplierChangesActualLoftedMotion()
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        float slowDistance = 0f;
        foreach (float multiplier in new[] { .5f, 2f })
        {
            _instance.Data.RocketLauncher.RocketAutoSpeedMultiplier = multiplier;
            Projectile shot = Fire(WeaponStatMode.Automatic);
            Assert.That(shot.ActiveSpeed, Is.EqualTo(shot.AuthoredSpeed * multiplier * DebugSpeedTool.ProjectileScale));
            AdvanceFlight(shot, 1);
            float distance = Vector3.Distance(_muzzle.position, shot.transform.position);
            if (multiplier == .5f) { slowDistance = distance; Assert.That(distance, Is.GreaterThan(0f)); }
            else Assert.That(distance, Is.GreaterThan(slowDistance * 2f));
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void LegacyQConeAngleChangesActualLockInclusionAndReleasedRocket(WeaponUpgradePath path)
    {
        Create(path);
        Target(_muzzle.position + Quaternion.Euler(0f, 30f, 0f) * Vector3.forward * 6f);
        foreach (float angle in new[] { 40f, 80f })
        {
            _instance.Data.RocketLauncher.RocketActiveConeAngle = angle;
            _instance.AbilityCooldownTimer = 0f;
            _weapon.BeginActiveAbility(Vector3.forward);
            int expected = angle == 40f ? 0 : 1;
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(expected), "The enemy at 30 degrees crosses the authored half-angle.");
            _weapon.ReleaseActiveAbility(Vector3.forward);
            Assert.That(Active().Length, Is.EqualTo(expected), "Only an acquired target produces a released Q rocket.");
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void LegacyQLockIntervalChangesElapsedHoldNeededForSecondLockAndRocket(WeaponUpgradePath path)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        Target(_muzzle.position + new Vector3(1f, 0f, 5f));
        _instance.Data.RocketLauncher.RocketActiveBaseRocketCount = 2;
        _instance.Data.RocketLauncher.RocketActiveInitialTargetCount = 1;
        _instance.Data.RocketLauncher.RocketActiveConeAngle = 120f;
        foreach (float interval in new[] { .25f, 1.5f })
        {
            _instance.Data.RocketLauncher.RocketActiveTargetLockInterval = interval;
            _instance.AbilityCooldownTimer = 0f;
            _weapon.BeginActiveAbility(Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(1));
            _weapon.TickActiveAbility(interval - .001f, Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(1));
            _weapon.TickActiveAbility(.002f, Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(2));
            _weapon.ReleaseActiveAbility(Vector3.forward);
            Assert.That(Active().Length, Is.EqualTo(2));
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void LegacyQMarkerRadiusChangesRenderedWorldBracketExtentWithoutChangingLocks(WeaponUpgradePath path)
    {
        Create(path);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        float smallExtent = 0f;
        foreach (float radius in new[] { .2f, .8f })
        {
            _instance.Data.RocketLauncher.RocketActiveTargetMarkerRadius = radius;
            _weapon.BeginActiveAbility(Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(1));
            var markers = Read<List<RocketTargetMarkerVfx>>(_weapon, "_targetMarkers");
            Assert.That(markers.Count, Is.EqualTo(1));
            RocketTargetMarkerVfx marker = markers[0];
            Call(marker, "DrawMarker");
            LineRenderer bracket = marker.transform.Find("Lock Bracket 1").GetComponent<LineRenderer>();
            Assert.That(bracket.useWorldSpace, Is.True);
            Vector3 corner = bracket.GetPosition(1);
            float extent = Mathf.Abs(corner.x - target.transform.position.x);
            // The existing animation pulses the actual radius by +/- 6 percent.
            Assert.That(extent, Is.InRange(radius * .94f - .002f, radius * 1.06f + .002f));
            Assert.That(Mathf.Abs(corner.z - target.transform.position.z),
                Is.InRange(radius * .94f - .002f, radius * 1.06f + .002f));
            if (radius == .2f) smallExtent = extent;
            else Assert.That(extent, Is.GreaterThan(smallExtent * 3.4f));
            _weapon.CancelActiveAbility();
            Assert.That(_instance.CurrentAmmo, Is.EqualTo(100f));
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void LegacyQSpeedChangesActualProjectileDisplacementWithoutChangingLifetimeOrManualSpeed(WeaponUpgradePath path)
    {
        Create(path);
        // EditMode does not run Awake for the inactive prewarmed clone. Match
        // Play activation before ConfigurePooled, rather than comparing a first-
        // Launch prefab lifetime with the pool lifetime used on later leases.
        foreach (Projectile pooled in _container.GetComponentsInChildren<Projectile>(true)) Call(pooled, "Awake");
        Target(_muzzle.position + Vector3.forward * 8f);
        float manualSpeed = _instance.Data.RocketLauncher.RocketManualSpeedMultiplier;
        float slowDistance = 0f;
        foreach (float multiplier in new[] { .5f, 2f })
        {
            _instance.Data.RocketLauncher.RocketActiveSpeedMultiplier = multiplier;
            Projectile shot = Fire(WeaponStatMode.Ability);
            Assert.That(shot, Is.Not.Null);
            Assert.That(shot.ActiveSpeed, Is.EqualTo(shot.AuthoredSpeed * multiplier * DebugSpeedTool.ProjectileScale));
            Assert.That(shot.ActiveLifetimeSeconds, Is.EqualTo(_pool.ProjectileLifetime));
            AdvanceFlight(shot, 1);
            float distance = Vector3.Distance(_muzzle.position, shot.transform.position);
            if (multiplier == .5f)
            {
                slowDistance = distance;
                Assert.That(distance, Is.GreaterThan(0f));
            }
            else
            {
                Assert.That(distance, Is.EqualTo(slowDistance * 4f).Within(.005f));
                Assert.That(shot.ActiveLifetimeSeconds, Is.EqualTo(_pool.ProjectileLifetime));
            }
            ReleaseAll();
        }
        Projectile manual = Fire(WeaponStatMode.Manual);
        Assert.That(manual.ActiveSpeed, Is.EqualTo(manual.AuthoredSpeed * manualSpeed * DebugSpeedTool.ProjectileScale));
    }

    [Test]
    public void FragmentQIgnoresLegacyLockConeIntervalAndMarkerControls()
    {
        Create(WeaponUpgradePath.PathB);
        Target(_muzzle.position + Vector3.forward * 4f);
        foreach (bool wideSlow in new[] { false, true })
        {
            _instance.Data.RocketLauncher.RocketActiveConeAngle = wideSlow ? 160f : 1f;
            _instance.Data.RocketLauncher.RocketActiveTargetLockInterval = wideSlow ? 3f : .02f;
            _instance.Data.RocketLauncher.RocketActiveTargetMarkerRadius = wideSlow ? 2f : .1f;
            _instance.AbilityCooldownTimer = 0f;
            _weapon.BeginActiveAbility(Vector3.forward);
            _weapon.TickActiveAbility(4f, Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.Zero);
            Assert.That(Read<List<RocketTargetMarkerVfx>>(_weapon, "_targetMarkers"), Is.Empty);
            _weapon.ReleaseActiveAbility(Vector3.forward);
            Assert.That(Active().Length, Is.EqualTo(1), "Fragment Q remains one direct parent casing.");
            ReleaseAll();
        }
    }

    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.None)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.None)]
    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.PathA)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.PathA)]
    [TestCase(WeaponStatMode.Automatic, WeaponUpgradePath.PathB)]
    [TestCase(WeaponStatMode.Ability, WeaponUpgradePath.PathB)]
    public void LegacyAutomaticAndQFalloffChangeActualHalfRadiusHealthLoss(WeaponStatMode mode, WeaponUpgradePath path)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        var target = Target(_muzzle.position + Vector3.right);
        Values(mode).Damage = Set(100f);
        Values(mode).ExplosionRadiusMeters = Set(2f);
        if (path == WeaponUpgradePath.PathA)
        {
            _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
            _row.Rocket.KineticExplosionFalloffMultiplier = Set(1f);
        }
        if (path == WeaponUpgradePath.PathB)
        {
            _row.Rocket.FragmentDamageMultiplier = Set(0f);
            _row.Rocket.AbilityClusterRocketCount = SetInt(0);
        }
        foreach (float falloff in new[] { 0f, .8f })
        {
            if (mode == WeaponStatMode.Automatic) _instance.Data.RocketLauncher.RocketAutoExplosionFalloff = falloff;
            else _instance.Data.RocketLauncher.RocketActiveExplosionFalloff = falloff;
            int before = target.CurrentHealth;
            Explode(Fire(mode), _muzzle.position);
            Assert.That(before - target.CurrentHealth, Is.EqualTo(falloff == 0f ? 100 : 60));
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void LegacyQMaximumRocketCountCapsActualHeldLocksAndReleasedVolley(WeaponUpgradePath path)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        Target(_muzzle.position + new Vector3(.5f, 0f, 5f));
        Target(_muzzle.position + new Vector3(-.5f, 0f, 6f));
        _instance.Data.RocketLauncher.RocketActiveInitialTargetCount = 1;
        _instance.Data.RocketLauncher.RocketActiveTargetLockInterval = .1f;
        _instance.Data.RocketLauncher.RocketActiveConeAngle = 120f;
        foreach (int maximum in new[] { 1, 3 })
        {
            _instance.Data.RocketLauncher.RocketActiveBaseRocketCount = maximum;
            _instance.AbilityCooldownTimer = 0f;
            _weapon.BeginActiveAbility(Vector3.forward);
            _weapon.TickActiveAbility(1f, Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(maximum));
            _weapon.ReleaseActiveAbility(Vector3.forward);
            Assert.That(Active().Length, Is.EqualTo(maximum));
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    public void LegacyQInitialTargetCountChangesImmediateLocksAndTapVolley(WeaponUpgradePath path)
    {
        Create(path);
        Target(_muzzle.position + Vector3.forward * 4f);
        Target(_muzzle.position + new Vector3(.5f, 0f, 5f));
        Target(_muzzle.position + new Vector3(-.5f, 0f, 6f));
        _instance.Data.RocketLauncher.RocketActiveBaseRocketCount = 3;
        _instance.Data.RocketLauncher.RocketActiveTargetLockInterval = 2f;
        _instance.Data.RocketLauncher.RocketActiveConeAngle = 120f;
        foreach (int initial in new[] { 1, 3 })
        {
            _instance.Data.RocketLauncher.RocketActiveInitialTargetCount = initial;
            _instance.AbilityCooldownTimer = 0f;
            _weapon.BeginActiveAbility(Vector3.forward);
            Assert.That(_weapon.CurrentRocketLocks, Is.EqualTo(initial), "No hold tick has elapsed.");
            _weapon.ReleaseActiveAbility(Vector3.forward);
            Assert.That(Active().Length, Is.EqualTo(initial));
            ReleaseAll();
        }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void LegacyManualSpeedChangesActualMotionAndInheritedFragmentChildSpeed(WeaponUpgradePath path)
    {
        Create(path);
        if (path == WeaponUpgradePath.PathB) _row.Rocket.AbilityClusterRocketCount = SetInt(1);
        float slowDistance = 0f;
        foreach (float multiplier in new[] { .5f, 2f })
        {
            _instance.Data.RocketLauncher.RocketManualSpeedMultiplier = multiplier;
            Projectile manual = Fire(WeaponStatMode.Manual);
            AdvanceFlight(manual, 1);
            float distance = Vector3.Distance(_muzzle.position, manual.transform.position);
            if (multiplier == .5f) { slowDistance = distance; Assert.That(distance, Is.GreaterThan(0f)); }
            else Assert.That(distance, Is.EqualTo(slowDistance * 4f).Within(.005f));
            ReleaseAll();
            if (path != WeaponUpgradePath.PathB) continue;
            Projectile parent = Fire(WeaponStatMode.Ability);
            Explode(parent, _muzzle.position + Vector3.forward * 30f);
            Projectile child = Active().Single(p => p != parent);
            Vector3 start = child.transform.position;
            float expectedStep = child.AuthoredSpeed * multiplier * DebugSpeedTool.ProjectileScale * Time.fixedDeltaTime;
            AdvanceFlight(child, 1);
            Assert.That(Vector3.Distance(start, child.transform.position), Is.EqualTo(expectedStep).Within(.003f));
            Assert.That(_row.Rocket.AbilityClusterSpeedMultiplier.Enabled, Is.False);
            ReleaseAll();
        }
    }

    [Test]
    public void LegacyAutomaticBurstRateChangesActualVolleyStartTimes()
    {
        Create();
        Target(_muzzle.position + Vector3.forward * 4f);
        foreach (float rate in new[] { 2f, 4f })
        {
            _instance.Data.RocketLauncher.RocketAutoBurstsPerSecond = rate;
            Fire(WeaponStatMode.Automatic);
            Assert.That(Active().Length, Is.EqualTo(1));
            Tick(WeaponStatMode.Automatic, 1f / rate - .001f);
            Assert.That(Active().Length, Is.EqualTo(1));
            Tick(WeaponStatMode.Automatic, .002f);
            Assert.That(Active().Length, Is.EqualTo(2));
            ReleaseAll();
        }
        Assert.That(_row.Automatic.AttackCyclesPerSecond.Enabled, Is.False);
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void LegacyQDamageScaleChangesInheritedActualHealthLossAndLeavesManualDamageAlone(WeaponUpgradePath path)
    {
        Create(path);
        if (path == WeaponUpgradePath.PathA) _row.Rocket.KineticDamageTakenMultiplier = Set(1f);
        var target = Target(_muzzle.position + Vector3.forward * 4f);
        foreach (float scale in new[] { 1.5f, 3f })
        {
            _instance.Data.RocketLauncher.RocketActiveDamageScale = scale;
            int before = target.CurrentHealth;
            Explode(Fire(WeaponStatMode.Ability), target.transform.position);
            Assert.That(before - target.CurrentHealth, Is.EqualTo(10f * scale));
            ReleaseAll();
        }
        int manualBefore = target.CurrentHealth;
        Explode(Fire(WeaponStatMode.Manual), target.transform.position);
        Assert.That(manualBefore - target.CurrentHealth, Is.EqualTo(10));
        Assert.That(_row.Ability.Damage.Enabled, Is.False);
    }

    [Test]
    public void FragmentQDiagnosticsOmitLockOnlyValuesAndReportActualParentFlightRange()
    {
        Create(WeaponUpgradePath.PathB);
        _row.Rocket.AbilityProjectileTravelRangeMeters = Set(7f);
        WeaponDiagnosticSection active = _weapon.CaptureDiagnostics().Sections.Single(s => s.Name == "Active Ability");
        string[] labels = active.Values.Select(v => v.Label).ToArray();
        Assert.That(labels, Does.Not.Contain("Maximum locked targets"));
        Assert.That(labels, Does.Not.Contain("Lock interval"));
        Assert.That(labels, Does.Not.Contain("Count assumption"));
        Assert.That(labels, Does.Contain("Cluster rockets"));
        Assert.That(active.Values.Single(v => v.Label == "Range").Value, Is.EqualTo("7 m"));
        Assert.That(active.Values.Single(v => v.Label == "Projectile travel range").Value, Is.EqualTo("7 m"));
    }

    private static void AdvanceFlight(Projectile shot, int maximumSteps, bool advanceLifetime = false)
    {
        SimulationMode previous = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            for (int i = 0; i < maximumSteps && shot.gameObject.activeSelf; i++)
            {
                Call(shot, "FixedUpdate");
                if (!shot.gameObject.activeSelf) break;
                Physics.Simulate(Time.fixedDeltaTime);
                if (advanceLifetime) Call(shot, "AdvanceLifetime", Time.fixedDeltaTime);
            }
        }
        finally { Physics.simulationMode = previous; }
    }

    private void Create(WeaponUpgradePath path = WeaponUpgradePath.None)
    {
        var data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.RocketLauncher; data.EnsureSpecificTuningForCurrentType();
        data.WeaponId = "rocket-stat-test"; data.BaseDamage = 10f; data.BaseAttackRate = 1f;
        data.BaseRange = 12f; data.BaseManualAmmo = 100f; data.BaseKnockback = 1f;
        data.ActiveAbilityAmmoCost = 10f; data.SkillCooldown = 5f;
        data.RocketLauncher.RocketAutoBaseRocketCount = 1;
        data.RocketLauncher.RocketAutoExplosionFalloff = 0f;
        data.RocketLauncher.RocketManualExplosionFalloff = 0f;
        data.RocketLauncher.RocketActiveExplosionFalloff = 0f;
        data.RocketLauncher.RocketActiveDamageScale = 1f;
        data.RocketLauncher.RocketActiveBaseRocketCount = 1;
        data.RocketLauncher.RocketActiveInitialTargetCount = 1;
        _row = WeaponStatProfile.CreateRow(WeaponType.RocketLauncher, path == WeaponUpgradePath.None ? 5 : 6, path);
        data.StatProfile.Levels.Add(_row);
        _instance = new WeaponInstance { Data = data, Level = _row.Level, SelectedPath = path, State = WeaponState.Manual, CurrentAmmo = 100f };
        _muzzle = Track(new GameObject("Rocket stat muzzle")).transform;
        _muzzle.position = new Vector3(14000f, 14000f, 14000f);
        _stats = Track(new GameObject("Rocket stat owner")).AddComponent<PlayerStats>();
        _stats.transform.position = _muzzle.position;
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction })
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            Field(definition, "<StatType>k__BackingField", type);
            Field(definition, "<BaseValue>k__BackingField", type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        Field(_stats, "_statDefinitions", definitions); Call(_stats, "Awake");
        var prefab = Track(new GameObject("Rocket stat projectile")); prefab.SetActive(false); prefab.AddComponent<Projectile>();
        _container = Track(new GameObject("Rocket stat projectiles")).transform;
        var poolObject = Track(new GameObject("Rocket stat pool")); poolObject.SetActive(false);
        _pool = poolObject.AddComponent<ProjectilePool>(); Field(_pool, "_projectilePrefab", prefab);
        Field(_pool, "_container", _container); Field(_pool, "_initialPoolSize", 1); Field(_pool, "_maxPoolSize", 64); Call(_pool, "Awake");
        _weapon = new RocketLauncherWeapon(new ConfiguredEnemyTargeting(), _pool, _muzzle);
        _weapon.Setup(_instance, _stats.transform, _stats, null);
    }

    private EnemyHealth Target(Vector3 position)
    {
        var target = Track(new GameObject("Rocket stat target")); target.transform.position = position;
        target.AddComponent<SphereCollider>().radius = .02f;
        var health = target.AddComponent<EnemyHealth>(); health.ApplyConfiguredMaxHealth(10000);
        EnemyRegistry.Register(target.transform); _targets.Add(target.transform); Physics.SyncTransforms(); return health;
    }
    private WeaponStatModeValues Values(WeaponStatMode mode) => mode == WeaponStatMode.Automatic ? _row.Automatic : mode == WeaponStatMode.Manual ? _row.Manual : _row.Ability;
    private Projectile[] Active() => _container.GetComponentsInChildren<Projectile>(true).Where(p => p.gameObject.activeSelf).ToArray();
    private Projectile Fire(WeaponStatMode mode)
    {
        Call(_weapon, "CancelAutomaticVolley");
        _instance.State = mode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual;
        _instance.AbilityCooldownTimer = 0f;
        typeof(BasicProjectileWeapon).GetField("FireTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_weapon, 0f);
        var before = new HashSet<Projectile>(Active());
        Tick(mode, 0f); return Active().FirstOrDefault(p => !before.Contains(p));
    }
    private void Tick(WeaponStatMode mode, float delta)
    {
        if (mode == WeaponStatMode.Automatic) _weapon.TickAutomatic(delta, Vector3.forward);
        else if (mode == WeaponStatMode.Manual) _weapon.TickManual(delta, Vector3.forward, true);
        else _weapon.UseActiveAbility(Vector3.forward);
    }
    private void Explode(Projectile projectile, Vector3 position)
    {
        Assert.That(projectile, Is.Not.Null);
        projectile.transform.position = position; projectile.GetComponent<Rigidbody>().position = position;
        Field(projectile, "_replaceExplosionVfx", true); Physics.SyncTransforms(); Call(projectile, "ApplyExplosionDamage");
    }
    private void ReleaseAll() { foreach (Projectile p in Active()) _pool.Release(p.gameObject); }
    private void PlayerValue(StatType type, float value)
    {
        Assert.That(_stats.TryGetRuntimeStat(type, out RuntimeStat runtime), Is.True);
        runtime.SetBaseValue(value);
    }
    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static WeaponOptionalFloat Set(float value) => new() { Enabled = true, Value = value };
    private static WeaponOptionalInt SetInt(int value) => new() { Enabled = true, Value = value };
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static T Call<T>(object target, string name, params object[] args) => (T)Call(target, name, args);
}
