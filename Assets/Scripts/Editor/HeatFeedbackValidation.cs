using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Runs the isolated edit-mode assertions without replacing the user's open scene.</summary>
public static class HeatFeedbackValidation
{
    [MenuItem("ScrapWaves/Heat/Validate feedback")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Heat feedback validation: stop Play mode before running isolated checks.");
            return;
        }
        int passed = 0;
        string failure = "";
        var tests = new HeatPresentationTests();
        try
        {
            tests.Warning_IsOneCrossing_RearmsBelow75_AndNeverReplaysDuringCooling(); passed++;
            tests.InitialBindingAndDirectOverheat_AreSilent_AndResetRestoresNormal(); passed++;
            tests.GameplayPrefab_HasBoundPresentation_AndPreservesFilledGauge(); passed++;
            tests.Events_Accessibility_AndDisable_KeepIndependentResourcesAndSubscriptions(); passed++;
            tests.Edges_LeaveCenterTransparent_AndDisappearAtZero(); passed++;
            tests.PreparationWarning_ReducedMotionKeepsInstructionStaticAndVisible(); passed++;
            var hud = new HudVfxTests();
            hud.Fills_KeepGameplayValues_Accessibility_AndMaterialOwnership(); passed++;
            hud.Shaders_RenderPartialAndFullFills_WithAtlasUvs(); passed++;
        }
        catch (Exception exception) { failure = exception.ToString(); Debug.LogException(exception); }
        string result = $"Heat feedback: {passed}/8 isolated checks passed.\n{failure}";
        File.WriteAllText(Path.GetFullPath("Library/HeatFeedbackValidation.txt"), result);
        if (failure.Length == 0) Debug.Log(result);
    }
}
