using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Captures the real Unity Inspector for the authored menu switch without saving scene changes.</summary>
public sealed class EntranceCinematicLaunchPreview : EditorWindow
{
    private Editor _gameObjectInspector;
    private Editor _settingsInspector;
    private Vector2 _origin;
    private float _pixelsPerPoint;
    private int _repaints;
    private double _started;
    private string _output;
    private bool _done;
    private bool _capturedEnabled;
    private SceneSetup[] _previousScenes;

    [MenuItem("Tools/ScrapWaves/Entrance Cinematic/Capture Launch Inspector")]
    public static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_ENTRANCE_PREVIEW_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = ".utmp/entrance-cinematic-launch";
        Directory.CreateDirectory(output);
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            File.WriteAllText(Path.Combine(output, "inspector-unavailable.txt"), "A graphical Unity Editor is required for real Inspector pixels.");
            Exit(2);
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the authoring Inspector outside Play Mode.");
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Save or discard your scene edits before capturing the Inspector. No scene was opened.");
        SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
        EntranceCinematicLaunchPreview window = null;
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity");
            var settings = FindFirstObjectByType<EntranceCinematicLaunchSettings>();
            if (settings == null) throw new InvalidOperationException("The authored menu launch setting is missing.");
            Selection.activeGameObject = settings.gameObject;
            window = CreateInstance<EntranceCinematicLaunchPreview>();
            window._capturedEnabled = settings.PlayEntranceCinematic;
            window._output = output;
            window._previousScenes = previousScenes;
            window._gameObjectInspector = Editor.CreateEditor(settings.gameObject);
            window._settingsInspector = Editor.CreateEditor(settings);
            window.titleContent = new GUIContent("Entrance Cinematic - Inspector");
            window.position = new Rect(100, 100, 680, 330);
            window.minSize = new Vector2(680, 330);
            window._started = EditorApplication.timeSinceStartup;
            window.ShowUtility();
            window.Focus();
            EditorApplication.update += window.CaptureWhenPainted;
        }
        catch
        {
            if (window != null) window.Close();
            else RestoreScenes(previousScenes);
            throw;
        }
    }

    private void OnGUI()
    {
        _origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
        _pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
        if (_gameObjectInspector != null) _gameObjectInspector.DrawHeader();
        EditorGUILayout.Space(8);
        if (_settingsInspector != null)
        {
            _settingsInspector.DrawHeader();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            bool previousWideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.labelWidth = 250f;
            EditorGUIUtility.wideMode = true;
            try { _settingsInspector.OnInspectorGUI(); }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUIUtility.wideMode = previousWideMode;
            }
        }
        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox($"TitleScreen / Developer - Entrance Cinematic\nSave the scene to apply this switch to Editor launches and builds. {(_capturedEnabled ? "ON" : "OFF")} is the configured value.", MessageType.Info);
        if (Event.current.type == EventType.Repaint) _repaints++;
    }

    private void CaptureWhenPainted()
    {
        if (_done) return;
        double elapsed = EditorApplication.timeSinceStartup - _started;
        Repaint();
        // Defer focus until the utility has painted and editor startup has settled.
        if (_repaints >= 3 && elapsed < 2d) Focus();
        var active = typeof(InternalEditorUtility).GetProperty("isApplicationActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        bool visible = hasFocus && active?.PropertyType == typeof(bool) && (bool)active.GetValue(null);
        if (!visible || _repaints < 3 || elapsed < 1d)
        {
            if (elapsed > 15d) Fail("Unity's Inspector preview was not visible and focused; no unrelated desktop pixels were captured.");
            return;
        }
        Texture2D pixels = null;
        try
        {
            int width = Mathf.RoundToInt(position.width * _pixelsPerPoint);
            int height = Mathf.RoundToInt(position.height * _pixelsPerPoint);
            Color[] colors = InternalEditorUtility.ReadScreenPixel(_origin * _pixelsPerPoint, width, height);
            if (colors == null || colors.Length != width * height) throw new InvalidOperationException("Incomplete Inspector capture.");
            bool varied = false;
            for (int i = 1; i < colors.Length; i++) if (colors[i] != colors[0]) { varied = true; break; }
            if (!varied) throw new InvalidOperationException("Blank Inspector capture.");
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.SetPixels(colors); pixels.Apply();
            File.WriteAllBytes(Path.Combine(_output, "entrance-cinematic-inspector.png"), pixels.EncodeToPNG());
            File.WriteAllText(Path.Combine(_output, "inspector-capture.txt"),
                $"{DateTime.UtcNow:O} | Unity {Application.unityVersion} | real Editor.CreateEditor / OnInspectorGUI | {width}x{height} | TitleScreen serialized {(_capturedEnabled ? "ON" : "OFF")} | scene not saved\n");
            Finish(0);
        }
        catch (Exception exception) { Fail(exception.Message); }
        finally { if (pixels != null) DestroyImmediate(pixels); }
    }

    private void Fail(string reason)
    {
        File.WriteAllText(Path.Combine(_output, "inspector-unavailable.txt"), reason);
        Debug.LogWarning(reason);
        Finish(2);
    }

    private void Finish(int code)
    {
        _done = true;
        EditorApplication.update -= CaptureWhenPainted;
        Close();
        Exit(code);
    }

    private static void Exit(int code)
    {
        if (Environment.GetEnvironmentVariable("SCRAPWAVES_ENTRANCE_PREVIEW_EXIT") == "1")
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }

    private void OnDestroy()
    {
        EditorApplication.update -= CaptureWhenPainted;
        if (_gameObjectInspector != null) DestroyImmediate(_gameObjectInspector);
        if (_settingsInspector != null) DestroyImmediate(_settingsInspector);
        bool dirty = false;
        for (int index = 0; index < SceneManager.sceneCount; index++)
            dirty |= SceneManager.GetSceneAt(index).isDirty;
        if (!dirty) RestoreScenes(_previousScenes);
    }
    private static void RestoreScenes(SceneSetup[] scenes)
    {
        if (scenes == null || scenes.Length == 0) return;
        foreach (var scene in scenes)
        {
            if (!string.IsNullOrEmpty(scene.path)) continue;
            // A new isolated editor starts with a clean, untitled empty scene.
            if (scenes.Length == 1)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return;
        }
        EditorSceneManager.RestoreSceneManagerSetup(scenes);
    }
}
