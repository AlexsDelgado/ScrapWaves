using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks saved assets affected by the GameplayScene merge recovery.</summary>
public sealed class GameplayRecoveryAuthoringTests
{
    [Test]
    public void GameplayScene_HasWiredHazardPreparationAndTwoCycleWarning()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/GameplayScene.unity");
        try
        {
            UpperLevelVolume volume = Single<UpperLevelVolume>(preview);
            PlayerUpperLevelOverheat hazard = Single<PlayerUpperLevelOverheat>(preview);
            PlayerPlatformState platform = Reference<PlayerPlatformState>(hazard, "_platformState");
            OverheatManager manager = Reference<OverheatManager>(hazard, "_overheatManager");

            Assert.That(Reference<UpperLevelVolume>(hazard, "_upperLevelVolume"), Is.SameAs(volume));
            Assert.That(Reference<UpperLevelVolume>(platform, "_upperLevelVolume"), Is.SameAs(volume));
            Assert.That(Reference<PlayerPlatformState>(manager, "_playerPlatformState"), Is.SameAs(platform));
            Assert.That(volume.StartingDamage, Is.EqualTo(1f).Within(.0001f));
            Assert.That(volume.DamageMultiplier, Is.EqualTo(1.3f).Within(.0001f));
            Assert.That(manager.ConfiguredPreparationDuration, Is.EqualTo(5f).Within(.0001f));

            OverheatWarningHud warning = Single<OverheatWarningHud>(preview);
            Assert.That(Reference<OverheatManager>(warning, "_overheatManager"), Is.SameAs(manager));
            Assert.That(Reference<PlayerPlatformState>(warning, "_playerPlatformState"), Is.SameAs(platform));
            GameObject root = Reference<GameObject>(warning, "_root");
            TextMeshProUGUI text = Reference<TextMeshProUGUI>(warning, "_text");
            CanvasGroup visibility = Reference<CanvasGroup>(warning, "_visibility");
            RectTransform motion = Reference<RectTransform>(warning, "_motion");
            Assert.That(root, Is.SameAs(warning.gameObject));
            Assert.That(visibility.gameObject, Is.SameAs(root));
            Assert.That(text.transform.IsChildOf(root.transform), Is.True);
            Assert.That(motion, Is.SameAs(text.rectTransform));

            var serializedWarning = new SerializedObject(warning);
            Assert.That(Property(serializedWarning, "_lifetimeMode").intValue,
                Is.EqualTo((int)OverheatWarningLifetimeMode.Cycles));
            Assert.That(Property(serializedWarning, "_cycleCount").intValue, Is.EqualTo(2));
            Assert.That(warning.CycleSeconds, Is.EqualTo(2.4f).Within(.0001f));
            Assert.That(warning.DisplayBudgetSeconds, Is.EqualTo(4.8f).Within(.0001f));
        }
        finally
        {
            // A preview reads the saved scene without replacing or saving any open scene.
            EditorSceneManager.ClosePreviewScene(preview);
            if (previousActive.IsValid() && previousActive.isLoaded &&
                SceneManager.GetActiveScene() != previousActive)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    [TestCase("Assets/Prefabs/Drone.prefab")]
    [TestCase("Assets/Prefabs/Drone (variant).prefab")]
    public void OrdinaryDronePrefab_HasAuthoredHoverHeightRange(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        EnemyStatRandomizer randomizer = prefab.GetComponent<EnemyStatRandomizer>();
        Assert.That(randomizer, Is.Not.Null, path + " must retain its per-spawn randomizer.");
        var serializedRandomizer = new SerializedObject(randomizer);
        Vector2 range = Property(serializedRandomizer, "_droneHoverHeightMultiplierRange").vector2Value;
        Assert.That(range.x, Is.EqualTo(.8f).Within(.0001f), path + " minimum hover multiplier");
        Assert.That(range.y, Is.EqualTo(1.2f).Within(.0001f), path + " maximum hover multiplier");
    }

    private static T Single<T>(Scene scene) where T : Component
    {
        T[] components = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        Assert.That(components, Has.Length.EqualTo(1), "Saved GameplayScene: " + typeof(T).Name);
        return components[0];
    }

    private static T Reference<T>(Object owner, string field) where T : Object
    {
        T reference = Property(new SerializedObject(owner), field).objectReferenceValue as T;
        Assert.That(reference, Is.Not.Null, owner.name + "." + field);
        return reference;
    }

    private static SerializedProperty Property(SerializedObject owner, string field)
    {
        SerializedProperty property = owner.FindProperty(field);
        Assert.That(property, Is.Not.Null, owner.targetObject.name + "." + field);
        return property;
    }
}
