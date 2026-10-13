using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>Gate A and mutation coverage for the first typed authoring slice.</summary>
public sealed class WeaponStatAuthoringTests
{
    private readonly List<Object> _cleanup = new();
    private readonly List<string> _assetPaths = new();
    private Random.State _randomState;
    private Object _selection;

    [SetUp]
    public void SetUp()
    {
        _randomState = Random.state;
        _selection = Selection.activeObject;
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Stat authoring challenge tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Selection.activeObject = _selection;
        foreach (string path in _assetPaths) AssetDatabase.DeleteAsset(path);
        _assetPaths.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        Random.state = _randomState;
    }

    [Test]
    public void InheritedProfiles_PreserveAll225BaselineModeStatesWithoutMutationOrRandomness()
    {
        PlayerStats stats = CreateStats(chance: .25f, criticalDamage: 2.5f, cooldownReduction: .2f);
        int checkedModes = 0;
        foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
        foreach (var key in ProgressionKeys())
        {
            WeaponInstance instance = CreateInstance(type, key.level, key.path);
            var behaviour = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(instance.Data, null, null, null, null);
            behaviour.Setup(instance, stats.transform, stats, null);
            instance.Data.StatProfile = null;
            WeaponDiagnosticsSnapshot legacy = behaviour.CaptureDiagnostics();
            float legacyFactor = type == WeaponType.AutomaticCannon
                ? instance.Data.AutomaticCannon.CannonCriticalDamageMultiplierOverride * (key.path == WeaponUpgradePath.PathB ? 1.5f : 1f)
                : 1f;
            instance.Data.StatProfile = new WeaponStatProfile
            {
                Levels = new List<WeaponStatLevelRow> { Row(type, key.level, key.path) }
            };
            string dataBefore = EditorJsonUtility.ToJson(instance.Data);
            string runtimeBefore = JsonUtility.ToJson(instance);
            Random.State randomBefore = Random.state;
            WeaponDiagnosticsSnapshot inherited = behaviour.CaptureDiagnostics();
            Assert.That(WeaponStatAuthoringResolver.ResolveCriticalChance(instance, .25f).Value, Is.EqualTo(.25f));
            Assert.That(WeaponStatAuthoringResolver.ResolveCriticalDamageMultiplier(instance, legacyFactor).Value, Is.EqualTo(legacyFactor));
            Assert.That(WeaponMath.GetAbilityCooldownDuration(instance, stats), Is.EqualTo(11.2f).Within(.0001f));
            foreach (string mode in new[] { "Automatic", "Manual", "Active Ability" })
            {
                Assert.That(Values(inherited, mode), Is.EqualTo(Values(legacy, mode)), $"{type}/{key.level}/{key.path}/{mode}");
                checkedModes++;
            }
            Assert.That(EditorJsonUtility.ToJson(instance.Data), Is.EqualTo(dataBefore));
            Assert.That(JsonUtility.ToJson(instance), Is.EqualTo(runtimeBefore));
            Assert.That(Random.state, Is.EqualTo(randomBefore));
        }
        Assert.That(checkedModes, Is.EqualTo(225));
    }

    [Test]
    public void ExactRows_IsolateWeaponsLevelsPathsAndLevelFiveToSixTransitions()
    {
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon, 5);
        var basic = Row(WeaponType.AutomaticCannon, 5, WeaponUpgradePath.None, .1f, 1.5f, 20f);
        var pathA = Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA, .2f, 2f, 8f);
        var pathB = Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB, .3f, 3f, 3f);
        var otherType = Row(WeaponType.RocketLauncher, 6, WeaponUpgradePath.PathA, .4f, 4f, 5f);
        instance.Data.StatProfile.Levels.AddRange(new[] { basic, pathA, pathB, otherType });
        AssertResolved(instance, .1f, 1.5f, 20f);
        instance.SelectedPath = WeaponUpgradePath.PathA; // Legacy pre-6 selection is ignored.
        AssertResolved(instance, .1f, 1.5f, 20f);
        instance.Level = 6;
        AssertResolved(instance, .2f, 2f, 8f);
        instance.SelectedPath = WeaponUpgradePath.PathB;
        AssertResolved(instance, .3f, 3f, 3f);
        pathA.AbilityCooldownSeconds = Enabled(80f);
        pathA.CriticalChance = Enabled(.9f);
        AssertResolved(instance, .3f, 3f, 3f);
        instance.Level = 7;
        Assert.That(WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(instance).Value, Is.EqualTo(14f));
        Assert.That(WeaponStatAuthoringResolver.ResolveCriticalChance(instance, .25f).IsAuthored, Is.False);
        instance.Level = 6;
        instance.SelectedPath = WeaponUpgradePath.None;
        Assert.That(WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(instance).IsAuthored, Is.False);
        instance.SelectedPath = WeaponUpgradePath.PathA;
        instance.Data.WeaponType = WeaponType.RocketLauncher;
        AssertResolved(instance, .4f, 4f, 5f);
        WeaponInstance separateAsset = CreateInstance(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA);
        Assert.That(WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(separateAsset).Value, Is.EqualTo(14f));
    }

    [TestCase(WeaponCriticalChanceComposition.AddToPlayer, .25f, .5f, .75f)]
    [TestCase(WeaponCriticalChanceComposition.AddToPlayer, .75f, .5f, 1f)]
    [TestCase(WeaponCriticalChanceComposition.ReplacePlayer, .75f, 0f, 0f)]
    [TestCase(WeaponCriticalChanceComposition.ReplacePlayer, 0f, 1f, 1f)]
    public void CriticalChance_ComposesInExplicitFractionUnits(WeaponCriticalChanceComposition composition, float player, float authored, float expected)
    {
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon);
        var row = Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        row.CriticalChance = Enabled(authored);
        row.CriticalChanceComposition = composition;
        instance.Data.StatProfile.Levels.Add(row);
        var resolved = WeaponStatAuthoringResolver.ResolveCriticalChance(instance, player);
        Assert.That(resolved.Value, Is.EqualTo(expected));
        Assert.That(resolved.IsAuthored, Is.True);
        row.CriticalChance.Enabled = false;
        Assert.That(WeaponStatAuthoringResolver.ResolveCriticalChance(instance, player).Value, Is.EqualTo(player));
    }

    [Test]
    public void CriticalRoll_UsesDeterministicBoundaryAndConsumesOneSampleAtZeroAndOne()
    {
        PlayerStats stats = CreateStats(chance: .8f);
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon);
        var row = Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, 0f);
        instance.Data.StatProfile.Levels.Add(row);
        Random.InitState(19037);
        Random.State before = Random.state;
        float sample = Random.value;
        Random.State after = Random.state;
        Assert.That(sample, Is.InRange(.0001f, .9999f));
        foreach (float chance in new[] { 0f, sample - .00001f, sample + .00001f, 1f })
        {
            row.CriticalChance = Enabled(chance);
            Random.state = before;
            Assert.That(WeaponDamageResolver.RollCrit(stats, instance), Is.EqualTo(chance > sample), $"sample={sample}; chance={chance}");
            Assert.That(Random.state, Is.EqualTo(after), "Each eligible hit retains one random sample.");
        }
    }

    [TestCase("version")]
    [TestCase("duplicate")]
    [TestCase("null row")]
    [TestCase("null list")]
    [TestCase("basic path")]
    [TestCase("advanced no path")]
    [TestCase("level zero")]
    [TestCase("level eleven")]
    [TestCase("unknown weapon")]
    [TestCase("unknown composition")]
    [TestCase("chance out of range")]
    [TestCase("chance NaN")]
    [TestCase("severity negative")]
    [TestCase("cooldown infinity")]
    [TestCase("null typed payload")]
    public void InvalidProfiles_FailExplicitlyInsteadOfSilentlySelectingARow(string defect)
    {
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon);
        var row = Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None);
        instance.Data.StatProfile.Levels.Add(row);
        switch (defect)
        {
            case "version": instance.Data.StatProfile.Version++; break;
            case "duplicate": instance.Data.StatProfile.Levels.Add(Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None)); break;
            case "null row": instance.Data.StatProfile.Levels.Add(null); break;
            case "null list": instance.Data.StatProfile.Levels = null; break;
            case "basic path": row.Path = WeaponUpgradePath.PathA; break;
            case "advanced no path": row.Level = 6; break;
            case "level zero": row.Level = 0; break;
            case "level eleven": row.Level = 11; row.Path = WeaponUpgradePath.PathA; break;
            case "unknown weapon": row.WeaponType = (WeaponType)99; break;
            case "unknown composition": row.CriticalChanceComposition = (WeaponCriticalChanceComposition)99; break;
            case "chance out of range": row.CriticalChance = Enabled(1.01f); break;
            case "chance NaN": row.CriticalChance = Enabled(float.NaN); break;
            case "severity negative": row.CriticalDamageMultiplier = Enabled(-.01f); break;
            case "cooldown infinity": row.AbilityCooldownSeconds = Enabled(float.PositiveInfinity); break;
            case "null typed payload": row.Cannon = null; break;
        }
        Assert.That(instance.Data.StatProfile.TryValidate(WeaponType.AutomaticCannon, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.Throws<InvalidOperationException>(() => WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(instance));
    }

    [TestCase(0f, 3f, 90)]
    [TestCase(1f, 3f, 40)]
    [TestCase(1f, 0f, 90)]
    public void CannonProjectile_AuthoredCriticalControlsChangeActualTargetHealth(float chance, float severity, int remainingHealth)
    {
        PlayerStats stats = CreateStats(chance: 1f, criticalDamage: 2f);
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon);
        instance.Data.StatProfile.Levels.Add(Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, chance, severity));
        instance.Data.AutomaticCannon.CannonManualBurstCount = 1;
        instance.Data.AutomaticCannon.CannonBurstProjectileScatterDegrees = 0f;
        Transform spawn = Track(new GameObject("Stat cannon muzzle")).transform;
        spawn.position = new Vector3(9000f, 9000f, 9000f);
        EnemyHealth target = CreateTarget(spawn.position + Vector3.forward);
        ProjectilePool pool = CreatePool(out Transform container);
        var weapon = new AutomaticCannonWeapon(null, pool, spawn);
        weapon.Setup(instance, stats.transform, stats, null);
        weapon.TickManual(1f, Vector3.forward, true);
        Projectile projectile = container.GetComponentsInChildren<Projectile>(true).Single(p => p.gameObject.activeSelf);
        Physics.SyncTransforms();
        bool consumed = Call<bool>(projectile, "TryConsumeSweptWorldCollision", spawn.position, Vector3.forward * 2f);
        Assert.That(consumed, Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(remainingHealth));
        Assert.That(instance.CurrentAmmo, Is.EqualTo(99f));
        string damageLabel = chance > 0f ? "Damage / hit (critical)" : "Damage / hit (non-critical)";
        Assert.That(Number(weapon.CaptureDiagnostics(), "Manual", damageLabel), Is.EqualTo(100 - remainingHealth));
        Assert.That(Number(weapon.CaptureDiagnostics(), "Manual", "Critical chance"), Is.EqualTo(chance * 100f));
    }

    [Test]
    public void FiredDamageContext_RetainsItsCriticalOutcomeWhenAuthoringChanges()
    {
        PlayerStats stats = CreateStats(chance: 0f, criticalDamage: 2f);
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon);
        var row = Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, 1f, 3f);
        instance.Data.StatProfile.Levels.Add(row);
        var fired = new WeaponDamageContext(stats, instance, true, 2f, 1f, false, 1f);
        row.CriticalChance = Enabled(0f);
        row.CriticalDamageMultiplier = Enabled(100f);
        var next = new WeaponDamageContext(stats, instance, true, 2f, 1f, false, 1f);
        Assert.That(fired.CalculateDamage(false), Is.EqualTo(60));
        Assert.That(next.CalculateDamage(false), Is.EqualTo(10));
    }

    [Test]
    public void MortarAndBurn_KeepExistingNonCriticalEligibility()
    {
        PlayerStats stats = CreateStats(chance: 1f, criticalDamage: 50f);
        WeaponInstance mortarInstance = CreateInstance(WeaponType.Mortar);
        var mortar = new MortarWeapon(null, null, null);
        mortar.Setup(mortarInstance, stats.transform, stats, null);
        var mortarContext = Call<WeaponDamageContext>(mortar, "CreateDamageContext", 1f, false, -1f);
        Assert.That(mortar.CanCrit(), Is.False);
        Assert.That(mortarContext.IsCritical, Is.False);
        Assert.That(mortarContext.CalculateDamage(false), Is.EqualTo(10));
        Assert.That(Values(mortar.CaptureDiagnostics(), "Manual"), Does.Contain("Critical hits=Unavailable"));

        WeaponInstance flameInstance = CreateInstance(WeaponType.Flamethrower);
        flameInstance.Data.StatProfile.Levels.Add(Row(WeaponType.Flamethrower, 1, WeaponUpgradePath.None, 1f, 100f));
        flameInstance.Data.Flamethrower.FlameBurnDamageScale = .5f;
        var flame = new FlamethrowerWeapon(null, null, null, null);
        flame.Setup(flameInstance, stats.transform, stats, null);
        var burnContext = Call<WeaponDamageContext>(flame, "CreateBurnDamageContext", flameInstance.Data.Flamethrower, false);
        Assert.That(burnContext.CanCrit, Is.False);
        Assert.That(burnContext.IsCritical, Is.False);
        EnemyHealth target = CreateTarget(new Vector3(9000f, 9010f, 9000f));
        Assert.That(target.ApplyDotDamage(burnContext.CalculateDamage(false)), Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(95), "Direct-hit critical overrides must not leak into burn damage.");
    }

    [TestCase(0f, false)]
    [TestCase(8f, false)]
    [TestCase(100000f, false)]
    [TestCase(0f, true)]
    [TestCase(8f, true)]
    [TestCase(100000f, true)]
    public void CannonQ_StartsResolvedCooldownAndRestartsAfterRepeatedReloads(float authoredCooldown, bool authoredCost)
    {
        PlayerStats stats = CreateStats(cooldownReduction: .25f);
        stats.gameObject.SetActive(false);
        WeaponManager manager = stats.gameObject.AddComponent<WeaponManager>();
        SetField(manager, "_stats", stats);
        WeaponData data = CreateInstance(WeaponType.AutomaticCannon).Data;
        data.StatProfile.Levels.Add(Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, cooldown: authoredCooldown));
        data.StatProfile.Levels[0].AbilityAmmoCost = new WeaponOptionalFloat { Enabled = authoredCost, Value = 7f };
        Assert.That(manager.AddWeapon(data), Is.True);
        IWeaponBehaviour behaviour = manager.GetCurrentManualBehaviour();
        WeaponInstance runtime = behaviour.Runtime;
        float expected = authoredCooldown * .75f;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            behaviour.UseActiveAbility(Vector3.forward);
            Assert.That(runtime.AbilityCooldownTimer, Is.EqualTo(expected).Within(.001f));
            Assert.That(runtime.CurrentAmmo, Is.EqualTo(authoredCost ? 93f : 60f));
            Assert.That(manager.GetAbilityCooldownNormalized(), Is.EqualTo(expected == 0f ? 1f : 0f));
            if (expected > 0f)
            {
                TickAbilityCooldown(runtime, expected * .5f);
                Assert.That(manager.GetAbilityCooldownNormalized(), Is.EqualTo(.5f).Within(.001f));
            }
            runtime.CurrentAmmo = 0f;
            Call(manager, "UpdateManualWeapon", 0f, Vector3.forward);
            Call(manager, "UpdateManualCycle", 6f);
            Assert.That(runtime.AbilityCooldownTimer, Is.Zero);
            Assert.That(runtime.CurrentAmmo, Is.EqualTo(100f));
            Assert.That(manager.GetCurrentManualWeapon(), Is.SameAs(runtime));
        }
    }

    [Test]
    public void Switching_RefreshesOnlyIncomingCooldownAndPreservesBothAuthoredDurations()
    {
        PlayerStats stats = CreateStats();
        stats.gameObject.SetActive(false);
        var manager = stats.gameObject.AddComponent<WeaponManager>();
        SetField(manager, "_stats", stats);
        WeaponData outgoingData = CreateInstance(WeaponType.AutomaticCannon).Data;
        WeaponData incomingData = CreateInstance(WeaponType.Mortar).Data;
        outgoingData.StatProfile.Levels.Add(Row(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, cooldown: 9f));
        incomingData.StatProfile.Levels.Add(Row(WeaponType.Mortar, 1, WeaponUpgradePath.None, cooldown: 3f));
        manager.AddWeapon(outgoingData);
        manager.AddWeapon(incomingData);
        WeaponInstance outgoing = manager.GetCurrentManualWeapon();
        WeaponInstance incoming = manager.GetEquippedWeapons()[1].Runtime;
        outgoing.AbilityCooldownTimer = 9f;
        incoming.AbilityCooldownTimer = 3f;
        outgoing.CurrentAmmo = 0f;
        Call(manager, "UpdateManualWeapon", 0f, Vector3.forward);
        Call(manager, "UpdateManualCycle", 2.99f);
        Assert.That(incoming.AbilityCooldownTimer, Is.EqualTo(3f));
        Call(manager, "UpdateManualCycle", .02f);
        Assert.That(manager.GetCurrentManualWeapon(), Is.SameAs(incoming));
        Assert.That(incoming.AbilityCooldownTimer, Is.Zero);
        Assert.That(outgoing.AbilityCooldownTimer, Is.EqualTo(9f));
        Assert.That(WeaponMath.GetAbilityCooldownDuration(incoming, stats), Is.EqualTo(3f));
        Assert.That(WeaponMath.GetAbilityCooldownDuration(outgoing, stats), Is.EqualTo(9f));
    }

    [Test]
    public void UpgradePreview_UsesIndependentPathRowWithoutMutatingCurrentLevel()
    {
        PlayerStats stats = CreateStats(chance: .2f);
        WeaponInstance instance = CreateInstance(WeaponType.AutomaticCannon, 5, WeaponUpgradePath.PathA);
        instance.Data.StatProfile.Levels.AddRange(new[]
        {
            Row(WeaponType.AutomaticCannon, 5, WeaponUpgradePath.None, .1f, 1f, 20f),
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA, .7f, 4f, 3f),
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB, .9f, 8f, 1f)
        });
        var weapon = new AutomaticCannonWeapon(null, null, null);
        weapon.Setup(instance, stats.transform, stats, null);
        var preview = weapon.CaptureUpgradeDiagnostics(6);
        Assert.That(Number(preview, "Manual", "Critical chance"), Is.EqualTo(70f));
        Assert.That(Number(preview, "Active Ability", "Ability cooldown duration"), Is.EqualTo(3f));
        Assert.That(Number(weapon.CaptureDiagnostics(), "Manual", "Critical chance"), Is.EqualTo(10f));
        Assert.That(instance.Level, Is.EqualTo(5));
        instance.SelectedPath = WeaponUpgradePath.PathB;
        Assert.That(Number(weapon.CaptureUpgradeDiagnostics(6), "Active Ability", "Ability cooldown duration"), Is.EqualTo(1f));
    }

    [Test]
    public void SerializedAuthoring_UndoRedoAndDiskReloadPreserveHiddenTypeAndPathValues()
    {
        WeaponData data = CreateInstance(WeaponType.AutomaticCannon).Data;
        data.StatProfile.Levels.AddRange(new[]
        {
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA, .35f, 2f, 4f),
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB, .65f, 3f, 7f),
            Row(WeaponType.Flamethrower, 7, WeaponUpgradePath.PathB, .25f, 5f, 12f)
        });
        string path = "Assets/__WeaponStatAuthoringTest_" + Guid.NewGuid().ToString("N") + ".asset";
        _assetPaths.Add(path);
        AssetDatabase.CreateAsset(data, path);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        try
        {
            using (var serialized = new SerializedObject(data))
            {
                var value = serialized.FindProperty("StatProfile.Levels").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("CriticalChance.Value");
                value.floatValue = .85f;
                serialized.ApplyModifiedProperties();
            }
            Undo.FlushUndoRecordObjects();
            Assert.That(data.StatProfile.Levels[0].CriticalChance.Value, Is.EqualTo(.85f));
            Undo.PerformUndo();
            Assert.That(data.StatProfile.Levels[0].CriticalChance.Value, Is.EqualTo(.35f));
            Undo.PerformRedo();
            Assert.That(data.StatProfile.Levels[0].CriticalChance.Value, Is.EqualTo(.85f));
            string profileBefore = JsonUtility.ToJson(data.StatProfile);
            data.WeaponType = WeaponType.Flamethrower;
            data.EnsureSpecificTuningForCurrentType();
            Selection.activeObject = data;
            Selection.activeObject = null;
            data.WeaponType = WeaponType.AutomaticCannon;
            data.EnsureSpecificTuningForCurrentType();
            Assert.That(JsonUtility.ToJson(data.StatProfile), Is.EqualTo(profileBefore));
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            Assert.That(JsonUtility.ToJson(reloaded.StatProfile), Is.EqualTo(profileBefore));
            Assert.That(reloaded.StatProfile.TryValidate(reloaded.WeaponType, out _), Is.True);
        }
        finally
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Undo.ClearUndo(data);
        }
    }

    [Test]
    public void InspectorContextChanges_PreserveAllSerializedValues()
    {
        WeaponData data = CreateInstance(WeaponType.AutomaticCannon).Data;
        data.StatProfile.Levels.AddRange(new[]
        {
            Row(WeaponType.AutomaticCannon, 5, WeaponUpgradePath.None, .2f, 2f, 12f),
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathA, .4f, 3f, 4f),
            Row(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB, .7f, 5f, 8f)
        });
        var editor = Track((WeaponDataEditor)Editor.CreateEditor(data));
        string before = EditorJsonUtility.ToJson(data);
        foreach (var key in ProgressionKeys())
        foreach (WeaponStatAuthoringMode mode in Enum.GetValues(typeof(WeaponStatAuthoringMode)))
            editor.SetAuthoringContext(key.level, key.path, mode);
        Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(before));
    }

    [Test]
    public void InspectorVisibility_RejectsUnsupportedModesAndAdvancedPaths()
    {
        Assert.That(WeaponStatInspectorPolicy.ShowsCriticalControls(WeaponType.Mortar), Is.False);
        Assert.That(WeaponStatInspectorPolicy.ShowsCriticalControls(WeaponType.AutomaticCannon), Is.True);
        Assert.That(WeaponStatInspectorPolicy.ShowsAbilityCooldown(WeaponStatAuthoringMode.Manual), Is.False);
        Assert.That(WeaponStatInspectorPolicy.ShowsAbilityCooldown(WeaponStatAuthoringMode.Automatic), Is.False);
        Assert.That(WeaponStatInspectorPolicy.ShowsAbilityCooldown(WeaponStatAuthoringMode.Ability), Is.True);
        foreach (WeaponStatAuthoringMode mode in Enum.GetValues(typeof(WeaponStatAuthoringMode)))
        {
            Assert.That(Visible(WeaponType.Mortar, nameof(MortarTuning.MortarActiveShellCount), 10, WeaponUpgradePath.PathA, mode), Is.False);
            Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.AtomicDashBaseRangeMultiplier), 5, WeaponUpgradePath.None, mode), Is.False);
            Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.AtomicDashBaseRangeMultiplier), 6, WeaponUpgradePath.PathA, mode), Is.False);
        }
        Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.AtomicDashBaseRangeMultiplier), 6, WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability), Is.True);
        Assert.That(Visible(WeaponType.RotatingBlade, nameof(RotatingBladeTuning.AtomicDashBaseRangeMultiplier), 6, WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Manual), Is.False);
        Assert.That(Visible(WeaponType.Flamethrower, nameof(FlamethrowerTuning.FlameBurnDamageScale), 6, WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Manual), Is.False);
        Assert.That(Visible(WeaponType.Flamethrower, nameof(FlamethrowerTuning.FlameBurnDuration), 6, WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Manual), Is.True);
        Assert.That(Visible(WeaponType.Mortar, nameof(FlamethrowerTuning.FlameActiveRadius), 6, WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability), Is.False);
        Assert.That(Visible(WeaponType.AutomaticCannon, nameof(AutomaticCannonTuning.CannonHeatDamageBonusPerThreshold), 1, WeaponUpgradePath.None, WeaponStatAuthoringMode.Manual), Is.False);
        Assert.That(Visible(WeaponType.RocketLauncher, nameof(RocketLauncherTuning.RocketAutoBaseRocketCount), 1, WeaponUpgradePath.None, WeaponStatAuthoringMode.Manual), Is.False);
    }

    [Test]
    public void SwitchingWeaponType_PreservesRetainedManuallyTunedPayloadThroughUndoAndReload()
    {
        WeaponData data = CreateInstance(WeaponType.Flamethrower).Data;
        data.Flamethrower.FlameAreaFinalRadius = 3.125f;
        data.Flamethrower.FlameBurnDuration = 8.25f;
        data.WeaponType = WeaponType.AutomaticCannon;
        data.EnsureSpecificTuningForCurrentType();
        data.AutomaticCannon.CannonManualBurstsPerSecond = 7.75f;
        string path = "Assets/__WeaponStatAuthoringTest_" + Guid.NewGuid().ToString("N") + ".asset";
        _assetPaths.Add(path);
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var reloaded = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        try
        {
            Undo.RecordObject(reloaded, "Switch retained weapon tuning");
            reloaded.WeaponType = WeaponType.Flamethrower;
            reloaded.EnsureSpecificTuningForCurrentType();
            Undo.FlushUndoRecordObjects();
            Assert.That(reloaded.Flamethrower.FlameAreaFinalRadius, Is.EqualTo(3.125f));
            Assert.That(reloaded.Flamethrower.FlameBurnDuration, Is.EqualTo(8.25f));
            Undo.PerformUndo();
            Assert.That(reloaded.WeaponType, Is.EqualTo(WeaponType.AutomaticCannon));
            Assert.That(reloaded.AutomaticCannon.CannonManualBurstsPerSecond, Is.EqualTo(7.75f));
            Undo.PerformRedo();
            Assert.That(reloaded.WeaponType, Is.EqualTo(WeaponType.Flamethrower));
            Assert.That(reloaded.Flamethrower.FlameAreaFinalRadius, Is.EqualTo(3.125f));
            reloaded.WeaponType = WeaponType.AutomaticCannon;
            reloaded.EnsureSpecificTuningForCurrentType();
            Assert.That(reloaded.AutomaticCannon.CannonManualBurstsPerSecond, Is.EqualTo(7.75f));
        }
        finally { Undo.RevertAllDownToGroup(group); Undo.ClearUndo(reloaded); }
    }

    private static bool Visible(WeaponType type, string field, int level, WeaponUpgradePath path, WeaponStatAuthoringMode mode) =>
        WeaponStatInspectorPolicy.ShowsSpecificField(type, field, level, path, mode);
    private static IEnumerable<(int level, WeaponUpgradePath path)> ProgressionKeys()
    {
        for (int level = 1; level <= 5; level++) yield return (level, WeaponUpgradePath.None);
        foreach (WeaponUpgradePath path in new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
            for (int level = 6; level <= 10; level++) yield return (level, path);
    }

    private WeaponInstance CreateInstance(WeaponType type, int level = 1, WeaponUpgradePath path = WeaponUpgradePath.None)
    {
        var data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.WeaponId = "stat-authoring-" + type;
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseRange = 12f;
        data.BaseManualAmmo = 100f;
        data.ActiveAbilityAmmoCost = 40f;
        data.SkillCooldown = 14f;
        data.EnsureSpecificTuningForCurrentType();
        return new WeaponInstance { Data = data, Level = level, SelectedPath = path, State = WeaponState.Manual, CurrentAmmo = 100f };
    }

    private PlayerStats CreateStats(float chance = 0f, float criticalDamage = 2f, float cooldownReduction = 0f)
    {
        var owner = Track(new GameObject("Stat authoring stats"));
        var stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction })
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField", type == StatType.CriticalChance ? chance
                : type == StatType.CriticalDamage ? criticalDamage : type == StatType.AbilityCooldownReduction ? cooldownReduction : 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        Call(stats, "Awake");
        return stats;
    }

    private EnemyHealth CreateTarget(Vector3 position)
    {
        var target = Track(new GameObject("Stat authoring target"));
        target.transform.position = position;
        target.AddComponent<CapsuleCollider>();
        EnemyHealth health = target.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(100);
        return health;
    }

    private ProjectilePool CreatePool(out Transform container)
    {
        var prefab = Track(new GameObject("Stat authoring projectile"));
        prefab.SetActive(false);
        prefab.AddComponent<Projectile>();
        container = Track(new GameObject("Stat authoring projectile container")).transform;
        var owner = Track(new GameObject("Stat authoring projectile pool"));
        owner.SetActive(false);
        var pool = owner.AddComponent<ProjectilePool>();
        SetField(pool, "_projectilePrefab", prefab);
        SetField(pool, "_container", container);
        SetField(pool, "_initialPoolSize", 1);
        SetField(pool, "_maxPoolSize", 2);
        Call(pool, "Awake");
        return pool;
    }

    private static WeaponOptionalFloat Enabled(float value) => new() { Enabled = true, Value = value };

    private static WeaponStatLevelRow Row(WeaponType type, int level, WeaponUpgradePath path,
        float? chance = null, float? severity = null, float? cooldown = null) => new()
    {
        WeaponType = type, Level = level, Path = path,
        CriticalChance = chance.HasValue ? Enabled(chance.Value) : default,
        CriticalChanceComposition = WeaponCriticalChanceComposition.ReplacePlayer,
        CriticalDamageMultiplier = severity.HasValue ? Enabled(severity.Value) : default,
        AbilityCooldownSeconds = cooldown.HasValue ? Enabled(cooldown.Value) : default
    };

    private static void AssertResolved(WeaponInstance instance, float chance, float severity, float cooldown)
    {
        Assert.That(WeaponStatAuthoringResolver.ResolveCriticalChance(instance, .25f).Value, Is.EqualTo(chance));
        Assert.That(WeaponStatAuthoringResolver.ResolveCriticalDamageMultiplier(instance, 2f).Value, Is.EqualTo(severity));
        Assert.That(WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(instance).Value, Is.EqualTo(cooldown));
    }

    private static string[] Values(WeaponDiagnosticsSnapshot snapshot, string section) => snapshot.Sections
        .Single(s => s.Name == section).Values.Select(v => v.Label + "=" + v.Value).ToArray();

    private static float Number(WeaponDiagnosticsSnapshot snapshot, string section, string label) => float.Parse(snapshot.Sections
        .Single(s => s.Name == section).Values.Single(v => v.Label == label).Value.Split(' ')[0].TrimEnd('%', 'x'), CultureInfo.InvariantCulture);

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        ParameterInfo[] parameters = method.GetParameters();
        if (arguments.Length == parameters.Length) return method.Invoke(target, arguments);
        Assert.That(arguments.Length, Is.LessThan(parameters.Length), name);
        var supplied = new object[parameters.Length];
        Array.Copy(arguments, supplied, arguments.Length);
        for (int i = arguments.Length; i < parameters.Length; i++)
        {
            Assert.That(parameters[i].IsOptional, Is.True, $"Missing required argument {parameters[i].Name} for {name}");
            supplied[i] = parameters[i].DefaultValue;
        }
        return method.Invoke(target, supplied);
    }
    private static T Call<T>(object target, string name, params object[] arguments) => (T)Call(target, name, arguments);
    private static void TickAbilityCooldown(WeaponInstance instance, float delta) => typeof(WeaponManager)
        .GetMethod("TickAbilityCooldown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { instance, delta });
}


