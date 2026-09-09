using System.Collections.Generic;
using System.Globalization;

/// <summary>On-demand, observation-only diagnostics. Implementations must not simulate combat.</summary>
public interface IWeaponDiagnostics
{
    WeaponDiagnosticsSnapshot CaptureDiagnostics();
}

public readonly struct WeaponDiagnosticValue
{
    public string Label { get; }
    public string Value { get; }
    public WeaponDiagnosticValue(string label, string value) { Label = label; Value = value; }
}

public sealed class WeaponDiagnosticSection
{
    private readonly List<WeaponDiagnosticValue> _values = new();
    public string Name { get; }
    public IReadOnlyList<WeaponDiagnosticValue> Values => _values.AsReadOnly();
    internal WeaponDiagnosticSection(string name) { Name = name; }
    internal WeaponDiagnosticSection Add(string label, string value)
    {
        _values.Add(new WeaponDiagnosticValue(label, value));
        return this;
    }
    internal WeaponDiagnosticSection Add(string label, float value, string unit = "") =>
        Add(label, value.ToString("0.###", CultureInfo.InvariantCulture) + unit);
}

public sealed class WeaponDiagnosticsSnapshot
{
    public IReadOnlyList<WeaponDiagnosticSection> Sections { get; }
    internal WeaponDiagnosticsSnapshot(List<WeaponDiagnosticSection> sections) { Sections = sections.AsReadOnly(); }
}
