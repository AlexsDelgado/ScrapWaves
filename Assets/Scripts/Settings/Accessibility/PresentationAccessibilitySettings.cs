using System;
using UnityEngine;

public enum CombatTextMode
{
    Off,
    ImportantOnly,
    Full
}

/// <summary>
/// Mutable JSON/Inspector representation of the player's global presentation accessibility choices.
/// Runtime systems should consume <see cref="PresentationAccessibilityState"/> instead.
/// </summary>
[Serializable]
public sealed class PresentationAccessibilitySettings
{
    public const float MinimumCombatTextScale = 0.75f;
    public const float MaximumCombatTextScale = 1.25f;

    public bool ReducedMotion;
    public bool ReducedShake;
    public bool ReducedFlash;
    public CombatTextMode CombatText = CombatTextMode.Full;

    [Range(MinimumCombatTextScale, MaximumCombatTextScale)]
    public float CombatTextScale = 1f;

    public PresentationAccessibilitySettings()
    {
    }

    public PresentationAccessibilitySettings(in PresentationAccessibilityState state)
    {
        ReducedMotion = state.ReducedMotion;
        ReducedShake = state.ReducedShake;
        ReducedFlash = state.ReducedFlash;
        CombatText = state.CombatText;
        CombatTextScale = state.CombatTextScale;
        Sanitize();
    }

    public void Sanitize()
    {
        CombatText = SanitizeCombatTextMode(CombatText);
        CombatTextScale = SanitizeCombatTextScale(CombatTextScale);
    }

    public PresentationAccessibilityState ToState() => new(
        ReducedMotion,
        ReducedShake,
        ReducedFlash,
        CombatText,
        CombatTextScale);

    public PresentationAccessibilitySettings CloneSanitized() => new(ToState());

    internal static CombatTextMode SanitizeCombatTextMode(CombatTextMode value)
    {
        return value is CombatTextMode.Off or CombatTextMode.ImportantOnly or CombatTextMode.Full
            ? value
            : CombatTextMode.Full;
    }

    internal static float SanitizeCombatTextScale(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 1f;

        return Mathf.Clamp(value, MinimumCombatTextScale, MaximumCombatTextScale);
    }
}

/// <summary>
/// Sanitized immutable snapshot distributed to presentation systems.
/// </summary>
public readonly struct PresentationAccessibilityState : IEquatable<PresentationAccessibilityState>
{
    public readonly bool ReducedMotion;
    public readonly bool ReducedShake;
    public readonly bool ReducedFlash;
    public readonly CombatTextMode CombatText;
    public readonly float CombatTextScale;

    public static PresentationAccessibilityState Default => new(
        reducedMotion: false,
        reducedShake: false,
        reducedFlash: false,
        combatText: CombatTextMode.Full,
        combatTextScale: 1f);

    public PresentationAccessibilityState(
        bool reducedMotion,
        bool reducedShake,
        bool reducedFlash,
        CombatTextMode combatText,
        float combatTextScale)
    {
        ReducedMotion = reducedMotion;
        ReducedShake = reducedShake;
        ReducedFlash = reducedFlash;
        CombatText = PresentationAccessibilitySettings.SanitizeCombatTextMode(combatText);
        CombatTextScale = PresentationAccessibilitySettings.SanitizeCombatTextScale(combatTextScale);
    }

    public PresentationAccessibilityState WithReducedMotion(bool value) => new(
        value, ReducedShake, ReducedFlash, CombatText, CombatTextScale);

    public PresentationAccessibilityState WithReducedShake(bool value) => new(
        ReducedMotion, value, ReducedFlash, CombatText, CombatTextScale);

    public PresentationAccessibilityState WithReducedFlash(bool value) => new(
        ReducedMotion, ReducedShake, value, CombatText, CombatTextScale);

    public PresentationAccessibilityState WithCombatText(CombatTextMode value) => new(
        ReducedMotion, ReducedShake, ReducedFlash, value, CombatTextScale);

    public PresentationAccessibilityState WithCombatTextScale(float value) => new(
        ReducedMotion, ReducedShake, ReducedFlash, CombatText, value);

    public bool Equals(PresentationAccessibilityState other)
    {
        return ReducedMotion == other.ReducedMotion &&
               ReducedShake == other.ReducedShake &&
               ReducedFlash == other.ReducedFlash &&
               CombatText == other.CombatText &&
               CombatTextScale.Equals(other.CombatTextScale);
    }

    public override bool Equals(object obj) =>
        obj is PresentationAccessibilityState other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ReducedMotion ? 1 : 0;
            hash = (hash * 397) ^ (ReducedShake ? 1 : 0);
            hash = (hash * 397) ^ (ReducedFlash ? 1 : 0);
            hash = (hash * 397) ^ (int)CombatText;
            hash = (hash * 397) ^ CombatTextScale.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(PresentationAccessibilityState left, PresentationAccessibilityState right) =>
        left.Equals(right);

    public static bool operator !=(PresentationAccessibilityState left, PresentationAccessibilityState right) =>
        !left.Equals(right);
}
