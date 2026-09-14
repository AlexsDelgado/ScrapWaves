using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameplayUiSceneTests
{
    [TestCase("Assets/Scenes/GameplayScene.unity")]
    [TestCase("Assets/Scenes/SampleScene.unity")]
    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox.unity")]
    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity")]
    [TestCase("Assets/Scenes/Testing/test_balance.unity")]
    [TestCase("Assets/Scenes/Testing/enemiesTesting.unity")]
    public void PlayerScene_HasAuthoredUiUnderSceneRootAndWiredPlayerControllers(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject[] uiRoots = roots.Where(root => root.name == "UI").ToArray();
            Assert.That(uiRoots, Has.Length.EqualTo(1), path + " must have one root UI object.");
            Transform ui = uiRoots[0].transform;
            LevelUpChoiceUI[] choices = Components<LevelUpChoiceUI>(scene);
            Assert.That(choices, Is.Not.Empty, path + " must contain a player choice presenter.");
            foreach (LevelUpChoiceUI choice in choices)
            {
                ChoiceMenuView levelUp = Reference<ChoiceMenuView>(choice, "_levelUpView");
                ChoiceMenuView weaponSelection = Reference<ChoiceMenuView>(choice, "_weaponSelectionView");
                AssertUnderUi(levelUp, ui);
                AssertUnderUi(weaponSelection, ui);
                Assert.That(weaponSelection, Is.Not.SameAs(levelUp));
                Assert.That(levelUp.CanPresent(3), Is.True);
                Assert.That(weaponSelection.CanPresent(2), Is.True);
                Assert.That(choice.GetComponentsInChildren<Canvas>(true), Is.Empty, "The player must not own scene menu canvases.");
                Assert.That(Reference<RunMenuContent>(choice, "_content"), Is.Not.Null);
            }

            CraftingUI[] crafting = Components<CraftingUI>(scene);
            Assert.That(crafting, Is.Not.Empty);
            foreach (CraftingUI controller in crafting)
            {
                CraftingMenuView view = Reference<CraftingMenuView>(controller, "_view");
                AssertUnderUi(view, ui);
                Assert.That(view.IsConfigured, Is.True);
                Assert.That(Reference<RunMenuContent>(controller, "_content"), Is.Not.Null);
            }

            ReticleHud[] reticles = Components<ReticleHud>(scene);
            Assert.That(reticles, Is.Not.Empty);
            foreach (ReticleHud reticle in reticles)
            {
                Assert.That(reticle.HasAuthoredUi, Is.True, path + " reticle must be authored before Play Mode.");
                GameObject canvas = Reference<GameObject>(reticle, "_canvasRoot");
                Assert.That(canvas.transform.IsChildOf(ui), Is.True, "Reticle canvas must belong to the scene UI root.");
            }

            foreach (ChoiceMenuView view in Components<ChoiceMenuView>(scene)) AssertUnderUi(view, ui);
            foreach (CraftingMenuView view in Components<CraftingMenuView>(scene)) AssertUnderUi(view, ui);
            foreach (Canvas canvas in Components<Canvas>(scene))
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                    Assert.That(canvas.transform.IsChildOf(ui), Is.True, "Screen canvas outside UI: " + canvas.name);
            }
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static T Reference<T>(Object owner, string field) where T : Object
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        Assert.That(property, Is.Not.Null, owner.name + "." + field);
        T reference = property.objectReferenceValue as T;
        Assert.That(reference, Is.Not.Null, owner.name + "." + field);
        return reference;
    }

    private static void AssertUnderUi(Component view, Transform ui)
    {
        Assert.That(view, Is.Not.Null);
        Assert.That(view.gameObject.scene, Is.EqualTo(ui.gameObject.scene));
        Assert.That(view.transform.IsChildOf(ui), Is.True, view.name + " must belong to the scene UI root.");
    }
}
