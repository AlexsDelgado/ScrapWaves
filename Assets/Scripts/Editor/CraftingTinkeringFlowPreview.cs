#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Controlled interaction and rendering of the production crafting prefab; no scene/save writes.</summary>
public static class CraftingTinkeringFlowPreview
{
    [MenuItem("ScrapWaves/Validation/Crafting/Capture Tinkering Flow")]
    public static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_TINKERING_PREVIEW_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "ScrapWavesTinkeringPreview");
        Directory.CreateDirectory(output);
        Scene scene = EditorSceneManager.NewPreviewScene();
        float timeScale = Time.timeScale;
        var random = UnityEngine.Random.state;
        SaveManager save = SaveManager.Instance;
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        try
        {
            SetSave(null); UnityEngine.Random.InitState(169);
            Camera camera = InScene(new GameObject("Crafting preview camera", typeof(Camera)), scene).GetComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .035f, .03f);
            camera.orthographic = true; camera.orthographicSize = 5; camera.transform.position = new Vector3(0, 0, -10);
            camera.targetTexture = target;
            EventSystem events = InScene(new GameObject("Preview pointer events", typeof(EventSystem)), scene).GetComponent<EventSystem>();
            GameObject owner = new GameObject("In-memory crafting preview"); owner.SetActive(false); InScene(owner, scene);
            MaterialInventory inventory = owner.AddComponent<MaterialInventory>();
            WeaponManager manager = owner.AddComponent<WeaponManager>();
            PlayerStats stats = owner.AddComponent<PlayerStats>();
            PlayerStats authoredStats = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab").GetComponent<PlayerStats>();
            Set(stats, "_statDefinitions", typeof(PlayerStats).GetField("_statDefinitions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(authoredStats));
            typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
            Set(manager, "_stats", stats);
            WeaponCraftingService service = owner.AddComponent<WeaponCraftingService>();
            CraftingUI controller = owner.AddComponent<CraftingUI>();
            Set(service, "_inventory", inventory); Set(service, "_weaponManager", manager);
            service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
            var pool = new List<WeaponData>();
            foreach (string name in new[] { "RocketLauncher", "AutomaticCannon", "Flamethrower", "Mortar", "RotatingBlade" })
                pool.Add(AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{name}.asset"));
            Set(service, "_weaponPool", pool);
            manager.AddWeapon(pool[0]);
            foreach (MaterialType material in Enum.GetValues(typeof(MaterialType))) inventory.Add(material, 100);
            GameObject menu = InScene(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RunMenuPrefabBuilder.CraftingPrefabPath)), scene);
            CraftingMenuView view = menu.GetComponent<CraftingMenuView>();
            view.Canvas.renderMode = RenderMode.ScreenSpaceCamera; view.Canvas.worldCamera = camera; view.Canvas.planeDistance = 1;
            Set(controller, "_view", view);
            if (!controller.PresentCoroutine(service, inventory, () => { }).MoveNext()) throw new Exception("Crafting did not open.");
            Render(camera, target, output, "01-station.png");
            Click(view.Slots[1].Button, events, camera, view.Canvas);
            if (service.HasPendingTinkeringChoice || view.TinkerChoicePanel.activeSelf) throw new Exception("Offer appeared before Tinker.");
            Render(camera, target, output, "02-before-tinker.png");
            int spends = 0; inventory.OnMaterialsSpent += () => spends++;
            Click(view.TinkerButton, events, camera, view.Canvas);
            WeaponData chosen = service.GetTinkeringOffer()[0], discarded = service.GetTinkeringOffer()[1];
            if (!controller.IsChoosingWeapon || spends != 1) throw new Exception("Tinker did not reserve one cost and open a choice.");
            if (CanClick(view.CloseButton, events, camera, view.Canvas)) throw new Exception("Popup allowed clicking the station behind it.");
            Render(camera, target, output, "03-mandatory-choice.png");
            Click(view.Candidates[0].Button, events, camera, view.Canvas);
            if (controller.IsChoosingWeapon || !manager.TryGetEquippedWeapon(chosen, out _) || spends != 1
                || service.BuildUnequippedWeapons().Contains(discarded)) throw new Exception("Choice award, exclusion or cost failed.");
            Render(camera, target, output, "04-weapon-received.png");
            File.WriteAllText(Path.Combine(output, "verification.txt"),
                $"Production prefab pointer raycasts: empty slot -> Tinker -> mandatory popup -> {chosen.DisplayName}.\n"
                + $"Unchosen {discarded.DisplayName} excluded. Exactly {spends} cost transaction. Underlying close button blocked.\n"
                + "Controlled editor fixture with real presenter/service; not live gameplay footage.\n");
            view.CloseButton.onClick.Invoke();
            Debug.Log("Tinkering flow verified and captured: " + output);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(target);
            Time.timeScale = timeScale; UnityEngine.Random.state = random; SetSave(save);
        }
    }

    private static GameObject InScene(GameObject root, Scene scene) { SceneManager.MoveGameObjectToScene(root, scene); return root; }
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static void SetSave(SaveManager value) => typeof(SaveManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private static bool CanClick(Button button, EventSystem events, Camera camera, Canvas canvas)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center)) };
        var hits = new List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        return hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject && button.IsInteractable();
    }
    private static void Click(Button button, EventSystem events, Camera camera, Canvas canvas)
    {
        if (!CanClick(button, events, camera, canvas)) throw new Exception("Authored button is not reachable: " + button.name);
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    private static void Render(Camera camera, RenderTexture target, string output, string name)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) text.ForceMeshUpdate();
        camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
}
#endif
