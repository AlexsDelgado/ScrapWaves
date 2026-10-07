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
            "Calibra el ritmo desde el submenú de pausa. Muestra el valor base y el propuesto. No se escribe en los stats: si el número cierra, hay que copiarlo a mano.",
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

        EditorGUILayout.LabelField("Jugador en uso", DebugSpeedTool.LocomotionScale.ToString("0.00"));
        EditorGUILayout.LabelField("Proyectiles en uso", DebugSpeedTool.ProjectileScale.ToString("0.00"));
        EditorGUILayout.LabelField("Enemigos en uso", DebugSpeedTool.EnemyScale.ToString("0.00"));
    }
}
