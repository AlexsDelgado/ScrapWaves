using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Overlay de desarrollo: FPS, conteos, heat, nivel; pausa / time scale; log en pantalla.
/// Desactivado por defecto: pulsa <see cref="_toggleKey"/> o usa <see cref="SetToolsVisible"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class DebugUI : MonoBehaviour
{
    public static DebugUI Instance { get; private set; }

    [SerializeField, Tooltip("Mostrar panel al iniciar (solo en Editor si solo Editor Start On).")]
    private bool _visibleOnPlay;

    [SerializeField] private Key _toggleKey = Key.F3;
    [SerializeField] private Key _pauseKey = Key.P;
    [SerializeField] private Key _timeSlowerKey = Key.NumpadMinus;
    [SerializeField] private Key _timeFasterKey = Key.NumpadPlus;

    [SerializeField, Min(1), Tooltip("Líneas visibles en el bloque de log.")]
    private int _maxLogLines = 8;

    [SerializeField] private bool _mirrorLogsToUnityConsole = true;

    [SerializeField, Tooltip("Suscribe eventos de Overheat / subida de nivel al log automático.")]
    private bool _autoLogImportantGameEvents;

    [SerializeField, Min(8), Tooltip("Actualizar bloque de stats cada N frames (FPS sigue suavizado cada frame).")]
    private int _statsRefreshIntervalFrames = 3;

    [SerializeField] private Color _panelColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color _textColor = new Color(0.85f, 1f, 0.85f, 1f);

    private bool _toolsVisible;
    private Canvas _canvas;
    private TextMeshProUGUI _statsText;
    private TextMeshProUGUI _logText;
    private TextMeshProUGUI _hintText;

    private readonly StringBuilder _sb = new StringBuilder(384);
    private readonly Queue<string> _logLines = new Queue<string>(16);
    private static readonly Queue<string> s_pendingLogs = new Queue<string>(32);

    private float _fpsSmoothed = 60f;
    private int _frameCounter;
    private HeatManager _heat;
    private PlayerXP _playerXp;
    private ProjectilePool _projectiles;
    private OverheatManager _overheatManager;
    private PlayerXP _subscribedXp;

    private float _savedTimeScale = 1f;
    private readonly float[] _timeScaleSteps = { 0.5f, 1f, 1.5f, 2f, 3f };
    private int _timeScaleStepIndex = 1;

    private void Awake()
    {
        Instance = this;
        _heat = HeatManager.GetInstance();
        _playerXp = FindAnyObjectByType<PlayerXP>();
        _projectiles = FindAnyObjectByType<ProjectilePool>();
        _overheatManager = FindAnyObjectByType<OverheatManager>();

        _toolsVisible = _visibleOnPlay;
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        UnhookGameEvents();
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        BuildUi();
        ApplyVisibility();
        FlushPendingLogs();
        if (_autoLogImportantGameEvents)
            HookGameEvents();
    }

    private void OnDestroy()
    {
        UnhookGameEvents();
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current[_toggleKey].wasPressedThisFrame)
        {
            _toolsVisible = !_toolsVisible;
            ApplyVisibility();
        }

        if (!_toolsVisible)
            return;

        if (Keyboard.current[_pauseKey].wasPressedThisFrame)
            TogglePause();

        if (Keyboard.current[_timeSlowerKey].wasPressedThisFrame)
            StepTimeScale(-1);

        if (Keyboard.current[_timeFasterKey].wasPressedThisFrame)
            StepTimeScale(1);
    }

    private void LateUpdate()
    {
        if (!_toolsVisible)
            return;

        float udt = Time.unscaledDeltaTime;
        if (udt > 0.0001f)
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / udt, 0.15f);

        _frameCounter++;
        if (_frameCounter >= _statsRefreshIntervalFrames)
        {
            _frameCounter = 0;
            RefreshStatsText();
        }
    }

    /// <summary>Muestra u oculta todo el panel de debug (tecla configurable).</summary>
    public void SetToolsVisible(bool visible)
    {
        _toolsVisible = visible;
        ApplyVisibility();
    }

    public static void LogEvent(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        s_pendingLogs.Enqueue(message);
        if (Instance != null)
            Instance.FlushPendingLogs();
    }

    private void FlushPendingLogs()
    {
        while (s_pendingLogs.Count > 0)
            AppendLogLine(s_pendingLogs.Dequeue());
    }

    private void AppendLogLine(string line)
    {
        if (_mirrorLogsToUnityConsole)
            Debug.Log("[DebugUI] " + line, this);

        _logLines.Enqueue(line);
        while (_logLines.Count > _maxLogLines)
            _logLines.Dequeue();

        if (_logText != null)
            RebuildLogDisplay();
    }

    private void RebuildLogDisplay()
    {
        _sb.Clear();
        foreach (string s in _logLines)
        {
            _sb.AppendLine(s);
        }

        _logText.text = _sb.ToString();
    }

    private void RefreshStatsText()
    {
        if (_statsText == null)
            return;

        int enemies = EnemyRegistry.ActiveCount;
        int proj = _projectiles != null ? _projectiles.ActiveLeasedCount : -1;

        float heat = _heat != null ? _heat.CurrentHeat : -1f;
        float heatMax = _heat != null ? _heat.MaxHeat : 0f;
        int level = _playerXp != null ? _playerXp.CurrentLevel : -1;

        string gm = "";
        if (GameManager.Instance != null)
            gm = $" | Run: {GameManager.Instance.State}";

        _sb.Clear();
        _sb.Append("FPS: ").Append(_fpsSmoothed.ToString("0"));
        _sb.Append("\nEnemies (registry): ").Append(enemies);
        _sb.Append("\nProjectiles (pool): ").Append(proj);
        _sb.Append("\nHeat: ").Append(heat.ToString("0.#")).Append(" / ").Append(heatMax.ToString("0.#"));
        _sb.Append("\nLevel: ").Append(level);
        _sb.Append("\nTimeScale: ").Append(Time.timeScale.ToString("0.##")).Append(gm);
        _statsText.text = _sb.ToString();
    }

    private void TogglePause()
    {
        if (Time.timeScale > 0.001f)
        {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            LogEvent("Pausa (debug)");
        }
        else
        {
            Time.timeScale = _savedTimeScale > 0.001f ? _savedTimeScale : 1f;
            LogEvent("Reanudar (debug)");
        }
    }

    private void StepTimeScale(int delta)
    {
        if (Time.timeScale < 0.001f)
            return;

        _timeScaleStepIndex = Mathf.Clamp(_timeScaleStepIndex + delta, 0, _timeScaleSteps.Length - 1);
        Time.timeScale = _timeScaleSteps[_timeScaleStepIndex];
        _savedTimeScale = Time.timeScale;
        LogEvent("TimeScale = " + Time.timeScale);
    }

    private void HookGameEvents()
    {
        UnhookGameEvents();
        _overheatManager = FindAnyObjectByType<OverheatManager>();

        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += OnDbgOverheatStart;
            _overheatManager.OnOverheatFinished += OnDbgOverheatEnd;
        }

        _subscribedXp = FindAnyObjectByType<PlayerXP>();
        if (_subscribedXp != null)
            _subscribedXp.OnLevelUp += OnDbgLevelUp;
    }

    private void UnhookGameEvents()
    {
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= OnDbgOverheatStart;
            _overheatManager.OnOverheatFinished -= OnDbgOverheatEnd;
        }

        if (_subscribedXp != null)
        {
            _subscribedXp.OnLevelUp -= OnDbgLevelUp;
            _subscribedXp = null;
        }
    }

    private void OnDbgOverheatStart() => LogEvent("Overheat started");

    private void OnDbgOverheatEnd(OverheatEndReason r) => LogEvent("Overheat ended: " + r);

    private void OnDbgLevelUp(int lv) => LogEvent("Level " + lv);

    private void BuildUi()
    {
        var go = new GameObject("DebugUI_Canvas");
        go.transform.SetParent(transform, false);
        int layer = LayerMask.NameToLayer("UI");
        if (layer >= 0)
            go.layer = layer;

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32000;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(go.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = new Vector2(420f, 320f);

        var bg = panel.AddComponent<Image>();
        bg.color = _panelColor;
        bg.raycastTarget = false;

        _statsText = CreateText(panel.transform, "Stats", new Vector2(-8f, -8f), 15, TextAnchor.UpperRight);
        _logText = CreateText(panel.transform, "Logs", new Vector2(-8f, -140f), 13, TextAnchor.UpperRight);
        _hintText = CreateText(panel.transform, "Hints", new Vector2(-8f, -280f), 11, TextAnchor.LowerRight);

        _hintText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        _hintText.text = $"{_toggleKey} panel | {_pauseKey} pausa | {_timeSlowerKey}/{_timeFasterKey} tiempo";

        RebuildLogDisplay();
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchoredPos, int fontSize, TextAnchor align)
    {
        var tgo = new GameObject(name);
        tgo.transform.SetParent(parent, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(1f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(1f, 1f);
        trt.anchoredPosition = anchoredPos;
        trt.sizeDelta = name == "Logs" ? new Vector2(400f, 130f) : new Vector2(400f, 110f);

        var txt = tgo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(txt);
        txt.fontSize = fontSize;
        txt.color = _textColor;
        txt.alignment = MapTextAnchorToTmp(align);
        txt.textWrappingMode = TextWrappingModes.Normal;
        txt.overflowMode = TextOverflowModes.Overflow;
        txt.raycastTarget = false;
        return txt;
    }

    private static TextAlignmentOptions MapTextAnchorToTmp(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.TopRight;
        }
    }

    private void ApplyVisibility()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(_toolsVisible);

        if (_toolsVisible)
            FlushPendingLogs();
    }
}
