using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

public class WeaponDiagnosticsTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void Cleanup()
    {
        foreach (Object obj in _cleanup) Object.DestroyImmediate(obj);
        _cleanup.Clear();
    }

    [Test]
    public void AllWeapons_AllPathsAndModes_ObservationPreservesRandomnessAndState()
    {
        PlayerStats stats = CreateStats();
        var heat = stats.gameObject.AddComponent<HeatManager>();
        int events = 0;
        Action<WeaponDamageRoll> listener = _ => events++;
        WeaponDamageResolver.OnDamageResolved += listener;
        Random.State originalRandom = Random.state;
        try
        {
            foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
            foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
            foreach (WeaponState state in Enum.GetValues(typeof(WeaponState)))
            foreach (float heatPoints in new[] { 0f, heat.TotalHeatCapacity })
            {
                SetField(heat, "_currentHeat", heatPoints);
                BasicProjectileWeapon weapon = CreateWeapon(type, stats, heat);
                weapon.Runtime.Level = path == WeaponUpgradePath.None ? 1 : 10;
                weapon.Runtime.SelectedPath = path;
                weapon.Runtime.State = state;
                weapon.Runtime.CurrentAmmo = 37f;
                weapon.Runtime.AbilityCooldownTimer = 0.75f;
                string before = JsonUtility.ToJson(weapon.Runtime);
                string assetBefore = EditorJsonUtility.ToJson(weapon.Runtime.Data);
                Random.State randomBefore = Random.state;
                WeaponDiagnosticsSnapshot snapshot = weapon.CaptureDiagnostics();
                Assert.That(snapshot.Sections.Select(s => s.Name), Is.EqualTo(new[] { "Runtime", "Automatic", "Manual", "Active Ability" }), type.ToString());
                foreach (string mode in new[] { "Automatic", "Manual", "Active Ability" })
                    Assert.That(Number(snapshot, mode, "Damage / hit (non-critical)"), Is.GreaterThanOrEqualTo(1), $"{type}/{path}/{mode}");
                Assert.That(Random.state, Is.EqualTo(randomBefore));
                Assert.That(JsonUtility.ToJson(weapon.Runtime), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(weapon.Runtime.Data), Is.EqualTo(assetBefore));
            }
            Assert.That(events, Is.Zero);
        }
        finally { Random.state = originalRandom; WeaponDamageResolver.OnDamageResolved -= listener; }
    }

    [Test]
    public void DamagePreviewMatchesResolver_AndReflectsStatAmmoAndCooldownChanges()
    {
        PlayerStats stats = CreateStats();
        BasicProjectileWeapon weapon = CreateWeapon(WeaponType.RocketLauncher, stats);
        var expected = new WeaponDamageContext(stats, weapon.Runtime, false, 1f, 1f, false, 1f);
        Assert.That(Number(weapon.CaptureDiagnostics(), "Manual", "Damage / hit (non-critical)"), Is.EqualTo(expected.EstimateDamage(false)));
        stats.AddModifier(new StatModifier(StatType.DamageMultiplier, 1f, StatUpgradeSource.LevelUp));
        stats.AddModifier(new StatModifier(StatType.AmmoMultiplier, 1f, StatUpgradeSource.LevelUp));
        stats.AddModifier(new StatModifier(StatType.AbilityCooldownReduction, 0.5f, StatUpgradeSource.LevelUp));
        var snapshot = weapon.CaptureDiagnostics();
        Assert.That(Number(snapshot, "Manual", "Damage / hit (non-critical)"), Is.EqualTo(20));
        Assert.That(Number(snapshot, "Runtime", "Maximum manual ammo"), Is.EqualTo(200));
        Assert.That(Number(snapshot, "Active Ability", "Ability cooldown duration"), Is.EqualTo(0.5f));
    }

    [Test]
    public void SpecializedTimingUsesGameplayGetters()
    {
        PlayerStats stats = CreateStats();
        var heat = stats.gameObject.AddComponent<HeatManager>();
        foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
        foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
        foreach (float points in new[] { 0f, heat.TotalHeatCapacity })
        {
            SetField(heat, "_currentHeat", points);
            BasicProjectileWeapon weapon = CreateWeapon(type, stats, heat);
            weapon.Runtime.Level = 10;
            weapon.Runtime.SelectedPath = path;
            var snapshot = weapon.CaptureDiagnostics();
            object tuning = type switch
            {
                WeaponType.AutomaticCannon => weapon.Runtime.Data.AutomaticCannon,
                WeaponType.Flamethrower => weapon.Runtime.Data.Flamethrower,
                WeaponType.RotatingBlade => weapon.Runtime.Data.RotatingBlade,
                _ => null
            };
            string method = type switch
            {
                WeaponType.AutomaticCannon => "GetAutomaticFireInterval",
                WeaponType.Flamethrower => "GetAutomaticTickInterval",
                WeaponType.RotatingBlade => "GetAutoDamageInterval",
                _ => "GetFireInterval"
            };
            var getter = weapon.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            float expected = (float)getter.Invoke(weapon, tuning == null ? null : new[] { tuning });
            Assert.That(Number(snapshot, "Automatic", "Action / tick interval"), Is.EqualTo(expected).Within(0.00051f), $"{type}/{path}");
        }
    }

    [Test]
    public void PreviewIncludesCannonCriticalOverrideAndMortarAbilityKnockbackScaling()
    {
        PlayerStats stats = CreateStats();
        var cannon = CreateWeapon(WeaponType.AutomaticCannon, stats);
        Assert.That(Number(cannon.CaptureDiagnostics(), "Manual", "Damage / hit (critical)"), Is.EqualTo(40));
        cannon.Runtime.Level = 6;
        cannon.Runtime.SelectedPath = WeaponUpgradePath.PathB;
        Assert.That(Number(cannon.CaptureDiagnostics(), "Manual", "Damage / hit (elite, non-critical)"), Is.EqualTo(20));
        Assert.That(Number(cannon.CaptureDiagnostics(), "Manual", "Damage / hit (boss, non-critical)"), Is.EqualTo(30));
        var mortar = CreateWeapon(WeaponType.Mortar, stats);
        float scale = mortar.Runtime.Data.Mortar.MortarActiveDamageScale;
        var context = new WeaponDamageContext(stats, mortar.Runtime, false, 1f, scale, true, scale);
        Assert.That(Number(mortar.CaptureDiagnostics(), "Active Ability", "Knockback (non-critical hit)"),
            Is.EqualTo(context.CalculateKnockback(context.EstimateDamage(false))).Within(0.00051f));
    }

    [Test]
    public void UninitializedAndMissingStats_AreSafe()
    {
        Assert.That(new BasicProjectileWeapon(null, null, null).CaptureDiagnostics().Sections[0].Name, Is.EqualTo("Status"));
        foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
            Assert.DoesNotThrow(() => CreateWeapon(type, null).CaptureDiagnostics());
    }

    [Test]
    public void BaseFlameAutomaticHasNoBurnOrKnockback_UpgradesAddStatus()
    {
        var weapon = CreateWeapon(WeaponType.Flamethrower, CreateStats());
        var automatic = weapon.CaptureDiagnostics().Sections.Single(s => s.Name == "Automatic");
        Assert.That(automatic.Values.Any(v => v.Label.StartsWith("Burn damage")), Is.False);
        Assert.That(Number(weapon.CaptureDiagnostics(), "Automatic", "Knockback (non-critical hit)"), Is.Zero);
        weapon.Runtime.Level = 6;
        weapon.Runtime.SelectedPath = WeaponUpgradePath.PathA;
        Assert.That(Number(weapon.CaptureDiagnostics(), "Automatic", "Burn damage / tick (non-critical)"), Is.EqualTo(7));
    }

    [TestCase(StatType.Lifesteal, 0.15f, "15%")]
    [TestCase(StatType.Scavenging, 50f, "50%")]
    [TestCase(StatType.DamageMultiplier, 1.5f, "1.5x")]
    [TestCase(StatType.CriticalDamage, 2.5f, "2.5x")]
    [TestCase(StatType.MovementSpeed, 5f, "5 m/s")]
    public void InspectorFormatsStatUnits(StatType type, float value, string expected)
    {
        var stats = CreateStats();
        Assert.That(DebugMonitorEditor.FormatStat(stats.GetDefinition(type), value), Is.EqualTo(expected));
    }

    [Test]
    public void InspectorCaptureTracksManualSelectionAndEquipmentClearingAtZeroTimeScale()
    {
        PlayerStats stats = CreateStats();
        var manager = stats.gameObject.AddComponent<WeaponManager>();
        var monitor = stats.gameObject.AddComponent<DebugMonitor>();
        var equipped = (List<IWeaponBehaviour>)manager.GetEquippedWeapons();
        equipped.Add(CreateWeapon(WeaponType.Mortar, stats));
        equipped.Add(CreateWeapon(WeaponType.RocketLauncher, stats));
        var editor = Editor.CreateEditor(monitor);
        _cleanup.Insert(0, editor);
        var capture = editor.GetType().GetMethod("Capture", BindingFlags.Instance | BindingFlags.NonPublic);
        var viewsField = editor.GetType().GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        float oldTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            capture.Invoke(editor, new object[] { monitor });
            var views = (System.Collections.IList)viewsField.GetValue(editor);
            Assert.That(views.Count, Is.EqualTo(2));
            Assert.That(views[0].GetType().GetField("Manual").GetValue(views[0]), Is.True);
            SetField(manager, "_currentManualIndex", 1);
            capture.Invoke(editor, new object[] { monitor });
            Assert.That(views[0].GetType().GetField("Manual").GetValue(views[0]), Is.False);
            Assert.That(views[1].GetType().GetField("Manual").GetValue(views[1]), Is.True);
            equipped.Clear();
            capture.Invoke(editor, new object[] { monitor });
            Assert.That(views.Count, Is.Zero);
        }
        finally { Time.timeScale = oldTimeScale; }
    }

    [Test]
    public void PlayerPrefabAlreadyHasMonitor_AndOverlayIsDisabled()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab.GetComponents<DebugMonitor>().Length, Is.EqualTo(1));
        using var serialized = new SerializedObject(prefab.GetComponent<DebugMonitor>());
        Assert.That(serialized.FindProperty("_showOverlay").boolValue, Is.False);
    }

    private BasicProjectileWeapon CreateWeapon(WeaponType type, PlayerStats stats, HeatManager heat = null)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(data);
        data.WeaponType = type;
        data.EnsureSpecificTuningForCurrentType();
        var weapon = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, null, null);
        weapon.Setup(new WeaponInstance { Data = data }, stats != null ? stats.transform : null, stats, heat);
        return weapon;
    }

    private PlayerStats CreateStats()
    {
        var owner = new GameObject("DiagnosticTest");
        _cleanup.Add(owner);
        var stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();
            _cleanup.Add(definition);
            SetField(definition, "<StatType>k__BackingField", type);
            float value = type == StatType.AbilityCooldownReduction ? 0f : type == StatType.CriticalDamage ? 2f : 1f;
            SetField(definition, "<BaseValue>k__BackingField", value);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
        return stats;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static float Number(WeaponDiagnosticsSnapshot snapshot, string section, string label) =>
        float.Parse(snapshot.Sections.Single(s => s.Name == section).Values.Single(v => v.Label == label).Value.Split(' ')[0], CultureInfo.InvariantCulture);
}
