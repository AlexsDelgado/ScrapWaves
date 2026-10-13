using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks saved lesson content without authoring, saving or entering Play Mode.</summary>
public sealed class TutorialFeedbackAuthoringTests
{
    private const string GameplayScenePath = "Assets/Scenes/GameplayScene.unity";
    private SceneSetup[] _previousSetup;
    private Scene _scene;
    private bool _openedScene;

    [OneTimeSetUp]
    public void OpenSavedGameplayScene()
    {
        Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False,
            "Authoring validation runs only in Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            Assert.That(scene.isDirty, Is.False,
                "Save or discard dirty scenes before running authoring validation: " + scene.name);
            Assert.That(string.IsNullOrEmpty(scene.path) && scene.rootCount > 0, Is.False,
                "Save an untitled scene with content before running authoring validation.");
        }
        _previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(_previousSetup.Length > 1 && _previousSetup.Any(item => string.IsNullOrEmpty(item.path)), Is.False,
            "Save untitled additive scenes before running authoring validation so the setup can be restored.");
        _scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
        _openedScene = true;
    }

    [OneTimeTearDown]
    public void RestorePreviousSceneSetup()
    {
        if (!_openedScene) return;
        // Only the clean scene opened by this fixture is discarded; no assets are saved.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (_previousSetup != null && _previousSetup.Length > 0
            && _previousSetup.All(item => !string.IsNullOrEmpty(item.path)))
            EditorSceneManager.RestoreSceneManagerSetup(_previousSetup);
        _openedScene = false;
    }

    [Test]
    public void SavedScene_HasNoActiveOrdinaryEnemyOutsideTheLesson()
    {
        Transform world = World();
        EnemyHealth[] ordinaryActiveEnemies = All<EnemyHealth>()
            .Where(enemy => !enemy.transform.IsChildOf(world) && enemy.gameObject.activeInHierarchy)
            .ToArray();

        Assert.That(ordinaryActiveEnemies, Is.Empty,
            "A scene-authored ordinary enemy must not appear before scripted tutorial combat. Found: "
            + string.Join(", ", ordinaryActiveEnemies.Select(enemy => enemy.name)));
    }

    [Test]
    public void SavedLesson_PreservesAllEightScriptedEnemiesAndWaveAssignments()
    {
        TutorialDirector director = Director();
        Transform world = World();
        TutorialEnemy[] actors = world.GetComponentsInChildren<TutorialEnemy>(true);
        Assert.That(actors, Has.Length.EqualTo(8));
        Assert.That(actors.All(actor => actor.GetComponent<EnemyHealth>() != null), Is.True,
            "Each scripted actor retains its gameplay health component.");
        Assert.That(actors.All(actor => !actor.gameObject.activeSelf), Is.True,
            "Scripted enemies remain dormant until the director activates their wave.");

        using var serialized = new SerializedObject(director);
        TutorialEnemy[] first = References<TutorialEnemy>(serialized, "_firstEnemies");
        TutorialEnemy[] second = References<TutorialEnemy>(serialized, "_secondEnemies");
        TutorialEnemy[] last = References<TutorialEnemy>(serialized, "_lastEnemies");
        Assert.That(first, Has.Length.EqualTo(1));
        Assert.That(second, Has.Length.EqualTo(3));
        Assert.That(last, Has.Length.EqualTo(4));
        TutorialEnemy[] assigned = first.Concat(second).Concat(last).ToArray();
        Assert.That(assigned.All(actor => actor != null), Is.True);
        Assert.That(assigned.Distinct().Count(), Is.EqualTo(8), "Wave references cannot duplicate or omit an actor.");
        CollectionAssert.AreEquivalent(actors, assigned);
    }

    [Test]
    public void SavedMovementMarker_IsHiddenUntilItsLesson()
    {
        GameObject marker = Reference<GameObject>(Director(), "_marker");
        Assert.That(marker.transform.IsChildOf(World()), Is.True);
        Assert.That(marker.activeSelf, Is.False,
            "The movement destination is authored hidden so Welcome cannot reveal it.");
    }

    [Test]
    public void LessonCanvas_IsAboveTheAuthoredCraftingMenu()
    {
        TutorialHudView hud = Reference<TutorialHudView>(Director(), "_hud");
        CraftingUI crafting = Reference<CraftingUI>(Director(), "_craftingUi");
        CraftingMenuView menu = Reference<CraftingMenuView>(crafting, "_view");
        Canvas lessonCanvas = hud.GetComponentInParent<Canvas>(true);
        Assert.That(lessonCanvas, Is.Not.Null);
        Assert.That(menu.Canvas, Is.Not.Null);
        Assert.That(lessonCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(lessonCanvas.sortingOrder, Is.EqualTo(5150));
        Assert.That(menu.Canvas.sortingOrder, Is.EqualTo(5100));
        Assert.That(lessonCanvas.sortingOrder, Is.GreaterThan(menu.Canvas.sortingOrder));
    }

    [Test]
    public void CraftingLesson_HasItsOwnCompactAuthoredPanelAndPassThroughReferences()
    {
        TutorialDirector director = Director();
        TutorialHudView hud = Reference<TutorialHudView>(director, "_hud");
        CanvasGroup main = Reference<CanvasGroup>(hud, "_group");
        CanvasGroup crafting = Reference<CanvasGroup>(hud, "_craftingGroup");
        TMP_Text title = Reference<TMP_Text>(hud, "_craftingTitle");
        TMP_Text body = Reference<TMP_Text>(hud, "_craftingBody");
        CraftingUI craftingUi = Reference<CraftingUI>(hud, "_craftingUi");
        Canvas canvas = hud.GetComponentInParent<Canvas>(true);

        Assert.That(hud.HasReferences, Is.True);
        Assert.That(crafting, Is.Not.SameAs(main));
        Assert.That(craftingUi, Is.SameAs(Reference<CraftingUI>(director, "_craftingUi")));
        Assert.That(main.GetComponentInParent<Canvas>(true), Is.SameAs(canvas));
        Assert.That(crafting.GetComponentInParent<Canvas>(true), Is.SameAs(canvas));
        Assert.That(title.transform.IsChildOf(crafting.transform), Is.True);
        Assert.That(body.transform.IsChildOf(crafting.transform), Is.True);
        Assert.That(title, Is.Not.SameAs(body));
        Assert.That(title.raycastTarget, Is.False);
        Assert.That(body.raycastTarget, Is.False);

        RectTransform panel = crafting.GetComponent<RectTransform>();
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel.sizeDelta.x, Is.EqualTo(1680f).Within(0.01f));
        Assert.That(panel.sizeDelta.y, Is.EqualTo(140f).Within(0.01f));
        Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(panel.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(panel.pivot.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(panel.anchoredPosition.y, Is.EqualTo(-20f).Within(0.01f));
        foreach (CanvasGroup group in new[] { main, crafting })
        {
            Assert.That(group.alpha, Is.Zero, "Both authored lesson variants start hidden.");
            Assert.That(group.interactable, Is.False);
            Assert.That(group.blocksRaycasts, Is.False,
                "The lesson must not intercept crafting controls beneath its canvas.");
        }
    }

    private T[] All<T>() where T : Component => _scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private TutorialDirector Director()
    {
        TutorialDirector[] directors = All<TutorialDirector>();
        Assert.That(directors, Has.Length.EqualTo(1));
        return directors[0];
    }

    private Transform World() => Reference<GameObject>(Director(), "_world").transform;

    private T Reference<T>(Object target, string field) where T : Object
    {
        using var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        Assert.That(property, Is.Not.Null, target.name + "." + field + " must be serialized.");
        T value = property.objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, target.name + "." + field + " must reference saved content.");
        if (value is Component component) Assert.That(component.gameObject.scene, Is.EqualTo(_scene));
        if (value is GameObject gameObject) Assert.That(gameObject.scene, Is.EqualTo(_scene));
        return value;
    }

    private static T[] References<T>(SerializedObject serialized, string field) where T : Object
    {
        SerializedProperty property = serialized.FindProperty(field);
        Assert.That(property, Is.Not.Null, field + " must be serialized.");
        Assert.That(property.isArray, Is.True);
        return Enumerable.Range(0, property.arraySize)
            .Select(index => property.GetArrayElementAtIndex(index).objectReferenceValue as T).ToArray();
    }
}

