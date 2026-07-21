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

    private bool _isOpen;

    private void Awake()
    {
        if (_craftingUi == null)
            _craftingUi = FindAnyObjectByType<CraftingUI>();
        if (_craftingService == null)
            _craftingService = FindAnyObjectByType<WeaponCraftingService>();
        if (_inventory == null)
            _inventory = FindAnyObjectByType<MaterialInventory>();
    }

    private void Update()
    {
        if (_isOpen || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
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
