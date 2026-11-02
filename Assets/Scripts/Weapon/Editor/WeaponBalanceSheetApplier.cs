using UnityEditor;
using UnityEngine;

/// <summary>
/// Compatibility entry point for the retired historical sheet snapshot.
/// Current WeaponData assets are authoritative; this command must never write them.
/// </summary>
public static class WeaponBalanceSheetApplier
{
    public const string QuarantineMessage =
        "Historical weapon balance snapshot import is disabled. Current WeaponData assets are the numerical " +
        "authority. Edit weapon stats in the Inspector; the old sheet is a concept inventory only. No assets changed.";

    // Preserve the public method for old command-line invocations, without retaining any overwrite code.
    [MenuItem("Tools/ScrapWaves/Weapon Stats/Legacy Snapshot Import (Disabled)")]
    public static void ApplyFromSheetSnapshot()
    {
        Debug.LogWarning(QuarantineMessage);
    }
}
