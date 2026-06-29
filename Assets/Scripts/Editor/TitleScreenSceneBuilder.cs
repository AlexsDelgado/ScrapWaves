using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TitleScreenSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/TitleScreen.unity";

    [MenuItem("Tools/Scenes/Rebuild Title Screen")]
    public static void Rebuild()
    {
        SceneSetup();
        ApplyBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void Verify()
    {
        RunVerification("SceneNavigationTests", VerifySceneNavigation);
        RunVerification("TitleScreenControllerTests", VerifyTitleScreenController);
        RunVerification("PauseMenuUITests", VerifyPauseMenu);
        Debug.Log("Title screen verification passed.");
    }

    private static void SceneSetup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject root = new("TitleScreenRoot");
        root.AddComponent<TitleScreenController>();

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene, ScenePath);
    }

    private static void ApplyBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/WeaponTestingSandbox.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/enemiesTesting.unity", true)
        };
    }

    private static void VerifySceneNavigation()
    {
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.Title, "TitleScreen"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.Play, "SampleScene"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.WeaponSandbox, "WeaponTestingSandbox"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetSceneName_ReturnsCanonicalSceneName(SceneDestination.EnemiesTesting, "enemiesTesting"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.PrepareForSceneChange_ResetsPausedTimeScale());
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.Title, "Assets/Scenes/TitleScreen.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.Play, "Assets/Scenes/SampleScene.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.WeaponSandbox, "Assets/Scenes/WeaponTestingSandbox.unity"));
        RunFixture(
            () => new SceneNavigationTests(),
            test => test.GetScenePath_ReturnsCanonicalScenePath(SceneDestination.EnemiesTesting, "Assets/Scenes/enemiesTesting.unity"));
    }

    private static void VerifyTitleScreenController()
    {
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_CreatesExactlyThreeMenuButtonsWithExpectedLabels());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_CreatesEventSystemWhenMissing());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.Awake_WiresButtonsToSharedSceneNavigationMethods());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.EnabledBuildSettingsOrder_MatchesTitleScreenBootFlow());
        RunFixture(
            () => new TitleScreenControllerTests(),
            test => test.TitleScreenScene_IsMinimalBootSceneWithControllerRoot());
    }

    private static void VerifyPauseMenu()
    {
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.Awake_CreatesResumeAndReturnToTitleButtons());
        RunFixture(
            () => new PauseMenuUITests(),
            test => test.Awake_WiresPauseButtonsToExpectedHandlers());
    }

    private static void RunVerification(string name, Action action)
    {
        try
        {
            action();
            Debug.Log($"{name}: PASS");
        }
        catch (Exception exception)
        {
            Debug.LogError($"{name}: FAIL\n{exception}");
            throw;
        }
    }

    private static void RunFixture<T>(Func<T> createFixture, Action<T> runTest) where T : class
    {
        T fixture = createFixture();

        fixture.GetType().GetMethod("SetUp")?.Invoke(fixture, null);
        try
        {
            runTest(fixture);
        }
        finally
        {
            fixture.GetType().GetMethod("TearDown")?.Invoke(fixture, null);
        }
    }
}
