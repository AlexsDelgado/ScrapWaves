using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector del hub de balance: además de los campos, muestra en vivo los números que el designer
/// necesita mientras tunea (intervalo real, multiplicador real de cantidad, estado del spawneo) y
/// atajos para saltar a cualquier punto de la barra de heat sin tener que farmear kills.
/// </summary>
[CustomEditor(typeof(BalanceTuningHub))]
public class BalanceTuningHubEditor : Editor
{
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BalanceTuningHub hub = (BalanceTuningHub)target;

        EditorGUILayout.Space();

        if (hub.Profile == null)
        {
            EditorGUILayout.HelpBox(
                "Sin profile asignado: cada manager usa sus propios campos de fallback.",
                MessageType.Warning);
        }
        else if (GUILayout.Button("Seleccionar el profile"))
        {
            Selection.activeObject = hub.Profile;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Entrá en Play Mode para ver los valores en vivo. Los cambios que hagas en el profile " +
                "durante Play Mode se guardan (es un asset, no un componente de escena).",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("En vivo", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.LabelField("Estado del spawneo", hub.SpawnStateLabel);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Tiempo", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("  Minutos de escalado", $"{hub.MinutesSinceScalingStarted:0.00}");
            EditorGUILayout.LabelField("  Intensidad", $"{hub.TimeIntensity:0.###}");

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Heat", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("  Barra visible (HUD)", $"{hub.HeatBarPercent:0}%");
            EditorGUILayout.LabelField("  Ratio (eje X de la curva)", $"{hub.HeatRatio:0.###}");
            EditorGUILayout.LabelField("  Intensidad", $"{hub.HeatSpawnIntensity:0.###}");

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Resultado", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("  Intervalo real", $"{hub.EffectiveSpawnIntervalSeconds:0.00} s");
            EditorGUILayout.LabelField("  Cantidad por oleada", $"x{hub.EffectiveSpawnCountMultiplier:0.00} sobre el BatchSize de la ruleta");
            EditorGUILayout.LabelField("  Enemigos activos", $"{hub.ActiveEnemies} / {hub.MaxActiveEnemies}");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Forzar heat (% de la barra visible)", EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("0%"))
                hub.SetHeatByBarPercent(0f);
            if (GUILayout.Button("40%"))
                hub.SetHeatByBarPercent(0.4f);
            if (GUILayout.Button("80%"))
                hub.SetHeatByBarPercent(0.8f);
            if (GUILayout.Button("99%"))
                hub.SetHeatByBarPercent(0.99f);
        }

        if (GUILayout.Button("Disparar Overheat"))
            hub.TriggerOverheat();

        EditorGUILayout.HelpBox(
            "La barra al 80% equivale a ratio 0.5 en la curva: los dos tramos de la barra cuestan los " +
            "mismos puntos de heat, así que el último 20% visual se llena el doble de rápido.",
            MessageType.None);
    }
}
