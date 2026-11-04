using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Validates saved station content in isolated preview scenes; never authors or saves assets.</summary>
public sealed class CraftingStationAuthoringTests
{
    private const string PrefabPath = "Assets/Prefabs/Props/CraftingStation.prefab";
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private static readonly string[] TuningFields = { "_initialBreakChancePercent", "_breakChanceIncrementPercent" };
    private Scene _scene;

    [OneTimeSetUp]
    public void OpenSavedSceneInPreview()
    {
        Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
        _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
    }

    [OneTimeTearDown]
    public void ClosePreview()
    {
        if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
    }

    [Test]
    public void CompletePrefab_HasEditableBalanceModelsCollisionAndDisabledPrompt()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, "A designer must be able to drop the complete station prefab.");
        CraftingStation station = prefab.GetComponent<CraftingStation>();
        Assert.That(station, Is.Not.Null);
        Assert.That(station.ParticipatesInRotation, Is.True);
        Assert.That(station.InitialBreakChancePercent, Is.Zero);
        Assert.That(station.BreakChanceIncrementPercent, Is.EqualTo(5f));
        AssertPresentation(station);
        AssertNoMissingReferences(prefab);
    }

    [Test]
    public void SavedNormalStations_PreservePlacementAndInheritPrefabBalance()
    {
        CraftingStation[] stations = All<CraftingStation>().Where(station => station.ParticipatesInRotation).ToArray();
        Assert.That(stations, Has.Length.EqualTo(2), "Retain both original gameplay placements.");
        AssertPlacement(stations.Single(station => station.name == "CraftingStation"), 197.46f);
        AssertPlacement(stations.Single(station => station.name == "CraftingStation (1)"), -197f);
        foreach (CraftingStation station in stations)
        {
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(station), Is.EqualTo(PrefabPath),
                station.name + " must receive future designer prefab changes.");
            using var serialized = new SerializedObject(station);
            foreach (string field in TuningFields)
                Assert.That(serialized.FindProperty(field).prefabOverride, Is.False,
                    station.name + "." + field + " must inherit the prefab, without a redundant migration override.");

            CraftingStation source = PrefabUtility.GetCorrespondingObjectFromSource(station);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(
                PrefabUtility.GetOutermostPrefabInstanceRoot(station)) ?? Array.Empty<PropertyModification>();
            Assert.That(modifications.Any(change => change.target == source && TuningFields.Contains(change.propertyPath)),
                Is.False, "Explicit balance overrides would prevent prefab tuning from propagating to " + station.name);
            AssertPresentation(station);
            AssertNoMissingReferences(station.gameObject);
        }
    }

    [Test]
    public void SavedTutorial_KeepsItsOriginalBoundStationOutsideThePool()
    {
        TutorialDirector director = All<TutorialDirector>().Single();
        CraftingStation station = Reference<CraftingStation>(director, "_station");
        GameObject world = Reference<GameObject>(director, "_world");
        Assert.That(station, Is.SameAs(All<CraftingStation>().Single(candidate => candidate.name == "Tutorial tinkering table")));
        Assert.That(station.transform.IsChildOf(world.transform), Is.True);
        Assert.That(GlobalObjectId.GetGlobalObjectIdSlow(station).targetObjectId, Is.EqualTo(2107452995UL),
            "Migration must retain the original tutorial station component and its serialized references.");
        Assert.That(station.ParticipatesInRotation, Is.False);
        Assert.That(station.InteractionRadius, Is.EqualTo(2.5f));
        Assert.That(Reference<Transform>(station, "_interactionPoint"), Is.SameAs(station.transform));
        Assert.That(Reference<CraftingUI>(station, "_craftingUi"), Is.SameAs(Reference<CraftingUI>(director, "_craftingUi")));
        Assert.That(Reference<WeaponCraftingService>(station, "_craftingService"), Is.SameAs(Reference<WeaponCraftingService>(director, "_crafting")));
        AssertPresentation(station);
        AssertNoMissingReferences(station.gameObject);
    }

    [Test]
    public void DroppedPrefabs_RegisterThroughTheirLifecycleAndRotateWithoutManualPoolWiring()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Scene preview = EditorSceneManager.NewPreviewScene();
        CraftingStation first = null, second = null;
        try
        {
            first = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, preview)).GetComponent<CraftingStation>();
            second = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, preview)).GetComponent<CraftingStation>();
            CraftingStationRotation.SetRandomSource(preview, new FirstChoiceRandom());
            // EditMode does not dispatch ordinary MonoBehaviour callbacks. Exercise the
            // same lifecycle entry points Unity dispatches when these prefabs enter play.
            Lifecycle(first, "OnEnable");
            Lifecycle(second, "OnEnable");
            Lifecycle(first, "Start");
            Assert.That(CraftingStationRotation.GetActiveStation(preview), Is.SameAs(first));
            Assert.That(first.IsAvailable, Is.True);
            Assert.That(second.IsAvailable, Is.False);
            Assert.That(first.CompleteTransaction(), Is.False, "The first purchase at zero percent is safe.");
            Assert.That(first.CompleteTransaction(), Is.True, "A deterministic zero roll breaks at five percent.");
            Assert.That(CraftingStationRotation.GetActiveStation(preview), Is.SameAs(second));
            Assert.That(first.IsAvailable, Is.False);
            Assert.That(second.IsAvailable, Is.True);
            Assert.That(first.gameObject.activeSelf && second.gameObject.activeSelf, Is.True);
            Assert.That(Reference<GameObject>(first, "_brokenVisual").activeSelf, Is.True);
            Assert.That(Reference<GameObject>(second, "_activeVisual").activeSelf, Is.True);
        }
        finally
        {
            if (first != null) Lifecycle(first, "OnDisable");
            if (second != null) Lifecycle(second, "OnDisable");
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void AssertPlacement(CraftingStation station, float z)
    {
        Assert.That(station.transform.parent, Is.Null, "Keep the existing station root.");
        Assert.That(Vector3.Distance(station.transform.position, new Vector3(.09f, 155.05f, z)), Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(station.transform.rotation, Quaternion.identity), Is.LessThan(.001f));
        Assert.That(Vector3.Distance(station.transform.localScale, Vector3.one * .5f), Is.LessThan(.001f));
        Assert.That(station.InteractionRadius, Is.EqualTo(2.5f));
        Assert.That(Reference<Transform>(station, "_interactionPoint"), Is.SameAs(station.transform));
    }

    private static void AssertPresentation(CraftingStation station)
    {
        GameObject active = Reference<GameObject>(station, "_activeVisual");
        GameObject broken = Reference<GameObject>(station, "_brokenVisual");
        Assert.That(active, Is.Not.SameAs(broken));
        Assert.That(active.transform.IsChildOf(station.transform) && broken.transform.IsChildOf(station.transform), Is.True);
        Assert.That(active, Is.Not.SameAs(station.gameObject));
        Assert.That(broken, Is.Not.SameAs(station.gameObject));
        Assert.That(active.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
        Assert.That(broken.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
        Bounds intactBounds = MeshBoundsInSpace(active, station.transform);
        Bounds brokenBounds = MeshBoundsInSpace(broken, station.transform);
        Assert.That(brokenBounds.size.x / intactBounds.size.x, Is.InRange(.9f, 1.1f),
            station.name + " broken workbench width must preserve its imported model scale.");
        Assert.That(brokenBounds.size.z / intactBounds.size.z, Is.InRange(.9f, 1.1f),
            station.name + " broken workbench depth must match the intact footprint.");
        Assert.That(brokenBounds.size.y / intactBounds.size.y, Is.InRange(.6f, 1.1f),
            station.name + " broken workbench may lose height, but cannot collapse into a tiny model.");
        Assert.That(Mathf.Abs(brokenBounds.min.y - intactBounds.min.y), Is.LessThanOrEqualTo(intactBounds.size.y * .05f),
            station.name + " broken workbench feet must remain on the intact model's ground plane.");
        Assert.That(Mathf.Abs(brokenBounds.center.x - intactBounds.center.x), Is.LessThanOrEqualTo(intactBounds.size.x * .1f),
            station.name + " broken workbench must remain centered on its station.");
        Assert.That(Mathf.Abs(brokenBounds.center.z - intactBounds.center.z), Is.LessThanOrEqualTo(intactBounds.size.z * .1f),
            station.name + " broken workbench must remain centered on its station.");
        Assert.That(station.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled && !collider.isTrigger
            && !collider.transform.IsChildOf(active.transform) && !collider.transform.IsChildOf(broken.transform)), Is.True,
            "The station retains solid collision regardless of which model is shown.");

        InteractPrompt[] prompts = station.GetComponentsInChildren<InteractPrompt>(true);
        Assert.That(prompts, Has.Length.EqualTo(1));
        InteractPrompt prompt = prompts[0];
        Transform visual = Reference<Transform>(prompt, "_visual");
        GameObject available = Reference<GameObject>(prompt, "_availableVisual");
        GameObject unavailable = Reference<GameObject>(prompt, "_unavailableVisual");
        Assert.That(available, Is.Not.SameAs(unavailable));
        Assert.That(available.transform.IsChildOf(visual) && unavailable.transform.IsChildOf(visual), Is.True);
        Assert.That(available.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
        Transform cross = unavailable.GetComponentsInChildren<Transform>(true).SingleOrDefault(child => child.name == "Unavailable Cross");
        Assert.That(cross, Is.Not.Null, "Unavailable feedback contains an authored cross.");
        Assert.That(cross.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(2));
        Bounds keyBounds = MeshBoundsInSpace(available, visual);
        Bounds crossBounds = MeshBoundsInSpace(cross.gameObject, visual);
        Assert.That(crossBounds.size.x, Is.LessThan(keyBounds.size.x * .65f),
            "The cross must stay a small badge; inherited FBX scale cannot enlarge it over the E key.");
        Renderer[] greyKey = unavailable.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !renderer.transform.IsChildOf(cross)).ToArray();
        Assert.That(greyKey, Is.Not.Empty);
        foreach (Material material in greyKey.SelectMany(renderer => renderer.sharedMaterials))
        {
            Assert.That(material, Is.Not.Null);
            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            Assert.That(Mathf.Abs(color.r - color.g) + Mathf.Abs(color.g - color.b), Is.LessThan(.01f),
                "Unavailable E material must be grey: " + material.name);
        }
    }

    private static Bounds MeshBoundsInSpace(GameObject root, Transform space)
    {
        // Imported/disabled models do not need to render before this can validate their geometry.
        // Renderer.bounds can be stale in a preview scene; mesh bounds and authored transforms cannot.
        Bounds result = default;
        bool hasMesh = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Assert.That(filter.sharedMesh, Is.Not.Null, filter.name + " must reference imported geometry.");
            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 signs = new Vector3((corner & 1) == 0 ? -1 : 1,
                    (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = toSpace.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, signs));
                if (!hasMesh) { result = new Bounds(point, Vector3.zero); hasMesh = true; }
                else result.Encapsulate(point);
            }
        }
        Assert.That(hasMesh, Is.True, root.name + " must contain saved mesh geometry.");
        Assert.That(result.size.x, Is.GreaterThan(0f), root.name + " has a collapsed width.");
        Assert.That(result.size.y, Is.GreaterThan(0f), root.name + " has a collapsed height.");
        Assert.That(result.size.z, Is.GreaterThan(0f), root.name + " has a collapsed depth.");
        return result;
    }

    private static void AssertNoMissingReferences(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            Assert.That(component, Is.Not.Null, root.name + " contains a missing script.");
            using var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Assert.That(property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0, Is.False,
                        component.name + "." + property.propertyPath + " contains a missing reference.");
        }
    }

    private T[] All<T>() where T : Component => _scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static T Reference<T>(Object target, string field) where T : Object
    {
        using var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        Assert.That(property, Is.Not.Null, field + " is an authored field.");
        T value = property.objectReferenceValue as T;
        Assert.That(value, Is.Not.Null, target.name + "." + field + " must be saved.");
        return value;
    }

    private static void Lifecycle(CraftingStation station, string method) => typeof(CraftingStation)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(station, null);

    private sealed class FirstChoiceRandom : CraftingStationRotation.IRandomSource
    {
        public double NextDouble() => 0d;
        public int NextIndex(int exclusiveMax) => 0;
    }
}
