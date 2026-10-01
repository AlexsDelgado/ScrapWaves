using System.Collections.Generic;
using System.Linq;

/// <summary>Presentation of the same read-only mode getters that gameplay diagnostics use.</summary>
public sealed class CraftingUpgradePreview
{
    public readonly struct Row
    {
        public readonly string Label, Value;
        public Row(string label, string value) { Label = label; Value = value; }
    }
    public Row[] Rows { get; private set; }
    public string Notice { get; private set; }

    public static CraftingUpgradePreview Build(IWeaponBehaviour behaviour, bool maximum = false)
    {
        if (behaviour is not BasicProjectileWeapon weapon || weapon.Runtime?.Data == null)
            return new CraftingUpgradePreview { Rows = new[] { new Row("Damage", "Unavailable"), new Row("Range", "Unavailable"), new Row("Manual capacity", "Unavailable") }, Notice = "Gameplay preview unavailable for this weapon." };
        string mode = weapon.Runtime.State == WeaponState.Manual ? "Manual" : "Automatic";
        WeaponDiagnosticsSnapshot current = weapon.CaptureDiagnostics();
        WeaponDiagnosticsSnapshot next = weapon.CaptureUpgradeDiagnostics(maximum ? weapon.Runtime.Level : weapon.Runtime.Level + 1);
        var candidates = new[]
        {
            (label: mode == "Manual" ? "Manual damage / hit" : "Auto damage / hit", section: mode, key: "Damage / hit (non-critical)"),
            (label: mode == "Manual" ? "Manual range" : weapon.Runtime.Data.WeaponType == WeaponType.RotatingBlade ? "Auto orbit radius" : "Auto range", section: mode, key: "Range"),
            (label: "Manual capacity", section: "Runtime", key: "Maximum manual ammo"),
            (label: "Actions / ticks per sec", section: mode, key: "Actions / ticks per second"),
            (label: "Hits / projectiles", section: mode, key: "Hits / projectiles per action")
        };
        var changed = new List<Row>(); var unchanged = new List<Row>();
        foreach (var candidate in candidates)
        {
            string before = Read(current, candidate.section, candidate.key), after = Read(next, candidate.section, candidate.key);
            bool differs = before != after;
            string value = maximum ? before : differs ? $"{before} → {after}" : before == "Unavailable" ? before : $"{before} (unchanged)";
            (differs ? changed : unchanged).Add(new Row(candidate.label, value));
        }
        string summary = maximum ? "Maximum level." : changed.Count == 0 ? "No changes to these gameplay stats." : $"{changed.Count} gameplay stat changes.";
        // Changes take priority over unchanged defaults in the authored three-row layout.
        Row[] rows = changed.Concat(unchanged).Take(3).ToArray();
        string additional = changed.Count > 3 ? " Also: " + string.Join("; ", changed.Skip(3).Select(row => row.Label + " " + row.Value)) + "." : string.Empty;
        return new CraftingUpgradePreview { Rows = rows, Notice = summary + additional + "\n" + mode + ": current player stats and heat. No crits, target bonuses or active abilities." };
    }

    private static string Read(WeaponDiagnosticsSnapshot snapshot, string section, string label) =>
        snapshot.Sections.FirstOrDefault(entry => entry.Name == section)?.Values.FirstOrDefault(entry => entry.Label == label).Value ?? "Unavailable";
}
