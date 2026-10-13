using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, idempotent scaffolding. Never imports historical balance values.</summary>
public static class WeaponStatMigration
{
    [Serializable] public sealed class Preview
    {
        public string AssetPath;
        public string AssetGuid;
        public string Sha256;
        public int ExistingRows;
        public int MissingRows;
        public string Policy = "Add disabled inherited rows only; retain every authored numeric value, reference, and hidden row.";
    }

    public static Preview DryRun(WeaponData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        Validate(data);
        string path = AssetDatabase.GetAssetPath(data);
        string hash = "Unsaved preview copy";
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            using var sha = SHA256.Create();
            hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        return new Preview { AssetPath = path, AssetGuid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path),
            Sha256 = hash, ExistingRows = data.StatProfile?.Levels.Count ?? 0, MissingRows = Missing(data).Count };
    }

    public static int ApplyInheritedRows(WeaponData data)
    {
        Validate(data);
        List<WeaponStatLevelRow> rows = Missing(data);
        if (rows.Count == 0) return 0;
        Undo.RecordObject(data, "Add inherited weapon progression rows");
        data.StatProfile ??= new WeaponStatProfile();
        data.StatProfile.Levels.AddRange(rows);
        EditorUtility.SetDirty(data);
        return rows.Count;
    }

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Preview Selected Weapon Migration")]
    public static void PreviewSelected()
    {
        foreach (WeaponData data in Selected()) Debug.Log(JsonUtility.ToJson(DryRun(data), true), data);
    }

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Add Inherited Rows to Selected Weapons")]
    public static void ApplySelected()
    {
        int rows = 0;
        foreach (WeaponData data in Selected()) rows += ApplyInheritedRows(data);
        Debug.Log($"Added {rows} inherited weapon rows. Numeric tuning is unchanged. Changes support Undo and are not automatically saved.");
    }

    private static WeaponData[] Selected()
    {
        WeaponData[] assets = Selection.objects.OfType<WeaponData>().ToArray();
        if (assets.Length == 0) throw new InvalidOperationException("Select one or more WeaponData assets first.");
        return assets;
    }

    private static void Validate(WeaponData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.StatProfile != null && !data.StatProfile.TryValidate(data.WeaponType, out string error))
            throw new InvalidOperationException("Migration cannot rewrite an invalid or unknown-version profile: " + error);
    }

    private static List<WeaponStatLevelRow> Missing(WeaponData data)
    {
        var result = new List<WeaponStatLevelRow>();
        foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
        for (int level = path == WeaponUpgradePath.None ? 1 : 6; level <= (path == WeaponUpgradePath.None ? 5 : 10); level++)
        {
            bool exists = data.StatProfile?.Levels.Any(row => row.WeaponType == data.WeaponType && row.Level == level && row.Path == path) == true;
            if (!exists) result.Add(WeaponStatProfile.CreateRow(data.WeaponType, level, path));
        }
        return result;
    }
}
