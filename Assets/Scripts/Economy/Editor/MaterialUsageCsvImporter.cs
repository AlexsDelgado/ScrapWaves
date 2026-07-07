#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MaterialUsageCsvImporter
{
    public static MaterialUsageBalanceSO Import(string csvPath, string outputAssetPath)
    {
        MaterialUsageParser.Parse(CsvReader.ReadAllRows(csvPath), out var assignments, out var totals);

        MaterialUsageBalanceSO asset = AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(outputAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<MaterialUsageBalanceSO>();
            AssetDatabase.CreateAsset(asset, outputAssetPath);
        }

        asset.SetData(assignments, totals);
        EditorUtility.SetDirty(asset);
        return asset;
    }
}
#endif
