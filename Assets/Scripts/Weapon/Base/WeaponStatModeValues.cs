using System;

public enum WeaponStatMode { Automatic, Manual, Ability }
public enum WeaponStatField { Damage, RangeMeters, AttackCyclesPerSecond, AmmoCapacity, KnockbackStrength, ExplosionRadiusMeters }

[Serializable]
public struct WeaponOptionalInt
{
    public bool Enabled;
    public int Value;
}

/// <summary>Absolute neutral weapon values for one mode, before player and heat modifiers.</summary>
[Serializable]
public sealed class WeaponStatModeValues
{
    public WeaponOptionalFloat Damage;
    public WeaponOptionalFloat RangeMeters;
    public WeaponOptionalFloat AttackCyclesPerSecond;
    public WeaponOptionalFloat AmmoCapacity;
    public WeaponOptionalFloat KnockbackStrength;
    public WeaponOptionalFloat ExplosionRadiusMeters;

    public WeaponOptionalFloat Get(WeaponStatField field) => field switch
    {
        WeaponStatField.Damage => Damage,
        WeaponStatField.RangeMeters => RangeMeters,
        WeaponStatField.AttackCyclesPerSecond => AttackCyclesPerSecond,
        WeaponStatField.AmmoCapacity => AmmoCapacity,
        WeaponStatField.KnockbackStrength => KnockbackStrength,
        WeaponStatField.ExplosionRadiusMeters => ExplosionRadiusMeters,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}

public readonly struct WeaponResolvedInt
{
    public readonly int Value;
    public readonly WeaponStatValueSource Source;
    public bool IsAuthored => Source != WeaponStatValueSource.Legacy;
    public WeaponResolvedInt(int value, WeaponStatValueSource source) { Value = value; Source = source; }
}