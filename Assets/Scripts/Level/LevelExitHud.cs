using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD de llaves, carga de puerta y presión de salida (sin flecha offscreen).
/// </summary>
[DisallowMultipleComponent]
public class LevelExitHud : MonoBehaviour
{
    [SerializeField] private LevelExitObjective _exitObjective;
    [SerializeField] private ExitDoor _exitDoor;
    [SerializeField] private LevelExitPressure _exitPressure;

    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private GameObject _root;

    [Header("Status text")]
    [SerializeField, TextArea(1, 3)] private string _discoveryText = "Find a way to escape";
    [SerializeField, Tooltip("Available tokens: {collected}, {required}.")]
    private string _batteryProgressText = "Batteries: {collected}/{required}";
    [SerializeField, TextArea(1, 3)] private string _activationText = "¡Salida desbloqueada! Acercate a la puerta y pulsa [E]";
    [SerializeField, TextArea(1, 3), Tooltip("Available token: {progress}.")]
    private string _chargingText = "Abriendo: {progress}% - salta y atravesala";
    [SerializeField, TextArea(1, 3)] private string _readyText = "Salida abierta - atravesala para escapar";
    [SerializeField, TextArea(1, 3), Tooltip("Available token: {multiplier}.")]
    private string _pressureText = "¡Salida desbloqueada! Oleadas x{multiplier} — encontrá la puerta";

    [Header("Status colors")]
    [SerializeField] private Color _objectiveColor = new(0.95f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _unlockedColor = new(0.4f, 1f, 0.55f, 1f);
    [SerializeField] private Color _chargingColor = new(0.55f, 0.9f, 1f, 1f);
    [SerializeField] private Color _pressureColor = new(1f, 0.45f, 0.3f, 1f);

    private void Awake()
    {
        ResolveRefs();
        TryWireFromHierarchy();
        Refresh();
    }

    private void OnEnable()
    {
        ResolveRefs();
        Subscribe(true);
        Refresh();
    }

    private void OnDisable() => Subscribe(false);

    private void Update() => Refresh();

    private void ResolveRefs()
    {
        if (_exitObjective == null)
            _exitObjective = FindAnyObjectByType<LevelExitObjective>();
        if (_exitDoor == null)
            _exitDoor = FindAnyObjectByType<ExitDoor>();
        if (_exitPressure == null)
            _exitPressure = FindAnyObjectByType<LevelExitPressure>();
    }

    private void Subscribe(bool subscribe)
    {
        if (_exitObjective != null)
        {
            if (subscribe)
            {
                _exitObjective.OnKeyProgressChanged += OnKeyProgressChanged;
                _exitObjective.OnAllKeysCollected += Refresh;
            }
            else
            {
                _exitObjective.OnKeyProgressChanged -= OnKeyProgressChanged;
                _exitObjective.OnAllKeysCollected -= Refresh;
            }
        }

        if (_exitDoor != null)
        {
            if (subscribe)
            {
                _exitDoor.OnDoorUnlocked += Refresh;
                _exitDoor.OnChargeStarted += Refresh;
                _exitDoor.OnChargeProgress += OnDoorChargeProgress;
                _exitDoor.OnDoorReady += Refresh;
            }
            else
            {
                _exitDoor.OnDoorUnlocked -= Refresh;
                _exitDoor.OnChargeStarted -= Refresh;
                _exitDoor.OnChargeProgress -= OnDoorChargeProgress;
                _exitDoor.OnDoorReady -= Refresh;
            }
        }

        if (_exitPressure != null)
        {
            if (subscribe)
                _exitPressure.OnPressureTierChanged += OnPressureTierChanged;
            else
                _exitPressure.OnPressureTierChanged -= OnPressureTierChanged;
        }
    }

    private void OnKeyProgressChanged(int _, int __) => Refresh();
    private void OnDoorChargeProgress(float _) => Refresh();
    private void OnPressureTierChanged(float _) => Refresh();

    private bool TryWireFromHierarchy()
    {
        if (_root != null && _statusText != null) return true;
        Transform exitHud = transform.Find("LevelExitHud");
        if (exitHud == null)
            return false;

        _root = exitHud.gameObject;
        _statusText = HudUiWire.FindTmp(exitHud, "Text");
        return _statusText != null;
    }

#if UNITY_EDITOR
    public void AuthorUi(Transform uiRoot)
    {
        if (TryWireFromHierarchy()) return;
        Transform parent = GetComponentInParent<Canvas>() != null ? transform : uiRoot;
        if (parent == null || parent.GetComponentInParent<Canvas>() == null)
        {
            var canvasGo = new GameObject("LevelExitHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(uiRoot, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            parent = canvasGo.transform;
        }
        _root = new GameObject("LevelExitHud", typeof(RectTransform));
        _root.transform.SetParent(parent, false);
        var rootRt = _root.GetComponent<RectTransform>();
        _statusText = HudUiFactory.CreateLabel(_root.transform, "Text", string.Empty, 24f, TextAlignmentOptions.MidlineRight);
        ConfigureObjectivePanel(rootRt, _statusText);
        _statusText.color = _objectiveColor;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>Shared editor defaults for newly authored panels and explicit HUD art updates.</summary>
    public static void ConfigureObjectivePanel(RectTransform panel, TextMeshProUGUI text)
    {
        // The centered boss bar occupies the first row; the objective sits beside the dialogue below it.
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
        panel.anchoredPosition = new Vector2(-28f, -96f);
        panel.sizeDelta = new Vector2(360f, 88f);
        Image background = panel.GetComponent<Image>();
        if (background == null) background = panel.gameObject.AddComponent<Image>();
        background.sprite = HudUiFactory.WhiteSprite;
        background.color = new Color(0f, 0f, 0f, .55f);
        background.raycastTarget = false;

        if (text == null) return;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(16f, 10f);
        text.rectTransform.offsetMax = new Vector2(-16f, -10f);
        text.fontSize = 24f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20f;
        text.fontSizeMax = 24f;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
    }
#endif

    private void Refresh()
    {
        if (TutorialDirector.IsActive) { if (_root != null) _root.SetActive(false); return; }
        if (_statusText == null || _root == null)
            return;

        if (_exitObjective == null)
        {
            _root.SetActive(false);
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.Used)
        {
            _root.SetActive(false);
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.Ready)
        {
            _root.SetActive(true);
            _statusText.text = _readyText;
            _statusText.color = _unlockedColor;
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.AwaitingActivation)
        {
            _root.SetActive(true);
            _statusText.text = _activationText;
            _statusText.color = _unlockedColor;
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.Charging)
        {
            _root.SetActive(true);
            int pct = Mathf.RoundToInt(_exitDoor.ChargeNormalized * 100f);
            _statusText.text = (_chargingText ?? string.Empty).Replace("{progress}", pct.ToString());
            _statusText.color = _chargingColor;
            return;
        }

        if (_exitPressure != null && _exitPressure.IsActive)
        {
            _root.SetActive(true);
            int mult = Mathf.RoundToInt(_exitPressure.CurrentSpawnMultiplier);
            _statusText.text = (_pressureText ?? string.Empty).Replace("{multiplier}", mult.ToString());
            _statusText.color = _pressureColor;
            return;
        }

        if (!_exitObjective.AllKeysCollected)
        {
            _root.SetActive(true);
            _statusText.text = _exitObjective.KeysCollected == 0
                ? _discoveryText
                : (_batteryProgressText ?? string.Empty)
                    .Replace("{collected}", _exitObjective.KeysCollected.ToString())
                    .Replace("{required}", _exitObjective.KeysRequired.ToString());
            _statusText.color = _objectiveColor;
            return;
        }

        _root.SetActive(false);
    }
}
