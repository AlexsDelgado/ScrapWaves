using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>Exercises the saved title scene and its real button/presenter wiring without loading gameplay.</summary>
public sealed class DemoLeaderboardLaunchTests
{
    private const string TestSaveVariable = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
    private const string TitleScenePath = "Assets/Scenes/TitleScreen.unity";
    private const string IsolationState = "DemoLeaderboardLaunchTests.Isolation";
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private Scene _scene;
    private TitleScreenController _controller;
    private TitleScreenScreenStack _stack;
    private ScrapSceneTransition _transition;
    private DemoLeaderboardSettings _settings;
    private DemoLeaderboardView _boards;
    private PlayerNameEntryView _nameEntry;
    private TMP_InputField _input;
    private Button _start;
    private Button _cancel;
    private EventSystem _eventSystem;
    private string _testDirectory;
    private int _routes;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        string previousDirectory = Environment.GetEnvironmentVariable(TestSaveVariable);
        SessionState.SetBool(IsolationState + ".HadPrevious", previousDirectory != null);
        SessionState.SetString(IsolationState + ".Previous", previousDirectory ?? string.Empty);
        string directory = Path.Combine(Path.GetTempPath(), "ScrapWavesLeaderboardLaunchTests", Guid.NewGuid().ToString("N"));
        SessionState.SetString(IsolationState + ".Directory", directory);
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable(TestSaveVariable, directory);
        EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
        // Use real Awake/OnEnable/Start and a running player clock, including EventSystem
        // registration. Manual EditMode lifecycle calls cannot supply that runtime contract.
        yield return new EnterPlayMode();
        yield return null;

        _routes = 0;
        _testDirectory = SessionState.GetString(IsolationState + ".Directory", string.Empty);
        Time.timeScale = 1f;
        _scene = SceneManager.GetSceneByName(SceneNavigation.TitleSceneName);
        Assert.That(_scene.IsValid() && _scene.isLoaded, Is.True);
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(_scene));
        _controller = FindOne<TitleScreenController>();
        _stack = FindOne<TitleScreenScreenStack>();
        _transition = FindOne<ScrapSceneTransition>();
        _settings = FindOne<DemoLeaderboardSettings>();
        _boards = FindOne<DemoLeaderboardView>();
        _nameEntry = FindOne<PlayerNameEntryView>();
        _eventSystem = FindOne<EventSystem>();
        _input = Read<TMP_InputField>(_nameEntry, "_input");
        _start = Read<Button>(_nameEntry, "_startButton");
        _cancel = Read<Button>(_nameEntry, "_cancelButton");
        Assert.That(_stack.HasPlayerNameEntryBinding, Is.True);
        Assert.That(_nameEntry.HasRequiredReferences, Is.True);
        Assert.That(_boards.HasRequiredReferences, Is.True);

        // Submit events are synthetic; leave the actual EventSystem registered but prevent
        // physical input from racing the test's deterministic button and focus operations.
        foreach (BaseInputModule module in _eventSystem.GetComponents<BaseInputModule>())
            module.enabled = false;
        Assert.That(EventSystem.current, Is.SameAs(_eventSystem));
        _settings.SetEnabled(true);
        Set(_transition, "_sceneLoadTimeout", 1000f);
        SetProperty(_transition, "Route", new Func<SceneDestination, bool>(destination =>
        {
            Assert.That(destination, Is.EqualTo(SceneDestination.Play));
            _routes++;
            return true;
        }));
        SetProperty(_transition, "ReducedMotion", new Func<bool>(() => true));

        MainMenuPresentationController presentation = FindOne<MainMenuPresentationController>();
        presentation.ApplyPreferences(true, false, false);
        _stack.ApplyReducedMotion(true);
        presentation.CompleteIntroImmediately();
        _eventSystem.SetSelectedGameObject(_controller.PlayButton.gameObject);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        try
        {
            if (Application.isPlaying)
            {
                DemoLeaderboardSession.ResetForMenu();
                TutorialDirector.CancelReplayRequest();
                Time.timeScale = 1f;
                yield return new ExitPlayMode();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        finally
        {
            string directory = SessionState.GetString(IsolationState + ".Directory", string.Empty);
            string previous = SessionState.GetBool(IsolationState + ".HadPrevious", false)
                ? SessionState.GetString(IsolationState + ".Previous", string.Empty)
                : null;
            Environment.SetEnvironmentVariable(TestSaveVariable, previous);
            string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScrapWavesLeaderboardLaunchTests")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) &&
                Path.GetFullPath(directory).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(directory))
                Directory.Delete(directory, true);
            SessionState.EraseString(IsolationState + ".Directory");
            SessionState.EraseString(IsolationState + ".Previous");
            SessionState.EraseBool(IsolationState + ".HadPrevious");
        }
    }

    [UnityTest]
    public IEnumerator BothPlayButtons_OpenOnePromptAndCancelReturnsToTheInvokingButton()
    {
        foreach (Button source in new[] { _controller.PlayButton, _controller.TutorialButton })
        {
            Submit(source);
            Assert.That(_stack.CurrentState, Is.EqualTo(TitleScreenLocalState.PlayerNameEntry));
            Assert.That(_stack.IsInputLocked, Is.True);
            Assert.That(Read<bool>(_nameEntry, "_ready"), Is.False, "Text input must wait until the screen is readable.");
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Not.SameAs(_input.gameObject));
            // Queued click callbacks must not open another screen or change the remembered route.
            _controller.PlayButton.onClick.Invoke();
            _controller.TutorialButton.onClick.Invoke();
            Assert.That(_routes, Is.Zero);
            Assert.That(IsReplayArmed(), Is.False, "Opening the tutorial prompt must not arm a tutorial replay.");
            Assert.That(PendingName(), Is.Null);

            yield return WaitForScreen();
            Assert.That(Read<bool>(_nameEntry, "_ready"), Is.True);
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(_input.gameObject));
            Assert.That(_input.isFocused || Read<bool>(_input, "m_ShouldActivateNextUpdate"), Is.True,
                "The authored input must be activated after opening, not merely made visible.");
            _input.text = "Discard me";
            if (source == _controller.TutorialButton)
                _stack.OnCancel(new BaseEventData(_eventSystem));
            else
                Submit(_cancel);
            yield return WaitForScreen();

            Assert.That(_stack.CurrentState, Is.EqualTo(TitleScreenLocalState.MainMenu));
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(source.gameObject));
            Assert.That(_input.text, Is.Empty);
            Assert.That(PendingName(), Is.Null);
            Assert.That(IsReplayArmed(), Is.False);
            Assert.That(_routes, Is.Zero);
        }
        AssertNoLeaderboardWrites();
    }

    [UnityTest]
    public IEnumerator ConfirmingName_QueuesOneNamedRouteDespiteDuplicateSubmitCallbacks()
    {
        Submit(_controller.PlayButton);
        yield return WaitForScreen();
        _input.text = "  Ana   R  ";
        Submit(_start);
        _start.onClick.Invoke();
        _input.onSubmit.Invoke(_input.text);
        _controller.TutorialButton.onClick.Invoke();

        Assert.That(_transition.IsTransitioning, Is.True);
        Assert.That(_routes, Is.Zero, "Gameplay must wait for the existing transition to cover the title scene.");
        Assert.That(PendingName(), Is.EqualTo("Ana R"));
        Assert.That(IsReplayArmed(), Is.False);
        AdvanceTransition(2f);
        AdvanceTransition(2f);
        Assert.That(_routes, Is.EqualTo(1));
        Assert.That(_transition.IsTransitioning, Is.True, "The route stub deliberately leaves gameplay unloaded.");
        AssertNoLeaderboardWrites();
    }

    [UnityTest]
    public IEnumerator DisabledPlay_BypassesPromptAndDoesNotCreateANamedResult()
    {
        yield return VerifyDisabledLaunch(_controller.PlayButton, false);
    }

    [UnityTest]
    public IEnumerator DisabledTutorial_BypassesPromptAndPreservesTutorialIntentWithoutRanking()
    {
        yield return VerifyDisabledLaunch(_controller.TutorialButton, true);
    }

    [UnityTest]
    public IEnumerator FailedTutorialRoute_CanRetryAndCannotLeakReplayIntoANormalLaunch()
    {
        SetProperty(_transition, "Route", new Func<SceneDestination, bool>(_ => { _routes++; return false; }));
        Submit(_controller.TutorialButton);
        yield return WaitForScreen();
        _input.text = "Ada";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Submit(_start);
            Assert.That(IsReplayArmed(), Is.True);
            LogAssert.Expect(LogType.Error, "ScrapSceneTransition could not load 'GameplayScene'. The current scene will be uncovered.");
            AdvanceTransition(10f);
            Assert.That(_transition.IsTransitioning, Is.False);
            Assert.That(_routes, Is.EqualTo(attempt + 1));
            Assert.That(PendingName(), Is.Null);
            Assert.That(IsReplayArmed(), Is.False);
            Assert.That(_input.text, Is.EqualTo("Ada"));
            Assert.That(_start.IsInteractable(), Is.True, "The same prompt must permit retry after a failed load.");
        }

        Submit(_cancel);
        yield return WaitForScreen();
        Submit(_controller.PlayButton);
        yield return WaitForScreen();
        _input.text = "Ben";
        Submit(_start);
        Assert.That(IsReplayArmed(), Is.False, "A failed tutorial attempt must not change the following normal Play route.");
        Assert.That(PendingName(), Is.EqualTo("Ben"));
        AssertNoLeaderboardWrites();
    }

    private IEnumerator VerifyDisabledLaunch(Button source, bool tutorial)
    {
        _settings.SetEnabled(false);
        Assert.That(Read<GameObject>(_boards, "_root").activeSelf, Is.False);
        Submit(source);
        Assert.That(_stack.CurrentState, Is.EqualTo(TitleScreenLocalState.MainMenu));
        Assert.That(_nameEntry.gameObject.activeInHierarchy, Is.False);
        Assert.That(PendingName(), Is.Null);
        Assert.That(IsReplayArmed(), Is.EqualTo(tutorial));
        AdvanceTransition(2f);
        Assert.That(_routes, Is.EqualTo(1));
        // Exercise the result endpoint as well: an unranked direct/test launch must not write.
        DemoLeaderboardSession.BeginGameplay();
        DemoLeaderboardSession.SubmitResult(false);
        AssertNoLeaderboardWrites();
        yield return null;
    }

    private IEnumerator WaitForScreen()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (_stack.IsInputLocked && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(_stack.IsInputLocked, Is.False, "The authored local-screen transition did not complete.");
    }

    private void Submit(Button button)
    {
        Assert.That(button.IsActive() && button.IsInteractable(), Is.True, button.name);
        _eventSystem.SetSelectedGameObject(button.gameObject);
        Assert.That(ExecuteEvents.Execute(button.gameObject, new BaseEventData(_eventSystem), ExecuteEvents.submitHandler), Is.True);
    }

    private void AdvanceTransition(float seconds) => Invoke(_transition, "AdvanceForTesting", seconds);
    private void AssertNoLeaderboardWrites() => Assert.That(
        Directory.GetFiles(_testDirectory, DemoLeaderboardStore.FileName + "*", SearchOption.AllDirectories), Is.Empty,
        "Menu entry, cancellation and unranked results must not create a leaderboard or its backup/temp files.");
    private static bool IsReplayArmed() => (bool)typeof(TutorialDirector).GetField("_replayRequested", StaticPrivate).GetValue(null);
    private static string PendingName() => (string)typeof(DemoLeaderboardSession).GetField("_pendingName", StaticPrivate).GetValue(null);
    private T FindOne<T>() where T : Component => FindAll<T>().Single();
    private T[] FindAll<T>() where T : Component => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, InstancePrivate).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, InstancePrivate).SetValue(target, value);
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, InstancePrivate).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, InstancePrivate).Invoke(target, args);
}
