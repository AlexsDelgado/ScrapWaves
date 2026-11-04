using System;
using UnityEngine;

/// <summary>
/// Guides newly affordable crafting independently of dialogue, and prioritizes the exit guide.
/// Each hint lasts until interaction or its configured timeout. Losing and regaining
/// crafting availability or changing the active station rearms the hint.
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
    private bool _doorHintShown;
    private float _hideAtTime = -1f;
    private Action _activeDismissUnsubscribe;
    private bool _dialogueDriven;
    private bool _craftingAvailableLastFrame;
    private bool _craftingGuidePending;
    private bool _craftingGuideActive;
    private float _pendingCraftingGuideDuration;

    private void Awake()
    {
        if (_guideArrow == null)
            _guideArrow = GetComponent<GuideArrow>() ?? FindAnyObjectByType<GuideArrow>();
        _craftingStation = CraftingStationRotation.GetActiveStation(gameObject.scene);
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
        CraftingStationRotation.ActiveStationChanged += HandleActiveStationChanged;
    }

    private void OnDisable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected -= HandleAllKeysCollected;
        DialogueDirector.EntryStarted -= HandleDialogueStarted;
        CraftingStationRotation.ActiveStationChanged -= HandleActiveStationChanged;

        EndGuide();
    }

    private void ResolveCraftingReferences()
    {
        if (_guideArrow == null)
            _guideArrow = GetComponent<GuideArrow>() ?? FindAnyObjectByType<GuideArrow>();
        CraftingStation station = CraftingStationRotation.GetActiveStation(gameObject.scene);
        if (_craftingStation != station)
        {
            if (_craftingGuideActive) EndGuide();
            _craftingStation = station;
            if (station != null) QueueCraftingGuide(_guideDurationSeconds);
        }
        if (_crafting == null)
            _crafting = FindAnyObjectByType<WeaponCraftingService>();
    }

    private bool ExitGuideActive => _guideArrow != null && _exitDoor != null
        && _guideArrow.Target == _exitDoor.transform;

    private void Update()
    {
        ResolveCraftingReferences();
        bool available = CanGuideCrafting();
        if (available && !_craftingAvailableLastFrame) QueueCraftingGuide(_guideDurationSeconds);
        if (!available) _craftingGuidePending = false;
        _craftingAvailableLastFrame = available;

        if (_craftingGuideActive && !available) EndGuide();
        if (_hideAtTime >= 0f && Time.unscaledTime >= _hideAtTime) EndGuide();

        if (_craftingGuidePending && available
            && (_dialogueDriven || RunSessionStats.ElapsedSeconds >= _craftingStationDelaySeconds)
            && Time.timeScale > 0f && !GameplayPause.IsUiPaused
            && (GameManager.Instance == null || GameManager.Instance.IsPlaying))
            ShowCraftingGuide(_pendingCraftingGuideDuration);
    }

    private void HandleActiveStationChanged(CraftingStation station)
    {
        if (station != null && station.gameObject.scene != gameObject.scene) return;
        ResolveCraftingReferences();
        // A sole station can break and reactivate in the same transition. Its identity
        // is unchanged, but the new availability still earns one fresh guide.
        if (station != null && station == _craftingStation)
            QueueCraftingGuide(_guideDurationSeconds);
    }

    private void QueueCraftingGuide(float duration)
    {
        _pendingCraftingGuideDuration = _craftingGuidePending
            ? Mathf.Max(_pendingCraftingGuideDuration, duration)
            : duration;
        _craftingGuidePending = true;
    }

    private void HandleDialogueStarted(DialogueEntry entry)
    {
        ResolveCraftingReferences();
        if (entry != null && entry.ShowsCraftingGuide && CanGuideCrafting())
        {
            QueueCraftingGuide(_dialogueGuideDurationSeconds);
            ShowCraftingGuide(_dialogueGuideDurationSeconds);
        }
    }

    private bool CanGuideCrafting() => _craftingStation != null && _craftingStation.IsAvailable
        && TutorialDirector.AllowsStation(_craftingStation)
        && _crafting != null && _crafting.HasAnyAvailableCraftingAction();

    private void ShowCraftingGuide(float duration)
    {
        ResolveCraftingReferences();
        if (!CanGuideCrafting() || _craftingStation == null || _guideArrow == null
            || _craftingStation.IsOpen || ExitGuideActive || Time.timeScale <= 0f
            || GameplayPause.IsUiPaused || (GameManager.Instance != null && !GameManager.Instance.IsPlaying)) return;
        _craftingGuidePending = false;
        _craftingAvailableLastFrame = true;
        CraftingStation station = _craftingStation;
        BeginGuide(
            station.transform,
            dismiss =>
            {
                void Handler() => dismiss();
                station.OnInteracted += Handler;
                return () => station.OnInteracted -= Handler;
            },
            duration);
        _craftingGuideActive = true;
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
        _craftingGuideActive = false;

        Action unsub = _activeDismissUnsubscribe;
        _activeDismissUnsubscribe = null;
        unsub?.Invoke();

        if (_guideArrow != null)
            _guideArrow.Hide();
    }
}
