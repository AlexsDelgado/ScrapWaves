using UnityEngine;

public readonly struct WeaponDamageRoll
{
    public readonly WeaponInstance Weapon;
    public readonly bool EliteOrBoss;
    public readonly bool CanCrit;
    public readonly bool IsCritical;
    public readonly bool IsAbilityDamage;
    public readonly float BaseDamage;
    public readonly float FinalDamage;

    public WeaponDamageRoll(WeaponInstance weapon, bool eliteOrBoss, bool canCrit, bool isCritical, float baseDamage, float finalDamage, bool isAbilityDamage = false)
    {
        Weapon = weapon;
        EliteOrBoss = eliteOrBoss;
        CanCrit = canCrit;
        IsCritical = isCritical;
        IsAbilityDamage = isAbilityDamage;
        BaseDamage = baseDamage;
        FinalDamage = finalDamage;
    }
}

public readonly struct WeaponDamageContext
{
    public readonly PlayerStats Stats;
    public readonly WeaponInstance Weapon;
    public readonly bool CanCrit;
    public readonly float CritMultiplierOverride;
    public readonly float DamageScale;
    public readonly bool IsAbilityDamage;
    public readonly float KnockbackScale;
    public readonly bool IsCritical;
    public readonly float BaseDamage;
    public readonly float TargetNeutralDamage;
    public readonly float EliteDamageMultiplier;

    public WeaponDamageContext(
        PlayerStats stats,
        WeaponInstance weapon,
        bool canCrit,
        float critMultiplierOverride,
        float damageScale,
        bool isAbilityDamage,
        float knockbackScale)
    {
        Stats = stats;
        Weapon = weapon;
        CanCrit = canCrit;
        CritMultiplierOverride = critMultiplierOverride;
        DamageScale = Mathf.Max(0f, damageScale);
        IsAbilityDamage = isAbilityDamage;
        KnockbackScale = Mathf.Max(0f, knockbackScale);
        IsCritical = false;
        BaseDamage = 0f;
        TargetNeutralDamage = 0f;
        EliteDamageMultiplier = 1f;

        if (stats == null || weapon?.Data == null)
            return;

        float damage = Mathf.Max(0f, weapon.Data.BaseDamage);
        BaseDamage = damage;
        damage *= WeaponDamageResolver.GetLevelDamageMultiplier(weapon);
        damage *= WeaponDamageResolver.GetPathDamageMultiplier(weapon);
        damage *= Mathf.Max(0f, stats.GetStat(StatType.DamageMultiplier));

        if (isAbilityDamage)
            damage *= WeaponMath.GetStatScale(stats, StatType.AbilityDamageMultiplier);

        EliteDamageMultiplier = Mathf.Max(0f, stats.GetStat(StatType.EliteDamageMultiplier));

        IsCritical = canCrit && WeaponDamageResolver.RollCrit(stats);
        if (IsCritical)
            damage *= Mathf.Max(1f, stats.GetStat(StatType.CriticalDamage) * critMultiplierOverride);

        TargetNeutralDamage = damage;
    }

    public bool IsValid => Stats != null && Weapon?.Data != null;

    public int CalculateDamage(Transform target, float additionalScale = 1f)
    {
        if (!IsValid)
            return 0;

        Vector3? targetPosition = target != null ? target.position : (Vector3?)null;
        return CalculateDamage(WeaponEnemyClassifier.CountsAsEliteOrBoss(target), targetPosition, additionalScale);
    }

    public int CalculateDamage(bool eliteOrBoss, float additionalScale = 1f) => CalculateDamage(eliteOrBoss, null, additionalScale);

    public int CalculateDamage(bool eliteOrBoss, Vector3? targetPosition, float additionalScale = 1f)
    {
        float damage = CalculateDamageValue(eliteOrBoss, targetPosition, additionalScale, report: true);
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    public int EstimateDamage(bool eliteOrBoss, float additionalScale = 1f) => EstimateDamage(eliteOrBoss, null, additionalScale);

    public int EstimateDamage(bool eliteOrBoss, Vector3? targetPosition, float additionalScale = 1f)
    {
        float damage = CalculateDamageValue(eliteOrBoss, targetPosition, additionalScale, report: false);
        return Mathf.Max(1, Mathf.RoundToInt(damage));
    }

    public float CalculateDamageValue(bool eliteOrBoss, float additionalScale = 1f, bool report = true)
        => CalculateDamageValue(eliteOrBoss, null, additionalScale, report);

    /// <summary>
    /// Igual que la sobrecarga sin posición, pero además aplica el multiplicador de daño por
    /// rango (Sharpshooter/CQB) cuando se conoce la posición del objetivo: distancia jugador-objetivo
    /// mayor a 15m usa <see cref="StatType.LongRangeDamageMultiplier"/>, menor a 10m usa
    /// <see cref="StatType.CloseRangeDamageMultiplier"/> (ambos con base neutra 1 si no hay ítem).
    /// </summary>
    public float CalculateDamageValue(bool eliteOrBoss, Vector3? targetPosition, float additionalScale = 1f, bool report = true)
    {
        if (!IsValid)
            return 0f;

        float damage = TargetNeutralDamage;
        if (eliteOrBoss)
            damage *= EliteDamageMultiplier;

        damage *= GetRangeDamageMultiplier(targetPosition);
        damage *= DamageScale * Mathf.Max(0f, additionalScale);

        if (report)
            WeaponDamageResolver.ReportDamageResolved(new WeaponDamageRoll(
                Weapon,
                eliteOrBoss,
                CanCrit,
                IsCritical,
                BaseDamage,
                damage,
                IsAbilityDamage));

        return damage;
    }

    private const float LongRangeDistance = 15f;
    private const float CloseRangeDistance = 10f;

    private float GetRangeDamageMultiplier(Vector3? targetPosition)
    {
        if (!targetPosition.HasValue || Stats == null)
            return 1f;

        float distance = Vector3.Distance(Stats.transform.position, targetPosition.Value);
        if (distance > LongRangeDistance)
            return Mathf.Max(0f, Stats.GetStat(StatType.LongRangeDamageMultiplier));
        if (distance < CloseRangeDistance)
            return Mathf.Max(0f, Stats.GetStat(StatType.CloseRangeDamageMultiplier));

        return 1f;
    }

    public float CalculateKnockback(int damage, float falloffScale = 1f)
    {
        if (!IsValid)
            return 0f;

        return WeaponMath.CalculateKnockback(Stats, Weapon, damage, KnockbackScale, falloffScale);
    }
}

public static class WeaponDamageResolver
{
    public static event System.Action<WeaponDamageRoll> OnDamageResolved;

    // Calculates damage from weapon base, level/path, stats, and crit.
    public static float CalculateDamage(PlayerStats stats, WeaponInstance instance, bool eliteOrBoss, bool canCrit, float critMultiplierOverride = 1f, bool isAbilityDamage = false, Vector3? targetPosition = null)
    {
        WeaponDamageContext context = new(
            stats,
            instance,
            canCrit,
            critMultiplierOverride,
            1f,
            isAbilityDamage,
            1f);
        return context.CalculateDamageValue(eliteOrBoss, targetPosition);
    }

    public static void ReportDamageResolved(WeaponDamageRoll roll)
    {
        OnDamageResolved?.Invoke(roll);
    }

    // Returns configured level damage multiplier for weapon instance.
    public static float GetLevelDamageMultiplier(WeaponInstance instance)
    {
        WeaponLevelData levelData = WeaponMath.GetLevelData(instance);
        return levelData != null ? Mathf.Max(0.01f, levelData.DamageMultiplier) : 1f;
    }

    // Returns selected path damage multiplier if advanced path exists.
    public static float GetPathDamageMultiplier(WeaponInstance instance)
    {
        WeaponUpgradePathData pathData = WeaponMath.GetPathData(instance);
        return pathData != null ? Mathf.Max(0.01f, pathData.DamageMultiplier) : 1f;
    }

    // Rolls crit chance from stat system with clamping.
    public static bool RollCrit(PlayerStats stats)
    {
        float critChance = Mathf.Clamp01(stats.GetStat(StatType.CriticalChance));
        return UnityEngine.Random.value <= critChance;
    }
}
