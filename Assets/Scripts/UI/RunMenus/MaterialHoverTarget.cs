using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Pointer hover relay placed on each material row of the crafting readouts.</summary>
public sealed class MaterialHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public MaterialType Type;
    public MaterialSourceTooltip Tooltip;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Tooltip != null)
            Tooltip.Show(Type, transform as RectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (Tooltip != null)
            Tooltip.Hide();
    }

    private void OnDisable()
    {
        if (Tooltip != null)
            Tooltip.Hide();
    }
}
