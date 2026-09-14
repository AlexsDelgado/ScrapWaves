#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring only. Runtime presenters keep references to scene-owned views.</summary>
public static class GameplayUiSceneMigration
{
    public const string PlayerPath = "Assets/Prefabs/player.prefab";

    [MenuItem("ScrapWaves/UI/Organize Current Scene Under UI")]
    public static void OrganizeCurrentScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        RunMenuPrefabBuilder.CreateMissingMenuAssets();
        AuthorScene(SceneManager.GetActiveScene());
    }

    [MenuItem("ScrapWaves/UI/Migrate All Player Scenes Under UI")]
    public static void MigrateAllPlayerScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Organize UI outside Play Mode.");
        RunMenuPrefabBuilder.CreateMissingMenuAssets();
        string guid = AssetDatabase.AssetPathToGUID(PlayerPath);
        string[] paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => File.ReadAllText(path).Contains(guid)).ToArray();
        Scene active = SceneManager.GetActiveScene();
        foreach (string path in paths.OrderBy(path => path == active.path ? 0 : 1))
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                // Save a separate copy, including pending Inspector changes, before moving anything.
                string backup = "Library/GameplayUiMigration/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "/" + scene.name + ".unity";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                EditorSceneManager.SaveScene(scene, backup, true);
                SceneManager.SetActiveScene(scene);
                AuthorScene(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
        RemovePlayerMenuVisuals();
        if (active.IsValid() && active.isLoaded)
        {
            SceneManager.SetActiveScene(active);
            EditorSceneManager.SaveScene(active);
            Selection.activeGameObject = active.GetRootGameObjects().FirstOrDefault(go => go.name == "UI");
        }
        Debug.Log($"Authored gameplay UI under UI in {paths.Length} player scenes. Player presenters retain scene references; testing tools are unchanged.");
    }

    public static void AuthorScene(Scene scene)
    {
        var players = Find<LevelUpChoiceUI>(scene).Where(component =>
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(component.gameObject) == PlayerPath).ToArray();
        if (players.Length != 1)
            throw new InvalidOperationException($"Expected one player prefab in {scene.path}, found {players.Length}.");
        GameObject player = players[0].gameObject;
        Transform ui = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "UI")?.transform;
        if (ui == null)
        {
            var root = new GameObject("UI");
            SceneManager.MoveGameObjectToScene(root, scene);
            ui = root.transform;
        }

        // Move complete authored HUD roots, keeping their canvases and all layout overrides together.
        foreach (GameplayHudRoot hud in Find<GameplayHudRoot>(scene)) MoveUnder(hud.transform, ui);
        foreach (Canvas canvas in Find<Canvas>(scene).ToArray())
        {
            if (canvas.transform.IsChildOf(player.transform) || canvas.transform.IsChildOf(ui)) continue;
            if (canvas.GetComponentInParent<Canvas>() != canvas) continue;
            Transform root = canvas.transform;
            if (root.parent != null && root.parent.parent == null && root.parent.childCount == 1
                && root.parent.GetComponents<Component>().All(component => component is Transform))
                root = root.parent;
            if (root.parent == null) MoveUnder(root, ui);
        }
        foreach (EventSystem events in Find<EventSystem>(scene))
            if (events.transform.parent == null) MoveUnder(events.transform, ui);

        var choiceFields = new SerializedObject(players[0]);
        var craftingFields = new SerializedObject(player.GetComponent<CraftingUI>());
        ChoiceMenuView level = GetMenu(ui, RunMenuPrefabBuilder.LevelUpPrefabPath,
            choiceFields.FindProperty("_levelUpView").objectReferenceValue as ChoiceMenuView);
        ChoiceMenuView weapon = GetMenu(ui, RunMenuPrefabBuilder.WeaponSelectionPrefabPath,
            choiceFields.FindProperty("_weaponSelectionView").objectReferenceValue as ChoiceMenuView);
        CraftingMenuView crafting = GetMenu(ui, RunMenuPrefabBuilder.CraftingPrefabPath,
            craftingFields.FindProperty("_view").objectReferenceValue as CraftingMenuView);
        var content = AssetDatabase.LoadAssetAtPath<RunMenuContent>(RunMenuPrefabBuilder.ContentPath);
        Wire(players[0], ("_levelUpView", level), ("_weaponSelectionView", weapon), ("_content", content));
        Wire(player.GetComponent<CraftingUI>(), ("_view", crafting), ("_content", content));

        HudUiFactory.EnsureWhiteSpriteAsset();
        foreach (ReticleHud component in Find<ReticleHud>(scene)) Author(component, () => component.AuthorUi(ui));
        foreach (LevelUpStatFeedback component in Find<LevelUpStatFeedback>(scene)) Author(component, () => component.AuthorUi(ui));
        foreach (MaterialInventoryHUD component in Find<MaterialInventoryHUD>(scene)) Author(component, () => component.AuthorUi(ui));
        foreach (SurvivorHud component in Find<SurvivorHud>(scene))
        {
            if (component.transform.parent == null) MoveUnder(component.transform, ui);
            Author(component, () => component.AuthorUi(component.transform.IsChildOf(ui) ? component.transform : ui));
        }
        foreach (UIManager component in Find<UIManager>(scene))
        {
            if (component.transform.parent == null) MoveUnder(component.transform, ui);
            Author(component, () => component.AuthorUi(component.transform.IsChildOf(ui) ? component.transform : ui));
        }
        foreach (MaterialInventoryDisplayView component in Find<MaterialInventoryDisplayView>(scene)) Author(component, component.AuthorUi);
        foreach (PauseMenuUI component in Find<PauseMenuUI>(scene)) Author(component, component.AuthorUi);
        foreach (BossHealthBarHud component in Find<BossHealthBarHud>(scene)) Author(component, component.AuthorUi);
        foreach (OverheatObjectiveHud component in Find<OverheatObjectiveHud>(scene)) Author(component, component.AuthorUi);
        foreach (OffscreenObjectiveIndicators component in Find<OffscreenObjectiveIndicators>(scene)) Author(component, component.AuthorUi);
        foreach (PlayerCombatFeedback component in Find<PlayerCombatFeedback>(scene)) Author(component, component.AuthorUi);
        foreach (RunEndScreenUI component in Find<RunEndScreenUI>(scene)) Author(component, component.AuthorUi);
        foreach (LevelExitHud component in Find<LevelExitHud>(scene)) Author(component, () => component.AuthorUi(ui));
        AchievementUnlockToastView.AuthorUi(ui);

        foreach (Canvas canvas in ui.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvas.GetComponentInParent<Canvas>() != canvas) continue;
            Vector3 scale = canvas.transform.localScale;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
            {
                canvas.transform.localScale = Vector3.one;
                Record(canvas.transform);
            }
        }
        GameplayUiAuthoringAssets.PersistGraphics(ui);
        // Do not record layout-driven RectTransform values as new prefab overrides.
        // Authored controllers and explicitly moved/repaired transforms are recorded above.
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void MoveUnder(Transform child, Transform ui)
    {
        if (child == ui || child.IsChildOf(ui)) return;
        bool active = child.gameObject.activeInHierarchy;
        child.SetParent(ui, true);
        if (!active && child.gameObject.activeSelf) child.gameObject.SetActive(false);
        Record(child);
    }

    private static T GetMenu<T>(Transform ui, string path, T current) where T : Component
    {
        if (current != null && current.transform.IsChildOf(ui)) return current;
        var candidates = ui.GetComponentsInChildren<T>(true).Where(view =>
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject) == path
            || view.name == Path.GetFileNameWithoutExtension(path)).ToArray();
        if (candidates.Length > 1) throw new InvalidOperationException($"More than one {path} under UI; select the intended view before migrating.");
        if (candidates.Length == 1) return candidates[0];
        GameObject instance;
        if (current != null)
        {
            // Copy the actual scene hierarchy, including nested prefab overrides and
            // internal view references, before removing visuals from the player asset.
            instance = Object.Instantiate(current.gameObject, ui, false);
            instance.name = current.name;
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        }
        else
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ui);
            instance.SetActive(false);
        }
        return instance.GetComponent<T>();
    }

    private static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static void Author(Component component, Action action)
    {
        action();
        Record(component);
    }

    private static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void Wire(Object target, params (string Name, Object Value)[] fields)
    {
        if (target == null) throw new InvalidOperationException("Player presenter is missing.");
        var serialized = new SerializedObject(target);
        foreach (var field in fields) serialized.FindProperty(field.Name).objectReferenceValue = field.Value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Record(target);
    }

    private static void RemovePlayerMenuVisuals()
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Transform menus = prefab.transform.Find("RunMenus");
            if (menus == null) return;
            Object.DestroyImmediate(menus.gameObject);
            Wire(prefab.GetComponent<LevelUpChoiceUI>(), ("_levelUpView", null), ("_weaponSelectionView", null));
            Wire(prefab.GetComponent<CraftingUI>(), ("_view", null));
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }
}
#endif
