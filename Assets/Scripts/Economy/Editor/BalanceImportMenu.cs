#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BalanceImportMenu
{
    private const string MaterialCsv = "Assets/Data/Balance/balance_material_usage.csv";
    private const string WeaponStatsCsv = "Assets/Data/Balance/balance_weapon_stats.csv";
    private const string MaterialBalanceAsset = "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset";
    private const string WeaponSoFolder = "Assets/ScriptableObjects/WeaponSO";

    [MenuItem("ScrapWaves/Balance/Import All CSV")]
    public static void ImportAll()
    {
        if (!File.Exists(MaterialCsv) || !File.Exists(WeaponStatsCsv))
        {
            EditorUtility.DisplayDialog("Balance Import", "CSV files missing in Assets/Data/Balance.", "OK");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MaterialBalanceAsset) ?? "Assets/ScriptableObjects/Economy");
        MaterialUsageCsvImporter.Import(MaterialCsv, MaterialBalanceAsset);
        WeaponStatsCsvImporter.ImportAll(WeaponStatsCsv, WeaponSoFolder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Balance import completed.");
    }

    [MenuItem("ScrapWaves/Balance/Create Default Drop Configs")]
    public static void CreateDefaultDropConfigs()
    {
        Directory.CreateDirectory("Assets/ScriptableObjects/Economy/Drops");
        CreateDrop("JunkSlimeDrops", 0.45f,
            (MaterialType.SheetMetal, 95f), (MaterialType.JellifiedFuel, 5f));
        CreateDrop("VigilanceDroneDrops", 0.65f,
            (MaterialType.Gears, 95f), (MaterialType.PlasticExplosive, 5f));
        CreateDrop("ChaserBotDrops", 0.55f,
            (MaterialType.MetalPipe, 95f), (MaterialType.Wiring, 5f));
        CreateDrop("HellfireSlimeDrops", 0.55f,
            (MaterialType.SheetMetal, 40f), (MaterialType.JellifiedFuel, 60f));
        CreateDrop("BomberDroneDrops", 0.55f,
            (MaterialType.Gears, 40f), (MaterialType.PlasticExplosive, 60f));
        CreateDrop("ShockerBotDrops", 0.55f,
            (MaterialType.MetalPipe, 40f), (MaterialType.Wiring, 60f));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateDrop(string name, float chance, params (MaterialType, float)[] entries)
    {
        string path = $"Assets/ScriptableObjects/Economy/Drops/{name}.asset";
        MaterialDropConfig config = AssetDatabase.LoadAssetAtPath<MaterialDropConfig>(path);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<MaterialDropConfig>();
            AssetDatabase.CreateAsset(config, path);
        }

        SerializedObject so = new SerializedObject(config);
        so.FindProperty("_dropChance").floatValue = chance;
        SerializedProperty pool = so.FindProperty("_pool");
        pool.ClearArray();
        for (int i = 0; i < entries.Length; i++)
        {
            pool.InsertArrayElementAtIndex(i);
            SerializedProperty element = pool.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("Material").enumValueIndex = (int)entries[i].Item1;
            element.FindPropertyRelative("WeightPercent").floatValue = entries[i].Item2;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
    }
}
#endif
