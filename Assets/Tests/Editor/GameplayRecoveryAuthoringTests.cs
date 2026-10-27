using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Checks saved assets affected by the GameplayScene merge recovery.</summary>
public sealed class GameplayRecoveryAuthoringTests
{
    [Test]
    public void GameplayScene_HasWiredHazardPreparationAndContinuousReadableWarning()
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
            TextMeshProUGUI repeat = Reference<TextMeshProUGUI>(warning, "_repeatText");
            CanvasGroup visibility = Reference<CanvasGroup>(warning, "_visibility");
            RectTransform motion = Reference<RectTransform>(warning, "_motion");
            Assert.That(root, Is.SameAs(warning.gameObject));
            Assert.That(visibility.gameObject, Is.SameAs(root));
            Assert.That(text.transform.IsChildOf(root.transform), Is.True);
            Assert.That(repeat.transform.IsChildOf(root.transform), Is.True);
            Assert.That(repeat, Is.Not.SameAs(text), "A second saved text object provides seamless repeated motion.");
            Assert.That(motion, Is.SameAs(text.rectTransform));
            Assert.That(root.GetComponent<RectMask2D>(), Is.Not.Null, "Oversized text must remain clipped to the band.");

            var serializedWarning = new SerializedObject(warning);
            Assert.That(Property(serializedWarning, "_scrollSpeed").floatValue, Is.EqualTo(180f));
            Assert.That(Property(serializedWarning, "_fontSize").floatValue, Is.EqualTo(96f));
            Assert.That(Property(serializedWarning, "_barHeight").floatValue, Is.EqualTo(160f));
            Assert.That(serializedWarning.FindProperty("_lifetimeMode"), Is.Null);
            Assert.That(serializedWarning.FindProperty("_cycleCount"), Is.Null);
            Assert.That(serializedWarning.FindProperty("_durationSeconds"), Is.Null);
            // Headless preview scenes have no display size. Resolve the saved anchored layout
            // against the same 1920x1080 Canvas reference used by the native UI preview.
            Canvas canvas = text.GetComponentsInParent<Canvas>(true).Last();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            ((RectTransform)canvas.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1920f);
            ((RectTransform)canvas.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 1080f);
            for (Transform ancestor = root.transform; ancestor != null; ancestor = ancestor.parent)
                ancestor.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            foreach (bool onGround in new[] { false, true })
            {
                warning.PreviewAt(0f, onGround);
                float viewportWidth = ((RectTransform)root.transform).rect.width;
                Assert.That(viewportWidth, Is.GreaterThan(0f));
                Assert.That(text.GetPreferredValues(text.text).x, Is.GreaterThan(viewportWidth),
                    "The default message itself must be wider than the viewport, rather than shrinking to fit.");
                Assert.That(repeat.text, Is.EqualTo(text.text));
                foreach (TextMeshProUGUI label in new[] { text, repeat })
                {
                    Assert.That(label.fontSize, Is.EqualTo(96f));
                    Assert.That(label.enableAutoSizing, Is.False);
                    Assert.That(label.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
                    Assert.That(label.raycastTarget, Is.False);
                    Assert.That(label.color.r, Is.InRange(.5f, .8f));
                    Assert.That(label.color.g, Is.LessThan(.1f));
                    Assert.That(label.color.b, Is.LessThan(.15f));
                }
            }
            Assert.That(((RectTransform)root.transform).rect.height, Is.EqualTo(160f).Within(.001f));
            Assert.That(visibility.blocksRaycasts, Is.False);
            Assert.That(visibility.interactable, Is.False);
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
