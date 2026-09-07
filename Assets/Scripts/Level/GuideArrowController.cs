using System;
using UnityEngine;

/// <summary>
/// Orquesta cuándo se muestra <see cref="GuideArrow"/>:
/// 1) A los <see cref="_craftingStationDelaySeconds"/> de partida, apunta a la crafting station.
/// 2) Al juntar todas las llaves (<see cref="LevelExitObjective.OnAllKeysCollected"/>), apunta a la puerta de salida.
/// Cada aparición dura <see cref="_guideDurationSeconds"/> o hasta que el jugador interactúe con el
/// objetivo (lo que ocurra primero). Cada disparador ocurre una única vez por partida.
/// </summary>
[DisallowMultipleComponent]
public class GuideArrowController : MonoBehaviour
{
    [SerializeField] private GuideArrow _guideArrow;
    [SerializeField] private CraftingStation _craftingStation;
    [SerializeField] private ExitDoor _exitDoor;
    [SerializeField] private LevelExitObjective _exitObjective;

    [SerializeField, Min(0f), Tooltip("Segundos de partida antes de mostrar la flecha hacia la crafting station.")]
    private float _craftingStationDelaySeconds = 20f;

    [SerializeField, Min(0f), Tooltip("Cuánto dura cada aparición de la flecha (o hasta interactuar con el objetivo, lo que pase antes).")]
    private float _guideDurationSeconds = 5f;

    private bool _craftingHintShown;
    private bool _doorHintShown;
    private float _hideAtTime = -1f;
    private Action _activeDismissUnsubscribe;

    private void Awake()
    {
        if (_guideArrow == null)
            _guideArrow = GetComponent<GuideArrow>() ?? FindAnyObjectByType<GuideArrow>();
        if (_craftingStation == null)
            _craftingStation = FindAnyObjectByType<CraftingStation>();
        if (_exitDoor == null)
            _exitDoor = FindAnyObjectByType<ExitDoor>();
        if (_exitObjective == null)
            _exitObjective = LevelExitObjective.Instance != null
                ? LevelExitObjective.Instance
                : FindAnyObjectByType<LevelExitObjective>();
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

        EndGuide();
    }

    private void Update()
    {
        if (!_craftingHintShown
            && _craftingStation != null
            && RunSessionStats.ElapsedSeconds >= _craftingStationDelaySeconds)
        {
            _craftingHintShown = true;
            BeginGuide(
                _craftingStation.transform,
                dismiss =>
                {
                    void Handler() => dismiss();
                    _craftingStation.OnInteracted += Handler;
                    return () => _craftingStation.OnInteracted -= Handler;
                });
        }

        if (_hideAtTime >= 0f && Time.unscaledTime >= _hideAtTime)
            EndGuide();
    }

    private void HandleAllKeysCollected()
    {
        if (_doorHintShown || _exitDoor == null)
            return;

        _doorHintShown = true;
        BeginGuide(
            _exitDoor.transform,
            dismiss =>
            {
                void Handler() => dismiss();
                _exitDoor.OnChargeStarted += Handler;
                return () => _exitDoor.OnChargeStarted -= Handler;
            });
    }

    /// <summary>
    /// Arranca una aparición de la flecha. <paramref name="subscribeDismiss"/> se suscribe al evento
    /// de interacción del objetivo y devuelve un Action para desuscribirse.
    /// </summary>
    private void BeginGuide(Transform target, Func<Action, Action> subscribeDismiss)
    {
        if (_guideArrow == null || target == null)
            return;

        EndGuide();

        _guideArrow.Show(target);
        _hideAtTime = Time.unscaledTime + _guideDurationSeconds;

        bool dismissed = false;
        Action dismiss = () =>
        {
            if (dismissed)
                return;
            dismissed = true;
            EndGuide();
        };

        _activeDismissUnsubscribe = subscribeDismiss(dismiss);
    }

    private void EndGuide()
    {
        _hideAtTime = -1f;

        Action unsub = _activeDismissUnsubscribe;
        _activeDismissUnsubscribe = null;
        unsub?.Invoke();

        if (_guideArrow != null)
            _guideArrow.Hide();
    }
}
