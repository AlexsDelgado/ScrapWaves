using UnityEngine;

/// <summary>
/// Calibración de ritmo para diseño. Tres factores independientes (jugador,
/// proyectiles y enemigos) solo mientras Play está activo y el interruptor está prendido.
/// No escribe stats, prefabs ni la escena: al salir de Play las escalas se apagan.
/// </summary>
[DisallowMultipleComponent]
public class DebugSpeedTool : MonoBehaviour
{
    public static DebugSpeedTool Active { get; private set; }

    /// <summary>1 cuando la herramienta está apagada. Si no, el factor de locomoción del jugador.</summary>
    public static float LocomotionScale => ReadScale(tool => tool.Scale);

    /// <summary>1 cuando la herramienta está apagada. Si no, el factor de velocidad de proyectiles.</summary>
    public static float ProjectileScale => ReadScale(tool => tool.ProjectileSpeedScale);

    /// <summary>1 cuando la herramienta está apagada. Si no, el factor de avance de los enemigos.</summary>
    public static float EnemyScale => ReadScale(tool => tool.EnemySpeedScale);

    private static float ReadScale(System.Func<DebugSpeedTool, float> pick)
    {
        DebugSpeedTool tool = Active;
        if (tool == null || !tool.isActiveAndEnabled || !tool._applying)
            return 1f;

        return pick(tool);
    }

    [SerializeField, Range(0.5f, 2.5f)]
    [Tooltip("1 = valores de autoría. 1.5 = un 50% más de carrera, aceleración, fricción y dash. No es Time.timeScale.")]
    private float _scale = 1.5f;

    [SerializeField, Range(0.5f, 2.5f)]
    [Tooltip("Velocidad de viaje de los proyectiles del jugador y de los enemigos. No cambia la cadencia de disparo.")]
    private float _projectileScale = 1f;

    [SerializeField, Range(0.5f, 2.5f)]
    [Tooltip("Avance de los enemigos (persecución, drones, gusano, carga). No cambia su cadencia de ataque.")]
    private float _enemyScale = 1f;

    [SerializeField, Tooltip("Panel de calibración. Arranca oculto; el menú de pausa de desarrollo lo abre.")]
    private bool _showPanel;

    private bool _applying;
    private PlayerMovement _movement;
    private PlayerStats _stats;

    public bool IsApplying => _applying;
    public float Scale
    {
        get => Mathf.Clamp(_scale, 0.5f, 2.5f);
        set => _scale = Mathf.Clamp(value, 0.5f, 2.5f);
    }

    public float ProjectileSpeedScale
    {
        get => Mathf.Clamp(_projectileScale, 0.5f, 2.5f);
        set => _projectileScale = Mathf.Clamp(value, 0.5f, 2.5f);
    }

    public float EnemySpeedScale
    {
        get => Mathf.Clamp(_enemyScale, 0.5f, 2.5f);
        set => _enemyScale = Mathf.Clamp(value, 0.5f, 2.5f);
    }

    public void SetApplying(bool value) => _applying = value;

    public bool IsPanelOpen => _showPanel;

    public void SetPanelOpen(bool open)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _showPanel = open;
#else
        _showPanel = false;
#endif
    }

    private void OnEnable()
    {
        Active = this;
        _applying = false;
        _showPanel = false;
    }

    private void OnDisable()
    {
        _applying = false;
        if (Active == this)
            Active = null;
    }

    private void Update()
    {
        if (_movement == null)
            _movement = FindAnyObjectByType<PlayerMovement>();
        if (_stats == null && _movement != null)
            _stats = _movement.GetComponent<PlayerStats>();
    }

    /// <summary>
    /// Tres columnas del submenú: valor de autoría y el que quedaría con el factor.
    /// En enemigos hay una fila por tipo de prefab, no por instancia viva.
    /// </summary>
    public void FillColumns(out string player, out string projectiles, out string enemies)
    {
        ResolveTargets();
        var sb = new System.Text.StringBuilder(512);
        float move = _stats != null ? _stats.GetStat(StatType.MovementSpeed) : 0f;
        float dash = _stats != null ? _stats.GetStat(StatType.DashSpeed) : 0f;
        float accel = _movement != null ? _movement.AuthoredMoveAcceleration : 0f;
        float friction = _movement != null ? _movement.AuthoredFriction : 0f;
        AppendRow(sb, "Tope de carrera", move, Scale, "m/s");
        AppendRow(sb, "Aceleración", accel, Scale, "m/s²");
        AppendRow(sb, "Fricción", friction, Scale, "m/s²");
        AppendRow(sb, "Impulso de dash", dash, Scale, "m/s");
        player = sb.ToString();

        sb.Clear();
        AppendProjectileRows(sb, ProjectileSpeedScale);
        projectiles = sb.ToString();

        sb.Clear();
        AppendEnemyTypeRows(sb, EnemySpeedScale);
        enemies = sb.ToString();
    }

    private void ResolveTargets()
    {
        if (_movement == null)
            _movement = FindAnyObjectByType<PlayerMovement>();
        if (_stats == null && _movement != null)
            _stats = _movement.GetComponent<PlayerStats>();
    }

    private static void AppendProjectileRows(System.Text.StringBuilder sb, float scale)
    {
        Projectile[] shots = FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var seen = new System.Collections.Generic.HashSet<string>();
        int shown = 0;
        for (int i = 0; i < shots.Length && shown < 6; i++)
        {
            Projectile shot = shots[i];
            if (shot == null)
                continue;

            string label = TrimName(shot.name);
            string key = label + shot.AuthoredSpeed.ToString("0.00");
            if (!seen.Add(key))
                continue;

            AppendRow(sb, label, shot.AuthoredSpeed, scale, "m/s");
            shown++;
        }

        if (shown == 0)
            AppendRow(sb, "Bala estándar", 18f, scale, "m/s");
    }

    private static void AppendEnemyTypeRows(System.Text.StringBuilder sb, float scale)
    {
        var prefabs = new System.Collections.Generic.List<GameObject>(24);
        EnemySpawnRouletteConfig[] configs = Resources.FindObjectsOfTypeAll<EnemySpawnRouletteConfig>();
        for (int i = 0; i < configs.Length; i++)
        {
            EnemySpawnRouletteConfig config = configs[i];
            if (config == null || config.Entries == null)
                continue;

            EnemySpawnRouletteConfig.Entry[] entries = config.Entries;
            for (int e = 0; e < entries.Length; e++)
            {
                EnemySpawnRouletteConfig.Entry entry = entries[e];
                if (entry != null && entry.Prefab != null)
                    prefabs.Add(entry.Prefab);
            }
        }

        OverheatEliteWaveSpawner[] eliteWaves = FindObjectsByType<OverheatEliteWaveSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < eliteWaves.Length; i++)
        {
            if (eliteWaves[i] != null)
                eliteWaves[i].CollectElitePrefabs(prefabs);
        }

        BossManager[] bosses = FindObjectsByType<BossManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                bosses[i].CollectBossPrefabs(prefabs);
        }

        var seen = new System.Collections.Generic.HashSet<int>();
        int shown = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null || !seen.Add(prefab.GetInstanceID()))
                continue;
            if (!TryAuthoredMoveSpeed(prefab, out float speed))
                continue;

            AppendRow(sb, TypeName(prefab.name), speed, scale, "m/s");
            shown++;
        }

        if (shown == 0)
            AppendLiveEnemyTypes(sb, scale);
    }

    private static bool TryAuthoredMoveSpeed(GameObject prefab, out float speed)
    {
        BomberDroneBehavior bomber = prefab.GetComponentInChildren<BomberDroneBehavior>(true);
        if (bomber != null)
        {
            speed = bomber.AuthoredMoveSpeed;
            return true;
        }

        FlyingRangedBehavior flying = prefab.GetComponentInChildren<FlyingRangedBehavior>(true);
        if (flying != null)
        {
            speed = flying.AuthoredMoveSpeed;
            return true;
        }

        GigaWormBehavior worm = prefab.GetComponentInChildren<GigaWormBehavior>(true);
        if (worm != null)
        {
            speed = worm.AuthoredMoveSpeed;
            return true;
        }

        SimpleFollow simple = prefab.GetComponentInChildren<SimpleFollow>(true);
        if (simple != null)
        {
            speed = simple.AuthoredMoveSpeed;
            return true;
        }

        EnemyFollow follow = prefab.GetComponentInChildren<EnemyFollow>(true);
        if (follow != null)
        {
            speed = follow.AuthoredMoveSpeed;
            return true;
        }

        speed = 0f;
        return false;
    }

    private static void AppendLiveEnemyTypes(System.Text.StringBuilder sb, float scale)
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        int shown = 0;
        BomberDroneBehavior[] bombers = FindObjectsByType<BomberDroneBehavior>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < bombers.Length; i++)
            ConsiderType(sb, scale, seen, bombers[i] != null ? bombers[i].name : null, bombers[i] != null ? bombers[i].AuthoredMoveSpeed : 0f, ref shown);

        FlyingRangedBehavior[] flying = FindObjectsByType<FlyingRangedBehavior>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < flying.Length; i++)
            ConsiderType(sb, scale, seen, flying[i] != null ? flying[i].name : null, flying[i] != null ? flying[i].AuthoredMoveSpeed : 0f, ref shown);

        GigaWormBehavior[] worms = FindObjectsByType<GigaWormBehavior>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < worms.Length; i++)
            ConsiderType(sb, scale, seen, worms[i] != null ? worms[i].name : null, worms[i] != null ? worms[i].AuthoredMoveSpeed : 0f, ref shown);

        SimpleFollow[] simple = FindObjectsByType<SimpleFollow>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < simple.Length; i++)
            ConsiderType(sb, scale, seen, simple[i] != null ? simple[i].name : null, simple[i] != null ? simple[i].AuthoredMoveSpeed : 0f, ref shown);

        EnemyFollow[] follows = FindObjectsByType<EnemyFollow>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < follows.Length; i++)
            ConsiderType(sb, scale, seen, follows[i] != null ? follows[i].name : null, follows[i] != null ? follows[i].AuthoredMoveSpeed : 0f, ref shown);

        if (shown == 0)
            sb.AppendLine("Sin tipos de enemigo cargados.");
    }

    private static void ConsiderType(System.Text.StringBuilder sb, float scale, System.Collections.Generic.HashSet<string> seen, string rawName, float speed, ref int shown)
    {
        if (string.IsNullOrEmpty(rawName))
            return;

        string label = TypeName(rawName);
        if (!seen.Add(label))
            return;

        AppendRow(sb, label, speed, scale, "m/s");
        shown++;
    }

    private static string TypeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Enemigo";

        const string clone = "(Clone)";
        int index = name.IndexOf(clone, System.StringComparison.Ordinal);
        if (index >= 0)
            name = name.Substring(0, index);

        name = name.Replace('_', ' ').Trim();
        if (name.Length > 16)
            name = name.Substring(0, 16);
        return name;
    }

    private static string TrimName(string name)
    {
        return TypeName(name);
    }

    private static void AppendRow(System.Text.StringBuilder sb, string label, float authored, float scale, string unit)
    {
        sb.Append(label.PadRight(16));
        sb.Append(authored.ToString("0.00").PadLeft(6));
        sb.Append(" → ");
        sb.Append((authored * scale).ToString("0.00").PadLeft(6));
        sb.Append(' ');
        sb.AppendLine(unit);
    }
}
