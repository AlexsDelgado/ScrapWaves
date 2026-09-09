using System.Collections.Generic;
using UnityEngine;

public partial class BasicProjectileWeapon
{
    public WeaponDiagnosticsSnapshot CaptureDiagnostics()
    {
        var sections = new List<WeaponDiagnosticSection>();
        if (Runtime?.Data == null)
        {
            sections.Add(new WeaponDiagnosticSection("Status").Add("Weapon", "Not initialized"));
            return new WeaponDiagnosticsSnapshot(sections);
        }

        var state = new WeaponDiagnosticSection("Runtime")
            .Add("Weapon", Runtime.Data.DisplayName)
            .Add("Type", Runtime.Data.WeaponType.ToString())
            .Add("Level", Runtime.Level)
            .Add("Path", Runtime.SelectedPath.ToString())
            .Add("Mode", Runtime.State.ToString())
            .Add("Ammo", Runtime.CurrentAmmo)
            .Add("Maximum manual ammo", WeaponMath.GetMaxManualAmmo(Runtime, Stats))
            .Add("Manual cooldown remaining", Mathf.Max(0f, Runtime.ManualCooldownTimer), " s")
            .Add("Ability cooldown remaining", Mathf.Max(0f, Runtime.AbilityCooldownTimer), " s")
            .Add("Heat", (Heat != null ? Heat.NormalizedHeat : 0f) * 100f, "%");
        sections.Add(state);
        CollectDiagnostics(sections);
        return new WeaponDiagnosticsSnapshot(sections);
    }

    protected virtual void CollectDiagnostics(List<WeaponDiagnosticSection> sections)
    {
        sections.Add(DiagnosticMode("Automatic", 1f, GetFireInterval(), Runtime.Data.BaseRange, 1, 0f));
        sections.Add(DiagnosticMode("Manual", 1f, GetFireInterval(), Runtime.Data.BaseRange, 1, 1f));
        sections.Add(DiagnosticMode("Active Ability", 1.75f, 0f, Runtime.Data.BaseRange, 1,
            WeaponMath.GetActiveAbilityAmmoCost(Runtime), true));
    }

    // No random roll, target lookup, damage reporting, or gameplay-state writes.
    protected WeaponDiagnosticSection DiagnosticMode(string name, float damageScale, float interval,
        float range, int count, float ammo, bool ability = false, float knockbackScale = -1f,
        float eliteDamageScale = 1f, float bossDamageScale = 1f)
    {
        var section = new WeaponDiagnosticSection(name);
        if (Stats != null && Stats.GetDefinition(StatType.DamageMultiplier) != null
            && Stats.GetDefinition(StatType.EliteDamageMultiplier) != null)
        {
            var context = new WeaponDamageContext(Stats, Runtime, false, 1f, damageScale, ability,
                knockbackScale >= 0f ? knockbackScale : damageScale);
            section.Add("Damage / hit (non-critical)", context.EstimateDamage(false));
            section.Add("Damage / hit (elite, non-critical)", context.EstimateDamage(true, eliteDamageScale));
            if (!Mathf.Approximately(eliteDamageScale, bossDamageScale))
                section.Add("Damage / hit (boss, non-critical)", context.EstimateDamage(true, bossDamageScale));
            if (CanCrit() && Stats.GetDefinition(StatType.CriticalDamage) != null)
            {
                float criticalMultiplier = Mathf.Max(1f, Stats.GetStat(StatType.CriticalDamage) * GetCritMultiplierOverride());
                section.Add("Damage / hit (critical)", context.WithScales(criticalMultiplier, 1f).EstimateDamage(false));
                if (Stats.GetDefinition(StatType.CriticalChance) != null)
                    section.Add("Critical chance", Mathf.Clamp01(Stats.GetStat(StatType.CriticalChance)) * 100f, "%");
            }
            else section.Add("Critical hits", "Unavailable");
            if (Stats.GetDefinition(StatType.Knockback) != null)
                section.Add("Knockback (non-critical hit)", context.CalculateKnockback(context.EstimateDamage(false)));
        }
        else section.Add("Damage", "Unavailable: missing player damage stats");
        if (interval > 0f)
        {
            section.Add("Action / tick interval", interval, " s");
            section.Add("Actions / ticks per second", 1f / interval);
        }
        section.Add("Range", range, " m").Add("Hits / projectiles per action", count).Add("Ammo / action", ammo);
        if (ability) section.Add("Ability cooldown duration", WeaponMath.GetAbilityCooldownDuration(Runtime, Stats), " s");
        return section;
    }
}
