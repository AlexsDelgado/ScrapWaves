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
    public Row[] Changes { get; private set; }
    public string Notice { get; private set; }

    public static CraftingUpgradePreview Build(IWeaponBehaviour behaviour, bool maximum = false) =>
        BuildForTarget(behaviour, maximum, null, null);

    public static CraftingUpgradePreview BuildAdvanced(IWeaponBehaviour behaviour, WeaponUpgradePath path) =>
        path == WeaponUpgradePath.PathA || path == WeaponUpgradePath.PathB
            ? BuildForTarget(behaviour, false, 6, path)
            : Unavailable();

    private static CraftingUpgradePreview BuildForTarget(IWeaponBehaviour behaviour, bool maximum,
        int? targetLevel, WeaponUpgradePath? targetPath)
    {
        if (behaviour is not BasicProjectileWeapon weapon || weapon.Runtime?.Data == null) return Unavailable();
        string mode = weapon.Runtime.State == WeaponState.Manual ? "Manual" : "Automatic";
        WeaponDiagnosticsSnapshot current = weapon.CaptureDiagnostics();
        WeaponDiagnosticsSnapshot next = weapon.CaptureUpgradeDiagnostics(
            targetLevel ?? (maximum ? weapon.Runtime.Level : weapon.Runtime.Level + 1), targetPath);
        var candidates = new[]
        {
            (label: mode == "Manual" ? "Manual damage / hit" : "Auto damage / hit", section: mode, key: "Damage / hit (non-critical)"),
            (label: mode == "Manual" ? "Manual range" : weapon.Runtime.Data.WeaponType == WeaponType.RotatingBlade ? "Auto orbit radius" : "Auto range", section: mode, key: "Range"),
            (label: "Manual capacity", section: "Runtime", key: "Maximum manual ammo"),
            (label: "Actions / ticks per sec", section: mode, key: "Actions / ticks per second"),
            (label: "Hits / projectiles", section: mode, key: "Hits / projectiles per action")
        };
        var changed = new List<Row>();
        var unchanged = new List<Row>();
        var visited = new HashSet<string>();
        foreach (var candidate in candidates)
        {
            visited.Add(candidate.section + "|" + candidate.key);
            string before = Read(current, candidate.section, candidate.key), after = Read(next, candidate.section, candidate.key);
            bool differs = before != after;
            string value = maximum ? before : differs ? $"{before} → {after}"
                : before == "Unavailable" ? before : $"{before} (unchanged)";
            (differs ? changed : unchanged).Add(new Row(candidate.label, value));
        }
        if (!maximum)
        {
            // Observe all modes, including Q and unique geometry/status/count controls.
            // Provenance strings explain sources, but are not gameplay changes themselves.
            foreach (string section in new[] { mode, mode == "Manual" ? "Automatic" : "Manual", "Active Ability" })
            {
                IEnumerable<string> labels = Values(current, section).Select(value => value.Label)
                    .Concat(Values(next, section).Select(value => value.Label)).Distinct();
                foreach (string label in labels)
                {
                    if (!visited.Add(section + "|" + label) || label.EndsWith(" source")) continue;
                    string before = Read(current, section, label), after = Read(next, section, label);
                    if (before == after) continue;
                    string prefix = section == "Active Ability" ? "Q" : section == "Automatic" ? "Auto" : "Manual";
                    changed.Add(new Row(prefix + " " + label, $"{before} → {after}"));
                }
            }
        }
        string summary = maximum ? "Maximum level." : changed.Count == 0 ? "No changes to these gameplay stats."
            : $"{changed.Count} gameplay stat changes.";
        Row[] rows = changed.Concat(unchanged).Take(3)
            .Select(row => new Row(ShortLabel(row.Label), row.Value)).ToArray();
        string additional = changed.Count > 3 ? $" Showing 3 of {changed.Count}." : string.Empty;
        return new CraftingUpgradePreview
        {
            Rows = rows, Changes = changed.ToArray(),
            Notice = summary + additional + "\n" + mode
                + ": current player stats and heat. Changes also include other modes and Q."
        };
    }

    public static string DescribeAdvanced(IWeaponBehaviour behaviour, WeaponUpgradePath path, string authoredDescription = null)
    {
        CraftingUpgradePreview preview = BuildAdvanced(behaviour, path);
        if (preview.Changes.Length == 0)
            return (string.IsNullOrWhiteSpace(authoredDescription) ? "No changes to these gameplay stats."
                : LimitLine(authoredDescription, 70)) + "\nCurrent player stats and heat; all modes and Q checked.";
        Row primary = preview.Changes[0];
        Row ability = preview.Changes.FirstOrDefault(row => row.Label == "Q Ability cooldown duration");
        if (ability.Label == null) ability = preview.Changes.FirstOrDefault(row => row.Label.StartsWith("Q ") && row.Label != primary.Label);
        Row secondary = ability.Label != null ? ability : preview.Changes.Length > 1 ? preview.Changes[1] : default;
        string highlights = Compact(primary, 33);
        int shown = 1;
        if (secondary.Label != null && secondary.Label != primary.Label)
        {
            highlights += " · " + Compact(secondary, 33);
            shown++;
        }
        int other = preview.Changes.Length - shown;
        string remainder = other > 0 ? $"{other} further stat changes. " : string.Empty;
        // The authored advanced description is 1084 x 72 at 25 px: two bounded
        // lines. Keep every comparison in Changes, without overflowing that text.
        return string.IsNullOrWhiteSpace(authoredDescription)
            ? highlights + "\n" + remainder + "Current player stats and heat."
            : LimitLine(authoredDescription, 70) + "\n" + LimitLine(highlights + (other > 0 ? $" (+{other} more)" : string.Empty), 70);
    }

    private static string ShortLabel(string label) => label
        .Replace("Damage / hit (non-critical)", "damage / hit")
        .Replace("Damage / hit (critical)", "crit damage / hit")
        .Replace("Damage / hit (elite, non-critical)", "elite damage / hit")
        .Replace("Damage / hit (boss, non-critical)", "boss damage / hit")
        .Replace("Ability cooldown duration", "cooldown")
        .Replace("Vulnerability incoming damage multiplier", "incoming damage")
        .Replace("Fragment cone full angle", "fragment angle")
        .Replace("Hits / projectiles per action", "hits / action");

    private static string Compact(Row row, int maximum)
    {
        string label = ShortLabel(row.Label), suffix = ": " + row.Value;
        // Preserve complete numeric values and units; shorten only their label.
        return suffix.Length < maximum - 4
            ? LimitLine(label, maximum - suffix.Length) + suffix
            : LimitLine(label, maximum);
    }

    private static string LimitLine(string value, int maximum)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= maximum ? value : value.Substring(0, maximum - 1).TrimEnd() + "…";
    }

    private static CraftingUpgradePreview Unavailable() => new()
    {
        Rows = new[] { new Row("Damage", "Unavailable"), new Row("Range", "Unavailable"), new Row("Manual capacity", "Unavailable") },
        Changes = System.Array.Empty<Row>(), Notice = "Gameplay preview unavailable for this weapon."
    };

    private static IEnumerable<WeaponDiagnosticValue> Values(WeaponDiagnosticsSnapshot snapshot, string section) =>
        snapshot.Sections.FirstOrDefault(entry => entry.Name == section)?.Values ?? System.Array.Empty<WeaponDiagnosticValue>();

    private static string Read(WeaponDiagnosticsSnapshot snapshot, string section, string label) =>
        Values(snapshot, section).FirstOrDefault(entry => entry.Label == label).Value ?? "Unavailable";
}
