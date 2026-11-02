using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Captures the real custom Inspector on disposable copies. Requires a visible,
/// focused graphical Unity Editor; batch/headless execution never fabricates a preview.
/// </summary>
public sealed class WeaponStatInspectorPreview : EditorWindow
{
    private const string DefaultOutput = ".utmp/weapon-stat-authoring";
    private WeaponData _copy;
    private WeaponDataEditor _inspector;
    private string _output;
    private int _case;
    private int _repaints;
    private double _caseStarted;
    private Vector2 _screenOrigin;
    private Vector2 _scroll;
    private float _pixelsPerPoint = 1f;
    private bool _capturePending;
    private bool _focusRequested;
    private bool _finished;
    private int _page;
    private float _contentHeight;
    private static readonly (string asset, WeaponUpgradePath path, WeaponStatAuthoringMode mode, string name)[] Cases =
    {
        ("AutomaticCannon", WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Automatic, "cannon-b6-automatic"),
        ("Flamethrower", WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Ability, "flame-a6-q"),
        ("Flamethrower", WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability, "flame-b6-q"),
        ("RocketLauncher", WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability, "rocket-b6-q"),
        ("Mortar", WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Manual, "mortar-a6-manual"),
        ("RotatingBlade", WeaponUpgradePath.PathA, WeaponStatAuthoringMode.Automatic, "blade-a6-automatic"),
        ("RotatingBlade", WeaponUpgradePath.PathB, WeaponStatAuthoringMode.Ability, "blade-b6-q")
    };

    [MenuItem("Tools/ScrapWaves/Weapon Stats/Capture Inspector Previews")]
    public static void CaptureInspectorPreviews()
    {
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_PREVIEW_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = DefaultOutput;
        Directory.CreateDirectory(output);
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Unavailable(output, "A real Inspector capture requires a visible graphical Unity Editor. Batch/headless mode has no Inspector pixels.");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the authoring Inspector outside Play Mode.");
        var window = CreateInstance<WeaponStatInspectorPreview>();
        window._output = output;
        window.titleContent = new GUIContent("Weapon Stat Inspector Preview");
        window.minSize = new Vector2(640, 500);
        window.ShowUtility();
        // Screen resolution is pixels, whereas EditorWindow size is GUI points.
        // Set the position after ShowUtility so a saved utility-window layout
        // cannot move the requested capture outside the visible desktop.
        float scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        window.position = new Rect(40, 40, Mathf.Min(820, Screen.currentResolution.width / scale - 100),
            Mathf.Clamp(Screen.currentResolution.height / scale - 110, 500, 820));
        window.Focus();
        window.LoadCase(0);
        EditorApplication.update += window.CaptureWhenPainted;
    }

    private void LoadCase(int index)
    {
        DisposeCopy();
        _case = index;
        var preview = Cases[index];
        string name = preview.asset;
        WeaponData source = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/" + name + ".asset");
        if (source == null) throw new InvalidOperationException("Missing production weapon for Inspector preview: " + name);
        _copy = Instantiate(source);
        _copy.name = source.name + " (preview copy)";
        _copy.hideFlags = HideFlags.HideAndDontSave;
        // Display every applicable control, all inheriting current tuning. Only the
        // disposable copy receives the empty row; no balance values are invented.
        _copy.StatProfile ??= new WeaponStatProfile();
        if (WeaponStatAuthoringResolver.FindRow(_copy.StatProfile, _copy.WeaponType, 6, preview.path) == null)
            _copy.StatProfile.Levels.Add(WeaponStatProfile.CreateRow(_copy.WeaponType, 6, preview.path));
        _inspector = (WeaponDataEditor)Editor.CreateEditor(_copy, typeof(WeaponDataEditor));
        _inspector.SetAuthoringContext(6, preview.path, preview.mode);
        _scroll = Vector2.zero;
        _page = 0;
        _repaints = 0;
        _caseStarted = EditorApplication.timeSinceStartup;
        _capturePending = true;
        _focusRequested = false;
        Repaint();
    }

    private void OnGUI()
    {
        _screenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
        _pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
        EditorGUILayout.HelpBox("Actual WeaponData Inspector on a disposable copy. No production asset is saved by this preview.", MessageType.Info);
        if (_inspector != null)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _inspector.OnInspectorGUI();
            Rect end = GUILayoutUtility.GetRect(1f, 1f);
            if (Event.current.type == EventType.Repaint) _contentHeight = end.yMax;
            EditorGUILayout.EndScrollView();
        }
        if (Event.current.type == EventType.Repaint) _repaints++;
    }

    private void CaptureWhenPainted()
    {
        if (_finished || !_capturePending) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            _caseStarted = EditorApplication.timeSinceStartup;
            return;
        }
        double elapsed = EditorApplication.timeSinceStartup - _caseStarted;
        // Opening a project can focus its main window after executeMethod runs.
        // Request this utility's focus once after that startup work settles.
        if (!_focusRequested && elapsed >= 1d)
        {
            _focusRequested = true;
            _repaints = 0;
            Focus();
            Repaint();
            return;
        }
        if (!_focusRequested) return;
        // Do not capture a different desktop application's pixels when Unity is hidden.
        if (!hasFocus || !ApplicationIsActive())
        {
            if (elapsed > 8d) Fail("Unity's preview window was not visible and focused. No desktop pixels were captured.");
            return;
        }
        Repaint();
        if (_repaints < 3 || elapsed < 0.75d) return;
        _capturePending = false;
        Texture2D image = null;
        try
        {
            // ReadScreenPixel uses the Editor's desktop coordinate space for
            // both origin and dimensions, including its Windows DPI handling.
            int width = Mathf.RoundToInt(position.width);
            int height = Mathf.RoundToInt(position.height);
            Vector2 origin = _screenOrigin;
            Color[] pixels = InternalEditorUtility.ReadScreenPixel(origin, width, height);
            if (pixels == null || pixels.Length != width * height)
                throw new InvalidOperationException("Unity did not return a complete Inspector image.");
            bool varied = false;
            for (int i = 1; i < pixels.Length; i++)
                if (pixels[i] != pixels[0]) { varied = true; break; }
            if (!varied) throw new InvalidOperationException("Unity returned a blank Inspector image.");
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels(pixels);
            image.Apply();
            string name = Cases[_case].name + (_page == 0 ? "-controls" : "-resolved") + ".png";
            File.WriteAllBytes(Path.Combine(_output, name), image.EncodeToPNG());
            File.AppendAllText(Path.Combine(_output, "inspector-capture.txt"),
                $"{DateTime.UtcNow:O} | {name} | Unity {Application.unityVersion} | actual WeaponDataEditor.OnInspectorGUI | {width}x{height} | origin {origin} | GUI rect {position} | scale {_pixelsPerPoint} | screen {Screen.currentResolution} | disposable asset copy\n");
            if (_page == 0 && _contentHeight > position.height - 80f)
            {
                _page = 1;
                _scroll.y = Mathf.Max(0f, _contentHeight - position.height + 100f);
                _repaints = 0;
                _caseStarted = EditorApplication.timeSinceStartup;
                _capturePending = true;
                Repaint();
            }
            else if (_case + 1 < Cases.Length) LoadCase(_case + 1);
            else Finish(0);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            if (image != null) DestroyImmediate(image);
        }
    }

    private static bool ApplicationIsActive()
    {
        PropertyInfo active = typeof(InternalEditorUtility).GetProperty("isApplicationActive",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return active?.PropertyType == typeof(bool) && (bool)active.GetValue(null);
    }

    private void Fail(string reason)
    {
        Unavailable(_output, reason, false);
        Finish(2);
    }

    private static void Unavailable(string output, string reason, bool allowExit = true)
    {
        File.WriteAllText(Path.Combine(output, "inspector-capture-unavailable.txt"), reason);
        Debug.LogWarning("Weapon stat Inspector preview unavailable: " + reason);
        if (allowExit && Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_PREVIEW_EXIT") == "1")
            EditorApplication.delayCall += () => EditorApplication.Exit(2);
    }

    private void Finish(int exitCode)
    {
        _finished = true;
        EditorApplication.update -= CaptureWhenPainted;
        Close();
        if (Environment.GetEnvironmentVariable("SCRAPWAVES_WEAPON_STAT_PREVIEW_EXIT") == "1")
            EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private void OnDestroy()
    {
        EditorApplication.update -= CaptureWhenPainted;
        DisposeCopy();
    }

    private void DisposeCopy()
    {
        if (_inspector != null) DestroyImmediate(_inspector);
        if (_copy != null) DestroyImmediate(_copy);
    }
}
