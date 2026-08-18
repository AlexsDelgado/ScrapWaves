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
    private float _craftingStationDelaySeconds = 60f;

    [SerializeField, Min(0f), Tooltip("Cuánto dura cada aparición de la flecha (o hasta interactuar con el objetivo, lo que pase antes).")]
    private float _guideDurationSeconds = 10f;

    private bool _craftingHintShown;
    private bool _doorHintShown;
    private float _hideAtTime = -1f;
    private Action _activeDismissUnsubscribe;

    private void Awake()
    {
        if (_guideArrow == null)
            _guideArrow = FindAnyObjectByType<GuideArrow>();
        if (_craftingStation == null)
            _craftingStation = FindAnyObjectByType<CraftingStation>();
        if (_exitDoor == null)
            _exitDoor = FindAnyObjectByType<ExitDoor>();
        if (_exitObjective == null)
            _exitObjective = LevelExitObjective.Instance != null ? LevelExitObjective.Instance : FindAnyObjectByType<LevelExitObjective>();
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

        StopActiveGuide();
    }

    private void Update()
    {
        if (!_craftingHintShown && _craftingStation != null
            && RunSessionStats.ElapsedSeconds >= _craftingStationDelaySeconds)
        {
            _craftingHintShown = true;
            BeginGuide(_craftingStation.transform, unsub => _craftingStation.OnInteracted += () => unsub());
        }

        if (_hideAtTime >= 0f && Time.unscaledTime >= _hideAtTime)
            EndGuide();
    }

    private void HandleAllKeysCollected()
    {
        if (_doorHintShown || _exitDoor == null)
            return;

        _doorHintShown = true;
        BeginGuide(_exitDoor.transform, unsub => _exitDoor.OnChargeStarted += () => unsub());
    }

    /// <summary>
    /// Arranca una aparición de la flecha. <paramref name="subscribeDismiss"/> recibe un callback
    /// "unsub" que hay que invocar (desde el evento de interacción correspondiente) para cortar la
    /// guía antes de tiempo; se encarga de armar y desarmar esa suscripción sin duplicarla.
    /// </summary>
    private void BeginGuide(Transform target, Action<Action> subscribeDismiss)
    {
        if (_guideArrow == null || target == null)
            return;

        StopActiveGuide();

        _guideArrow.Show(target);
        _hideAtTime = Time.unscaledTime + _guideDurationSeconds;

        bool dismissed = false;
        Action unsub = null;
        unsub = () =>
        {
            if (dismissed)
                return;
            dismissed = true;
            EndGuide();
        };
        subscribeDismiss(unsub);
        _activeDismissUnsubscribe = unsub;
    }

    private void EndGuide()
    {
        _hideAtTime = -1f;
        _activeDismissUnsubscribe = null;
        if (_guideArrow != null)
            _guideArrow.Hide();
    }

    private void StopActiveGuide()
    {
        _hideAtTime = -1f;
        _activeDismissUnsubscribe = null;
    }
}
