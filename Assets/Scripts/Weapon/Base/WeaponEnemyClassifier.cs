using System;
using UnityEngine;

public enum WeaponEnemyKind
{
    Normal,
    Elite,
    Boss
}

public interface IWeaponEnemyMetadata
{
    WeaponEnemyKind WeaponEnemyKind { get; }
    bool CountsAsEliteOrBoss { get; }
}

public static class WeaponEnemyClassifier
{
    public static WeaponEnemyKind GetKind(Transform target)
    {
        if (target == null)
            return WeaponEnemyKind.Normal;

        IWeaponEnemyMetadata metadata = target.GetComponentInParent<IWeaponEnemyMetadata>();
        if (metadata != null)
            return metadata.WeaponEnemyKind;

        // Prefer the EnemyHealth object name; pooled enemies live under a holder root
        // like "[PooledEnemies]" that would hide "Elite"/"Boss"/"Stalker" tokens.
        EnemyHealth health = target.GetComponentInParent<EnemyHealth>();
        string probeName = health != null
            ? health.name
            : (target.root != null ? target.root.name : target.name);

        if (probeName.Contains("Boss", StringComparison.OrdinalIgnoreCase)
            || probeName.Contains("Stalker", StringComparison.OrdinalIgnoreCase)
            || probeName.Contains("Destroyer", StringComparison.OrdinalIgnoreCase))
            return WeaponEnemyKind.Boss;

        if (target.GetComponentInParent<BossKeyDrop>() != null)
            return WeaponEnemyKind.Boss;

        if (probeName.Contains("Elite", StringComparison.OrdinalIgnoreCase)
            || probeName.Contains("variant", StringComparison.OrdinalIgnoreCase))
            return WeaponEnemyKind.Elite;

        return WeaponEnemyKind.Normal;
    }

    public static bool CountsAsEliteOrBoss(Transform target)
    {
        if (target == null)
            return false;

        IWeaponEnemyMetadata metadata = target.GetComponentInParent<IWeaponEnemyMetadata>();
        if (metadata != null)
            return metadata.CountsAsEliteOrBoss;

        WeaponEnemyKind kind = GetKind(target);
        return kind == WeaponEnemyKind.Elite || kind == WeaponEnemyKind.Boss;
    }
}
