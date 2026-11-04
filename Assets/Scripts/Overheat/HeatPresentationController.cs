using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Owns cosmetic heat feedback only. Never writes to either gameplay manager.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(120)]
public sealed class HeatPresentationController : MonoBehaviour
{
    [Header("Sources (empty = discover at startup)")]
    [SerializeField] private HeatManager _heatManager;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private PlayerHealth _playerHealth;
    [Header("Authored presentation")]
    // Retained only to hide labels serialized by the previous HUD, including live instances.
    [SerializeField, HideInInspector] private TextMeshProUGUI _statusLabel;
    [SerializeField] private VolumeProfile _screenProfile;
    [SerializeField] private Material _edgeMaterial;
    [SerializeField, Range(0f, 1f)] private float _screenIntensity = .95f;
    [SerializeField, Min(.1f)] private float _transitionSeconds = .7f;

    private readonly HeatPresentationState _state = new();
    private HeatManager _boundHeat;
    private OverheatManager _boundOverheat;
    private PlayerHealth _boundHealth;
    private UserSettingsService _settings;
    private GameObject _runtimeRoot;
    private Volume _volume;
    private VolumeProfile _runtimeProfile;
    private Material _runtimeEdgeMaterial;
    private RawImage _edgeImage;
    private float _edgeWeight;
    private float _edgeTime;
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EffectTimeId = Shader.PropertyToID("_EffectTime");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private bool _reducedMotion;
    private bool _reducedFlash;
    private bool _suppressed;
    private float _screenWeight;
    private float _warningEmphasis;
    private float _nextResolveTime;
    private float _pulseTime;

    public HeatPresentationPhase Phase => _state.Phase;
    public float DisplayedHeat => _state.Heat;
    public float ScreenWeight => _volume != null ? _volume.weight : 0f;
    public bool ReducedMotion => _reducedMotion;
    public bool ReducedFlash => _reducedFlash;

    private void OnEnable()
    {
        HideLegacyLabel();
        ResolveSources();
        EnsureOutputs();
        PresentationAccessibilityRuntime.Changed += OnAccessibilityChanged;
        UserSettingsService.InstanceChanged += BindSettings;
        BindSettings(UserSettingsService.Instance);
        _suppressed = false;
        _state.Reset();
        ReadSources(false);
    }

    private void Start()
    {
        ResolveSources();
        ReadSources(false);
    }

    private void OnDisable()
    {
        UnbindSources();
        PresentationAccessibilityRuntime.Changed -= OnAccessibilityChanged;
        UserSettingsService.InstanceChanged -= BindSettings;
        if (_settings != null) _settings.Changed -= OnUserSettingsChanged;
        _settings = null;
        ClearFeedback();
        if (_statusLabel != null) _statusLabel.enabled = false;
        if (_runtimeRoot != null) _runtimeRoot.SetActive(false);
#if UNITY_EDITOR
        _preview = false;
#endif
    }

    private void OnDestroy()
    {
        if (_volume != null) { _volume.enabled = false; _volume.profile = null; }
        if (_runtimeProfile != null)
        {
            foreach (VolumeComponent component in _runtimeProfile.components) CoreUtils.Destroy(component);
            CoreUtils.Destroy(_runtimeProfile);
        }
        if (_edgeImage != null) _edgeImage.material = null;
        if (_runtimeEdgeMaterial != null) CoreUtils.Destroy(_runtimeEdgeMaterial);
        if (_runtimeRoot != null) CoreUtils.Destroy(_runtimeRoot);
    }

    private void ResolveSources()
    {
        if (_heatManager == null) _heatManager = HeatManager.GetInstance();
        if (_overheatManager == null) _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_playerHealth == null) _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (_boundHeat == _heatManager && _boundOverheat == _overheatManager && _boundHealth == _playerHealth) return;
        UnbindSources();
        _boundHeat = _heatManager;
        _boundOverheat = _overheatManager;
        _boundHealth = _playerHealth;
        if (_boundHeat != null) _boundHeat.OnHeatChanged += OnHeatChanged;
        if (_boundOverheat != null)
        {
            _boundOverheat.OnOverheatStarted += OnOverheatStarted;
            _boundOverheat.OnOverheatFinished += OnOverheatFinished;
        }
        if (_boundHealth != null)
        {
            _boundHealth.OnPlayerDied += OnPlayerDied;
            _boundHealth.OnReset += OnPlayerReset;
        }
    }

    private void UnbindSources()
    {
        if (_boundHeat != null) _boundHeat.OnHeatChanged -= OnHeatChanged;
        if (_boundOverheat != null)
        {
            _boundOverheat.OnOverheatStarted -= OnOverheatStarted;
            _boundOverheat.OnOverheatFinished -= OnOverheatFinished;
        }
        if (_boundHealth != null)
        {
            _boundHealth.OnPlayerDied -= OnPlayerDied;
            _boundHealth.OnReset -= OnPlayerReset;
        }
        _boundHeat = null;
        _boundOverheat = null;
        _boundHealth = null;
    }

    private void BindSettings(UserSettingsService service)
    {
        if (_settings != null) _settings.Changed -= OnUserSettingsChanged;
        _settings = service;
        if (_settings != null) _settings.Changed += OnUserSettingsChanged;
        ApplyAccessibility();
    }

    private void OnUserSettingsChanged(UserSettingsChange _) => ApplyAccessibility();
    private void OnAccessibilityChanged(PresentationAccessibilityState _) => ApplyAccessibility();

    private void ApplyAccessibility()
    {
        PresentationAccessibilityState accessibility = PresentationAccessibilityRuntime.Current;
        _reducedMotion = accessibility.ReducedMotion || (_settings != null && _settings.ReducedMotion);
        _reducedFlash = accessibility.ReducedFlash || (_settings != null && !_settings.ScreenFlash);
        if (_reducedMotion || _reducedFlash)
        {
            _warningEmphasis = 0f;
        }
        // Apply limits immediately, including while the options menu has paused gameplay.
        ApplyScreenWeight();
    }

    private bool RunActive => !_suppressed && _playerHealth != null &&
        _playerHealth.isActiveAndEnabled && _playerHealth.IsAlive &&
        (GameManager.Instance == null || GameManager.Instance.IsPlaying);
    private bool Paused => GameplayPause.IsUiPaused || Time.timeScale <= 0f || EntranceCinematic.StartupHeld;

    private void OnHeatChanged() { ReadSources(true); }
    private void OnOverheatStarted()
    {
        ReadSources(false);
        _warningEmphasis = 0f;
    }
    private void OnOverheatFinished(OverheatEndReason _)
    {
        ReadSources(false);
    }
    private void OnPlayerDied() { _suppressed = true; ClearFeedback(); }
    private void OnPlayerReset()
    {
        _suppressed = false;
        _state.Reset();
        ClearFeedback();
        ReadSources(false);
    }

    private void ReadSources(bool notify)
    {
#if UNITY_EDITOR
        if (_preview) return;
#endif
        bool overheated = _overheatManager != null && _overheatManager.IsOverheating;
        bool warning = _state.Read(_heatManager != null ? _heatManager.NormalizedHeat : 0f,
            overheated, _heatManager != null && _heatManager.IsPostOverheatDecayActive, notify);
        if (warning && RunActive && !Paused && !_reducedMotion && !_reducedFlash) _warningEmphasis = 1f;
    }

    private void Update()
    {
        if ((_heatManager == null || _overheatManager == null || _playerHealth == null) && Time.unscaledTime >= _nextResolveTime)
        {
            _nextResolveTime = Time.unscaledTime + .5f;
            ResolveSources();
            ReadSources(false);
        }
        // Also sample phase flags: the manager sets its cooldown flag after raising OnHeatChanged.
        ReadSources(false);
        if (!RunActive)
        {
            ClearFeedback();
            return;
        }
        if (Paused) return;
        AdvanceFeedback(Mathf.Min(Time.unscaledDeltaTime, .1f));
    }

    private void AdvanceFeedback(float dt)
    {
        // Reveal both the tint and flames over the displayed warning range (80-100%).
        float heat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HeatPresentationState.WarningThreshold, 1f, _state.Heat));
        float edges = heat;
        float step = dt / Mathf.Max(.1f, _transitionSeconds);
        _screenWeight = Mathf.MoveTowards(_screenWeight, heat, step);
        _edgeWeight = Mathf.MoveTowards(_edgeWeight, edges, step);
        _warningEmphasis = Mathf.MoveTowards(_warningEmphasis, 0f, dt / 1.25f);
        if (!_reducedMotion && !_reducedFlash)
        {
            _pulseTime = Mathf.Repeat(_pulseTime + dt, 2.4f);
            _edgeTime += dt;
        }
        ApplyScreenWeight();
    }

    private void ApplyScreenWeight()
    {
        float pulse = 1f;
        bool danger = _state.Phase == HeatPresentationPhase.Warning || _state.Phase == HeatPresentationPhase.Overheated;
        if (danger && !_reducedMotion && !_reducedFlash)
        {
            // One gentle breath per 2.4 seconds; never a flash or camera movement.
            float amplitude = _state.Phase == HeatPresentationPhase.Overheated ? .14f : .07f;
            pulse -= amplitude * (.5f + .5f * Mathf.Cos(_pulseTime * Mathf.PI * 2f / 2.4f));
        }
        float weight = Mathf.Clamp01(_screenWeight + _warningEmphasis * .06f) * Mathf.Clamp01(_screenIntensity) * pulse;
        if (_volume != null) _volume.weight = _reducedFlash ? Mathf.Min(.2f, weight * .2f) : weight;
        if (_runtimeEdgeMaterial != null)
        {
            float edge = _edgeWeight * Mathf.Clamp01(_screenIntensity) * pulse;
            _runtimeEdgeMaterial.SetFloat(IntensityId, edge * (_reducedFlash ? .18f : 1f));
            _runtimeEdgeMaterial.SetFloat(EffectTimeId, _edgeTime);
            _runtimeEdgeMaterial.SetFloat(AspectId, (float)Screen.width / Mathf.Max(1, Screen.height));
        }
    }

    private void HideLegacyLabel()
    {
        if (_statusLabel != null) _statusLabel.gameObject.SetActive(false);
        // This exact child belonged to the removed percentage label, not the preparation warning.
        Transform legacy = transform.Find("HeatStatus");
        if (legacy != null) legacy.gameObject.SetActive(false);
    }

    private void EnsureOutputs()
    {
        if (_runtimeRoot != null) { _runtimeRoot.SetActive(true); return; }
        _runtimeRoot = new GameObject("Heat feedback (runtime)") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(_runtimeRoot, gameObject.scene);
        // The gameplay camera already renders postprocessing and includes Default in its volume mask.
        if (_screenProfile != null)
        {
            _volume = _runtimeRoot.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 20f;
            _volume.weight = 0f;
            _volume.sharedProfile = _screenProfile;
            _runtimeProfile = _volume.profile;
        }
        if (_edgeMaterial == null) return;
        _runtimeEdgeMaterial = new Material(_edgeMaterial) { hideFlags = HideFlags.DontSave };
        _runtimeEdgeMaterial.SetFloat(IntensityId, 0f);
        GameObject screen = new("Heat screen edges", typeof(RectTransform), typeof(Canvas));
        screen.transform.SetParent(_runtimeRoot.transform, false);
        Canvas canvas = screen.GetComponent<Canvas>();
        Canvas hudCanvas = GetComponentInParent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Behind the authored HUD, with no raycaster or input interception.
        canvas.sortingOrder = hudCanvas != null ? hudCanvas.rootCanvas.sortingOrder - 1 : 599;
        if (hudCanvas != null) canvas.targetDisplay = hudCanvas.targetDisplay;
        GameObject surface = new("Heated perimeter", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        surface.transform.SetParent(screen.transform, false);
        _edgeImage = surface.GetComponent<RawImage>();
        _edgeImage.texture = Texture2D.whiteTexture;
        _edgeImage.material = _runtimeEdgeMaterial;
        _edgeImage.raycastTarget = false;
        _edgeImage.maskable = false;
        RectTransform rect = _edgeImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void ClearFeedback()
    {
        _screenWeight = _edgeWeight = _warningEmphasis = _pulseTime = _edgeTime = 0f;
        ApplyScreenWeight();
    }

#if UNITY_EDITOR
    private bool _preview;
    /// <summary>Play-mode visual preview only; never changes heat, spawns, buffs or saved settings.</summary>
    public void PreviewInEditor(float heat, bool overheat, bool cooling)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        _preview = true;
        if (_state.Read(heat, overheat, cooling) && !_reducedMotion && !_reducedFlash) _warningEmphasis = 1f;
        if (UnityEditor.EditorApplication.isPaused && RunActive && !Paused)
        {
            AdvanceFeedback(.1f);
        }
    }
    public void EndEditorPreview()
    {
        _preview = false;
        _state.Reset();
        ClearFeedback();
        ReadSources(false);
    }
#endif
}
