using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Menú de QA (tecla F3) para ver y ajustar en runtime las variables del CORE LOOP:
/// el spawner orbital (cadencia, tope, radios) y la ruleta (pesos, batch, variante).
///
/// Seguridad: la ruleta vive en un ScriptableObject; los cambios sobre él se
/// aplican en vivo (los spawners leen la config en cada tirada) pero se RESTAURAN
/// al salir del play (OnDisable), así el asset no queda modificado. Los campos del
/// OrbitalSpawner son de un componente de escena, así que tampoco persisten.
///
/// Se integra con <see cref="QaPanels"/> para ser excluyente con F1/F2.
/// </summary>
[DisallowMultipleComponent]
public class QaCoreLoopMenu : MonoBehaviour
{
    [SerializeField, Tooltip("Vacío = el del EnemiesTestingHarness / FindAnyObjectByType.")]
    private EnemySpawnRouletteConfig _config;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType.")]
    private OrbitalSpawner _orbitalSpawner;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType.")]
    private DifficultyManager _difficultyManager;

    [SerializeField, Tooltip("Mostrar el panel al iniciar el play.")]
    private bool _show;

    private static readonly (string field, string label)[] OrbitalFields =
    {
        ("_spawnInterval", "Intervalo (s)"),
        ("_maxActiveEnemies", "Tope activos"),
        ("_minSpawnRadius", "Radio min"),
        ("_maxSpawnRadius", "Radio max"),
        ("_spawnHeightOffset", "Alto offset"),
    };

    private readonly Dictionary<string, string> _buffers = new();
    private readonly Dictionary<string, FieldInfo> _fieldCache = new();

    // Snapshot del asset de ruleta para restaurar al salir del play.
    private bool _hasSnapshot;
    private int[] _snapWeights;
    private int[] _snapBatches;
    private bool[] _snapVariants;
    private float _snapBonusEvery;
    private int _snapBonusStep;

    private Vector2 _rouletteScroll;

    private void Awake()
    {
        ResolveRefs();
        SnapshotConfig();

        if (_show)
            QaPanels.Active = QaPanelKind.CoreLoop;
    }

    private void OnDisable()
    {
        RestoreConfig();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            QaPanels.Toggle(QaPanelKind.CoreLoop);
    }

    private void ResolveRefs()
    {
        if (_config == null)
        {
            EnemiesTestingHarness harness = FindAnyObjectByType<EnemiesTestingHarness>(FindObjectsInactive.Include);
            if (harness != null)
            {
                FieldInfo cfgField = typeof(EnemiesTestingHarness).GetField("_config",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (cfgField != null)
                    _config = cfgField.GetValue(harness) as EnemySpawnRouletteConfig;
            }
        }

        if (_orbitalSpawner == null)
            _orbitalSpawner = FindAnyObjectByType<OrbitalSpawner>(FindObjectsInactive.Include);
        if (_difficultyManager == null)
            _difficultyManager = FindAnyObjectByType<DifficultyManager>(FindObjectsInactive.Include);
    }

    private void SnapshotConfig()
    {
        if (_config == null || _config.Entries == null)
            return;

        int n = _config.Entries.Length;
        _snapWeights = new int[n];
        _snapBatches = new int[n];
        _snapVariants = new bool[n];
        for (int i = 0; i < n; i++)
        {
            EnemySpawnRouletteConfig.Entry e = _config.Entries[i];
            if (e == null)
                continue;
            _snapWeights[i] = e.BaseWeight;
            _snapBatches[i] = e.BatchSize;
            _snapVariants[i] = e.IsVariant;
        }

        _snapBonusEvery = _config.VariantWeightBonusEverySeconds;
        _snapBonusStep = _config.VariantWeightBonusPerStep;
        _hasSnapshot = true;
    }

    private void RestoreConfig()
    {
        if (!_hasSnapshot || _config == null || _config.Entries == null)
            return;

        int n = Mathf.Min(_config.Entries.Length, _snapWeights.Length);
        for (int i = 0; i < n; i++)
        {
            EnemySpawnRouletteConfig.Entry e = _config.Entries[i];
            if (e == null)
                continue;
            e.BaseWeight = _snapWeights[i];
            e.BatchSize = _snapBatches[i];
            e.IsVariant = _snapVariants[i];
        }

        SetConfigField("_variantWeightBonusEverySeconds", _snapBonusEvery);
        SetConfigField("_variantWeightBonusPerStep", _snapBonusStep);
    }

    private void OnGUI()
    {
        if (QaPanels.Active != QaPanelKind.CoreLoop)
            return;

        const float top = 10f;
        const float gap = 6f;
        float height = Mathf.Min(Screen.height - 20f, 420f);
        float x = 10f;

        x += QaPanels.DrawPanel(new Rect(x, top, 210f, height), "CORE LOOP (F3)", DrawStatus) + gap;
        x += QaPanels.DrawPanel(new Rect(x, top, 230f, height), "SPAWNER ORBITAL", DrawOrbital) + gap;
        QaPanels.DrawPanel(new Rect(x, top, 280f, height), "RULETA", DrawRoulette);
    }

    private void DrawStatus()
    {
        GUILayout.Label($"Enemigos activos: {EnemyRegistry.ActiveCount}");

        if (_orbitalSpawner != null)
        {
            GUILayout.Label($"Orbital activos: {_orbitalSpawner.ActiveSpawnedCount}");
            bool on = _orbitalSpawner.enabled && _orbitalSpawner.gameObject.activeInHierarchy;
            bool next = GUILayout.Toggle(on, " Orbital encendido");
            if (next != on)
            {
                if (next && !_orbitalSpawner.gameObject.activeSelf)
                    _orbitalSpawner.gameObject.SetActive(true);
                _orbitalSpawner.enabled = next;
            }
        }
        else
        {
            GUILayout.Label("Sin OrbitalSpawner.");
        }

        GUILayout.Label($"Overheat wave x{OverheatSwarmBoost.SpawnWaveMultiplier}");

        if (_difficultyManager != null)
        {
            GUILayout.Space(4f);
            GUILayout.Label("<b>Dificultad</b>", QaPanels.RichLabel());
            GUILayout.Label($"  count x{_difficultyManager.GetSpawnCountMultiplier():0.##}");
            GUILayout.Label($"  interval x{_difficultyManager.GetSpawnIntervalScale():0.##}");
        }

        GUILayout.Space(6f);
        if (GUILayout.Button("Copiar reporte QA"))
            QaPanels.Copy(BuildCoreLoopReport());

        GUILayout.Space(4f);
        GUILayout.Label("Cambios en vivo; el asset de");
        GUILayout.Label("ruleta se restaura al salir.");
    }

    private string BuildCoreLoopReport()
    {
        var sb = new StringBuilder(640);
        sb.AppendLine("===== SCRAPWAVES QA — CORE LOOP (F3) =====");
        sb.AppendLine($"Fecha: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"timeScale: {Time.timeScale:0.##}");
        sb.AppendLine();

        sb.AppendLine($"Enemigos activos: {EnemyRegistry.ActiveCount}");
        if (_orbitalSpawner != null)
        {
            bool on = _orbitalSpawner.enabled && _orbitalSpawner.gameObject.activeInHierarchy;
            sb.AppendLine($"Orbital activos: {_orbitalSpawner.ActiveSpawnedCount} | encendido: {on}");
        }

        sb.AppendLine($"Overheat wave x{OverheatSwarmBoost.SpawnWaveMultiplier}");
        sb.AppendLine();

        sb.AppendLine("## SPAWNER ORBITAL");
        if (_orbitalSpawner != null)
        {
            foreach ((string field, string label) in OrbitalFields)
            {
                FieldInfo info = GetField(typeof(OrbitalSpawner), field);
                if (info != null)
                    sb.AppendLine($"  {label} ({field}) = {QaPanels.ValueToString(info.GetValue(_orbitalSpawner))}");
            }
        }
        else
        {
            sb.AppendLine("  (sin OrbitalSpawner)");
        }

        sb.AppendLine();
        sb.AppendLine("## RULETA");
        if (_config != null && _config.Entries != null)
        {
            sb.AppendLine($"  Bonus variante: cada {_config.VariantWeightBonusEverySeconds}s +{_config.VariantWeightBonusPerStep}");
            foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
            {
                if (entry == null)
                    continue;
                sb.AppendLine($"  {entry.Kind,-16} weight={entry.BaseWeight,-4} batch={entry.BatchSize,-3} variant={entry.IsVariant}");
            }
        }
        else
        {
            sb.AppendLine("  (sin config)");
        }

        if (_difficultyManager != null)
        {
            sb.AppendLine();
            sb.AppendLine("## DIFICULTAD");
            sb.AppendLine($"  count x{_difficultyManager.GetSpawnCountMultiplier():0.##} | interval x{_difficultyManager.GetSpawnIntervalScale():0.##}");
        }

        sb.AppendLine("===== FIN DEL REPORTE =====");
        return sb.ToString();
    }

    private void DrawOrbital()
    {
        if (_orbitalSpawner == null)
        {
            GUILayout.Label("Sin OrbitalSpawner en escena.");
            return;
        }

        foreach ((string field, string label) in OrbitalFields)
            DrawReflectedField(_orbitalSpawner, field, label, "orb.");
    }

    private void DrawRoulette()
    {
        if (_config == null || _config.Entries == null)
        {
            GUILayout.Label("Sin EnemySpawnRouletteConfig.");
            return;
        }

        GUILayout.Label("Bonus variante:");
        DrawConfigBonusField("_variantWeightBonusEverySeconds", "Cada (s)");
        DrawConfigBonusField("_variantWeightBonusPerStep", "+peso/paso");

        GUILayout.Space(4f);
        GUILayout.Label("<b>weight / batch / variante</b>", QaPanels.RichLabel());

        _rouletteScroll = GUILayout.BeginScrollView(_rouletteScroll);
        foreach (EnemySpawnRouletteConfig.Entry entry in _config.Entries)
        {
            if (entry == null)
                continue;

            GUILayout.Label(entry.Kind.ToString());
            GUILayout.BeginHorizontal();
            entry.BaseWeight = Mathf.Max(0, IntField($"rw.{entry.Kind}", "w", entry.BaseWeight, 46f));
            entry.BatchSize = Mathf.Max(1, IntField($"rb.{entry.Kind}", "b", entry.BatchSize, 46f));
            entry.IsVariant = GUILayout.Toggle(entry.IsVariant, " var");
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    // -----------------------------------------------------------------
    // Helpers de reflexión / campos
    // -----------------------------------------------------------------

    private void DrawReflectedField(object target, string fieldName, string label, string keyPrefix)
    {
        FieldInfo field = GetField(target.GetType(), fieldName);
        if (field == null)
            return;

        string key = keyPrefix + fieldName;
        object current = field.GetValue(target);
        if (!_buffers.TryGetValue(key, out string buffer))
        {
            buffer = QaPanels.ValueToString(current);
            _buffers[key] = buffer;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(120f));
        string edited = GUILayout.TextField(buffer, GUILayout.Width(70f));
        GUILayout.EndHorizontal();

        if (edited == buffer)
            return;

        _buffers[key] = edited;
        if (QaPanels.TryParseValue(field.FieldType, edited, out object parsed))
            field.SetValue(target, parsed);
    }

    private void DrawConfigBonusField(string fieldName, string label)
    {
        if (_config == null)
            return;

        FieldInfo field = GetField(typeof(EnemySpawnRouletteConfig), fieldName);
        if (field == null)
            return;

        string key = "cfg." + fieldName;
        object current = field.GetValue(_config);
        if (!_buffers.TryGetValue(key, out string buffer))
        {
            buffer = QaPanels.ValueToString(current);
            _buffers[key] = buffer;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(120f));
        string edited = GUILayout.TextField(buffer, GUILayout.Width(70f));
        GUILayout.EndHorizontal();

        if (edited == buffer)
            return;

        _buffers[key] = edited;
        if (QaPanels.TryParseValue(field.FieldType, edited, out object parsed))
            field.SetValue(_config, parsed);
    }

    private int IntField(string key, string label, int value, float fieldWidth)
    {
        if (!_buffers.TryGetValue(key, out string buffer))
        {
            buffer = value.ToString(CultureInfo.InvariantCulture);
            _buffers[key] = buffer;
        }

        GUILayout.Label(label, GUILayout.Width(14f));
        string edited = GUILayout.TextField(buffer, GUILayout.Width(fieldWidth));
        if (edited == buffer)
            return value;

        _buffers[key] = edited;
        if (int.TryParse(edited, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;

        return value;
    }

    private void SetConfigField(string fieldName, object value)
    {
        FieldInfo field = GetField(typeof(EnemySpawnRouletteConfig), fieldName);
        field?.SetValue(_config, value);
    }

    private FieldInfo GetField(System.Type type, string fieldName)
    {
        string cacheKey = type.Name + "." + fieldName;
        if (_fieldCache.TryGetValue(cacheKey, out FieldInfo cached))
            return cached;

        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _fieldCache[cacheKey] = field;
        return field;
    }
}
