using NUnit.Framework;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class SceneNavigationTests
{
    private const string GameplayHudV2PrefabPath = "Assets/Prefabs/UI/GameplayHud V2.prefab";
    private const string GameplayScenePath = "Assets/Scenes/GameplayScene.unity";

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

    [Test]
    public void GameplayHudV2_AuthorsRetryAndMainMenuButtonsForRunEndScreen()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayHudV2PrefabPath);
        RunEndScreenUI screen = prefab != null ? prefab.GetComponentInChildren<RunEndScreenUI>(true) : null;
        Transform runEndRoot = screen != null ? screen.transform.Find("RunEndRoot") : null;
        Transform panel = runEndRoot != null ? runEndRoot.Find("Panel") : null;
        Button retryButton = panel != null ? panel.Find("RetryButton")?.GetComponent<Button>() : null;
        Button mainMenuButton = panel != null ? panel.Find("MainMenuButton")?.GetComponent<Button>() : null;

        Assert.That(prefab, Is.Not.Null);
        Assert.That(screen, Is.Not.Null);
        Assert.That(runEndRoot, Is.Not.Null);
        Assert.That(runEndRoot.gameObject.activeSelf, Is.False);
        Assert.That(retryButton, Is.Not.Null);
        Assert.That(mainMenuButton, Is.Not.Null);
        Assert.That(mainMenuButton.GetComponentInChildren<TextMeshProUGUI>(true)?.text, Is.EqualTo("Main Menu"));
    }

    [Test]
    public void RunEndScreenUI_DoesNotConstructMissingHierarchyAtRuntime()
    {
        GameObject root = new("RunEndScreenUI_Test");
        root.SetActive(false);
        RunEndScreenUI screen = root.AddComponent<RunEndScreenUI>();
        MethodInfo awake = typeof(RunEndScreenUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

        LogAssert.Expect(LogType.Error, new Regex("authored RunEndRoot hierarchy is incomplete"));
        awake.Invoke(screen, null);

        Assert.That(root.transform.childCount, Is.Zero);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void GameplayScene_KeepsAuthoredRunEndButtonsInsideBackground()
    {
        var scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);

        try
        {
            RunEndScreenUI screen = Object.FindObjectsByType<RunEndScreenUI>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(candidate => candidate.gameObject.scene == scene && candidate.gameObject.activeInHierarchy);
            Transform runEndRoot = screen.transform.Find("RunEndRoot");
            RectTransform panel = runEndRoot.Find("Panel") as RectTransform;
            RectTransform background = panel.Find("Background") as RectTransform;
            RectTransform retryButton = panel.Find("RetryButton") as RectTransform;
            RectTransform mainMenuButton = panel.Find("MainMenuButton") as RectTransform;

            runEndRoot.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            Assert.That(mainMenuButton.GetSiblingIndex(), Is.GreaterThan(retryButton.GetSiblingIndex()));
            Assert.That(IsContainedBy(mainMenuButton, background), Is.True,
                "Main Menu must remain inside the authored run-end background after the production layout rebuilds.");
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static bool IsContainedBy(RectTransform child, RectTransform container)
    {
        Vector3[] corners = new Vector3[4];
        child.GetWorldCorners(corners);

        for (int index = 0; index < corners.Length; index++)
        {
            Vector2 localPoint = container.InverseTransformPoint(corners[index]);
            if (!container.rect.Contains(localPoint))
                return false;
        }

        return true;
    }
}
