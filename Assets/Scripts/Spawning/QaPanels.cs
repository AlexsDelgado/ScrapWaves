using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Paneles de debug de la escena de QA. Solo uno puede estar visible a la vez:
/// F1 = banco de pruebas (EnemiesTestingHarness), F2 = editor de balance
/// (QaRuntimeTweaker), F3 = variables de core loop (QaCoreLoopMenu).
/// </summary>
public enum QaPanelKind
{
    None,
    Qa,
    Balance,
    CoreLoop,
}

/// <summary>
/// Estado compartido + helpers de IMGUI para los paneles de debug de QA.
/// Centraliza la exclusión mutua entre F1/F2/F3 y el dibujo de paneles.
/// </summary>
public static class QaPanels
{
    public static QaPanelKind Active = QaPanelKind.Qa;

    /// <summary>Alterna el panel pedido; si ya estaba activo lo apaga.</summary>
    public static void Toggle(QaPanelKind kind)
    {
        Active = Active == kind ? QaPanelKind.None : kind;
    }

    /// <summary>Copia el texto al portapapeles y lo escribe en consola.</summary>
    public static void Copy(string report)
    {
        GUIUtility.systemCopyBuffer = report;
        Debug.Log(report);
        Debug.Log("[QA] Reporte copiado al portapapeles.");
    }

    private static GUIStyle s_richLabel;

    public static GUIStyle RichLabel()
    {
        if (s_richLabel == null)
            s_richLabel = new GUIStyle(GUI.skin.label) { richText = true };

        return s_richLabel;
    }

    /// <summary>Dibuja un panel-caja con título y devuelve su ancho (para encadenar en horizontal).</summary>
    public static float DrawPanel(Rect rect, string title, Action body)
    {
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label($"<b>{title}</b>", RichLabel());
        body();
        GUILayout.EndArea();
        return rect.width;
    }

    public static string ValueToString(object value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case float f:
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    public static bool TryParseValue(Type type, string text, out object value)
    {
        value = null;
        if (type == typeof(float))
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                value = f;
                return true;
            }

            return false;
        }

        if (type == typeof(int))
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            {
                value = i;
                return true;
            }

            return false;
        }

        if (type.IsEnum)
        {
            try
            {
                value = Enum.Parse(type, text, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
