using System;

public enum HeatPresentationPhase { Normal, Warning, Overheated, Cooling }

/// <summary>Presentation only: input is the displayed gauge, never the linear heat ratio.</summary>
public sealed class HeatPresentationState
{
    public const float WarningThreshold = .8f;
    public const float WarningRearmThreshold = .75f;

    public float Heat { get; private set; }
    public HeatPresentationPhase Phase { get; private set; }
    private bool _initialized;
    private bool _warningArmed = true;

    // Returns one notification per upward crossing. Cooling and initial binding are silent.
    public bool Read(float displayedHeat, bool overheated, bool cooling, bool notify = true)
    {
        Heat = float.IsNaN(displayedHeat) || float.IsInfinity(displayedHeat)
            ? 0f : Math.Max(0f, Math.Min(1f, displayedHeat));
        if (overheated) Heat = 1f;
        Phase = overheated ? HeatPresentationPhase.Overheated
            : cooling && Heat > 0f ? HeatPresentationPhase.Cooling
            : Heat >= WarningThreshold ? HeatPresentationPhase.Warning
            : HeatPresentationPhase.Normal;

        if (Heat <= WarningRearmThreshold) _warningArmed = true;
        bool warning = _initialized && notify && _warningArmed && Phase == HeatPresentationPhase.Warning;
        if (Heat >= WarningThreshold || overheated || cooling) _warningArmed = false;
        _initialized = true;
        return warning;
    }

    public void Reset()
    {
        Heat = 0f;
        Phase = HeatPresentationPhase.Normal;
        _initialized = false;
        _warningArmed = true;
    }
}
