using UnityEngine;

public enum CombatTextStyleId
{
    Normal,
    Burn,
    JellifiedBurn,
    Critical,
    WeakPoint,
    CriticalWeakPoint,
    Kill,
    Ability
}

public enum CombatTextPriority
{
    Decorative = 0,
    BurnTally = 10,
    AutomaticDirect = 20,
    ManualDirect = 30,
    EliteBoss = 40,
    MajorAbility = 50,
    Critical = 60,
    WeakPoint = 70,
    CriticalWeakPoint = 80,
    Kill = 90,
    CriticalWeakPointKill = 100
}

public static class CombatTextStyleResolver
{
    public static CombatTextStyleId ResolveStyle(in CombatTextEvent combatTextEvent)
    {
        if (combatTextEvent.IsKill)
            return CombatTextStyleId.Kill;
        if (combatTextEvent.IsCritical && combatTextEvent.IsWeakPoint)
            return CombatTextStyleId.CriticalWeakPoint;
        if (combatTextEvent.IsWeakPoint)
            return CombatTextStyleId.WeakPoint;
        if (combatTextEvent.IsCritical)
            return CombatTextStyleId.Critical;
        if (combatTextEvent.DamageKind == DamageFeedbackKind.JellifiedBurn)
            return CombatTextStyleId.JellifiedBurn;
        if (combatTextEvent.DamageKind == DamageFeedbackKind.Burn)
            return CombatTextStyleId.Burn;
        if (combatTextEvent.IsAbilityDamage || combatTextEvent.DamageKind == DamageFeedbackKind.Ability)
            return CombatTextStyleId.Ability;
        return CombatTextStyleId.Normal;
    }

    public static CombatTextStyleId ResolveStyle(in CombatTextAggregate aggregate)
    {
        if (aggregate.IsKill)
            return CombatTextStyleId.Kill;
        if (aggregate.IsCritical && aggregate.IsWeakPoint)
            return CombatTextStyleId.CriticalWeakPoint;
        if (aggregate.IsWeakPoint)
            return CombatTextStyleId.WeakPoint;
        if (aggregate.IsCritical)
            return CombatTextStyleId.Critical;
        if (aggregate.Key.DamageKind == DamageFeedbackKind.JellifiedBurn)
            return CombatTextStyleId.JellifiedBurn;
        if (aggregate.Key.DamageKind == DamageFeedbackKind.Burn)
            return CombatTextStyleId.Burn;
        if (aggregate.IsAbilityDamage || aggregate.Key.DamageKind == DamageFeedbackKind.Ability)
            return CombatTextStyleId.Ability;
        return CombatTextStyleId.Normal;
    }

    public static CombatTextPriority ResolvePriority(
        in CombatTextAggregate aggregate,
        CombatTextProfile profile)
    {
        if (aggregate.IsKill && aggregate.IsCritical && aggregate.IsWeakPoint)
            return CombatTextPriority.CriticalWeakPointKill;
        if (aggregate.IsKill)
            return CombatTextPriority.Kill;
        if (aggregate.IsCritical && aggregate.IsWeakPoint)
            return CombatTextPriority.CriticalWeakPoint;
        if (aggregate.IsWeakPoint)
            return CombatTextPriority.WeakPoint;
        if (aggregate.IsCritical)
            return CombatTextPriority.Critical;

        float ratio = Mathf.Max(aggregate.StrongestSingleRatio, Mathf.Sqrt(Mathf.Max(0f, aggregate.TotalRatio)));
        if ((aggregate.IsAbilityDamage || aggregate.Key.DamageKind == DamageFeedbackKind.Ability) &&
            ratio >= profile.MajorAbilityRatioThreshold)
        {
            return CombatTextPriority.MajorAbility;
        }
        if ((aggregate.TargetClass == WeaponEnemyKind.Elite || aggregate.TargetClass == WeaponEnemyKind.Boss) &&
            ratio >= profile.EliteBossImportantRatioThreshold)
        {
            return CombatTextPriority.EliteBoss;
        }
        if (aggregate.IsBurnFamily)
            return CombatTextPriority.BurnTally;
        return aggregate.Mode == WeaponFeedbackMode.Manual
            ? CombatTextPriority.ManualDirect
            : CombatTextPriority.AutomaticDirect;
    }

    public static float ResolveScale(
        in CombatTextAggregate aggregate,
        CombatTextProfile profile,
        float userScale,
        float distanceScale)
    {
        float ratio = Mathf.Max(
            aggregate.StrongestSingleRatio,
            Mathf.Sqrt(Mathf.Max(0f, aggregate.TotalRatio)));
        float magnitude = profile.DamageRatioToScale.Evaluate(ratio);
        magnitude = Mathf.Clamp(magnitude, profile.MinimumMagnitudeScale, profile.MaximumMagnitudeScale);

        float flags = 1f;
        if (aggregate.IsCritical && aggregate.IsWeakPoint)
            flags *= profile.CriticalWeakPointScaleCap;
        else
        {
            if (aggregate.IsCritical) flags *= profile.CriticalScaleMultiplier;
            if (aggregate.IsWeakPoint) flags *= profile.WeakPointScaleMultiplier;
        }
        if (aggregate.IsKill) flags *= profile.KillScaleMultiplier;
        if (aggregate.TargetClass == WeaponEnemyKind.Elite || aggregate.TargetClass == WeaponEnemyKind.Boss)
            flags *= profile.EliteBossScaleMultiplier;
        if (aggregate.IsBurnFamily) flags *= profile.BurnScaleMultiplier;

        CombatTextStyleDefinition style = profile.GetStyle(ResolveStyle(in aggregate));
        float resolved = magnitude * flags * style.BaseScale *
                         Mathf.Clamp(userScale, 0.75f, 1.25f) * Mathf.Clamp(distanceScale, 0.5f, 1f);
        return Mathf.Clamp(resolved, profile.MinimumResolvedScale, profile.MaximumResolvedScale);
    }
}

public enum CombatTextSuppressionReason
{
    None,
    Disabled,
    Invalid,
    Mode,
    BehindCamera,
    Offscreen,
    Distance,
    Density,
    BurnDensity,
    FrameStartBudget,
    PoolExhausted,
    RecordCapacity
}

public readonly struct CombatTextVisibilityDecision
{
    public readonly bool Visible;
    public readonly CombatTextSuppressionReason Reason;
    public readonly float DistanceScale;

    public CombatTextVisibilityDecision(bool visible, CombatTextSuppressionReason reason, float distanceScale = 1f)
    {
        Visible = visible;
        Reason = reason;
        DistanceScale = Mathf.Clamp(distanceScale, 0.5f, 1f);
    }
}

public static class CombatTextVisibilityPolicy
{
    public static bool AllowsMode(CombatTextMode mode, CombatTextPriority priority)
    {
        return mode switch
        {
            CombatTextMode.Off => false,
            CombatTextMode.ImportantOnly => priority >= CombatTextPriority.EliteBoss,
            _ => true
        };
    }

    public static CombatTextVisibilityDecision EvaluateDistance(
        float distance,
        CombatTextPriority priority,
        CombatTextProfile profile)
    {
        bool important = priority >= CombatTextPriority.EliteBoss;
        float maximum = important ? profile.ImportantMaximumDistance : profile.RoutineMaximumDistance;
        if (distance > maximum)
            return new CombatTextVisibilityDecision(false, CombatTextSuppressionReason.Distance);
        float distanceScale = !important && distance > profile.FullSizeDistance
            ? profile.DistantScaleMultiplier
            : 1f;
        return new CombatTextVisibilityDecision(true, CombatTextSuppressionReason.None, distanceScale);
    }

    public static CombatTextVisibilityDecision EvaluateDensity(
        bool burnTally,
        int activeViews,
        int visibleBurnTallies,
        int startsThisFrame,
        GameFeelQualityLevel quality,
        CombatTextProfile profile)
    {
        if (burnTally && visibleBurnTallies >= profile.GetBurnLimit(quality))
            return new CombatTextVisibilityDecision(false, CombatTextSuppressionReason.BurnDensity);
        if (activeViews >= profile.GetActiveLimit(quality))
            return new CombatTextVisibilityDecision(false, CombatTextSuppressionReason.Density);
        if (startsThisFrame >= profile.GetStartLimit(quality))
            return new CombatTextVisibilityDecision(false, CombatTextSuppressionReason.FrameStartBudget);
        return new CombatTextVisibilityDecision(true, CombatTextSuppressionReason.None);
    }

    public static bool TryProject(
        Camera camera,
        Vector3 worldPosition,
        CombatTextPriority priority,
        CombatTextProfile profile,
        out Vector2 screenPoint,
        out float distanceScale,
        out CombatTextSuppressionReason reason)
    {
        screenPoint = default;
        distanceScale = 1f;
        reason = CombatTextSuppressionReason.None;
        if (camera == null)
        {
            reason = CombatTextSuppressionReason.Invalid;
            return false;
        }

        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
        if (viewport.z <= 0f)
        {
            reason = CombatTextSuppressionReason.BehindCamera;
            return false;
        }
        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            reason = CombatTextSuppressionReason.Offscreen;
            return false;
        }

        float distance = Vector3.Distance(camera.transform.position, worldPosition);
        CombatTextVisibilityDecision distanceDecision = EvaluateDistance(distance, priority, profile);
        if (!distanceDecision.Visible)
        {
            reason = distanceDecision.Reason;
            return false;
        }

        viewport.x = Mathf.Clamp(viewport.x, profile.HorizontalViewportInset, 1f - profile.HorizontalViewportInset);
        viewport.y = Mathf.Clamp(viewport.y, profile.VerticalViewportInset, 1f - profile.VerticalViewportInset);
        screenPoint = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        distanceScale = distanceDecision.DistanceScale;
        return true;
    }
}
