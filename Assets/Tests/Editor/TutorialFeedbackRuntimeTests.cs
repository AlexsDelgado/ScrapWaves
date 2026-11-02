using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Real menu, E/WASD, recovery and physical geyser regression. Combat is accelerated through
/// existing enemy/pickup APIs; movement and launch are never invoked through private gameplay APIs.
/// SCRAPWAVES_TUTORIAL_QA_BASELINE=1 records findings without failing the final aggregate assertion.
/// SCRAPWAVES_TUTORIAL_QA_OUTPUT writes JSON measurements and actual camera/UI renders to that folder.
/// </summary>
public sealed class TutorialFeedbackRuntimeTests
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type LaunchFixture = typeof(EntranceCinematicLaunchRuntimeTests);
    private readonly List<string> _findings = new();
    private object _input;
    private string _prefix;
    private bool Baseline => Environment.GetEnvironmentVariable("SCRAPWAVES_TUTORIAL_QA_BASELINE") == "1";
    private static string Output => Environment.GetEnvironmentVariable("SCRAPWAVES_TUTORIAL_QA_OUTPUT");
    private static PlayerMovement Player => Director != null ? Get<PlayerMovement>(Director, "_player")
        : Object.FindFirstObjectByType<PlayerMovement>(FindObjectsInactive.Include);
    private static TutorialDirector Director => TutorialDirector.Instance;

    [UnityTest]
    public IEnumerator ActualTutorialMovementRecoveryCraftingAndGeyser([Values(false, true)] bool playCinematic)
    {
        CallLaunch("PrepareScene", true);
        yield return new EnterPlayMode();
        BeginInput(playCinematic ? "walkthrough-on" : "walkthrough-off");
        CallLaunch("ActivateTitleWithInMemoryPreferences");
        yield return Launch("LaunchFromMenu", playCinematic, false);
        yield return Launch("WaitForTutorial", playCinematic);
        yield return Settle(.12f);
        PlayerMovement lessonPlayer = Player;
        Assert.That(lessonPlayer, Is.Not.Null, "Startup player must exist.");
        LevelUpChoiceUI lessonChoice = lessonPlayer.GetComponent<LevelUpChoiceUI>();
        Assert.That(lessonChoice, Is.Not.Null, "Startup player must own its authored choice controller.");
        Vector3 initialPosition = lessonPlayer.transform.position;
        GameObject marker = Get<GameObject>(Director, "_marker");
        Check(!marker.activeInHierarchy, "Welcome keeps the movement marker hidden before E");
        var normalEnemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None)
            .Where(enemy => enemy.CurrentHealth > 0 && enemy.GetComponentInParent<TutorialEnemy>() == null).ToArray();
        Note("START_ENEMIES " + string.Join("; ", normalEnemies.Select(enemy => enemy.name + "@" + enemy.transform.position)));
        Check(normalEnemies.Length == 0, "No unintended normal enemy is active at tutorial start");
        Measure("01-welcome", true);
        Capture("01-welcome");

        yield return Press(Key.E);
        Check(Director.Step == TutorialStep.Movement, "First physical E advances Welcome to Movement");
        Check(marker.activeInHierarchy, "Movement marker appears after the first E");
        Check(!Object.FindFirstObjectByType<CraftingUI>().IsVisible, "The same E does not also open crafting");
        Capture("02-movement-after-e");

        // Move across real terrain before deliberately testing the two existing recovery triggers.
        SetKeys(Key.D);
        yield return Settle(.4f);
        SetKeys();
        yield return Settle(.35f);
        Check(PlanarDistance(initialPosition, Player.transform.position) > .25f, "WASD moves the real rigidbody after E");
        Measure("03-after-movement", true);
        Capture("03-after-movement");
        yield return Recover("04-fall-recovery", Get<Transform>(Director, "_spawn").position + Vector3.down * 12f);
        yield return Recover("05-radius-recovery", Get<Transform>(Director, "_markerCenter").position +
            Vector3.right * (Get<TutorialDefinition>(Director, "_definition").RecoveryRadius + 12f));

        yield return DriveTo(Get<Transform>(Director, "_markerCenter").position, 1.7f, 12f,
            () => Director.Step != TutorialStep.Movement);
        Check(Director.Step == TutorialStep.FirstEnemy, "Walking into the highlighted area starts the first scripted enemy");
        if (Director.Step != TutorialStep.FirstEnemy)
        {
            if (!Baseline) Assert.Fail("Cannot continue: actual movement did not reach FirstEnemy. See captures and movement log.");
            // Only baseline diagnosis can use a documented recovery setup after the failed actual route.
            PlaceForObservation(Get<Transform>(Director, "_markerCenter").position + Vector3.up * 1.5f);
            yield return Settle(.5f);
        }
        Check(!marker.activeInHierarchy, "The marker hides after reaching the movement target");
        Check(Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None).Length == 1, "Exactly one scripted first enemy is active");
        KillAndCollect();
        yield return Settle(.08f);
        Assert.That(Director.Step, Is.EqualTo(TutorialStep.Upgrade), "Combat setup must reach the upgrade page.");

        var station = Get<CraftingStation>(Director, "_station");
        var craftingUi = Get<CraftingUI>(Director, "_craftingUi");
        var crafting = Player.GetComponent<WeaponCraftingService>();
        var definition = Get<TutorialDefinition>(Director, "_definition");
        int authoredUiObjects = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        yield return DriveTo(station.InteractionPosition, station.InteractionRadius * .55f, 15f);
        yield return Press(Key.E);
        yield return Settle(.15f);
        Check(craftingUi.IsVisible && station.IsOpen, "Physical E near the tutorial table opens crafting");
        if (!craftingUi.IsVisible && Baseline) { Note("BASELINE_SETUP OpenCrafting after failed physical E route"); station.OpenCrafting(); yield return Settle(.1f); }
        Assert.That(craftingUi.IsVisible, Is.True, "Cannot inspect crafting without its authored UI.");
        var view = Get<CraftingMenuView>(craftingUi, "_view");
        Check(Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == authoredUiObjects,
            "Opening tutorial crafting creates no UI hierarchy");
        CheckCraftingLayer("06-upgrade-open", view);
        CheckButtonRaycast(view.UpgradeButton, "Upgrade");
        CheckButtonRaycast(view.CloseButton, "Close");
        Capture("06-upgrade-open");
        CapturePulseComparison();
        int uiObjects = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        view.CloseButton.onClick.Invoke();
        yield return Settle(.15f);
        Check(!craftingUi.IsVisible && !station.IsOpen && !GameplayPause.IsUiPaused, "Close returns control from tutorial crafting");
        yield return Press(Key.E);
        yield return Settle(.15f);
        Check(craftingUi.IsVisible, "Crafting can reopen with another E");
        Check(Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == uiObjects,
            "Closing/reopening crafting creates no UI hierarchy");
        Check(view.UpgradeButton.interactable, "The funded tutorial upgrade remains interactable under the tutorial HUD");
        view.UpgradeButton.onClick.Invoke();
        yield return Until(() => Director.Step == TutorialStep.Heat, 3f, "Real upgrade button advances to Heat");
        Check(!craftingUi.IsVisible && !GameplayPause.IsUiPaused, "Successful upgrade closes crafting and releases pause");
        yield return Press(Key.E);
        Check(Director.Step == TutorialStep.Ability, "Physical E advances the Heat explanation");
        KillAndCollect();
        yield return Settle(.08f);
        Assert.That(Director.Step, Is.EqualTo(TutorialStep.Tinker));
        yield return Press(Key.E);
        yield return Settle(.15f);
        Check(craftingUi.IsVisible, "E opens the table again for tinkering");
        if (!craftingUi.IsVisible && Baseline) { station.OpenCrafting(); yield return Settle(.1f); }
        CheckCraftingLayer("07-tinker-open", view);
        Capture("07-tinker-open");
        view.Slots[1].Button.onClick.Invoke();
        Check(view.TinkerButton.interactable, "The funded second-slot action is available");
        view.TinkerButton.onClick.Invoke();
        yield return Settle(.1f);
        Check(crafting.HasPendingTinkeringChoice, "Actual Tinker button creates its paid choice");
        CheckButtonRaycast(view.Candidates[0].Button, "Tinker candidate");
        view.Candidates[0].Button.onClick.Invoke();
        yield return Until(() => Director.Step == TutorialStep.AutomaticCombat, 3f, "Choosing a real candidate starts the final wave");
        Check(Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None).Length == 4, "All four later scripted enemies remain present");
        KillAndCollect();
        yield return SelectPassiveChoice(lessonChoice);
        yield return Until(() => Director.Step == TutorialStep.Escape, 4f, "Chosen passive reaches Escape");
        Measure("08-escape", true);
        yield return Press(Key.E);
        Check(Director.Step == TutorialStep.Vent, "Physical E advances Escape to the geyser guidance");
        var vent = Get<MapLaunchPad>(Director, "_vent");
        Collider trigger = vent.GetComponent<Collider>();
        Note($"GEYSER before-walk trigger={trigger.bounds} enabled={trigger.enabled} isTrigger={trigger.isTrigger} player={Player.transform.position}");
        Capture("09-geyser-guidance");
        // No TryLaunch invocation or teleport: physical WASD occupancy must reach OnTriggerEnter/Stay.
        yield return DriveTo(trigger.bounds.center, .1f, 20f, () => Player.IsLaunching || !TutorialDirector.IsActive);
        Check(Player.IsLaunching && !TutorialDirector.IsActive, "Walking into the real geyser physically launches and completes tutorial");
        if (!Player.IsLaunching)
        {
            Measure("10-geyser-blocked", false);
            Note($"GEYSER blocked trigger={trigger.bounds} playerCapsule={Player.GetComponent<CapsuleCollider>().bounds} distance={PlanarDistance(Player.transform.position, trigger.bounds.center):F3}");
        }
        Capture("10-geyser-result");
        if (!TutorialDirector.IsActive)
        {
            Check(!SaveManager.Instance.ShouldAutoPlayTutorial && SaveManager.Instance.MetaTutorialPending, "Only successful geyser handoff completes the isolated fresh save");
            Check(Object.FindFirstObjectByType<OrbitalSpawner>().enabled, "Normal spawning is restored after geyser handoff");
            yield return Until(() => !Player.IsLaunching && Player.IsGroundedOnSurface &&
                Get<float>(Player.GetComponent<PlayerAnimationDriver>(), "_landRemaining") <= 0f,
                15f, "The real geyser flight lands and completes its landing pose");
            yield return Settle(.15f);
            Measure("11-geyser-landing", true);
            Capture("11-geyser-landing");
            Check(GameManager.Instance != null && GameManager.Instance.IsPlaying && !GameplayPause.IsUiPaused,
                "Normal gameplay stays active after the geyser landing");
            Check(Object.FindFirstObjectByType<OrbitalSpawner>().isActiveAndEnabled && RunSessionStats.ElapsedSeconds > .1f,
                "Normal spawning and run time remain active after landing");
        }
        FinishChecks();
    }

    [UnityTest]
    public IEnumerator InterruptionsAndRepeatedMenuEntriesKeepFreshAndReplayRules()
    {
        CallLaunch("PrepareScene", true);
        yield return new EnterPlayMode();
        BeginInput("reentry");
        CallLaunch("ActivateTitleWithInMemoryPreferences");
        foreach (bool cinematic in new[] { false, true, false })
        {
            yield return Launch("LaunchFromMenu", cinematic, false);
            yield return Launch("WaitForTutorial", cinematic);
            Check(!Get<GameObject>(Director, "_marker").activeInHierarchy, "Repeated entry resets marker at Welcome (cinematic=" + cinematic + ")");
            yield return Press(Key.E);
            Check(Director.Step == TutorialStep.Movement, "Repeated entry accepts fresh E (cinematic=" + cinematic + ")");
            Measure("entry-" + cinematic, true);
            yield return Launch("ReturnToMenu");
            Check(SaveManager.Instance.ShouldAutoPlayTutorial, "Interrupted lessons remain incomplete in the fresh save");
        }
        SaveManager.Instance.CompleteGameplayTutorial();
        yield return Launch("LaunchFromMenu", false, false);
        yield return Launch("WaitForSelection", false);
        Check(!TutorialDirector.IsActive, "Existing completed save starts a normal run");
        yield return Launch("SelectFirstWeapon");
        yield return Launch("ReturnToMenu");
        yield return Launch("LaunchFromMenu", false, true);
        yield return Launch("WaitForTutorial", false);
        Check(!SaveManager.Instance.ShouldAutoPlayTutorial, "Explicit replay preserves the completed-save flag");
        Check(!Get<GameObject>(Director, "_marker").activeInHierarchy, "Explicit replay resets the marker before E");
        yield return Press(Key.E);
        yield return Launch("ReturnToMenu");
        FinishChecks();
    }

    private IEnumerator Recover(string stage, Vector3 displaced)
    {
        yield return Settle(1.1f); // Let the authored recovery debounce expire.
        Vector3 before = Player.transform.position;
        PlaceForObservation(displaced);
        yield return Settle(.6f);
        yield return Until(() => Player.IsGroundedOnSurface && Get<float>(Player.GetComponent<PlayerAnimationDriver>(), "_landRemaining") <= 0f,
            3f, stage + " completes its real landing pose before standing measurements");
        yield return Settle(.15f); // Finish the authored animation crossfade, too.
        Check(PlanarDistance(Player.transform.position, Get<Transform>(Director, "_spawn").position) < 2f,
            stage + " returns the player to the tutorial spawn");
        Measure(stage, true);
        Capture(stage);
        SetKeys(Key.D);
        Vector3 recovered = Player.transform.position;
        yield return Settle(.4f);
        SetKeys();
        yield return Settle(.25f);
        Check(PlanarDistance(recovered, Player.transform.position) > .25f, stage + " accepts actual movement");
        Measure(stage + "-moved", true);
        // A failing baseline must remain useful for the later crafting/geyser checks.
        if (Baseline && PlanarDistance(recovered, Player.transform.position) <= .25f)
        { Note("BASELINE_SETUP restoring finalized initial height after blocked recovery"); PlaceForObservation(before); yield return Settle(.3f); }
    }

    private IEnumerator SelectPassiveChoice(LevelUpChoiceUI choice)
    {
        // Instantiate this closure only after EnterPlayMode. EditMode domain reload does
        // not restore display classes created before EnterPlayMode in the outer iterator.
        yield return Until(() =>
        {
            ChoiceMenuView menu = Get<ChoiceMenuView>(choice, "_activeView");
            return choice.IsVisible && menu != null && menu.Cards.Any(card =>
                card.gameObject.activeInHierarchy && card.SelectButton != null && card.SelectButton.interactable);
        }, 8f, "Final pickups bind an interactable passive choice");
        Get<ChoiceMenuView>(choice, "_activeView").Cards.First(card => card.gameObject.activeInHierarchy &&
            card.SelectButton != null && card.SelectButton.interactable).SelectButton.onClick.Invoke();
    }

    private IEnumerator DriveTo(Vector3 point, float stoppingDistance, float timeout, Func<bool> done = null)
    {
        Vector3 start = Player.transform.position;
        double deadline = EditorApplication.timeSinceStartup + timeout;
        while (EditorApplication.timeSinceStartup < deadline && (done == null || !done()) && PlanarDistance(Player.transform.position, point) > stoppingDistance)
        {
            Vector3 direction = point - Player.transform.position; direction.y = 0;
            var camera = Camera.main.GetComponent<ThirdPersonCamera>();
            Vector3 forward = camera != null ? camera.GameplayForward : Camera.main.transform.forward;
            forward.y = 0; forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float f = Vector3.Dot(direction.normalized, forward), r = Vector3.Dot(direction.normalized, right);
            var keys = new List<Key>();
            if (Mathf.Abs(f) > .32f) keys.Add(f > 0 ? Key.W : Key.S);
            if (Mathf.Abs(r) > .32f) keys.Add(r > 0 ? Key.D : Key.A);
            SetKeys(keys.ToArray());
            yield return null;
        }
        SetKeys();
        Note($"WALK from={start} target={point} end={Player.transform.position} remaining={PlanarDistance(Player.transform.position, point):F3} launch={Player.IsLaunching} step={Director?.Step}");
        if (done == null || !done()) yield return Settle(.2f);
    }

    private void Measure(string stage, bool grounded)
    {
        object measured = CallLaunch("MeasureTutorialPlacement", stage);
        string json = JsonUtility.ToJson(measured);
        Note("PLACEMENT " + json);
        float capsule = Public<float>(measured, "CapsuleClearance"), visible = Public<float>(measured, "VisibleClearance");
        float hipDelta = Public<float>(measured, "ThreatMinusHips"), headDelta = Public<float>(measured, "ThreatMinusHead");
        if (grounded)
        {
            Check(capsule > -.06f && capsule < .2f, stage + " solid capsule stands on the supporting surface (" + capsule.ToString("F4") + " m)");
            Check(visible > -.12f && visible < .3f, stage + " baked visible feet remain near the supporting surface (" + visible.ToString("F4") + " m)");
        }
        Check(Mathf.Abs(hipDelta) < Mathf.Abs(headDelta), stage + " threat helper stays nearer the hips than the head");
        if (!string.IsNullOrEmpty(Output)) File.WriteAllText(Path.Combine(Output, _prefix + "-" + stage + ".json"), json);
    }

    private void CheckCraftingLayer(string stage, CraftingMenuView view)
    {
        var hud = Get<TutorialHudView>(Director, "_hud");
        var canvas = hud.GetComponentsInParent<Canvas>(true).First(c => c.isRootCanvas || c.overrideSorting);
        var mainGroup = Get<CanvasGroup>(hud, "_group");
        var group = Get<CanvasGroup>(hud, "_craftingGroup");
        Note($"LAYER {stage} tutorial={canvas.name}:{canvas.sortingOrder} crafting={view.Canvas.name}:{view.Canvas.sortingOrder} visible={hud.IsVisible} blocksRaycasts={group.blocksRaycasts}");
        Check(hud.IsVisible && canvas.sortingOrder > view.Canvas.sortingOrder, stage + " tutorial canvas renders above crafting");
        Check(group.alpha > .99f && mainGroup.alpha < .01f, stage + " compact authored lesson replaces the gameplay lesson while crafting is open");
        Check(!group.blocksRaycasts, stage + " tutorial text leaves crafting controls clickable");
    }

    private void CheckButtonRaycast(Button button, string label)
    {
        Assert.That(EventSystem.current, Is.Not.Null, "The authored UI needs its EventSystem.");
        Canvas.ForceUpdateCanvases();
        RectTransform rect = (RectTransform)button.transform;
        Canvas canvas = button.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        GameObject first = hits.Count > 0 ? hits[0].gameObject : null;
        Note($"UI_RAYCAST {label} screen={point} first={(first != null ? first.name : "none")} hits={hits.Count}");
        Check(button.IsInteractable() && first != null && (first == button.gameObject || first.transform.IsChildOf(button.transform)),
            label + " receives the top UI raycast with the tutorial lesson visible");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void KillAndCollect()
    {
        foreach (var actor in Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None))
        {
            string actorName = actor != null ? actor.name : "<destroyed>";
            string stage = Director != null ? Director.Step.ToString() : "<no director>";
            Note("COMBAT_SETUP kill " + actorName + " at " + stage);
            try
            {
                Assert.That(actor, Is.Not.Null, "A listed tutorial actor must exist before the test damages it.");
                EnemyHealth health = actor.GetComponent<EnemyHealth>();
                Assert.That(health, Is.Not.Null, actorName + " needs EnemyHealth. Components: " +
                    string.Join(",", actor.GetComponents<Component>().Select(c => c != null ? c.GetType().Name : "missing")));
                health.ApplyDamage(100000);
            }
            catch (Exception error) { throw new Exception("Tutorial combat setup failed killing '" + actorName + "' at " + stage, error); }
        }
        foreach (var drop in Object.FindObjectsByType<TutorialPickup>(FindObjectsSortMode.None))
        {
            string dropName = drop != null ? drop.name : "<destroyed>";
            string stage = Director != null ? Director.Step.ToString() : "<no director>";
            Note("COMBAT_SETUP collect " + dropName + " at " + stage);
            try { drop.Collect(); }
            catch (Exception error) { throw new Exception("Tutorial combat setup failed collecting '" + dropName + "' at " + stage, error); }
        }
    }
    private void BeginInput(string prefix)
    {
        _prefix = prefix;
        _input = Activator.CreateInstance(LaunchFixture.GetNestedType("RuntimeInput", BindingFlags.NonPublic), true);
        if (!string.IsNullOrEmpty(Output)) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, prefix + "-measurements.txt"), "baseline=" + Baseline + "\n"); }
    }
    private void SetKeys(params Key[] keys) => _input.GetType().GetMethod("SetKeys").Invoke(_input, new object[] { keys });
    private IEnumerator Press(Key key)
    {
        SetKeys();
        yield return null;
        // Do not manually InputSystem.Update: that consumes wasPressedThisFrame before the
        // game's following dynamic Update. Let the normal player loop process both events.
        var keyboard = Get<Keyboard>(_input, "_keyboard");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }
    private static IEnumerator Settle(float seconds) => Launch("ForSeconds", seconds);
    private IEnumerator Until(Func<bool> condition, float timeout, string message)
    {
        double deadline = EditorApplication.timeSinceStartup + timeout;
        while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, message);
        Note("PASS " + message);
    }
    private void Check(bool condition, string message) { Note((condition ? "PASS " : "FINDING ") + message); if (!condition) _findings.Add(message); }
    private void FinishChecks() { Note("RESULT findings=" + _findings.Count); if (!Baseline) Assert.That(_findings, Is.Empty, string.Join("\n", _findings)); }
    private void Note(string message)
    {
        TestContext.WriteLine(message);
        if (!string.IsNullOrEmpty(Output)) File.AppendAllText(Path.Combine(Output, _prefix + "-measurements.txt"), message + "\n");
    }
    private static float PlanarDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    private static void PlaceForObservation(Vector3 point)
    {
        Player.transform.position = point;
        var body = Player.GetComponent<Rigidbody>(); body.position = point; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, HiddenInstance).GetValue(target);
    private static T Public<T>(object target, string field) => (T)target.GetType().GetField(field).GetValue(target);
    private static object CallLaunch(string method, params object[] arguments)
    {
        try { return LaunchFixture.GetMethod(method, HiddenStatic).Invoke(null, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static IEnumerator Launch(string method, params object[] arguments) => (IEnumerator)CallLaunch(method, arguments);

    private void CapturePulseComparison()
    {
        if (string.IsNullOrEmpty(Output)) return;
        // Four renders of the same real Upgrade screen. Only currently available overlays
        // are sampled; the saved prefab, action availability, icons and text are untouched.
        var overlays = Object.FindObjectsByType<CraftingAvailabilityView>(FindObjectsSortMode.None)
            .Where(v => v.Glow != null && v.Glow.gameObject.activeInHierarchy).ToArray();
        var speeds = overlays.Select(v => v.PulseSpeed).ToArray();
        var depths = overlays.Select(v => v.PulseDepth).ToArray();
        var colors = overlays.Select(v => v.OutlineColor).ToArray();
        var fills = overlays.Select(v => v.Glow.color).ToArray();
        var edges = overlays.SelectMany(v => v.OutlineEdges ?? Array.Empty<UnityEngine.UI.Image>()).Where(e => e != null).Distinct().ToArray();
        var sizes = edges.Select(e => e.rectTransform.sizeDelta).ToArray();
        var edgeColors = edges.Select(e => e.color).ToArray();
        try
        {
            foreach (bool stronger in new[] { false, true })
            {
                float speed = stronger ? 4.2f : 2.4f, depth = stronger ? .9f : .72f, alpha = stronger ? 1f : .65f;
                float width = stronger ? 5f : 3f;
                foreach (var overlay in overlays)
                {
                    overlay.PulseSpeed = speed; overlay.PulseDepth = depth;
                    Color outline = overlay.OutlineColor; outline.a = alpha; overlay.OutlineColor = outline;
                }
                foreach (var edge in edges)
                    edge.rectTransform.sizeDelta = edge.name == "Top" || edge.name == "Bottom" ? new Vector2(0, width) : new Vector2(width, 0);
                foreach (bool maximum in new[] { false, true })
                {
                    float sampleTime = (maximum ? 1.5f : .5f) * Mathf.PI / speed;
                    foreach (var overlay in overlays) overlay.RefreshPulse(sampleTime);
                    string name = "06-upgrade-runtime-sampled-" + (stronger ? "after" : "before") + "-" + (maximum ? "maximum" : "minimum");
                    Note($"PULSE_SAMPLE {name} edgeAlpha={alpha} depth={depth} speed={speed} width={width} phaseTime={sampleTime:F6} availableOverlays={overlays.Length}");
                    Capture(name);
                }
            }
        }
        finally
        {
            for (int i = 0; i < overlays.Length; i++)
            { overlays[i].PulseSpeed = speeds[i]; overlays[i].PulseDepth = depths[i]; overlays[i].OutlineColor = colors[i]; overlays[i].Glow.color = fills[i]; }
            for (int i = 0; i < edges.Length; i++) { edges[i].rectTransform.sizeDelta = sizes[i]; edges[i].color = edgeColors[i]; }
        }
    }

    private void Capture(string name)
    {
        if (string.IsNullOrEmpty(Output)) return;
        Camera camera = Camera.main;
        Assert.That(camera, Is.Not.Null);
        var target = new RenderTexture(1920, 1080, 24);
        RenderTexture oldTarget = camera.targetTexture, previous = RenderTexture.active;
        float oldAspect = camera.aspect;
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var oldCameras = canvases.Select(c => c.worldCamera).ToArray();
        var oldPlanes = canvases.Select(c => c.planeDistance).ToArray();
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = target; camera.aspect = 1920f / 1080;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            Canvas.ForceUpdateCanvases();
            foreach (var text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
            if (name == "06-upgrade-open" || name == "07-tinker-open")
            {
                var body = Get<TMPro.TMP_Text>(Get<TutorialHudView>(Director, "_hud"), "_craftingBody");
                Check(!body.isTextOverflowing, name + " authored crafting lesson fits its 1920x1080 capture bounds");
            }
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, _prefix + "-" + name + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = RenderMode.ScreenSpaceOverlay; canvases[i].worldCamera = oldCameras[i]; canvases[i].planeDistance = oldPlanes[i]; }
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = previous;
            Object.DestroyImmediate(target); if (pixels != null) Object.DestroyImmediate(pixels);
        }
    }

    [UnityTearDown]
    public IEnumerator RestoreEnvironment()
    {
        (_input as IDisposable)?.Dispose(); _input = null;
        // Unity's EditMode runner requires ExitPlayMode at the top level of teardown.
        IEnumerator restore = new EntranceCinematicLaunchRuntimeTests().RestoreEnvironment();
        while (restore.MoveNext()) yield return restore.Current;
    }
}
