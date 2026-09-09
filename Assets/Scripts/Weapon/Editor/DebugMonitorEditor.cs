using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DebugMonitor))]
public sealed class DebugMonitorEditor : Editor
{
    private const double RefreshInterval = 0.2;
    private readonly Dictionary<string, bool> _foldouts = new();
    private readonly List<WeaponView> _weapons = new();
    private readonly List<(string category, string name, string value)> _player = new();
    private double _nextRefresh;
    private string _managerStatus;
    private bool _settings;

    private sealed class WeaponView
    {
        public string Key;
        public string Name;
        public bool Manual;
        public WeaponData Data;
        public WeaponDiagnosticsSnapshot Snapshot;
    }

    private void OnEnable() => EditorApplication.update += Tick;
    private void OnDisable() => EditorApplication.update -= Tick;
    private void Tick()
    {
        if (Application.isPlaying && EditorApplication.timeSinceStartup >= _nextRefresh)
            Repaint();
    }

    public override void OnInspectorGUI()
    {
        if (!Application.isPlaying || EditorUtility.IsPersistent(target))
        {
            EditorGUILayout.HelpBox("Enter Play Mode and select the player instance in the Hierarchy to inspect live balancing stats.", MessageType.Info);
            _nextRefresh = 0;
        }
        else
        {
            if (Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup >= _nextRefresh)
            {
                Capture((DebugMonitor)target);
                _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            }
            EditorGUILayout.LabelField("Live Balancing Stats", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Read-only • refreshes 5 times/second, including while paused. Damage is per hit at full strength, without distance bonuses, target defenses, weak points or secondary effects unless stated. Timing is the action/tick interval; bursts, input, target availability and ammo can limit throughput.", MessageType.None);
            if (Foldout("player", "Player"))
            {
                string previous = null;
                foreach (var row in _player)
                {
                    if (row.category != previous) EditorGUILayout.LabelField(row.category, EditorStyles.boldLabel);
                    Row(row.name, row.value);
                    previous = row.category;
                }
            }
            if (!string.IsNullOrEmpty(_managerStatus)) EditorGUILayout.HelpBox(_managerStatus, MessageType.None);
            DrawWeapons(true, "Manual Weapon");
            DrawWeapons(false, "Automatic Weapons");
        }
        _settings = EditorGUILayout.Foldout(_settings, "Existing Debug Settings", true);
        if (_settings) DrawDefaultInspector();
    }

    private void Capture(DebugMonitor monitor)
    {
        _player.Clear();
        _weapons.Clear();
        PlayerHealth health = monitor.GetComponent<PlayerHealth>();
        PlayerXP xp = monitor.GetComponent<PlayerXP>();
        if (health != null)
        {
            _player.Add(("Runtime", "Health", $"{health.CurrentHealth} / {health.MaxHealth}"));
            _player.Add(("Runtime", "Shields", $"{health.ShieldCharges} / {health.MaxShieldCharges}"));
        }
        if (xp != null)
        {
            _player.Add(("Runtime", "Level", xp.CurrentLevel.ToString()));
            _player.Add(("Runtime", "XP", xp.IsAtLevelCap ? "Level cap reached" : $"{xp.XpTowardsNext} / {xp.XpRequiredForCurrentLevel}"));
        }
        PlayerStats stats = monitor.MonitoredStats;
        if (stats == null) _player.Add(("Stats", "Status", "PlayerStats component missing"));
        else
        {
            var seen = new HashSet<StatType>();
            foreach (StatCategory category in Enum.GetValues(typeof(StatCategory)))
                foreach (StatDefinition definition in stats.GetAllDefinitions())
                {
                    if (definition == null || definition.Category != category || !seen.Add(definition.StatType)
                        || stats.GetDefinition(definition.StatType) == null) continue;
                    _player.Add((category.ToString(), ObjectNames.NicifyVariableName(definition.StatType.ToString()),
                        FormatStat(definition, stats.GetStat(definition.StatType))));
                }
            if (seen.Count == 0) _player.Add(("Stats", "Status", "No initialized stats"));
        }
        WeaponManager manager = monitor.MonitoredWeapons;
        _managerStatus = manager == null ? "WeaponManager component missing" :
            $"Equipped: {manager.GetEquippedWeapons().Count} / {WeaponManager.MaxWeaponSlots} • Cycle cooldown: {manager.GetManualCooldownRemaining():0.###} s";
        if (manager == null) return;
        int index = 0;
        foreach (IWeaponBehaviour weapon in manager.GetEquippedWeapons())
        {
            WeaponInstance runtime = weapon?.Runtime;
            _weapons.Add(new WeaponView
            {
                Key = "slot" + index++, Name = runtime?.Data != null ? runtime.Data.DisplayName : "Uninitialized weapon",
                Manual = runtime != null && ReferenceEquals(runtime, manager.GetCurrentManualWeapon()),
                Data = runtime?.Data, Snapshot = (weapon as IWeaponDiagnostics)?.CaptureDiagnostics()
            });
        }
    }

    private void DrawWeapons(bool manual, string title)
    {
        if (!Foldout(title, title)) return;
        bool found = false;
        foreach (WeaponView weapon in _weapons)
        {
            if (weapon.Manual != manual) continue;
            found = true;
            if (!Foldout(weapon.Key, weapon.Name)) continue;
            EditorGUI.indentLevel++;
            if (weapon.Snapshot == null) Row("Status", "Diagnostics unavailable");
            else foreach (WeaponDiagnosticSection section in weapon.Snapshot.Sections)
            {
                if (!Foldout(weapon.Key + section.Name, section.Name)) continue;
                foreach (WeaponDiagnosticValue value in section.Values) Row(value.Label, value.Value);
            }
            if (weapon.Data != null && Foldout(weapon.Key + "tuning", "Configured Tuning (before runtime scaling)", false))
                DrawTuning(weapon.Data);
            EditorGUI.indentLevel--;
        }
        if (!found) EditorGUILayout.LabelField("None equipped");
    }

    private static void DrawTuning(WeaponData data)
    {
        // Inspect serialized fields directly: never create/repair tuning on the source asset.
        using (var serialized = new SerializedObject(data))
        using (new EditorGUI.DisabledScope(true))
        {
            SerializedProperty iterator = serialized.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.name == "m_Script") continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }

    private bool Foldout(string key, string title, bool defaultOpen = true)
    {
        bool open = _foldouts.TryGetValue(key, out bool value) ? value : defaultOpen;
        return _foldouts[key] = EditorGUILayout.Foldout(open, title, true);
    }

    private static void Row(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel, GUILayout.Width(Mathf.Max(120f, EditorGUIUtility.currentViewWidth * 0.46f)));
        EditorGUILayout.LabelField(value, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndHorizontal();
    }

    public static string FormatStat(StatDefinition definition, float value)
    {
        StatType type = definition.StatType;
        bool multiplier = type.ToString().EndsWith("Multiplier", StringComparison.Ordinal)
            || type == StatType.CriticalDamage || type == StatType.ProjectileAreaSize || type == StatType.Knockback;
        if (multiplier) return value.ToString("0.###", CultureInfo.InvariantCulture) + "x";
        if (type == StatType.Scavenging || type == StatType.DoubleDrop)
            return value.ToString("0.###", CultureInfo.InvariantCulture) + "%";
        if (definition.IsPercentage || type == StatType.Lifesteal || type == StatType.DamageResistance
            || type == StatType.ExtraEliteChance)
            return (value * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%";
        if (type == StatType.BaseFireInterval || type == StatType.ShieldRechargeDelay || type == StatType.HealthRegenerationDelayReduction)
            return value.ToString("0.###", CultureInfo.InvariantCulture) + " s";
        if (type == StatType.MovementSpeed || type == StatType.DashSpeed)
            return value.ToString("0.###", CultureInfo.InvariantCulture) + " m/s";
        if (type == StatType.JumpHeight || type == StatType.PickupRange)
            return value.ToString("0.###", CultureInfo.InvariantCulture) + " m";
        if (type == StatType.HealthRegeneration)
            return value.ToString("0.###", CultureInfo.InvariantCulture) + " HP/s";
        return value.ToString(definition.IsInteger ? "0" : "0.###", CultureInfo.InvariantCulture);
    }
}
