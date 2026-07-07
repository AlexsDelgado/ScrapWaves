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
    [SerializeField] private List<MaterialRoleAssignment> _roleAssignments = new();
    [SerializeField] private List<MaterialRoleTotalRow> _roleTotals = new();

    public IReadOnlyList<MaterialRoleAssignment> RoleAssignments => _roleAssignments;
    public IReadOnlyList<MaterialRoleTotalRow> RoleTotals => _roleTotals;

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
