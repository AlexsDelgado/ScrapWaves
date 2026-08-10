using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Utilidad TEMPORAL de QA: con Ctrl + Numpad 9 otorga 999 de cada material
/// directo al <see cref="MaterialInventory"/> (sin pasar por pickups ni XP).
/// Pensado para un GameObject vacío en la escena de gameplay.
/// </summary>
[DisallowMultipleComponent]
public class DebugCrafting : MonoBehaviour
{
    [SerializeField, Tooltip("Vacío = MaterialInventory.Instance / FindAnyObjectByType.")]
    private MaterialInventory _inventory;

    [SerializeField, Min(1), Tooltip("Cantidad a la que se setea cada material al activar el cheat.")]
    private int _amountPerMaterial = 999;

    private void Awake() => ResolveInventory();

    private void Update()
    {
        if (!WasGrantHotkeyPressed())
            return;

        ResolveInventory();
        if (_inventory == null)
        {
            Debug.LogWarning("[DebugCrafting] No hay MaterialInventory en la escena.", this);
            return;
        }

        GrantAllMaterials(_amountPerMaterial);
    }

    private void GrantAllMaterials(int targetAmount)
    {
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            int current = _inventory.GetAmount(type);
            int delta = targetAmount - current;
            if (delta > 0)
                _inventory.Add(type, delta);
        }

        Debug.Log($"[DebugCrafting] Materiales seteados a {targetAmount} (Ctrl+Numpad9).", this);
    }

    private void ResolveInventory()
    {
        if (_inventory != null)
            return;

        _inventory = MaterialInventory.Instance != null
            ? MaterialInventory.Instance
            : FindAnyObjectByType<MaterialInventory>();
    }

    private static bool WasGrantHotkeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        return ctrl && keyboard.numpad9Key.wasPressedThisFrame;
    }
}
