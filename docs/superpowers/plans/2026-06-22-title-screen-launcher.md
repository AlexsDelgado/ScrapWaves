# Title Screen Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dedicated title scene with three launch buttons and a consistent `Back to Title` flow from gameplay pause, gameplay end screen, and both debug scenes.

**Architecture:** Add one shared `SceneNavigation` helper that owns canonical scene names and resets `Time.timeScale` before scene changes. Build the title scene and debug pause UI at runtime from code using the existing UI helpers, and extend the current gameplay HUD components instead of introducing a second gameplay navigation system.

**Tech Stack:** Unity, C#, NUnit editor tests, TextMeshPro, Unity UI, Input System

---

## File Structure

- Create: `Assets/Scripts/SceneNavigation.cs`
  Responsibility: canonical scene names, timescale reset, guarded scene loading entry points.
- Create: `Assets/Scripts/UI/TitleScreenController.cs`
  Responsibility: runtime-built title menu UI and menu button wiring.
- Create: `Assets/Scripts/UI/SimpleScenePauseMenu.cs`
  Responsibility: minimal pause overlay for debug scenes with `Resume` and `Back to Title`.
- Modify: `Assets/Scripts/Weapon/UI/PauseMenuUI.cs`
  Responsibility: add gameplay pause return-to-title action.
- Modify: `Assets/Scripts/Weapon/UI/RunEndScreenUI.cs`
  Responsibility: add gameplay run-end return-to-title action.
- Modify: `Assets/Scripts/Weapon/UI/GameplayHudHierarchyBuilder.cs`
  Responsibility: ensure fallback run-end hierarchy includes named `Retry` and `Back to Title` buttons so `RunEndScreenUI` can wire them.
- Create: `Assets/Tests/Editor/SceneNavigationTests.cs`
  Responsibility: shared routing tests.
- Create: `Assets/Tests/Editor/TitleScreenControllerTests.cs`
  Responsibility: title screen UI construction and build-settings registration tests.
- Create: `Assets/Tests/Editor/SceneReturnUiTests.cs`
  Responsibility: gameplay pause and run-end UI button coverage.
- Create: `Assets/Tests/Editor/SimpleScenePauseMenuTests.cs`
  Responsibility: debug pause UI construction and pause-state behavior coverage.
- Create: `Assets/Scenes/TitleScreen.unity`
  Responsibility: dedicated boot scene containing a root object with `TitleScreenController`.
- Modify: `Assets/Scenes/WeaponTestingSandbox.unity`
  Responsibility: add a root `DebugPauseMenu` object with `SimpleScenePauseMenu`.
- Modify: `Assets/Scenes/enemiesTesting.unity`
  Responsibility: add a root `DebugPauseMenu` object with `SimpleScenePauseMenu`.
- Modify: `ProjectSettings/EditorBuildSettings.asset`
  Responsibility: enable and order scenes as `TitleScreen`, `SampleScene`, `WeaponTestingSandbox`, `enemiesTesting`.

### Task 1: Add the shared scene navigation helper

**Files:**
- Create: `Assets/Tests/Editor/SceneNavigationTests.cs`
- Create: `Assets/Scripts/SceneNavigation.cs`

- [ ] **Step 1: Write the failing routing tests**

```csharp
using NUnit.Framework;
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
    [TestCase(SceneDestination.Play, "SampleScene")]
    [TestCase(SceneDestination.WeaponSandbox, "WeaponTestingSandbox")]
    [TestCase(SceneDestination.EnemiesTesting, "enemiesTesting")]
    public void GetSceneName_ReturnsExpectedNames(SceneDestination destination, string expected)
    {
        Assert.That(SceneNavigation.GetSceneName(destination), Is.EqualTo(expected));
    }

    [Test]
    public void PrepareForSceneChange_ResetsTimeScaleToOne()
    {
        Time.timeScale = 0f;

        SceneNavigation.PrepareForSceneChange();

        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: Unity editor test runner for `SceneNavigationTests`
Expected: FAIL because `SceneDestination` and `SceneNavigation` do not exist yet.

- [ ] **Step 3: Write the minimal shared navigation helper**

```csharp
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
    public const string PlaySceneName = "SampleScene";
    public const string WeaponSandboxSceneName = "WeaponTestingSandbox";
    public const string EnemiesTestingSceneName = "enemiesTesting";

    public static string GetSceneName(SceneDestination destination) => destination switch
    {
        SceneDestination.Title => TitleSceneName,
        SceneDestination.Play => PlaySceneName,
        SceneDestination.WeaponSandbox => WeaponSandboxSceneName,
        SceneDestination.EnemiesTesting => EnemiesTestingSceneName,
        _ => PlaySceneName
    };

    public static void PrepareForSceneChange()
    {
        Time.timeScale = 1f;
    }

    public static void Load(SceneDestination destination)
    {
        string sceneName = GetSceneName(destination);
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"SceneNavigation: scene '{sceneName}' is not available in build settings.");
            return;
        }

        PrepareForSceneChange();
        SceneManager.LoadScene(sceneName);
    }

    public static void LoadTitle() => Load(SceneDestination.Title);
    public static void LoadPlay() => Load(SceneDestination.Play);
    public static void LoadWeaponSandbox() => Load(SceneDestination.WeaponSandbox);
    public static void LoadEnemiesTesting() => Load(SceneDestination.EnemiesTesting);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: Unity editor test runner for `SceneNavigationTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/Editor/SceneNavigationTests.cs Assets/Scripts/SceneNavigation.cs
git commit -m "test: cover shared scene navigation"
```

### Task 2: Build the title screen scene and boot-scene registration

**Files:**
- Create: `Assets/Tests/Editor/TitleScreenControllerTests.cs`
- Create: `Assets/Scripts/UI/TitleScreenController.cs`
- Create: `Assets/Scenes/TitleScreen.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset`

- [ ] **Step 1: Write the failing title-screen tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TitleScreenControllerTests
{
    private readonly List<Object> cleanupObjects = new();

    [SetUp]
    public void SetUp()
    {
        cleanupObjects.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanupObjects.Count - 1; i >= 0; i--)
        {
            if (cleanupObjects[i] != null)
                Object.DestroyImmediate(cleanupObjects[i]);
        }

        foreach (EventSystem eventSystem in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (eventSystem != null)
                Object.DestroyImmediate(eventSystem.gameObject);
        }

        cleanupObjects.Clear();
    }

    [Test]
    public void Awake_CreatesThreeExpectedButtons()
    {
        var host = Track(new GameObject("TitleScreenRoot"));
        var controller = host.AddComponent<TitleScreenController>();

        InvokePrivate(controller, "Awake");

        string[] labels = host.GetComponentsInChildren<TextMeshProUGUI>(true)
            .Select(text => text.text)
            .Where(text => text == "Play" || text == "Weapon Sandbox" || text == "Enemies Testing")
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "Play", "Weapon Sandbox", "Enemies Testing" },
            labels);
        Assert.That(host.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(3));
    }

    [Test]
    public void Awake_CreatesEventSystem_WhenMissing()
    {
        var host = Track(new GameObject("TitleScreenRoot"));
        var controller = host.AddComponent<TitleScreenController>();

        InvokePrivate(controller, "Awake");

        Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Not.Null);
    }

#if UNITY_EDITOR
    [Test]
    public void BuildSettings_RegisterExpectedSceneOrder()
    {
        string[] enabledScenePaths = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .Take(4)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Assets/Scenes/TitleScreen.unity",
                "Assets/Scenes/SampleScene.unity",
                "Assets/Scenes/WeaponTestingSandbox.unity",
                "Assets/Scenes/enemiesTesting.unity"
            },
            enabledScenePaths);
    }
#endif

    private T Track<T>(T unityObject) where T : Object
    {
        cleanupObjects.Add(unityObject);
        return unityObject;
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(instance, null);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: Unity editor test runner for `TitleScreenControllerTests`
Expected: FAIL because `TitleScreenController` and the `TitleScreen` scene are missing.

- [ ] **Step 3: Write the runtime title-screen controller**

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

[DisallowMultipleComponent]
public class TitleScreenController : MonoBehaviour
{
    private static readonly (string objectName, string label, Action action)[] Buttons =
    {
        ("PlayButton", "Play", SceneNavigation.LoadPlay),
        ("WeaponSandboxButton", "Weapon Sandbox", SceneNavigation.LoadWeaponSandbox),
        ("EnemiesTestingButton", "Enemies Testing", SceneNavigation.LoadEnemiesTesting)
    };

    private void Awake()
    {
        EnsureEventSystem();
        BuildUi();
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("TitleScreenCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        HudUiWire.StretchFull(canvasGo.GetComponent<RectTransform>());

        var overlay = HudUiFactory.CreatePanel(canvasGo.transform, "Overlay", Vector2.zero);
        overlay.color = new Color(0.05f, 0.07f, 0.09f, 0.96f);
        HudUiWire.StretchFull(overlay.GetComponent<RectTransform>());
        overlay.raycastTarget = true;

        var panel = HudUiFactory.CreatePanel(canvasGo.transform, "MenuPanel", new Vector2(520f, 420f));
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;

        var title = HudUiFactory.CreateLabel(panel.transform, "Title", "SCRAP WAVES", 42f, TextAlignmentOptions.Center);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(420f, 70f);
        titleRt.anchoredPosition = new Vector2(0f, -32f);
        title.fontStyle = FontStyles.Bold;

        for (int i = 0; i < Buttons.Length; i++)
        {
            (string objectName, string label, Action action) = Buttons[i];
            CreateMenuButton(panel.transform, objectName, label, new Vector2(0f, -120f - (i * 84f)), action);
        }
    }

    private static void CreateMenuButton(Transform parent, string objectName, string label, Vector2 anchoredPosition, Action action)
    {
        Button button = HudUiFactory.CreateButton(parent, label, new Vector2(280f, 56f));
        button.gameObject.name = objectName;
        var rt = button.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPosition;
        button.onClick.AddListener(() => action());
    }
}
```

- [ ] **Step 4: Create the boot scene and register build order**

Create `Assets/Scenes/TitleScreen.unity` in the Unity editor with:

- one root object named `TitleScreenRoot`
- the `TitleScreenController` component on that root

Then update `ProjectSettings/EditorBuildSettings.asset` so the enabled order is:

1. `Assets/Scenes/TitleScreen.unity`
2. `Assets/Scenes/SampleScene.unity`
3. `Assets/Scenes/WeaponTestingSandbox.unity`
4. `Assets/Scenes/enemiesTesting.unity`

- [ ] **Step 5: Run the test to verify it passes**

Run: Unity editor test runner for `TitleScreenControllerTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Assets/Tests/Editor/TitleScreenControllerTests.cs Assets/Scripts/UI/TitleScreenController.cs Assets/Scenes/TitleScreen.unity ProjectSettings/EditorBuildSettings.asset
git commit -m "feat: add title screen launcher scene"
```

### Task 3: Add `Back to Title` to the gameplay pause and run-end UI

**Files:**
- Create: `Assets/Tests/Editor/SceneReturnUiTests.cs`
- Modify: `Assets/Scripts/Weapon/UI/PauseMenuUI.cs`
- Modify: `Assets/Scripts/Weapon/UI/RunEndScreenUI.cs`
- Modify: `Assets/Scripts/Weapon/UI/GameplayHudHierarchyBuilder.cs`

- [ ] **Step 1: Write the failing gameplay UI tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SceneReturnUiTests
{
    private readonly List<Object> cleanupObjects = new();

    [SetUp]
    public void SetUp()
    {
        cleanupObjects.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanupObjects.Count - 1; i >= 0; i--)
        {
            if (cleanupObjects[i] != null)
                Object.DestroyImmediate(cleanupObjects[i]);
        }

        cleanupObjects.Clear();
    }

    [Test]
    public void PauseMenuUi_Awake_CreatesBackToTitleButton()
    {
        var host = Track(new GameObject("PauseMenuHost"));
        var pauseMenu = host.AddComponent<PauseMenuUI>();

        InvokePrivate(pauseMenu, "Awake");

        Assert.That(FindButtonByLabel(host, "Back to Title"), Is.Not.Null);
        Assert.That(FindButtonByLabel(host, "Continuar"), Is.Not.Null);
    }

    [Test]
    public void RunEndScreenUi_Awake_CreatesRetryAndBackToTitleButtons()
    {
        var host = Track(new GameObject("RunEndHost"));
        var runEndScreen = host.AddComponent<RunEndScreenUI>();

        InvokePrivate(runEndScreen, "Awake");

        Assert.That(FindButtonByLabel(host, "Reintentar"), Is.Not.Null);
        Assert.That(FindButtonByLabel(host, "Back to Title"), Is.Not.Null);
    }

    private T Track<T>(T unityObject) where T : Object
    {
        cleanupObjects.Add(unityObject);
        return unityObject;
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(instance, null);
    }

    private static Button FindButtonByLabel(GameObject root, string label)
    {
        return root.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(button =>
            {
                TextMeshProUGUI tmp = button.GetComponentInChildren<TextMeshProUGUI>(true);
                return tmp != null && tmp.text == label;
            });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: Unity editor test runner for `SceneReturnUiTests`
Expected: FAIL because neither runtime UI currently builds a `Back to Title` button.

- [ ] **Step 3: Extend `PauseMenuUI` with a return action**

Add a new button under the existing resume button and wire it to the shared scene helper.

```csharp
private void ReturnToTitle()
{
    _isPaused = false;
    _root.SetActive(false);
    _camera?.SetLookBlockedByUi(false);
    SceneNavigation.LoadTitle();
}
```

```csharp
var backBtn = HudUiFactory.CreateButton(_root.transform, "Back to Title", new Vector2(220f, 48f));
backBtn.gameObject.name = "BackToTitleButton";
var backRt = backBtn.GetComponent<RectTransform>();
backRt.anchorMin = new Vector2(0.5f, 0.5f);
backRt.anchorMax = new Vector2(0.5f, 0.5f);
backRt.pivot = new Vector2(0.5f, 0.5f);
backRt.anchoredPosition = new Vector2(0f, -100f);
backBtn.onClick.AddListener(ReturnToTitle);
```

- [ ] **Step 4: Extend the run-end UI and fallback hierarchy**

Update `RunEndScreenUI` so it can wire both buttons:

```csharp
private Button _titleButton;
```

```csharp
_retryButton = panel != null ? HudUiWire.FindButton(panel, "RetryButton") : HudUiWire.FindButton(runEndRoot, "RetryButton");
_titleButton = panel != null ? HudUiWire.FindButton(panel, "BackToTitleButton") : HudUiWire.FindButton(runEndRoot, "BackToTitleButton");

if (_retryButton != null)
{
    _retryButton.onClick.RemoveListener(Retry);
    _retryButton.onClick.AddListener(Retry);
}

if (_titleButton != null)
{
    _titleButton.onClick.RemoveListener(BackToTitle);
    _titleButton.onClick.AddListener(BackToTitle);
}
```

```csharp
private void BackToTitle()
{
    _camera?.SetLookBlockedByUi(false);
    SceneNavigation.LoadTitle();
}
```

Update `GameplayHudHierarchyBuilder.BuildRunEndHierarchy()` so the fallback hierarchy creates stable button names:

```csharp
var retryButton = HudUiFactory.CreateButton(panelGo.transform, "RetryButton", new Vector2(240f, 52f));
retryButton.gameObject.name = "RetryButton";
var retryLabel = retryButton.GetComponentInChildren<TextMeshProUGUI>();
if (retryLabel != null)
    retryLabel.text = "Reintentar";

var titleButton = HudUiFactory.CreateButton(panelGo.transform, "Back to Title", new Vector2(240f, 52f));
titleButton.gameObject.name = "BackToTitleButton";
```

- [ ] **Step 5: Run the test to verify it passes**

Run: Unity editor test runner for `SceneReturnUiTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Assets/Tests/Editor/SceneReturnUiTests.cs Assets/Scripts/Weapon/UI/PauseMenuUI.cs Assets/Scripts/Weapon/UI/RunEndScreenUI.cs Assets/Scripts/Weapon/UI/GameplayHudHierarchyBuilder.cs
git commit -m "feat: add gameplay return to title actions"
```

### Task 4: Add a minimal pause menu to both debug scenes

**Files:**
- Create: `Assets/Tests/Editor/SimpleScenePauseMenuTests.cs`
- Create: `Assets/Scripts/UI/SimpleScenePauseMenu.cs`
- Modify: `Assets/Scenes/WeaponTestingSandbox.unity`
- Modify: `Assets/Scenes/enemiesTesting.unity`

- [ ] **Step 1: Write the failing debug-pause tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SimpleScenePauseMenuTests
{
    private readonly List<Object> cleanupObjects = new();

    [SetUp]
    public void SetUp()
    {
        cleanupObjects.Clear();
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        for (int i = cleanupObjects.Count - 1; i >= 0; i--)
        {
            if (cleanupObjects[i] != null)
                Object.DestroyImmediate(cleanupObjects[i]);
        }

        foreach (EventSystem eventSystem in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (eventSystem != null)
                Object.DestroyImmediate(eventSystem.gameObject);
        }

        cleanupObjects.Clear();
    }

    [Test]
    public void Awake_CreatesResumeAndBackToTitleButtons()
    {
        var host = Track(new GameObject("DebugPauseMenu"));
        var pauseMenu = host.AddComponent<SimpleScenePauseMenu>();

        InvokePrivate(pauseMenu, "Awake");

        string[] labels = host.GetComponentsInChildren<TextMeshProUGUI>(true)
            .Select(text => text.text)
            .ToArray();

        CollectionAssert.IsSubsetOf(new[] { "Resume", "Back to Title" }, labels);
        Assert.That(host.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(2));
    }

    [Test]
    public void ShowPause_AndResume_ToggleTimeScale()
    {
        var host = Track(new GameObject("DebugPauseMenu"));
        var pauseMenu = host.AddComponent<SimpleScenePauseMenu>();

        InvokePrivate(pauseMenu, "Awake");
        InvokePrivate(pauseMenu, "ShowPause");
        Assert.That(Time.timeScale, Is.EqualTo(0f));

        InvokePrivate(pauseMenu, "Resume");
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void Awake_CreatesEventSystem_WhenMissing()
    {
        var host = Track(new GameObject("DebugPauseMenu"));
        var pauseMenu = host.AddComponent<SimpleScenePauseMenu>();

        InvokePrivate(pauseMenu, "Awake");

        Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Not.Null);
    }

    private T Track<T>(T unityObject) where T : Object
    {
        cleanupObjects.Add(unityObject);
        return unityObject;
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(instance, null);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: Unity editor test runner for `SimpleScenePauseMenuTests`
Expected: FAIL because `SimpleScenePauseMenu` does not exist yet.

- [ ] **Step 3: Write the minimal debug-scene pause menu**

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

[DisallowMultipleComponent]
public class SimpleScenePauseMenu : MonoBehaviour
{
    private GameObject _root;
    private float _savedTimeScale = 1f;
    private bool _isPaused;

    private void Awake()
    {
        EnsureEventSystem();
        BuildUi();
        _root.SetActive(false);
    }

    private void OnDisable()
    {
        if (_isPaused)
        {
            _isPaused = false;
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        if (!WasEscapePressed())
            return;

        if (_isPaused)
        {
            Resume();
            return;
        }

        ShowPause();
    }

    private static bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private void ShowPause()
    {
        _isPaused = true;
        _savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        _root.SetActive(true);
    }

    private void Resume()
    {
        _isPaused = false;
        _root.SetActive(false);
        Time.timeScale = _savedTimeScale > 0.001f ? _savedTimeScale : 1f;
    }

    private void BackToTitle()
    {
        _isPaused = false;
        _root.SetActive(false);
        SceneNavigation.LoadTitle();
    }

    private void BuildUi()
    {
        _root = new GameObject("PauseRoot", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        HudUiWire.StretchFull(_root.GetComponent<RectTransform>());

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1200;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _root.AddComponent<GraphicRaycaster>();

        var overlay = HudUiFactory.CreatePanel(_root.transform, "Overlay", Vector2.zero);
        overlay.color = new Color(0f, 0f, 0f, 0.74f);
        overlay.raycastTarget = true;
        HudUiWire.StretchFull(overlay.GetComponent<RectTransform>());

        var title = HudUiFactory.CreateLabel(_root.transform, "Title", "PAUSED", 42f, TMPro.TextAlignmentOptions.Center);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(320f, 56f);
        titleRt.anchoredPosition = new Vector2(0f, 80f);

        Button resumeButton = HudUiFactory.CreateButton(_root.transform, "Resume", new Vector2(220f, 48f));
        resumeButton.gameObject.name = "ResumeButton";
        var resumeRt = resumeButton.GetComponent<RectTransform>();
        resumeRt.anchorMin = new Vector2(0.5f, 0.5f);
        resumeRt.anchorMax = new Vector2(0.5f, 0.5f);
        resumeRt.pivot = new Vector2(0.5f, 0.5f);
        resumeRt.anchoredPosition = new Vector2(0f, 0f);
        resumeButton.onClick.AddListener(Resume);

        Button titleButton = HudUiFactory.CreateButton(_root.transform, "Back to Title", new Vector2(220f, 48f));
        titleButton.gameObject.name = "BackToTitleButton";
        var titleButtonRt = titleButton.GetComponent<RectTransform>();
        titleButtonRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleButtonRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleButtonRt.pivot = new Vector2(0.5f, 0.5f);
        titleButtonRt.anchoredPosition = new Vector2(0f, -64f);
        titleButton.onClick.AddListener(BackToTitle);
    }
}
```

- [ ] **Step 4: Attach the pause menu to both debug scenes**

In the Unity editor:

- open `Assets/Scenes/WeaponTestingSandbox.unity`
- add a new root object named `DebugPauseMenu`
- attach `SimpleScenePauseMenu`
- save the scene

Then repeat the same steps in `Assets/Scenes/enemiesTesting.unity`.

- [ ] **Step 5: Run the test to verify it passes**

Run: Unity editor test runner for `SimpleScenePauseMenuTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Assets/Tests/Editor/SimpleScenePauseMenuTests.cs Assets/Scripts/UI/SimpleScenePauseMenu.cs Assets/Scenes/WeaponTestingSandbox.unity Assets/Scenes/enemiesTesting.unity
git commit -m "feat: add debug scene pause return menu"
```

### Task 5: Verify the full launcher flow

**Files:**
- Test: `Assets/Tests/Editor/SceneNavigationTests.cs`
- Test: `Assets/Tests/Editor/TitleScreenControllerTests.cs`
- Test: `Assets/Tests/Editor/SceneReturnUiTests.cs`
- Test: `Assets/Tests/Editor/SimpleScenePauseMenuTests.cs`
- Verify: `Assets/Scenes/TitleScreen.unity`
- Verify: `Assets/Scenes/SampleScene.unity`
- Verify: `Assets/Scenes/WeaponTestingSandbox.unity`
- Verify: `Assets/Scenes/enemiesTesting.unity`
- Verify: `ProjectSettings/EditorBuildSettings.asset`

- [ ] **Step 1: Run the new editor test files**

Run: Unity editor test runner for:
- `SceneNavigationTests`
- `TitleScreenControllerTests`
- `SceneReturnUiTests`
- `SimpleScenePauseMenuTests`

Expected: PASS for all four test files.

- [ ] **Step 2: Run the full editor test suite**

Run: Unity editor test runner for all tests under `Assets/Tests/Editor`
Expected: PASS with no regressions outside the launcher work.

- [ ] **Step 3: Manually verify the scene launcher flow**

Run the game in the Unity editor and confirm:

- boot starts on `TitleScreen`
- `Play` loads `SampleScene`
- `Weapon Sandbox` loads `WeaponTestingSandbox`
- `Enemies Testing` loads `enemiesTesting`
- `Escape` opens pause in `SampleScene`
- gameplay pause `Back to Title` returns to `TitleScreen`
- gameplay run-end `Back to Title` returns to `TitleScreen`
- `Escape` opens pause in both debug scenes
- debug-scene `Back to Title` returns to `TitleScreen`
- returning to title after a paused scene does not leave the next scene frozen

- [ ] **Step 4: Commit the completed feature**

```bash
git add Assets/Scripts/SceneNavigation.cs Assets/Scripts/UI/TitleScreenController.cs Assets/Scripts/UI/SimpleScenePauseMenu.cs Assets/Scripts/Weapon/UI/PauseMenuUI.cs Assets/Scripts/Weapon/UI/RunEndScreenUI.cs Assets/Scripts/Weapon/UI/GameplayHudHierarchyBuilder.cs Assets/Tests/Editor/SceneNavigationTests.cs Assets/Tests/Editor/TitleScreenControllerTests.cs Assets/Tests/Editor/SceneReturnUiTests.cs Assets/Tests/Editor/SimpleScenePauseMenuTests.cs Assets/Scenes/TitleScreen.unity Assets/Scenes/WeaponTestingSandbox.unity Assets/Scenes/enemiesTesting.unity ProjectSettings/EditorBuildSettings.asset
git commit -m "feat: add title screen launcher flow"
```
