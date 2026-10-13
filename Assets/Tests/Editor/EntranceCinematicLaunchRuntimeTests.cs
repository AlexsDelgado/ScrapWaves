using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Runs the authored menu/startup routes with an isolated save and runtime-only settings.</summary>
public sealed class EntranceCinematicLaunchRuntimeTests
{
    private const string SetupKey = "EntranceCinematicLaunchRuntimeTests.Setup";
    private const string SaveDirectoryVariable = "SCRAPWAVES_TEST_SAVE_DIRECTORY";
    private const string TransitionPrefabPath = "Assets/Prefabs/UI/MainMenu/ScrapSceneTransition.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static RuntimeInput _activeInput;

    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }
    [Serializable] private sealed class SavedSetup
    {
        public SavedScene[] Scenes;
        public string SaveDirectory;
        public string PreviousSaveDirectory;
        public bool HadSaveDirectory;
        public bool RestorePlayModeOptions;
        public bool PreviousPlayModeOptionsEnabled;
        public int PreviousPlayModeOptions;
    }

    [UnityTest]
    public IEnumerator MenuOffOnRetryAndDirectEntryKeepTheStartupHandoff()
    {
        PrepareScene(freshSave: false);
        yield return new EnterPlayMode();
        using (var input = new RuntimeInput())
        {
            ActivateTitleWithInMemoryPreferences();

            yield return LaunchFromMenu(playCinematic: false);
            yield return WaitForSelection(playCinematic: false);
            yield return SelectAndCheckControls(input);

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: true);
            yield return WaitForSelection(playCinematic: true); // Real natural completion.
            yield return SelectFirstWeapon();

            Invoke(Object.FindFirstObjectByType<RunEndScreenUI>(), "Retry");
            yield return WaitForGameplay();
            yield return WaitForSelection(playCinematic: false);
            yield return SelectFirstWeapon();

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: false);
            yield return WaitForSelection(playCinematic: false);
            yield return SelectFirstWeapon();

            // A scene opened without a menu request retains the established cinematic workflow.
            SceneNavigation.PrepareForSceneChange();
            SceneManager.LoadScene(SceneNavigation.PlaySceneName);
            yield return WaitForGameplay();
            yield return WaitForPlayback();
            input.SetKeys(Key.Space);
            yield return Until(() => Intro().IsFinished, 3f, "Direct-entry skip did not finalize.");
            Assert.That(Choice().IsVisible, Is.False, "A held skip must not open or confirm selection.");
            input.SetKeys();
            yield return WaitForSelection(playCinematic: false);
            yield return SelectFirstWeapon();
        }
    }

    [UnityTest]
    public IEnumerator FreshSaveAndExplicitReplayRespectBothSettingsAndTutorialRetry()
    {
        PrepareScene(freshSave: true);
        yield return new EnterPlayMode();
        using (var input = new RuntimeInput())
        {
            ActivateTitleWithInMemoryPreferences();
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.True);

            yield return LaunchFromMenu(playCinematic: false);
            yield return WaitForTutorial(playCinematic: false);
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.True,
                "Bypassing the entrance must not mark an incomplete lesson complete.");

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: true);
            yield return WaitForTutorial(playCinematic: true);
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.True);

            // Set up a completed profile only in this fixture's unique temporary save.
            // This test covers startup/replay; TutorialSmokeValidation covers lesson completion.
            SaveManager.Instance.CompleteGameplayTutorial();
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.False);

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: false, replay: true);
            yield return WaitForTutorial(playCinematic: false);
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.False);

            Invoke(Object.FindFirstObjectByType<RunEndScreenUI>(), "Retry");
            yield return WaitForGameplay();
            yield return WaitForTutorial(playCinematic: false);
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.False,
                "Retry must replay the active lesson without changing the completed profile.");

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: true, replay: true);
            yield return WaitForTutorial(playCinematic: true);
            Assert.That(SaveManager.Instance.ShouldAutoPlayTutorial, Is.False);
        }
    }

    [UnityTest]
    public IEnumerator TutorialOffAndOnKeepTheSameMeasuredBodyAndThreatPlacement()
    {
        PrepareScene(freshSave: true);
        yield return new EnterPlayMode();
        using (var input = new RuntimeInput())
        {
            ActivateTitleWithInMemoryPreferences();
            yield return LaunchFromMenu(playCinematic: false);
            yield return WaitForTutorial(playCinematic: false);
            yield return ForSeconds(.12f);
            TutorialPlacement off = MeasureTutorialPlacement("OFF");

            yield return ReturnToMenu();
            yield return LaunchFromMenu(playCinematic: true);
            yield return WaitForTutorial(playCinematic: true); // Natural completion, not a skip.
            yield return ForSeconds(.12f);
            TutorialPlacement on = MeasureTutorialPlacement("ON");

            // Always emit both absolute measurements before comparing them. An existing
            // defect shared by ON and OFF remains explicit evidence for the tutorial task;
            // matching two submerged meshes alone is not a claim of correct placement.
            ReportTutorialPlacement(off);
            ReportTutorialPlacement(on);
            Assert.That(Vector3.Distance(off.Root, on.Root), Is.LessThan(.04f), "Toggle changed the solid body position.");
            Assert.That(Vector3.Distance(off.VisualRoot, on.VisualRoot), Is.LessThan(.04f), "Toggle changed the visible root.");
            Assert.That(Vector3.Distance(off.Hips, on.Hips), Is.LessThan(.04f), "Toggle changed the hips.");
            Assert.That(Vector3.Distance(off.Head, on.Head), Is.LessThan(.04f), "Toggle changed the head.");
            Assert.That(off.CapsuleClearance, Is.EqualTo(on.CapsuleClearance).Within(.04f));
            Assert.That(off.BakedMin.y, Is.EqualTo(on.BakedMin.y).Within(.04f));
            Assert.That(off.BakedMax.y, Is.EqualTo(on.BakedMax.y).Within(.04f));
            Assert.That(off.VisibleClearance, Is.EqualTo(on.VisibleClearance).Within(.04f));
            Assert.That(off.ThreatMinusHips, Is.EqualTo(on.ThreatMinusHips).Within(.04f));
            Assert.That(off.ThreatMinusHead, Is.EqualTo(on.ThreatMinusHead).Within(.04f));
            Assert.That(off.ThreatMeshMinY, Is.EqualTo(on.ThreatMeshMinY).Within(.04f));
            Assert.That(off.ThreatMeshMaxY, Is.EqualTo(on.ThreatMeshMaxY).Within(.04f));
            Assert.That(off.BakedVertexCount, Is.EqualTo(on.BakedVertexCount));
        }
    }

    [Serializable]
    private sealed class TutorialPlacement
    {
        public string Mode, CapsuleSurface, VisibleSurface, SkinnedMeshes;
        public Vector3 Root, VisualRoot, Hips, Head, CapsuleMin, CapsuleMax, BakedMin, BakedMax;
        public Vector3 ThreatAnchor, ThreatConfiguredOffset;
        public float CapsuleGroundY, CapsuleClearance, VisibleGroundY, VisibleClearance;
        public float ThreatMinusHips, ThreatMinusHead, ThreatMeshMinY, ThreatMeshMaxY;
        public int BakedVertexCount, VerticesBelowRootSurface;
    }

    private static TutorialPlacement MeasureTutorialPlacement(string mode)
    {
        PlayerMovement player = Player();
        Transform root = player.transform;
        Transform visual = root.Find("PlaceholderPlayerVisual");
        Assert.That(visual, Is.Not.Null);
        Transform[] bones = visual.GetComponentsInChildren<Transform>(true);
        Transform hips = bones.Single(bone => bone.name == "spine");
        Transform head = bones.Single(bone => bone.name == "spine.006");
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        Assert.That(capsule != null && capsule.enabled && !capsule.isTrigger, Is.True,
            "The measurement requires the enabled solid movement capsule, not the trigger hurtbox.");
        Physics.SyncTransforms();
        Bounds solid = capsule.bounds;
        RaycastHit support = FindTutorialSupport(solid.center, solid.max.y, root);
        var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(skin => skin.enabled && skin.gameObject.activeInHierarchy && skin.sharedMesh != null).ToArray();
        Assert.That(skins.Length, Is.GreaterThan(0), "No visible skinned character meshes to measure.");
        var vertices = new List<Vector3>();
        var baked = new Mesh();
        try
        {
            foreach (var skin in skins)
            {
                baked.Clear();
                skin.BakeMesh(baked); // Actual deformed geometry; imported renderer bounds are intentionally broad.
                foreach (Vector3 vertex in baked.vertices) vertices.Add(skin.transform.TransformPoint(vertex));
            }
        }
        finally { Object.DestroyImmediate(baked); }
        Assert.That(vertices.Count, Is.GreaterThan(0));
        Bounds visible = new Bounds(vertices[0], Vector3.zero);
        Vector3 lowest = vertices[0];
        foreach (Vector3 vertex in vertices)
        {
            visible.Encapsulate(vertex);
            if (vertex.y < lowest.y) lowest = vertex;
        }
        RaycastHit visualSupport = FindTutorialSupport(lowest, Mathf.Max(solid.max.y, visible.max.y), root);
        RearThreatPresenter threat = Object.FindObjectsByType<RearThreatPresenter>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None).Single(presenter => presenter.Player == root);
        MeshFilter fill = GetField<MeshFilter>(threat, "_fill");
        MeshFilter outline = GetField<MeshFilter>(threat, "_outline");
        Assert.That(threat.isActiveAndEnabled, Is.True);
        Assert.That(fill != null && fill.sharedMesh != null && fill.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(outline != null && outline.sharedMesh != null, Is.True);
        // The authored transparent outline is intentionally disabled; measure visible geometry only.
        Vector3[] threatVertices = new[] { fill, outline }.Where(filter => filter.GetComponent<MeshRenderer>().enabled).SelectMany(filter =>
            filter.sharedMesh.vertices.Select(vertex => filter.transform.TransformPoint(vertex))).ToArray();
        Assert.That(threatVertices.Length, Is.GreaterThan(0));
        var result = new TutorialPlacement
        {
            Mode = mode, Root = root.position, VisualRoot = visual.position, Hips = hips.position, Head = head.position,
            CapsuleMin = solid.min, CapsuleMax = solid.max, CapsuleSurface = support.collider.name,
            CapsuleGroundY = support.point.y, CapsuleClearance = solid.min.y - support.point.y,
            BakedMin = visible.min, BakedMax = visible.max, BakedVertexCount = vertices.Count,
            SkinnedMeshes = string.Join(", ", skins.Select(skin => skin.name)),
            VisibleSurface = visualSupport.collider.name, VisibleGroundY = visualSupport.point.y,
            VisibleClearance = lowest.y - visualSupport.point.y,
            VerticesBelowRootSurface = vertices.Count(vertex => vertex.y < support.point.y - .05f),
            ThreatAnchor = threat.transform.position, ThreatConfiguredOffset = GetField<Vector3>(threat, "_hipOffset"),
            ThreatMinusHips = threat.transform.position.y - hips.position.y,
            ThreatMinusHead = threat.transform.position.y - head.position.y,
            ThreatMeshMinY = threatVertices.Min(vertex => vertex.y), ThreatMeshMaxY = threatVertices.Max(vertex => vertex.y)
        };
        Assert.That(result.Head.y, Is.GreaterThan(result.Hips.y), "The named rig landmarks must be vertically ordered.");
        Assert.That(result.BakedMax.y - result.BakedMin.y, Is.GreaterThan(.5f), "Baked body bounds must have real height.");
        return result;
    }

    private static RaycastHit FindTutorialSupport(Vector3 position, float top, Transform playerRoot)
    {
        Vector3 origin = new Vector3(position.x, top + 2f, position.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider.enabled && !hit.collider.transform.IsChildOf(playerRoot) && hit.normal.y > .5f)
            .OrderBy(hit => hit.distance).ToArray();
        Assert.That(hits.Length, Is.GreaterThan(0), "No solid supporting surface below the measured character point.");
        return hits[0];
    }

    private static void ReportTutorialPlacement(TutorialPlacement sample)
    {
        TestContext.WriteLine("TUTORIAL_PLACEMENT " + JsonUtility.ToJson(sample));
        if (sample.CapsuleClearance < -.05f || sample.CapsuleClearance > .15f)
            TestContext.WriteLine($"TUTORIAL_PLACEMENT_FINDING {sample.Mode}: solid capsule clearance is {sample.CapsuleClearance:F4} m.");
        if (sample.VisibleClearance < -.10f || sample.VisibleClearance > .25f)
            TestContext.WriteLine($"TUTORIAL_PLACEMENT_FINDING {sample.Mode}: actual rendered body clearance is {sample.VisibleClearance:F4} m; {sample.VerticesBelowRootSurface}/{sample.BakedVertexCount} baked vertices are over 0.05 m below the root's supporting surface.");
        if (Mathf.Abs(sample.ThreatMinusHead) < Mathf.Abs(sample.ThreatMinusHips))
            TestContext.WriteLine($"TUTORIAL_PLACEMENT_FINDING {sample.Mode}: threat anchor is closer vertically to the head than hips (head delta {sample.ThreatMinusHead:F4} m; hip delta {sample.ThreatMinusHips:F4} m).");
    }

    [UnityTest]
    public IEnumerator RepeatedPlayModeWithoutDomainReloadRestoresTheAuthoredOffSetting()
    {
        PrepareScene(freshSave: false);
        var saved = JsonUtility.FromJson<SavedSetup>(SessionState.GetString(SetupKey, ""));
        saved.RestorePlayModeOptions = true;
        saved.PreviousPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        saved.PreviousPlayModeOptions = (int)EditorSettings.enterPlayModeOptions;
        SessionState.SetString(SetupKey, JsonUtility.ToJson(saved));
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        yield return new EnterPlayMode();
        using (var input = new RuntimeInput())
        {
            ActivateTitleWithInMemoryPreferences();
            yield return LaunchFromMenu(playCinematic: true);
            yield return WaitForPlayback();
            input.SetKeys(Key.Space);
            yield return Until(() => Intro().IsFinished, 3f, "First-session skip did not finish.");
            input.SetKeys();
            yield return WaitForSelection(playCinematic: false);
            yield return SelectFirstWeapon();
            yield return ReturnToMenu();
            SetField(Object.FindFirstObjectByType<EntranceCinematicLaunchSettings>(), "_playEntranceCinematic", true);
            EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
            // Leave an unconsumed ON request to challenge session/menu reset.
        }
        DisableSceneMenuSubscribers();
        yield return new ExitPlayMode();

        yield return new EnterPlayMode();
        using (var input = new RuntimeInput())
        {
            ActivateTitleWithInMemoryPreferences();
            yield return LaunchFromMenu(playCinematic: false);
            yield return WaitForSelection(playCinematic: false);
            yield return SelectFirstWeapon();
        }
    }

    private static void PrepareScene(bool freshSave)
    {
        Assert.That(Application.isPlaying, Is.False);
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save untitled scenes before running the cinematic launch tests.");
        Assert.That(Enumerable.Range(0, SceneManager.sceneCount).All(index => !SceneManager.GetSceneAt(index).isDirty), Is.True,
            "Save scene changes before running the cinematic launch tests.");

        string previousDirectory = Environment.GetEnvironmentVariable(SaveDirectoryVariable);
        string directory = Path.Combine(Path.GetTempPath(), "ScrapWavesEntranceLaunchTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray(),
            SaveDirectory = directory,
            PreviousSaveDirectory = previousDirectory,
            HadSaveDirectory = previousDirectory != null
        }));
        Environment.SetEnvironmentVariable(SaveDirectoryVariable, directory);
        if (!freshSave)
            File.WriteAllText(Path.Combine(directory, "scrapwaves_save.json"),
                "{\"Version\":4,\"FreshSaveTutorialEligible\":false,\"GameplayTutorialCompleted\":true,\"MetaTutorialSeen\":true}");

        // An edited title is redirected to Temp/__Backupscenes even on later runtime
        // loads. Start empty so the real saved menu keeps its production name and path.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static void ActivateTitleWithInMemoryPreferences()
    {
        Assert.That(UserSettingsService.Instance == null, Is.True,
            "The empty runtime fixture must not inherit a settings service.");
        var root = new GameObject("Cinematic launch test settings");
        root.SetActive(false);
        var service = root.AddComponent<UserSettingsService>();
        SetField(service, "_data", UserSettingsData.CreateDefault());
        SetField(service, "_initialized", true);
        SetField(service, "_savePending", false);
        var applier = root.AddComponent<UserSettingsApplier>();
        SetField(applier, "_settingsService", service);
        root.SetActive(true);
        Assert.That(UserSettingsService.Instance, Is.SameAs(service));

        // The saved title rejects its duplicate settings root before its child transition
        // can awaken. Supply the real authored overlay separately so menu buttons still
        // exercise ScrapSceneTransition and SceneNavigation.Load, with no fallback route.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TransitionPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        var transitionRoot = Object.Instantiate(prefab);
        Assert.That(ScrapSceneTransition.Instance, Is.Not.Null);
        Assert.That(ScrapSceneTransition.Instance.transform.root.gameObject, Is.SameAs(transitionRoot));
        SceneManager.LoadScene(SceneNavigation.TitleSceneName);
    }

    private static IEnumerator LaunchFromMenu(bool playCinematic, bool replay = false)
    {
        yield return WaitForMenu();
        var settings = Object.FindFirstObjectByType<EntranceCinematicLaunchSettings>();
        Assert.That(settings, Is.Not.Null, "TitleScreen needs its authored developer switch.");
        Assert.That(settings.PlayEntranceCinematic, Is.False, "The saved menu setting must remain OFF between launches.");
        SetField(settings, "_playEntranceCinematic", playCinematic); // Runtime instance only; never save the scene.
        Assert.That(settings.PlayEntranceCinematic, Is.EqualTo(playCinematic));
        var title = Object.FindFirstObjectByType<TitleScreenController>();
        if (replay) title.PlayTutorial();
        else title.PlayButton.onClick.Invoke();
        yield return WaitForGameplay();
    }

    private static IEnumerator ReturnToMenu()
    {
        Assert.That(SceneNavigation.LoadTitle(), Is.True);
        yield return WaitForMenu();
        Assert.That(EntranceCinematic.StartupHeld, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    private static IEnumerator WaitForMenu()
    {
        yield return Until(() =>
        {
            var title = Object.FindFirstObjectByType<TitleScreenController>();
            var presentation = title != null ? title.GetComponent<MainMenuPresentationController>() : null;
            return SceneManager.GetActiveScene().name == SceneNavigation.TitleSceneName &&
                title != null && presentation != null && presentation.IsInteractive &&
                ScrapSceneTransition.Instance != null && !ScrapSceneTransition.Instance.IsTransitioning;
        }, 15f, "The authored menu never became interactive.", MenuDiagnostics);
    }

    private static string MenuDiagnostics()
    {
        Scene scene = SceneManager.GetActiveScene();
        var title = Object.FindFirstObjectByType<TitleScreenController>(FindObjectsInactive.Include);
        var presentation = title != null ? title.GetComponent<MainMenuPresentationController>() : null;
        var transition = ScrapSceneTransition.Instance;
        string titleState = title == null ? "missing" : $"active={title.isActiveAndEnabled}, scene={title.gameObject.scene.name}";
        string presentationState = presentation == null ? "missing" :
            $"active={presentation.isActiveAndEnabled}, interactive={presentation.IsInteractive}, intro={presentation.IsIntroPlaying}, elapsed={GetField<float>(presentation, "_introElapsed"):F3}";
        string transitionState = transition == null ? "missing" :
            $"active={transition.isActiveAndEnabled}, transitioning={transition.IsTransitioning}, state={GetField<object>(transition, "_state")}, elapsed={GetField<float>(transition, "_phaseElapsed"):F3}";
        return $"Scene='{scene.name}', path='{scene.path}'; title: {titleState}; presentation: {presentationState}; transition: {transitionState}; timeScale={Time.timeScale}, playing={Application.isPlaying}.";
    }

    private static IEnumerator WaitForGameplay()
    {
        // LoadScene is completed on the following frame, even when reloading the same name.
        yield return null;
        yield return null;
        yield return Until(() => SceneManager.GetActiveScene().name == SceneNavigation.PlaySceneName && Intro() != null,
            15f, "The menu/retry did not load GameplayScene.");
    }

    private static IEnumerator WaitForPlayback()
    {
        yield return Until(() => Intro() != null && Intro().PlaybackTime > 0f, 5f, "Enabled cinematic did not begin playback.");
        Assert.That(Intro().IsFinished, Is.False, "Enabled/direct-entry launch incorrectly bypassed the cinematic.");
        Assert.That(Intro().PlaybackTime, Is.LessThan(Intro().Duration));
        Assert.That(EntranceCinematic.StartupHeld, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(Choice().IsVisible, Is.False);
        Assert.That(RunSessionStats.ElapsedSeconds, Is.LessThan(.02f));
    }

    private static IEnumerator WaitForSelection(bool playCinematic)
    {
        if (playCinematic) yield return WaitForPlayback();
        float timeout = playCinematic ? Intro().Duration + 5f : 4f;
        yield return Until(() => Intro().IsFinished && Choice().IsVisible, timeout,
            "Startup did not reach the initial weapon selection.");
        yield return Until(() => ScrapSceneTransition.Instance == null || !ScrapSceneTransition.Instance.IsTransitioning,
            5f, "The menu transition did not release its overlay.");

        Assert.That(TutorialDirector.IsActive, Is.False);
        Assert.That(Intro().PlaybackTime, Is.EqualTo(Intro().Duration), "OFF must still use finalized placement.");
        Assert.That(EntranceCinematic.StartupHeld, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1), "Only the visible selector should own pause.");
        Assert.That(Object.FindObjectsByType<LevelUpChoiceUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(choice => choice.IsVisible), Is.EqualTo(1));
        Assert.That(GetField<bool>(Camera.main.GetComponent<ThirdPersonCamera>(), "_lookBlockedByUi"), Is.True);
        AssertRestoredPresentation();
    }

    private static IEnumerator SelectFirstWeapon()
    {
        GetField<ChoiceMenuView>(Choice(), "_activeView").Cards[0].SelectButton.onClick.Invoke();
        yield return Until(() => !EntranceCinematic.StartupHeld, 3f, "Weapon selection did not release startup.");
        Assert.That(Player().GetComponent<WeaponManager>().GetEquippedWeapons().Count, Is.EqualTo(1));
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    private static IEnumerator SelectAndCheckControls(RuntimeInput input)
    {
        PlayerMovement player = Player();
        PlayerAnimationDriver driver = player.GetComponent<PlayerAnimationDriver>();
        Transform visual = player.transform.Find("PlaceholderPlayerVisual");
        Transform head = visual.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == "spine.006");
        Vector3 visualPosition = visual.position, headPosition = head.position, cameraPosition = Camera.main.transform.position;
        int landings = 0;
        Action landed = () => landings++;
        player.OnLanded += landed;
        try
        {
            input.SetMouseHeld(true);
            yield return SelectFirstWeapon();
            var weapon = player.GetComponent<WeaponManager>().GetCurrentManualWeapon();
            Assert.That(weapon, Is.Not.Null);
            float ammo = weapon.CurrentAmmo;
            yield return ForSeconds(.15f);
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(ammo), "The held selector click must not fire the new weapon.");
            Assert.That(driver.IsEntranceIdleHeld, Is.True);
            Assert.That(player.IsGroundedOnSurface, Is.True);
            Assert.That(landings, Is.Zero, "Finalized startup must not emit another landing.");
            Assert.That(Vector3.Distance(visualPosition, visual.position), Is.LessThan(.05f));
            Assert.That(Vector3.Distance(headPosition, head.position), Is.LessThan(.05f));
            Assert.That(Vector3.Distance(cameraPosition, Camera.main.transform.position), Is.LessThan(.05f));
            Assert.That(GetField<bool>(Camera.main.GetComponent<ThirdPersonCamera>(), "_lookBlockedByUi"), Is.False);
            Assert.That(player.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));

            input.SetMouseHeld(false);
            yield return ForSeconds(.08f);
            Assert.That(driver.IsEntranceIdleHeld, Is.True);
            Assert.That(driver.IsEntranceInputReleasePending, Is.False);
            Vector3 beforeMove = player.transform.position;
            input.SetKeys(Key.W);
            yield return Until(() => Vector3.Distance(beforeMove, player.transform.position) > .1f, 2f,
                "Fresh movement input remained suppressed after selection.");
            Assert.That(driver.IsEntranceIdleHeld, Is.False);
            input.SetKeys();
        }
        finally { player.OnLanded -= landed; }
    }

    private static IEnumerator WaitForTutorial(bool playCinematic)
    {
        if (playCinematic) yield return WaitForPlayback();
        float timeout = playCinematic ? Intro().Duration + 5f : 4f;
        yield return Until(() => Intro().IsFinished && TutorialDirector.IsActive &&
            GetField<bool>(TutorialDirector.Instance, "_begun") && !EntranceCinematic.StartupHeld,
            timeout, "Startup did not hand the player to the tutorial.");
        yield return ForSeconds(.08f);
        var director = TutorialDirector.Instance;
        Assert.That(director.HasReferences, Is.True);
        Assert.That(director.Step, Is.EqualTo(TutorialStep.Welcome));
        Assert.That(Choice().IsVisible, Is.False, "Tutorial startup must not also open the normal selector.");
        Assert.That(GameplayPause.LockCount, Is.Zero);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(RunSessionStats.ElapsedSeconds, Is.Zero);
        Assert.That(Player().IsGroundedOnSurface, Is.True);
        Assert.That(TutorialDirector.AbilityAllowed, Is.False);
        Assert.That(GetField<GameObject>(director, "_world").activeSelf, Is.True);
        var definition = GetField<TutorialDefinition>(director, "_definition");
        var weapons = Player().GetComponent<WeaponManager>();
        Assert.That(weapons.GetEquippedWeapons().Count, Is.EqualTo(1));
        Assert.That(weapons.TryGetEquippedWeapon(definition.StartingWeapon, out _), Is.True);
        Assert.That(Vector3.Dot(Camera.main.transform.forward,
            GetField<Transform>(director, "_markerCenter").position - Camera.main.transform.position), Is.GreaterThan(0f));
        AssertRestoredPresentation();
    }

    private static void AssertRestoredPresentation()
    {
        Assert.That(Player().enabled, Is.True);
        Assert.That(Player().GetComponent<WeaponManager>().enabled, Is.True);
        Assert.That(Player().GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(Camera.main.GetComponent<ThirdPersonCamera>().enabled, Is.True);
        Assert.That(Vector3.Dot(Camera.main.transform.forward, Player().transform.forward), Is.GreaterThan(.85f));
        Assert.That(Vector3.Dot(Camera.main.transform.position - Player().transform.position,
            Player().transform.forward), Is.LessThan(-1f));
        foreach (var presenter in Object.FindObjectsByType<RearThreatPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Assert.That(presenter.enabled, Is.True);
        Assert.That(GetField<AudioSource>(Intro(), "_audio").isPlaying, Is.False);
        Assert.That(GetField<GameObject>(Intro(), "_impact").activeSelf, Is.False);
        Assert.That(GetField<Transform>(Intro(), "_shadow").gameObject.activeSelf, Is.False);
        Assert.That(GetField<Light>(Intro(), "_fill").enabled, Is.False);
    }

    private static IEnumerator Until(Func<bool> condition, float seconds, string message, Func<string> diagnostics = null)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!condition() && EditorApplication.timeSinceStartup < deadline)
        {
            var intro = Intro();
            if (intro != null) SetField(intro, "_focused", true);
            yield return null;
        }
        bool complete = condition();
        Assert.That(complete, Is.True, complete || diagnostics == null ? message : message + "\n" + diagnostics());
    }

    private static IEnumerator ForSeconds(float seconds)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < deadline) yield return null;
    }

    private static EntranceCinematic Intro() => Object.FindFirstObjectByType<EntranceCinematic>();
    private static PlayerMovement Player() => Object.FindFirstObjectByType<PlayerMovement>();
    private static LevelUpChoiceUI Choice() => Player().GetComponent<LevelUpChoiceUI>();
    private static T GetField<T>(Object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static void SetField(Object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static void Invoke(Object target, string name) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null);

    private sealed class RuntimeInput : IDisposable
    {
        private readonly InputSettings _originalSettings = InputSystem.settings;
        private readonly InputSettings _settings;
        private readonly string _originalSettingsJson;
        private bool _disposed;
        private readonly List<InputDevice> _disabled = new();
        private readonly Keyboard _keyboard;
        private readonly Mouse _mouse;
        private readonly bool _runInBackground = Application.runInBackground;

        public RuntimeInput()
        {
            _activeInput = this;
            _originalSettingsJson = JsonUtility.ToJson(_originalSettings);
            _settings = Object.Instantiate(_originalSettings);
            _settings.hideFlags = HideFlags.HideAndDontSave;
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
            Application.runInBackground = true;
            foreach (InputDevice device in InputSystem.devices.ToArray())
                if (device.enabled) { _disabled.Add(device); InputSystem.DisableDevice(device); }
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            SetKeys();
            SetMouseHeld(false);
        }

        public void SetKeys(params Key[] keys) { InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        public void SetMouseHeld(bool held) { InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = (ushort)(held ? 1 : 0) }); InputSystem.Update(); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // A nested UnityTest assertion need not unwind the enclosing iterator's
            // using block. UnityTearDown also calls this before leaving Play Mode.
            InputSettings restore = _originalSettings;
            if (restore == null)
            {
                restore = ScriptableObject.CreateInstance<InputSettings>();
                restore.hideFlags = HideFlags.HideAndDontSave;
                JsonUtility.FromJsonOverwrite(_originalSettingsJson, restore);
            }
            InputSystem.settings = restore;
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            foreach (InputDevice device in _disabled) if (device.added) InputSystem.EnableDevice(device);
            if (_settings != null) Object.DestroyImmediate(_settings);
            Application.runInBackground = _runInBackground;
            if (ReferenceEquals(_activeInput, this)) _activeInput = null;
        }
    }

    private static void DisableSceneMenuSubscribers()
    {
        foreach (var menu in Object.FindObjectsByType<PauseMenuUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            menu.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator RestoreEnvironment()
    {
        _activeInput?.Dispose();
        string json = SessionState.GetString(SetupKey, "");
        if (string.IsNullOrEmpty(json)) yield break;
        if (Application.isPlaying)
        {
            // Unsubscribe scene menus before the persistent settings fixture is destroyed.
            DisableSceneMenuSubscribers();
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
        var saved = JsonUtility.FromJson<SavedSetup>(json);
        Environment.SetEnvironmentVariable(SaveDirectoryVariable, saved.HadSaveDirectory ? saved.PreviousSaveDirectory : null);
        if (saved.RestorePlayModeOptions)
        {
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)saved.PreviousPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = saved.PreviousPlayModeOptionsEnabled;
        }
        SessionState.EraseString(SetupKey);
        // Discard the empty test scene before restoring the original editor scene setup.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (saved.Scenes?.Length > 0 && saved.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(saved.Scenes.Select(scene => new SceneSetup
                { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(saved.SaveDirectory);
        if (directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(directory).StartsWith("ScrapWavesEntranceLaunchTests-", StringComparison.Ordinal) &&
            Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
