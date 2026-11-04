using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ShieldHudValidation
{
    [MenuItem("ScrapWaves/UI/Validate shield HUD")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode to run the isolated HUD checks.");
            return;
        }
        int passed = 0;
        string failure = "";
        try
        {
            var tests = new ShieldHudTests();
            tests.ShieldEvents_UpdateCapacityConsumptionRechargeAndDetach(); passed++;
            tests.AuthoredShield_AboveLife_HasDistinctCompartments(); passed++;
        }
        catch (Exception exception) { failure = exception.ToString(); Debug.LogException(exception); }
        string result = $"Shield HUD: {passed}/2 isolated checks passed.\n{failure}";
        File.WriteAllText(Path.GetFullPath("Library/ShieldHudValidation.txt"), result);
        if (failure.Length == 0) Debug.Log(result);
        HeatFeedbackValidation.Run();
        if (failure.Length == 0) ShieldHudPreviewCapture.Export();
    }
}
