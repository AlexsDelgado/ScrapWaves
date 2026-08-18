using System.Collections;
using System.Collections.Generic;
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

    private bool _isOpen;

    /// <summary>Se dispara cada vez que el jugador abre esta estación (para la flecha guía, por ejemplo).</summary>
    public event System.Action OnInteracted;

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
        if (_isOpen || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            return;

        // Otra estación (o el level-up choice) puede tener el CraftingUI compartido abierto:
        // no reabrir encima ni disparar una segunda corrutina sobre la misma UI.
        if ((_craftingUi != null && _craftingUi.IsVisible) || (_levelUpChoiceUi != null && _levelUpChoiceUi.IsVisible))
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null || !WasInteractPressed())
            return;

        Vector3 point = _interactionPoint != null ? _interactionPoint.position : transform.position;
        if (Vector3.Distance(player.position, point) > _interactionRadius)
            return;

        OpenCrafting();
    }

    public void OpenCrafting()
    {
        if (_craftingUi == null)
            return;

        _isOpen = true;
        OnInteracted?.Invoke();
        StartCoroutine(_craftingUi.PresentCoroutine(_craftingService, _inventory, () => _isOpen = false));
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
