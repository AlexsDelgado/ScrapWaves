using UnityEngine;

public readonly struct WeaponPresentationLoopHandle
{
    public readonly int Id;

    public WeaponPresentationLoopHandle(int id)
    {
        Id = Mathf.Max(0, id);
    }

    public bool IsValid => Id > 0;
}

public readonly struct WeaponPresentationContext
{
    public readonly WeaponPresentationCue Cue;
    public readonly WeaponInstance Weapon;
    public readonly Vector3 Position;
    public readonly Vector3 Direction;
    public readonly float Intensity;
    public readonly Transform Target;
    public readonly bool IsAbility;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly Transform Anchor;
    public readonly WeaponFeedbackMode Mode;
    public readonly WeaponUpgradePath UpgradePath;
    public readonly int WeaponLevel;
    public readonly float NormalizedHeat;
    public readonly Vector3 ImpactNormal;
    public readonly int DamageAmount;
    public readonly bool IsKill;
    public readonly WeaponEnemyKind TargetClass;
    public readonly ImpactSurfaceType SurfaceType;
    public readonly float ExplosionRadius;
    public readonly GameFeelQualityLevel Quality;
    public readonly bool ReducedFlash;
    public readonly float HeatEmissionMultiplier;
    public readonly float HeatSmokeMultiplier;
    public readonly float HeatSparkMultiplier;
    public readonly Color ReducedFlashColor;
    public readonly float ReducedFlashIntensity;

    public WeaponPresentationContext(
        WeaponPresentationCue cue,
        WeaponInstance weapon,
        Vector3 position,
        Vector3 direction,
        float intensity = 1f,
        Transform target = null,
        bool isAbility = false,
        bool isCritical = false,
        bool isWeakPoint = false,
        Transform anchor = null,
        WeaponFeedbackMode mode = WeaponFeedbackMode.Automatic,
        WeaponUpgradePath upgradePath = WeaponUpgradePath.None,
        int weaponLevel = 1,
        float normalizedHeat = 0f,
        Vector3 impactNormal = default,
        int damageAmount = 0,
        bool isKill = false,
        WeaponEnemyKind targetClass = WeaponEnemyKind.Normal,
        ImpactSurfaceType surfaceType = ImpactSurfaceType.Default,
        float explosionRadius = 0f,
        GameFeelQualityLevel quality = GameFeelQualityLevel.High,
        bool reducedFlash = false,
        float heatEmissionMultiplier = 1f,
        float heatSmokeMultiplier = 1f,
        float heatSparkMultiplier = 1f,
        Color reducedFlashColor = default,
        float reducedFlashIntensity = 0.35f)
    {
        Cue = cue;
        Weapon = weapon;
        Position = position;
        Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        Intensity = Mathf.Max(0f, intensity);
        Target = target;
        IsAbility = isAbility;
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        Anchor = anchor;
        Mode = mode;
        UpgradePath = upgradePath;
        WeaponLevel = Mathf.Clamp(weaponLevel, 1, 10);
        NormalizedHeat = Mathf.Clamp01(normalizedHeat);
        ImpactNormal = impactNormal.sqrMagnitude > 0.0001f ? impactNormal.normalized : -Direction;
        DamageAmount = Mathf.Max(0, damageAmount);
        IsKill = isKill;
        TargetClass = targetClass;
        SurfaceType = surfaceType;
        ExplosionRadius = Mathf.Max(0f, explosionRadius);
        Quality = quality;
        ReducedFlash = reducedFlash;
        HeatEmissionMultiplier = Mathf.Max(0f, heatEmissionMultiplier);
        HeatSmokeMultiplier = Mathf.Max(0f, heatSmokeMultiplier);
        HeatSparkMultiplier = Mathf.Max(0f, heatSparkMultiplier);
        ReducedFlashColor = reducedFlashColor;
        ReducedFlashIntensity = Mathf.Clamp01(reducedFlashIntensity);
    }

    public static WeaponPresentationContext FromFeedback(
        WeaponPresentationCue cue,
        in WeaponFeedbackContext context,
        WeaponPresentationProfile profile,
        WeaponPresentationCueData cueData,
        GameFeelQualityLevel quality,
        bool reducedFlash)
    {
        Vector3 position = context.ImpactPosition != default ? context.ImpactPosition : context.Origin;
        WeaponHeatPresentationSettings heat = profile?.Heat;
        float cueHeat = cueData?.HeatMultiplier != null
            ? Mathf.Max(0f, cueData.HeatMultiplier.Evaluate(context.NormalizedHeat))
            : 1f;
        return new WeaponPresentationContext(
            cue,
            context.Weapon,
            position,
            context.Direction,
            context.EventIntensity,
            context.Target,
            context.IsAbilityDamage,
            context.IsCritical,
            context.IsWeakPoint,
            context.Anchor,
            context.Mode,
            context.UpgradePath,
            context.WeaponLevel,
            context.NormalizedHeat,
            context.ImpactNormal,
            context.DamageAmount,
            context.IsKill,
            context.TargetClass,
            context.SurfaceType,
            context.ExplosionRadius,
            quality,
            reducedFlash,
            heat != null ? heat.Emission.Evaluate(context.NormalizedHeat) * cueHeat : cueHeat,
            heat != null ? heat.SmokeRate.Evaluate(context.NormalizedHeat) * cueHeat : cueHeat,
            heat != null ? heat.SparkRate.Evaluate(context.NormalizedHeat) * cueHeat : cueHeat,
            profile != null ? profile.ReducedFlashColor : new Color(1f, 0.58f, 0.16f, 0.55f),
            profile != null ? profile.ReducedFlashIntensity : 0.35f);
    }
}
