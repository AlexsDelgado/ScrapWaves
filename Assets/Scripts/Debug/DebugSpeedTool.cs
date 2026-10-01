using UnityEngine;

/// <summary>
/// Calibración de ritmo para diseño. Multiplica la locomoción del jugador
/// solo mientras Play está activo y el interruptor está prendido.
/// No escribe stats, prefabs ni la escena: al salir de Play la escala se apaga.
/// </summary>
[DisallowMultipleComponent]
public class DebugSpeedTool : MonoBehaviour
{
    public static DebugSpeedTool Active { get; private set; }

    /// <summary>1 cuando la herramienta está apagada. Si no, el factor elegido.</summary>
    public static float LocomotionScale
    {
        get
        {
            DebugSpeedTool tool = Active;
            if (tool == null || !tool.isActiveAndEnabled || !tool._applying)
                return 1f;

            return tool.Scale;
        }
    }

    [SerializeField, Range(0.5f, 2.5f)]
    [Tooltip("1 = valores de autoría. 1.5 = un 50% más de carrera, aceleración, fricción y dash. No es Time.timeScale.")]
    private float _scale = 1.5f;

    [SerializeField, Tooltip("Panel en la esquina de Game view durante Play.")]
    private bool _showPanel = true;

    private bool _applying;
    private PlayerMovement _movement;
    private PlayerStats _stats;

    private static readonly float[] Presets = { 1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f, 2f };

    public bool IsApplying => _applying;
    public float Scale
    {
        get => Mathf.Clamp(_scale, 0.5f, 2.5f);
        set => _scale = Mathf.Clamp(value, 0.5f, 2.5f);
    }

    public void SetApplying(bool value) => _applying = value;

    private void OnEnable()
    {
        Active = this;
        _applying = false;
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

    private void OnGUI()
    {
        if (!Application.isPlaying || !_showPanel)
            return;

        const float width = 460f;
        Rect area = new Rect(12f, 12f, width, 340f);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label("DEBUG_SPEED  ·  ritmo del jugador");
        GUILayout.Label("No se guarda. Al salir de Play vuelve a x1.");

        bool next = GUILayout.Toggle(_applying, _applying ? "Escala ACTIVA" : "Escala apagada (x1, valores reales)");
        if (next != _applying)
            _applying = next;

        GUILayout.Label($"Factor {Scale:0.00}   ({PercentLabel(Scale)})");
        Scale = GUILayout.HorizontalSlider(Scale, 0.5f, 2.5f);

        GUILayout.BeginHorizontal();
        for (int i = 0; i < Presets.Length; i++)
        {
            float preset = Presets[i];
            string caption = preset.ToString("0.0");
            if (GUILayout.Button(caption, GUILayout.Width(50f)))
            {
                Scale = preset;
                _applying = preset > 1.001f;
            }
        }
        GUILayout.EndHorizontal();

        float live = LocomotionScale;
        GUILayout.Space(4f);
        DrawRow("Tope de carrera", Stat(StatType.MovementSpeed), "m/s", live);
        DrawRow("Aceleración", _movement != null ? _movement.AuthoredMoveAcceleration : 0f, "m/s²", live);
        DrawRow("Fricción", _movement != null ? _movement.AuthoredFriction : 0f, "m/s²", live);
        DrawRow("Impulso de dash", Stat(StatType.DashSpeed), "m/s", live);

        float cap = Stat(StatType.MovementSpeed) * live;
        float now = _movement != null ? _movement.PlanarSpeed : 0f;
        GUILayout.Label($"Velocidad ahora {now:0.00} / tope {cap:0.00} m/s");

        GUILayout.Space(2f);
        GUILayout.Label($"Sin tocar: salto {Stat(StatType.JumpHeight):0.00} m, dash {_movement?.AuthoredDashDuration ?? 0f:0.00} s, enemigos, disparos y timeScale.");
        GUILayout.EndArea();
    }

    private float Stat(StatType type) => _stats != null ? _stats.GetStat(type) : 0f;

    private static void DrawRow(string label, float authored, string unit, float live)
    {
        GUILayout.Label($"{label}: {authored:0.00} {unit}  →  {authored * live:0.00}");
    }

    private static string PercentLabel(float scale)
    {
        float delta = (scale - 1f) * 100f;
        if (delta >= 0f)
            return $"+{delta:0}%";
        return $"{delta:0}%";
    }
}
