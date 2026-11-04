using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CraftingStationPromptTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene _scene;

    [SetUp]
    public void SetUp()
    {
        _scene = EditorSceneManager.NewPreviewScene();
    }

    [TearDown]
    public void TearDown()
    {
        if (!_scene.IsValid() || !_scene.isLoaded) return;
        foreach (var root in _scene.GetRootGameObjects())
            foreach (var station in root.GetComponentsInChildren<CraftingStation>(true))
                CraftingStationRotation.Unregister(station);
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    [Test]
    public void AuthoredPrompt_ChangesBetweenNormalAndCrossedGreyWithoutCreatingObjects()
    {
        var station = CreateStation("Prompt target");
        Set(station, "_initialBreakChancePercent", 100f);
        CraftingStationRotation.InitializeScene(_scene);
        var replacement = CreateStation("Replacement");
        Set(replacement, "_initialBreakChancePercent", 100f);

        var root = new GameObject("Authored prompt");
        SceneManager.MoveGameObjectToScene(root, _scene);
        root.transform.SetParent(station.transform, false);
        var visual = new GameObject("Animated visual");
        SceneManager.MoveGameObjectToScene(visual, _scene);
        visual.transform.SetParent(root.transform, false);
        var available = new GameObject("Normal E");
        SceneManager.MoveGameObjectToScene(available, _scene);
        available.transform.SetParent(visual.transform, false);
        var unavailable = new GameObject("Grey E with cross");
        SceneManager.MoveGameObjectToScene(unavailable, _scene);
        unavailable.transform.SetParent(visual.transform, false);
        var cross = new GameObject("Authored cross");
        SceneManager.MoveGameObjectToScene(cross, _scene);
        cross.transform.SetParent(unavailable.transform, false);
        var prompt = root.AddComponent<InteractPrompt>();
        Set(prompt, "_station", station);
        Set(prompt, "_visual", visual.transform);
        Set(prompt, "_availableVisual", available);
        Set(prompt, "_unavailableVisual", unavailable);
        int[] hierarchy = root.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()).ToArray();

        Call(prompt, "Awake");
        Assert.That(available.activeSelf, Is.True);
        Assert.That(unavailable.activeSelf, Is.False);
        Assert.That(station.CompleteTransaction(), Is.True);
        Call(prompt, "RefreshAvailability");
        Assert.That(available.activeSelf, Is.False);
        Assert.That(unavailable.activeSelf, Is.True);
        Assert.That(cross.activeSelf, Is.True);
        Assert.That(root.activeSelf, Is.True, "The prompt root must stay alive for nearby disabled feedback.");
        Assert.That(station.gameObject.activeSelf, Is.True);

        Assert.That(replacement.CompleteTransaction(), Is.True);
        Call(prompt, "RefreshAvailability");
        Assert.That(available.activeSelf, Is.True);
        Assert.That(unavailable.activeSelf, Is.False);
        Assert.That(root.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()), Is.EqualTo(hierarchy));
    }

    [Test]
    public void AuthoredPromptPrefab_HasBothStateVisualsUnderTheAnimatedRoot()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/InteractPrompt_E.prefab");
        Assert.That(prefab, Is.Not.Null);
        var prompt = prefab.GetComponent<InteractPrompt>();
        Assert.That(prompt, Is.Not.Null);
        var visual = Get<Transform>(prompt, "_visual");
        var available = Get<GameObject>(prompt, "_availableVisual");
        var unavailable = Get<GameObject>(prompt, "_unavailableVisual");
        Assert.That(visual, Is.Not.Null);
        Assert.That(available, Is.Not.Null);
        Assert.That(unavailable, Is.Not.Null);
        Assert.That(available, Is.Not.SameAs(unavailable));
        Assert.That(available.transform.IsChildOf(visual), Is.True);
        Assert.That(unavailable.transform.IsChildOf(visual), Is.True);
        Assert.That(available.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
        Assert.That(unavailable.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
    }

    private CraftingStation CreateStation(string name)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, _scene);
        var station = root.AddComponent<CraftingStation>();
        CraftingStationRotation.Register(station);
        return station;
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);
}
