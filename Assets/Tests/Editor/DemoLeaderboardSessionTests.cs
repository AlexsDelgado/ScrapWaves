using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DemoLeaderboardSessionTests
{
    private const string SaveDirectoryVariable = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private string _previousSaveDirectory;
    private string _testDirectory;
    private float _previousRunStart;
    private int _previousKills;
    private int _previousBossKills;
    private TutorialDirector _previousTutorial;
    private GameObject _tutorialObject;

    [SetUp]
    public void SetUp()
    {
        _previousSaveDirectory = Environment.GetEnvironmentVariable(SaveDirectoryVariable);
        _testDirectory = Path.Combine(Path.GetTempPath(), "ScrapWavesLeaderboardSessionTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(SaveDirectoryVariable, _testDirectory);
        _previousRunStart = (float)typeof(RunSessionStats).GetField("_runStartTime", PrivateStatic).GetValue(null);
        _previousKills = RunCombatStats.EnemiesEliminated;
        _previousBossKills = RunSessionStats.BossKills;
        _previousTutorial = TutorialDirector.Instance;
        SetTutorial(null);
        DemoLeaderboardSession.ResetForMenu();
    }

    [TearDown]
    public void TearDown()
    {
        DemoLeaderboardSession.ResetForMenu();
        if (_tutorialObject != null) Object.DestroyImmediate(_tutorialObject);
        SetTutorial(_previousTutorial);
        typeof(RunSessionStats).GetField("_runStartTime", PrivateStatic).SetValue(null, _previousRunStart);
        typeof(RunCombatStats).GetField("_enemiesEliminated", PrivateStatic).SetValue(null, _previousKills);
        typeof(RunSessionStats).GetField("_bossKills", PrivateStatic).SetValue(null, _previousBossKills);
        Environment.SetEnvironmentVariable(SaveDirectoryVariable, _previousSaveDirectory);
        if (Directory.Exists(_testDirectory))
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScrapWavesLeaderboardSessionTests")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(_testDirectory);
            Assert.That(target.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
            Directory.Delete(target, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CompletedRun_SnapshotsResultsOnceForEitherOutcome(bool victory)
    {
        BeginNamedRun("  Ada  B  ");
        SetStats(7, 42.75f);
        DemoLeaderboardSession.SubmitResult(victory);
        SetStats(90, 900f);
        DemoLeaderboardSession.SubmitResult(!victory);

        DemoLeaderboardData data = new DemoLeaderboardStore().Load();
        Assert.That(data.MostKills.Count, Is.EqualTo(1));
        Assert.That(data.LongestSurvival.Count, Is.EqualTo(1));
        DemoLeaderboardEntry entry = data.MostKills[0];
        Assert.That(entry.Name, Is.EqualTo("Ada B"));
        Assert.That(entry.Kills, Is.EqualTo(7));
        Assert.That(entry.SurvivalSeconds, Is.EqualTo(42));
        Assert.That(entry.Victory, Is.EqualTo(victory));
        Assert.That(Guid.TryParseExact(entry.RunId, "N", out _), Is.True);
        Assert.That(data.LongestSurvival[0].RunId, Is.EqualTo(entry.RunId));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void DisabledOrInvalidMenuLaunch_DoesNotReadOrWriteScores(string name)
    {
        DemoLeaderboardSession.PrepareMenuLaunch(name);
        ArmRoute(SceneDestination.Play, SceneNavigation.TitleSceneName);
        DemoLeaderboardSession.BeginGameplay();
        SetStats(50, 500f);
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [Test]
    public void NameWithoutValidatedRoute_DoesNotQualifyDirectSceneEntry()
    {
        DemoLeaderboardSession.PrepareMenuLaunch("Ada");
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [TestCase(SceneDestination.WeaponSandbox, SceneNavigation.TitleSceneName)]
    [TestCase(SceneDestination.EnemiesTesting, SceneNavigation.TitleSceneName)]
    [TestCase(SceneDestination.Play, SceneNavigation.WeaponSandboxSceneName)]
    [TestCase(SceneDestination.Play, SceneNavigation.PlaySceneName)]
    public void NonMenuGameplayRoute_CannotConsumeNamedAttempt(SceneDestination destination, string source)
    {
        DemoLeaderboardSession.PrepareMenuLaunch("Ada");
        ArmRoute(destination, source);
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [Test]
    public void CanceledOrFailedLaunch_CannotLeakIntoALaterDirectEntry()
    {
        DemoLeaderboardSession.PrepareMenuLaunch("Ada");
        ArmRoute(SceneDestination.Play, SceneNavigation.TitleSceneName);
        DemoLeaderboardSession.CancelPendingLaunch();
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [Test]
    public void PendingLaunch_IsConsumedByOnlyOneGameplayEntry()
    {
        BeginNamedRun("Ada");
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [Test]
    public void Retry_KeepsNameButCreatesNewAttemptAndAcceptsImprovement()
    {
        BeginNamedRun("Ada");
        SetStats(2, 12f);
        DemoLeaderboardSession.SubmitResult(false);
        string firstId = new DemoLeaderboardStore().Load().MostKills[0].RunId;

        DemoLeaderboardSession.PrepareRetry();
        DemoLeaderboardSession.BeginGameplay();
        SetStats(5, 20f);
        DemoLeaderboardSession.SubmitResult(true);

        DemoLeaderboardData data = new DemoLeaderboardStore().Load();
        Assert.That(data.MostKills.Count, Is.EqualTo(1));
        Assert.That(data.MostKills[0].Name, Is.EqualTo("Ada"));
        Assert.That(data.MostKills[0].Kills, Is.EqualTo(5));
        Assert.That(data.MostKills[0].RunId, Is.Not.EqualTo(firstId));
        Assert.That(data.LongestSurvival[0].SurvivalSeconds, Is.EqualTo(20));
    }

    [Test]
    public void TutorialDeath_IsTerminalWithoutScore_ButRetryCanQualifyAfterLesson()
    {
        BeginNamedRun("Ada");
        SetTutorialActive(true);
        SetStats(15, 200f);
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
        SetTutorialActive(false);
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();

        DemoLeaderboardSession.PrepareRetry();
        DemoLeaderboardSession.BeginGameplay();
        RunSessionStats.Reset(); // The tutorial completion boundary keeps the named attempt.
        SetStats(3, 18f);
        DemoLeaderboardSession.SubmitResult(false);
        DemoLeaderboardEntry entry = new DemoLeaderboardStore().Load().MostKills[0];
        Assert.That(entry.Name, Is.EqualTo("Ada"));
        Assert.That(entry.Kills, Is.EqualTo(3));
        Assert.That(entry.SurvivalSeconds, Is.EqualTo(18));
    }

    [Test]
    public void ReturningToMenu_AbandonsAttemptAndRetryCannotRecoverIt()
    {
        BeginNamedRun("Ada");
        SetStats(10, 100f);
        ArmRoute(SceneDestination.Title, SceneNavigation.PlaySceneName);
        DemoLeaderboardSession.PrepareRetry();
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    [Test]
    public void SubsystemRegistration_ClearsActiveAndPendingNamesWithoutDomainReload()
    {
        BeginNamedRun("Ada");
        DemoLeaderboardSession.PrepareRetry();
        typeof(DemoLeaderboardSession).GetMethod("ResetStatics", PrivateStatic).Invoke(null, null);
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        DemoLeaderboardSession.PrepareRetry();
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoScoreFile();
    }

    private static void BeginNamedRun(string name)
    {
        DemoLeaderboardSession.PrepareMenuLaunch(name);
        ArmRoute(SceneDestination.Play, SceneNavigation.TitleSceneName);
        DemoLeaderboardSession.BeginGameplay();
    }

    private static void ArmRoute(SceneDestination destination, string source)
    {
        typeof(DemoLeaderboardSession).GetMethod("PrepareSceneLoad", PrivateStatic)
            .Invoke(null, new object[] { destination, source });
    }

    private static void SetStats(int kills, float seconds)
    {
        typeof(RunCombatStats).GetField("_enemiesEliminated", PrivateStatic).SetValue(null, kills);
        typeof(RunSessionStats).GetField("_runStartTime", PrivateStatic).SetValue(null, Time.time - seconds);
    }

    private void SetTutorialActive(bool active)
    {
        if (_tutorialObject == null)
        {
            _tutorialObject = new GameObject("Leaderboard tutorial test");
            _tutorialObject.SetActive(false);
            _tutorialObject.AddComponent<TutorialDirector>();
        }
        TutorialDirector tutorial = _tutorialObject.GetComponent<TutorialDirector>();
        typeof(TutorialDirector).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tutorial, active);
        SetTutorial(tutorial);
    }

    private static void SetTutorial(TutorialDirector tutorial) =>
        typeof(TutorialDirector).GetProperty(nameof(TutorialDirector.Instance), BindingFlags.Public | BindingFlags.Static)
            .SetValue(null, tutorial);

    private static void AssertNoScoreFile() => Assert.That(File.Exists(DemoLeaderboardStore.DefaultPath), Is.False);
}
