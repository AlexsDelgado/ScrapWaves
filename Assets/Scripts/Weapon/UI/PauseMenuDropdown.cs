using TMPro;
using UnityEngine;

/// <summary>Keeps the authored dropdown template above its enclosing pause canvas.</summary>
public sealed class PauseMenuDropdown : TMP_Dropdown
{
    protected override GameObject CreateDropdownList(GameObject template)
    {
        GameObject dropdownList = base.CreateDropdownList(template);
        Canvas pauseCanvas = transform.GetComponentInParent<Canvas>();
        Canvas popupCanvas = dropdownList.GetComponent<Canvas>();
        if (pauseCanvas == null || popupCanvas == null)
            return dropdownList;

        popupCanvas.overrideSorting = true;
        popupCanvas.sortingLayerID = pauseCanvas.sortingLayerID;
        popupCanvas.sortingOrder = pauseCanvas.sortingOrder + 2;
        return dropdownList;
    }
}
