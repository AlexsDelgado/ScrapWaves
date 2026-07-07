using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SceneDestination
{
    Title,
    Play,
    WeaponSandbox,
    EnemiesTesting
}

public static class SceneNavigation
{
    public const string TitleSceneName = "TitleScreen";
    public const string PlaySceneName = "GameplayScene";
    public const string WeaponSandboxSceneName = "WeaponTestingSandbox";
    public const string EnemiesTestingSceneName = "enemiesTesting";

    public static string GetSceneName(SceneDestination destination)
    {
        return destination switch
        {
            SceneDestination.Title => TitleSceneName,
            SceneDestination.Play => PlaySceneName,
            SceneDestination.WeaponSandbox => WeaponSandboxSceneName,
            SceneDestination.EnemiesTesting => EnemiesTestingSceneName,
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown scene destination.")
        };
    }

    public static void PrepareForSceneChange()
    {
        Time.timeScale = 1f;
    }

    public static bool Load(SceneDestination destination)
    {
        string sceneName = GetSceneName(destination);
        string scenePath = GetScenePath(destination);
        int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
        if (buildIndex < 0)
        {
            Debug.LogError($"SceneNavigation cannot load '{sceneName}' at path '{scenePath}' for destination '{destination}' because it is not enabled in Build Settings.");
            return false;
        }

        PrepareForSceneChange();
        SceneManager.LoadScene(buildIndex);
        return true;
    }

    public static bool LoadTitle()
    {
        return Load(SceneDestination.Title);
    }

    public static bool LoadPlay()
    {
        return Load(SceneDestination.Play);
    }

    public static bool LoadWeaponSandbox()
    {
        return Load(SceneDestination.WeaponSandbox);
    }

    public static bool LoadEnemiesTesting()
    {
        return Load(SceneDestination.EnemiesTesting);
    }

    private static string GetScenePath(SceneDestination destination)
    {
        return destination switch
        {
            SceneDestination.Title => "Assets/Scenes/TitleScreen.unity",
            SceneDestination.Play => "Assets/Scenes/GameplayScene.unity",
            SceneDestination.WeaponSandbox => "Assets/Scenes/WeaponTestingSandbox.unity",
            SceneDestination.EnemiesTesting => "Assets/Scenes/enemiesTesting.unity",
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown scene destination.")
        };
    }
}
