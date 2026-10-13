using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Exercises the production offer text and authored level-up cards. The native capture is
/// a deterministic Editor fixture, not a randomly rolled level-up during live gameplay.
/// </summary>
public sealed class LevelUpStatPresentationTests
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string MenuPath = "Assets/Prefabs/UI/RunMenus/LevelUpMenu.prefab";
    private const string PassiveFolder = "Assets/ScriptableObjects/PlayerSO/Passives/";
    private const string OutputFolder = "Logs/LevelUpStatFormatting";
    private static readonly string[] ItemNames =
    {
        "Shop_Arm_HonedWeaponrySystem", "Shop_Leg_BionicBoots", "Shop_Core_PlatedReactor"
    };
    private static readonly string[] ExpectedDescriptions =
    {
        "Slot: Arm.\n+15% Damage",
        "Slot: Leg.\n+10% Movement Speed",
        "Slot: Core.\n+30 HP Max Health"
    };

    [Test]
    public void ProductionOffers_BindAuthoredCardsAndSelectOnlyOnceWithoutChangingData()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        float previousTimeScale = Time.timeScale;
        int previousPauseLocks = GameplayPause.LockCount;
        CursorLockMode previousCursorLock = Cursor.lockState;
        bool previousCursorVisible = Cursor.visible;
        LevelUpChoiceUI controller = null;
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
            Assert.That(prefab, Is.Not.Null);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            ChoiceMenuView view = root.GetComponent<ChoiceMenuView>();
            var owner = new GameObject("Level-up presentation test controller");
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.SetActive(false);
            controller = owner.AddComponent<LevelUpChoiceUI>();
            SetField(controller, "_levelUpView", view);
            // This isolated binding test does not need to block any camera in an open user scene.
            ThirdPersonCamera fixtureCamera = owner.AddComponent<ThirdPersonCamera>();
            SetField(fixtureCamera, "_lockCursorOnPlay", false);
            SetField(controller, "_resolvedCamera", fixtureCamera);
            PassiveItemData[] items = LoadItems();
            string[] before = items.Select(item => EditorJsonUtility.ToJson(item)).ToArray();
            int[] hierarchy = Hierarchy(view);
            int selected = -1;
            int callbackCount = 0;
            controller.Show("LEVEL UP", BuildOptions(items), index => { selected = index; callbackCount++; });

            AssertCards(view, items);
            Assert.That(Hierarchy(view), Is.EqualTo(hierarchy), "Binding must use the authored hierarchy.");
            Assert.That(GameplayPause.LockCount, Is.EqualTo(previousPauseLocks + 1));
            view.Cards[2].SelectButton.onClick.Invoke();
            view.Cards[2].SelectButton.onClick.Invoke();
            Assert.That(selected, Is.EqualTo(2));
            Assert.That(callbackCount, Is.EqualTo(1), "The same card must not apply twice.");
            Assert.That(controller.IsVisible, Is.False);
            Assert.That(GameplayPause.LockCount, Is.EqualTo(previousPauseLocks));
            Assert.That(items.Select(item => EditorJsonUtility.ToJson(item)).ToArray(), Is.EqualTo(before));
        }
        finally
        {
            ClosePresentation(controller);
            EditorSceneManager.ClosePreviewScene(scene);
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }

    [Test]
    [Category("NativeCapture")]
    public void NativeGameplayScene_CapturesFormattedProductionOffersWithoutOverflow()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Native UI capture needs a graphics device; run Unity without -nographics.");

        // A preview scene is a disposable, isolated copy. No open user scene is replaced or saved.
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        float previousTimeScale = Time.timeScale;
        int previousPauseLocks = GameplayPause.LockCount;
        CursorLockMode previousCursorLock = Cursor.lockState;
        bool previousCursorVisible = Cursor.visible;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        LevelUpChoiceUI controller = null;
        Camera camera = null;
        try
        {
            controller = Components<LevelUpChoiceUI>(scene).Single();
            ChoiceMenuView view = Reference<ChoiceMenuView>(controller, "_levelUpView");
            Assert.That(view.gameObject.scene, Is.EqualTo(scene));
            Assert.That(view.CanPresent(3), Is.True);
            camera = Components<Camera>(scene).Single(value => value.CompareTag("MainCamera"));
            camera.scene = scene;
            // Resolve only this fixture's authored camera, even if the user has another scene open.
            SetField(controller, "_thirdPersonCamera", camera.GetComponent<ThirdPersonCamera>());
            SetField(controller, "_resolvedCamera", camera.GetComponent<ThirdPersonCamera>());
            foreach (Canvas canvas in Components<Canvas>(scene))
                if (canvas != view.Canvas && !canvas.transform.IsChildOf(view.transform))
                    canvas.enabled = false;

            PassiveItemData[] items = LoadItems();
            string[] before = items.Select(item => EditorJsonUtility.ToJson(item)).ToArray();
            int[] hierarchy = Hierarchy(view);
            string authoredTitle = view.TitleText.text;
            string authoredSubtitle = view.SubtitleText.text;
            string authoredFooter = view.FooterText.text;
            IEnumerator presentation = controller.PresentCoroutine("Ignored authored heading override", BuildOptions(items), _ => { });
            Assert.That(presentation.MoveNext(), Is.True);
            Assert.That(controller.IsVisible, Is.True);
            AssertCards(view, items);
            Assert.That(view.TitleText.text, Is.EqualTo(authoredTitle));
            Assert.That(view.SubtitleText.text, Is.EqualTo(authoredSubtitle));
            Assert.That(view.FooterText.text, Is.EqualTo(authoredFooter));

            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.Create();
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.aspect = 1920f / 1080f;
            // Route the real authored overlay to Unity's capture camera. No replacement UI is built.
            view.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            view.Canvas.worldCamera = camera;
            view.Canvas.planeDistance = camera.nearClipPlane + 1f;
            view.Canvas.enabled = true;
            Canvas.ForceUpdateCanvases();
            CanvasScaler scaler = view.Canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)view.transform);
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>())
                text.ForceMeshUpdate(true, true);
            camera.Render();
            Canvas.ForceUpdateCanvases();

            var notes = new StringBuilder();
            notes.AppendLine("Controlled native Unity Editor fixture, not live gameplay or a random level-up roll.");
            notes.AppendLine("Source: " + ScenePath + "; existing scene LevelUpChoiceUI and authored LevelUpMenu hierarchy.");
            notes.AppendLine("Production PassiveItemUiText -> LevelUpChoiceUI.PresentCoroutine -> ChoiceMenuView -> ChoiceCardView.");
            notes.AppendLine("1920x1080 Unity Camera.Render capture; original overlay temporarily routed to that camera.");
            notes.AppendLine("Other screen canvases hidden in the disposable preview scene to isolate the level-up menu.");
            notes.AppendLine("No source scenes, prefabs, item values, or gameplay data are saved or changed.");
            foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate(true, true);
                Assert.That(text.isTextOverflowing, Is.False, text.name + ": " + text.text);
                notes.AppendLine(text.name + " | overflow=false | " + text.text.Replace("\n", " / "));
            }
            Assert.That(Hierarchy(view), Is.EqualTo(hierarchy), "The preview must not construct UI objects.");
            Assert.That(items.Select(item => EditorJsonUtility.ToJson(item)).ToArray(), Is.EqualTo(before));
            for (int i = 0; i < items.Length; i++)
                notes.AppendLine(AssetDatabase.GetAssetPath(items[i]) + " | " + ExpectedDescriptions[i].Replace("\n", " / "));

            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory(OutputFolder);
            string output = Path.Combine(OutputFolder, "level-up-stats.png");
            File.WriteAllBytes(output, pixels.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutputFolder, "capture-notes.txt"), notes.ToString());
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(10000), "Native image should contain the rendered menu.");
            Debug.Log("LEVEL_UP_STAT_CAPTURE_COMPLETE " + Path.GetFullPath(output));

            view.Cards[0].SelectButton.onClick.Invoke();
            Assert.That(presentation.MoveNext(), Is.False);
            Assert.That(GameplayPause.LockCount, Is.EqualTo(previousPauseLocks));
        }
        finally
        {
            ClosePresentation(controller);
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = previousActive;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }

    private static PassiveItemData[] LoadItems() => ItemNames.Select(name =>
    {
        PassiveItemData item = AssetDatabase.LoadAssetAtPath<PassiveItemData>(PassiveFolder + name + ".asset");
        Assert.That(item, Is.Not.Null, name);
        return item;
    }).ToArray();

    private static LevelUpChoiceOption[] BuildOptions(PassiveItemData[] items) => items.Select(item =>
    {
        var offer = new PassiveItemOffer(item, false, null);
        return new LevelUpChoiceOption(offer.DisplayLabel, PassiveItemUiText.BuildOfferDescription(offer),
            item.Icon, PassiveItemUiText.GetPlaceholderKind(item));
    }).ToArray();

    private static void AssertCards(ChoiceMenuView view, PassiveItemData[] items)
    {
        Assert.That(view.Cards, Has.Length.EqualTo(items.Length));
        for (int i = 0; i < items.Length; i++)
        {
            ChoiceCardView card = view.Cards[i];
            Assert.That(card.IsConfigured, Is.True);
            Assert.That(card.NameText.text, Is.EqualTo(items[i].DisplayName));
            Assert.That(card.DescriptionText.text, Is.EqualTo(ExpectedDescriptions[i]));
            Assert.That(card.Icon.sprite, Is.EqualTo(items[i].Icon));
            Assert.That(card.Icon.enabled, Is.True);
            Assert.That(card.SelectButton.interactable, Is.True);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static int[] Hierarchy(Component root) => root.GetComponentsInChildren<Transform>(true)
        .Select(value => value.GetInstanceID()).OrderBy(value => value).ToArray();

    private static T Reference<T>(Object owner, string field) where T : Object
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        Assert.That(property, Is.Not.Null, field);
        T value = property.objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, field);
        return value;
    }

    private static void SetField(object owner, string field, object value) =>
        owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

    private static void ClosePresentation(LevelUpChoiceUI controller)
    {
        if (controller != null)
            typeof(LevelUpChoiceUI).GetMethod("Hide", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
    }
}
