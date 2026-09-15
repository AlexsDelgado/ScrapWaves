using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit validation only; creates disposable preview scenes and writes evidence under Library.</summary>
[InitializeOnLoad]
public static class RearThreatValidation
{
    public const string PrefabPath = "Assets/Prefabs/UI/RearThreatIndicator.prefab";
    public const string OutputDirectory = "Library/CodexValidation/RearThreat";
    private const string PlayValidationPending = "RearThreatValidation.PlayModePending";
    private static readonly PlayValidationCallbacks PlayCallbacks = new();

    static RearThreatValidation()
    {
        if (SessionState.GetBool(PlayValidationPending, false))
            TestRunnerApi.RegisterTestCallback(PlayCallbacks);
    }

    // Batch invocation must omit -quit; the callback exits after the Play Mode test and cleanup finish.
    public static void BeginPlayModeValidation()
    {
        RequireEditMode();
        Directory.CreateDirectory(OutputDirectory);
        SessionState.SetBool(PlayValidationPending, true);
        TestRunnerApi.UnregisterTestCallback(PlayCallbacks);
        TestRunnerApi.RegisterTestCallback(PlayCallbacks);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            testNames = new[] { "RearThreatPlayModeValidationTests" }
        }));
    }

    private sealed class PlayValidationCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(PlayValidationPending, false)) return;
            SessionState.SetBool(PlayValidationPending, false);
            TestRunnerApi.SaveResultToFile(result, Path.Combine(OutputDirectory, "PlayModeTests.xml"));
            File.WriteAllText(Path.Combine(OutputDirectory, "PlayModeResult.txt"),
                $"{result.ResultState}; passed={result.PassCount}; failed={result.FailCount}; duration={result.Duration:F3}s\n{result.Message}");
            TestRunnerApi.UnregisterTestCallback(this);
            if (Application.isBatchMode)
                EditorApplication.delayCall += () => EditorApplication.Exit(result.FailCount == 0 && result.PassCount > 0 ? 0 : 1);
        }
    }

    [MenuItem("ScrapWaves/Validation/Rear Threat/Compile and Render")]
    public static void ValidateAll()
    {
        CompilePlayerScripts();
        RenderPreviews();
    }

    public static void CompilePlayerScripts()
    {
        RequireEditMode();
        string directory = Path.Combine(OutputDirectory, "PlayerScripts");
        Directory.CreateDirectory(directory);
        ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(
            new ScriptCompilationSettings
            {
                target = BuildTarget.StandaloneWindows64,
                group = BuildTargetGroup.Standalone,
                options = ScriptCompilationOptions.None
            }, directory);
        if (result.assemblies == null || result.assemblies.Count == 0)
            throw new InvalidOperationException("Player script compilation returned no assemblies.");
        File.WriteAllLines(Path.Combine(OutputDirectory, "PlayerCompilation.txt"), result.assemblies);
        Debug.Log($"Rear threat validation: player scripts compiled ({result.assemblies.Count} assemblies).");
    }

    [MenuItem("ScrapWaves/Validation/Rear Threat/Render PC and Mobile Previews")]
    public static void RenderPreviews()
    {
        RequireEditMode();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Author the rear threat prefab before validation: " + PrefabPath);
        Directory.CreateDirectory(OutputDirectory);
        int originalQuality = QualitySettings.GetQualityLevel();
        RenderPipelineAsset originalQualityPipeline = QualitySettings.renderPipeline;
        RenderPipelineAsset originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        Material actorMaterial = null;
        Material wallMaterial = null;
        var report = new StringBuilder();
        report.AppendLine("Rear threat render validation " + DateTime.UtcNow.ToString("O"));
        report.AppendLine("Graphics API: " + SystemInfo.graphicsDeviceType);
        report.AppendLine("PC and Mobile are project quality/pipeline profiles on this Editor graphics API; this is not a native mobile device build.");
        try
        {
            GameObject indicator = Object.Instantiate(prefab);
            indicator.name = "RearThreatValidationIndicator";
            SceneManager.MoveGameObjectToScene(indicator, previewScene);
            RearThreatPresenter presenter = indicator.GetComponent<RearThreatPresenter>();
            if (presenter == null) throw new InvalidOperationException("The authored prefab has no RearThreatPresenter.");

            actorMaterial = CreateOpaqueMaterial(new Color(0.22f, 0.27f, 0.3f, 1f));
            wallMaterial = CreateOpaqueMaterial(new Color(0.35f, 0.32f, 0.28f, 1f));
            GameObject actor = CreatePrimitive(previewScene, PrimitiveType.Capsule, "Player overlap reference",
                new Vector3(0f, 1f, 0f), new Vector3(0.65f, 1f, 0.65f), actorMaterial);
            GameObject wall = CreatePrimitive(previewScene, PrimitiveType.Cube, "Opaque foreground wall",
                new Vector3(0f, 1f, -2f), new Vector3(7f, 4f, 0.25f), wallMaterial);
            wall.SetActive(false);
            presenter.BindPlayer(actor.transform);

            Camera camera = CreateValidationCamera(previewScene);

            foreach (string qualityName in new[] { "PC", "Mobile" })
            {
                int qualityIndex = Array.IndexOf(QualitySettings.names, qualityName);
                if (qualityIndex < 0) throw new InvalidOperationException("Missing quality profile: " + qualityName);
                QualitySettings.SetQualityLevel(qualityIndex, true);
                if (GraphicsSettings.currentRenderPipeline == null)
                    throw new InvalidOperationException(qualityName + " has no render pipeline asset.");
                report.AppendLine(qualityName + " pipeline: " + GraphicsSettings.currentRenderPipeline.name);
                ValidateShaders(indicator, report);
                for (int pass = 0; pass < actorMaterial.passCount; pass++) ShaderUtil.CompilePass(actorMaterial, pass, true);
                for (int pass = 0; pass < wallMaterial.passCount; pass++) ShaderUtil.CompilePass(wallMaterial, pass, true);

                RenderCase(presenter, camera, qualityName, "Idle", 0f, Array.Empty<float>(), Array.Empty<float>());
                RenderCase(presenter, camera, qualityName, "Single", 0.45f, new[] { -25f }, new[] { 0.65f });
                RenderCase(presenter, camera, qualityName, "ClusteredFullUrgency", 1f,
                    new[] { -65f, -12f, 0f, 12f, 64f }, new[] { 0.65f, 0.85f, 1f, 0.85f, 0.65f });
                wall.SetActive(true);
                RenderCase(presenter, camera, qualityName, "ThroughWallAndPlayer", 1f,
                    new[] { -35f, 0f, 35f }, new[] { 0.85f, 1f, 0.85f });
                wall.SetActive(false);
                camera.transform.position = new Vector3(0f, 2f, -6f);
                camera.transform.LookAt(new Vector3(0f, 1f, 0f));
                RenderCase(presenter, camera, qualityName, "PlayerOverlapLowCamera", 0.7f,
                    new[] { -20f, 20f }, new[] { 0.9f, 0.9f });
                camera.transform.position = new Vector3(0f, 4.2f, -6f);
                camera.transform.LookAt(new Vector3(0f, 1f, 0f));
                if (qualityName == "PC") RenderActualPlayer(presenter, camera, previewScene, actor);
                ValidateShaders(indicator, report);
            }
            File.WriteAllText(Path.Combine(OutputDirectory, "RenderValidation.txt"), report.ToString());
            Debug.Log("Rear threat PC/Mobile previews and shader checks completed: " + OutputDirectory);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
            if (actorMaterial != null) Object.DestroyImmediate(actorMaterial);
            if (wallMaterial != null) Object.DestroyImmediate(wallMaterial);
            QualitySettings.SetQualityLevel(originalQuality, true);
            QualitySettings.renderPipeline = originalQualityPipeline;
            GraphicsSettings.defaultRenderPipeline = originalDefaultPipeline;
        }
    }

    private static void RenderCase(RearThreatPresenter presenter, Camera camera, string quality,
        string name, float urgency, float[] angles, float[] spikeUrgencies)
    {
        SetPreview(presenter, urgency, angles, spikeUrgencies);
        WriteCameraPng(camera, Path.Combine(OutputDirectory, quality + "_" + name + ".png"));
    }

    private static void RenderActualPlayer(RearThreatPresenter presenter, Camera camera, Scene scene, GameObject capsule)
    {
        const string playerPrefabPath = "Assets/Prefabs/player.prefab";
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (playerPrefab == null) throw new InvalidOperationException("Missing player prefab for hip-height visual validation.");
        GameObject actualPlayer = null;
        GameObject lightObject = null;
        bool capsuleWasActive = capsule.activeSelf;
        try
        {
            actualPlayer = Object.Instantiate(playerPrefab);
            actualPlayer.name = "Actual player prefab hip-height reference";
            SceneManager.MoveGameObjectToScene(actualPlayer, scene);
            actualPlayer.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
            actualPlayer.SetActive(true);
            capsule.SetActive(false);
            lightObject = new GameObject("Actual player preview light", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            presenter.BindPlayer(actualPlayer.transform);
            RenderCase(presenter, camera, "PC", "ActualPlayerHipHeight", 0.7f,
                new[] { -40f, 0f, 40f }, new[] { 0.65f, 1f, 0.65f });
        }
        finally
        {
            capsule.SetActive(capsuleWasActive);
            presenter.BindPlayer(capsule.transform);
            if (actualPlayer != null) Object.DestroyImmediate(actualPlayer);
            if (lightObject != null) Object.DestroyImmediate(lightObject);
        }
    }

    public static void SetPreview(RearThreatPresenter presenter, float urgency, float[] angles, float[] spikeUrgencies)
    {
        var serialized = new SerializedObject(presenter);
        RequireProperty(serialized, "_previewUrgency").floatValue = urgency;
        SetFloats(RequireProperty(serialized, "_previewSpikeAngles"), angles);
        SetFloats(RequireProperty(serialized, "_previewSpikeUrgencies"), spikeUrgencies);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        presenter.RefreshPreview();
    }

    private static void SetFloats(SerializedProperty property, float[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).floatValue = values[i];
    }

    private static SerializedProperty RequireProperty(SerializedObject serialized, string name) =>
        serialized.FindProperty(name) ?? throw new InvalidOperationException("Missing preview binding: " + name);

    public static Material CreateOpaqueMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("URP/Unlit is required for the occlusion validation reference.");
        var material = new Material(shader) { name = "RearThreatValidationReference" };
        material.SetColor("_BaseColor", color);
        return material;
    }

    public static GameObject CreatePrimitive(Scene scene, PrimitiveType type, string name,
        Vector3 position, Vector3 scale, Material material)
    {
        GameObject value = GameObject.CreatePrimitive(type);
        value.name = name;
        SceneManager.MoveGameObjectToScene(value, scene);
        value.transform.position = position;
        value.transform.localScale = scale;
        value.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(value.GetComponent<Collider>());
        return value;
    }

    private static void ValidateShaders(GameObject indicator, StringBuilder report)
    {
        Material[] materials = indicator.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToArray();
        if (materials.Length == 0) throw new InvalidOperationException("The authored indicator has no materials.");
        foreach (Material material in materials)
        {
            Shader shader = material.shader;
            if (shader == null) throw new InvalidOperationException("Missing shader on " + material.name);
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            string messages = string.Join(Environment.NewLine, ShaderUtil.GetShaderMessages(shader).Select(message => message.message));
            if (!shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader validation failed for " + shader.name + Environment.NewLine + messages);
            report.AppendLine("Shader passed: " + shader.name + " (" + material.passCount + " passes)");
            if (!string.IsNullOrEmpty(messages)) report.AppendLine(messages);
        }
    }

    public static Camera CreateValidationCamera(Scene scene)
    {
        var cameraObject = new GameObject("Rear threat validation camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.scene = scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.065f, 0.07f, 0.075f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 2.6f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.transform.position = new Vector3(0f, 4.2f, -6f);
        camera.transform.LookAt(new Vector3(0f, 1f, 0f));
        return camera;
    }

    public static void WriteCameraPng(Camera camera, string path)
    {
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
            name = "RearThreatValidationCapture"
        };
        Texture2D pixels = null;
        try
        {
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            pixels.Apply(false, false);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (pixels != null) Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run rear threat validation outside Play Mode.");
    }
}
