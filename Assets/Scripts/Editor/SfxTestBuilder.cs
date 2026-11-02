using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Crea la escena SfxTest y la compila como build independiente en Builds/SfxTest.
/// Menú: ScrapWaves/Audio/Build SFX Test. En batch: -executeMethod SfxTestBuilder.Build.
/// No toca la lista de escenas del build principal.
/// </summary>
public static class SfxTestBuilder
{
    private const string ScenePath = "Assets/Scenes/Testing/SfxTest.unity";
    private const string LibraryPath = "Assets/Audio/Synth/SynthSfxLibrary.asset";
    private const string OutputDir = "Builds/SfxTest";
    private const string ExeName = "SfxTest.exe";

    [MenuItem("ScrapWaves/Audio/Build SFX Test")]
    public static void Build()
    {
        CreateScene();

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(OutputDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        Directory.CreateDirectory(OutputDir);
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"SfxTest build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB -> {Path.GetFullPath(options.locationPathName)}");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
            EditorApplication.Exit(1);
    }

    private static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Se carga después de NewScene: crear la escena descarga los assets sin uso y dejaría la referencia nula.
        SynthSfxLibrary library = AssetDatabase.LoadAssetAtPath<SynthSfxLibrary>(LibraryPath);
        if (library == null)
            throw new FileNotFoundException(LibraryPath);

        var cameraObject = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.07f, 0.08f);

        var runnerObject = new GameObject("SfxAuditionRunner", typeof(SfxAuditionRunner));
        var runner = new SerializedObject(runnerObject.GetComponent<SfxAuditionRunner>());
        runner.FindProperty("_library").objectReferenceValue = library;
        runner.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
