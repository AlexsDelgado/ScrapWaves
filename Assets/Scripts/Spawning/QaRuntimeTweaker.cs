using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor de balance en runtime para la escena de QA. Permite a diseñadores/QA
/// modificar stats de los enemigos (vida, daño, velocidad, parámetros de cada
/// comportamiento) en vivo, SIN tocar los prefabs/assets.
///
/// Cómo funciona:
/// - Lee los campos serializados editables (float/int/bool/enum) de los prefabs
///   referenciados por la ruleta (solo para mostrar los valores por defecto).
/// - Los cambios se guardan como "overrides" en memoria por tipo de enemigo.
/// - Un escáner aplica esos overrides a los enemigos vivos y a los que vayan
///   naciendo (identifica el tipo por el nombre del prefab "<Nombre>(Clone)").
/// - "Resetear" devuelve a los valores por defecto del prefab y limpia overrides.
///
/// Toggle con F2.
/// </summary>
[DisallowMultipleComponent]
public class QaRuntimeTweaker : MonoBehaviour
{
    [SerializeField, Tooltip("Vacío = el del EnemiesTestingHarness / FindAnyObjectByType.")]
    private EnemySpawnRouletteConfig _config;

    [SerializeField, Tooltip("Mostrar la ventana al iniciar el play.")]
    private bool _show;

    [SerializeField, Tooltip("Cada cuánto re-escanear enemigos vivos para aplicar overrides (s).")]
    [Min(0.05f)]
    private float _scanInterval = 0.25f;

    private static readonly string[] TweakableComponents =
    {
        "EnemyHealth",
        "EnemyContactDamage",
        "SimpleFollow",
        "EnemyFollow",
        "FlyingRangedBehavior",
        "ChargerEnemyBehavior",
        "HellfireSlimeBehavior",
        "BomberDroneBehavior",
    };

    // override key = "ComponentType.field"
    private readonly Dictionary<EnemySpawnKind, Dictionary<string, object>> _overrides = new();

    // valores por defecto leídos del prefab, para poder resetear.
    private readonly Dictionary<EnemySpawnKind, Dictionary<string, object>> _defaults = new();

    // buffers de texto de la UI: key = "kind|ComponentType.field"
    private readonly Dictionary<string, string> _editBuffers = new();

    // mapa nombre-de-prefab -> kind, para identificar instancias.
    private readonly Dictionary<string, EnemySpawnKind> _prefabNameToKind = new();

    // instancias ya estampadas con la versión de overrides aplicada.
    private readonly Dictionary<int, int> _appliedVersion = new();

    private int _version;
    private float _nextScanTime;
    private readonly Dictionary<EnemySpawnKind, Vector2> _kindScroll = new();

    private void Awake()
    {
        ResolveConfig();
        BuildCatalog();

        if (_show)
            QaPanels.Active = QaPanelKind.Balance;
    }

    private void ResolveConfig()
    {
        if (_config != null)
            return;

        EnemiesTestingHarness harness = FindAnyObjectByType<EnemiesTestingHarness>(FindObjectsInactive.Include);
        if (harness != null)
        {
            FieldInfo cfgField = typeof(EnemiesTestingHarness).GetField("_config",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (cfgField != null)
                _config = cfgField.GetValue(harness) as EnemySpawnRouletteConfig;
        }
    }

    private void BuildCatalog()
    {
        _prefabNameToKind.Clear();
        _defaults.Clear();

        if (_config == null || _config.Entries == null)
            return;

        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null || entry.Prefab == null)
                continue;

            _prefabNameToKind[entry.Prefab.name] = entry.Kind;

            var defaults = new Dictionary<string, object>();
            foreach (MonoBehaviour component in entry.Prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || Array.IndexOf(TweakableComponents, component.GetType().Name) < 0)
                    continue;

                foreach (FieldInfo field in GetTweakableFields(component.GetType()))
                {
                    string key = component.GetType().Name + "." + field.Name;
                    if (!defaults.ContainsKey(key))
                        defaults[key] = field.GetValue(component);
                }
            }

            _defaults[entry.Kind] = defaults;
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            QaPanels.Toggle(QaPanelKind.Balance);

        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + _scanInterval;
        ApplyToLiveEnemies();
    }

    private void ApplyToLiveEnemies()
    {
        if (_overrides.Count == 0 && _appliedVersion.Count == 0)
            return;

        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var alive = new HashSet<int>();

        foreach (EnemyHealth enemy in enemies)
        {
            GameObject go = enemy.gameObject;
            int id = go.GetInstanceID();
            alive.Add(id);

            if (_appliedVersion.TryGetValue(id, out int applied) && applied == _version)
                continue;

            if (TryResolveKind(go.name, out EnemySpawnKind kind))
                ApplyOverridesToInstance(go, kind);

            _appliedVersion[id] = _version;
        }

        // limpiar instancias muertas del registro
        if (_appliedVersion.Count > alive.Count)
        {
            var dead = new List<int>();
            foreach (int id in _appliedVersion.Keys)
                if (!alive.Contains(id))
                    dead.Add(id);
            foreach (int id in dead)
                _appliedVersion.Remove(id);
        }
    }

    private bool TryResolveKind(string instanceName, out EnemySpawnKind kind)
    {
        kind = default;
        if (string.IsNullOrEmpty(instanceName))
            return false;

        int clone = instanceName.IndexOf("(Clone)", StringComparison.Ordinal);
        string baseName = clone >= 0 ? instanceName.Substring(0, clone) : instanceName;
        baseName = baseName.TrimEnd();
        return _prefabNameToKind.TryGetValue(baseName, out kind);
    }

    private void ApplyOverridesToInstance(GameObject go, EnemySpawnKind kind)
    {
        if (!_overrides.TryGetValue(kind, out Dictionary<string, object> kindOverrides) || kindOverrides.Count == 0)
            return;

        foreach (MonoBehaviour component in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            foreach (FieldInfo field in GetTweakableFields(component.GetType()))
            {
                string key = typeName + "." + field.Name;
                if (kindOverrides.TryGetValue(key, out object value))
                    field.SetValue(component, value);
            }
        }
    }

    private void ResetInstanceToDefaults(GameObject go, EnemySpawnKind kind)
    {
        if (!_defaults.TryGetValue(kind, out Dictionary<string, object> kindDefaults))
            return;

        foreach (MonoBehaviour component in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            foreach (FieldInfo field in GetTweakableFields(component.GetType()))
            {
                string key = typeName + "." + field.Name;
                if (kindDefaults.TryGetValue(key, out object value))
                    field.SetValue(component, value);
            }
        }
    }

    private void ResetAll()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (EnemyHealth enemy in enemies)
            if (TryResolveKind(enemy.gameObject.name, out EnemySpawnKind kind))
                ResetInstanceToDefaults(enemy.gameObject, kind);

        _overrides.Clear();
        _editBuffers.Clear();
        _appliedVersion.Clear();
        _version++;
    }

    private static IEnumerable<FieldInfo> GetTweakableFields(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
            if (!serialized || field.IsNotSerialized)
                continue;

            Type ft = field.FieldType;
            if (ft == typeof(float) || ft == typeof(int) || ft == typeof(bool) || ft.IsEnum)
                yield return field;
        }
    }

    private void OnGUI()
    {
        if (QaPanels.Active != QaPanelKind.Balance)
            return;

        const float top = 10f;
        const float gap = 6f;
        float height = Mathf.Min(Screen.height - 20f, 440f);
        float x = 10f;

        x += QaPanels.DrawPanel(new Rect(x, top, 180f, height), "BALANCE (F2)", DrawControls) + gap;

        if (_config == null || _config.Entries == null)
            return;

        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null || entry.Prefab == null)
                continue;

            EnemySpawnRouletteConfig.Entry captured = entry;
            x += QaPanels.DrawPanel(new Rect(x, top, 215f, height), entry.Kind.ToString(),
                () => DrawEnemyPanel(captured)) + gap;
        }
    }

    private void DrawControls()
    {
        GUILayout.Label("Cambios en vivo.");
        GUILayout.Label("Temporales salvo APLICAR.");
        GUILayout.Space(6f);

        if (GUILayout.Button("Aplicar a vivos"))
        {
            _appliedVersion.Clear();
            ApplyToLiveEnemies();
        }

        if (GUILayout.Button("Resetear a defaults"))
            ResetAll();

#if UNITY_EDITOR
        GUI.color = new Color(0.7f, 1f, 0.7f);
        if (GUILayout.Button("APLICAR CAMBIOS\n(persistir a prefabs)"))
            ApplyOverridesToPrefabs();
        GUI.color = Color.white;
#else
        GUILayout.Label("(APLICAR solo en Editor)");
#endif

        if (GUILayout.Button("Copiar reporte QA"))
            QaPanels.Copy(BuildBalanceReport());

        GUILayout.Space(6f);
        int overrideCount = 0;
        foreach (KeyValuePair<EnemySpawnKind, Dictionary<string, object>> kv in _overrides)
            overrideCount += kv.Value.Count;
        GUILayout.Label($"Overrides activos: {overrideCount}");
    }

#if UNITY_EDITOR
    /// <summary>
    /// Escribe los overrides actuales sobre los prefabs de la ruleta y guarda los
    /// assets. A partir de aquí los cambios persisten en otras escenas (p. ej.
    /// SampleScene), porque los spawners instancian estos mismos prefabs.
    /// </summary>
    private void ApplyOverridesToPrefabs()
    {
        if (_config == null || _config.Entries == null)
        {
            Debug.LogWarning("[QA Balance] Sin EnemySpawnRouletteConfig; no se puede persistir.");
            return;
        }

        int written = 0;
        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null || entry.Prefab == null)
                continue;

            if (!_overrides.TryGetValue(entry.Kind, out Dictionary<string, object> kindOverrides) || kindOverrides.Count == 0)
                continue;

            foreach (MonoBehaviour component in entry.Prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                string typeName = component.GetType().Name;
                foreach (FieldInfo field in GetTweakableFields(component.GetType()))
                {
                    string key = typeName + "." + field.Name;
                    if (!kindOverrides.TryGetValue(key, out object value))
                        continue;

                    field.SetValue(component, value);
                    UnityEditor.EditorUtility.SetDirty(component);
                    written++;
                }
            }
        }

        UnityEditor.AssetDatabase.SaveAssets();

        // Los prefabs ahora contienen los nuevos valores: refrescamos los defaults
        // para que "Resetear" use esta nueva línea base.
        BuildCatalog();

        Debug.Log($"[QA Balance] {written} valores persistidos a los prefabs (afecta a todas las escenas).");
    }
#endif

    private string BuildBalanceReport()
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("===== SCRAPWAVES QA — BALANCE (F2) =====");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"timeScale: {Time.timeScale:0.##}");
        sb.AppendLine("(* = valor modificado en runtime)");
        sb.AppendLine();

        if (_config == null || _config.Entries == null)
        {
            sb.AppendLine("(sin EnemySpawnRouletteConfig)");
            return sb.ToString();
        }

        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null || entry.Prefab == null)
                continue;

            sb.AppendLine($"- {entry.Kind}  (prefab: {entry.Prefab.name})");
            _overrides.TryGetValue(entry.Kind, out Dictionary<string, object> kindOverrides);

            foreach (MonoBehaviour component in entry.Prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || Array.IndexOf(TweakableComponents, component.GetType().Name) < 0)
                    continue;

                string typeName = component.GetType().Name;
                sb.AppendLine($"  [{typeName}]");
                foreach (FieldInfo field in GetTweakableFields(component.GetType()))
                {
                    string key = typeName + "." + field.Name;
                    bool isOverride = kindOverrides != null && kindOverrides.TryGetValue(key, out object _);
                    object value = isOverride ? kindOverrides[key] : field.GetValue(component);
                    sb.AppendLine($"    {field.Name} = {QaPanels.ValueToString(value)}{(isOverride ? " *" : string.Empty)}");
                }
            }
        }

        sb.AppendLine("===== FIN DEL REPORTE =====");
        return sb.ToString();
    }

    private void DrawEnemyPanel(EnemySpawnRouletteConfig.Entry entry)
    {
        if (!_kindScroll.TryGetValue(entry.Kind, out Vector2 scroll))
            scroll = Vector2.zero;

        scroll = GUILayout.BeginScrollView(scroll);
        foreach (MonoBehaviour component in entry.Prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || Array.IndexOf(TweakableComponents, component.GetType().Name) < 0)
                continue;

            GUILayout.Label($"<b>{component.GetType().Name}</b>", QaPanels.RichLabel());
            DrawComponentFields(entry.Kind, component);
        }

        GUILayout.EndScrollView();
        _kindScroll[entry.Kind] = scroll;
    }

    private void DrawComponentFields(EnemySpawnKind kind, MonoBehaviour component)
    {
        string typeName = component.GetType().Name;
        foreach (FieldInfo field in GetTweakableFields(component.GetType()))
        {
            string overrideKey = typeName + "." + field.Name;
            object current = GetCurrentValue(kind, overrideKey, component, field);

            if (field.FieldType == typeof(bool))
            {
                bool cur = current is bool b && b;
                bool next = GUILayout.Toggle(cur, $" {field.Name}");
                if (next != cur)
                    SetOverride(kind, overrideKey, next);
                continue;
            }

            string bufferKey = kind + "|" + overrideKey;
            if (!_editBuffers.TryGetValue(bufferKey, out string buffer))
            {
                buffer = QaPanels.ValueToString(current);
                _editBuffers[bufferKey] = buffer;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(field.Name, GUILayout.Width(120f));
            string edited = GUILayout.TextField(buffer, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            if (edited == buffer)
                continue;

            _editBuffers[bufferKey] = edited;
            if (QaPanels.TryParseValue(field.FieldType, edited, out object parsed))
                SetOverride(kind, overrideKey, parsed);
        }
    }

    private object GetCurrentValue(EnemySpawnKind kind, string overrideKey, MonoBehaviour prefabComponent, FieldInfo field)
    {
        if (_overrides.TryGetValue(kind, out Dictionary<string, object> kindOverrides) &&
            kindOverrides.TryGetValue(overrideKey, out object value))
            return value;

        return field.GetValue(prefabComponent);
    }

    private void SetOverride(EnemySpawnKind kind, string overrideKey, object value)
    {
        if (!_overrides.TryGetValue(kind, out Dictionary<string, object> kindOverrides))
        {
            kindOverrides = new Dictionary<string, object>();
            _overrides[kind] = kindOverrides;
        }

        kindOverrides[overrideKey] = value;
        _version++;
    }
}
