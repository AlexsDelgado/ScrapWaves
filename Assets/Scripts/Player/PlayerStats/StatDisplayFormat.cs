using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Formateo de stats compartido entre el inspector de debug y la UI de runtime.
/// El cuerpo de <see cref="FormatStat"/> vivía en DebugMonitorEditor; se movió acá para
/// que la vista de atribución pueda reusarlo sin duplicar reglas de unidades.
/// </summary>
public static class StatDisplayFormat
{
    private const string Number = "0.###";

    /// <summary>
    /// Formats an offered bonus, rather than a final stat value. Additive bonuses to
    /// factor/fraction stats use percent units; multiplicative values ARE factors
    /// (1.2 means +20%). Scavenging and DoubleDrop are already percentage points.
    /// This conversion is presentation only and never changes the authored value.
    /// </summary>
    public static string FormatBonus(StatType type, float value,
        StatModifierType modifierType = StatModifierType.Additive)
    {
        if (modifierType == StatModifierType.Multiplicative)
            return FormatBonusAmount((value - 1f) * 100f, "%");

        switch (type)
        {
            case StatType.DamageMultiplier:
            case StatType.EliteDamageMultiplier:
            case StatType.AttackSpeedMultiplier:
            case StatType.ProjectileAreaSize:
            case StatType.CriticalChance:
            case StatType.CriticalDamage:
            case StatType.Knockback:
            case StatType.AmmoMultiplier:
            case StatType.Lifesteal:
            case StatType.DamageResistance:
            case StatType.ExtraEliteChance:
            case StatType.AbilityDamageMultiplier:
            case StatType.AbilityCooldownReduction:
            case StatType.LongRangeDamageMultiplier:
            case StatType.CloseRangeDamageMultiplier:
                return FormatBonusAmount(value * 100f, "%");
            case StatType.Scavenging:
            case StatType.DoubleDrop:
                return FormatBonusAmount(value, "%");
            case StatType.MovementSpeed:
            case StatType.DashSpeed:
                return FormatBonusAmount(value, " m/s");
            case StatType.JumpHeight:
            case StatType.PickupRange:
                return FormatBonusAmount(value, " m");
            case StatType.MaxHealth:
                return FormatBonusAmount(value, " HP");
            case StatType.HealthRegeneration:
                return FormatBonusAmount(value, " HP/s");
            case StatType.BaseFireInterval:
            case StatType.ShieldRechargeDelay:
            case StatType.HealthRegenerationDelayReduction:
                return FormatBonusAmount(value, " s");
            default:
                return FormatBonusAmount(value, string.Empty);
        }
    }

    private static string FormatBonusAmount(float value, string unit)
    {
        double rounded = Math.Round(Math.Abs((double)value), 3, MidpointRounding.AwayFromZero);
        // Do not turn a small, real bonus or penalty into a misleading signed zero.
        string amount = value != 0f && rounded == 0d
            ? "<0.001"
            : rounded.ToString(Number, CultureInfo.InvariantCulture);
        return (value < 0f ? "-" : "+") + amount + unit;
    }

    // Formats a stat value with the unit suffix implied by its type and definition.
    public static string FormatStat(StatDefinition definition, float value)
    {
        if (definition == null)
            return value.ToString(Number, CultureInfo.InvariantCulture);

        StatType type = definition.StatType;
        bool multiplier = type.ToString().EndsWith("Multiplier", StringComparison.Ordinal)
            || type == StatType.CriticalDamage || type == StatType.ProjectileAreaSize || type == StatType.Knockback;
        if (multiplier) return value.ToString(Number, CultureInfo.InvariantCulture) + "x";
        if (type == StatType.Scavenging || type == StatType.DoubleDrop)
            return value.ToString(Number, CultureInfo.InvariantCulture) + "%";
        if (definition.IsPercentage || type == StatType.Lifesteal || type == StatType.DamageResistance
            || type == StatType.ExtraEliteChance)
            return (value * 100f).ToString(Number, CultureInfo.InvariantCulture) + "%";
        if (type == StatType.BaseFireInterval || type == StatType.ShieldRechargeDelay || type == StatType.HealthRegenerationDelayReduction)
            return value.ToString(Number, CultureInfo.InvariantCulture) + " s";
        if (type == StatType.MovementSpeed || type == StatType.DashSpeed)
            return value.ToString(Number, CultureInfo.InvariantCulture) + " m/s";
        if (type == StatType.JumpHeight || type == StatType.PickupRange)
            return value.ToString(Number, CultureInfo.InvariantCulture) + " m";
        if (type == StatType.HealthRegeneration)
            return value.ToString(Number, CultureInfo.InvariantCulture) + " HP/s";
        return value.ToString(definition.IsInteger ? "0" : Number, CultureInfo.InvariantCulture);
    }

    // Formats a raw number without unit, for breakdown columns that share one scale.
    public static string FormatRaw(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    // Formats a delta with an explicit sign, for additive modifier rows.
    public static string FormatSigned(float delta)
        => (delta >= 0f ? "+" : "-") + Mathf.Abs(delta).ToString("0.000", CultureInfo.InvariantCulture);

    // Formats a multiplicative factor as "x1.250".
    public static string FormatMultiplier(float value)
        => "x" + value.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Describe el origen de un modificador: primero el label explícito, luego el nombre
    /// del objeto fuente si es un UnityEngine.Object, y por último la categoría de fuente.
    /// </summary>
    public static string DescribeModifier(StatModifier modifier)
    {
        if (modifier == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(modifier.Label))
            return modifier.Label;

        if (modifier.SourceReference is UnityEngine.Object unityObject && unityObject != null)
            return unityObject.name;

        return modifier.Source.ToString();
    }

    /// <summary>
    /// Límite superior que los consumidores aplican al leer el stat, o -1 si no hay clamp.
    /// Refleja <c>RollCrit</c> (WeaponDamageResolver) y <c>PlayerStatMath.GetFractionStat</c>.
    /// Sirve para mostrar el valor crudo junto al efectivo cuando el stat satura.
    /// </summary>
    public static float GetEffectiveMaximum(StatType statType)
    {
        switch (statType)
        {
            case StatType.CriticalChance: return 1f;      // WeaponDamageResolver.RollCrit -> Mathf.Clamp01
            case StatType.Lifesteal: return 1f;           // PlayerStatMath.CalculateLifestealHeal
            case StatType.DamageResistance: return 0.95f; // PlayerStatMath.ApplyDamageResistance
            case StatType.ExtraEliteChance: return 0.95f; // PlayerStatMath.GetExtraEliteChance
            default: return -1f;
        }
    }

    // True when the raw stat value exceeds the clamp its consumers apply.
    public static bool IsSaturated(StatType statType, float rawValue)
    {
        float max = GetEffectiveMaximum(statType);
        return max >= 0f && rawValue > max;
    }
}
