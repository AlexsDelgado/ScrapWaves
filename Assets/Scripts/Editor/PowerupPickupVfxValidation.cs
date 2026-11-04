using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class PowerupPickupVfxValidation
{
    [MenuItem("ScrapWaves/Powerups/Validate pickup VFX")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play to run isolated pickup checks."); return; }
        string[] cases = {
            nameof(PowerupPickupVfxTests.FullHealPickup_RestoresGameplay_AndOnlyPickupStartsTheGreenEffect),
            nameof(PowerupPickupVfxTests.NukePickup_IsSeparate_StaysAtDetonation_AndRetainsConfiguredReach),
            nameof(PowerupPickupVfxTests.NukeFragments_StayClearOfMovingPlayer_AndScreenSilhouette),
            nameof(PowerupPickupVfxTests.ReducedEffects_FreezeGeometryAndLowerIntensity_ForBothPickups),
            nameof(PowerupPickupVfxTests.PauseDeathResetAndDisable_ClearOrFreezeBothEffects),
            nameof(PowerupPickupVfxTests.RepeatedPickups_ReuseResources_AndDestroyOwnedMeshes),
            nameof(PowerupPickupVfxTests.Material_IsIncludedInBuild_AndCompilesOnTheCurrentGpu)
        };
        int passed = 0; string report = "";
        foreach (string name in cases)
        {
            var test = new PowerupPickupVfxTests();
            try { test.SetUp(); typeof(PowerupPickupVfxTests).GetMethod(name).Invoke(test, null); passed++; report += "PASS " + name + "\n"; }
            catch (Exception error) { report += "FAIL " + name + "\n" + (error.InnerException ?? error) + "\n"; Debug.LogException(error.InnerException ?? error); }
            finally { test.TearDown(); }
        }
        report = $"Pickup VFX: {passed}/{cases.Length} checks passed.\n" + report;
        File.WriteAllText(Path.GetFullPath("Library/PowerupPickupVfxValidation.txt"), report);
        Debug.Log(report);
    }
}
