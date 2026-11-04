using UnityEditor;
using UnityEngine;

/// <summary>Visual-only shield preview. Closing it restores the actual player's values.</summary>
public sealed class ShieldHudPreview : EditorWindow
{
    private PlayerBarsHud _hud;
    private ShieldChargesGraphic _meter;
    private bool _preview;
    private int _capacity = 6;
    private int _charges = 6;

    [MenuItem("ScrapWaves/UI/Preview shields")]
    public static void Open() => GetWindow<ShieldHudPreview>("Shield preview");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Vista visual en Play: no equipa escudos ni cambia las cargas del personaje. Al desactivarla se muestran los valores reales.", MessageType.Info);
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.LabelField("Entrá en Play para probar el indicador.");
            return;
        }
        bool preview = EditorGUILayout.Toggle("Vista previa", _preview);
        if (_preview && !preview) Restore();
        _preview = preview;
        using (new EditorGUI.DisabledScope(!_preview))
        {
            _capacity = EditorGUILayout.IntSlider("Capacidad", _capacity, 0, 6);
            _charges = EditorGUILayout.IntSlider("Cargas disponibles", _charges, 0, _capacity);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Impacto")) _charges = Mathf.Max(0, _charges - 1);
            if (GUILayout.Button("Recargar")) _charges = _capacity;
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Volver a los valores reales")) Restore();
    }

    private void OnInspectorUpdate()
    {
        if (!EditorApplication.isPlaying) { _preview = false; _hud = null; _meter = null; return; }
        if (!_preview) return;
        if (_hud == null) _hud = Object.FindAnyObjectByType<PlayerBarsHud>();
        if (_hud == null) return;
        if (_meter == null) _meter = _hud.GetComponentInChildren<ShieldChargesGraphic>(true);
        if (_meter != null) _meter.SetCharges(_charges, _capacity);
        Canvas.ForceUpdateCanvases();
        Repaint();
    }

    private void OnDisable() => Restore();
    private void Restore()
    {
        bool wasPreviewing = _preview;
        _preview = false;
        if (!wasPreviewing || !EditorApplication.isPlaying || _hud == null || _meter == null) return;
        var health = (PlayerHealth)new SerializedObject(_hud).FindProperty("_playerHealth").objectReferenceValue;
        _meter.SetCharges(health != null ? health.ShieldCharges : 0, health != null ? health.MaxShieldCharges : 0);
        Canvas.ForceUpdateCanvases();
    }
}
