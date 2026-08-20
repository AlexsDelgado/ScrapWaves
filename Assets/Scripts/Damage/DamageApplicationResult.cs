using UnityEngine;

public readonly struct DamageApplicationResult
{
    public readonly bool Applied;
    public readonly bool Blocked;
    public readonly bool Killed;
    public readonly bool IsAuthoritative;
    public readonly int RequestedDamage;
    public readonly int ModifiedDamage;
    public readonly int AppliedDamage;
    public readonly int HealthBefore;
    public readonly int HealthAfter;

    public DamageApplicationResult(
        bool applied,
        bool blocked,
        bool killed,
        int requestedDamage,
        int modifiedDamage,
        int appliedDamage,
        int healthBefore,
        int healthAfter,
        bool isAuthoritative = true)
    {
        Applied = applied;
        Blocked = blocked;
        Killed = killed;
        IsAuthoritative = isAuthoritative;
        RequestedDamage = Mathf.Max(0, requestedDamage);
        ModifiedDamage = Mathf.Max(0, modifiedDamage);
        AppliedDamage = Mathf.Max(0, appliedDamage);
        HealthBefore = Mathf.Max(0, healthBefore);
        HealthAfter = Mathf.Max(0, healthAfter);
    }

    public static DamageApplicationResult Rejected(in DamageRequest request, int health)
    {
        int currentHealth = Mathf.Max(0, health);
        return new DamageApplicationResult(
            false,
            false,
            false,
            request.RequestedDamage,
            request.ModifiedDamage,
            0,
            currentHealth,
            currentHealth);
    }

    public static DamageApplicationResult BlockedResult(in DamageRequest request, int health)
    {
        int currentHealth = Mathf.Max(0, health);
        return new DamageApplicationResult(
            false,
            true,
            false,
            request.RequestedDamage,
            request.ModifiedDamage,
            0,
            currentHealth,
            currentHealth);
    }

    public static DamageApplicationResult FromHealthDelta(
        in DamageRequest request,
        int healthBefore,
        int healthAfter)
    {
        int before = Mathf.Max(0, healthBefore);
        int after = Mathf.Clamp(healthAfter, 0, before);
        int appliedDamage = before - after;
        bool applied = appliedDamage > 0;
        bool killed = applied && before > 0 && after == 0;
        return new DamageApplicationResult(
            applied,
            false,
            killed,
            request.RequestedDamage,
            request.ModifiedDamage,
            appliedDamage,
            before,
            after);
    }

    public static DamageApplicationResult FromLegacy(in DamageRequest request, bool applied)
    {
        int appliedDamage = applied ? request.ModifiedDamage : 0;
        return new DamageApplicationResult(
            applied && appliedDamage > 0,
            false,
            false,
            request.RequestedDamage,
            request.ModifiedDamage,
            appliedDamage,
            0,
            0,
            false);
    }
}
