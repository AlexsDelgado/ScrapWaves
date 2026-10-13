using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WeaponStatMigrationTests
{
    private readonly List<Object> _cleanup = new();
    [TearDown] public void TearDown() { for (int i = _cleanup.Count - 1; i >= 0; i--) if (_cleanup[i]) Object.DestroyImmediate(_cleanup[i]); _cleanup.Clear(); }

    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    [TestCase(WeaponType.RotatingBlade)]
    public void MigrationAddsIndependentInheritedRowsWithoutReplacingTuning(WeaponType type)
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>(); _cleanup.Add(data);
        data.WeaponType = type; data.EnsureSpecificTuningForCurrentType();
        data.BaseDamage = 137f; data.BaseRange = 42f; data.BaseManualAmmo = 213f;
        string before = EditorJsonUtility.ToJson(data);
        Assert.That(WeaponStatMigration.DryRun(data).MissingRows, Is.EqualTo(15));
        Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(before), "Dry-run must not dirty or author data");
        Assert.That(WeaponStatMigration.ApplyInheritedRows(data), Is.EqualTo(15));
        Assert.That(data.StatProfile.Levels.Count, Is.EqualTo(15));
        Assert.That(data.StatProfile.Levels.Select(r => r.Level + "/" + r.Path).Distinct().Count(), Is.EqualTo(15));
        Assert.That(data.StatProfile.Levels.All(r => !r.Automatic.Damage.Enabled && !r.Manual.Damage.Enabled
            && !r.Ability.Damage.Enabled && !r.CriticalChance.Enabled && !r.AbilityCooldownSeconds.Enabled && !r.Mortar.EnableCriticalHits), Is.True);
        Assert.That(data.BaseDamage, Is.EqualTo(137)); Assert.That(data.BaseRange, Is.EqualTo(42)); Assert.That(data.BaseManualAmmo, Is.EqualTo(213));
        string first = EditorJsonUtility.ToJson(data);
        Assert.That(WeaponStatMigration.ApplyInheritedRows(data), Is.Zero);
        Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(first), "Repeated migration must be idempotent");
        data.StatProfile.Levels[0].Manual.Damage = new WeaponOptionalFloat { Enabled = true, Value = 33 };
        Assert.That(data.StatProfile.Levels.Skip(1).All(r => !r.Manual.Damage.Enabled), Is.True);
    }

    [Test] public void ProductionMigrationOnCopiesPreservesAll225DiagnosticModes()
    {
        PlayerStats stats = WeaponStatBaselineCapture.CreateNeutralStats(_cleanup);
        int modes = 0;
        foreach (string name in WeaponStatBaselineCapture.ProductionNames)
        {
            WeaponData source = AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{name}.asset");
            string sourceBefore = EditorJsonUtility.ToJson(source); bool dirty = EditorUtility.IsDirty(source);
            WeaponData copy = Object.Instantiate(source); _cleanup.Add(copy); copy.StatProfile = null;
            var observations = new Dictionary<string, string>();
            foreach (var key in Keys()) observations[key.Item1 + "/" + key.Item2] = Observe(copy, stats, key.Item1, key.Item2);
            WeaponStatMigration.ApplyInheritedRows(copy);
            foreach (var key in Keys())
            {
                Assert.That(Observe(copy, stats, key.Item1, key.Item2), Is.EqualTo(observations[key.Item1 + "/" + key.Item2]), name + "/" + key);
                modes += 3;
            }
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(sourceBefore)); Assert.That(EditorUtility.IsDirty(source), Is.EqualTo(dirty));
        }
        Assert.That(modes, Is.EqualTo(225));
    }

    [Test] public void ExistingAuthoredRowsAndForeignPayloadsSurviveMigration()
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>(); _cleanup.Add(data); data.WeaponType = WeaponType.AutomaticCannon;
        var row = WeaponStatProfile.CreateRow(WeaponType.AutomaticCannon, 7, WeaponUpgradePath.PathB);
        row.Manual.Damage = new WeaponOptionalFloat { Enabled = true, Value = 89 };
        row.Flame.AbilityFreezeDurationSeconds = new WeaponOptionalFloat { Enabled = true, Value = 99 };
        data.StatProfile.Levels.Add(row);
        string original = JsonUtility.ToJson(row);
        Assert.That(WeaponStatMigration.ApplyInheritedRows(data), Is.EqualTo(14));
        Assert.That(JsonUtility.ToJson(row), Is.EqualTo(original));
    }

    [Test] public void UnknownProfileVersionFailsWithoutChanges()
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>(); _cleanup.Add(data); data.StatProfile.Version = 999;
        string before = EditorJsonUtility.ToJson(data);
        Assert.Throws<InvalidOperationException>(() => WeaponStatMigration.DryRun(data));
        Assert.Throws<InvalidOperationException>(() => WeaponStatMigration.ApplyInheritedRows(data));
        Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(before));
    }

    private static IEnumerable<Tuple<int, WeaponUpgradePath>> Keys()
    {
        foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
        for (int level = path == WeaponUpgradePath.None ? 1 : 6; level <= (path == WeaponUpgradePath.None ? 5 : 10); level++)
            yield return Tuple.Create(level, path);
    }
    private static string Observe(WeaponData data, PlayerStats stats, int level, WeaponUpgradePath path)
    {
        var instance = new WeaponInstance { Data = data, Level = level, SelectedPath = path, State = WeaponState.Manual };
        var weapon = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, null, null); weapon.Setup(instance, stats.transform, stats, null);
        return string.Join("\n", weapon.CaptureDiagnostics().Sections.SelectMany(s => s.Values.Select(v => s.Name + "/" + v.Label + "=" + v.Value)));
    }
}
