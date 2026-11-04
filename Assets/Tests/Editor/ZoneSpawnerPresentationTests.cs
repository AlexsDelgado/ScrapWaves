using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Contracts for the saved spawner assets and their presentation-only state transitions.</summary>
public sealed class ZoneSpawnerPresentationTests
{
    private const string ArtPath = "Assets/Art/Spawners/";

    [SetUp]
    public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TearDown]
    public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TestCase("Drone", 6, "aefb60095c090fc4ba790479da511bed")]
    [TestCase("Slime", 12, "e5ce45a08e4483447a365a6f4c98ff38")]
    [TestCase("Chaser", 8, "1780b9abf01edad45945412918dd7eda")]
    public void AuthoredPrefab_PreservesTriggerAndEnemyConfiguration_WithFourIndependentCornerLenses(
        string type, int spawnCount, string enemyGuid)
    {
        GameObject root = Instantiate(type);
        ZoneSpawner spawner = root.GetComponent<ZoneSpawner>();
        Assert.That(spawner, Is.Not.Null);
        var spawnData = new SerializedObject(spawner);
        Assert.That(spawnData.FindProperty("_spawnCount").intValue, Is.EqualTo(spawnCount));
        Assert.That(spawnData.FindProperty("_spawnAreaRadius").floatValue, Is.EqualTo(6f));
        Assert.That(spawnData.FindProperty("_rearmOnOverheat").boolValue, Is.True);
        Object enemy = spawnData.FindProperty("_enemyPrefab").objectReferenceValue;
        Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(enemy)), Is.EqualTo(enemyGuid));
        AssertTrigger(root);

        ZoneSpawnerPresentation view = root.GetComponent<ZoneSpawnerPresentation>();
        Assert.That(view, Is.Not.Null);
        var data = new SerializedObject(view);
        Assert.That(Reference<ZoneSpawner>(data, "_spawner"), Is.SameAs(spawner));
        GameObject hologram = Reference<GameObject>(data, "_hologramRoot");
        Transform pivot = Reference<Transform>(data, "_hologramPivot");
        Assert.That(hologram, Is.Not.Null);
        Assert.That(pivot, Is.Not.Null);
        Assert.That(hologram.transform.IsChildOf(root.transform), Is.True);
        Assert.That(hologram, Is.Not.SameAs(root));
        Assert.That(pivot.IsChildOf(hologram.transform), Is.True);
        Assert.That(pivot, Is.Not.SameAs(root.transform));

        Renderer[] lamps = Lamps(data);
        Assert.That(lamps.Length, Is.EqualTo(4));
        Assert.That(lamps.All(lamp => lamp != null), Is.True);
        Assert.That(lamps.Distinct().Count(), Is.EqualTo(4), "Each corner needs its own lens renderer.");
        Transform platform = root.transform.Find("FabricationPlatform");
        Assert.That(platform, Is.Not.Null);
        var corners = new HashSet<Vector2Int>();
        foreach (Renderer lamp in lamps)
        {
            Assert.That(lamp.transform.IsChildOf(platform), Is.True);
            Assert.That(lamp.transform.IsChildOf(hologram.transform), Is.False);
            Vector3 position = root.transform.InverseTransformPoint(lamp.transform.position);
            Assert.That(Mathf.Abs(position.x), Is.GreaterThan(1f));
            Assert.That(Mathf.Abs(position.z), Is.GreaterThan(1f));
            corners.Add(new Vector2Int(position.x < 0f ? -1 : 1, position.z < 0f ? -1 : 1));
        }
        Assert.That(corners.Count, Is.EqualTo(4), "The four lamps must cover the four platform corners.");
        AssertDedicatedLampMaterials(data);
        AssertVisualOnlyHologram(root, hologram);

        view.ApplyState(true, true);
        view.SetAnimationTime(0f);
        Bounds bounds = RendererBounds(hologram);
        Assert.That(bounds.size.x, Is.LessThanOrEqualTo(9.21f));
        Assert.That(bounds.size.y, Is.LessThanOrEqualTo(8.51f));
        Assert.That(bounds.size.z, Is.LessThanOrEqualTo(9.21f));
        Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.GreaterThan(8f),
            "Imported enemy units must be normalized into a readable preview, not left tiny.");
        Assert.That(Mathf.Abs(bounds.center.x - pivot.position.x), Is.LessThan(.02f));
        Assert.That(Mathf.Abs(bounds.center.z - pivot.position.z), Is.LessThan(.02f));
        Assert.That(Mathf.Abs(bounds.min.y - pivot.position.y), Is.LessThan(.02f));
    }

    [TestCase("Drone")]
    [TestCase("Slime")]
    [TestCase("Chaser")]
    public void ReadySpentRestored_ChangesOnlyHologramAndAllFourLamps(string type)
    {
        GameObject root = Instantiate(type);
        ZoneSpawnerPresentation view = root.GetComponent<ZoneSpawnerPresentation>();
        Assert.That(view, Is.Not.Null);
        var data = new SerializedObject(view);
        GameObject hologram = Reference<GameObject>(data, "_hologramRoot");
        Renderer[] lamps = Lamps(data);
        Assert.That(lamps.Length, Is.EqualTo(4));
        Material on = Reference<Material>(data, "_lampOnMaterial");
        Material off = Reference<Material>(data, "_lampOffMaterial");
        var platformMaterials = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !renderer.transform.IsChildOf(hologram.transform) && !lamps.Contains(renderer))
            .ToDictionary(renderer => renderer, renderer => renderer.sharedMaterials);
        var poses = CapturePoses(root);
        ZoneSpawner spawner = root.GetComponent<ZoneSpawner>();

        AssertState(true, true, on);
        AssertState(false, false, off);
        AssertState(true, true, on);
        AssertState(true, false, off); // Preparation/pause blocks use without consuming the preview.
        AssertState(true, true, on);

        void AssertState(bool armed, bool available, Material expected)
        {
            view.ApplyState(armed, available);
            Assert.That(hologram.activeSelf, Is.EqualTo(armed));
            Assert.That(root.activeInHierarchy && view.isActiveAndEnabled && spawner.isActiveAndEnabled, Is.True,
                "Hiding a consumed preview must retain its controller and refresh subscription.");
            Assert.That(spawner.IsArmed, Is.True, "Presentation sampling must not modify gameplay state.");
            foreach (Renderer lamp in lamps)
            {
                Assert.That(lamp.gameObject.activeInHierarchy, Is.True, "Spent hardware stays visible.");
                Assert.That(lamp.sharedMaterial, Is.SameAs(expected));
            }
            foreach (var pair in platformMaterials)
                Assert.That(pair.Key.sharedMaterials, Is.EqualTo(pair.Value), "Lamp changes must not recolor the chassis.");
            AssertPosesUnchanged(poses, null);
            AssertTrigger(root);
        }
    }

    [TestCase("Drone")]
    [TestCase("Slime")]
    [TestCase("Chaser")]
    public void SampledMotion_BobsAndTurnsUprightPivot_WithoutMovingTriggerOrAccumulatingDrift(string type)
    {
        GameObject root = Instantiate(type);
        root.transform.SetPositionAndRotation(new Vector3(12f, 4f, -7f), Quaternion.Euler(0f, 31f, 0f));
        ZoneSpawnerPresentation view = root.GetComponent<ZoneSpawnerPresentation>();
        Assert.That(view, Is.Not.Null);
        var data = new SerializedObject(view);
        Transform pivot = Reference<Transform>(data, "_hologramPivot");
        view.ApplyState(true, true);
        view.SetAnimationTime(0f);
        var poses = CapturePoses(root);
        Vector3 restPosition = pivot.position;
        Quaternion restRotation = pivot.rotation;
        float sampleTime = data.FindProperty("_bobPeriod").floatValue * .25f;

        view.SetAnimationTime(sampleTime);
        Vector3 sampledPosition = pivot.position;
        Quaternion sampledRotation = pivot.rotation;
        Assert.That(sampledPosition.y, Is.GreaterThan(restPosition.y + .01f), "The preview must visibly float.");
        Assert.That(Mathf.Abs(sampledPosition.x - restPosition.x), Is.LessThan(.0001f));
        Assert.That(Mathf.Abs(sampledPosition.z - restPosition.z), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(sampledRotation, restRotation), Is.GreaterThan(1f));
        Assert.That(Vector3.Dot(pivot.up, Vector3.up), Is.GreaterThan(.9999f), "Import-axis correction belongs below the spinning pivot.");
        AssertPosesUnchanged(poses, pivot);
        AssertTrigger(root);

        view.SetAnimationTime(10000f);
        view.SetAnimationTime(sampleTime);
        AssertVector(pivot.position, sampledPosition);
        Assert.That(Quaternion.Angle(pivot.rotation, sampledRotation), Is.LessThan(.001f));
        foreach (float restSample in new[] { 0f, float.NaN, float.PositiveInfinity })
        {
            view.SetAnimationTime(sampleTime);
            view.SetAnimationTime(restSample);
            AssertVector(pivot.position, restPosition);
            Assert.That(Quaternion.Angle(pivot.rotation, restRotation), Is.LessThan(.001f));
            AssertPosesUnchanged(poses, null);
        }
    }

    [Test]
    public void GameplayInstances_KeepLinkedAssets_AndNormalizeNonuniformScaleAboveTheUprightPivot()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity", OpenSceneMode.Single);
        ZoneSpawner[] zones = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ZoneSpawner>(true)).ToArray();
        Assert.That(zones.Length, Is.EqualTo(3));
        foreach (ZoneSpawner zone in zones)
        {
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(zone), Is.True);
            ZoneSpawnerPresentation view = zone.GetComponent<ZoneSpawnerPresentation>();
            Assert.That(view, Is.Not.Null, zone.name);
            var data = new SerializedObject(view);
            GameObject hologram = Reference<GameObject>(data, "_hologramRoot");
            Transform pivot = Reference<Transform>(data, "_hologramPivot");
            AssertVisualOnlyHologram(zone.gameObject, hologram);
            Vector3 scale = pivot.lossyScale;
            Assert.That(Mathf.Abs(scale.x - 1f), Is.LessThan(.001f), zone.name);
            Assert.That(Mathf.Abs(scale.y - 1f), Is.LessThan(.001f), zone.name);
            Assert.That(Mathf.Abs(scale.z - 1f), Is.LessThan(.001f), zone.name);
            Assert.That(Vector3.Dot(pivot.up, Vector3.up), Is.GreaterThan(.9999f), zone.name);
        }
    }

    private static GameObject Instantiate(string type)
    {
        string path = "Assets/Prefabs/ZoneSpawner_" + type + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    private static void AssertTrigger(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        Assert.That(colliders.Length, Is.EqualTo(1), "The replacement platform and preview must not add physics obstacles.");
        SphereCollider trigger = root.GetComponent<SphereCollider>();
        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.enabled && trigger.isTrigger, Is.True);
        Assert.That(trigger.radius, Is.EqualTo(6f));
        AssertVector(trigger.center, new Vector3(0f, 1f, 0f));
    }

    private static void AssertDedicatedLampMaterials(SerializedObject data)
    {
        Material on = Reference<Material>(data, "_lampOnMaterial");
        Material off = Reference<Material>(data, "_lampOffMaterial");
        Assert.That(on, Is.Not.Null);
        Assert.That(off, Is.Not.Null);
        Assert.That(on, Is.Not.SameAs(off));
        Assert.That(AssetDatabase.GetAssetPath(on), Does.StartWith(ArtPath));
        Assert.That(AssetDatabase.GetAssetPath(off), Does.StartWith(ArtPath));
        Assert.That(on.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(.5f));
        Assert.That(off.GetColor("_EmissionColor").maxColorComponent, Is.LessThan(.01f));
    }

    private static void AssertVisualOnlyHologram(GameObject root, GameObject hologram)
    {
        Assert.That(root.transform.Find("ZoneMarker"), Is.Null, "The old blue disc must be replaced.");
        Assert.That(hologram.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        Assert.That(hologram.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(hologram.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(hologram.GetComponentsInChildren<Animator>(true), Is.Empty);
        Renderer[] previews = hologram.GetComponentsInChildren<Renderer>(true);
        Assert.That(previews, Is.Not.Empty);
        foreach (Renderer renderer in previews)
        {
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(material), Does.StartWith(ArtPath));
                Assert.That(material.shader.name, Is.EqualTo("ScrapWaves/Spawners/Hologram"));
            }
        }
        Transform platform = root.transform.Find("FabricationPlatform");
        Assert.That(platform, Is.Not.Null);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            Assert.That(renderer.transform.IsChildOf(hologram.transform) || renderer.transform.IsChildOf(platform), Is.True,
                "No legacy solid enemy preview may remain outside the authored hologram: " + renderer.name);
    }

    private static Renderer[] Lamps(SerializedObject data)
    {
        SerializedProperty array = data.FindProperty("_cornerLamps");
        var result = new Renderer[array.arraySize];
        for (int i = 0; i < result.Length; i++) result[i] = array.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
        return result;
    }

    private static T Reference<T>(SerializedObject data, string property) where T : Object =>
        data.FindProperty(property).objectReferenceValue as T;

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> CapturePoses(GameObject root) =>
        root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));

    private static void AssertPosesUnchanged(
        Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> poses, Transform except)
    {
        foreach (var pair in poses)
        {
            if (pair.Key == except) continue;
            AssertVector(pair.Key.localPosition, pair.Value.position);
            Assert.That(Quaternion.Angle(pair.Key.localRotation, pair.Value.rotation), Is.LessThan(.001f), pair.Key.name);
            AssertVector(pair.Key.localScale, pair.Value.scale);
        }
    }

    private static void AssertVector(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
}
