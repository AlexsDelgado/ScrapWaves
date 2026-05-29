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

        string rootName = target.root != null ? target.root.name : target.name;
        if (rootName.Contains("Boss", StringComparison.OrdinalIgnoreCase))
            return WeaponEnemyKind.Boss;

        if (rootName.Contains("Elite", StringComparison.OrdinalIgnoreCase)
            || rootName.Contains("variant", StringComparison.OrdinalIgnoreCase))
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
