/// <summary>One capability map shared by authoring visibility and runtime validation.</summary>
public static class WeaponStatCapabilities
{
    public static bool SupportsModeField(WeaponType type, int level, WeaponUpgradePath path,
        WeaponStatMode mode, WeaponStatField field)
    {
        bool ability = mode == WeaponStatMode.Ability;
        bool manual = mode == WeaponStatMode.Manual;
        bool automatic = mode == WeaponStatMode.Automatic;
        bool advancedB = level >= 6 && path == WeaponUpgradePath.PathB;
        switch (field)
        {
            case WeaponStatField.Damage: return true;
            case WeaponStatField.AmmoCapacity: return manual;
            case WeaponStatField.KnockbackStrength:
                return !(type == WeaponType.Flamethrower && automatic)
                    && !(type == WeaponType.RotatingBlade && advancedB);
            case WeaponStatField.RangeMeters:
                if (type == WeaponType.RotatingBlade) return manual;
                return !ability || (type == WeaponType.RocketLauncher && !advancedB);
            case WeaponStatField.AttackCyclesPerSecond: return !ability;
            case WeaponStatField.ExplosionRadiusMeters:
                return type == WeaponType.RocketLauncher
                    || (type == WeaponType.Mortar && !(level >= 6 && path == WeaponUpgradePath.PathA && !ability))
                    || (type == WeaponType.Flamethrower && ability);
            default: return false;
        }
    }

    public static bool SupportsSpecificField(WeaponType type, int level, WeaponUpgradePath path,
        WeaponStatMode mode, string field)
    {
        bool a = level >= 6 && path == WeaponUpgradePath.PathA;
        bool b = level >= 6 && path == WeaponUpgradePath.PathB;
        bool q = mode == WeaponStatMode.Ability;
        bool auto = mode == WeaponStatMode.Automatic;
        bool manual = mode == WeaponStatMode.Manual;
        switch (type)
        {
            case WeaponType.AutomaticCannon:
                switch (field)
                {
                    case "ManualProjectileLifetimeSeconds": return manual && !b;
                    case "ContinuousAbilityBulletsPerSecond":
                    case "ContinuousAbilityDurationSeconds": return a && q;
                    case "ContinuousAbilityHeatDurationSteps": return a && q;
                    case "ContinuousModeHeatStep":
                    case "ContinuousModeHeatSpeedBonus": return a && !q;
                    case "HeadHunterEliteDamageMultiplier":
                    case "HeadHunterBossDamageMultiplier": return b;
                    case "HeadHunterPierceTargetCount":
                    case "HeadHunterPierceDamageLossPerTarget": return b && !q;
                    case "HeadHunterAbilityChargeSeconds":
                    case "HeadHunterAbilityPierceRangeMeters": return b && q;
                    case "HeadHunterWeakPointMultiplier":
                    case "HeadHunterWeakPointHeatStep":
                    case "HeadHunterWeakPointMaximum": return b && !auto;
                    default: return false;
                }
            case WeaponType.Flamethrower:
                switch (field)
                {
                    case "ContactPuddleRadiusMeters":
                    case "ContactPuddleDurationSeconds": return a;
                    case "AbilityPuddleRadiusMeters":
                    case "AbilityPuddleDurationSeconds": return a && q;
                    case "AbilityFreezeDurationSeconds": return b && q;
                    case "NitrogenInitialSlowMultiplier":
                    case "NitrogenMinimumSlowMultiplier":
                    case "NitrogenHitsToMinimum":
                    case "NitrogenSlowDurationSeconds": return b && !q;
                    case "AbilitySlowMultiplier":
                    case "AbilitySlowDurationPerFreezeSecond": return b && q;
                    case "JellifiedBurnDamageMultiplier":
                    case "JellifiedBurnDurationMultiplier": return a;
                    default: return false;
                }
            case WeaponType.RocketLauncher:
                switch (field)
                {
                    case "AutomaticProjectileLifetimeSeconds": return auto;
                    case "ManualProjectileLifetimeSeconds": return manual;
                    case "AbilityProjectileLifetimeSeconds": return q;
                    case "AutomaticProjectileTravelRangeMeters": return auto;
                    case "AbilityProjectileTravelRangeMeters": return q;
                    case "AbilityClusterExplosionRadiusMeters":
                    case "AbilityClusterTravelRangeMeters":
                    case "AbilityClusterRocketCount":
                    case "AbilityClusterDamageMultiplier":
                    case "AbilityClusterSpeedMultiplier":
                    case "AbilityClusterFalloff":
                    case "AbilityClusterLifetimeSeconds": return b && q;
                    case "KineticDebuffDurationSeconds":
                    case "KineticDamageTakenMultiplier":
                    case "KineticExplosionRadiusMultiplier":
                    case "KineticExplosionFalloffMultiplier": return a;
                    case "KineticOrdinaryKnockbackMultiplier": return a && !q;
                    case "KineticAbilityKnockbackMultiplier": return a && q;
                    case "FragmentOrdinaryExplosionRadiusMultiplier":
                    case "FragmentKnockbackMultiplier":
                    case "FragmentDamageMultiplier": return b;
                    case "FragmentAdditionalAutomaticRockets": return b && auto;
                    case "FragmentConeRangePerExplosionRadius":
                    case "FragmentConeAngleDegrees": return b;
                    default: return false;
                }
            case WeaponType.Mortar:
                switch (field)
                {
                    case "EnableCriticalHits": return true;
                    case "BarrageRadiusMeters": return q;
                    case "ActiveShellCount": return q && !a;
                    case "GrapeshotPelletCount": return a && !q;
                    case "GrapeshotAbilityProjectilesPerSecond":
                    case "GrapeshotAbilityDurationSeconds": return a && q;
                    case "GrapeshotAbilityHeatProjectilesPerSecond": return a && q;
                    case "GrapeshotAutomaticLandingSpreadMeters": return a && auto;
                    case "GrapeshotManualLandingSpreadMeters": return a && manual;
                    case "GrapeshotDamageMultiplier": return a && !q;
                    case "AbilityHeatShellBonus": return q && !a;
                    case "BasicAbilityShellSpacingSeconds": return q && !a && !b;
                    case "MultiChargedTotalExplosions":
                    case "MultiChargedRepeatDelaySeconds": return b;
                    default: return false;
                }
            case WeaponType.RotatingBlade:
                switch (field)
                {
                    case "OrbitRadiusMeters":
                    case "AutomaticHitRadiusMeters":
                    case "RotationsPerSecond": return auto;
                    case "ThrustLengthMeters":
                    case "AbilityHitRadiusMeters": return q && !b;
                    case "AtomicDashRangeMeters":
                    case "AtomicHitRadiusMeters":
                    case "AtomicRangePerHitMultiplier":
                    case "AtomicDashSegmentSeconds":
                    case "AtomicPostDashInvulnerabilitySeconds": return q && b;
                    case "ExtraBladeCount": return a;
                    case "MultiBladeActionIntervalSeconds": return a && !auto;
                    case "MultiBladeSpreadAngleDegrees": return a && manual;
                    case "ManualConeAngleDegrees": return manual;
                    default: return false;
                }
            default: return false;
        }
    }
}
