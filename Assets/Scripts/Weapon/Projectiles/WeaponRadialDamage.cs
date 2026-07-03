using System.Collections.Generic;
using UnityEngine;

public static class WeaponRadialDamage
{
    private static readonly List<IDamageable> s_damaged = new(128);

    public static int Apply(Vector3 center, float radius, int damage, float falloff, float knockback, int maxTargets = 128, bool showVfx = true)
    {
        if (radius <= 0f || damage <= 0)
            return 0;

        if (showVfx && Application.isPlaying)
            ExplosionRadiusVfx.Spawn(center, radius);

        s_damaged.Clear();
        Physics.SyncTransforms();
        Collider[] hits = Physics.OverlapSphere(center, radius);
        int applied = 0;

        for (int i = 0; i < hits.Length && applied < maxTargets; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null || s_damaged.Contains(damageable))
                continue;

            s_damaged.Add(damageable);
            float distance = Vector3.Distance(center, hits[i].transform.position);
            float t = Mathf.Clamp01(distance / radius);
            float falloffScale = Mathf.Lerp(1f, 1f - Mathf.Clamp01(falloff), t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * falloffScale));
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
            {
                EnemyKnockbackReceiver.TryApply(damageable, center, knockback * falloffScale);
                applied++;
            }
        }

        return applied;
    }
}
