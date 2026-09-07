using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Replaces gameplay placeholders while retaining their serialized gameplay settings.</summary>
public static class GameplayModelsSetupEditor
{
    public const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    public const string WorkbenchPath = "Assets/Art/Level/Workbench.fbx";

    [MenuItem("Tools/ScrapWaves/Replace Gameplay Placeholder Models")]
    public static void Setup()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var oldDoor = Find<ExitDoor>(scene).Single();
        if (oldDoor.GetComponent<CompactorDoorPresentation>() == null)
        {
            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(CompactorEscapeSetupEditor.PrefabPath), scene);
            replacement.name = "ExitDoor";
            var door = replacement.GetComponent<ExitDoor>();
            var interaction = new SerializedObject(door).FindProperty("_interactionPoint").objectReferenceValue;
            EditorUtility.CopySerialized(oldDoor, door);
            var settings = new SerializedObject(door);
            settings.FindProperty("_interactionPoint").objectReferenceValue = interaction;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(door);
            // Match the old landmark's width with uniform scaling so the rig is not distorted.
            float width = oldDoor.GetComponent<Renderer>().bounds.size.x;
            replacement.transform.SetPositionAndRotation(
                new Vector3(oldDoor.transform.position.x, 0f, oldDoor.transform.position.z), oldDoor.transform.rotation);
            replacement.transform.localScale = Vector3.one * (width / 3.68f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.transform);
            RemapReferences(scene, oldDoor, door);
            RemapReferences(scene, oldDoor.transform, replacement.transform);
            RemapReferences(scene, oldDoor.gameObject, replacement);
            Object.DestroyImmediate(oldDoor.gameObject);
        }

        var station = Find<CraftingStation>(scene).Single();
        if (station.transform.Find("Workbench") == null)
        {
            var placeholder = station.transform.Find("Cube");
            if (placeholder == null) throw new InvalidOperationException("Expected crafting placeholder Cube.");
            float width = placeholder.GetComponent<Renderer>().bounds.size.x;
            Object.DestroyImmediate(placeholder.gameObject);
            // Preserve the station and its interaction point, removing only placeholder geometry.
            station.transform.localScale = Vector3.one;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WorkbenchPath), scene);
            model.name = "Workbench";
            model.transform.SetParent(station.transform, false);
            Bounds bounds = BoundsOf(model);
            model.transform.localScale *= width / bounds.size.x;
            bounds = BoundsOf(model);
            model.transform.position += new Vector3(station.transform.position.x - bounds.center.x,
                station.transform.position.y - bounds.min.y, station.transform.position.z - bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            bounds = BoundsOf(model);
            var collision = new GameObject("Workbench Collision");
            collision.transform.SetParent(station.transform, false);
            var box = collision.AddComponent<BoxCollider>();
            box.center = station.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            Debug.Log($"Workbench bounds: {bounds}");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        foreach (var presentation in Find<CompactorDoorPresentation>(scene)) presentation.Dispose();
        Debug.Log("GAMEPLAY_MODELS_SETUP_COMPLETE");
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static Bounds BoundsOf(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    public static void RenderPreviews()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var door = Find<ExitDoor>(scene).Single();
        var animator = door.GetComponentInChildren<Animator>();
        var clip = CompactorEscapeSetupEditor.OpeningClip;
        clip.SampleAnimation(animator.gameObject, 0f);
        Render(door.gameObject, "door-closed");
        clip.SampleAnimation(animator.gameObject, clip.length);
        var suction = door.transform.Find("Black Suction Opening").GetComponent<Renderer>();
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_Activity", 1f);
        suction.SetPropertyBlock(properties);
        Render(door.gameObject, "door-open");
        Render(Find<CraftingStation>(scene).Single().gameObject, "workbench");
        foreach (var presentation in Find<CompactorDoorPresentation>(scene)) presentation.Dispose();
    }

    private static void Render(GameObject subject, string name)
    {
        var bounds = BoundsOf(subject);
        var cameraObject = new GameObject("Model preview");
        var camera = cameraObject.AddComponent<Camera>();
        float size = bounds.size.magnitude;
        camera.transform.position = bounds.center + new Vector3(.55f, .3f, 1f).normalized * size * 1.4f;
        camera.transform.LookAt(bounds.center);
        camera.nearClipPlane = .05f;
        camera.farClipPlane = size * 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .09f, .12f);
        var target = new RenderTexture(1000, 1000, 24);
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
        image.Apply();
        System.IO.Directory.CreateDirectory("tmp/gameplay-models");
        System.IO.File.WriteAllBytes("tmp/gameplay-models/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
    }

    private static void RemapReferences(Scene scene, Object source, Object target)
    {
        foreach (var component in Find<MonoBehaviour>(scene).Where(c => c != null))
        {
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (!property.propertyPath.StartsWith("m_", StringComparison.Ordinal)
                    && property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == source)
                    property.objectReferenceValue = target;
            if (serialized.ApplyModifiedPropertiesWithoutUndo())
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
