using UnityEngine;

public readonly struct WeaponFeedbackContext
{
    public readonly WeaponInstance Weapon;
    public readonly WeaponType WeaponType;
    public readonly WeaponFeedbackMode Mode;
    public readonly WeaponUpgradePath UpgradePath;
    public readonly int WeaponLevel;
    public readonly float NormalizedHeat;
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;
    public readonly Vector3 ImpactPosition;
    public readonly Vector3 ImpactNormal;
    /// <summary>Exact health removed by this single confirmed damage event.</summary>
    public readonly int DamageAmount;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly bool IsKill;
    public readonly bool IsAbilityDamage;
    public readonly WeaponEnemyKind TargetClass;
    public readonly ImpactSurfaceType SurfaceType;
    public readonly float ExplosionRadius;
    public readonly float EventIntensity;
    public readonly Transform Target;
    public readonly Transform Anchor;
    public readonly float ReferenceDamage;
    public readonly int ActionSequenceId;
    public readonly DamageFeedbackKind DamageKind;
    public readonly int StatusInstanceId;
    public readonly WeaponStatusKind StatusKind;
    public readonly int SegmentIndex;

    public WeaponFeedbackContext(
        WeaponInstance weapon,
        WeaponFeedbackMode mode,
        float normalizedHeat,
        Vector3 origin,
        Vector3 direction,
        Vector3 impactPosition = default,
        Vector3 impactNormal = default,
        int damageAmount = 0,
        bool isCritical = false,
        bool isWeakPoint = false,
        bool isKill = false,
        bool isAbilityDamage = false,
        WeaponEnemyKind targetClass = WeaponEnemyKind.Normal,
        ImpactSurfaceType surfaceType = ImpactSurfaceType.Default,
        float explosionRadius = 0f,
        float eventIntensity = 1f,
        Transform target = null,
        Transform anchor = null,
        float referenceDamage = 0f,
        int actionSequenceId = 0,
        DamageFeedbackKind damageKind = DamageFeedbackKind.Direct,
        int statusInstanceId = 0,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        int segmentIndex = 0)
    {
        Weapon = weapon;
        WeaponType = weapon?.Data != null ? weapon.Data.WeaponType : WeaponType.AutomaticCannon;
        Mode = mode;
        UpgradePath = weapon != null && weapon.HasAdvancedPath ? weapon.SelectedPath : WeaponUpgradePath.None;
        WeaponLevel = weapon != null ? Mathf.Clamp(weapon.Level, 1, 10) : 1;
        NormalizedHeat = Mathf.Clamp01(normalizedHeat);
        Origin = origin;
        Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        ImpactPosition = impactPosition;
        ImpactNormal = impactNormal.sqrMagnitude > 0.0001f ? impactNormal.normalized : -Direction;
        DamageAmount = Mathf.Max(0, damageAmount);
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        IsKill = isKill;
        IsAbilityDamage = isAbilityDamage;
        TargetClass = targetClass;
        SurfaceType = surfaceType;
        ExplosionRadius = Mathf.Max(0f, explosionRadius);
        EventIntensity = Mathf.Max(0f, eventIntensity);
        Target = target;
        Anchor = anchor;
        ReferenceDamage = Mathf.Max(0f, referenceDamage);
        ActionSequenceId = Mathf.Max(0, actionSequenceId);
        DamageKind = damageKind;
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        SegmentIndex = Mathf.Max(0, segmentIndex);
    }

    public WeaponFeedbackContext WithImpact(
        Vector3 impactPosition,
        Vector3 impactNormal,
        int damageAmount,
        bool isCritical,
        bool isWeakPoint,
        bool isKill,
        Transform target,
        WeaponEnemyKind targetClass,
        ImpactSurfaceType surfaceType)
    {
        return new WeaponFeedbackContext(
            Weapon,
            Mode,
            NormalizedHeat,
            Origin,
            Direction,
            impactPosition,
            impactNormal,
            damageAmount,
            isCritical,
            isWeakPoint,
            isKill,
            IsAbilityDamage,
            targetClass,
            surfaceType,
            ExplosionRadius,
            EventIntensity,
            target,
            anchor: null,
            ReferenceDamage,
            ActionSequenceId,
            DamageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex);
    }

    public WeaponFeedbackContext WithAppliedDamage(
        Vector3 impactPosition,
        Vector3 impactNormal,
        int appliedDamage,
        bool isCritical,
        bool isWeakPoint,
        bool isKill,
        Transform target,
        WeaponEnemyKind targetClass,
        ImpactSurfaceType surfaceType)
    {
        return WithImpact(
            impactPosition,
            impactNormal,
            appliedDamage,
            isCritical,
            isWeakPoint,
            isKill,
            target,
            targetClass,
            surfaceType);
    }

    public WeaponFeedbackContext WithIntensity(float eventIntensity)
    {
        return new WeaponFeedbackContext(
            Weapon,
            Mode,
            NormalizedHeat,
            Origin,
            Direction,
            ImpactPosition,
            ImpactNormal,
            DamageAmount,
            IsCritical,
            IsWeakPoint,
            IsKill,
            IsAbilityDamage,
            TargetClass,
            SurfaceType,
            ExplosionRadius,
            eventIntensity,
            Target,
            Anchor,
            ReferenceDamage,
            ActionSequenceId,
            DamageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex);
    }

    public WeaponFeedbackContext WithExplosionRadius(float explosionRadius)
    {
        return new WeaponFeedbackContext(
            Weapon,
            Mode,
            NormalizedHeat,
            Origin,
            Direction,
            ImpactPosition,
            ImpactNormal,
            DamageAmount,
            IsCritical,
            IsWeakPoint,
            IsKill,
            IsAbilityDamage,
            TargetClass,
            SurfaceType,
            explosionRadius,
            EventIntensity,
            Target,
            Anchor,
            ReferenceDamage,
            ActionSequenceId,
            DamageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex);
    }

    public WeaponFeedbackContext WithDirection(Vector3 direction)
    {
        return new WeaponFeedbackContext(
            Weapon,
            Mode,
            NormalizedHeat,
            Origin,
            direction,
            ImpactPosition,
            ImpactNormal,
            DamageAmount,
            IsCritical,
            IsWeakPoint,
            IsKill,
            IsAbilityDamage,
            TargetClass,
            SurfaceType,
            ExplosionRadius,
            EventIntensity,
            Target,
            Anchor,
            ReferenceDamage,
            ActionSequenceId,
            DamageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex);
    }

    public WeaponFeedbackContext WithDamageMetadata(
        float referenceDamage,
        int actionSequenceId,
        DamageFeedbackKind damageKind,
        int statusInstanceId = 0,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        int segmentIndex = 0)
    {
        return new WeaponFeedbackContext(
            Weapon,
            Mode,
            NormalizedHeat,
            Origin,
            Direction,
            ImpactPosition,
            ImpactNormal,
            DamageAmount,
            IsCritical,
            IsWeakPoint,
            IsKill,
            IsAbilityDamage,
            TargetClass,
            SurfaceType,
            ExplosionRadius,
            EventIntensity,
            Target,
            Anchor,
            referenceDamage,
            actionSequenceId,
            damageKind,
            statusInstanceId,
            statusKind,
            segmentIndex);
    }

    public WeaponFeedbackContext WithSequence(int actionSequenceId, DamageFeedbackKind damageKind)
    {
        return WithDamageMetadata(
            ReferenceDamage,
            actionSequenceId,
            damageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex);
    }

    public WeaponFeedbackContext WithStatus(
        int statusInstanceId,
        WeaponStatusKind statusKind,
        int segmentIndex = 0)
    {
        DamageFeedbackKind statusDamageKind = statusKind == WeaponStatusKind.JellifiedBurn
            ? DamageFeedbackKind.JellifiedBurn
            : DamageFeedbackKind.Burn;
        return WithDamageMetadata(
            ReferenceDamage,
            ActionSequenceId,
            statusDamageKind,
            statusInstanceId,
            statusKind,
            segmentIndex);
    }
}
