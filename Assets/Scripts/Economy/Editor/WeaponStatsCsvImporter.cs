#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class WeaponStatsCsvImporter
{
    public static void ImportAll(string csvPath, string weaponSoFolder)
    {
        List<string[]> rows = CsvReader.ReadAllRows(csvPath);
        var assets = new List<WeaponData>();
        foreach (WeaponType type in new[] { WeaponType.Flamethrower, WeaponType.RocketLauncher, WeaponType.Mortar, WeaponType.AutomaticCannon, WeaponType.RotatingBlade })
        {
            string assetName = type switch
            {
                WeaponType.Flamethrower => "Flamethrower",
                WeaponType.RocketLauncher => "RocketLauncher",
                WeaponType.Mortar => "Mortar",
                WeaponType.AutomaticCannon => "AutomaticCannon",
                WeaponType.RotatingBlade => "RotatingBlade",
                _ => type.ToString()
            };

            WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>($"{weaponSoFolder}/{assetName}.asset");
            if (data != null)
                assets.Add(data);
        }

        WeaponStatsParser.ImportAll(rows, assets);
        for (int i = 0; i < assets.Count; i++)
            EditorUtility.SetDirty(assets[i]);
    }
}
#endif
