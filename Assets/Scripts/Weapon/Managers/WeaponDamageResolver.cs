using UnityEngine;

/// <summary>
/// Cadena de factores multiplicativos que produjo un impacto, en el orden en que se aplican.
/// Es puramente descriptiva: guarda valores que el cálculo ya computó, sin alterarlo.
/// No incluye el nombre del arma a propósito — <see cref="WeaponDamageRoll.Weapon"/> ya lo
/// expone y formatear un string por impacto alocaría en el camino caliente.
/// </summary>
public readonly struct WeaponDamageFactors
{
    public readonly float BaseDamage;
    public readonly float LevelMultiplier;
    public readonly float PathMultiplier;
    public readonly float StatDamageMultiplier;
    public readonly float AbilityMultiplier;
    public readonly float CritMultiplier;
    public readonly float EliteMultiplier;
    public readonly float RangeMultiplier;
    public readonly float DamageScale;
    public readonly float AdditionalScale;
    public readonly float FinalDamage;

    /// <summary>Stat CriticalDamage al momento del disparo; 0 si no fue crítico.</summary>
    public readonly float CritDamageStat;

    /// <summary>Multiplicador de crítico propio del arma que escala al stat.</summary>
    public readonly float CritOverride;

    public WeaponDamageFactors(
        float baseDamage,
        float levelMultiplier,
        float pathMultiplier,
        float statDamageMultiplier,
        float abilityMultiplier,
        float critMultiplier,
        float eliteMultiplier,
        float rangeMultiplier,
        float damageScale,
        float additionalScale,
        float finalDamage,
        float critDamageStat = 0f,
        float critOverride = 1f)
    {
        CritDamageStat = critDamageStat;
        CritOverride = critOverride;
        BaseDamage = baseDamage;
        LevelMultiplier = levelMultiplier;
        PathMultiplier = pathMultiplier;
        StatDamageMultiplier = statDamageMultiplier;
        AbilityMultiplier = abilityMultiplier;
        CritMultiplier = critMultiplier;
        EliteMultiplier = eliteMultiplier;
        RangeMultiplier = rangeMultiplier;
        DamageScale = damageScale;
        AdditionalScale = additionalScale;
        FinalDamage = finalDamage;
    }

    /// <summary>Producto de toda la cadena; debe reproducir <see cref="FinalDamage"/>.</summary>
    public float Product => BaseDamage * LevelMultiplier * PathMultiplier * StatDamageMultiplier
        * AbilityMultiplier * CritMultiplier * EliteMultiplier * RangeMultiplier
        * DamageScale * AdditionalScale;
}

public readonly struct WeaponDamageRoll
{
    public readonly WeaponInstance Weapon;
    public readonly bool EliteOrBoss;
    public readonly bool CanCrit;
    public readonly bool IsCritical;
    public readonly bool IsAbilityDamage;
    public readonly float BaseDamage;
    public readonly float ReferenceDamage;
    public readonly float FinalDamage;
    public readonly WeaponDamageFactors Factors;

    public WeaponDamageRoll(
        WeaponInstance weapon,
        bool eliteOrBoss,
        bool canCrit,
        bool isCritical,
        float baseDamage,
        float finalDamage,
        bool isAbilityDamage = false,
        float referenceDamage = 0f,
        WeaponDamageFactors factors = default)
    {
        Weapon = weapon;
        EliteOrBoss = eliteOrBoss;
        CanCrit = canCrit;
        IsCritical = isCritical;
        IsAbilityDamage = isAbilityDamage;
        BaseDamage = baseDamage;
        ReferenceDamage = Mathf.Max(0f, referenceDamage);
        FinalDamage = finalDamage;
        Factors = factors;
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
    public readonly float ReferenceDamage;
    public readonly float TargetNeutralDamage;
    public readonly float EliteDamageMultiplier;
    public readonly int ActionSequenceId;
    public readonly DamageFeedbackKind DamageKind;
    public readonly int StatusInstanceId;
    public readonly WeaponStatusKind StatusKind;
    public readonly int SegmentIndex;

    // Factores intermedios retenidos solo para diagnóstico; el cálculo ya los computaba
    // como locales. Neutros (1) cuando no aplican.
    public readonly float LevelMultiplier;
    public readonly float PathMultiplier;
    public readonly float StatDamageMultiplier;
    public readonly float AbilityMultiplier;
    public readonly float CritMultiplier;
    public readonly float CritDamageStat;

    public WeaponDamageContext(
        PlayerStats stats,
        WeaponInstance weapon,
        bool canCrit,
        float critMultiplierOverride,
        float damageScale,
        bool isAbilityDamage,
        float knockbackScale,
        int actionSequenceId = 0,
        DamageFeedbackKind damageKind = DamageFeedbackKind.Direct,
        int statusInstanceId = 0,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        int segmentIndex = 0)
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
        ReferenceDamage = 0f;
        TargetNeutralDamage = 0f;
        EliteDamageMultiplier = 1f;
        ActionSequenceId = Mathf.Max(0, actionSequenceId);
        DamageKind = damageKind;
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        SegmentIndex = Mathf.Max(0, segmentIndex);
        LevelMultiplier = 1f;
        PathMultiplier = 1f;
        StatDamageMultiplier = 1f;
        AbilityMultiplier = 1f;
        CritMultiplier = 1f;
        CritDamageStat = 0f;

        if (stats == null || weapon?.Data == null)
            return;

        float damage = Mathf.Max(0f, weapon.Data.BaseDamage);
        BaseDamage = damage;

        LevelMultiplier = WeaponDamageResolver.GetLevelDamageMultiplier(weapon);
        damage *= LevelMultiplier;

        PathMultiplier = WeaponDamageResolver.GetPathDamageMultiplier(weapon);
        damage *= PathMultiplier;

        StatDamageMultiplier = Mathf.Max(0f, stats.GetStat(StatType.DamageMultiplier));
        damage *= StatDamageMultiplier;

        if (isAbilityDamage)
        {
            AbilityMultiplier = WeaponMath.GetStatScale(stats, StatType.AbilityDamageMultiplier);
            damage *= AbilityMultiplier;
        }

        EliteDamageMultiplier = Mathf.Max(0f, stats.GetStat(StatType.EliteDamageMultiplier));
        ReferenceDamage = damage * DamageScale;

        IsCritical = canCrit && WeaponDamageResolver.RollCrit(stats);
        if (IsCritical)
        {
            CritDamageStat = stats.GetStat(StatType.CriticalDamage);
            CritMultiplier = Mathf.Max(1f, CritDamageStat * critMultiplierOverride);
            damage *= CritMultiplier;
        }

        TargetNeutralDamage = damage;
    }

    private WeaponDamageContext(
        in WeaponDamageContext source,
        int actionSequenceId,
        DamageFeedbackKind damageKind,
        int statusInstanceId,
        WeaponStatusKind statusKind,
        int segmentIndex,
        float damageScaleMultiplier = 1f,
        float knockbackScaleMultiplier = 1f)
    {
        Stats = source.Stats;
        Weapon = source.Weapon;
        CanCrit = source.CanCrit;
        CritMultiplierOverride = source.CritMultiplierOverride;
        float safeDamageScale = Mathf.Max(0f, damageScaleMultiplier);
        DamageScale = source.DamageScale * safeDamageScale;
        IsAbilityDamage = source.IsAbilityDamage;
        KnockbackScale = source.KnockbackScale * Mathf.Max(0f, knockbackScaleMultiplier);
        IsCritical = source.IsCritical;
        BaseDamage = source.BaseDamage;
        ReferenceDamage = source.ReferenceDamage * safeDamageScale;
        TargetNeutralDamage = source.TargetNeutralDamage;
        EliteDamageMultiplier = source.EliteDamageMultiplier;
        LevelMultiplier = source.LevelMultiplier;
        PathMultiplier = source.PathMultiplier;
        StatDamageMultiplier = source.StatDamageMultiplier;
        AbilityMultiplier = source.AbilityMultiplier;
        CritMultiplier = source.CritMultiplier;
        CritDamageStat = source.CritDamageStat;
        ActionSequenceId = Mathf.Max(0, actionSequenceId);
        DamageKind = damageKind;
        StatusInstanceId = Mathf.Max(0, statusInstanceId);
        StatusKind = statusKind;
        SegmentIndex = Mathf.Max(0, segmentIndex);
    }

    public bool IsValid => Stats != null && Weapon?.Data != null;

    public WeaponDamageContext WithFeedbackMetadata(
        int actionSequenceId,
        DamageFeedbackKind damageKind,
        int statusInstanceId = 0,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn,
        int segmentIndex = 0)
    {
        return new WeaponDamageContext(
            in this,
            actionSequenceId,
            damageKind,
            statusInstanceId,
            statusKind,
            segmentIndex);
    }

    public WeaponDamageContext WithScales(
        float damageScaleMultiplier,
        float knockbackScaleMultiplier,
        DamageFeedbackKind? damageKind = null)
    {
        return new WeaponDamageContext(
            in this,
            ActionSequenceId,
            damageKind ?? DamageKind,
            StatusInstanceId,
            StatusKind,
            SegmentIndex,
            damageScaleMultiplier,
            knockbackScaleMultiplier);
    }

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

        float eliteMultiplier = eliteOrBoss ? EliteDamageMultiplier : 1f;
        float rangeMultiplier = GetRangeDamageMultiplier(targetPosition);
        float safeAdditionalScale = Mathf.Max(0f, additionalScale);

        float damage = TargetNeutralDamage;
        damage *= eliteMultiplier;
        damage *= rangeMultiplier;
        damage *= DamageScale * safeAdditionalScale;

        if (report)
            WeaponDamageResolver.ReportDamageResolved(new WeaponDamageRoll(
                Weapon,
                eliteOrBoss,
                CanCrit,
                IsCritical,
                BaseDamage,
                damage,
                IsAbilityDamage,
                ReferenceDamage,
                new WeaponDamageFactors(
                    BaseDamage,
                    LevelMultiplier,
                    PathMultiplier,
                    StatDamageMultiplier,
                    AbilityMultiplier,
                    CritMultiplier,
                    eliteMultiplier,
                    rangeMultiplier,
                    DamageScale,
                    safeAdditionalScale,
                    damage,
                    CritDamageStat,
                    CritMultiplierOverride)));

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
            return WeaponMath.GetStatScale(Stats, StatType.LongRangeDamageMultiplier);
        if (distance < CloseRangeDistance)
            return WeaponMath.GetStatScale(Stats, StatType.CloseRangeDamageMultiplier);

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
