#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring of the scene-owned rear threat indicator. Existing visual edits are preserved.</summary>
public static class RearThreatIndicatorAuthoring
{
    public const string PrefabPath = "Assets/Prefabs/UI/RearThreatIndicator.prefab";
    public const string ArtFolder = "Assets/Art/UI/RearThreat";
    public const string FillMaterialPath = ArtFolder + "/RearThreatFill.mat";
    public const string OutlineMaterialPath = ArtFolder + "/RearThreatOutline.mat";
    public const string FillMeshPath = ArtFolder + "/RearThreatFillPreview.asset";
    public const string OutlineMeshPath = ArtFolder + "/RearThreatOutlinePreview.asset";
    private const string ShaderName = "ScrapWaves/UI/Rear Threat Crescent";
    private const string PlayerPrefabPath = "Assets/Prefabs/player.prefab";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/GameplayScene.unity",
        "Assets/Scenes/SampleScene.unity",
        "Assets/Scenes/Testing/WeaponTestingSandbox.unity",
        "Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity",
        "Assets/Scenes/Testing/test_balance.unity",
        "Assets/Scenes/Testing/enemiesTesting.unity"
    };

    [MenuItem("ScrapWaves/UI/Create Missing Rear Threat Indicator Assets")]
    public static void CreateMissingAssets()
    {
        RequireEditMode();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            return;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
            throw new InvalidOperationException($"Missing rear threat shader: {ShaderName}");
        EnsureFolder(ArtFolder);
        EnsureFolder("Assets/Prefabs/UI");
        Material fillMaterial = GetOrCreateMaterial(FillMaterialPath, shader, 2991, new Color(1f, 0.35f, 0.65f, 0.08f));
        Material outlineMaterial = GetOrCreateMaterial(OutlineMaterialPath, shader, 2992, Color.clear);

        var root = new GameObject("RearThreatIndicator");
        root.SetActive(false);
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            RearThreatSensor sensor = root.AddComponent<RearThreatSensor>();
            RearThreatPresenter presenter = root.AddComponent<RearThreatPresenter>();
            MeshFilter fill = CreateMeshChild(root.transform, "Fill", fillMaterial);
            MeshFilter outline = CreateMeshChild(root.transform, "Outline", outlineMaterial);
            presenter.Configure(sensor, fill, outline);
            root.SetActive(true);
            presenter.RefreshPreview();
            // Capture both before asset writes, whose save callbacks may restore the view's authored references.
            Mesh generatedFill = fill.sharedMesh;
            Mesh generatedOutline = outline.sharedMesh;
            Mesh fillMesh = GetOrCreatePreviewMesh(FillMeshPath, generatedFill);
            Mesh outlineMesh = GetOrCreatePreviewMesh(OutlineMeshPath, generatedOutline);
            presenter.SetAuthoredMeshes(fillMesh, outlineMesh);
            presenter.RestoreAuthoredMeshes();
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException($"Unable to save {PrefabPath}.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("ScrapWaves/UI/Add Rear Threat Indicator To Current Scene")]
    public static void AuthorCurrentScene() => AuthorScene(SceneManager.GetActiveScene());

    [MenuItem("ScrapWaves/UI/Refresh Rear Threat Prefab Preview Meshes")]
    public static void RefreshPrefabPreviewMeshes()
    {
        RequireEditMode();
        CreateMissingAssets();
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            RearThreatPresenter presenter = instance.GetComponent<RearThreatPresenter>();
            presenter.RefreshPreview();
            // Capture both copies before saving: the save processor restores asset references.
            Mesh generatedFill = presenter.Fill.sharedMesh;
            Mesh generatedOutline = presenter.Outline.sharedMesh;
            RefreshPreviewAsset(FillMeshPath, generatedFill);
            RefreshPreviewAsset(OutlineMeshPath, generatedOutline);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void RefreshPreviewAsset(string path, Mesh source)
    {
        Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (asset == null || source == null)
            throw new InvalidOperationException("Missing rear threat preview mesh: " + path);
        string name = asset.name;
        EditorUtility.CopySerialized(source, asset);
        asset.name = name;
        asset.hideFlags = HideFlags.None;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
    }

    public static RearThreatPresenter AuthorScene(Scene scene)
    {
        RequireEditMode();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("Rear threat authoring requires a loaded scene.", nameof(scene));
        LevelUpChoiceUI[] players = Components<LevelUpChoiceUI>(scene).Where(component =>
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(component.gameObject) == PlayerPrefabPath).ToArray();
        if (players.Length != 1)
            throw new InvalidOperationException($"Expected one player prefab in {scene.path}, found {players.Length}.");
        Transform[] uiRoots = scene.GetRootGameObjects().Where(root => root.name == "UI").Select(root => root.transform).ToArray();
        if (uiRoots.Length > 1)
            throw new InvalidOperationException($"More than one UI root exists in {scene.path}.");
        Transform ui = uiRoots.FirstOrDefault();
        if (ui == null)
        {
            var root = new GameObject("UI");
            SceneManager.MoveGameObjectToScene(root, scene);
            ui = root.transform;
        }
        return AuthorUi(ui, players[0].transform);
    }

    public static RearThreatPresenter AuthorUi(Transform uiRoot, Transform player)
    {
        RequireEditMode();
        if (uiRoot == null || player == null || uiRoot.gameObject.scene != player.gameObject.scene)
            throw new ArgumentException("UI root and player must belong to the same scene.");
        RearThreatPresenter[] existing = uiRoot.GetComponentsInChildren<RearThreatPresenter>(true);
        if (existing.Length > 1)
            throw new InvalidOperationException("More than one rear threat indicator exists under UI.");

        RearThreatPresenter presenter = existing.FirstOrDefault();
        bool created = presenter == null;
        if (created)
        {
            if (uiRoot.Find("RearThreatIndicator") != null)
                throw new InvalidOperationException("RearThreatIndicator exists without its presenter; repair its component before authoring.");
            CreateMissingAssets();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, uiRoot);
            presenter = instance.GetComponent<RearThreatPresenter>();
            if (presenter == null)
                throw new InvalidOperationException($"The authored prefab is missing {nameof(RearThreatPresenter)}.");
            // UI is an organizational root and may have a nonzero world position.
            instance.transform.SetPositionAndRotation(player.position, Quaternion.Euler(0f, player.eulerAngles.y, 0f));
            Record(instance.transform);
        }

        RearThreatSensor sensor = presenter.Sensor != null ? presenter.Sensor : presenter.GetComponent<RearThreatSensor>();
        MeshFilter fill = presenter.Fill != null ? presenter.Fill : presenter.transform.Find("Fill")?.GetComponent<MeshFilter>();
        MeshFilter outline = presenter.Outline != null ? presenter.Outline : presenter.transform.Find("Outline")?.GetComponent<MeshFilter>();
        if (sensor == null || fill == null || outline == null)
            throw new InvalidOperationException("Rear threat indicator has an incomplete authored hierarchy; existing visuals were preserved.");
        bool presenterChanged = presenter.Sensor != sensor || presenter.Fill != fill || presenter.Outline != outline;
        if (presenterChanged)
            presenter.Configure(sensor, fill, outline);

        bool sensorChanged = false;
        if (sensor.Player == null)
        {
            presenter.BindPlayer(player);
            sensorChanged = true;
        }
        else if (sensor.Player != player)
            throw new InvalidOperationException("The existing rear threat indicator is bound to a different player; select its intended binding manually.");

        Camera camera = Components<Camera>(player.gameObject.scene).FirstOrDefault(candidate => candidate.CompareTag("MainCamera"));
        if (sensor.CameraReference == null && camera != null)
        {
            sensor.CameraReference = camera;
            sensorChanged = true;
        }
        if (sensor.GameplayCamera == null && sensor.CameraReference != null)
        {
            ThirdPersonCamera gameplayCamera = sensor.CameraReference.GetComponent<ThirdPersonCamera>();
            if (gameplayCamera != null)
            {
                sensor.GameplayCamera = gameplayCamera;
                sensorChanged = true;
            }
        }
        if (presenterChanged || created) Record(presenter);
        if (sensorChanged || created) Record(sensor);
        if (created || presenterChanged || sensorChanged)
            EditorSceneManager.MarkSceneDirty(uiRoot.gameObject.scene);
        return presenter;
    }

    [MenuItem("ScrapWaves/UI/Add Rear Threat Indicator To All Player Scenes")]
    public static void MigrateAllPlayerScenes()
    {
        RequireEditMode();
        CreateMissingAssets();
        Scene active = SceneManager.GetActiveScene();
        string backupFolder = "Library/RearThreatIndicatorAuthoring/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        foreach (string path in ScenePaths.OrderBy(path => path == active.path ? 0 : 1))
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                // Save a separate copy of the loaded scene, including pending Inspector changes.
                Directory.CreateDirectory(backupFolder);
                if (!EditorSceneManager.SaveScene(scene, backupFolder + "/" + scene.name + ".unity", true))
                    throw new InvalidOperationException($"Unable to back up {path}; no authoring was attempted for that scene.");
                SceneManager.SetActiveScene(scene);
                AuthorScene(scene);
                if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"Unable to save authored rear threat indicator in {path}.");
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
        Debug.Log($"Rear threat indicator authored in {ScenePaths.Length} player scenes. Scene backups: {backupFolder}");
    }

    private static MeshFilter CreateMeshChild(Transform parent, string name, Material material)
    {
        var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return child.GetComponent<MeshFilter>();
    }

    private static Material GetOrCreateMaterial(string path, Shader shader, int queue, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), renderQueue = queue };
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Mesh GetOrCreatePreviewMesh(string path, Mesh generated)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        if (generated == null || generated.vertexCount == 0)
            throw new InvalidOperationException($"Rear threat preview geometry was not generated for {path}.");
        Mesh mesh = Object.Instantiate(generated);
        mesh.name = Path.GetFileNameWithoutExtension(path);
        mesh.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static void Record(Object value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author rear threat UI outside Play Mode.");
    }
}

/// <summary>Preview meshes are temporary; scene and prefab serialization always retains authored mesh assets.</summary>
internal sealed class RearThreatPreviewSaveProcessor : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return paths;
        RearThreatPresenter[] presenters = Resources.FindObjectsOfTypeAll<RearThreatPresenter>()
            .Where(presenter => !EditorUtility.IsPersistent(presenter) && presenter.gameObject.scene.IsValid()).ToArray();
        foreach (RearThreatPresenter presenter in presenters)
            presenter.RestoreAuthoredMeshes();
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            foreach (RearThreatPresenter presenter in presenters)
                if (presenter != null && presenter.isActiveAndEnabled
                    && presenter.gameObject.scene.IsValid() && presenter.gameObject.scene.isLoaded)
                    presenter.RefreshPreview();
        };
        return paths;
    }
}
#endif
