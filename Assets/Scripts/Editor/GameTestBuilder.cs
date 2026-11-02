using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build de prueba del juego completo con las escenas activas de Build Settings, en Builds/DumpsterFire.
/// Menú: ScrapWaves/Build/Game Test Build. En batch: -executeMethod GameTestBuilder.Build.
/// </summary>
public static class GameTestBuilder
{
    private const string OutputDir = "Builds/DumpsterFire";
    private const string ExeName = "DumpsterFire.exe";

    [MenuItem("ScrapWaves/Build/Game Test Build")]
    public static void Build()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutputDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        Directory.CreateDirectory(OutputDir);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"GameTest build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors -> {Path.GetFullPath(options.locationPathName)}");
        if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
