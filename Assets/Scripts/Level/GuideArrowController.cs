using System;
using UnityEngine;

/// <summary>
/// Guides newly affordable crafting independently of dialogue, and prioritizes the exit guide.
/// Each hint lasts until interaction or its configured timeout. Losing and regaining
/// crafting availability rearms the hint; continuous availability does not repeat it.
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

    [SerializeField, Min(0f), Tooltip("Duración de la flecha cuando la dispara un diálogo del jefe.")]
    private float _dialogueGuideDurationSeconds = 10f;

    [SerializeField] private WeaponCraftingService _crafting;
    private bool _craftingHintShown;
    private bool _doorHintShown;
    private float _hideAtTime = -1f;
    private Action _activeDismissUnsubscribe;
    private bool _dialogueDriven;
    private bool _craftingAvailableLastFrame;
    private bool _craftingGuidePending;

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
        _dialogueDriven = FindAnyObjectByType<DialogueDirector>() != null;
        if (_crafting == null) _crafting = FindAnyObjectByType<WeaponCraftingService>();
    }

    private void OnEnable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected += HandleAllKeysCollected;
        DialogueDirector.EntryStarted += HandleDialogueStarted;
    }

    private void OnDisable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected -= HandleAllKeysCollected;
        DialogueDirector.EntryStarted -= HandleDialogueStarted;

        EndGuide();
    }

    private void ResolveCraftingReferences()
    {
        if (_guideArrow == null)
            _guideArrow = GetComponent<GuideArrow>() ?? FindAnyObjectByType<GuideArrow>();
        if (_craftingStation == null)
            _craftingStation = FindAnyObjectByType<CraftingStation>();
        if (_crafting == null)
            _crafting = FindAnyObjectByType<WeaponCraftingService>();
    }

    private bool ExitGuideActive => _guideArrow != null && _exitDoor != null
        && _guideArrow.Target == _exitDoor.transform;

    private void Update()
    {
        ResolveCraftingReferences();
        bool available = CanGuideCrafting();
        if (available && !_craftingAvailableLastFrame) _craftingGuidePending = true;
        if (!available) _craftingGuidePending = false;
        _craftingAvailableLastFrame = available;

        if (_guideArrow != null && _craftingStation != null
            && _guideArrow.Target == _craftingStation.transform && !available) EndGuide();
        if (_hideAtTime >= 0f && Time.unscaledTime >= _hideAtTime) EndGuide();

        if (_craftingGuidePending && available
            && (_dialogueDriven || RunSessionStats.ElapsedSeconds >= _craftingStationDelaySeconds)
            && Time.timeScale > 0f && !GameplayPause.IsUiPaused
            && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
            ShowCraftingGuide(_guideDurationSeconds);
    }

    private void HandleDialogueStarted(DialogueEntry entry)
    {
        if (entry != null && entry.ShowsCraftingGuide && _craftingStation != null)
            ShowCraftingGuide(_dialogueGuideDurationSeconds);
    }

    private bool CanGuideCrafting() => _crafting != null && _crafting.HasAnyAvailableCraftingAction();

    private void ShowCraftingGuide(float duration)
    {
        ResolveCraftingReferences();
        if (!CanGuideCrafting() || _craftingStation == null || _guideArrow == null
            || _craftingStation.IsOpen || ExitGuideActive) return;
        _craftingHintShown = true;
        _craftingGuidePending = false;
        _craftingAvailableLastFrame = true;
        BeginGuide(
            _craftingStation.transform,
            dismiss =>
            {
                void Handler() => dismiss();
                _craftingStation.OnInteracted += Handler;
                return () => _craftingStation.OnInteracted -= Handler;
            },
            duration);
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
            },
            _guideDurationSeconds);
    }

    /// <summary>
    /// Arranca una aparición de la flecha. <paramref name="subscribeDismiss"/> se suscribe al evento
    /// de interacción del objetivo y devuelve un Action para desuscribirse.
    /// </summary>
    private void BeginGuide(Transform target, Func<Action, Action> subscribeDismiss, float duration)
    {
        if (_guideArrow == null || target == null)
            return;

        EndGuide();

        _guideArrow.Show(target);
        _hideAtTime = Time.unscaledTime + duration;

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
