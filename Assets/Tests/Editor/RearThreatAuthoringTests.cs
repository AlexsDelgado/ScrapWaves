using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RearThreatAuthoringTests
{
    private static readonly MethodInfo PresenterLateUpdate = typeof(RearThreatPresenter)
        .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void CreateMissingAssets_PreservesExistingAuthoredAssets()
    {
        string[] paths =
        {
            RearThreatIndicatorAuthoring.PrefabPath,
            RearThreatIndicatorAuthoring.FillMaterialPath,
            RearThreatIndicatorAuthoring.OutlineMaterialPath,
            RearThreatIndicatorAuthoring.FillMeshPath,
            RearThreatIndicatorAuthoring.OutlineMeshPath
        };
        foreach (string path in paths) Assert.That(File.Exists(path), Is.True, path);
        byte[][] contents = paths.Select(File.ReadAllBytes).ToArray();
        RearThreatIndicatorAuthoring.CreateMissingAssets();
        for (int i = 0; i < paths.Length; i++)
            CollectionAssert.AreEqual(contents[i], File.ReadAllBytes(paths[i]), paths[i]);
    }

    [Test]
    public void AuthorUi_PreservesExistingHierarchyAndManualVisualChanges()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ui = new GameObject("UI");
        var player = new GameObject("Player");
        Material customMaterial = null;
        try
        {
            ui.transform.position = new Vector3(250f, 12f, -31f);
            player.transform.position = new Vector3(3f, 2f, 5f);
            RearThreatPresenter presenter = RearThreatIndicatorAuthoring.AuthorUi(ui.transform, player.transform);
            presenter.RestoreAuthoredMeshes();
            Mesh fillMesh = presenter.Fill.sharedMesh;
            Mesh outlineMesh = presenter.Outline.sharedMesh;
            MeshRenderer renderer = presenter.Fill.GetComponent<MeshRenderer>();
            customMaterial = new Material(renderer.sharedMaterial);
            renderer.sharedMaterial = customMaterial;
            presenter.Fill.transform.localScale = new Vector3(1.2f, 1f, 0.9f);
            presenter.Fill.gameObject.name = "Custom Fill";
            int[] hierarchy = ui.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()).ToArray();

            RearThreatPresenter second = RearThreatIndicatorAuthoring.AuthorUi(ui.transform, player.transform);
            second.RestoreAuthoredMeshes();

            Assert.That(second, Is.SameAs(presenter));
            CollectionAssert.AreEqual(hierarchy, ui.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()).ToArray());
            Assert.That(second.Player, Is.SameAs(player.transform));
            Assert.That(second.transform.position, Is.EqualTo(player.transform.position));
            Assert.That(second.Fill.gameObject.name, Is.EqualTo("Custom Fill"));
            Assert.That(second.Fill.transform.localScale, Is.EqualTo(new Vector3(1.2f, 1f, 0.9f)));
            Assert.That(renderer.sharedMaterial, Is.SameAs(customMaterial));
            Assert.That(second.Fill.sharedMesh, Is.SameAs(fillMesh));
            Assert.That(second.Outline.sharedMesh, Is.SameAs(outlineMesh));
        }
        finally
        {
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(player);
            if (customMaterial != null) Object.DestroyImmediate(customMaterial);
        }
    }

    [Test]
    public void EditorPreview_FollowsMovedAndRotatedPlayerWithoutEditingPresenter()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ui = new GameObject("UI");
        var player = new GameObject("Player");
        try
        {
            ui.transform.position = new Vector3(250f, 12f, -31f);
            player.transform.position = new Vector3(3f, 2f, 5f);
            RearThreatPresenter presenter = RearThreatIndicatorAuthoring.AuthorUi(ui.transform, player.transform);
            var presenterFields = new SerializedObject(presenter);
            presenterFields.FindProperty("_hipOffset").vector3Value = Vector3.up * 0.42f;
            presenterFields.ApplyModifiedPropertiesWithoutUndo();
            presenter.RefreshPreview();
            Mesh mesh = presenter.Fill.sharedMesh;

            player.transform.SetPositionAndRotation(new Vector3(7f, 3.5f, -2f), Quaternion.Euler(12f, 113f, 9f));
            Assert.That(PresenterLateUpdate, Is.Not.Null);
            PresenterLateUpdate.Invoke(presenter, null);

            Assert.That(Vector3.Distance(presenter.transform.position, player.transform.position + Vector3.up * 0.42f), Is.LessThan(0.0001f));
            Vector3 horizontalForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            Assert.That(Vector3.Angle(presenter.transform.forward, horizontalForward), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(presenter.transform.up, Vector3.up), Is.LessThan(0.01f));
            Assert.That(presenter.Fill.sharedMesh, Is.SameAs(mesh));
        }
        finally
        {
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void EditorPreview_UpdatesArcAfterSensorInspectorChangesWithoutEditingPresenter()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ui = new GameObject("UI");
        var player = new GameObject("Player");
        try
        {
            RearThreatPresenter presenter = RearThreatIndicatorAuthoring.AuthorUi(ui.transform, player.transform);
            presenter.RefreshPreview();
            Mesh mesh = presenter.Fill.sharedMesh;
            float previousHalfArc = Vector3.Angle(Vector3.back, mesh.vertices[0]);
            var sensor = new SerializedObject(presenter.Sensor);
            sensor.FindProperty("_rearArcDegrees").floatValue = 120f;
            sensor.FindProperty("_arcExitMargin").floatValue = 3f;
            sensor.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(PresenterLateUpdate, Is.Not.Null);
            PresenterLateUpdate.Invoke(presenter, null);

            Assert.That(presenter.Fill.sharedMesh, Is.SameAs(mesh));
            Vector3[] vertices = mesh.vertices;
            float updatedHalfArc = Vector3.Angle(Vector3.back, vertices[0]);
            Assert.That(updatedHalfArc, Is.EqualTo(63f).Within(0.01f));
            Assert.That(updatedHalfArc, Is.Not.EqualTo(previousHalfArc).Within(0.01f));
            Assert.That(Vector3.Angle(Vector3.back, vertices[vertices.Length - 2]), Is.EqualTo(63f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(player);
        }
    }

    [TestCase("Assets/Scenes/GameplayScene.unity")]
    [TestCase("Assets/Scenes/SampleScene.unity")]
    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox.unity")]
    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity")]
    [TestCase("Assets/Scenes/Testing/test_balance.unity")]
    [TestCase("Assets/Scenes/Testing/enemiesTesting.unity")]
    public void PlayerScene_HasOneAuthoredRearThreatIndicatorUnderUi(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        try
        {
            Transform[] ui = scene.GetRootGameObjects().Where(root => root.name == "UI").Select(root => root.transform).ToArray();
            Assert.That(ui, Has.Length.EqualTo(1), path);
            RearThreatPresenter[] presenters = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RearThreatPresenter>(true)).ToArray();
            Assert.That(presenters, Has.Length.EqualTo(1), path);
            RearThreatPresenter presenter = presenters[0];
            Assert.That(presenter.transform.IsChildOf(ui[0]), Is.True);
            Assert.That(presenter.Sensor, Is.Not.Null);
            Assert.That(presenter.Sensor.gameObject, Is.SameAs(presenter.gameObject));
            Assert.That(presenter.Player, Is.Not.Null);
            Assert.That(presenter.Player.GetComponent<LevelUpChoiceUI>(), Is.Not.Null);
            Assert.That(presenter.Player.gameObject.scene, Is.EqualTo(scene));
            presenter.RestoreAuthoredMeshes();
            AssertMesh(presenter.Fill, presenter, 2991);
            AssertMesh(presenter.Outline, presenter, 2992);
            Assert.That(presenter.GetComponentsInChildren<Canvas>(true), Is.Empty, "The indicator is a world mesh, not a screen canvas.");
            Assert.That(presenter.GetComponentsInChildren<Collider>(true), Is.Empty, "Presentation must not add physics geometry.");
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static void AssertMesh(MeshFilter filter, RearThreatPresenter presenter, int queue)
    {
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter.transform.IsChildOf(presenter.transform), Is.True);
        Assert.That(filter.sharedMesh, Is.Not.Null);
        Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
        Assert.That(EditorUtility.IsPersistent(filter.sharedMesh), Is.True, "Authored mesh must survive scene reloads.");
        MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sharedMaterial, Is.Not.Null);
        Assert.That(EditorUtility.IsPersistent(renderer.sharedMaterial), Is.True);
        Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/UI/Rear Threat Crescent"));
        Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(queue));
    }
}
