using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ExitDoorState
{
    /// <summary>Keys are missing.</summary>
    Locked,
    /// <summary>All keys collected; the player must press [E] to start charging.</summary>
    AwaitingActivation,
    /// <summary>Charging after the first interaction.</summary>
    Charging,
    /// <summary>Charge complete; the player presses [E] to exit and win.</summary>
    Ready,
    /// <summary>Level completed.</summary>
    Used
}

/// <summary>
/// Puerta de salida del nivel.
/// Flujo: Locked → AwaitingActivation (llaves recogidas) → Charging ([E] primera vez)
///        → Ready (carga completa) → Used ([E] segunda vez) → victoria.
/// </summary>
[DisallowMultipleComponent]
public class ExitDoor : MonoBehaviour
{
    [SerializeField, Min(0.1f), Tooltip("Segundos que tarda la carga tras activar la puerta con [E].")]
    private float _chargeDurationSeconds = 5f;

    [SerializeField, Min(0.5f), Tooltip("Distancia al jugador para interactuar con [E].")]
    private float _interactionRadius = 3f;

    [SerializeField, Tooltip("Punto de interacción. Vacío = posición de este transform.")]
    private Transform _interactionPoint;

    [SerializeField] private LevelExitObjective _exitObjective;
    [SerializeField] private CraftingUI _craftingUi;
    [SerializeField] private LevelUpChoiceUI _levelUpChoiceUi;

    private ExitDoorState _state = ExitDoorState.Locked;
    private float _chargeRemaining;

    public ExitDoorState State => _state;
    public float ChargeNormalized => _chargeDurationSeconds > 0f
        ? 1f - Mathf.Clamp01(_chargeRemaining / _chargeDurationSeconds)
        : 1f;

    public event Action OnDoorUnlocked;
    public event Action OnChargeStarted;
    public event Action<float> OnChargeProgress;
    public event Action OnDoorReady;

    private void Awake()
    {
        if (_exitObjective == null)
            _exitObjective = FindAnyObjectByType<LevelExitObjective>();
        if (_craftingUi == null)
            _craftingUi = FindAnyObjectByType<CraftingUI>();
        if (_levelUpChoiceUi == null)
            _levelUpChoiceUi = FindAnyObjectByType<LevelUpChoiceUI>();
    }

    private void OnEnable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected += HandleAllKeysCollected;
    }

    private void OnDisable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected -= HandleAllKeysCollected;
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        // No reaccionar a [E] mientras haya una UI modal abierta (Crafting / Level-up choice),
        // igual que hace PauseMenuUI.CanPause() con Escape.
        if ((_craftingUi != null && _craftingUi.IsVisible) || (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible))
            return;

        if (_state != ExitDoorState.AwaitingActivation && _state != ExitDoorState.Ready)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return;

        Vector3 point = _interactionPoint != null ? _interactionPoint.position : transform.position;
        Vector3 flat = player.position - point;
        flat.y = 0f;
        if (flat.magnitude > _interactionRadius)
            return;

        if (!WasInteractPressed())
            return;

        if (_state == ExitDoorState.AwaitingActivation)
        {
            StartCoroutine(ChargeRoutine());
        }
        else if (_state == ExitDoorState.Ready)
        {
            _state = ExitDoorState.Used;
            GameManager.Instance?.TriggerVictory();
        }
    }

    private void HandleAllKeysCollected()
    {
        if (_state != ExitDoorState.Locked)
            return;

        _state = ExitDoorState.AwaitingActivation;
        OnDoorUnlocked?.Invoke();
    }

    private IEnumerator ChargeRoutine()
    {
        _state = ExitDoorState.Charging;
        _chargeRemaining = _chargeDurationSeconds;
        OnChargeStarted?.Invoke();

        while (_chargeRemaining > 0f)
        {
            _chargeRemaining -= Time.deltaTime;
            OnChargeProgress?.Invoke(ChargeNormalized);
            yield return null;
        }

        _chargeRemaining = 0f;
        _state = ExitDoorState.Ready;
        OnChargeProgress?.Invoke(1f);
        OnDoorReady?.Invoke();
    }

    private static bool WasInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 point = _interactionPoint != null ? _interactionPoint.position : transform.position;
        Gizmos.color = _state == ExitDoorState.Ready
            ? new Color(0.2f, 1f, 0.4f, 0.5f)
            : new Color(0.2f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireSphere(point, _interactionRadius);
    }
#endif
}
