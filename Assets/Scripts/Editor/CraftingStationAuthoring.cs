#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit migration of existing authored benches; never runs on import or in gameplay.</summary>
public static class CraftingStationAuthoring
{
    public const string PrefabPath = "Assets/Prefabs/Props/CraftingStation.prefab";
    public const string BrokenModelPath = "Assets/Art/Level/WorkbenchBroken.fbx";
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";

    [MenuItem("ScrapWaves/Crafting/Author Rotation Prefab And Gameplay Stations")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author stations outside Play Mode.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        ImportBrokenModel();
        InteractPromptBuilder.BuildPrefab();
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        CraftingStation[] stations = Find<CraftingStation>(scene);
        TutorialDirector tutorial = Find<TutorialDirector>(scene).Single();
        var tutorialSettings = new SerializedObject(tutorial);
        var tutorialStation = tutorialSettings.FindProperty("_station").objectReferenceValue as CraftingStation;
        if (tutorialStation == null) throw new InvalidOperationException("Tutorial station reference is missing.");

        // Read the specific legacy placement components, then retain those same objects and references.
        foreach (CraftingStation station in stations)
        {
            EnsureVisuals(station);
            // Tutorial/legacy benches may contain an unpacked prompt; retain that instance
            // and author the same presentation references without replacing its placement.
            foreach (InteractPrompt prompt in station.GetComponentsInChildren<InteractPrompt>(true))
                InteractPromptBuilder.UpgradeStateVisuals(prompt);
            if (station == tutorialStation)
            {
                var settings = new SerializedObject(station);
                settings.FindProperty("_participatesInRotation").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            CraftingStation source = stations.First(station => station != tutorialStation);
            GameObject template = Object.Instantiate(source.gameObject);
            try
            {
                template.name = "CraftingStation";
                template.transform.position = Vector3.zero;
                template.transform.rotation = Quaternion.identity;
                var settings = new SerializedObject(template.GetComponent<CraftingStation>());
                foreach (string field in new[] { "_craftingUi", "_craftingService", "_inventory", "_levelUpChoiceUi" })
                    settings.FindProperty(field).objectReferenceValue = null;
                settings.FindProperty("_participatesInRotation").boolValue = true;
                settings.FindProperty("_initialBreakChancePercent").floatValue = 0f;
                settings.FindProperty("_breakChanceIncrementPercent").floatValue = 5f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
            }
            finally { Object.DestroyImmediate(template); }
        }

        foreach (CraftingStation station in stations.Where(station => station != tutorialStation))
        {
            Vector3 position = station.transform.position;
            Quaternion rotation = station.transform.rotation;
            Vector3 scale = station.transform.localScale;
            if (!PrefabUtility.IsPartOfPrefabInstance(station.gameObject))
            {
                PrefabUtility.ConvertToPrefabInstance(station.gameObject, prefab,
                    new ConvertToPrefabInstanceSettings
                    {
                        objectMatchMode = ObjectMatchMode.ByHierarchy,
                        componentsNotMatchedBecomesOverride = true,
                        gameObjectsNotMatchedBecomesOverride = true,
                        recordPropertyOverridesOfMatches = true,
                        changeRootNameToAssetName = false
                    }, InteractionMode.AutomatedAction);
            }
            if (station.transform.position != position || station.transform.rotation != rotation || station.transform.localScale != scale)
                throw new InvalidOperationException("Station conversion changed an authored placement.");
            // Normal stations inherit the new prefab's tuning; subsequent authoring does not reset overrides.
            PrefabUtility.RecordPrefabInstancePropertyModifications(station);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        foreach (CompactorDoorPresentation presentation in Find<CompactorDoorPresentation>(scene)) presentation.Dispose();
        Debug.Log($"CRAFTING_STATIONS_AUTHORED: {stations.Length - 1} rotating, 1 tutorial-exempt; prefab={PrefabPath}");
    }

    private static void EnsureVisuals(CraftingStation station)
    {
        var settings = new SerializedObject(station);
        Transform active = settings.FindProperty("_activeVisual").objectReferenceValue is GameObject authored
            ? authored.transform : station.transform.Find("Workbench");
        if (active == null) throw new InvalidOperationException($"{station.name} has no authored Workbench model.");
        Transform broken = settings.FindProperty("_brokenVisual").objectReferenceValue is GameObject damaged
            ? damaged.transform : station.transform.Find("Workbench Broken");
        if (broken == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BrokenModelPath);
            broken = new GameObject("Workbench Broken").transform;
            SceneManager.MoveGameObjectToScene(broken.gameObject, station.gameObject.scene);
            broken.SetParent(station.transform, false);
            broken.localPosition = active.localPosition;
            broken.localRotation = active.localRotation;
            broken.localScale = active.localScale;
            var imported = (GameObject)PrefabUtility.InstantiatePrefab(model, station.gameObject.scene);
            imported.transform.SetParent(broken, false);
            imported.name = "Broken Model";
            broken.gameObject.SetActive(false);
        }
        settings.FindProperty("_activeVisual").objectReferenceValue = active.gameObject;
        settings.FindProperty("_brokenVisual").objectReferenceValue = broken.gameObject;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(station);
    }

    private static void ImportBrokenModel()
    {
        if (!File.Exists(BrokenModelPath)) throw new FileNotFoundException("Export the broken workbench model first.", BrokenModelPath);
        AssetDatabase.ImportAsset(BrokenModelPath, ImportAssetOptions.ForceSynchronousImport);
        var original = AssetImporter.GetAtPath("Assets/Art/Level/Workbench.fbx") as ModelImporter;
        var importer = AssetImporter.GetAtPath(BrokenModelPath) as ModelImporter;
        importer.globalScale = original.globalScale;
        importer.useFileScale = original.useFileScale;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        const string folder = "Assets/Art/Level/WorkbenchBrokenMaterials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Art/Level", "WorkbenchBrokenMaterials");
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(BrokenModelPath).OfType<Material>())
        {
            string safeName = string.Concat(embedded.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = $"{folder}/{safeName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = embedded.name };
                Color color = embedded.HasProperty("_BaseColor") ? embedded.GetColor("_BaseColor") : embedded.color;
                material.SetColor("_BaseColor", color);
                string lower = embedded.name.ToLowerInvariant();
                material.SetFloat("_Metallic", lower.Contains("goma") || lower.Contains("cable") || lower.Contains("rubber") ? 0f : .65f);
                material.SetFloat("_Smoothness", lower.Contains("ox") || lower.Contains("rust") ? .1f : .3f);
                AssetDatabase.CreateAsset(material, path);
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded), material);
        }
        importer.SaveAndReimport();
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

}
#endif
