using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector del hub de powerups: además del campo del perfil, muestra en vivo el buff activo, el factor de nivel
/// y la probabilidad real de drop, y da botones para aplicar o hacer caer cada powerup sin farmear enemigos.
/// </summary>
[CustomEditor(typeof(PowerupBalanceHub))]
public class PowerupBalanceHubEditor : Editor
{
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PowerupBalanceHub hub = (PowerupBalanceHub)target;

        EditorGUILayout.Space();

        if (hub.Profile == null)
        {
            EditorGUILayout.HelpBox(
                "Sin profile asignado: los powerups usan los valores originales del código.",
                MessageType.Warning);
        }
        else if (GUILayout.Button("Seleccionar el profile"))
        {
            Selection.activeObject = hub.Profile;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Entrá en Play Mode para ver los valores en vivo y usar los botones. Los cambios que hagas en el " +
                "profile durante Play Mode se guardan (es un asset, no un componente de escena).",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("En vivo", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.LabelField("Buff activo", hub.ActiveBuffLabel);
            EditorGUILayout.LabelField("Factor de nivel (0 = nivel 1, 1 = tope)", $"{hub.CurrentLevelT:0.###}");
            EditorGUILayout.LabelField("Drop con enemigo común", $"{hub.EffectiveDropChance(false) * 100f:0.##}%");
            EditorGUILayout.LabelField("Drop con élite o jefe", $"{hub.EffectiveDropChance(true) * 100f:0.##}%");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Aplicar ahora al jugador", EditorStyles.miniBoldLabel);
        DrawTypeButtons(type => hub.ApplyNow(type));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hacer caer delante del jugador", EditorStyles.miniBoldLabel);
        DrawTypeButtons(type => hub.SpawnInFrontOfPlayer(type));

        EditorGUILayout.HelpBox(
            "El factor de nivel interpola cada valor entre su mínimo y su máximo. Para ver los extremos sin subir de " +
            "nivel, usá 'Forced Level T' en el profile.",
            MessageType.None);
    }

    private static void DrawTypeButtons(Action<TemporaryPowerupType> onClick)
    {
        foreach (TemporaryPowerupType type in Enum.GetValues(typeof(TemporaryPowerupType)))
        {
            if (GUILayout.Button(type.ToString()))
                onClick(type);
        }
    }
}
