using TMPro;
using UnityEngine;

/// <summary>
/// HUD de llaves, carga de puerta y presión de salida (sin flecha offscreen).
/// </summary>
[DisallowMultipleComponent]
public class LevelExitHud : MonoBehaviour
{
    [SerializeField] private LevelExitObjective _exitObjective;
    [SerializeField] private ExitDoor _exitDoor;
    [SerializeField] private LevelExitPressure _exitPressure;

    private TextMeshProUGUI _statusText;
    private GameObject _root;

    private void Awake()
    {
        ResolveRefs();
        if (!TryWireFromHierarchy())
            BuildUi();
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
        Transform exitHud = transform.Find("LevelExitHud");
        if (exitHud == null)
            return false;

        _root = exitHud.gameObject;
        _statusText = HudUiWire.FindTmp(exitHud, "Text");
        return _statusText != null;
    }

    private void BuildUi()
    {
        _root = new GameObject("LevelExitHud", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, -156f);
        rootRt.sizeDelta = new Vector2(820f, 44f);

        _statusText = HudUiFactory.CreateLabel(_root.transform, "Text", string.Empty, 24f, TextAlignmentOptions.Center);
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.color = new Color(0.95f, 0.85f, 0.35f, 1f);
    }

    private void Refresh()
    {
        if (_statusText == null || _root == null)
            return;

        if (_exitObjective == null)
        {
            _root.SetActive(false);
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.Ready)
        {
            _root.SetActive(true);
            _statusText.text = "Salida lista — acercate y pulsa [E] para salir";
            _statusText.color = new Color(0.4f, 1f, 0.55f, 1f);
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.AwaitingActivation)
        {
            _root.SetActive(true);
            _statusText.text = "¡Salida desbloqueada! Acercate a la puerta y pulsa [E]";
            _statusText.color = new Color(0.4f, 1f, 0.55f, 1f);
            return;
        }

        if (_exitDoor != null && _exitDoor.State == ExitDoorState.Charging)
        {
            _root.SetActive(true);
            float pct = Mathf.RoundToInt(_exitDoor.ChargeNormalized * 100f);
            _statusText.text = $"Abriendo salida… {pct}%";
            _statusText.color = new Color(0.55f, 0.9f, 1f, 1f);
            return;
        }

        if (_exitPressure != null && _exitPressure.IsActive)
        {
            _root.SetActive(true);
            int mult = Mathf.RoundToInt(_exitPressure.CurrentSpawnMultiplier);
            _statusText.text = $"¡Salida desbloqueada! Oleadas x{mult} — encontrá la puerta";
            _statusText.color = new Color(1f, 0.45f, 0.3f, 1f);
            return;
        }

        if (!_exitObjective.AllKeysCollected)
        {
            _root.SetActive(true);
            _statusText.text = $"Llaves: {_exitObjective.KeysCollected}/{_exitObjective.KeysRequired}";
            _statusText.color = new Color(0.95f, 0.85f, 0.35f, 1f);
            return;
        }

        _root.SetActive(false);
    }
}
