using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponStatOverride : MonoBehaviour
{
    public bool UseOverrides = true;

    [Min(0f)] public float DamageMultiplier = 1f;
    [Min(0f)] public float EliteDamageMultiplier = 1f;
    [Min(0.01f)] public float AttackSpeedMultiplier = 1f;
    [Min(0.01f)] public float ProjectileAreaSizeMultiplier = 1f;
    [Range(0f, 1f)] public float CriticalChance = 0.05f;
    [Min(1f)] public float CriticalDamageMultiplier = 2f;
    [Min(0f)] public float KnockbackMultiplier = 1f;
    [Min(0f)] public float AmmoMultiplier = 1f;

    private PlayerStats _stats;
    private float _defaultDamageMultiplier = 1f;
    private float _defaultEliteDamageMultiplier = 1f;
    private float _defaultAttackSpeedMultiplier = 1f;
    private float _defaultProjectileAreaSizeMultiplier = 1f;
    private float _defaultCriticalChance = 0.05f;
    private float _defaultCriticalDamageMultiplier = 2f;
    private float _defaultKnockbackMultiplier = 1f;
    private float _defaultAmmoMultiplier = 1f;

    public void Bind(PlayerStats stats)
    {
        if (_stats == stats)
            return;

        ClearAppliedModifiers();
        _stats = stats;
        CacheDefaultsFromStats();
        ApplyOverrides();
    }

    public void ApplyOverrides()
    {
        if (_stats == null)
            return;

        ClearAppliedModifiers();
        if (!UseOverrides)
            return;

        ApplyExact(StatType.DamageMultiplier, DamageMultiplier);
        ApplyExact(StatType.EliteDamageMultiplier, EliteDamageMultiplier);
        ApplyExact(StatType.AttackSpeedMultiplier, AttackSpeedMultiplier);
        ApplyExact(StatType.ProjectileAreaSize, ProjectileAreaSizeMultiplier);
        ApplyExact(StatType.CriticalChance, CriticalChance);
        ApplyExact(StatType.CriticalDamage, CriticalDamageMultiplier);
        ApplyExact(StatType.Knockback, KnockbackMultiplier);
        ApplyExact(StatType.AmmoMultiplier, AmmoMultiplier);
    }

    public void ResetToDefaults()
    {
        DamageMultiplier = _defaultDamageMultiplier;
        EliteDamageMultiplier = _defaultEliteDamageMultiplier;
        AttackSpeedMultiplier = _defaultAttackSpeedMultiplier;
        ProjectileAreaSizeMultiplier = _defaultProjectileAreaSizeMultiplier;
        CriticalChance = _defaultCriticalChance;
        CriticalDamageMultiplier = _defaultCriticalDamageMultiplier;
        KnockbackMultiplier = _defaultKnockbackMultiplier;
        AmmoMultiplier = _defaultAmmoMultiplier;
        ApplyOverrides();
    }

    private void OnDisable()
    {
        ClearAppliedModifiers();
    }

    private void CacheDefaultsFromStats()
    {
        if (_stats == null)
            return;

        _defaultDamageMultiplier = ReadOrFallback(StatType.DamageMultiplier, _defaultDamageMultiplier);
        _defaultEliteDamageMultiplier = ReadOrFallback(StatType.EliteDamageMultiplier, _defaultEliteDamageMultiplier);
        _defaultAttackSpeedMultiplier = ReadOrFallback(StatType.AttackSpeedMultiplier, _defaultAttackSpeedMultiplier);
        _defaultProjectileAreaSizeMultiplier = ReadOrFallback(StatType.ProjectileAreaSize, _defaultProjectileAreaSizeMultiplier);
        _defaultCriticalChance = ReadOrFallback(StatType.CriticalChance, _defaultCriticalChance);
        _defaultCriticalDamageMultiplier = ReadOrFallback(StatType.CriticalDamage, _defaultCriticalDamageMultiplier);
        _defaultKnockbackMultiplier = ReadOrFallback(StatType.Knockback, _defaultKnockbackMultiplier);
        _defaultAmmoMultiplier = ReadOrFallback(StatType.AmmoMultiplier, _defaultAmmoMultiplier);
    }

    private float ReadOrFallback(StatType statType, float fallback)
    {
        return _stats.GetDefinition(statType) != null ? _stats.GetStat(statType) : fallback;
    }

    private void ApplyExact(StatType statType, float targetValue)
    {
        if (_stats.GetDefinition(statType) == null)
            return;

        float current = _stats.GetStat(statType);
        _stats.AddModifier(new StatModifier(statType, targetValue - current, StatUpgradeSource.TemporaryEffect, this));
    }

    private void ClearAppliedModifiers()
    {
        if (_stats != null)
            _stats.RemoveModifiersFromSource(this);
    }
}
