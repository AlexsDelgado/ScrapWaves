using System;
using TMPro;
using UnityEngine;

[Serializable]
public sealed class CombatTextStyleDefinition
{
    public Color TextColor = new(1f, 0.93f, 0.78f, 1f);
    public Color AccentColor = new(1f, 0.65f, 0.18f, 0.92f);
    [Range(8f, 128f)] public float FontSize = 34f;
    [Range(0.5f, 1.5f)] public float BaseScale = 1f;
    public FontStyles FontStyle = FontStyles.Bold;
    public Material SharedMaterial;

    public void Sanitize()
    {
        FontSize = Mathf.Clamp(FontSize, 8f, 128f);
        BaseScale = Mathf.Clamp(BaseScale, 0.5f, 1.5f);
        TextColor.a = Mathf.Clamp01(TextColor.a);
        AccentColor.a = Mathf.Clamp01(AccentColor.a);
    }
}

[Serializable]
public sealed class CombatTextMotionSettings
{
    [Min(0.05f)] public float Lifetime = 0.78f;
    [Min(0f)] public float ConnectionDuration = 0.10f;
    [Min(0f)] public float HorizontalSpeed = 20f;
    [Min(0f)] public float UpwardSpeed = 120f;
    [Min(0f)] public float DownwardAcceleration = 255f;
    [Range(0.1f, 1f)] public float SpawnScale = 0.60f;
    [Range(1f, 1.5f)] public float PopOvershoot = 1.15f;
    [Min(0.01f)] public float SettleTime = 0.14f;
    [Range(0f, 1f)] public float FadeStartNormalized = 0.64f;
    [Range(0.5f, 1f)] public float EndScaleMultiplier = 0.93f;
    [Min(0f)] public float InitialJitterX = 8f;
    [Min(0f)] public float InitialJitterY = 5f;
    [Min(0f)] public float LocalShakeAmplitude = 3f;
    [Min(0f)] public float LocalShakeDuration = 0.13f;
    public AnimationCurve ScaleOverLife = CreateScaleCurve();
    public AnimationCurve AlphaOverLife = CreateAlphaCurve();

    public void Sanitize()
    {
        Lifetime = Mathf.Max(0.05f, Lifetime);
        ConnectionDuration = Mathf.Clamp(ConnectionDuration, 0f, Lifetime);
        HorizontalSpeed = Mathf.Max(0f, HorizontalSpeed);
        UpwardSpeed = Mathf.Max(0f, UpwardSpeed);
        DownwardAcceleration = Mathf.Max(0f, DownwardAcceleration);
        SpawnScale = Mathf.Clamp(SpawnScale, 0.1f, 1f);
        PopOvershoot = Mathf.Clamp(PopOvershoot, 1f, 1.5f);
        SettleTime = Mathf.Clamp(SettleTime, 0.01f, Lifetime);
        FadeStartNormalized = Mathf.Clamp01(FadeStartNormalized);
        EndScaleMultiplier = Mathf.Clamp(EndScaleMultiplier, 0.5f, 1f);
        InitialJitterX = Mathf.Max(0f, InitialJitterX);
        InitialJitterY = Mathf.Max(0f, InitialJitterY);
        LocalShakeAmplitude = Mathf.Max(0f, LocalShakeAmplitude);
        LocalShakeDuration = Mathf.Max(0f, LocalShakeDuration);
        ScaleOverLife ??= CreateScaleCurve();
        AlphaOverLife ??= CreateAlphaCurve();
    }

    public static CombatTextMotionSettings CreateBurnDefault()
    {
        return new CombatTextMotionSettings
        {
            Lifetime = 0.48f,
            ConnectionDuration = 0f,
            HorizontalSpeed = 4f,
            UpwardSpeed = 62f,
            DownwardAcceleration = 100f,
            SpawnScale = 0.82f,
            PopOvershoot = 1.07f,
            SettleTime = 0.10f,
            FadeStartNormalized = 0.52f,
            EndScaleMultiplier = 0.94f,
            InitialJitterX = 3f,
            InitialJitterY = 2f,
            LocalShakeAmplitude = 0f,
            LocalShakeDuration = 0f
        };
    }

    public static CombatTextMotionSettings CreateReducedDefault()
    {
        return new CombatTextMotionSettings
        {
            Lifetime = 0.66f,
            ConnectionDuration = 0.08f,
            HorizontalSpeed = 7f,
            UpwardSpeed = 72f,
            DownwardAcceleration = 90f,
            SpawnScale = 0.78f,
            PopOvershoot = 1.05f,
            SettleTime = 0.10f,
            FadeStartNormalized = 0.60f,
            EndScaleMultiplier = 0.95f,
            InitialJitterX = 2f,
            InitialJitterY = 1f,
            LocalShakeAmplitude = 0f,
            LocalShakeDuration = 0f
        };
    }

    private static AnimationCurve CreateScaleCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.60f),
            new Keyframe(0.10f, 1.15f),
            new Keyframe(0.19f, 1f),
            new Keyframe(0.72f, 1f),
            new Keyframe(1f, 0.93f));
    }

    private static AnimationCurve CreateAlphaCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.62f, 1f),
            new Keyframe(0.82f, 0.55f),
            new Keyframe(1f, 0f));
    }
}

[CreateAssetMenu(fileName = "CombatTextProfile", menuName = "ScrapWaves/Game Feel/Combat Text Profile")]
public sealed class CombatTextProfile : ScriptableObject
{
    public const int LaneCount = 4;

    [Header("Master")]
    public bool Enabled = true;
    public CombatTextView ViewPrefab;
    public TMP_FontAsset FontAsset;
    public Material DefaultFontMaterial;
    public bool CompactLargeNumbers = true;
    [Range(0, 32000)] public int CanvasSortingOrder = 800;
    public Vector2 ReferenceResolution = new(1920f, 1080f);

    [Header("Styles")]
    public CombatTextStyleDefinition Normal = new();
    public CombatTextStyleDefinition Burn = CreateBurnStyle(false);
    public CombatTextStyleDefinition JellifiedBurn = CreateBurnStyle(true);
    public CombatTextStyleDefinition Critical = CreateCriticalStyle();
    public CombatTextStyleDefinition WeakPoint = CreateWeakPointStyle();
    public CombatTextStyleDefinition CriticalWeakPoint = CreateCriticalWeakPointStyle();
    public CombatTextStyleDefinition Kill = CreateKillStyle();
    public CombatTextStyleDefinition Ability = CreateAbilityStyle();

    [Header("Motion")]
    public CombatTextMotionSettings NormalMotion = new();
    public CombatTextMotionSettings BurnTallyMotion = CombatTextMotionSettings.CreateBurnDefault();
    public CombatTextMotionSettings CriticalMotion = new() { Lifetime = 0.86f, PopOvershoot = 1.20f };
    public CombatTextMotionSettings WeakPointMotion = new() { Lifetime = 0.84f, UpwardSpeed = 138f, PopOvershoot = 1.18f };
    public CombatTextMotionSettings KillMotion = new() { Lifetime = 0.92f, UpwardSpeed = 145f, PopOvershoot = 1.20f };
    public CombatTextMotionSettings ReducedMotion = CombatTextMotionSettings.CreateReducedDefault();

    [Header("Magnitude")]
    public AnimationCurve DamageRatioToScale = CreateMagnitudeCurve();
    [Range(0.5f, 1f)] public float MinimumMagnitudeScale = 0.85f;
    [Range(1f, 2f)] public float MaximumMagnitudeScale = 1.42f;
    [Range(0.5f, 1f)] public float MinimumResolvedScale = 0.82f;
    [Range(1f, 2f)] public float MaximumResolvedScale = 1.48f;
    [Range(1f, 1.5f)] public float CriticalScaleMultiplier = 1.08f;
    [Range(1f, 1.5f)] public float WeakPointScaleMultiplier = 1.08f;
    [Range(1f, 1.5f)] public float CriticalWeakPointScaleCap = 1.16f;
    [Range(1f, 1.25f)] public float KillScaleMultiplier = 1.05f;
    [Range(1f, 1.25f)] public float EliteBossScaleMultiplier = 1.03f;
    [Range(0.5f, 1f)] public float BurnScaleMultiplier = 0.90f;

    [Header("Aggregation")]
    [Min(0.01f)] public float CannonAutomaticFallbackWindow = 0.16f;
    [Min(0.01f)] public float CannonManualFallbackWindow = 0.24f;
    [Min(0.01f)] public float CannonActiveScatterFallbackWindow = 0.18f;
    [Min(0.01f)] public float HeadHunterFallbackWindow = 0.08f;
    [Min(0.01f)] public float SustainedContactFallbackWindow = 0.14f;
    [Min(0.01f)] public float RocketExplosionFallbackWindow = 0.14f;
    [Min(0.01f)] public float FragmentFallbackWindow = 0.30f;
    [Min(0.01f)] public float FlamethrowerDirectFallbackWindow = 0.30f;
    [Min(0.01f)] public float BurnFallbackWindow = 0.65f;
    [Min(0.01f)] public float MortarFallbackWindow = 0.18f;
    [Min(0.01f)] public float BladeSustainedFallbackWindow = 0.22f;
    [Min(0.01f)] public float ManualMultiHitFallbackWindow = 0.18f;
    [Range(0f, 0.25f)] public float DirectRePunchScale = 0.08f;
    [Range(0f, 0.15f)] public float BurnRePunchScale = 0.04f;
    [Min(0.01f)] public float RePunchDuration = 0.10f;
    [Min(0f)] public float DirectRePunchNudge = 6f;
    [Min(0f)] public float BurnRePunchNudge = 2f;
    [Min(0.1f)] public float DirectMaximumSegmentLifetime = 1.10f;
    [Min(0.1f)] public float RocketMaximumSegmentLifetime = 1.10f;
    [Min(0.1f)] public float BurnMaximumSegmentLifetime = 3.25f;
    [Min(0f)] public float SequenceCompletionGrace = 0.12f;

    [Header("Density")]
    [Range(1, 40)] public int LowPrewarmViews = 18;
    [Range(1, 40)] public int MediumPrewarmViews = 26;
    [Range(1, 40)] public int HighPrewarmViews = 36;
    [Range(1, 40)] public int LowActiveViews = 16;
    [Range(1, 40)] public int MediumActiveViews = 24;
    [Range(1, 40)] public int HighActiveViews = 32;
    [Range(1, 16)] public int LowStartsPerFrame = 3;
    [Range(1, 16)] public int MediumStartsPerFrame = 5;
    [Range(1, 16)] public int HighStartsPerFrame = 7;
    [Range(1, 20)] public int LowVisibleBurnTallies = 6;
    [Range(1, 20)] public int MediumVisibleBurnTallies = 10;
    [Range(1, 20)] public int HighVisibleBurnTallies = 16;
    [Range(1, 64)] public int MaximumPooledViews = 40;
    [Range(16, 256)] public int AggregateCapacity = 128;
    [Range(8, 128)] public int SequenceCapacity = 64;
    [Min(0.1f)] public float SequenceOrphanTimeout = 1.25f;
    [Min(0f)] public float LaneSpacing = 20f;

    [Header("Visibility")]
    [Min(0f)] public float FullSizeDistance = 26f;
    [Min(0f)] public float RoutineMaximumDistance = 38f;
    [Min(0f)] public float ImportantMaximumDistance = 50f;
    [Range(0.5f, 1f)] public float DistantScaleMultiplier = 0.82f;
    [Range(0f, 0.25f)] public float HorizontalViewportInset = 0.04f;
    [Range(0f, 0.25f)] public float VerticalViewportInset = 0.06f;
    [Min(0f)] public float WorldAnchorHeight = 1.25f;
    [Range(1f, 60f)] public float BurnAnchorProjectionRate = 20f;
    [Min(0f)] public float MajorAbilityRatioThreshold = 1.15f;
    [Min(0f)] public float EliteBossImportantRatioThreshold = 1.50f;

    [Header("Accessibility")]
    [Range(0f, 1f)] public float ReducedMotionLateralMultiplier = 0.35f;
    [Range(0f, 1f)] public float ReducedShakeMultiplier = 0f;
    [Range(0f, 1f)] public float ReducedFlashAccentAlpha = 0.45f;

    private static CombatTextProfile s_fallback;

    public static CombatTextProfile Resolve(CombatTextProfile authored)
    {
        if (authored != null)
        {
            authored.Sanitize();
            return authored;
        }
        if (s_fallback == null)
        {
            s_fallback = CreateInstance<CombatTextProfile>();
            s_fallback.hideFlags = HideFlags.HideAndDontSave;
            s_fallback.Sanitize();
        }
        return s_fallback;
    }

    public CombatTextStyleDefinition GetStyle(CombatTextStyleId style)
    {
        return style switch
        {
            CombatTextStyleId.Burn => Burn,
            CombatTextStyleId.JellifiedBurn => JellifiedBurn,
            CombatTextStyleId.Critical => Critical,
            CombatTextStyleId.WeakPoint => WeakPoint,
            CombatTextStyleId.CriticalWeakPoint => CriticalWeakPoint,
            CombatTextStyleId.Kill => Kill,
            CombatTextStyleId.Ability => Ability,
            _ => Normal
        };
    }

    public CombatTextMotionSettings GetMotion(CombatTextStyleId style, bool burnTally, bool reducedMotion)
    {
        if (reducedMotion)
            return ReducedMotion;
        if (burnTally)
            return BurnTallyMotion;
        return style switch
        {
            CombatTextStyleId.Kill => KillMotion,
            CombatTextStyleId.WeakPoint or CombatTextStyleId.CriticalWeakPoint => WeakPointMotion,
            CombatTextStyleId.Critical => CriticalMotion,
            _ => NormalMotion
        };
    }

    public float GetFallbackWindow(DamageFeedbackKind kind, WeaponFeedbackMode mode, WeaponType weaponType)
    {
        if (kind.IsBurnFamily()) return BurnFallbackWindow;
        if (weaponType == WeaponType.Mortar &&
            (kind == DamageFeedbackKind.Explosion || kind == DamageFeedbackKind.Fragment))
        {
            return MortarFallbackWindow;
        }
        if (weaponType == WeaponType.AutomaticCannon)
        {
            if (kind == DamageFeedbackKind.Piercing)
                return HeadHunterFallbackWindow;
            if (kind == DamageFeedbackKind.Ability)
                return CannonActiveScatterFallbackWindow;
        }
        return kind switch
        {
            DamageFeedbackKind.Fragment => FragmentFallbackWindow,
            DamageFeedbackKind.Explosion => RocketExplosionFallbackWindow,
            DamageFeedbackKind.SustainedContact => weaponType == WeaponType.RotatingBlade
                ? BladeSustainedFallbackWindow
                : SustainedContactFallbackWindow,
            DamageFeedbackKind.ManualMultiHit => ManualMultiHitFallbackWindow,
            DamageFeedbackKind.PersistentArea => FlamethrowerDirectFallbackWindow,
            _ => mode == WeaponFeedbackMode.Manual
                ? CannonManualFallbackWindow
                : CannonAutomaticFallbackWindow
        };
    }

    public float GetMaximumSegmentLifetime(DamageFeedbackKind kind)
    {
        if (kind.IsBurnFamily()) return BurnMaximumSegmentLifetime;
        if (kind == DamageFeedbackKind.Explosion || kind == DamageFeedbackKind.Fragment)
            return RocketMaximumSegmentLifetime;
        return DirectMaximumSegmentLifetime;
    }

    public int GetPrewarmCount(GameFeelQualityLevel quality) => quality switch
    {
        GameFeelQualityLevel.Low => LowPrewarmViews,
        GameFeelQualityLevel.Medium => MediumPrewarmViews,
        _ => HighPrewarmViews
    };

    public int GetActiveLimit(GameFeelQualityLevel quality) => quality switch
    {
        GameFeelQualityLevel.Low => LowActiveViews,
        GameFeelQualityLevel.Medium => MediumActiveViews,
        _ => HighActiveViews
    };

    public int GetStartLimit(GameFeelQualityLevel quality) => quality switch
    {
        GameFeelQualityLevel.Low => LowStartsPerFrame,
        GameFeelQualityLevel.Medium => MediumStartsPerFrame,
        _ => HighStartsPerFrame
    };

    public int GetBurnLimit(GameFeelQualityLevel quality) => quality switch
    {
        GameFeelQualityLevel.Low => LowVisibleBurnTallies,
        GameFeelQualityLevel.Medium => MediumVisibleBurnTallies,
        _ => HighVisibleBurnTallies
    };

    public void Sanitize()
    {
        Normal ??= new CombatTextStyleDefinition();
        Burn ??= CreateBurnStyle(false);
        JellifiedBurn ??= CreateBurnStyle(true);
        Critical ??= CreateCriticalStyle();
        WeakPoint ??= CreateWeakPointStyle();
        CriticalWeakPoint ??= CreateCriticalWeakPointStyle();
        Kill ??= CreateKillStyle();
        Ability ??= CreateAbilityStyle();
        Normal.Sanitize(); Burn.Sanitize(); JellifiedBurn.Sanitize(); Critical.Sanitize();
        WeakPoint.Sanitize(); CriticalWeakPoint.Sanitize(); Kill.Sanitize(); Ability.Sanitize();

        NormalMotion ??= new CombatTextMotionSettings();
        BurnTallyMotion ??= CombatTextMotionSettings.CreateBurnDefault();
        CriticalMotion ??= new CombatTextMotionSettings();
        WeakPointMotion ??= new CombatTextMotionSettings();
        KillMotion ??= new CombatTextMotionSettings();
        ReducedMotion ??= CombatTextMotionSettings.CreateReducedDefault();
        NormalMotion.Sanitize(); BurnTallyMotion.Sanitize(); CriticalMotion.Sanitize();
        WeakPointMotion.Sanitize(); KillMotion.Sanitize(); ReducedMotion.Sanitize();

        DamageRatioToScale ??= CreateMagnitudeCurve();
        MinimumMagnitudeScale = Mathf.Clamp(MinimumMagnitudeScale, 0.5f, 1f);
        MaximumMagnitudeScale = Mathf.Max(1f, MaximumMagnitudeScale);
        MinimumResolvedScale = Mathf.Clamp(MinimumResolvedScale, 0.5f, 1f);
        MaximumResolvedScale = Mathf.Max(1f, MaximumResolvedScale);
        ReferenceResolution.x = Mathf.Max(320f, ReferenceResolution.x);
        ReferenceResolution.y = Mathf.Max(180f, ReferenceResolution.y);

        CannonAutomaticFallbackWindow = Mathf.Max(0.01f, CannonAutomaticFallbackWindow);
        CannonManualFallbackWindow = Mathf.Max(0.01f, CannonManualFallbackWindow);
        CannonActiveScatterFallbackWindow = Mathf.Max(0.01f, CannonActiveScatterFallbackWindow);
        HeadHunterFallbackWindow = Mathf.Max(0.01f, HeadHunterFallbackWindow);
        SustainedContactFallbackWindow = Mathf.Max(0.01f, SustainedContactFallbackWindow);
        RocketExplosionFallbackWindow = Mathf.Max(0.01f, RocketExplosionFallbackWindow);
        FragmentFallbackWindow = Mathf.Max(0.01f, FragmentFallbackWindow);
        FlamethrowerDirectFallbackWindow = Mathf.Max(0.01f, FlamethrowerDirectFallbackWindow);
        BurnFallbackWindow = Mathf.Max(0.01f, BurnFallbackWindow);
        MortarFallbackWindow = Mathf.Max(0.01f, MortarFallbackWindow);
        BladeSustainedFallbackWindow = Mathf.Max(0.01f, BladeSustainedFallbackWindow);
        ManualMultiHitFallbackWindow = Mathf.Max(0.01f, ManualMultiHitFallbackWindow);
        DirectMaximumSegmentLifetime = Mathf.Max(0.1f, DirectMaximumSegmentLifetime);
        RocketMaximumSegmentLifetime = Mathf.Max(0.1f, RocketMaximumSegmentLifetime);
        BurnMaximumSegmentLifetime = Mathf.Max(0.1f, BurnMaximumSegmentLifetime);

        MaximumPooledViews = Mathf.Clamp(MaximumPooledViews, 1, 64);
        LowActiveViews = Mathf.Clamp(LowActiveViews, 1, MaximumPooledViews);
        MediumActiveViews = Mathf.Clamp(MediumActiveViews, LowActiveViews, MaximumPooledViews);
        HighActiveViews = Mathf.Clamp(HighActiveViews, MediumActiveViews, MaximumPooledViews);
        LowPrewarmViews = Mathf.Clamp(LowPrewarmViews, LowActiveViews, MaximumPooledViews);
        MediumPrewarmViews = Mathf.Clamp(MediumPrewarmViews, MediumActiveViews, MaximumPooledViews);
        HighPrewarmViews = Mathf.Clamp(HighPrewarmViews, HighActiveViews, MaximumPooledViews);
        LowVisibleBurnTallies = Mathf.Clamp(LowVisibleBurnTallies, 1, LowActiveViews);
        MediumVisibleBurnTallies = Mathf.Clamp(MediumVisibleBurnTallies, LowVisibleBurnTallies, MediumActiveViews);
        HighVisibleBurnTallies = Mathf.Clamp(HighVisibleBurnTallies, MediumVisibleBurnTallies, HighActiveViews);
        AggregateCapacity = Mathf.Clamp(AggregateCapacity, 16, 256);
        SequenceCapacity = Mathf.Clamp(SequenceCapacity, 8, 128);
        SequenceOrphanTimeout = Mathf.Max(0.1f, SequenceOrphanTimeout);
        LaneSpacing = Mathf.Max(0f, LaneSpacing);
        FullSizeDistance = Mathf.Max(0f, FullSizeDistance);
        RoutineMaximumDistance = Mathf.Max(FullSizeDistance, RoutineMaximumDistance);
        ImportantMaximumDistance = Mathf.Max(RoutineMaximumDistance, ImportantMaximumDistance);
        BurnAnchorProjectionRate = Mathf.Clamp(BurnAnchorProjectionRate, 1f, 60f);
    }

    private void OnValidate() => Sanitize();

    private static AnimationCurve CreateMagnitudeCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.25f, 0.86f),
            new Keyframe(0.50f, 0.93f),
            new Keyframe(1f, 1f),
            new Keyframe(2f, 1.13f),
            new Keyframe(4f, 1.27f),
            new Keyframe(8f, 1.38f));
    }

    private static CombatTextStyleDefinition CreateBurnStyle(bool jellified)
    {
        return new CombatTextStyleDefinition
        {
            TextColor = jellified ? new Color(0.72f, 1f, 0.46f) : new Color(1f, 0.62f, 0.18f),
            AccentColor = jellified ? new Color(0.34f, 0.92f, 0.24f) : new Color(1f, 0.30f, 0.05f),
            FontSize = 30f,
            BaseScale = 0.90f,
            FontStyle = FontStyles.Bold
        };
    }

    private static CombatTextStyleDefinition CreateCriticalStyle() => new()
    {
        TextColor = new Color(1f, 0.82f, 0.22f), AccentColor = new Color(1f, 0.42f, 0.04f),
        FontSize = 38f, BaseScale = 1.06f, FontStyle = FontStyles.Bold
    };
    private static CombatTextStyleDefinition CreateWeakPointStyle() => new()
    {
        TextColor = Color.white, AccentColor = new Color(0.35f, 0.95f, 1f),
        FontSize = 38f, BaseScale = 1.07f, FontStyle = FontStyles.Bold
    };
    private static CombatTextStyleDefinition CreateCriticalWeakPointStyle() => new()
    {
        TextColor = new Color(1f, 0.94f, 0.54f), AccentColor = new Color(0.42f, 0.96f, 1f),
        FontSize = 41f, BaseScale = 1.10f, FontStyle = FontStyles.Bold
    };
    private static CombatTextStyleDefinition CreateKillStyle() => new()
    {
        TextColor = new Color(1f, 0.96f, 0.75f), AccentColor = new Color(1f, 0.36f, 0.10f),
        FontSize = 42f, BaseScale = 1.10f, FontStyle = FontStyles.Bold
    };
    private static CombatTextStyleDefinition CreateAbilityStyle() => new()
    {
        TextColor = new Color(0.82f, 0.92f, 1f), AccentColor = new Color(0.30f, 0.72f, 1f),
        FontSize = 37f, BaseScale = 1.04f, FontStyle = FontStyles.Bold
    };
}
