using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Batch entrypoint; omit -quit because tests cross a Play Mode domain reload.</summary>
[InitializeOnLoad]
public static class PlayerAnimationValidation
{
    private const string Pending = "PlayerAnimationValidation.Pending";
    private static readonly Callbacks Callback = new();
    static PlayerAnimationValidation()
    {
        if (SessionState.GetBool(Pending, false)) TestRunnerApi.RegisterTestCallback(Callback);
    }

    public static void BuildAndTest()
    {
        PlaceholderPlayerAnimationBuilder.Build();
        BeginTests();
    }

    public static void RefitAndTest()
    {
        PlaceholderPlayerAnimationBuilder.RefitWearableSockets();
        BeginTests();
    }

    public static void RefitAndCheckClearance()
    {
        PlaceholderPlayerAnimationBuilder.RefitWearableSockets();
        CompilePlayer();
        WearableAnimationClearanceAudit.Run();
        BeginTests();
    }

    [MenuItem("Tools/ScrapWaves/Validate Placeholder Player Animation")]
    public static void BeginTests()
    {
        Run(new[]
        {
            "PlayerAnimationIntegrationTests", "PlayerAnimationSandboxTests", "PlayerRunPreviewTests", "PlayerWearableClearanceTests", "PlayerWearableSurfaceFollowerTests", "AutomaticWeaponMountTests",
            "WearableWeaponFiringTests", "PlayerWearableMountFitTests", "PlayerTorsoSkinningTests", "PlayerDirectionalDashTests", "ManualWeaponFireCooldownTests", "WeaponManagerSandboxParityTests",
            "ReticleAimProviderTests"
        }, "tests");
    }

    public static void BeginWeaponRegression() => Run(new[] { "WeaponUpgradeEffectTests" }, "weapon-upgrade-regression");

    public static void BeginRuntimePreview() => Run(new[] { "PlayerRunPreviewTests" }, "runtime-preview-tests");

    public static void BeginWearableClearance() => Run(new[] { "PlayerWearableClearanceTests" }, "wearable-clearance-tests");

    public static void CompileAndCheckWeaponRegression()
    {
        CompilePlayer();
        BeginWeaponRegression();
    }

    public static void CompileAndTest()
    {
        CompilePlayer();
        BeginTests();
    }

    private static void Run(string[] fixtures, string resultName)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Run animation validation in Edit Mode.");
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new System.InvalidOperationException("Save scene changes before animation validation.");
        Directory.CreateDirectory(PlaceholderPlayerAnimationBuilder.Output);
        SessionState.SetBool(Pending, true);
        SessionState.SetString(Pending + ".ResultName",resultName);
        TestRunnerApi.UnregisterTestCallback(Callback);
        TestRunnerApi.RegisterTestCallback(Callback);
        var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            testNames = fixtures
        }));
    }

    public static void CompilePlayer()
    {
        string output = PlaceholderPlayerAnimationBuilder.Output + "/PlayerCompilation";
        Directory.CreateDirectory(output);
        var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
        {
            target = BuildTarget.StandaloneWindows64,
            group = BuildTargetGroup.Standalone,
            options = ScriptCompilationOptions.None
        }, output);
        if (result.assemblies == null || result.assemblies.Count == 0)
            throw new System.InvalidOperationException("No player assemblies compiled.");
        File.WriteAllLines(output + "/assemblies.txt",result.assemblies);
    }

    private sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(Pending,false)) return;
            SessionState.SetBool(Pending,false);
            string output = PlaceholderPlayerAnimationBuilder.Output;
            string resultName = SessionState.GetString(Pending + ".ResultName","tests");
            TestRunnerApi.SaveResultToFile(result,Path.Combine(output,resultName + ".xml"));
            File.WriteAllText(Path.Combine(output,resultName + "-summary.txt"),
                $"{result.ResultState}; passed={result.PassCount}; failed={result.FailCount}; skipped={result.SkipCount}; duration={result.Duration:F3}s\n{result.Message}");
            TestRunnerApi.UnregisterTestCallback(this);
            if (Application.isBatchMode)
                EditorApplication.delayCall += () => EditorApplication.Exit(result.FailCount == 0 && result.PassCount > 0 ? 0 : 1);
        }
    }
}
