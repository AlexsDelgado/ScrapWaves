using System.Collections.Generic;
using UnityEngine;

public interface IWeaponRadialDamageObserver
{
    void OnRadialDamageApplied(
        Collider hitCollider,
        IDamageable damageable,
        Transform target,
        Vector3 impactPosition,
        in DamageApplicationResult result,
        in WeaponDamageContext damageContext);
}

public static class WeaponRadialDamage
{
    private const int SmallHitBufferCapacity = 256;
    private const int MediumHitBufferCapacity = 1024;
    private const int LargeHitBufferCapacity = 4096;
    private static readonly List<IDamageable> s_damaged = new(LargeHitBufferCapacity);
    private static readonly Collider[] s_smallHits = new Collider[SmallHitBufferCapacity];
    private static readonly Collider[] s_mediumHits = new Collider[MediumHitBufferCapacity];
    private static readonly Collider[] s_largeHits = new Collider[LargeHitBufferCapacity];
    private static bool s_reportedLargestBufferSaturation;

    public static int Apply(Vector3 center, float radius, int damage, float falloff, float knockback, int maxTargets = 128, bool showVfx = true)
    {
        if (radius <= 0f || damage <= 0)
            return 0;

        if (showVfx && Application.isPlaying)
            ExplosionRadiusVfx.Spawn(center, radius);

        s_damaged.Clear();
        Physics.SyncTransforms();
        Collider[] hits = QueryHits(center, radius, out int hitCount);
        int applied = 0;

        for (int i = 0; i < hitCount && applied < maxTargets; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || s_damaged.Contains(damageable))
                continue;

            s_damaged.Add(damageable);
            float distance = Vector3.Distance(center, hit.transform.position);
            float t = Mathf.Clamp01(distance / radius);
            float falloffScale = Mathf.Lerp(1f, 1f - Mathf.Clamp01(falloff), t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * falloffScale));
            DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(damageable, finalDamage);
            if (result.Applied)
            {
                EnemyKnockbackReceiver.TryApply(damageable, center, knockback * falloffScale);
                applied++;
            }
        }

        return applied;
    }

    public static int Apply(
        Vector3 center,
        float radius,
        WeaponDamageContext damageContext,
        float falloff,
        int maxTargets = 128,
        bool showVfx = true)
    {
        return Apply(
            center,
            radius,
            damageContext,
            falloff,
            maxTargets,
            showVfx,
            observer: null,
            channel: DamageChannel.Direct,
            statusKind: WeaponStatusKind.Burn);
    }

    public static int Apply(
        Vector3 center,
        float radius,
        WeaponDamageContext damageContext,
        float falloff,
        int maxTargets,
        bool showVfx,
        IWeaponRadialDamageObserver observer,
        DamageChannel channel,
        WeaponStatusKind statusKind)
    {
        if (radius <= 0f || !damageContext.IsValid)
            return 0;

        if (showVfx && Application.isPlaying)
            ExplosionRadiusVfx.Spawn(center, radius);

        s_damaged.Clear();
        Physics.SyncTransforms();
        Collider[] hits = QueryHits(center, radius, out int hitCount);
        int applied = 0;

        for (int i = 0; i < hitCount && applied < maxTargets; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;
            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null || s_damaged.Contains(damageable))
                continue;

            s_damaged.Add(damageable);
            float distance = Vector3.Distance(center, hit.transform.position);
            float t = Mathf.Clamp01(distance / radius);
            float falloffScale = Mathf.Lerp(1f, 1f - Mathf.Clamp01(falloff), t);
            Transform target = damageable is Component damageComponent
                ? damageComponent.transform
                : hit.transform;
            Vector3 impactPosition = target != null ? target.position : hit.bounds.center;
            int finalDamage = damageContext.CalculateDamage(target, falloffScale);
            DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
                damageable,
                finalDamage,
                channel,
                statusKind);
            if (result.Applied)
            {
                EnemyKnockbackReceiver.TryApply(
                    damageable,
                    center,
                    damageContext.CalculateKnockback(finalDamage, falloffScale));
                applied++;
                if (result.IsAuthoritative && result.AppliedDamage > 0)
                {
                    observer?.OnRadialDamageApplied(
                        hit,
                        damageable,
                        target,
                        impactPosition,
                        in result,
                        in damageContext);
                }
            }
        }

        return applied;
    }

    private static Collider[] QueryHits(Vector3 center, float radius, out int hitCount)
    {
        hitCount = Physics.OverlapSphereNonAlloc(center, radius, s_smallHits);
        if (hitCount < s_smallHits.Length)
            return s_smallHits;

        hitCount = Physics.OverlapSphereNonAlloc(center, radius, s_mediumHits);
        if (hitCount < s_mediumHits.Length)
            return s_mediumHits;

        hitCount = Physics.OverlapSphereNonAlloc(center, radius, s_largeHits);
        if (hitCount == s_largeHits.Length && !s_reportedLargestBufferSaturation)
        {
            s_reportedLargestBufferSaturation = true;
            Debug.LogWarning(
                "WeaponRadialDamage overlap exceeded 4096 colliders. " +
                "Some damageable targets may be skipped; reduce collider density or split the area query.");
        }

        return s_largeHits;
    }
}
