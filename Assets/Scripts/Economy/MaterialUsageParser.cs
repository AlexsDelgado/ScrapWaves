using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class MaterialUsageParser
{
    private static readonly Dictionary<string, WeaponMaterialColumn> ColumnLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Flamethrower", WeaponMaterialColumn.Flamethrower },
        { "Rocket launcher", WeaponMaterialColumn.RocketLauncher },
        { "Mortar", WeaponMaterialColumn.Mortar },
        { "Autocannon", WeaponMaterialColumn.AutomaticCannon },
        { "Rotating Blades", WeaponMaterialColumn.RotatingBlades },
        { "Flame A", WeaponMaterialColumn.FlameA },
        { "Flame B", WeaponMaterialColumn.FlameB },
        { "Rocket A", WeaponMaterialColumn.RocketA },
        { "Rocket B", WeaponMaterialColumn.RocketB },
        { "Mortar A", WeaponMaterialColumn.MortarA },
        { "Mortar B", WeaponMaterialColumn.MortarB },
        { "Auto A", WeaponMaterialColumn.AutoA },
        { "Auto B", WeaponMaterialColumn.AutoB },
        { "Blades A", WeaponMaterialColumn.BladesA },
        { "Blades B", WeaponMaterialColumn.BladesB }
    };

    public static void Parse(List<string[]> rows, out List<MaterialRoleAssignment> assignments, out List<MaterialRoleTotalRow> totals)
    {
        assignments = new List<MaterialRoleAssignment>();
        totals = new List<MaterialRoleTotalRow>();
        if (rows == null || rows.Count == 0)
            throw new InvalidOperationException("Material usage CSV is empty.");

        string[] header = rows[0];
        var columns = new List<(int index, WeaponMaterialColumn column)>();
        for (int i = 1; i < header.Length; i++)
        {
            string name = header[i]?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            if (ColumnLookup.TryGetValue(name, out WeaponMaterialColumn column))
                columns.Add((i, column));
        }

        int quantitiesRowIndex = -1;
        for (int r = 1; r < rows.Count; r++)
        {
            string first = rows[r].Length > 0 ? rows[r][0].Trim() : string.Empty;
            if (first.Equals("Cantidades/Nivel", StringComparison.OrdinalIgnoreCase))
            {
                quantitiesRowIndex = r;
                break;
            }

            if (!MaterialCatalog.TryParse(first, out MaterialType material))
                continue;

            for (int c = 0; c < columns.Count; c++)
            {
                (int index, WeaponMaterialColumn column) entry = columns[c];
                if (entry.index >= rows[r].Length)
                    continue;

                MaterialRole role = MaterialCatalog.ParseRole(rows[r][entry.index]);
                if (role == MaterialRole.None)
                    continue;

                assignments.Add(new MaterialRoleAssignment
                {
                    Column = entry.column,
                    Material = material,
                    Role = role
                });
            }
        }

        if (quantitiesRowIndex < 0)
            throw new InvalidOperationException("Could not find Cantidades/Nivel row.");

        string[] levelHeader = rows[quantitiesRowIndex];
        var levelColumns = new List<(int index, int level)>();
        for (int i = 1; i < levelHeader.Length; i++)
        {
            string label = levelHeader[i]?.Trim();
            if (string.IsNullOrEmpty(label))
                continue;

            if (label.Equals("Base", StringComparison.OrdinalIgnoreCase))
            {
                levelColumns.Add((i, 1));
                continue;
            }

            if (label.Contains("Tinkering", StringComparison.OrdinalIgnoreCase))
                continue;

            if (float.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out float levelFloat))
                levelColumns.Add((i, Mathf.RoundToInt(levelFloat)));
        }

        for (int r = quantitiesRowIndex + 1; r < rows.Count; r++)
        {
            string rowName = rows[r].Length > 0 ? rows[r][0].Trim() : string.Empty;
            MaterialRole? role = rowName switch
            {
                var s when s.StartsWith("Total principal", StringComparison.OrdinalIgnoreCase) => MaterialRole.Principal,
                var s when s.StartsWith("Total secundario", StringComparison.OrdinalIgnoreCase) => MaterialRole.Secondary,
                var s when s.StartsWith("Total terciario", StringComparison.OrdinalIgnoreCase) => MaterialRole.Tertiary,
                var s when s.StartsWith("Total prin. extra", StringComparison.OrdinalIgnoreCase) => MaterialRole.PrincipalExtra,
                _ => null
            };

            if (role == null)
                continue;

            for (int i = 0; i < levelColumns.Count; i++)
            {
                (int index, int level) col = levelColumns[i];
                if (col.index >= rows[r].Length)
                    continue;

                string cell = rows[r][col.index]?.Trim();
                if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
                {
                    if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float totalFloat))
                        continue;
                    total = Mathf.RoundToInt(totalFloat);
                }

                totals.Add(new MaterialRoleTotalRow
                {
                    Role = role.Value,
                    Level = col.level,
                    Total = total
                });
            }
        }
    }
}
