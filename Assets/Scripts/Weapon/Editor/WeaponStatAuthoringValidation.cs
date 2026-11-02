using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Explicit validation only. In batch mode use -executeMethod and omit -quit:
/// test fixtures can enter Play Mode, and the callback exits after teardown.
/// Never saves assets or scenes. Player compilation is a script check, not a complete player build.
/// </summary>
[InitializeOnLoad]
public static class WeaponStatAuthoringValidation
{
    public const string OutputDirectory = "Library/CodexValidation/WeaponStatAuthoring";
    private const string Pending = "WeaponStatAuthoringValidation.Pending";
    private static readonly Callbacks Callback = new();
    private static readonly string[] BaselineFixtures =
    {
        "WeaponBalanceSnapshotQuarantineTests", "WeaponBalanceStartupTests",
        "WeaponUpgradeMathTests", "WeaponDiagnosticsTests"
    };
    private static readonly string[] AuthoringFixtures =
    {
        "WeaponStatAuthoringTests", "WeaponModeResolutionTests", "WeaponStatMigrationTests", "WeaponStatValidationPerformanceTests",
        "WeaponStatCapabilitiesTests", "WeaponLegacyStatVisibilityTests", "CannonMortarStatConsumerTests", "WeaponFlameStatAuthoringTests",
        "WeaponRocketStatAuthoringTests", "WeaponBladeStatAuthoringTests", "AutomaticCannonFireLogicTests",
        "WeaponPresentationConsumerTests", "CraftingGameplayPreviewTests", "GameplayAimTests", "WeaponManagerSandboxParityTests"
    };

    [Serializable]
    private sealed class AssetFile
    {
        public string Path;
        public string Sha256;
        public long Length;
    }

    [Serializable]
    private sealed class Evidence
    {
        public string CapturedUtc;
        public string UnityVersion;
        public string[] Fixtures;
        public AssetFile[] Files;
    }

    static WeaponStatAuthoringValidation()
    {
        if (SessionState.GetBool(Pending, false))
            TestRunnerApi.RegisterTestCallback(Callback);
    }

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Validate Authoring and Consumers")]
    public static void BeginTests()
    {
        string overrideNames = Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_TESTS");
        string[] fixtures = string.IsNullOrWhiteSpace(overrideNames)
            ? BaselineFixtures.Concat(AuthoringFixtures).ToArray()
            : overrideNames.Split(';').Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().ToArray();
        Run(fixtures, "authoring-tests");
    }

    public static void BeginBaselineTests() => Run(BaselineFixtures, "baseline-tests");

    public static void CompileAndTest()
    {
        CompilePlayerScripts();
        BeginTests();
    }

    public static void CompileAndCheckBaseline()
    {
        CompilePlayerScripts();
        BeginBaselineTests();
    }

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Build Windows Player")]
    public static void BuildWindowsPlayer()
    {
        RequireCleanEditMode();
        string output = GetOutput();
        Directory.CreateDirectory(output);
        Evidence before = Capture(Array.Empty<string>());
        File.WriteAllText(Path.Combine(output, "player-build-before.json"), JsonUtility.ToJson(before, true));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = Path.Combine(output, "WindowsPlayer", "ScrapWaves.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        string[] changes = Compare(before, Capture(Array.Empty<string>()));
        File.WriteAllText(Path.Combine(output, "player-build-summary.txt"),
            $"Result: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\n" +
            $"Duration: {report.summary.totalTime}\nOutput: {report.summary.outputPath}\n" +
            "Authored asset changes: " + (changes.Length == 0 ? "none" : string.Join(", ", changes)));
        if (changes.Length > 0)
            throw new InvalidOperationException("Weapon asset bytes changed during player build: " + string.Join(", ", changes));
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows player build failed; see the Unity log and player-build-summary.txt.");
    }

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Compile Player Scripts")]
    public static void CompilePlayerScripts()
    {
        RequireCleanEditMode();
        string output = GetOutput();
        string directory = Path.Combine(output, "PlayerScripts");
        Directory.CreateDirectory(directory);
        Evidence before = Capture(Array.Empty<string>());
        File.WriteAllText(Path.Combine(output, "player-compilation-before.json"), JsonUtility.ToJson(before, true));
        ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(
            new ScriptCompilationSettings
            {
                target = BuildTarget.StandaloneWindows64,
                group = BuildTargetGroup.Standalone,
                options = ScriptCompilationOptions.None
            }, directory);
        string[] changes = Compare(before, Capture(Array.Empty<string>()));
        if (changes.Length > 0)
            throw new InvalidOperationException("Weapon asset bytes changed during player compilation: " + string.Join(", ", changes));
        if (result.assemblies == null || result.assemblies.Count == 0)
            throw new InvalidOperationException("Player script compilation returned no assemblies.");
        File.WriteAllLines(Path.Combine(output, "player-compilation-assemblies.txt"), result.assemblies);
        Debug.Log($"Weapon stat validation: {result.assemblies.Count} Windows player script assemblies compiled; authored assets unchanged. This is not a full player build.");
    }

    private static void Run(string[] fixtures, string resultName)
    {
        RequireCleanEditMode();
        if (SessionState.GetBool(Pending, false))
            throw new InvalidOperationException("Weapon stat validation is already running.");
        if (fixtures.Length == 0)
            throw new InvalidOperationException("No weapon stat validation fixtures were selected.");
        // Do not quietly accept a misspelled/missing fixture and claim a passing focused suite.
        foreach (string fixture in fixtures)
            if (typeof(WeaponStatAuthoringValidation).Assembly.GetType(fixture) == null)
                throw new InvalidOperationException("Missing weapon stat test fixture: " + fixture);

        string output = GetOutput();
        Directory.CreateDirectory(output);
        Evidence before = Capture(fixtures);
        File.WriteAllText(Path.Combine(output, resultName + "-before.json"), JsonUtility.ToJson(before, true));
        SessionState.SetString(Pending + ".Evidence", JsonUtility.ToJson(before));
        SessionState.SetString(Pending + ".Output", output);
        SessionState.SetString(Pending + ".ResultName", resultName);
        SessionState.SetBool(Pending, true);
        TestRunnerApi.UnregisterTestCallback(Callback);
        TestRunnerApi.RegisterTestCallback(Callback);
        try
        {
            var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, testNames = fixtures }));
        }
        catch
        {
            SessionState.SetBool(Pending, false);
            TestRunnerApi.UnregisterTestCallback(Callback);
            throw;
        }
    }

    private static string GetOutput()
    {
        string requested = Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_OUTPUT");
        return string.IsNullOrWhiteSpace(requested) ? OutputDirectory : requested;
    }

    private static void RequireCleanEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run weapon stat validation in Edit Mode.");
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Save or close edited scenes before validation; the runner never saves user scenes.");
    }

    private static Evidence Capture(string[] fixtures)
    {
        string[] paths = AssetDatabase.FindAssets("t:WeaponData").Select(AssetDatabase.GUIDToAssetPath)
            .Distinct().OrderBy(path => path).ToArray();
        if (paths.Length == 0)
            throw new InvalidOperationException("No WeaponData assets found for preservation evidence.");
        return new Evidence
        {
            CapturedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion, Fixtures = fixtures,
            Files = paths.SelectMany(path => new[] { path, path + ".meta" }).Select(path =>
            {
                using var sha = SHA256.Create();
                byte[] bytes = File.ReadAllBytes(path);
                return new AssetFile { Path = path, Length = bytes.Length, Sha256 = Convert.ToBase64String(sha.ComputeHash(bytes)) };
            }).ToArray()
        };
    }

    private static string[] Compare(Evidence before, Evidence after)
    {
        var expected = before.Files.ToDictionary(file => file.Path);
        var actual = after.Files.ToDictionary(file => file.Path);
        return expected.Keys.Union(actual.Keys).Where(path => !expected.ContainsKey(path) || !actual.ContainsKey(path)
            || expected[path].Sha256 != actual[path].Sha256 || expected[path].Length != actual[path].Length).ToArray();
    }

    private sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            string output = SessionState.GetString(Pending + ".Output", OutputDirectory);
            string resultName = SessionState.GetString(Pending + ".ResultName", "authoring-tests");
            int exitCode = 1;
            try
            {
                Evidence before = JsonUtility.FromJson<Evidence>(SessionState.GetString(Pending + ".Evidence", ""));
                Evidence after = Capture(before.Fixtures);
                string[] changed = Compare(before, after);
                TestRunnerApi.SaveResultToFile(result, Path.Combine(output, resultName + ".xml"));
                File.WriteAllText(Path.Combine(output, resultName + "-after.json"), JsonUtility.ToJson(after, true));
                string preservation = changed.Length == 0 ? "All authored asset and metadata bytes unchanged."
                    : "FAILED asset preservation: " + string.Join(", ", changed);
                File.WriteAllText(Path.Combine(output, resultName + "-summary.txt"),
                    $"{result.ResultState}; passed={result.PassCount}; failed={result.FailCount}; skipped={result.SkipCount}; duration={result.Duration:F3}s\n{preservation}\n{result.Message}");
                exitCode = result.FailCount == 0 && result.PassCount > 0 && changed.Length == 0 ? 0 : 1;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText(Path.Combine(output, resultName + "-evidence-error.txt"), exception.ToString());
            }
            finally
            {
                TestRunnerApi.UnregisterTestCallback(this);
                SessionState.EraseString(Pending + ".Evidence");
                if (Application.isBatchMode)
                {
                    int code = exitCode;
                    EditorApplication.delayCall += () => EditorApplication.Exit(code);
                }
            }
        }
    }
}
