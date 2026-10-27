#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Editor-only production prefab interaction/rendering fixture; never changes scene or save assets.</summary>
public static class CraftingAdvancedTinkeringFlowPreview
{
    [MenuItem("ScrapWaves/Validation/Crafting/Capture Advanced Tinkering Flow")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture outside Play Mode.");
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_ADVANCED_PREVIEW_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "ScrapWavesAdvancedPreview");
        Directory.CreateDirectory(output);
        Scene scene = EditorSceneManager.NewPreviewScene();
        float timeScale = Time.timeScale;
        var random = UnityEngine.Random.state;
        SaveManager save = SaveManager.Instance;
        EventSystem previousEvents = EventSystem.current;
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        var evidence = new StringBuilder();
        CraftingUI controller = null;
        try
        {
            SetSave(null);
            UnityEngine.Random.InitState(169);
            Camera camera = InScene(new GameObject("Advanced preview camera", typeof(Camera)), scene).GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .03f);
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.targetTexture = target;
            EventSystem events = InScene(new GameObject("Preview pointer events", typeof(EventSystem)), scene).GetComponent<EventSystem>();
            EventSystem.current = events;
            GameObject owner = new GameObject("In-memory advanced crafting preview");
            owner.SetActive(false);
            InScene(owner, scene);
            MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
            WeaponManager manager = owner.AddComponent<WeaponManager>();
            PlayerStats stats = owner.AddComponent<PlayerStats>();
            PlayerStats authoredStats = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab").GetComponent<PlayerStats>();
            Set(stats, "_statDefinitions", typeof(PlayerStats).GetField("_statDefinitions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(authoredStats));
            typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
            Set(manager, "_stats", stats);
            WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
            controller = owner.AddComponent<CraftingUI>();
            Set(service, "_inventory", inventory);
            Set(service, "_weaponManager", manager);
            service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
            var weapons = new List<WeaponData>();
            foreach (string name in new[] { "RocketLauncher", "Mortar", "RotatingBlade" })
            {
                WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{name}.asset");
                Check(manager.AddWeapon(data) && manager.TryGetEquippedWeapon(data, out _), "Could not equip preview weapon.");
                manager.TryGetEquippedWeapon(data, out WeaponInstance instance);
                while (instance.Level < 5) manager.UpgradeWeapon(instance);
                weapons.Add(data);
            }
            foreach (MaterialType material in Enum.GetValues(typeof(MaterialType))) inventory.Add(material, 100);
            GameObject menu = InScene(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RunMenuPrefabBuilder.CraftingPrefabPath)), scene);
            CraftingMenuView view = menu.GetComponent<CraftingMenuView>();
            Check(view.IsConfigured, "Authored crafting view has missing references.");
            view.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            view.Canvas.worldCamera = camera;
            view.Canvas.planeDistance = 1;
            Set(controller, "_view", view);
            Set(controller, "_content", AssetDatabase.LoadAssetAtPath<RunMenuContent>(RunMenuPrefabBuilder.ContentPath));
            Check(controller.PresentCoroutine(service, inventory, () => { }).MoveNext(), "Crafting did not open.");
            WeaponData weapon = weapons[0];
            manager.TryGetEquippedWeapon(weapon, out WeaponInstance runtime);
            Check(!service.TryGetAdvancedOffer(weapon, out _) && !view.AdvancedChoicePanel.activeSelf
                && string.IsNullOrEmpty(view.AdvancedPathText.text), "An offer leaked before payment.");
            CaptureFrame(view, camera, target, output, "01-before-purchase.png", evidence);
            int spends = 0;
            inventory.OnMaterialsSpent += () => spends++;
            var firstCost = new List<MaterialCost>(service.GetAdvancedTinkeringCost(weapon));
            var before = Snapshot(inventory);
            Click(view.AdvancedPurchaseButton, events, camera, view.Canvas);
            Check(service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath firstOffer), "Paid offer was not revealed.");
            Check(spends == 1 && runtime.Level == 5 && runtime.SelectedPath == WeaponUpgradePath.None,
                "Purchase must charge once and leave the upgrade unapplied.");
            CheckDebit(before, inventory, firstCost);
            CheckModal(view, events, camera);
            view.AdvancedPurchaseButton.onClick.Invoke();
            view.CloseButton.onClick.Invoke();
            Check(spends == 1 && controller.IsVisible && !controller.CloseForTutorial(), "Repeated purchase or close changed the paid choice.");
            CaptureFrame(view, camera, target, output, "02-paid-offer.png", evidence);
            var paid = Snapshot(inventory);
            Click(view.DeclineButton, events, camera, view.Canvas);
            Check(!service.HasPendingAdvancedChoice && runtime.Level == 5 && runtime.SelectedPath == WeaponUpgradePath.None,
                "Decline applied an upgrade or retained the resolved offer.");
            CheckSame(paid, inventory);
            Check(!service.TryGetAdvancedOffer(weapon, out _) && !view.AdvancedChoicePanel.activeSelf
                && string.IsNullOrEmpty(view.AdvancedPathText.text), "Retry path leaked before its purchase.");
            CaptureFrame(view, camera, target, output, "03-declined.png", evidence);
            var retryCost = new List<MaterialCost>(service.GetAdvancedTinkeringCost(weapon));
            Click(view.AdvancedPurchaseButton, events, camera, view.Canvas);
            CheckDebit(paid, inventory, retryCost);
            Check(service.TryGetAdvancedOffer(weapon, out WeaponUpgradePath retryOffer) && retryOffer != firstOffer
                && spends == 2 && !view.DeclineButton.interactable, "Guaranteed retry, price or decline restriction failed.");
            CheckModal(view, events, camera);
            CaptureFrame(view, camera, target, output, "04-paid-guaranteed-retry.png", evidence);
            var retryPaid = Snapshot(inventory);
            Click(view.AcceptButton, events, camera, view.Canvas);
            Check(runtime.Level == 6 && runtime.SelectedPath == retryOffer && spends == 2 && !service.HasPendingAdvancedChoice,
                "Accept did not apply the paid path exactly once.");
            CheckSame(retryPaid, inventory);
            CaptureFrame(view, camera, target, output, "05-accepted.png", evidence);
            evidence.AppendLine($"RocketLauncher: paid {firstOffer}, declined without refund, paid configured retry, accepted {retryOffer}; exactly two transactions.");
            for (int i = 1; i < weapons.Count; i++)
            {
                Click(view.Slots[i].Button, events, camera, view.Canvas);
                Click(view.AdvancedPurchaseButton, events, camera, view.Canvas);
                CheckModal(view, events, camera);
                CaptureFrame(view, camera, target, output, $"0{5 + i}-{weapons[i].name}-offer.png", evidence);
                Click(view.AcceptButton, events, camera, view.Canvas);
            }
            Click(view.CloseButton, events, camera, view.Canvas);
            evidence.AppendLine("All purchases used production prefab pointer raycasts; underlying station controls were blocked by the authored modal.");
            evidence.AppendLine("Every visible popup label fits its authored rect; popup labels remain inside the authored window. No scene/save writes.");
            evidence.AppendLine("Controlled editor preview using real presenter/service, not live gameplay footage or a physical controller test.");
            File.WriteAllText(Path.Combine(output, "verification.txt"), evidence.ToString());
            Debug.Log("Advanced Tinkering flow verified and captured: " + output);
        }
        finally
        {
            // The inactive fixture owner may never receive OnDestroy; explicitly release any interrupted UI pause.
            if (controller != null) typeof(CraftingUI).GetMethod("ClosePresentation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(target);
            EventSystem.current = previousEvents;
            Time.timeScale = timeScale;
            UnityEngine.Random.state = random;
            SetSave(save);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static GameObject InScene(GameObject root, Scene scene) { SceneManager.MoveGameObjectToScene(root, scene); return root; }
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static void SetSave(SaveManager value) => typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private static Dictionary<MaterialType, int> Snapshot(MaterialInventory inventory)
    {
        var result = new Dictionary<MaterialType, int>();
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType))) result[type] = inventory.GetAmount(type);
        return result;
    }
    private static void CheckSame(Dictionary<MaterialType, int> expected, MaterialInventory inventory)
    {
        foreach (var entry in expected) Check(inventory.GetAmount(entry.Key) == entry.Value, "Resolution changed material balance.");
    }
    private static void CheckDebit(Dictionary<MaterialType, int> before, MaterialInventory inventory, IReadOnlyList<MaterialCost> costs)
    {
        var expected = new Dictionary<MaterialType, int>(before);
        foreach (MaterialCost cost in costs) expected[cost.Material] -= cost.Amount;
        CheckSame(expected, inventory);
    }
    private static RaycastResult Hit(Button button, EventSystem events, Camera camera, Canvas canvas)
    {
        Canvas.ForceUpdateCanvases();
        // Newly enabled editor-preview graphics receive their raycast depth when the displayed frame is rendered.
        camera.Render();
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center)) };
        var hits = new List<RaycastResult>();
        canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        Check(hits.Count > 0, "No authored UI at button position: " + button.name);
        return hits[0];
    }
    private static void Click(Button button, EventSystem events, Camera camera, Canvas canvas)
    {
        Check(button.IsInteractable() && ExecuteEvents.GetEventHandler<IPointerClickHandler>(Hit(button, events, camera, canvas).gameObject)
            == button.gameObject, "Authored button is not reachable: " + button.name);
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    private static void CheckModal(CraftingMenuView view, EventSystem events, Camera camera)
    {
        Check(view.AdvancedChoicePanel.activeInHierarchy, "Paid popup is hidden.");
        foreach (Button underlying in new[] { view.CloseButton, view.Slots[0].Button, view.AdvancedPurchaseButton })
        {
            RaycastResult hit = Hit(underlying, events, camera, view.Canvas);
            Check(hit.gameObject.transform.IsChildOf(view.AdvancedChoicePanel.transform),
                $"Authored modal failed to block underlying button: {underlying.name}; top hit={HierarchyPath(hit.gameObject.transform)}, depth={hit.depth}, distance={hit.distance:0.00}");
        }
    }
    private static string HierarchyPath(Transform current)
    {
        string path = current.name;
        while (current.parent != null) { current = current.parent; path = current.name + "/" + path; }
        return path;
    }
    private static void CaptureFrame(CraftingMenuView view, Camera camera, RenderTexture target, string output, string name, StringBuilder evidence)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        if (view.AdvancedChoicePanel.activeInHierarchy)
        {
            RectTransform window = (RectTransform)view.AdvancedChoicePanel.transform.Find("Window");
            var corners = new Vector3[4];
            foreach (TMP_Text text in view.AdvancedChoicePanel.GetComponentsInChildren<TMP_Text>())
            {
                float preferredHeight = text.GetPreferredValues(text.text, text.rectTransform.rect.width, Mathf.Infinity).y;
                Check(!text.isTextOverflowing && preferredHeight <= text.rectTransform.rect.height + 1f,
                    $"Popup text does not fit: {text.name} ({preferredHeight:0.0}/{text.rectTransform.rect.height:0.0})");
                text.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                    Check(window.rect.Contains((Vector2)window.InverseTransformPoint(corner)), "Popup text is outside window: " + text.name);
            }
            evidence.AppendLine(name + ": " + view.AdvancedDescriptionText.text.Replace('\n', ' '));
        }
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
}
#endif
