#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BalanceAutoImporter
{
    private const string MaterialCsv = "Assets/Data/Balance/balance_material_usage.csv";
    private const string WeaponStatsCsv = "Assets/Data/Balance/balance_weapon_stats.csv";
    private const string MaterialBalanceAsset = "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset";

    static BalanceAutoImporter()
    {
        EditorApplication.delayCall += TryImportIfMissing;
    }

    private static void TryImportIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!System.IO.File.Exists(MaterialCsv) || !System.IO.File.Exists(WeaponStatsCsv))
            return;

        if (AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(MaterialBalanceAsset) != null)
            return;

        BalanceImportMenu.ImportAll();
        BalanceImportMenu.CreateDefaultDropConfigs();
    }
}
#endif
