using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class CraftingStation : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float _interactionRadius = 3f;
    [SerializeField] private Transform _interactionPoint;
    [SerializeField] private CraftingUI _craftingUi;
    [SerializeField] private WeaponCraftingService _craftingService;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private LevelUpChoiceUI _levelUpChoiceUi;

    [Header("Station rotation")]
    [SerializeField, Tooltip("Normal stations automatically join the rotation for their scene. Tutorial stations opt out.")]
    private bool _participatesInRotation = true;
    [SerializeField, Range(0f, 100f), Tooltip("Break chance on the first completed transaction after each activation.")]
    private float _initialBreakChancePercent;
    [SerializeField, Range(0f, 100f), Tooltip("Percentage points added after each transaction that does not break the station.")]
    private float _breakChanceIncrementPercent = 5f;
    [SerializeField] private GameObject _activeVisual;
    [SerializeField] private GameObject _brokenVisual;

    private bool _isOpen;
    private bool _isAvailable;
    private float _currentBreakChancePercent;

    /// <summary>Se dispara cada vez que el jugador abre esta estación (para la flecha guía, por ejemplo).</summary>
    public event System.Action OnInteracted;
    public event Action<CraftingStation> AvailabilityChanged;

    public Vector3 InteractionPosition => _interactionPoint != null ? _interactionPoint.position : transform.position;
    public float InteractionRadius => _interactionRadius;
    public bool IsOpen => _isOpen;
    public bool IsAvailable => isActiveAndEnabled && _isAvailable;
    public bool ParticipatesInRotation => _participatesInRotation;
    public float InitialBreakChancePercent => ClampPercent(_initialBreakChancePercent);
    public float BreakChanceIncrementPercent => ClampPercent(_breakChanceIncrementPercent);
    public float CurrentBreakChancePercent => _currentBreakChancePercent;

    private void Awake()
    {
        if (_craftingUi == null)
            _craftingUi = FindAnyObjectByType<CraftingUI>();
        if (_craftingService == null)
            _craftingService = FindAnyObjectByType<WeaponCraftingService>();
        if (_inventory == null)
            _inventory = FindAnyObjectByType<MaterialInventory>();
        if (_levelUpChoiceUi == null)
            _levelUpChoiceUi = FindAnyObjectByType<LevelUpChoiceUI>();
    }

    private void Update()
    {
        if (!IsAvailable || TutorialInput.WasConsumed || !TutorialDirector.AllowsStation(this) || GameplayPause.IsUiPaused || _isOpen || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            return;

        // Otra estación (o el level-up choice) puede tener el CraftingUI compartido abierto:
        // no reabrir encima ni disparar una segunda corrutina sobre la misma UI.
        if ((_craftingUi != null && _craftingUi.IsVisible) || (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible))
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null || !WasInteractPressed())
            return;

        if (Vector3.Distance(player.position, InteractionPosition) > _interactionRadius)
            return;

        OpenCrafting();
    }

    public void OpenCrafting()
    {
        if (!IsAvailable || _isOpen || _craftingUi == null || _craftingUi.IsVisible
            || (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible)
            || GameplayPause.IsUiPaused || TutorialInput.WasConsumed
            || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            return;

        if (!TutorialDirector.AllowsStation(this)) return;
        _isOpen = true;
        OnInteracted?.Invoke();
        CraftingStationRotation.NotifyInteracted(this);
        StartCoroutine(_craftingUi.PresentCoroutine(_craftingService, _inventory, () => _isOpen = false, this));
    }

    private void OnEnable()
    {
        if (_participatesInRotation)
            CraftingStationRotation.Register(this);
        else
            SetAvailability(true);
        ApplyVisuals();
    }

    private void Start()
    {
        if (_participatesInRotation)
            CraftingStationRotation.InitializeScene(gameObject.scene);
    }

    private void OnDisable()
    {
        _isOpen = false;
        SetAvailability(false);
        CraftingStationRotation.Unregister(this);
    }

    public void ConfigureForTutorial() => SetParticipation(false);

    public void SetParticipation(bool participates)
    {
        if (_participatesInRotation == participates)
        {
            if (!participates) SetAvailability(true);
            return;
        }
        _participatesInRotation = participates;
        CraftingStationRotation.Unregister(this);
        if (participates)
        {
            SetAvailability(false);
            if (isActiveAndEnabled) CraftingStationRotation.Register(this);
        }
        else
            SetAvailability(true);
    }

    /// <summary>Called exactly once by the crafting service after a paid transaction is fully resolved.</summary>
    public bool CompleteTransaction()
    {
        if (!IsAvailable || !_participatesInRotation)
            return false;
        return CraftingStationRotation.CompleteTransaction(this);
    }

    internal void SetAvailability(bool available)
    {
        bool changed = _isAvailable != available;
        _isAvailable = available;
        if (available) _currentBreakChancePercent = InitialBreakChancePercent;
        ApplyVisuals();
        if (changed) AvailabilityChanged?.Invoke(this);
    }

    internal void IncreaseBreakChance()
    {
        _currentBreakChancePercent = Mathf.Min(100f, _currentBreakChancePercent + BreakChanceIncrementPercent);
    }

    private void ApplyVisuals()
    {
        if (_activeVisual != null && _activeVisual != gameObject)
            _activeVisual.SetActive(_isAvailable);
        if (_brokenVisual != null && _brokenVisual != gameObject)
            _brokenVisual.SetActive(!_isAvailable);
    }

    private static float ClampPercent(float value) => float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, 100f);

    private void OnValidate()
    {
        _initialBreakChancePercent = InitialBreakChancePercent;
        _breakChanceIncrementPercent = BreakChanceIncrementPercent;
        _interactionRadius = Mathf.Max(0.5f, _interactionRadius);
    }

    private static bool WasInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }
}
