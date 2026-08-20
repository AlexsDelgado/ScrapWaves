using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameFeelQualityLevel
{
    Low,
    Medium,
    High
}

public enum WeaponFeedbackEvent
{
    ChargeStarted,
    ChargeUpdated,
    ChargeCancelled,
    ShotFired,
    SustainedFireStarted,
    SustainedFireStopped,
    ProjectileImpact,
    DamageConfirmed,
    StatusApplied,
    AmmoEmpty,
    HeatThresholdCrossed
}

public enum WeaponFeedbackMode
{
    Automatic,
    Manual,
    Active
}

public enum ImpactSurfaceType
{
    Default,
    EnemyOrganic,
    EnemyMetal,
    GroundDirt,
    EnvironmentMetal,
    StoneConcrete
}

public enum ProjectilePresentationArchetypeId
{
    Default,
    CannonRound,
    CannonTracer,
    HeadHunterBolt,
    Rocket,
    FragmentRocket,
    ClusterRocket
}

public enum FeedbackFilter
{
    Any,
    Required,
    Excluded
}

public enum WeaponFeedbackModeFilter
{
    Any,
    Automatic,
    Manual,
    Active
}

public enum WeaponUpgradePathFilter
{
    Any,
    Base,
    PathA,
    PathB
}

public enum ImpactSurfaceFilter
{
    Any,
    Default,
    EnemyOrganic,
    EnemyMetal,
    GroundDirt,
    EnvironmentMetal,
    StoneConcrete
}

[Serializable]
public sealed class WeaponFeedbackBinding
{
    [Tooltip("Semantic gameplay feedback event resolved by this authored binding.")]
    public WeaponFeedbackEvent Event;

    [Tooltip("Optional firing-mode filter. More specific matching bindings win.")]
    public WeaponFeedbackModeFilter Mode = WeaponFeedbackModeFilter.Any;

    [Tooltip("Optional upgrade-path filter. Base matches levels before an advanced path is active.")]
    public WeaponUpgradePathFilter UpgradePath = WeaponUpgradePathFilter.Any;

    public FeedbackFilter Critical = FeedbackFilter.Any;
    public FeedbackFilter WeakPoint = FeedbackFilter.Any;
    public FeedbackFilter Kill = FeedbackFilter.Any;
    public ImpactSurfaceFilter Surface = ImpactSurfaceFilter.Any;

    [Tooltip("Authored presentation cue selected when every filter matches.")]
    public WeaponPresentationCue Cue;

    public bool Matches(WeaponFeedbackEvent feedbackEvent, in WeaponFeedbackContext context)
    {
        return Event == feedbackEvent &&
               MatchesMode(context.Mode) &&
               MatchesPath(context.UpgradePath) &&
               MatchesFlag(Critical, context.IsCritical) &&
               MatchesFlag(WeakPoint, context.IsWeakPoint) &&
               MatchesFlag(Kill, context.IsKill) &&
               MatchesSurface(context.SurfaceType);
    }

    public int Specificity
    {
        get
        {
            int value = 0;
            if (Mode != WeaponFeedbackModeFilter.Any) value++;
            if (UpgradePath != WeaponUpgradePathFilter.Any) value++;
            if (Critical != FeedbackFilter.Any) value++;
            if (WeakPoint != FeedbackFilter.Any) value++;
            if (Kill != FeedbackFilter.Any) value++;
            if (Surface != ImpactSurfaceFilter.Any) value++;
            return value;
        }
    }

    private bool MatchesMode(WeaponFeedbackMode mode)
    {
        return Mode == WeaponFeedbackModeFilter.Any ||
               (Mode == WeaponFeedbackModeFilter.Automatic && mode == WeaponFeedbackMode.Automatic) ||
               (Mode == WeaponFeedbackModeFilter.Manual && mode == WeaponFeedbackMode.Manual) ||
               (Mode == WeaponFeedbackModeFilter.Active && mode == WeaponFeedbackMode.Active);
    }

    private bool MatchesPath(WeaponUpgradePath path)
    {
        return UpgradePath == WeaponUpgradePathFilter.Any ||
               (UpgradePath == WeaponUpgradePathFilter.Base && path == WeaponUpgradePath.None) ||
               (UpgradePath == WeaponUpgradePathFilter.PathA && path == WeaponUpgradePath.PathA) ||
               (UpgradePath == WeaponUpgradePathFilter.PathB && path == WeaponUpgradePath.PathB);
    }

    private bool MatchesSurface(ImpactSurfaceType surface)
    {
        return Surface == ImpactSurfaceFilter.Any || (int)Surface - 1 == (int)surface;
    }

    private static bool MatchesFlag(FeedbackFilter filter, bool value)
    {
        return filter == FeedbackFilter.Any ||
               (filter == FeedbackFilter.Required && value) ||
               (filter == FeedbackFilter.Excluded && !value);
    }
}

[Serializable]
public sealed class ProjectileArchetypePresentation
{
    public ProjectilePresentationArchetypeId Archetype = ProjectilePresentationArchetypeId.Default;
    public Mesh Mesh;
    public Material Material;
    public Material TrailMaterial;
    public Material FlightSmokeMaterial;
    public Vector3 LocalScale = Vector3.one;
    public Vector3 LocalEulerAngles;
    [Min(0f)] public float TrailLifetime = 0.08f;
    [Min(0f)] public float TrailStartWidth = 0.08f;
    [Min(0f)] public float TrailEndWidth;
    [Min(0f)] public float LightIntensity;
    [Min(0f)] public float LightRange = 2f;
    public Gradient TrailColor = CreateDefaultTrailGradient();
    [Min(0f)] public float BaseEmission = 1f;
    [Min(0f)] public float FlightSmokeRate;
    [Min(0f)] public float FlightSmokeSize = 0.14f;
    [Min(0f)] public float FlightSmokeLifetime = 0.5f;

    public void Sanitize()
    {
        LocalScale.x = Mathf.Max(0.001f, Mathf.Abs(LocalScale.x));
        LocalScale.y = Mathf.Max(0.001f, Mathf.Abs(LocalScale.y));
        LocalScale.z = Mathf.Max(0.001f, Mathf.Abs(LocalScale.z));
        TrailLifetime = Mathf.Max(0f, TrailLifetime);
        TrailStartWidth = Mathf.Max(0f, TrailStartWidth);
        TrailEndWidth = Mathf.Max(0f, TrailEndWidth);
        LightIntensity = Mathf.Max(0f, LightIntensity);
        LightRange = Mathf.Max(0f, LightRange);
        BaseEmission = Mathf.Max(0f, BaseEmission);
        FlightSmokeRate = Mathf.Max(0f, FlightSmokeRate);
        FlightSmokeSize = Mathf.Max(0f, FlightSmokeSize);
        FlightSmokeLifetime = Mathf.Max(0f, FlightSmokeLifetime);
        TrailColor ??= CreateDefaultTrailGradient();
    }

    private static Gradient CreateDefaultTrailGradient()
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.18f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }
}

[Serializable]
public sealed class WeaponHeatPresentationSettings
{
    public Gradient Color = CreateDefaultHeatGradient();
    public AnimationCurve Emission = AnimationCurve.Linear(0f, 0.75f, 1f, 2.25f);
    public AnimationCurve SmokeRate = AnimationCurve.Linear(0f, 0.35f, 1f, 1.75f);
    public AnimationCurve SparkRate = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
    public AnimationCurve AudioPitch = AnimationCurve.Linear(0f, 0.96f, 1f, 1.08f);
    public AnimationCurve MechanicalStrainVolume = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve TrailWidth = AnimationCurve.Linear(0f, 0.85f, 1f, 1.3f);
    public AnimationCurve CameraVibration = AnimationCurve.Linear(0f, 0.85f, 1f, 1.2f);

    public void Sanitize()
    {
        Color ??= CreateDefaultHeatGradient();
        Emission ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        SmokeRate ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        SparkRate ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        AudioPitch ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        MechanicalStrainVolume ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
        TrailWidth ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
        CameraVibration ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
    }

    private static Gradient CreateDefaultHeatGradient()
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.32f, 0.04f), 0f),
                new GradientColorKey(new Color(1f, 0.68f, 0.12f), 0.75f),
                new GradientColorKey(new Color(1f, 0.95f, 0.72f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        return gradient;
    }
}

[Serializable]
public sealed class WeaponDensitySettings
{
    [Min(1)] public int LowQualitySecondaryLimit = 12;
    [Min(1)] public int MediumQualitySecondaryLimit = 28;
    [Min(1)] public int HighQualitySecondaryLimit = 56;
    [Min(1)] public int DenseCombatThreshold = 40;
    [Min(0.1f)] public float DistantSecondaryCutoff = 28f;

    public int GetSecondaryLimit(GameFeelQualityLevel quality)
    {
        return quality switch
        {
            GameFeelQualityLevel.Low => Mathf.Max(1, LowQualitySecondaryLimit),
            GameFeelQualityLevel.Medium => Mathf.Max(1, MediumQualitySecondaryLimit),
            _ => Mathf.Max(1, HighQualitySecondaryLimit)
        };
    }

    public void Sanitize()
    {
        LowQualitySecondaryLimit = Mathf.Max(1, LowQualitySecondaryLimit);
        MediumQualitySecondaryLimit = Mathf.Max(LowQualitySecondaryLimit, MediumQualitySecondaryLimit);
        HighQualitySecondaryLimit = Mathf.Max(MediumQualitySecondaryLimit, HighQualitySecondaryLimit);
        DenseCombatThreshold = Mathf.Max(1, DenseCombatThreshold);
        DistantSecondaryCutoff = Mathf.Max(0.1f, DistantSecondaryCutoff);
    }
}

[Serializable]
public sealed class AutomaticCannonPresentationSettings
{
    [Header("Projectile readability")]
    [Min(1), Tooltip("Every Nth base active scatter projectile uses the brighter tracer archetype. Line bursts always trace only their final round.")]
    public int BaseTracerFrequency = 3;
    [Min(1), Tooltip("Every Nth Continuous Fire projectile uses the tracer archetype.")]
    public int ContinuousTracerFrequency = 5;

    [Header("Piercing readability")]
    [Min(1), Tooltip("Maximum Head Hunter impacts allowed to request full secondary presentation per shot.")]
    public int MaximumPiercingAccents = 6;

    [Header("Sustained cadence")]
    [Min(0.01f), Tooltip("Grace interval used when aggregating continuous-fire feedback.")]
    public float SustainedFeedbackGrace = 0.14f;

    public void Sanitize()
    {
        BaseTracerFrequency = Mathf.Max(1, BaseTracerFrequency);
        ContinuousTracerFrequency = Mathf.Max(1, ContinuousTracerFrequency);
        MaximumPiercingAccents = Mathf.Max(1, MaximumPiercingAccents);
        SustainedFeedbackGrace = Mathf.Max(0.01f, SustainedFeedbackGrace);
    }
}

[Serializable]
public sealed class FlamethrowerPresentationSettings
{
    [Header("Production prefabs")]
    [Tooltip("Authored procedural ribbon used for both the automatic cone and the manual hose.")]
    public GameObject StreamPrefab;
    [Tooltip("Authored, pooled Jellified Fuel puddle. Damage still uses the gameplay puddle radius.")]
    public GameObject FuelPuddlePrefab;

    [Header("Density")]
    [Range(2, 48), Tooltip("Hard presentation cap; the authoritative hose simulation may provide fewer points.")]
    public int MaximumStreamSegments = 48;
    [Min(0)] public int FuelPuddlePrewarmCount = 8;
    [Min(1)] public int FuelPuddlePoolCapacity = 32;

    public void Sanitize()
    {
        MaximumStreamSegments = Mathf.Clamp(MaximumStreamSegments, 2, 48);
        FuelPuddlePoolCapacity = Mathf.Max(1, FuelPuddlePoolCapacity);
        FuelPuddlePrewarmCount = Mathf.Clamp(FuelPuddlePrewarmCount, 0, FuelPuddlePoolCapacity);
    }
}

[Serializable]
public sealed class RotatingBladePresentationSettings
{
    [Header("Production prefab")]
    [Tooltip("Authored physical blade, persistent trail, slash surface, and thrust ribbon runtime presentation.")]
    public GameObject RuntimeVfxPrefab;

    [Header("Bounded runtime layers")]
    [Range(1, 8)] public int MaximumOrbitingBlades = 6;
    [Range(1, 16)] public int MaximumConcurrentSlashes = 8;
    [Range(1, 16)] public int MaximumConcurrentThrusts = 8;

    public void Sanitize()
    {
        MaximumOrbitingBlades = Mathf.Clamp(MaximumOrbitingBlades, 1, 8);
        MaximumConcurrentSlashes = Mathf.Clamp(MaximumConcurrentSlashes, 1, 16);
        MaximumConcurrentThrusts = Mathf.Clamp(MaximumConcurrentThrusts, 1, 16);
    }
}

[Serializable]
public sealed class MortarPresentationSettings
{
    [Header("Production shell")]
    [Tooltip("Authored shell, short trail, flight smoke, and world-space landing prediction presentation.")]
    public GameObject ShellPrefab;
    [Tooltip("Authored manual-aim landing marker with blast radius, path color, and time-to-impact pulse.")]
    public GameObject LandingIndicatorPrefab;

    [Header("Pooling and density")]
    [Min(0), Tooltip("Shell instances prepared before the first launch while the Mortar is equipped.")]
    public int ShellPrewarmCount = 24;
    [Min(1), Tooltip("Maximum number of inactive authored shells retained by the Mortar pool.")]
    public int ShellPoolCapacity = 128;
    [Min(1), Tooltip("Maximum shells in a dense active rain that receive full smoke, trail, and landing-warning detail.")]
    public int MaximumDetailedRainShells = 14;
    [Min(1), Tooltip("Active-rain shells grouped into one combat-text action sequence.")]
    public int DamageFeedbackSubVolleyShellCount = 5;

    public void Sanitize()
    {
        ShellPoolCapacity = Mathf.Max(1, ShellPoolCapacity);
        ShellPrewarmCount = Mathf.Clamp(ShellPrewarmCount, 0, ShellPoolCapacity);
        MaximumDetailedRainShells = Mathf.Clamp(MaximumDetailedRainShells, 1, ShellPoolCapacity);
        DamageFeedbackSubVolleyShellCount = Mathf.Clamp(DamageFeedbackSubVolleyShellCount, 1, ShellPoolCapacity);
    }
}

[Serializable]
public sealed class GameFeelRuntimeOptions
{
    [Header("Presentation Channels")]
    public bool ProductionPresentationEnabled = true;
    public bool VfxEnabled = true;
    public bool AudioEnabled = true;
    public bool CameraFeedbackEnabled = true;
    public bool HitStopEnabled = true;
    public bool EnemyReactionEnabled = true;
    public bool HeatPresentationEnabled = true;
    public bool DebugGeometryEnabled;

    [Header("Accessibility")]
    [Tooltip("Runtime mirror of the global setting, or a sandbox-only local override.")]
    public bool ReducedMotion;
    [Tooltip("Runtime mirror of the global setting, or a sandbox-only local override.")]
    public bool ReducedShake;
    [Tooltip("Runtime mirror of the global setting, or a sandbox-only local override.")]
    public bool ReducedFlash;
    [Tooltip("Runtime mirror of the global combat-text visibility setting, or a sandbox-only local override.")]
    public CombatTextMode CombatText = CombatTextMode.Full;
    [Range(PresentationAccessibilitySettings.MinimumCombatTextScale, PresentationAccessibilitySettings.MaximumCombatTextScale)]
    [Tooltip("Runtime mirror of the global combat-text scale setting, or a sandbox-only local override.")]
    public float CombatTextScale = 1f;

    [Header("Performance")]
    public GameFeelQualityLevel Quality = GameFeelQualityLevel.High;
}
