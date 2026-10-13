using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MaterialRoleAssignment
{
    public WeaponMaterialColumn Column;
    public MaterialType Material;
    public MaterialRole Role;
}

[Serializable]
public class MaterialRoleTotalRow
{
    public MaterialRole Role;
    public int Level;
    public int Total;
}

[CreateAssetMenu(fileName = "MaterialUsageBalance", menuName = "ScrapWaves/Economy/Material Usage Balance")]
public class MaterialUsageBalanceSO : ScriptableObject
{
    private const int DefaultBasicSecondWeaponCost = 5;
    private const int DefaultBasicThirdWeaponCost = 15;
    private const int DefaultAdvancedFirstWeaponCost = 5;
    private const int DefaultAdvancedSecondWeaponCost = 15;
    private const int DefaultAdvancedThirdWeaponCost = 30;
    private const int DefaultRejectedFirstWeaponCost = 8;
    private const int DefaultRejectedSecondWeaponCost = 22;
    private const int DefaultRejectedThirdWeaponCost = 45;

    [Header("Basic tinkering: each common material")]
    [SerializeField, Min(0)]
    [Tooltip("Sheet Metal, Metal Pipe and Gears. Each material costs this amount when adding the second equipped weapon. Zero makes this tier free; negative data is treated as zero.")]
    private int _basicSecondWeaponCost = DefaultBasicSecondWeaponCost;
    [SerializeField, Min(0)]
    [Tooltip("Each of Sheet Metal, Metal Pipe and Gears when adding the third equipped weapon. Zero makes this tier free; negative data is treated as zero.")]
    private int _basicThirdWeaponCost = DefaultBasicThirdWeaponCost;
    [Header("Advanced tinkering: each rare material")]
    [SerializeField, Min(0)]
    [Tooltip("Jellified Fuel, Plastic Explosive and Wiring. Tier counts weapons already advanced, not the equipped slot. Accepting or rejecting the first offer spends this amount of each material. Zero makes this tier free; negative data is treated as zero.")]
    private int _advancedFirstWeaponCost = DefaultAdvancedFirstWeaponCost;
    [SerializeField, Min(0)]
    [Tooltip("Each rare material when one equipped weapon already has an advanced path. Zero makes this tier free; negative data is treated as zero.")]
    private int _advancedSecondWeaponCost = DefaultAdvancedSecondWeaponCost;
    [SerializeField, Min(0)]
    [Tooltip("Each rare material when two equipped weapons already have advanced paths. Zero makes this tier free; negative data is treated as zero.")]
    private int _advancedThirdWeaponCost = DefaultAdvancedThirdWeaponCost;
    [Header("After rejecting an advanced offer: each rare material")]
    [SerializeField, Min(0)]
    [Tooltip("Each rare material for the next attempt on a weapon whose offer was rejected. Independent of the initial offer price. Zero makes this tier free; negative data is treated as zero.")]
    private int _rejectedFirstWeaponCost = DefaultRejectedFirstWeaponCost;
    [SerializeField, Min(0)]
    [Tooltip("Each rare material for a rejected offer when one equipped weapon already has an advanced path. Zero makes this tier free; negative data is treated as zero.")]
    private int _rejectedSecondWeaponCost = DefaultRejectedSecondWeaponCost;
    [SerializeField, Min(0)]
    [Tooltip("Each rare material for a rejected offer when two equipped weapons already have advanced paths. Zero makes this tier free; negative data is treated as zero.")]
    private int _rejectedThirdWeaponCost = DefaultRejectedThirdWeaponCost;

    [Header("Ordinary upgrades: material roles and totals")]
    [SerializeField] private List<MaterialRoleAssignment> _roleAssignments = new();
    [SerializeField] private List<MaterialRoleTotalRow> _roleTotals = new();

    public IReadOnlyList<MaterialRoleAssignment> RoleAssignments => _roleAssignments;
    public IReadOnlyList<MaterialRoleTotalRow> RoleTotals => _roleTotals;

    // Field initializers preserve legacy prices when older assets lack these fields.
    // Zero is authored data, never a missing-value sentinel. CSV SetData only replaces
    // ordinary upgrade rows, so neither import nor runtime fallback resets these costs.
    public int GetTinkeringCommonCost(int targetSlotIndex) => Mathf.Max(0, targetSlotIndex switch
    {
        2 => _basicSecondWeaponCost,
        3 => _basicThirdWeaponCost,
        _ => 0
    });

    public int GetAdvancedTinkeringRareCost(int advancedWeaponIndex, bool rejectedOnce) => Mathf.Max(0, advancedWeaponIndex switch
    {
        1 => rejectedOnce ? _rejectedFirstWeaponCost : _advancedFirstWeaponCost,
        2 => rejectedOnce ? _rejectedSecondWeaponCost : _advancedSecondWeaponCost,
        3 => rejectedOnce ? _rejectedThirdWeaponCost : _advancedThirdWeaponCost,
        _ => 0
    });

    public static int GetDefaultTinkeringCommonCost(int targetSlotIndex) => targetSlotIndex switch
    {
        2 => DefaultBasicSecondWeaponCost,
        3 => DefaultBasicThirdWeaponCost,
        _ => 0
    };

    public static int GetDefaultAdvancedTinkeringRareCost(int advancedWeaponIndex, bool rejectedOnce) => advancedWeaponIndex switch
    {
        1 => rejectedOnce ? DefaultRejectedFirstWeaponCost : DefaultAdvancedFirstWeaponCost,
        2 => rejectedOnce ? DefaultRejectedSecondWeaponCost : DefaultAdvancedSecondWeaponCost,
        3 => rejectedOnce ? DefaultRejectedThirdWeaponCost : DefaultAdvancedThirdWeaponCost,
        _ => 0
    };

    public void SetData(List<MaterialRoleAssignment> assignments, List<MaterialRoleTotalRow> totals)
    {
        _roleAssignments = assignments ?? new List<MaterialRoleAssignment>();
        _roleTotals = totals ?? new List<MaterialRoleTotalRow>();
    }

    public MaterialRole GetRole(WeaponMaterialColumn column, MaterialType material)
    {
        for (int i = 0; i < _roleAssignments.Count; i++)
        {
            MaterialRoleAssignment entry = _roleAssignments[i];
            if (entry.Column == column && entry.Material == material)
                return entry.Role;
        }

        return MaterialRole.None;
    }

    public int GetTotalForRole(MaterialRole role, int level)
    {
        level = Mathf.Clamp(level, 1, 10);
        for (int i = 0; i < _roleTotals.Count; i++)
        {
            MaterialRoleTotalRow row = _roleTotals[i];
            if (row.Role == role && row.Level == level)
                return row.Total;
        }

        return 0;
    }

    public static WeaponMaterialColumn GetColumnForWeapon(WeaponType weaponType, WeaponUpgradePath path, int level)
    {
        if (level >= 6 && path != WeaponUpgradePath.None)
        {
            return (weaponType, path) switch
            {
                (WeaponType.Flamethrower, WeaponUpgradePath.PathA) => WeaponMaterialColumn.FlameA,
                (WeaponType.Flamethrower, WeaponUpgradePath.PathB) => WeaponMaterialColumn.FlameB,
                (WeaponType.RocketLauncher, WeaponUpgradePath.PathA) => WeaponMaterialColumn.RocketA,
                (WeaponType.RocketLauncher, WeaponUpgradePath.PathB) => WeaponMaterialColumn.RocketB,
                (WeaponType.Mortar, WeaponUpgradePath.PathA) => WeaponMaterialColumn.MortarA,
                (WeaponType.Mortar, WeaponUpgradePath.PathB) => WeaponMaterialColumn.MortarB,
                (WeaponType.AutomaticCannon, WeaponUpgradePath.PathA) => WeaponMaterialColumn.AutoA,
                (WeaponType.AutomaticCannon, WeaponUpgradePath.PathB) => WeaponMaterialColumn.AutoB,
                (WeaponType.RotatingBlade, WeaponUpgradePath.PathA) => WeaponMaterialColumn.BladesA,
                (WeaponType.RotatingBlade, WeaponUpgradePath.PathB) => WeaponMaterialColumn.BladesB,
                _ => GetBaseColumn(weaponType)
            };
        }

        return GetBaseColumn(weaponType);
    }

    private static WeaponMaterialColumn GetBaseColumn(WeaponType weaponType) => weaponType switch
    {
        WeaponType.Flamethrower => WeaponMaterialColumn.Flamethrower,
        WeaponType.RocketLauncher => WeaponMaterialColumn.RocketLauncher,
        WeaponType.Mortar => WeaponMaterialColumn.Mortar,
        WeaponType.AutomaticCannon => WeaponMaterialColumn.AutomaticCannon,
        WeaponType.RotatingBlade => WeaponMaterialColumn.RotatingBlades,
        _ => WeaponMaterialColumn.Flamethrower
    };
}
