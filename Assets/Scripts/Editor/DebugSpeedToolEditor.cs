using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DebugSpeedTool))]
public class DebugSpeedToolEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DebugSpeedTool tool = (DebugSpeedTool)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Calibra el ritmo del jugador en Play. La escala no se escribe en los stats: al salir de Play queda apagada y los valores de autoría siguen iguales.",
            MessageType.Info);

        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("Entrá a Play para prender la escala y ver real → efectivo.");
            return;
        }

        Repaint();

        bool applying = EditorGUILayout.Toggle("Aplicar escala ahora", tool.IsApplying);
        if (applying != tool.IsApplying)
            tool.SetApplying(applying);

        EditorGUILayout.LabelField("Factor en uso", DebugSpeedTool.LocomotionScale.ToString("0.00"));
    }
}
