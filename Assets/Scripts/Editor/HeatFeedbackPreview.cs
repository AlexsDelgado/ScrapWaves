using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Preview presentation in the running game without triggering encounters or changing heat.</summary>
public sealed class HeatFeedbackPreview : EditorWindow
{
    private HeatPresentationController _view;
    private Image _fill;
    private bool _preview;
    private float _heat;
    private bool _overheat;
    private bool _cooling;

    [MenuItem("ScrapWaves/Heat/Preview feedback")]
    public static void Open() => GetWindow<HeatFeedbackPreview>("Heat preview");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Heat feedback", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Play mode: preview only the visuals. You can pause the editor (Ctrl+Shift+P) to freeze combat while comparing heat levels.", MessageType.Info);
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.LabelField("Enter Play mode to preview.");
            return;
        }
        if (_view == null) _view = Object.FindAnyObjectByType<HeatPresentationController>();
        if (_view == null) { EditorGUILayout.LabelField("No heat HUD in this scene."); return; }
        bool preview = EditorGUILayout.Toggle("Visual preview", _preview);
        if (_preview && !preview) Restore();
        _preview = preview;
        using (new EditorGUI.DisabledScope(!_preview))
        {
            _heat = EditorGUILayout.Slider("Displayed heat", _heat, 0f, 1f);
            _overheat = EditorGUILayout.Toggle("Overheat", _overheat);
            _cooling = EditorGUILayout.Toggle("Cooling", _cooling);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0%")) Set(0f, false, false);
            if (GUILayout.Button("80%")) Set(.8f, false, false);
            if (GUILayout.Button("Overheat")) Set(1f, true, false);
            if (GUILayout.Button("Cooling")) Set(.6f, false, true);
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Return to real heat")) Restore();
    }

    private void Set(float heat, bool overheat, bool cooling)
    {
        _heat = heat; _overheat = overheat; _cooling = cooling;
    }

    private void OnInspectorUpdate()
    {
        if (!EditorApplication.isPlaying) { _preview = false; _view = null; _fill = null; return; }
        if (!_preview || _view == null) return;
        _view.PreviewInEditor(_heat, _overheat, _cooling);
        if (_fill == null)
        {
            var hud = _view.GetComponent<PlayerBarsHud>();
            if (hud != null) _fill = (Image)new SerializedObject(hud).FindProperty("_overheatFill").objectReferenceValue;
        }
        if (_fill != null)
        {
            _fill.fillAmount = _overheat ? 1f : _heat;
            _fill.GetComponent<EnergyMeterVfx>()?.SetHeat(_heat, _overheat, true);
            _fill.color = _overheat ? new Color(1f, .22f, .1f) : _heat >= .8f && !_cooling
                ? new Color(1f, .68f, .12f) : new Color(1f, .42f, .08f);
        }
        Canvas.ForceUpdateCanvases();
        Repaint();
    }

    private void OnDisable() => Restore();
    private void Restore()
    {
        _preview = false;
        if (_view != null) _view.EndEditorPreview();
        if (_fill == null) return;
        var heat = Object.FindAnyObjectByType<HeatManager>();
        var overheat = Object.FindAnyObjectByType<OverheatManager>();
        bool active = overheat != null && overheat.IsOverheating;
        _fill.fillAmount = active ? 1f : heat != null ? heat.NormalizedHeat : 0f;
        _fill.GetComponent<EnergyMeterVfx>()?.SetHeat(_fill.fillAmount, active, true);
        _fill.color = active ? new Color(1f, .22f, .1f, 1f) :
            _fill.fillAmount >= .8f && heat != null && !heat.IsPostOverheatDecayActive
            ? new Color(1f, .68f, .12f, .95f) : new Color(1f, .42f, .08f, .95f);
        Canvas.ForceUpdateCanvases();
    }
}
