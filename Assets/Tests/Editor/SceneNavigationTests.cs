using NUnit.Framework;
using System.Reflection;
using UnityEngine;

public class SceneNavigationTests
{
    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
    }

    [TestCase(SceneDestination.Title, "TitleScreen")]
    [TestCase(SceneDestination.Play, "GameplayScene")]
    [TestCase(SceneDestination.WeaponSandbox, "WeaponTestingSandbox")]
    [TestCase(SceneDestination.EnemiesTesting, "enemiesTesting")]
    public void GetSceneName_ReturnsCanonicalSceneName(SceneDestination destination, string expectedSceneName)
    {
        Assert.That(SceneNavigation.GetSceneName(destination), Is.EqualTo(expectedSceneName));
    }

    [Test]
    public void PrepareForSceneChange_ResetsPausedTimeScale()
    {
        Time.timeScale = 0f;

        SceneNavigation.PrepareForSceneChange();

        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [TestCase(SceneDestination.Title, "Assets/Scenes/TitleScreen.unity")]
    [TestCase(SceneDestination.Play, "Assets/Scenes/GameplayScene.unity")]
    [TestCase(SceneDestination.WeaponSandbox, "Assets/Scenes/Testing/WeaponTestingSandbox.unity")]
    [TestCase(SceneDestination.EnemiesTesting, "Assets/Scenes/Testing/enemiesTesting.unity")]
    public void GetScenePath_ReturnsCanonicalScenePath(SceneDestination destination, string expectedScenePath)
    {
        MethodInfo method = typeof(SceneNavigation).GetMethod("GetScenePath", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { destination }), Is.EqualTo(expectedScenePath));
    }
}
